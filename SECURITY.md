# Security policy

## Reporting a vulnerability

**Do not open a public issue.** An issue is world-readable the moment it is filed, and a
report is a working exploit until it is fixed.

Use GitHub's private reporting instead:
[**Report a vulnerability**](https://github.com/konradcinkusz/parcel-number-generator/security/advisories/new).
That opens a draft advisory visible only to you and the maintainer, and it is the same
place the fix and the CVE are coordinated from, so nothing has to move between tools.

If private reporting is unavailable to you, email the address on
[the maintainer's GitHub profile](https://github.com/konradcinkusz) with `SECURITY` in the
subject.

What to include, in whatever detail you have: the version or commit, which of the three
services is affected, what an attacker gets, and the smallest sequence of requests that
demonstrates it. A `curl` that reproduces beats a description of one.

This is a personal project with a single maintainer and no paid support. It carries no
response-time guarantee. In practice you should expect an acknowledgement within a week;
if nothing arrives in two, assume the mail was lost and open a public issue that says a
security report is waiting **without describing the flaw**.

There is no bug bounty.

## What is in scope

The three services in this repository — the generator API, the notification service and
the operator console with its BFF — and the deployment configuration in [`flyio/`](flyio/).

Reported against the **default configuration**, which is documented as open on purpose:
authentication registers only when `Jwt__Authority` is set, so a fresh clone runs with no
identity provider and every endpoint reachable. That is [P8][adr2] and it is not a
vulnerability. A finding that a development clone needs no token is a finding about
[ADR-0002][adr2], not about the code.

What is a vulnerability is any of the following:

- A way to reach a protected endpoint **when an issuer is configured** — signature or
  audience checks bypassed, `alg: none`, a token accepted after its `exp`.
- A way to make the generator issue the same number twice, or to read another tenant's
  notification.
- A way to infer daily allocation volume from the generator's responses. Not leaking it is
  a design goal of the allocation strategies, not a side effect.
- SQL injection, SSRF through the console's BFF proxy, or anything that escapes the
  container.
- A production deployment that starts without a connection string or an issuer and
  *without* `Security__AllowAnonymousAccess=true`. The startup guards are a security
  control, and CI smoke-tests them precisely so that a hole here cannot open quietly.

[adr2]: docs/decisions/0002-token-validation-only.md

## What is out of scope

- The credentials in [`docker-compose.yml`](docker-compose.yml). They are deliberately
  weak, deliberately committed, allowlisted in [`.gitleaks.toml`](.gitleaks.toml), and the
  file says so at the top. That compose stack is for local evaluation and nothing else.
- Missing hardening on a deployment you configured yourself. This repository ships the
  guards; it cannot ship your issuer.
- Automated-scanner output with no demonstrated impact.
- The known open deviations already tracked and dated in
  [`docs/architecture/DEVIATIONS.md`](docs/architecture/DEVIATIONS.md). Reporting one of
  those tells us what we already wrote down. Reporting a *consequence* of one that the
  ledger does not anticipate is useful — say which row you started from.

## Supported versions

`master` only. There are no release branches and no backports; a fix ships forward.

## What this project does with key material

Nothing — it holds none. Both services validate bearer tokens against an identity
provider's JWKS endpoint discovered over OIDC. Neither holds a signing key, so neither can
mint a token, and a compromise of either cannot produce a credential that the other
accepts. The reasoning and the rejected symmetric-secret alternative are in
[ADR-0002][adr2].

Secrets reach a deployment as Fly secrets, inventoried per service in
[`flyio/SECRETS.md`](flyio/SECRETS.md). None is committed.

## Preventive controls

Not a promise that the code is safe — a statement of what runs, so you know what a report
has already had to get past:

| Control | Where |
|---|---|
| Secret scan over **full history**, every push and weekly | [`secret-scan.yml`](.github/workflows/secret-scan.yml) |
| Pre-commit secret scan | [`.githooks/pre-commit`](.githooks/), installed by `scripts/setup.sh` |
| Transitive vulnerable-dependency audit, build-gating | [`secret-scan.yml`](.github/workflows/secret-scan.yml), plus `NuGetAudit` |
| Startup guards asserted against a real production image | [`ci.yml`](.github/workflows/ci.yml) |
| Warnings as errors, including NuGet advisories | [`Directory.Build.props`](Directory.Build.props) |
| Non-root container user, unprivileged port | each `Dockerfile` |

There is **no SAST job**, and the reason is written down rather than left to be discovered:
DEV-4 in [`DEVIATIONS.md`](docs/architecture/DEVIATIONS.md). Code scanning is free for
public repositories, so that row closes with this one.
