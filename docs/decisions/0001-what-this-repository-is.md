# ADR-0001 — What this repository is

**Status:** Superseded by [ADR-0004](0004-one-repository-for-the-png-system.md) — the repository it describes is deprecated
**Date:** 2026-08-15

> **Provenance:** written in the `komunikaty` repository, transferred here with the
> notification service by [ADR-0004](0004-one-repository-for-the-png-system.md).

## Context

This repository is named `komunikaty` — Polish for "announcements". Between 2019 and 2026
it held a Windows Forms application for broadcasting school announcements: six .NET
Framework projects, no tests, no CI, no deployment, last touched in July 2019.

It is now the notification service of **PNG — Parcel Number Generator**, a warehousing
system. A reader arriving at the repository name, the git history, or the older commits
will find none of that obvious, and the gap between the name and the contents is exactly
the kind of thing that costs an hour of someone's afternoon.

## Decision

**This repository holds the PNG notification service, and nothing else.**

Concretely:

- **In scope.** Notifications raised against parcels and against the warehouse generally:
  raising, listing, filtering, acknowledging, delivering to a channel. The severity /
  acknowledgement / pinning mechanics inherited from the legacy application, re-framed
  around parcel events.
- **Out of scope — parcel number generation.** Despite the system's name, this service
  *validates and normalizes* parcel numbers; it does not mint them. That is the generator
  service's bounded context. See [ADR-0003](0003-parcel-number-format.md).
- **Out of scope — parcels themselves.** This service stores a parcel number as a string
  and knows nothing else about the parcel. The moment it grows a `Parcel` entity, it has
  taken over another service's data (P3).
- **Out of scope — any client.** The service is the deliverable. The WinForms application
  was deleted rather than ported, and no replacement UI ships here.

The repository keeps its `komunikaty` name. Renaming breaks every existing clone, link and
remote for a cosmetic gain; the README's first paragraph carries the correction, which is
where a stranger actually looks.

## Consequences

- A reader who finds this file knows what the repository is for in one paragraph, which is
  the entire point of writing it down.
- The scope boundaries are testable rather than aspirational: no `Parcel` entity exists,
  and `ParcelNumber` has no `Generate` method. A PR that adds either is visibly crossing a
  line this document drew.
- The mismatch between the repository name and its contents is permanent and deliberate.
  Anyone who finds it surprising is meant to find this file.
- Should PNG's other services ever want to live here, this ADR has to be superseded first.
  One repository per bounded context is not stated as a rule anywhere in the constitution,
  but a service-per-context architecture in a monorepo needs its own reasoning, and this
  repository does not currently have it.

## Alternatives considered

**Rename the repository to `png-notifications`.** Rejected: it breaks existing clones and
remotes, and the git history would still start with a school announcement board. The
confusion is cheaper to document than to erase.

**Archive this repository and start a new one.** Rejected: the git history is the record
of what the code used to do, and `01-CURRENT-STATE.md` cites specific commits in it. A new
repository would leave that record somewhere else, which is how a `01-CURRENT-STATE`
document ends up describing a tree nobody can find.
