#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
EXPECTED_ROOT="/root/buddy-proxy"
PID_FILE="$ROOT/run/buddy-proxy.pid"
LOG_FILE="$ROOT/logs/buddy-proxy.log"

if [[ "$ROOT" != "$EXPECTED_ROOT" ]]; then
  echo "Refusing to run outside $EXPECTED_ROOT" >&2
  exit 1
fi

chmod 700 "$ROOT"
mkdir -p "$ROOT/run" "$ROOT/logs" "$ROOT/data"
if [[ -f "$PID_FILE" ]]; then
  PID="$(<"$PID_FILE")"
  if [[ "$PID" =~ ^[0-9]+$ ]] && kill -0 "$PID" 2>/dev/null; then
    if tr '\0' ' ' < "/proc/$PID/cmdline" | grep -Fq "$ROOT/buddy-proxy"; then
      echo "Buddy proxy is already running as PID $PID."
      exit 0
    fi
  fi
  rm -f -- "$PID_FILE"
fi

if [[ ! -x "$ROOT/buddy-proxy" ]]; then
  echo "The deployed buddy-proxy executable is missing." >&2
  exit 1
fi

if [[ ! -r "$ROOT/appsettings.Production.json" || ! -r "$ROOT/private/tls.pfx" ]]; then
  echo "Production settings or TLS certificate are missing." >&2
  exit 1
fi

(
  cd "$ROOT"
  nohup env ASPNETCORE_ENVIRONMENT=Production \
    "$ROOT/buddy-proxy" --contentRoot "$ROOT" \
    >> "$LOG_FILE" 2>&1 &
  echo "$!" > "$PID_FILE"
)

PID="$(<"$PID_FILE")"
for _ in {1..30}; do
  if ! kill -0 "$PID" 2>/dev/null; then
    echo "Buddy proxy exited during startup. See $LOG_FILE" >&2
    exit 1
  fi

  if curl --silent --fail \
      --resolve rs.flcl.me:38472:127.0.0.1 \
      --cacert "$ROOT/private/tls.crt" \
      https://rs.flcl.me:38472/healthz >/dev/null; then
    echo "Buddy proxy is healthy as PID $PID."
    exit 0
  fi
  sleep 1
done

echo "Buddy proxy did not become healthy. See $LOG_FILE" >&2
exit 1
