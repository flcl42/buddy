#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
EXPECTED_ROOT="/root/buddy-proxy"
PID_FILE="$ROOT/run/buddy-proxy.pid"

if [[ "$ROOT" != "$EXPECTED_ROOT" ]]; then
  echo "Refusing to run outside $EXPECTED_ROOT" >&2
  exit 1
fi

if [[ ! -f "$PID_FILE" ]]; then
  echo "state=stopped"
  exit 1
fi

PID="$(<"$PID_FILE")"
if [[ ! "$PID" =~ ^[0-9]+$ ]] || ! kill -0 "$PID" 2>/dev/null; then
  echo "state=stale pid=$PID"
  exit 1
fi

if ! tr '\0' ' ' < "/proc/$PID/cmdline" | grep -Fq "$ROOT/buddy-proxy"; then
  echo "state=foreign pid=$PID"
  exit 1
fi

if curl --silent --show-error --fail \
    --resolve rs.flcl.me:38472:127.0.0.1 \
    --cacert "$ROOT/private/tls.crt" \
    https://rs.flcl.me:38472/healthz; then
  echo
  echo "state=healthy pid=$PID"
  exit 0
fi

echo "state=unhealthy pid=$PID"
exit 1
