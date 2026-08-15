# 02 — Gap analysis

> Every principle in the reference architecture, the gap as it stood at commit `c1258ed`, and
> where it is now. Ordered by the severity of the gap, not by principle number.

Severity is what happens if the gap is left open, not how far it is from the ideal:

| | |
|---|---|
| **Critical** | Produces wrong data, or exposes credentials |
| **High** | Blocks deployment or operation entirely |
| **Medium** | Makes change expensive or risky |
| **Low** | Hygiene |

---

## Critical

### G-1 — Nothing stopped the same number being issued twice · P3, P4

The service has one job. Its schema was `create table USED_NUMBERS (usedNumber INT)`: no
primary key, no unique index, no constraint. Allocation was a `SELECT`-then-`INSERT` pair
with no transaction between them.

**Failure scenario.** Two despatch terminals request a number within the same few
milliseconds. Both draw 4,260,013. Both search the used table, both miss, both insert. Two
parcels enter the carrier's network with the same tracking number; the second one to be
scanned overwrites the first one's status, and the first parcel becomes untrackable. Nothing
in the application or the database logs an error, because nothing was violated.

**Closed by.** The number is the primary key of `used_numbers`, and allocation is
insert-and-catch rather than check-then-insert: both callers reach the insert, the database
rejects one, and the loser is told it lost and draws again.
`EfUsedNumberStoreTests.Two_separate_contexts_cannot_both_claim_one_number` asserts it.

### G-2 — The pool arithmetic was wrong at both ends · (no principle; a defect)

D1, D2 and D3 of [`01-CURRENT-STATE.md`](01-CURRENT-STATE.md): an inclusive-range count that
was one too high, a draw that ignored the pool's lower bound, and an exclusion filter that
compared row positions against numbers.

**Failure scenario.** A deployment configured for the range `[500, 600]` issues numbers
between 1 and 100 — every one of them outside its own allocation, and every one of them
potentially another site's. Separately, a pool with one number left reports itself full and
the service stops issuing while 1 in 10,000,000 numbers is still free.

**Closed by.** `NumberPool` normalizes the range and its exclusions once, into disjoint
segments with a prefix sum, and exposes `NumberAt`/`IndexOf` as inverses. `NumberPoolTests`
covers both range ends, single-number ranges, adjacent and overlapping exclusions, exclusions
clipped at the pool boundary, a fully excluded pool, and the `int.MaxValue` edge.

---

## High

### G-3 — Not deployable anywhere the architecture deploys · P6, P7, P12

.NET Framework 4.5.2 runs on Windows only and cannot be containerized in any image the estate
uses. No Dockerfile, no `fly.toml`, no workflow. The delivery mechanism was a developer with
Visual Studio and a copy of `CreateTable.sql`.

**Closed by.** `net10.0`, a multi-stage Dockerfile on `:8080` running as the base image's
non-root `app` user, one `fly.toml` per app with the database holding no public listener, and
a tag-driven publish to GHCR. CI builds the image on every PR and asserts the startup guard
still fires, so the Dockerfile cannot rot between releases.

### G-4 — Not operable: no telemetry, no health, no shared kernel · P2, P15, P2a

No logging, no metrics, no traces, no health endpoint, no shared plumbing of any kind. The
only observability was a `Stopwatch` printing elapsed time to a console.

**Closed by.** `ParcelNumberGenerator.ServiceDefaults` — OTLP traces, metrics and logs with
health probes filtered out of traces; `/health` and `/alive`; service discovery; the standard
resilience handler on every `HttpClient`; the database provider switch. 414 lines, against
P2's ~800-line ceiling, and `SharedKernelTests` asserts the ceiling, that the kernel
references neither the domain nor the data assembly, and that it exports nothing inheritable.

### G-5 — Schema was created by hand and never migrated · P4

Two mutually inconsistent stories: a `CreateTable.sql` run by hand, and an unreferenced EF6
migration set describing a different table. Neither was applied by the application.

**Failure scenario.** A column is added to the entity. Development works, because nothing
checks. The deploy succeeds, because nothing applies schema. The first query against the new
column fails at runtime, in production, on a database whose actual shape nobody can state
without connecting to it.

**Closed by.** Provider-specific migration assemblies for PostgreSQL and SQL Server, applied
by `MigrateAsync` in a hosted service that runs after Kestrel starts — so probes answer while
schema work is in flight. `EnsureCreatedAsync` is reachable only on the in-memory path. A CI
job runs `dotnet ef migrations has-pending-model-changes` for both providers, so a model
change without a migration fails the pull request rather than the deploy.

---

## Medium

### G-6 — Six implementations sharing behaviour by inheritance · P10, P9

`NumberPoolDBv2` held the algorithm *and* its own ADO.NET plumbing, and each variant
subclassed it and overrode a `protected virtual` hook. The database access could not be
substituted at all, which is why there was no test — and a change to the search touched every
subclass.

