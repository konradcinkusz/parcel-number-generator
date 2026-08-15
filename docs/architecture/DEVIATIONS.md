# Known open deviations

> §3a of the reference architecture requires each repository to keep its own register of
> principles it does not currently satisfy: what, which principle, since when, and why it is
> still open. A row is deleted when it is fixed. A row that is deliberately accepted says so
> with the reasoning — an acknowledged deviation is a decision; an unacknowledged one is
> drift.

| # | Deviation | Principle | Open since | Status |
|---|---|---|---|---|
| 1 | No deploy job — CI builds and publishes an image, nothing deploys it | P12 | 2026-08-15 | Blocked on the first deployment |
| 2 | No identity provider configured; a deployment is open unless one is set | P5 | 2026-08-15 | Blocked on an owner decision |
| 3 | No integration test against a real PostgreSQL or SQL Server | P13 | 2026-08-15 | Open |

---

### 1 — No deploy job

**What.** `.github/workflows/publish-image.yml` builds and pushes a multi-arch image to GHCR
on a `v*` tag. Nothing deploys it. P12 asks for change detection and an ordered deploy —
data, then the service.

**Why it is open.** The apps do not exist yet, and neither does a `FLY_API_TOKEN`. Writing a
deploy job against apps that have never been created produces a workflow that has never run,
which is the "committed but never executed" failure the standards call out elsewhere.

**What closes it.** Phase 6 of [`04-MIGRATION-PLAN.md`](04-MIGRATION-PLAN.md). The deploy job
should carry the rule most often missing: *a service whose Fly app does not exist is always
selected*, whatever the diff says — that is what lets a cold estate come up from one tag. With
two apps, ordering is trivial (database, then API), and change detection is not yet worth its
complexity.

**Interim risk.** Deployment is manual, so it is undocumented in the sense that matters:
nothing verifies the runbook. Mitigated by `flyio/*.fly.toml` being complete and by the image
being built and smoke-tested on every pull request.

---

### 2 — No identity provider, so a deployment is open unless one is configured

**What.** `Jwt:Authority` is unset. With no issuer, the endpoints accept unauthenticated
requests. The checklist item "exactly one service holds a signing key; all others validate
against its JWKS endpoint" is satisfied in *shape* — this service holds no key material and
can only verify — but there is nothing to verify against.

**Why it is open.** Which issuer this service should trust is an owner decision, not a
technical one. `konradcinkusz/authservice` is the obvious candidate.

**What closes it.** Set `Jwt__Authority` on the Fly app. No code change; the wiring is in
place and conditional (P8).

**Interim risk.** Bounded, deliberately. The startup guard refuses to start a Production host
without either an issuer or an explicit `Security:AllowAnonymousAccess=true`, so an open
production deployment cannot happen by omission — only by someone setting a verbosely named
flag. CI asserts the guard still fires by running the built image. Development is
unauthenticated and stays that way.

---

### 3 — No integration test against a real relational provider

**What.** Persistence is tested against EF Core's in-memory provider. P13 asks for
"integration: xUnit + InMemory **or** a real container", so this is within the letter of the
principle — but the in-memory provider is not a database, and the one behaviour that matters
most here is exactly where it differs.

**Why it matters more than usual.** The whole concurrency design rests on a primary-key
violation being raised and reported as a lost race. The in-memory provider raises it from its
change tracker and its table writer, not from a database engine, and it has no real
transaction semantics. So the code path is covered, and the *guarantee* is only covered by
construction: the key is in the migration, and a relational provider enforces it.

**What closes it.** A Testcontainers-backed PostgreSQL fixture running the committed
migration, with a test that fires N concurrent `TryReserveAsync` calls for the same number
across N contexts and asserts exactly one wins. That is the assertion the in-memory provider
cannot make.

**Interim risk.** A duplicate-detection regression specific to a real provider would pass CI.
Partly mitigated by `SchemaTests`, which asserts the primary key is in the committed DDL
for both providers.
