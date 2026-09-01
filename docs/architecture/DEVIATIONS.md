# Known open deviations

> §3a of the reference architecture requires each repository to keep its own register of
> principles it does not currently satisfy: what, which principle, since when, and why it is
> still open. A row is deleted when it is fixed. A row that is deliberately accepted says so
> with the reasoning — an acknowledged deviation is a decision; an unacknowledged one is
> drift.
>
> This ledger speaks for the whole system: the notification service's deviations moved here
> with the service ([ADR-0004](../decisions/0004-one-repository-for-the-png-system.md)).

## Open

| # | Deviation | Principle | Open since | Status |
|---|---|---|---|---|
| DEV-1 | Nothing is deployed — the tag-driven workflow can create the whole estate, but no `png-*` app exists and no request has reached a running instance | P7, P12 | 2026-08-15 | Blocked on repository secrets and one `v*` tag |
| DEV-2 | No identity provider configured; a deployment is open unless one is set | P5 | 2026-08-15 | Blocked on an owner decision |
| DEV-3 | No integration test against a real PostgreSQL or SQL Server | P13 | 2026-08-15 | Open |
| DEV-4 | No CodeQL/SAST job in CI | REPO-BASELINE §1 | 2026-08-15 | Blocked on GitHub Advanced Security or the repository going public — unblocked by step 1 of [`06-OPEN-SOURCE-READINESS.md`](06-OPEN-SOURCE-READINESS.md) §7 |
| DEV-7 | The repository is private, and the four steps that release it are not commits | OPEN-SOURCE-RELEASE | 2026-09-01 | Blocked on an owner decision. The audit that gates them is done and clean |

---

### DEV-1 — Nothing is deployed

**What.** `.github/workflows/flyio.yml` tests, builds all three images, and deploys in
dependency order — databases, services, console — with the *an-app-that-does-not-exist-is-
always-selected* rule, so a cold estate comes up from a single tag. It has never run: no
Fly app exists, no `FLY_API_TOKEN` is configured, and no migration has ever been applied to
a real database.

**Why it is open.** The four repository secrets ([`flyio/SECRETS.md`](../../flyio/SECRETS.md))
need values, and DEV-2 needs its decision first — the deploy sets `Jwt__Authority`, and
with it empty the startup guard refuses the host, which is the guard doing its job.

**What closes it.** Configure the secrets, push a `v*` tag, and replace this row's text
with what the first deploy actually measured. Until then the health-check grace period,
the `.flycast` wake path and the JWKS validation path are all unexercised.

**Interim risk.** Deployment claims in the documentation are argued, not observed. The
image builds and startup-guard smoke tests run on every pull request, which bounds the
"works on my machine" class but not the platform class.

### DEV-2 — No identity provider

**What.** `Jwt:Authority` is unset everywhere. Development is deliberately open (P8);
production refuses to start without an issuer or the verbose
`Security__AllowAnonymousAccess=true`, on both services, enforced by startup guards that CI
smoke-tests.

**Why it is open.** Which issuer the estate trusts is an owner decision.
`konradcinkusz/authservice` is the obvious candidate.

**What closes it.** Set `JWT_AUTHORITY` in the deploy environment. No code change; the
wiring is conditional and already in place. Closing it also unlocks the console's session
layer — see the accepted deviation on the frontend below.

**Interim risk.** Bounded by the guards: an open production deployment cannot happen by
omission, only by someone setting a flag whose name says what it does.

### DEV-3 — No integration test against a real relational provider

**What.** Persistence is tested against EF Core's in-memory provider. The generator's
whole concurrency design rests on a primary-key violation being raised by a real engine
and reported as a lost race; the in-memory provider raises it from its change tracker, so
the guarantee is covered by construction (the key is in both committed migration sets,
asserted by `SchemaTests`) rather than by execution.

**What closes it.** A Testcontainers-backed PostgreSQL fixture running the committed
migrations, with N concurrent `TryReserveAsync` calls for the same number asserting exactly
one winner — and, now that the notification service lives here too, the same fixture
exercising its migrations.

**Interim risk.** A duplicate-detection regression specific to a real provider would pass
CI.

### DEV-4 — No CodeQL/SAST job

**What.** There is no static analysis job. The repository is private, so code scanning
requires GitHub Advanced Security; the analysis runs and is then rejected at upload with
*"Code scanning is not enabled for this repository"*. This repository previously carried
exactly that job, permanently red on every push, plus a README badge claiming it passed —
the committed-but-never-executed pattern, with decoration. The job and badge were removed;
the reasoning lives as a comment in `.github/workflows/secret-scan.yml` where the job
would otherwise be.

**What closes it.** Enable GitHub Advanced Security, **or** make the repository public —
code scanning is free there, but OPEN-SOURCE-RELEASE.md's full-history secret audit is the
precondition, not a follow-up. **That audit is now done and clean**
([`06-OPEN-SOURCE-READINESS.md`](06-OPEN-SOURCE-READINESS.md) §2), so the precondition is
satisfied and this row is one settings change away from closable. Then restore the job from
the comment — and the badge only once it is green, because the badge that was removed
alongside it claimed a scan that could not run.

### DEV-7 — The repository is private

**What.** Publishing is four steps, none of which a commit can perform: flip visibility,
set the description and topics, restore CodeQL, and flip the three GHCR packages after the
first tag. They are enumerated in
[`06-OPEN-SOURCE-READINESS.md`](06-OPEN-SOURCE-READINESS.md) §7, in the order they have to
happen.

**Why it is open.** Whether this system is public is an owner decision, and it is the one
decision on this page that cannot be undone: a pushed public commit is public forever, and
re-privating the repository does not reach the clones and forks that already exist.

**What closes it.** Steps 1 and 2 depend on nothing and can be done today — the gate that
made them unsafe, a credential reachable somewhere in the 2018 history, was audited over
all 16 commits and all 248 blobs on 2026-09-01 and found clean, with nothing to rotate and
no history rewrite needed. Steps 3 and 4 wait on DEV-4 and DEV-1 respectively.

**Interim risk.** None from staying private. The cost is the other direction: DEV-4 cannot
close while the repository is private, so the system has no SAST gate for as long as this
row is open.

## Accepted deliberately

| # | Deviation | Principle | Since | Why it is accepted |
|---|---|---|---|---|
| DEV-5 | The console is static assets served by an ASP.NET BFF, not the estate's Next.js norm | FRONTEND-BFF | 2026-08-15 | The guide's *patterns* are kept — own-origin only, server-side proxy with the candidate ladder, runtime configuration, no tokens in browser JS. The framework is not: this repository is one .NET toolchain end to end, the console is a single operator page with no SSR or routing needs, and a Node build pipeline would be the largest dependency in the repo serving the smallest project. Sessions and edge verification (FRONTEND-BFF §3–4) become relevant when DEV-2 closes; whoever implements them decides then whether the console has grown enough to justify the estate's standard stack. Recorded rather than silent, so that decision is made once, deliberately |
| DEV-6 | No characterisation tests preceded the notification service's original rewrite | P13 | 2026-08-15 | Transferred verbatim from the komunikaty ledger: the legacy WinForms tree could not execute on the target platform, so the ten defects were catalogued from source and D1–D3 carry named regression tests instead. Historical — the reasoning lives in the deprecated repository's `04-MIGRATION-PLAN.md` §3 |
