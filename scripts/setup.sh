#!/usr/bin/env bash
#
# One-command onboarding. Checks prerequisites, then tells you what you can run.
#
# Deliberately short, because this service has no mandatory secret: it falls back to an
# in-memory database with no configuration at all (P8), so the honest onboarding story is
# "clone and run" rather than a wizard that pretends otherwise.

set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

fail=0

step() { printf '\n\033[1m%s\033[0m\n' "$1"; }
ok() { printf '  \033[32m✓\033[0m %s\n' "$1"; }
missing() { printf '  \033[31m✗\033[0m %s\n' "$1"; fail=1; }

step "1. Prerequisites"

if command -v dotnet >/dev/null 2>&1; then
  version="$(dotnet --version)"
  case "$version" in
    10.*) ok ".NET SDK $version" ;;
    *) missing ".NET SDK $version — 10.x is required. https://dotnet.microsoft.com/download" ;;
  esac
else
  missing ".NET SDK not found. Install 10.x: https://dotnet.microsoft.com/download"
fi

if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
  ok "Docker is running (needed for the AppHost's Postgres, optional otherwise)"
else
  printf '  \033[33m!\033[0m %s\n' "Docker not available — optional. Without it, use the in-memory fallback below."
fi

[ "$fail" -eq 0 ] || { printf '\n\033[31mFix the above and run again.\033[0m\n'; exit 1; }

step "2. Repository tooling"
git config core.hooksPath .githooks
ok "pre-commit secret scan hook enabled (.githooks)"
dotnet tool restore >/dev/null
ok "dotnet-ef restored from dotnet-tools.json"

step "3. Restore and test"
dotnet restore ParcelNumberGenerator.slnx
dotnet test --solution ParcelNumberGenerator.slnx

step "4. What you can run now"
cat <<'EOF'
  Everything at once, via the Aspire composition root (needs Docker):

      dotnet run --project src/ParcelNumberGenerator.AppHost

  That brings up Postgres, the generator API, the notification service and the operator
  console together, wired, with the dashboard. The console is the "web" resource's URL.

  In-memory, no dependencies — data is lost when the processes stop:

      dotnet run --project src/ParcelNumberGenerator.Api            # :5180
      dotnet run --project src/ParcelNumberGenerator.Notifications  # :5181
      dotnet run --project src/ParcelNumberGenerator.Web            # :5170 — the console

      curl -X POST 'http://localhost:5180/parcel-numbers?count=5'
      curl http://localhost:5181/api/notifications

  Or without the SDK at all:  docker compose up --build   (console on :8090)

  Secrets: none are required. In Production each service refuses to start without a
  connection string and a JWT issuer — see docs/architecture/03-TARGET-ARCHITECTURE.md.
  Local overrides go in the user-secret store, never in a file in the working tree:

      dotnet user-secrets --project src/ParcelNumberGenerator.Api set "Jwt:Authority" "https://your-idp"
EOF
