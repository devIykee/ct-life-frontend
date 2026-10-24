#!/usr/bin/env bash
# Share the test phones between parallel agents: one owner per device at a time.
#   tools/integration/device_lock.sh acquire <owner> [seeker|infinix|any]   -> prints the serial it locked (exit 1 if none free)
#   tools/integration/device_lock.sh release <owner> [serial]               -> frees that owner's lock(s)
#   tools/integration/device_lock.sh status                                  -> who holds what
# Locks are directories under ~/.ctlife-local/devices (mkdir is atomic). A lock whose owner has not refreshed it
# for LOCK_TTL_MIN minutes is treated as stale and can be taken over; long runs should re-run `acquire` (it refreshes).
# Use the serial with the launcher: SEEKER=<serial> tools/integration/seeker_launch.sh ...
set -euo pipefail
ADB="${ADB:-$HOME/Library/Android/sdk/platform-tools/adb}"
DIR="$HOME/.ctlife-local/devices"; mkdir -p "$DIR"
TTL="${LOCK_TTL_MIN:-45}"
SEEKER_SERIAL=SM02G40619141343
INFINIX_SERIAL=097784036T001536

connected() { "$ADB" devices | awk 'NR>1 && $2=="device"{print $1}'; }
label() { case "$1" in "$SEEKER_SERIAL") echo seeker ;; "$INFINIX_SERIAL") echo infinix ;; *) echo other ;; esac; }
stale() { [ -n "$(find "$1" -maxdepth 0 -mmin +"$TTL" 2>/dev/null)" ]; }

cmd="${1:-status}"
case "$cmd" in
  acquire)
    owner="${2:?owner}"; want="${3:-any}"
    for s in $(connected); do
      l="$(label "$s")"; [ "$want" = any ] || [ "$want" = "$l" ] || continue
      d="$DIR/$s"
      if [ -d "$d" ] && [ "$(cat "$d/owner" 2>/dev/null)" = "$owner" ]; then touch "$d"; echo "$s"; exit 0; fi
      if [ -d "$d" ] && stale "$d"; then rm -rf "$d"; fi
      if mkdir "$d" 2>/dev/null; then echo "$owner" > "$d/owner"; date -u +%FT%TZ > "$d/since"; echo "$s"; exit 0; fi
    done
    echo "no free device ($want)" >&2; exit 1 ;;
  release)
    owner="${2:?owner}"; only="${3:-}"
    for d in "$DIR"/*/; do
      [ -d "$d" ] || continue; s="$(basename "$d")"
      [ -z "$only" ] || [ "$only" = "$s" ] || continue
      [ "$(cat "$d/owner" 2>/dev/null)" = "$owner" ] && rm -rf "$d" && echo "released $s"
    done; exit 0 ;;
  status)
    for s in $(connected); do
      d="$DIR/$s"
      if [ -d "$d" ]; then echo "$(label "$s") $s held by $(cat "$d/owner") since $(cat "$d/since")$(stale "$d" && echo ' (stale)')"
      else echo "$(label "$s") $s free"; fi
    done ;;
  *) echo "usage: $0 acquire <owner> [seeker|infinix|any] | release <owner> [serial] | status" >&2; exit 2 ;;
esac
