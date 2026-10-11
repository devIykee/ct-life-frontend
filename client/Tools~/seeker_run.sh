#!/usr/bin/env bash
# Install + launch the client on the Seeker and capture perf evidence.
#   client/Tools~/seeker_run.sh <label> <seconds> [extra am-start args...]
# e.g. client/Tools~/seeker_run.sh spike 150 -e spike 1 -e livekitUrl ws://localhost:7880 -e livekitToken "$(python3 client/Tools~/livekit_dev_token.py)"
# Output: docs/progress/screens/<label>-<time>.{png,logcat.txt,gfxinfo.txt}
set -uo pipefail
REPO="$(cd "$(dirname "$0")/../.." && pwd)"
SERIAL="${SEEKER_SERIAL:-SM02G40619141343}"
ADB=("$HOME/Library/Android/sdk/platform-tools/adb" -s "$SERIAL")
PKG=com.ctlife.app
ACT=com.unity3d.player.UnityPlayerGameActivity
LABEL="$1"; SECS="$2"; shift 2
OUT="$REPO/docs/progress/screens"; mkdir -p "$OUT"
STAMP="$(date +%Y%m%d-%H%M%S)"; BASE="$OUT/$LABEL-$STAMP"
APK="$REPO/client/Builds/ctlife-dev.apk"
[ -f "$APK" ] && "${ADB[@]}" install -r -g "$APK" | tail -1
for p in 4010 2567 3000 7880 7881; do "${ADB[@]}" reverse tcp:$p tcp:$p >/dev/null; done
"${ADB[@]}" shell input keyevent KEYCODE_WAKEUP
"${ADB[@]}" shell am force-stop $PKG
"${ADB[@]}" logcat -G 16M >/dev/null 2>&1
"${ADB[@]}" logcat -c
"${ADB[@]}" logcat -v time -s Unity:V CRASH:E AndroidRuntime:E libc:F DEBUG:F > "$BASE.logcat.txt" &
LOGPID=$!
"${ADB[@]}" shell dumpsys gfxinfo $PKG reset >/dev/null 2>&1
"${ADB[@]}" shell am start -W -n $PKG/$ACT "$@" | grep -E "Status|TotalTime|WaitTime"
sleep "$SECS"
"${ADB[@]}" exec-out screencap -p > "$BASE.png"
"${ADB[@]}" shell dumpsys gfxinfo $PKG > "$BASE.gfxinfo.txt"
"${ADB[@]}" shell dumpsys SurfaceFlinger --latency 2>/dev/null | head -1 >> "$BASE.gfxinfo.txt"
"${ADB[@]}" shell dumpsys thermalservice | grep -E "Thermal Status|mValue" | head -6 >> "$BASE.gfxinfo.txt"
"${ADB[@]}" shell dumpsys battery | grep -E "level|temperature" >> "$BASE.gfxinfo.txt"
kill $LOGPID 2>/dev/null
echo "== $BASE"
grep -E "CTLIFE \[(fps|spike|perf|boot|voice|rt|env)\]" "$BASE.logcat.txt" | tail -40
grep -E "FATAL|Fatal|crash|SIGSEGV|NullReferenceException|Exception:" "$BASE.logcat.txt" | head -10
