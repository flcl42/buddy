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
  echo "Buddy proxy is not running."
  exit 0
fi

PID="$(<"$PID_FILE")"
if [[ ! "$PID" =~ ^[0-9]+$ ]] || ! kill -0 "$PID" 2>/dev/null; then
  rm -f -- "$PID_FILE"
  echo "Removed a stale Buddy proxy PID file."
  exit 0
fi

if ! tr '\0' ' ' < "/proc/$PID/cmdline" | grep -Fq "$ROOT/buddy-proxy"; then
  echo "PID $PID does not belong to this deployment; refusing to stop it." >&2
  exit 1
fi

kill -TERM "$PID"
for _ in {1..20}; do
  if ! kill -0 "$PID" 2>/dev/null; then
    rm -f -- "$PID_FILE"
    echo "Buddy proxy stopped."
    exit 0
  fi
  sleep 1
done

echo "Buddy proxy did not stop within 20 seconds; refusing to force-kill it." >&2
exit 1