**Closed by.** `IAllocationStrategy`: three implementations, three registration lines, no base
class. Persistence sits behind `IUsedNumberStore`, so a strategy is tested against an
in-memory fake with a scripted random source. The variants that were benchmark entries are
gone; the two that describe genuinely different behaviour survive as strategies, and
`adaptive` composes them.

### G-7 — Configuration hardcoded in five files · P5

Connection string, table name, column name and pool range as literals, disagreeing with each
other. No credential was present, so nothing needed rotating — but nothing would have stopped
one being pasted in.

**Closed by.** `Pool` and `Allocation` configuration sections bound to options and validated
at startup (`ValidateOnStart`), so an inverted range or an unknown strategy name stops the
host with a message that lists the valid options — rather than a 500 on the first allocation
in production. Environment variables with `__`, platform secrets in cloud, `dotnet
user-secrets` locally. A gitleaks job over full history on every push, and the build fails on
a NuGet advisory.

### G-8 — No tests, and the code could not have had any · P13

Zero test projects. Not an oversight so much as a consequence: with the SQL embedded in the
generator classes, there was nothing to substitute.

**Closed by.** 72 tests. Pool arithmetic and strategies as unit tests with no I/O;
persistence against the in-memory provider including the duplicate-claim path; the API
end-to-end through `WebApplicationFactory` over the real `Program.cs`; the startup guard; and
the shared-kernel boundary. The characterisation-before-migration rule in P13 was followed in
the sense that mattered — the defects in §4 of the current-state document were each written
down as an assertion before the replacement was written.

### G-9 — No composition root · P1

Running the system meant: install SQL Server Express, run a SQL script by hand, open the
solution in Visual Studio, set the startup project, paste a connection string into a textbox.

**Closed by.** `ParcelNumberGenerator.AppHost` declares Postgres, pgAdmin and the API with
`WithReference`, `WaitFor` and `WithHttpHealthCheck`, so `dotnet run` on that project brings
up the system. And, because every dependency is optional (P8), `dotnet run` on the API alone
works with nothing installed at all — the in-memory fallback keeps a fresh clone runnable.

---

## Low

### G-10 — Documentation described a class list · P14

33 lines of Polish naming five classes, one of which was marked obsolete-as-an-error. It said
nothing about what the service does, what a pool is, or how anything is deployed.

**Closed by.** A README in English covering what it is, how to run it, the API, configuration
and the deployment shape; this five-document set; and decision records. P14's language rule —
English for anything needed to build or deploy — is followed.

### G-11 — No repository baseline · REPO-BASELINE

No `CODEOWNERS`, no dependency automation, no `.editorconfig`, no central package management,
no templates, no `.dockerignore`, no `.gitattributes`, no secret scanning, no SAST.

**Closed by.** All of the above, plus `Directory.Build.props`/`Directory.Packages.props`,
CodeQL weekly and per-PR, a `dotnet format` gate, and `scripts/setup.sh`.

### G-12 — No authentication · P5

There was no network surface to authenticate, so this is a gap the modernization creates
rather than one it inherits: allocation permanently consumes a finite resource, and it is now
reachable over HTTP.

**Closed by.** Bearer validation against an external issuer's JWKS — this service holds no key
material and cannot mint a token. Registration is conditional on `Jwt:Authority` (P8), so a
credential-free clone still runs; the startup guard makes an unauthenticated *production*
deployment something you have to ask for by name. See
[`DEVIATIONS.md`](DEVIATIONS.md) for what is still open.

---

## Summary

| Principle | Before | After |
|---|---|---|
| P1 AppHost | ✗ | ✓ |
| P2 Shared kernel | ✗ | ✓ 414 lines, asserted |
| P3 Service per context, own database | ✗ | ✓ |
| P4 Migrated, not ensured | ✗ | ✓ both providers, CI-checked |
| P5 Config from environment, one signer | ✗ | ◐ verify-only; see DEVIATIONS |
| P6 Multi-stage container | ✗ | ✓ |
| P7 Fly.io topology | ✗ | ✓ written, not yet deployed |
| P8 Optional dependencies degrade | ✗ | ✓ |
| P9 Program.cs as manifest | ✗ | ✓ 50 lines |
| P10 Interface + registration | ✗ | ✓ |
| P11 Anti-corruption at the edge | n/a | n/a — no external dialect |
| P12 Tag-driven CI/CD | ✗ | ◐ build and publish; no deploy job |
| P13 Test at the logic-bearing layer | ✗ | ✓ 72 tests |
| P14 Documentation with reasoning | ✗ | ✓ |
| P15 Observability at build time | ✗ | ✓ |

Checklist score: **0/17 → 14/17**, with the three remaining items and their reasons in
[`DEVIATIONS.md`](DEVIATIONS.md).
