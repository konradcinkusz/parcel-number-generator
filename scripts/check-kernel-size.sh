#!/usr/bin/env bash
#
# P2's ceiling, made mechanical.
#
# The shared kernel is plumbing. Stating that in prose has already failed twice in this
# estate: a shared library that began as cross-cutting plumbing ended up holding entities,
# pricing tables and user-facing strings, and every consumer was then coupled to every
# change. A line count is a crude proxy for "someone put a domain in here", but it is a
# proxy that cannot be talked round in review.
#
# The companion check — that the kernel references no domain assembly and declares no
# entity or enum — is an architecture test, in tests/ParcelNumberGenerator.Tests.
#
# If this fails, the answer is almost never to raise the ceiling. It is to move what was
# added into the service that owns it, or into ParcelNumberGenerator.Contracts if two services genuinely
# need the same shape.

set -euo pipefail

cd "$(dirname "$0")/.."

CEILING=800
KERNEL_DIR="src/ParcelNumberGenerator.ServiceDefaults"

if [ ! -d "$KERNEL_DIR" ]; then
  echo "error: $KERNEL_DIR does not exist" >&2
  exit 1
fi

TOTAL=$(find "$KERNEL_DIR" -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -print0 \
  | xargs -0 cat \
  | wc -l)

echo "Shared kernel: ${TOTAL} lines (ceiling ${CEILING})"

if [ "$TOTAL" -gt "$CEILING" ]; then
  cat >&2 <<EOF

error: the shared kernel is ${TOTAL} lines, over its ${CEILING}-line ceiling.

Something was added to ParcelNumberGenerator.ServiceDefaults that probably is not plumbing. Check what
changed: business rules, entities, seed data, user-facing strings and per-service
constants all belong to the service that owns them, not here.

Raising CEILING in this script is a decision that belongs in an ADR, not in a commit
that is trying to do something else.
EOF
  exit 1
fi
