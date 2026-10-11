#!/usr/bin/env bash
# Keeps `adb reverse` for the local backend alive on the Seeker. If anything restarts the adb server (another tool
# running adb kill-server), the reverse mappings vanish and the phone loses the backend; this re-applies them.
#   tools/integration/adb_reverse_watchdog.sh [minutes]   (default 60)
ADB="${ADB:-$HOME/Library/Android/sdk/platform-tools/adb}"; S="${SEEKER:-SM02G40619141343}"
end=$((SECONDS + ${1:-60} * 60))
while [ $SECONDS -lt $end ]; do
  if ! "$ADB" -s "$S" reverse --list 2>/dev/null | grep -q "tcp:4010"; then
    for p in 4010 2567 7880 7881; do "$ADB" -s "$S" reverse tcp:$p tcp:$p >/dev/null 2>&1; done
    echo "$(date +%H:%M:%S) re-applied adb reverse"
  fi
  sleep 3
done
