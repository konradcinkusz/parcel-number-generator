# Operational scripts

Every script is runnable from any directory, exits non-zero on failure, and says what it
did. CI runs the two `check-*` guards on every push, so a local run answers "will CI
pass" before the push.

| Script | What it does |
|---|---|
| `setup.sh` | One-command onboarding: prerequisites, git hooks, tool restore, build, test, and what to run next |
| `generate-migrations.sh <service> [Name]` | Regenerates a service's EF migration set for **both** providers — `parcelnumbers` or `notifications`. Always both: the DDL genuinely differs |
| `check-kernel-size.sh` | P2's ceiling — the shared kernel stays under 800 lines, mechanically |
| `check-runtime-image-major.sh` | P6 — every Dockerfile's runtime image major version equals the TFM major |
| `compose-init-db.sql` | Not a script to run by hand: mounted by docker-compose to create the second database on first boot |
