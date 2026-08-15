# PNG — Parcel Number Generator

Issues parcel tracking numbers from a fixed pool, never the same number twice, honouring
sub-ranges withheld from allocation.

Nothing to do with the image format. **PNG = ParcelNumberGenerator.**

[![CI](https://github.com/konradcinkusz/PNG/actions/workflows/ci.yml/badge.svg)](https://github.com/konradcinkusz/PNG/actions/workflows/ci.yml)
[![Secret scan](https://github.com/konradcinkusz/PNG/actions/workflows/secret-scan.yml/badge.svg)](https://github.com/konradcinkusz/PNG/actions/workflows/secret-scan.yml)
[![CodeQL](https://github.com/konradcinkusz/PNG/actions/workflows/codeql.yml/badge.svg)](https://github.com/konradcinkusz/PNG/actions/workflows/codeql.yml)

---

## The problem

A carrier has a finite space of tracking numbers — say `1` to `9,999,999` — with blocks
reserved for other purposes. Every parcel needs one, no two parcels may share one, and the
numbers should not be handed out in an order that leaks the carrier's daily volume.

That is harder than it looks at the edges. Handing out numbers at random is cheap while the
pool is empty and unusable when it is nearly full. Checking whether a number is free and then
taking it is a race: two despatch terminals a millisecond apart both see it free, and two
parcels enter the network with the same number. And a pool with exclusions is easy to get
subtly wrong in a way nobody notices until a number outside the allocation reaches a label.

This service solves those three problems specifically. See
[`docs/architecture/03-TARGET-ARCHITECTURE.md`](docs/architecture/03-TARGET-ARCHITECTURE.md).

## Run it

```bash
git clone https://github.com/konradcinkusz/PNG && cd PNG
./scripts/setup.sh
dotnet run --project src/ParcelNumberGenerator.Api
```

No database, no credentials, no configuration. With no connection string the service falls
back to an in-memory database, so a fresh clone runs with reduced durability rather than not
at all.

```bash
curl -X POST 'http://localhost:5180/parcel-numbers?count=5'
curl http://localhost:5180/pool
```

With a real PostgreSQL, via the Aspire composition root — needs Docker:

```bash
dotnet run --project src/ParcelNumberGenerator.AppHost
```

That brings up Postgres, pgAdmin and the API together, wired, with the dashboard. Or without
the .NET SDK at all: `docker compose up --build`.

## API

| | | |
|---|---|---|
| `POST` | `/parcel-numbers?count=n` | Issue up to *n* numbers. Rate limited: 60/minute |
| `GET` | `/parcel-numbers/{number}` | Is it issued? Is it allocatable at all? |
| `GET` | `/pool` | Range, exclusions, capacity, used, remaining, density, strategy |
| `GET` | `/health` · `/alive` | Readiness · liveness |
| `GET` | `/openapi/v1.json` | OpenAPI document (Development only) |

```jsonc
// POST /parcel-numbers?count=3  →  201
{ "numbers": [4260013, 921697, 6377649], "requested": 3, "complete": true, "reason": null }
```

The status code carries what the caller should do next:

| | |
|---|---|
| **201** | At least one number was issued. A partial batch is still 201 with `complete: false` — those numbers are permanently claimed, and reporting them as an error would burn them |
| **409** | The pool is exhausted. Retrying will never help |
| **503** | Contention — free numbers exist, this request kept losing them. Retrying will help; `Retry-After` says when |
| **429** | Rate limited |

## Configuration

Everything comes from configuration; nothing is a literal in source. Environment transport
uses `__` as the separator, so `Pool:From` is `Pool__From`.

| Key | Default | |
|---|---|---|
| `Pool:From` / `Pool:To` | `1` / `9999999` | Inclusive at both ends |
| `Pool:Exclusions:0:From` / `:To` | none | Any number of entries. Overlapping and adjacent ones are merged |
| `Allocation:Strategy` | `adaptive` | `adaptive`, `random-probe`, `sequential-scan` |
| `Allocation:MaxBatchSize` | `1000` | Ceiling on `count` |
| `DATABASE_PROVIDER` | `PostgreSQL` | `SqlServer`, or in-memory when no connection string is set |
| `ConnectionStrings:parcelnumbersdb` | none | |
| `Jwt:Authority` | none | An issuer to validate bearer tokens against. Absent ⇒ endpoints are open |
| `Security:AllowAnonymousAccess` | `false` | Required to run an open deployment in Production |

An invalid value stops the host at startup with a message naming the key — an inverted range,
an unknown strategy, exclusions that leave nothing to allocate. It does not become a 500 on
the first allocation in production.

### Choosing a strategy

| | | |
|---|---|---|
| `random-probe` | Draw a random index, claim it, retry on collision | One round trip while the pool is sparse. Cannot finish a nearly-full pool |
| `sequential-scan` | Stream issued numbers in order, take the k-th free one | One pass. Always finishes; wasteful when the pool is mostly empty |
| `adaptive` | Probe, escalate to a scan on contention | The default. Free on the happy path, and it can drain a pool |

Adding a fourth is one class implementing `IAllocationStrategy` and one registration line.

## Security

This service **validates** tokens and holds no key material, so it cannot mint one. Validation
is against an identity provider's JWKS endpoint via OIDC discovery — key rotation needs no
deploy here.

Authentication registers only when `Jwt:Authority` is set, so a credential-free clone runs.
In **Production** the host refuses to start without both a connection string and an issuer,
because the fallbacks that make a clone convenient are silently wrong in production: an
in-memory database loses every issued number on restart and then reissues them, and an open
endpoint drains a finite pool for anyone who finds it. Neither would fail a health check.

Running an open production deployment is possible and deliberately verbose:
`Security__AllowAnonymousAccess=true`.

Secret scanning runs over full history on every push. Report a vulnerability by opening a
security advisory rather than an issue.

## Deploy

One Fly app per component, in [`flyio/`](flyio/). The database has no public listener at all —
it is reachable only over Fly's private network. The API scales to zero and health-checks
`/health` with a grace period covering a .NET cold start plus schema migration.

Images are published to GHCR on a `v*` tag, multi-arch, cross-compiled rather than emulated.

Schema is applied by `MigrateAsync` from provider-specific migrations, in a hosted service
that runs *after* Kestrel starts — so probes answer while migrations are in flight and a slow
migration is not read as a failed deploy. CI fails a pull request whose model has drifted from
its committed migrations.

## Repository

```
src/
  ParcelNumberGenerator.Domain/         Pool arithmetic and strategies. No package references
  ParcelNumberGenerator.Data/           EF Core store; the number is the primary key
  ParcelNumberGenerator.Migrations.*/   One migration set per provider
  ParcelNumberGenerator.ServiceDefaults/ Shared kernel — plumbing only
  ParcelNumberGenerator.Api/            Endpoints, wiring, startup guard, Dockerfile
  ParcelNumberGenerator.AppHost/        Aspire composition root (development)
tests/ParcelNumberGenerator.Tests/      72 tests
docs/architecture/                      What this is, and why it is shaped this way
flyio/                                  One fly.toml per app
```

## Documentation

This repository was modernized in August 2026 from a 2018 .NET Framework 4.5.2 solution — a
WinForms application over a console benchmark harness — against
[`konradcinkusz/architecture-standards`](https://github.com/konradcinkusz/architecture-standards).

| | |
|---|---|
| [`01-CURRENT-STATE.md`](docs/architecture/01-CURRENT-STATE.md) | What it was, including the ten defects the rewrite fixes |
| [`02-GAP-ANALYSIS.md`](docs/architecture/02-GAP-ANALYSIS.md) | Each gap, its failure scenario, and what closed it |
| [`03-TARGET-ARCHITECTURE.md`](docs/architecture/03-TARGET-ARCHITECTURE.md) | What it is now |
| [`04-MIGRATION-PLAN.md`](docs/architecture/04-MIGRATION-PLAN.md) | The phases, including the ones not yet done |
| [`05-DECISIONS.md`](docs/architecture/05-DECISIONS.md) | Nine decisions, with the alternatives rejected |
| [`DEVIATIONS.md`](docs/architecture/DEVIATIONS.md) | Three principles not yet satisfied, and why |

## Contributing

`./scripts/setup.sh`, then `dotnet test`. CI runs the tests, `dotnet format`, a migration
drift check, an image build with a startup-guard smoke test, gitleaks over full history, and
CodeQL. Warnings are errors, including NuGet advisories.

A schema change ships with a migration for both providers, generated with
`dotnet ef migrations add <Name> --project src/ParcelNumberGenerator.Migrations.<Provider>
--startup-project src/ParcelNumberGenerator.Api --context ParcelNumbersDbContext`.
