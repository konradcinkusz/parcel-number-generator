# 04 — Migration plan

> The plan this modernization followed, and what is left. Phases 1–5 are done; the rest are
> written down because a plan that stops at "it compiles" is not a plan for a service that
> has to run somewhere.

## 0. Why there was no strangler fig

MODERNIZE normally means moving incrementally behind a facade, with a running system to
verify against at every step. That was not available here, and saying so plainly matters more
than following the shape of the mode:

- The old and the new cannot run in one process — .NET Framework 4.5.2 and .NET 10 do not
  share a runtime, so there was no host that could route between them.
- There was nothing to route. The system's only interface was a WinForms form; there was no
  network boundary to put a facade at.
- Nothing is deployed. Nobody depends on the old behaviour, so there is no compatibility to
  preserve and no cutover to sequence.
- The behaviour being preserved is one sentence — *issue a number from the pool, never the
  same one twice* — and it is the part the old implementation got wrong.

So the plan is a rewrite of a small system, with the old behaviour's **defects** captured as
assertions first. The characterisation-test discipline P13 asks for was applied to what the
code was *supposed* to do; pinning what it actually did would have pinned the bugs.

## Phase 1 — Establish the truth about the old system ✅

Read every source file, record what the algorithm does and where it is wrong, in
[`01-CURRENT-STATE.md`](01-CURRENT-STATE.md) §3–§5. Ten defects, each with the file it lives
in. Confirm the history carries no credential — it does not; the connection strings use
integrated authentication.

**Exit criterion:** every behaviour worth keeping is written down, and every defect worth not
keeping is too.

## Phase 2 — Repository baseline ✅

`.editorconfig`, `.gitattributes`, `.dockerignore`, `.gitleaks.toml`, `CODEOWNERS`,
Dependabot, PR and issue templates, `Directory.Build.props`, `Directory.Packages.props`.

Before any code, deliberately: these are the files that stop the second commit undoing the
first one's conventions.

**Exit criterion:** `dotnet format --verify-no-changes` is meaningful, and a secret cannot be
committed without a job failing.

## Phase 3 — Domain, with no I/O ✅

`NumberRange`, `NumberPool` and the strategies, in a project with no package reference.
Tests written against each defect from Phase 1: the inclusive-count off-by-one, the draw that
ignored the lower bound, the exclusion filter that compared row indices with numbers, plus
the boundaries the old code never considered — a single-number range, a fully excluded pool,
`int.MaxValue`.

**Exit criterion:** the pool arithmetic is provably right without a database in the loop.

## Phase 4 — Persistence, kernel, API ✅

`IUsedNumberStore` over EF Core with the number as the primary key; the shared kernel;
`Program.cs` as a manifest; provider-specific migrations; the startup guard.

**Exit criterion:** two concurrent claims on one number produce one winner and one loser, and
it is asserted by a test rather than argued for in a comment.

## Phase 5 — Container, composition root, CI ✅

Multi-stage Dockerfile, AppHost, `fly.toml` per app, and workflows for build/test/format,
migration drift, image build, secret scanning and CodeQL.

**Exit criterion:** CI exercises everything the repository claims — including the Dockerfile,
which is otherwise only built at release time and rots unnoticed.

---

## Phase 6 — First deployment ⬜

Not done, and not doable from a session with no Fly credentials.

1. `fly apps create png-parcelnumbers-db` and `png-parcelnumbers-api`.
2. Create the volume, deploy Postgres from `flyio/postgres.fly.toml`, confirm it has no
   public listener (`fly ips list` should show none).
3. `fly secrets set ConnectionStrings__parcelnumbersdb=...` on the API app.
4. Deploy the API. Watch the migration hosted service apply `InitialCreate` *after* the
   health check starts answering — if `/health` fails during migration, the grace period is
   wrong.
5. Allocate one number. Restart the machine. Allocate again and confirm the first number is
   still recorded — the check that the in-memory fallback is not silently active.

**Known trap.** Fly's managed `postgres-flex` does not come up from a plain `flyctl deploy`;
`flyio/postgres.fly.toml` uses the stock `postgres:17-alpine` image for that reason.

## Phase 7 — Decide the pool, and the identity provider ⬜

Two questions this repository cannot answer for itself, and both should be answered before
anything issues a number that reaches a parcel:

- **What is the real pool?** `1..9,999,999` is inherited from the old default, not chosen. A
  carrier's number space usually has structure — a prefix, a check digit, reserved blocks —
  and the exclusions are the mechanism for expressing the reserved parts.
- **Which issuer?** `konradcinkusz/authservice` is the estate's identity service. Pointing
  `Jwt:Authority` at it closes G-12 fully. Until then the deployment is either open, which
  has to be asked for by name, or it does not start.

## Phase 8 — Close the remaining deviations ⬜

The three open rows in [`DEVIATIONS.md`](DEVIATIONS.md): the deploy job, the authenticated
production deployment, and the absence of a real-provider integration test. Each has an owner
question rather than a technical blocker.

## Phase 9 — Load-test the density curve ⬜

The `adaptive` strategy's escalation point is reasoned about and unit-tested, but its
*cost* at 90–99.9% density has not been measured against a real Postgres. The measurement
that matters: allocations per second, and round trips per allocation, at 50%, 90%, 99% and
99.9% full. If the scan turns out to dominate earlier than expected, the fix is a third
strategy — one class and one line — not a redesign.
