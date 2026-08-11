#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
EXPECTED_ROOT="/root/buddy-proxy"
PID_FILE="$ROOT/run/buddy-proxy.pid"
LOG_FILE="$ROOT/logs/buddy-proxy.log"
TELEGRAM_TOKEN_FILE="$ROOT/private/telegram-bot-token"
TELEGRAM_CHAT_FILE="$ROOT/private/telegram-chat-id"

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

TELEGRAM_ENV=()
if [[ -r "$TELEGRAM_TOKEN_FILE" && -r "$TELEGRAM_CHAT_FILE" ]]; then
  TELEGRAM_TOKEN="$(<"$TELEGRAM_TOKEN_FILE")"
  TELEGRAM_CHAT_ID="$(<"$TELEGRAM_CHAT_FILE")"
  if [[ -z "$TELEGRAM_TOKEN" || -z "$TELEGRAM_CHAT_ID" ]]; then
    echo "Telegram token and chat files must not be empty." >&2
    exit 1
  fi

  TELEGRAM_ENV=(
    "Telegram__Enabled=true"
    "Telegram__BotToken=$TELEGRAM_TOKEN"
    "Telegram__ChatId=$TELEGRAM_CHAT_ID"
  )
elif [[ -e "$TELEGRAM_CHAT_FILE" ]]; then
  echo "Telegram chat is configured without a readable bot token." >&2
  exit 1
elif [[ -e "$TELEGRAM_TOKEN_FILE" ]]; then
  if [[ ! -r "$TELEGRAM_TOKEN_FILE" ]]; then
    echo "Telegram bot token is not readable." >&2
    exit 1
  fi

  echo "Telegram feedback is waiting for a chat ID; delivery remains disabled." >&2
fi

(
  cd "$ROOT"
  nohup env ASPNETCORE_ENVIRONMENT=Production \
    "${TELEGRAM_ENV[@]}" \
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
