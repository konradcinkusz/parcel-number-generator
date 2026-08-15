#!/usr/bin/env bash
#
# Generates (or updates) an EF Core migration set for both providers of one service.
#
#   scripts/generate-migrations.sh parcelnumbers AddSomething
#   scripts/generate-migrations.sh notifications AddDispatchColumn
#
# Migrations are generated from the model, not from a database, so nothing needs to be
# running — the placeholder connection strings below exist only to stop the provider
# resolution in AddDatabaseContext from degrading to InMemory (P4/P8), which would
# produce no migration at all and no error either.
#
# Commit the result. A relational provider will not start without it: schema is migrated,
# never "ensured". CI fails a pull request whose model has drifted from its migrations.

set -euo pipefail

cd "$(dirname "$0")/.."

SERVICE="${1:-}"
NAME="${2:-}"

if [ -z "$NAME" ]; then
  NAME="Schema$(date -u +%Y%m%d%H%M)"
fi

case "$SERVICE" in
  parcelnumbers)
    STARTUP=src/ParcelNumberGenerator.Api
    CONTEXT=ParcelNumbersDbContext
    PROJECT_PREFIX=ParcelNumberGenerator.Migrations
    CONNECTION_VAR=ConnectionStrings__parcelnumbersdb
    DB=parcelnumbers
    ;;
  notifications)
    STARTUP=src/ParcelNumberGenerator.Notifications
    CONTEXT=NotificationsDbContext
    PROJECT_PREFIX=ParcelNumberGenerator.Notifications.Migrations
    CONNECTION_VAR=ConnectionStrings__notificationsdb
    DB=notifications
    ;;
  *)
    echo "usage: scripts/generate-migrations.sh <parcelnumbers|notifications> [MigrationName]" >&2
    exit 1
    ;;
esac

if ! dotnet tool run dotnet-ef --version >/dev/null 2>&1; then
  echo "dotnet-ef is not restored. Run:" >&2
  echo "  dotnet tool restore" >&2
  exit 1
fi

generate() {
  local provider="$1"
  local placeholder="$2"

  echo "── ${provider} ──"

  env \
    DATABASE_PROVIDER="${provider}" \
    "${CONNECTION_VAR}=${placeholder}" \
    dotnet tool run dotnet-ef migrations add "${NAME}" \
    --project "src/${PROJECT_PREFIX}.${provider}" \
    --startup-project "${STARTUP}" \
    --context "${CONTEXT}"
}

generate PostgreSQL "Host=localhost;Database=${DB};Username=placeholder;Password=placeholder"
generate SqlServer "Server=localhost;Database=${DB};User Id=placeholder;Password=placeholder;TrustServerCertificate=true"

echo
echo "Both provider sets updated. Review and commit the generated files."
