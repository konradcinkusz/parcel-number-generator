# PNG — Parcel Number Generator

[![Ask me anything](https://flat.badgen.net/static/Ask%20me/anything?icon=github&color=black&scale=1.01)](https://github.com/konradcinkusz "Ask me anything")
[![GitHub license](https://flat.badgen.net/github/license/konradcinkusz/parcel-number-generator?icon=github&color=black&scale=1.01)](https://github.com/konradcinkusz/parcel-number-generator/blob/master/LICENSE "GitHub license")
[![Maintained](https://flat.badgen.net/static/Maintained/yes?icon=github&color=black&scale=1.01)](https://github.com/konradcinkusz/parcel-number-generator/commits/master "Maintained")
[![GitHub branches](https://flat.badgen.net/github/branches/konradcinkusz/parcel-number-generator?icon=github&color=black&scale=1.01)](https://github.com/konradcinkusz/parcel-number-generator/branches "GitHub branches")
[![GitHub commits](https://flat.badgen.net/github/commits/konradcinkusz/parcel-number-generator?icon=github&color=black&scale=1.01)](https://github.com/konradcinkusz/parcel-number-generator/commits/master "GitHub commits")
[![GitHub issues](https://flat.badgen.net/github/issues/konradcinkusz/parcel-number-generator?icon=github&color=black&scale=1.01)](https://github.com/konradcinkusz/parcel-number-generator/issues "GitHub issues")
[![GitHub pull requests](https://flat.badgen.net/github/prs/konradcinkusz/parcel-number-generator?icon=github&color=black&scale=1.01)](https://github.com/konradcinkusz/parcel-number-generator/pulls "GitHub pull requests")

[![CI](https://github.com/konradcinkusz/parcel-number-generator/actions/workflows/ci.yml/badge.svg)](https://github.com/konradcinkusz/parcel-number-generator/actions/workflows/ci.yml "CI")
[![CodeQL](https://github.com/konradcinkusz/parcel-number-generator/actions/workflows/codeql.yml/badge.svg)](https://github.com/konradcinkusz/parcel-number-generator/actions/workflows/codeql.yml "CodeQL")
[![Secret scan](https://github.com/konradcinkusz/parcel-number-generator/actions/workflows/secret-scan.yml/badge.svg)](https://github.com/konradcinkusz/parcel-number-generator/actions/workflows/secret-scan.yml "Secret scan")

The PNG warehousing system: issues parcel tracking numbers from a fixed pool — never the
same number twice, honouring sub-ranges withheld from allocation — records the
notifications a warehouse raises against those parcels, and gives operators one console
over both.

Nothing to do with the image format. **PNG = ParcelNumberGenerator.**

---

## The system

Three services, one repository, built to
[`konradcinkusz/architecture-standards`](https://github.com/konradcinkusz/architecture-standards):

| Service | What it does |
|---|---|
| **Generator API** | Allocates tracking numbers from a finite pool with exclusions, concurrency-safe, without leaking daily volume. The hard part of the system, and its system of record |
| **Notifications** | Records events raised against parcels and the warehouse — a damaged carton, a blocked bay, a shift announcement — and tracks which of them an operator still has to acknowledge. Normalizes every parcel-number dialect at the edge |
| **Console** | The operator UI and its BFF. The browser talks only to this origin; the BFF proxies to both services. Pool dashboard, batch allocation, dialect-tolerant lookup, the notification board |

The notification service transferred here from the deprecated
[`konradcinkusz/komunikaty`](https://github.com/konradcinkusz/komunikaty) repository —
[ADR-0004](docs/decisions/0004-one-repository-for-the-png-system.md) records why and what
was harmonized in the move.

## Run it

```bash
git clone https://github.com/konradcinkusz/parcel-number-generator && cd parcel-number-generator
./scripts/setup.sh
dotnet run --project src/ParcelNumberGenerator.AppHost
```

That brings up Postgres (both databases), the generator, the notification service and the
console together, wired, with the Aspire dashboard — needs Docker. The console is the
`web` resource's URL.

No Docker? Each service falls back to an in-memory database, so a fresh clone still runs,
with reduced durability rather than not at all:

```bash
dotnet run --project src/ParcelNumberGenerator.Api            # :5180
dotnet run --project src/ParcelNumberGenerator.Notifications  # :5181
dotnet run --project src/ParcelNumberGenerator.Web            # :5170 — open this one
```

No .NET SDK at all: `docker compose up --build`, console on <http://localhost:8090>.

```bash
curl -X POST 'http://localhost:5180/parcel-numbers?count=5'
curl 'http://localhost:5181/api/notifications'
```

## The APIs

### Generator (`:5180`, or `/api/generator/*` through the console's BFF)

| | | |
|---|---|---|
| `POST` | `/parcel-numbers?count=n` | Issue up to *n* numbers. Rate limited: 60/minute |
| `GET` | `/parcel-numbers/{number}` | Is it issued? Is it allocatable at all? |
| `GET` | `/pool` | Range, exclusions, capacity, used, remaining, density, strategy |
| `GET` | `/health` · `/alive` | Readiness · liveness |

```jsonc
// POST /parcel-numbers?count=3  →  201
{ "numbers": [4260013, 921697, 6377649], "requested": 3, "complete": true, "reason": null }
```

The status code carries what the caller should do next: **201** at least one number issued
(a partial batch is still 201 with `complete: false` — those numbers are permanently
claimed), **409** the pool is exhausted and retrying will never help, **503** contention —
retrying will help and `Retry-After` says when, **429** rate limited.

The generator issues **integers**. The canonical presentation form `PNG-NNNNNNNN-C` —
eight digits, Luhn check digit — is defined by
[ADR-0003](docs/decisions/0003-parcel-number-format.md) and rendered by the console and
labels; the notification service accepts it and every other dialect the estate speaks.

### Notifications (`:5181`, or `/api/notifications/*` through the BFF)

| | | |
|---|---|---|
| `GET` | `/api/notifications` | List. `page`, `limit`, `parcelNumber`, `outstandingOnly`, `severity` — clamped, never rejected |
| `GET` | `/api/notifications/{id}` | One |
| `POST` | `/api/notifications` | Raise. The parcel number arrives in any dialect and is stored canonically |
| `PUT` | `/api/notifications/{id}` | Amend |
| `POST` | `/api/notifications/{id}/acknowledgement` | Acknowledge — idempotent, first timestamp wins |
| `DELETE` | `/api/notifications/admin/{id}` | Delete. `Admin` or `WarehouseManager`, when authentication is configured |
| `GET` | `/health` · `/alive` | Readiness · liveness |

## Configuration

Everything comes from configuration; nothing is a literal in source. Environment transport
uses `__` as the separator. The full variable inventory, per service, with tiers and
troubleshooting keyed on literal error text: [`flyio/SECRETS.md`](flyio/SECRETS.md).

The generator's pool and strategy (`Pool__From/To`, `Pool__Exclusions__…`,
`Allocation__Strategy` — `adaptive`, `random-probe`, `sequential-scan`) validate at
startup: an invalid value stops the host with a message naming the key, not a 500 on the
first allocation in production. Adding a fourth strategy is one class implementing
`IAllocationStrategy` and one registration line.

## Security

Both services **validate** tokens against an identity provider's JWKS endpoint via OIDC
discovery and hold no key material — they cannot mint a token
([ADR-0002](docs/decisions/0002-token-validation-only.md)).

Authentication registers only when `Jwt__Authority` is set, so a credential-free clone
runs open in development. In **Production** each service's startup guard refuses to start
without both a connection string and an issuer, because the fallbacks that make a clone
convenient are silently wrong in production: an in-memory database loses its data on
restart, an open generator drains a finite pool, and an open notification board serves
operational detail about customers' shipments. Running an open production deployment is
possible and deliberately verbose: `Security__AllowAnonymousAccess=true`. CI runs the
built images against `ASPNETCORE_ENVIRONMENT=Production` to assert the guards still fire.

Secret scanning runs over full history on every push and weekly, plus a pre-commit hook
(`scripts/setup.sh` installs it). To report a vulnerability, open a
[security advisory](https://github.com/konradcinkusz/parcel-number-generator/security/advisories/new)
rather than an issue — [`SECURITY.md`](SECURITY.md) says what is in scope, and why a
credential-free clone answering without a token is not.

## Deploy

One Fly app per component, in [`flyio/`](flyio/): the console (the only public face), the
two services, and one database app per service — reasoned sizing and cost in
[`flyio/INFRASTRUCTURE-ANALYSIS.md`](flyio/INFRASTRUCTURE-ANALYSIS.md). A `v*` tag runs
the tests, publishes multi-arch images to GHCR, and deploys in dependency order with
change detection; an app that does not exist yet is always selected, so a cold estate
comes up from one tag. **Nothing is deployed yet** — that is DEV-1 in
[`DEVIATIONS.md`](docs/architecture/DEVIATIONS.md), with the steps to close it.

Schema is applied by `MigrateAsync` from provider-specific migrations, in a hosted service
that runs *after* Kestrel starts — so probes answer while migrations are in flight. CI
fails a pull request whose model has drifted from its committed migrations, for both
services, both providers.

## Repository

```
src/
  ParcelNumberGenerator.Domain/                    Pool arithmetic and strategies. No package references
  ParcelNumberGenerator.Data/                      EF Core store; the number is the primary key
  ParcelNumberGenerator.Migrations.*/              Generator migrations, one set per provider
  ParcelNumberGenerator.Api/                       Generator endpoints, wiring, startup guard, Dockerfile
  ParcelNumberGenerator.Contracts/                 The notification service's published shape. Zero dependencies
  ParcelNumberGenerator.Notifications.Data/        Notification entity and DbContext
  ParcelNumberGenerator.Notifications.Migrations.*/ Notification migrations, one set per provider
  ParcelNumberGenerator.Notifications/             Notification endpoints, domain, channels, guard, Dockerfile
  ParcelNumberGenerator.Web/                       Operator console + BFF proxy, Dockerfile
  ParcelNumberGenerator.ServiceDefaults/           Shared kernel — plumbing only, ceiling 800 lines
  ParcelNumberGenerator.AppHost/                   Aspire composition root (development)
tests/
  ParcelNumberGenerator.Tests/                     74 — generator domain, API surface, kernel purity
  ParcelNumberGenerator.Notifications.Tests/       65 — notification domain, service, query, mapping
docs/architecture/                                 What this is, and why it is shaped this way
docs/decisions/                                    ADRs, including the consolidation (0004)
flyio/                                             One fly.toml per app, secrets inventory, infra analysis
scripts/                                           Onboarding, migration generation, CI guards
```

## Documentation

[`docs/index.html`](docs/index.html) indexes everything below with one line each on the
question it answers — open it from a clone, or browse the table here.

This repository was modernized in August 2026 from a 2018 .NET Framework 4.5.2 solution
against [`konradcinkusz/architecture-standards`](https://github.com/konradcinkusz/architecture-standards),
then absorbed the notification service from `komunikaty` (itself modernized from a 2019
WinForms application).

| | |
|---|---|
| [`01-CURRENT-STATE.md`](docs/architecture/01-CURRENT-STATE.md) | What the generator was, including the ten defects the rewrite fixes |
| [`02-GAP-ANALYSIS.md`](docs/architecture/02-GAP-ANALYSIS.md) | Each gap against the standards, its failure scenario, and what closed it |
| [`03-TARGET-ARCHITECTURE.md`](docs/architecture/03-TARGET-ARCHITECTURE.md) | What the system is now — all three services |
| [`04-MIGRATION-PLAN.md`](docs/architecture/04-MIGRATION-PLAN.md) | The phases, including the ones not yet done |
| [`05-DECISIONS.md`](docs/architecture/05-DECISIONS.md) | The generator rewrite's decisions, with the alternatives rejected |
| [`DEVIATIONS.md`](docs/architecture/DEVIATIONS.md) | The whole system's open deviations, dated, with what closes each |
| [`06-OPEN-SOURCE-READINESS.md`](docs/architecture/06-OPEN-SOURCE-READINESS.md) | The full-history secret audit, and the four release steps no commit can perform |
| [`docs/decisions/`](docs/decisions/) | Four ADRs — repository identity, token posture, the parcel-number format, the consolidation |

## Roadmap

[`ROADMAP.md`](ROADMAP.md) is the plan from here: four phases, thirteen issues, tracked in
[#30](https://github.com/konradcinkusz/parcel-number-generator/issues/30). Nine of the
thirteen close a numbered row in [`DEVIATIONS.md`](docs/architecture/DEVIATIONS.md), which
is this repository's own register of what it does not yet satisfy — so the roadmap is
finished when that ledger's *Open* table is empty.

## Contributing

[`CONTRIBUTING.md`](CONTRIBUTING.md) has the house rules; the short version is
`./scripts/setup.sh`, then `dotnet test --solution ParcelNumberGenerator.slnx`. CI runs
the tests, `dotnet format`, migration drift checks for both services, the architecture
guards (kernel size ceiling, runtime-image majors), three image builds with startup-guard
smoke tests, gitleaks over full history, and a transitive vulnerability audit. Warnings
are errors, including NuGet advisories.

A schema change ships with a migration for both providers:

```bash
scripts/generate-migrations.sh parcelnumbers AddSomething
scripts/generate-migrations.sh notifications AddSomethingElse
```

## License

MIT — see [`LICENSE`](LICENSE).
