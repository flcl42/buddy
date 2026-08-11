#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${DBUS_SESSION_BUS_ADDRESS:-}" ]]; then
  exec dbus-run-session -- bash "$0" "$@"
fi

if [[ "${BUDDY_TRAY_IN_XVFB:-}" != "1" ]]; then
  exec env BUDDY_TRAY_IN_XVFB=1 GDK_BACKEND=x11 \
    xvfb-run --auto-servernum bash "$0" "$@"
fi

if [[ $# -ne 1 ]]; then
  echo "Usage: $0 <published Buddy directory>" >&2
  exit 2
fi

publish=$(realpath "$1")
repo=$(cd "$(dirname "$0")/.." && pwd)
scratch=$(mktemp -d)

cleanup() {
  if [[ -n "${buddy_pid:-}" ]]; then kill "$buddy_pid" 2>/dev/null || true; fi
  if [[ -n "${watcher_pid:-}" ]]; then kill "$watcher_pid" 2>/dev/null || true; fi
  rm -rf -- "$scratch"
}
trap cleanup EXIT

export BUDDY_DATA_ROOT="$scratch/data"
export BUDDY_TRAY_CAPTURE="$scratch/registered"
export BUDDY_TRAY_READY="$scratch/watcher-ready"

/usr/bin/python3 "$repo/scripts/linux-tray-watcher.py" \
  >"$scratch/watcher.log" 2>&1 &
watcher_pid=$!

for _ in $(seq 1 50); do
  [[ -f "$BUDDY_TRAY_READY" ]] && break
  sleep 0.1
done
test -f "$BUDDY_TRAY_READY"

"$publish/Buddy" >"$scratch/buddy.log" 2>&1 &
buddy_pid=$!

for _ in $(seq 1 150); do
  [[ -f "$BUDDY_TRAY_CAPTURE" ]] && break
  if ! kill -0 "$buddy_pid" 2>/dev/null; then
    cat "$scratch/buddy.log" >&2
    exit 1
  fi
  sleep 0.1
done

if [[ ! -f "$BUDDY_TRAY_CAPTURE" ]]; then
  cat "$scratch/buddy.log" >&2
  echo "Buddy did not register its StatusNotifier item." >&2
  exit 1
fi

service=$(cat "$BUDDY_TRAY_CAPTURE")
id_result=$(gdbus call --session \
  --dest "$service" \
  --object-path /StatusNotifierItem \
  --method org.freedesktop.DBus.Properties.Get \
  org.kde.StatusNotifierItem Id)
title_result=$(gdbus call --session \
  --dest "$service" \
  --object-path /StatusNotifierItem \
  --method org.freedesktop.DBus.Properties.Get \
  org.kde.StatusNotifierItem Title)
gdbus call --session \
  --dest "$service" \
  --object-path /StatusNotifierItem \
  --method org.kde.StatusNotifierItem.Activate 0 0 >/dev/null

[[ "$id_result" == *"'chitchat-buddy'"* ]]
[[ "$title_result" == *"'Chitchat Buddy"* ]]
kill -0 "$buddy_pid"

echo "Buddy registered, answered tray properties, and handled activation."
