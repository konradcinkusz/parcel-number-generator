# Roadmap

The plan for taking this repository from *published* to *finished*, and the handoff
artifact a future session reads first.

GitHub milestones carry *what* and *when*. They do not carry *why this order* or *what must
not break*, and a session that starts by reading the issue list alone will re-derive both,
usually differently. This document is the part the issue tracker cannot hold.

**Tracker: [#30](https://github.com/konradcinkusz/parcel-number-generator/issues/30)** —
hosts the running log and the decision log.

---

## 1. What "complete" means here

This repository is a **self-hostable warehouse parcel-number allocation system** — issues
tracking numbers from a finite pool without repeats or volume leakage, records the
notifications a warehouse raises against those parcels, and gives operators one console
over both — published as a working reference for
[`konradcinkusz/architecture-standards`](https://github.com/konradcinkusz/architecture-standards).

It is not a skeleton. Three services, 15 projects, 139 tests, five workflows, six
architecture documents and four ADRs already exist and work. What is missing is not
features. Mature means:

1. **Claims observed, not argued.** Every deployment, concurrency and security claim in the
   documentation is backed by something that ran. Today the deploy path has never executed,
   the no-duplicates guarantee is proven only against an in-memory provider, and the JWKS
   validation path has never validated a token.
2. **A public face a stranger can act on** — findable by search, with a rendered
   documentation entry point, and images they can actually pull.
3. **A complete gate set**, including the SAST job that going public just made free.
4. **A maintained dependency floor** — weekly Dependabot with someone landing the bumps.
5. **A deviation ledger that is empty, or every row of which is a deliberate acceptance.**

The fifth is the load-bearing one. [`DEVIATIONS.md`](docs/architecture/DEVIATIONS.md) is
this repository's own dated register of the principles it does not satisfy, with *why it is
open* and *what closes it* per row. **Closing that ledger is closing this roadmap.** Nine of
the thirteen issues trace to a numbered DEV row; the plan is largely that ledger turned
into issues rather than a second, competing account of what is unfinished.

## 2. Phases

Four phases, two weeks each. Cadence is **assumed, not inferred** — the commit history is
bursty (2017-12, 2018-02, 2026-08, 2026-09) rather than rhythmic, so the solo-maintainer
default was applied.

Phases are `phase-N` **labels**, not GitHub milestones. The reason is recorded in the
tracker's decision log: no milestone tool is reachable from the environment driving this
work. Nothing else about the plan depends on that, and the issues can be back-filled into
real milestones later without changing a word of their content.

| Phase | Due | Issues | Goal |
|---|---|---|---|
| **1 — Close the public-release loop** | 2026-09-15 | [#17](../../issues/17) [#18](../../issues/18) [#19](../../issues/19) [#20](../../issues/20) | Finish what going public unblocked: the SAST gate, a rendered docs page, an honest badge row, findable metadata |
| **2 — Dependency currency** | 2026-09-29 | [#21](../../issues/21) [#22](../../issues/22) [#23](../../issues/23) | Land the three Dependabot updates closed on 2026-09-01 to clear the slate |
| **3 — Prove the persistence guarantee** | 2026-10-13 | [#24](../../issues/24) [#25](../../issues/25) [#26](../../issues/26) | Turn *never the same number twice* from an argument into a measurement against a real engine |
| **4 — Deployment and identity** | 2026-10-27 | [#27](../../issues/27) [#28](../../issues/28) [#29](../../issues/29) | Make the deployment claims observed rather than argued. All owner-gated |

## 3. Why this order

**Phase 1 first because going public changed what is possible.** DEV-4 sat open for weeks
with its blocker recorded as *GitHub Advanced Security, or the repository going public*.
The second of those happened on 2026-09-01, so the largest `REPO-BASELINE` §1 gap became
free to close. Every later PR is reviewed by a gate that exists, which is worth having
before the phases that touch more code.

**Phase 2 before phase 3 because it is cheap and it moves the floor.** Three dependency
updates, one of which crosses seven major versions of the actions that build every image.
Doing that *before* phase 3 adds test infrastructure means the new tests are written
against current tooling rather than being rewritten by the bump.

**Phase 3 before phase 4 because a deployment should not be the first real database.**
DEV-1 and DEV-3 are both about things never having executed, and they are one step apart:
the migrations have never been applied to a real engine, and the deploy would apply them to
a production one. Proving them against Testcontainers first means the first production
migration is not also the first migration.

**Phase 4 last because none of it is a commit.** Every issue in it needs a decision or a
credential that no pull request can supply. Putting it last is not deferral — it is the
only position from which the preceding work is available to it.

## 4. Dependencies

Every `Blocked by` in the plan, in one place.

```
#17 ──▶ #19          badge row waits on a green CodeQL job
#24 ──▶ #25          concurrency proof needs the fixture
   └──▶ #26          notification migrations need the fixture
#27 ──▶ #28 ──▶ #29  issuer ─▶ first deploy ─▶ packages exist to publish
```

Everything else is parallel-safe.

Two of these are worth understanding rather than just obeying:

- **#17 → #19** is a guard against a specific past mistake. This repository once carried a
  CodeQL badge claiming a scan that could not run; both job and badge were deleted for it.
  Ordering the badge behind a green job makes that structurally impossible. If #17 ends up
  `blocked`, #19 ships the rest of the row with no CodeQL badge — a complete outcome, not a
  partial one.
- **#27 → #28** is enforced by the code. `flyio.yml` sets `Jwt__Authority` on deploy, and
  with it empty the startup guard refuses the host. That is the guard working. It is not to
  be worked around with `Security__AllowAnonymousAccess=true`.

## 5. Protected paths

Files whose breakage would compromise every later pull request. **A PR touching one of
these is never force-merged**, however many attempts it has cost — see §6.

| Path | Why |
|---|---|
| `.github/workflows/` | Every gate this repository claims to run |
| `Directory.Build.props` | Warnings-as-errors, TFM, analysis level, for all 15 projects |
| `Directory.Packages.props` | Central package management; every project resolves through it |
| `global.json` | SDK pin and the test runner selection |
| `ParcelNumberGenerator.slnx` | The solution every CI job builds |
| `src/ParcelNumberGenerator.Api/Dockerfile` | Image build + startup-guard smoke test |
| `src/ParcelNumberGenerator.Notifications/Dockerfile` | Image build + startup-guard smoke test |
| `src/ParcelNumberGenerator.Web/Dockerfile` | Image build + health smoke test |
| `scripts/check-kernel-size.sh` | Architecture guard, P2 |
| `scripts/check-runtime-image-major.sh` | Architecture guard, P6 |
| `.gitleaks.toml` | Secret-scanner configuration |
| `src/ParcelNumberGenerator.ServiceDefaults/` | Shared kernel; every service depends on it |
| `src/**/Migrations/` | The migration-drift gate compares against these |

The reasoning, which is the part worth keeping: **a broken content file is contained; a
broken validator turns every later PR into three-retries-and-force-merge, which is CI
ceasing to exist.**

Note that **8 of the 13 issues touch a protected path.** That is a property of a repository
whose remaining work is mostly infrastructure, and it means the force-merge escape hatch
will rarely be available. That is the correct outcome rather than an obstacle.

## 6. Execution policy

- **One issue = one PR.** Never batched.
- **CI must actually run** on the pushed branch before any merge. A diff is never assumed to
  pass because it looks like it would.
- **Retry cap: 3** fix attempts per PR. Fixed, not renegotiable mid-run.
- **After 3 failures**, branch on the diff:
  - *Touches no protected path* → force-merge, say so explicitly, and open a `Fix CI:`
    issue in the same phase with the last failure excerpt, labelled `tech-debt`.
  - *Touches a protected path* → do **not** merge. Leave the PR open, label PR and issue
    `blocked`, comment with the diagnosis and the three attempts, and move on.
- **Infrastructure failures** (auth, quota, outage, runner unavailable — no code path in the
  log) do not burn the retry cap. Re-run once; if it still fails for the same infra reason,
  force-merge and open **one** `Fix CI: pipeline` issue, not one per PR. The protected-path
  rule does not apply, because an infra failure says nothing about the code.
- **Never** skip, disable or quarantine a test to get green. Never push an empty commit or
  close-and-reopen to kick CI.
- Merge convention: squash, matching the repository's merged history.

## 7. Non-goals

- **No new product features.** Every issue closes a gap the repository already documented
  about itself.
- **No re-litigating accepted deviations.** DEV-5 (the console is static assets behind an
  ASP.NET BFF rather than the estate's Next.js norm) and DEV-6 (no characterisation tests
  before the notification service's original rewrite) are deliberate acceptances with
  recorded reasoning. They are decisions, not debt.
- **No SQL Server integration testing.** Support is real and both migration sets are
  maintained, but PostgreSQL is what `docker-compose.yml` and both Fly database apps run,
  and a fixture per provider doubles the slowest tests to cover a path nothing deploys.
- **No history rewrite.** Audited clean across all 16 commits and all 248 blobs
  ([`06-OPEN-SOURCE-READINESS.md`](docs/architecture/06-OPEN-SOURCE-READINESS.md) §2).
  Nothing to rotate, nothing to scrub.
- **No Node build pipeline**, for the docs page or anything else. DEV-5 already rejected one
  for the console; it applies with more force to a documentation page.

## 8. When this roadmap is done

When [`DEVIATIONS.md`](docs/architecture/DEVIATIONS.md) contains no *Open* rows — only the
*Accepted deliberately* table.

At that point DEV-1 through DEV-4 and DEV-7 have each been replaced by something that ran:
a deployment that served a request, migrations applied to a real database, a token
validated against a real issuer, a static-analysis job that reports, and three packages a
stranger can pull.
