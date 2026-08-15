# ADR-0004 — One repository for the PNG system

**Status:** Accepted
**Date:** 2026-08-15

## Context

The PNG warehousing system lived in two repositories: this one, holding the parcel number
generator, and `konradcinkusz/komunikaty`, holding the notification service. Both were
modernized against the same
[architecture standards](https://github.com/konradcinkusz/architecture-standards) on the
same day, by parallel efforts, and the split cost more than it bought:

- The two repos duplicated a shared kernel, a CI pipeline, a deviations ledger and a
  dependabot configuration — four copies of things the standards say to keep in one place,
  drifting from day one (different test stacks, different SDK pinning, one repo with a
  permanently red CodeQL job the other had already diagnosed and removed).
- The notification service's entire identity presumed the generator: its solution was named
  `PNG.slnx`, its ADRs forbade it from minting numbers because *the generator* mints them,
  and its parcel-number parser existed to normalize *the generator's* numbers. A satellite
  repo whose every document points at another repo is one repo, split.
- An assessment of both (recorded in the pull request that implements this ADR) put the
  system's irreplaceable value — the allocation domain, the risk it neutralizes, the
  system-of-record data — in the generator, and the better engineering hygiene in the
  notification repo. Value is expensive to move; hygiene is a day's work to copy.

## Decision

**The PNG system lives in this repository.** The notification service, its contracts, its
data layer, its migrations and its tests transferred here under `ParcelNumberGenerator.*`
names. `komunikaty` is deprecated: a banner points here, no further development happens
there, and its history — including the architecture documents describing the 2019 WinForms
announcement board it once was — stays there as the record.

The transfer harmonized four things, deliberately:

1. **One kernel.** The generator's `ServiceDefaults` is the kernel; the notification
   kernel's CORS, OpenAPI-with-JWT, standard rate limiting and validation extensions were
   grafted into it (671 lines against the 800 ceiling). The transferred kernel's unused
   `MigrationCompletionSignal` was dropped — nothing consumed it; restoring it is a
   SERVICE-API-PATTERNS §7 exercise for whoever first needs it.
2. **One security posture.** The notification service originally threw at startup without
   `Jwt__Authority`. It now uses the estate's posture: JWKS-only validation registers when
   an issuer is configured (P8), endpoints require authorization when authentication
   exists, and a startup guard refuses a half-configured *production* host — open
   production is possible only via the verbose `Security__AllowAnonymousAccess=true`.
   ADR-0002's core (validate, never mint; no symmetric branch) is unchanged.
3. **One answer on the number format.** The generator issues integers and defines no
   format ([03-TARGET-ARCHITECTURE §9](../architecture/03-TARGET-ARCHITECTURE.md)); the
   canonical `PNG-NNNNNNNN-C` form of ADR-0003 is *presentation* over that integer —
   zero-padded to eight digits, Luhn check digit appended — rendered by the console and by
   labels. ADR-0003's dialect table listed the generator as *sending* the canonical form;
   that was aspiration, not fact, and the ambiguity about who owns formatting is exactly
   the seam this consolidation closes. The eight-digit payload accommodates the default
   pool (1–9,999,999) with headroom; widening the pool past 99,999,999 would be a change
   to both, and wants its own ADR.
4. **One test stack.** Everything runs on xunit.v3 + Microsoft.Testing.Platform, the
   newer stack the notification repo had already proven, with the kernel-purity
   assertions from both repos merged into one suite.

## Consequences

- One clone, one command, one system: the AppHost brings up Postgres, both services and
  the operator console together. The cross-service contract (parcel numbers) is now
  exercised in one place instead of assumed across two.
- The notification service's git history does not travel. `git log` here starts at the
  transfer; archaeology goes to the deprecated repo. That is the price of not merging two
  unrelated histories, and it is paid knowingly.
- The komunikaty deviations DEV-1 (nothing deployed) and DEV-4 (no SAST on a private
  repo) transfer to this repository's ledger, which now speaks for the whole system.
- Should a service ever need to leave this repository — a second warehouse system wanting
  notifications, say — the Contracts project and the kernel are the extraction seams, and
  this ADR is the one to supersede.

## Alternatives considered

**Keep two repositories and fix the drift.** Rejected: every fix (shared kernel package,
shared workflow templates) adds machinery whose only job is compensating for a split that
serves no boundary — the services already share an owner, a lifecycle and a deploy target.
P3 requires a service boundary at the database, not at the repository.

**Move the generator into komunikaty instead.** Rejected: the system is named PNG, the
generator is its core, and komunikaty's name is a documented historical accident
(ADR-0001). Consolidating into the satellite would preserve the accident and deprecate
the identity.

**Merge git histories with a subtree.** Rejected: the transferred code was renamed
namespace-by-namespace in the move, so history would not follow file identity anyway, and
the deprecated repo remains readable at zero cost.
