# ADR-0003 — Parcel number format, and who is allowed to mint one

**Status:** Accepted; amended by [ADR-0004](0004-one-repository-for-the-png-system.md) — the canonical form is presentation over the generator's integer, not the generator's wire format
**Date:** 2026-08-15

> **Provenance:** written in the `komunikaty` repository, transferred here with the
> notification service by [ADR-0004](0004-one-repository-for-the-png-system.md).

## Context

Parcel numbers reach this service from several places, each speaking its own dialect:

| Source | What it sends |
|---|---|
| The Parcel Number Generator | `PNG-12345678-2` — the canonical form |
| Handheld barcode scanners | `12345678` — the payload, no prefix, no check digit |
| Printed labels | `123456782` — payload plus check digit, no prefix |
| The legacy WMS being replaced | `WMS/12345678` |
| Humans, via the operator UI | any of the above, lowercased, with spaces or `.` or `/` |

Two questions follow: what is the one form the system stores, and is this service allowed
to create a number that did not exist before.

## Decision

### The canonical form

```
PNG-NNNNNNNN-C
     │        └─ Luhn check digit over the eight-digit payload
     └────────── eight decimal digits
```

Fourteen characters, always. `ParcelNumberLimits.CanonicalLength` is the single source of
that number, consumed by both the parser and the schema.

**Luhn**, specifically, because it catches every single-digit error and almost every
adjacent transposition — which between them are what a mis-keyed parcel number actually
is. It is not a security property and is not treated as one.

### Normalization rules

At the edge (P11), in `ParcelNumber.TryParse`:

1. Uppercase; drop ` `, `-`, `/`, `.`, `_` and tab. Nothing else is dropped, so anything
   unexpected fails the digit check rather than being mangled into something valid.
2. Strip a leading `PNG` or `WMS`. **Only those two** — `DHL/12345678` is rejected rather
   than silently accepted as parcel `12345678`.
3. Eight digits remaining: compute the check digit. Nine: verify the supplied one, and
   reject on mismatch.
4. Emit the canonical form.

**Eight digits with no check digit is accepted, not rejected.** The check digit exists to
catch transcription errors, and a barcode scan was not transcribed. Demanding one from a
scanner would mean either rejecting every scan or having the scanner compute it, which
puts the algorithm in two places.

### Who may mint

**This service may not.** `ParcelNumber` has `TryParse`, `TryParseOptional` and `Parse`.
It has no `Generate`, `Next` or `Allocate`, and it never will.

Allocating a parcel number is the generator service's bounded context (P3). A second
service that can also mint one is a duplicate-key incident waiting for its first busy
morning — and unlike most bounded-context violations, this one produces silent data
corruption rather than a compile error, because two parcels sharing a number both look
perfectly valid.

## Consequences

- One form is stored, indexed and returned. A query filter written as `wms/12345678`
  matches a row raised as `PNG-12345678-2`, because both normalize before they are
  compared — this is asserted by `NotificationQueryTests`.
- Adding a sixth dialect costs one branch in `StripKnownPrefix` and one row in the test
  theory. Adding a carrier whose numbers are not eight digits costs more, and would want
  its own ADR.
- The eight-digit payload gives 100 million numbers. At the ~30k parcel events per day
  assumed in `flyio/INFRASTRUCTURE-ANALYSIS.md`, that is not a constraint this system will
  reach. If it ever is, widening the payload is a change to the *generator* and a
  migration here — the parser's length checks are the only thing that would need to move.
- This service will reject a parcel number the generator considers valid if the two
  disagree about the format. That is the intended direction of failure: better a rejected
  notification than a notification filed against a parcel number nobody can look up.

## Alternatives considered

**Store whatever arrived, normalize on read.** Rejected: it puts the dialect problem in
every query, every index and every consumer, forever. Normalizing once at the edge is the
whole of P11.

**Accept any prefix and strip it.** Rejected: `DHL/12345678` and `PNG-12345678-2` would
become the same parcel, and a carrier reference is not a parcel number. Two known prefixes
are a decision; "whatever precedes the digits" is a guess.

**No check digit — just eight digits.** Rejected: the operator UI has humans typing parcel
numbers into a filter box, and a single mistyped digit would silently return the wrong
parcel's notifications rather than nothing. A check digit turns that into an error
message.

**Random or UUID parcel numbers.** Rejected: parcel numbers are read aloud, written on
labels and typed by hand. Eight digits with a check digit is at the edge of what that
tolerates; a UUID is well past it.
