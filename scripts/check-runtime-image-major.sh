#!/usr/bin/env bash
#
# P6 — the runtime image major version must equal the TFM major version.
#
# .NET's default roll-forward does not cross a major version, so a mismatch is not a
# warning: it is a container that exits immediately, in the deployed environment, with no
# useful error. It is also invisible in review, because the two numbers live in different
# files.
#
# Both are derived from Directory.Build.props here, so this check exists to catch a
# Dockerfile that hardcoded a version instead of taking the build argument.

set -euo pipefail

cd "$(dirname "$0")/.."

TFM=$(grep -oP '(?<=<TargetFramework>net)[0-9]+\.[0-9]+(?=</TargetFramework>)' Directory.Build.props | head -1)

if [ -z "$TFM" ]; then
  echo "error: could not read <TargetFramework> from Directory.Build.props" >&2
  exit 1
fi

TFM_MAJOR="${TFM%%.*}"
STATUS=0

while IFS= read -r dockerfile; do
  while IFS= read -r image; do
    # Skip the ARG-driven form; that is the shape this check wants to see.
    case "$image" in
      *'${DOTNET_VERSION}'*) continue ;;
    esac

    version="${image##*:}"
    major="${version%%.*}"

    if [ "$major" != "$TFM_MAJOR" ]; then
      echo "error: ${dockerfile} uses ${image} but the TFM is net${TFM}" >&2
      STATUS=1
    fi
  done < <(grep -oP '(?<=^FROM )mcr\.microsoft\.com/dotnet/[a-z]+:[^ ]+' "$dockerfile" || true)

  # The default must match too, or a build that omits --build-arg picks the wrong one.
  while IFS= read -r default; do
    major="${default%%.*}"
    if [ "$major" != "$TFM_MAJOR" ]; then
      echo "error: ${dockerfile} defaults DOTNET_VERSION to ${default} but the TFM is net${TFM}" >&2
      STATUS=1
    fi
  done < <(grep -oP '(?<=^ARG DOTNET_VERSION=)[0-9.]+' "$dockerfile" || true)
done < <(find src -name Dockerfile -not -path '*/obj/*' -not -path '*/bin/*')

if [ "$STATUS" -eq 0 ]; then
  echo "Runtime image major version matches the TFM (net${TFM}) in every Dockerfile."
fi

exit "$STATUS"
