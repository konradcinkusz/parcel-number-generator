# Secrets and configuration

Every value the estate reads, where it comes from, and which tier it sits in. P5: the
shape is hierarchical keys bound to options; the transport is environment variables with
`__` as the separator; the store depends on the environment.

**One source of truth per variable.** Where a variable appears in more than one place,
the authoritative one is named below. Drift between a development composition root and a
platform config is found exactly at variables that have two owners and no stated winner.

## Tiers

| Tier | Meaning |
|---|---|
| **Required in production** | Defaulted or guarded for development; the startup guard refuses a production host without it |
| **Optional** | Absent, a feature degrades and the service still starts (P8) |

## GitHub Actions repository secrets (the deploy workflow's inputs)

| Secret | Feeds | Notes |
|---|---|---|
| `FLY_API_TOKEN` | every deploy job | A deploy token for the Fly organization |
| `POSTGRES_PASSWORD` | `png-parcelnumbers-db` | The generator database's superuser password |
| `NOTIFICATIONS_POSTGRES_PASSWORD` | `png-notifications-db` | The notification database's superuser password |
| `PARCELNUMBERS_DB_CONNECTION_STRING` | `png-parcelnumbers-api` | `Host=png-parcelnumbers-db.internal;Port=5432;Database=parcelnumbers;Username=postgres;Password=…` |
| `NOTIFICATIONS_DB_CONNECTION_STRING` | `png-notifications` | `Host=png-notifications-db.internal;Port=5432;Database=notificationsdb;Username=png;Password=…` |
| `JWT_AUTHORITY` | both services | The identity provider's base URL. Empty ⇒ the startup guard refuses the production host, which is DEV-2 doing its job |

## Per-service variables

### Generator API (`png-parcelnumbers-api`)

| Variable | Tier | Secret | Authoritative source |
|---|---|---|---|
| `ConnectionStrings__parcelnumbersdb` | Required in production | **Yes** | `fly secrets`, set by the deploy workflow |
| `DATABASE_PROVIDER` | Required in production | No | `flyio/parcelnumbers-api.fly.toml` — `PostgreSQL` or `SqlServer`; anything else selects InMemory, a working service whose issued numbers vanish on restart |
| `Jwt__Authority` | Required in production* | No | `fly secrets` — *or the verbose `Security__AllowAnonymousAccess=true` |
| `Pool__From` / `Pool__To` / `Pool__Exclusions__n__From/To` | Optional | No | `appsettings.json` defaults; override in `[env]` |
| `Allocation__Strategy` / `__MaxBatchSize` | Optional | No | `appsettings.json` |

### Notification service (`png-notifications`)

| Variable | Tier | Secret | Authoritative source |
|---|---|---|---|
| `ConnectionStrings__notificationsdb` | Required in production | **Yes** | `fly secrets`, set by the deploy workflow |
| `DATABASE_PROVIDER` | Required in production | No | `flyio/notifications.fly.toml` |
| `Jwt__Authority` | Required in production* | No | `fly secrets` — *same escape hatch, same guard |
| `Jwt__Audience` | Optional | No | `appsettings.json` (`png-notifications`) |
| `Cors__AllowedOrigins__0` … | Optional | No | `[env]` — empty means no cross-origin caller is permitted; the console does not need one, it proxies |
| `Notifications__Webhook__Endpoint` | Optional | No | `fly secrets` — unset, notifications are logged rather than pushed |

### Console (`png-web`)

| Variable | Tier | Secret | Authoritative source |
|---|---|---|---|
| `Services__Generator__BaseUrl` | Required in production | No | `flyio/web.fly.toml` — `.flycast`, so a scaled-to-zero backend wakes |
| `Services__Notifications__BaseUrl` | Required in production | No | `flyio/web.fly.toml` |

### Everything

| Variable | Tier | Secret | Authoritative source |
|---|---|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Optional | No | `fly secrets` — unset, telemetry is recorded but not exported |

## The journey a value takes

```
local:      dotnet user-secrets  →  Aspire parameter  →  environment variable  →  config key
deployed:   fly secrets          →  environment variable                        →  config key
```

## Troubleshooting, keyed on the literal error text

| You see | It means |
|---|---|
| `Refusing to start in Production:` … `No connection string 'parcelnumbersdb'` | The startup guard fired. Set the connection-string secret, or you are about to run an in-memory pool in production |
| `Refusing to start in Production:` … `No 'Jwt:Authority'` | Same guard, other rule. Set `JWT_AUTHORITY`, or make the open deployment explicit with `Security__AllowAnonymousAccess=true` |
| Every request answers `401` after an otherwise healthy deploy | `Jwt__Authority` and the token's `iss` claim are not byte-identical, or the issuer's JWKS endpoint is unreachable from the service |
| The console shows *"Backend timed out … still be waking"* | Normal on the first request after idle: scale-to-zero cold start. Persistent ⇒ check the target app exists and `Services__*__BaseUrl` points at `.flycast`, not `.internal` |
| `database "notificationsdb" does not exist` | The Postgres app booted with a different `POSTGRES_DB` than the connection string names |

## What is not here

No signing key. Both services **validate** tokens against the identity service's JWKS
endpoint and hold no key material of their own — that is P5, and it is why there is no
`Jwt__SecretKey` row above. A symmetric secret shared between an issuer and a validator
means verify equals mint: any holder can forge a token for any user. If a future change
adds a symmetric key here, it is a finding, not a feature.
