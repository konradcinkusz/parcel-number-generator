# ADR-0002 — This service validates tokens and cannot mint them

**Status:** Accepted; amended by [ADR-0004](0004-one-repository-for-the-png-system.md) — validation stays JWKS-only, but registration is now conditional with a production startup guard
**Date:** 2026-08-15

> **Provenance:** written in the `komunikaty` repository, transferred here with the
> notification service by [ADR-0004](0004-one-repository-for-the-png-system.md).

## Context

P5 requires that exactly one service in the estate holds a signing key, and every other
service validates against that service's published JWKS endpoint.

The usual way this gets broken is not by decision but by convenience. A service adds JWT
validation, the simplest thing that works is a symmetric secret both sides know, and the
configuration ends up shaped so that the algorithm is *inferred* from what happens to be
present:

```csharp
// The shape to avoid.
if (!string.IsNullOrEmpty(config["Jwt:SecretKey"]))
    UseSymmetric(config["Jwt:SecretKey"]);
else
    UseJwks(config["Jwt:Authority"]);
```

A symmetric secret shared between an issuer and a validator means **verify equals mint**:
any holder of the secret can forge a token for any user. The estate has arrived at this
shape twice independently — one system distributes a single HS256 secret to six services
and two frontends, and a second shipped a `GenerateTokens` implementation it never calls
and had no business owning.

There is a subtler adjacent failure the estate has also hit: an issuer whose signing key
was missing selected the symmetric path by inference and published a syntactically valid,
**empty** JWKS. Nothing reported unhealthy, the deploy went green, and every consumer
rejected every token.

## Decision

`AddJwtAuthentication` in the shared kernel has **no symmetric branch at all.**

- `ValidAlgorithms` is `[RS256, ES256]`. A symmetric token is rejected by algorithm before
  any key is consulted.
- `Jwt:Authority` is required. Absent it, the service **throws at startup** with a message
  naming the variable — it does not fall back, and it does not start degraded.
- No configuration key named `Jwt:SecretKey` is read anywhere in this repository. There is
  nothing for a well-meaning change to populate.
- `ValidIssuer` is the Authority. The two must be byte-identical, and the failure when
  they are not is documented in `flyio/SECRETS.md` keyed on the literal error text
  (`IDX10205`), because a trailing slash is the usual cause and it is invisible.

## Consequences

- **This service cannot start without a reachable identity service.** That is the correct
  cost. A notification service that starts and rejects every request is a clear failure;
  one that starts and accepts forged tokens is not a failure at all until it is a breach.
- P8 does not apply. An identity provider is not an optional dependency that degrades — it
  is the thing that decides whether a request is allowed, and there is no reduced-feature
  version of that.
- Rotating the issuer's keypair requires nothing here. The JWKS endpoint is fetched and
  cached by the handler; a new `kid` is picked up without a deploy of this service.
- If a future change adds a symmetric key to this repository, it is a finding rather than
  a feature, and `flyio/SECRETS.md` says so in the "What is not here" section.

## Alternatives considered

**Support both, defaulting to asymmetric.** Rejected — this is precisely the shape whose
failure mode is described above. A branch that is never meant to be taken is taken
eventually, and it is taken silently.

**Support both, with a check that fails startup when the symmetric path is selected in
production.** Rejected as more machinery to reach the same place, with an environment
check as the only thing standing between the estate and a forgeable token. Deleting the
branch is strictly stronger than guarding it.

**Accept any algorithm the JWKS advertises.** Rejected: it re-opens the algorithm-confusion
class of attack for no gain. The issuer signs with RS256 or ES256; listing them is free.
