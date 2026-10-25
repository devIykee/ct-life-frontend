#!/usr/bin/env bash
# Launch CT Life on the Seeker with development launch extras (dev builds only).
# Writes a fresh automation key into the app's private files (adb run-as) and passes it, as LaunchArgs requires.
#   tools/integration/seeker_launch.sh [extras...]      e.g. -e autoenter 1 -e venue perp_pit
set -euo pipefail
ADB="${ADB:-$HOME/Library/Android/sdk/platform-tools/adb}"
# Two phones and parallel agents share this Mac: the serial must come from the caller's lock
# (tools/integration/device_lock.sh acquire <owner> ...). No fallback, so a failed acquire can never hit another lane's phone.
S="${SEEKER:-}"
if [ -z "$S" ]; then echo "SEEKER=<serial> is required (get one with tools/integration/device_lock.sh acquire <owner> any)." >&2; exit 2; fi
LOCK="$HOME/.ctlife-local/devices/$S"
if [ -d "$LOCK" ] && [ -n "${CTLIFE_LOCK_OWNER:-}" ] && [ "$(cat "$LOCK/owner" 2>/dev/null)" != "$CTLIFE_LOCK_OWNER" ]; then
  echo "Device $S is locked by $(cat "$LOCK/owner"), not $CTLIFE_LOCK_OWNER; refusing to launch." >&2; exit 3
fi
if [ ! -d "$LOCK" ]; then echo "warning: $S is not locked; acquire it with device_lock.sh before launching." >&2; fi
PKG=com.ctlife.app; ACT=com.unity3d.player.UnityPlayerGameActivity
API_PORT="${CTLIFE_API_PORT:-4010}"; REALTIME_PORT="${CTLIFE_REALTIME_PORT:-2567}"
for port in "$API_PORT" "$REALTIME_PORT"; do
  if [[ ! "$port" =~ ^[0-9]{1,5}$ ]] || (( 10#$port < 1 || 10#$port > 65535 )); then
    echo "Invalid backend port: expected an integer from 1 to 65535." >&2
    exit 1
  fi
done
KEY="$(openssl rand -hex 24)"
"$ADB" -s "$S" shell am force-stop $PKG
"$ADB" -s "$S" shell "run-as $PKG sh -c 'mkdir -p files && echo $KEY > files/automation.key'"
# Any failed mapping stops the script before launch; an older mapping may still exist.
"$ADB" -s "$S" reverse tcp:4010 "tcp:$API_PORT" >/dev/null
"$ADB" -s "$S" reverse tcp:2567 "tcp:$REALTIME_PORT" >/dev/null
for p in 7880 7881; do "$ADB" -s "$S" reverse tcp:$p tcp:$p >/dev/null; done
"$ADB" -s "$S" shell am start -n $PKG/$ACT -e automationKey "$KEY" "$@" >/dev/null
