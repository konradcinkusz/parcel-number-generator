# Infrastructure analysis

Topology, sizing and what it costs, reasoned per app rather than assumed. P7 asks for
this in the repository so the cost of a shape is arguable rather than discovered on an
invoice.

**Status: not yet deployed (DEV-1).** The `fly.toml` files describe the intended topology
and the workflow can create it, but no `png-*` app exists yet. Nothing below is a
measurement; the numbers are list prices and the sizing is an argument, not an
observation. When the first deploy happens, this file gets the actual figures and this
paragraph is deleted.

## Topology

```
                         Internet
                            │  https, force_https
                            ▼
             ┌──────────────────────────────┐
             │  png-web                     │   the only public face
             │  console + BFF proxy         │   scale-to-zero
             └───────┬──────────────┬───────┘
                     │ .flycast (wakes machines)
           ┌─────────▼─────┐  ┌─────▼──────────────┐
           │ png-parcel-   │  │ png-notifications  │   no public traffic needed;
           │ numbers-api   │  │                    │   scale-to-zero
           └─────────┬─────┘  └─────┬──────────────┘
                     │ 6PN .internal, no public port
           ┌─────────▼─────┐  ┌─────▼──────────────┐
           │ png-parcel-   │  │ png-notifications- │   NO [http_service] at all
           │ numbers-db    │  │ db                 │   volume-backed, always on
           └───────────────┘  └────────────────────┘
```

Five apps. One database per service (P3): physical co-location of both databases on one
Postgres instance was considered and would be an acceptable *cost* decision, but two
stock `postgres:17-alpine` apps deployed by one workflow are operationally simpler than
one app whose init has to create a second database, and the delta is ~$2.45/month.

## Sizing, and why

| App | Size | Reasoning |
|---|---|---|
| `png-web` | shared-cpu-1x, 512 MB | Serves static files and streams proxied responses. 512 MB is the smallest size a .NET container starts reliably in; 256 MB gets killed during JIT on a cold boot |
| `png-parcelnumbers-api` | shared-cpu-1x, 512 MB | Allocation is one indexed insert per number; the strategies are arithmetic, not memory |
| `png-notifications` | shared-cpu-1x, 512 MB | One row written per parcel event, one indexed page read per operator refresh |
| `png-parcelnumbers-db` | shared-cpu-1x, 512 MB, 3 GB volume | The whole pool issued is ~10 M small rows; 3 GB is years of headroom, and a volume extends in place but never shrinks |
| `png-notifications-db` | shared-cpu-1x, 512 MB, 3 GB volume | At ~30k parcel events/day and ~200 bytes a row with indexes, ~18 months before it is worth thinking about |

## Scale-to-zero, deliberately

`min_machines_running = 0` on all three service apps. The rule from P7 is mechanical: for
every in-request call A→B, either B pins a machine or A's timeout exceeds B's cold start.

The one in-request chain here is browser → web → service. It is covered the second way,
explicitly: the BFF's upstream timeout is 75 seconds against a .NET cold start of a few
seconds plus a machine wake, and its base URLs use `.flycast` — through Fly's proxy, which
is what starts a stopped machine — rather than `.internal`, which would connect to nothing.
A timeout surfaces to the operator as a 504 with "retrying is reasonable", which a human
absorbs. The day an unattended system calls these services in-request, its service's
`min_machines_running` becomes 1.

The databases are not scaled to zero: a database that stops has to be started by
something, and the only thing that would start it is the connection that is already
timing out.

## Cost

List prices, `fra`, as an argument rather than a bill:

| Item | Shape | Monthly |
|---|---|---|
| `png-web` | shared-cpu-1x/512mb, scale-to-zero | ≈ $0 idle; cents per active hour |
| `png-parcelnumbers-api` | shared-cpu-1x/512mb, scale-to-zero | ≈ $0 idle |
| `png-notifications` | shared-cpu-1x/512mb, scale-to-zero | ≈ $0 idle |
| `png-parcelnumbers-db` | shared-cpu-1x/512mb, always on | ≈ $2 |
| `png-notifications-db` | shared-cpu-1x/512mb, always on | ≈ $2 |
| two 3 GB volumes | | ≈ $0.90 |
| **Total, idle estate** | | **≈ $4.90** |

## What this topology deliberately does not have

- **No read replica.** One writer per database, one region, workloads that are not
  read-bound.
- **No connection pooler.** One small consumer per database; PgBouncer would cost more
  than it saves.
- **No separate migration app.** Migrations run in-process after Kestrel starts (P4).
- **No event bus.** The trigger to introduce one is a use case that needs durable
  cross-service delivery, and neither service has one yet: a failed webhook delivery is
  reported and the notification stays readable over the API.
- **No public listener on the services.** Only `png-web` faces the internet. The services
  keep `[http_service]` for `.flycast` reachability and health checks, but nothing points
  a human at them; when DEV-2 closes, their bearer-token requirement closes the gap
  properly.
