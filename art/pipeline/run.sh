#!/bin/sh
# One heavy Blender job at a time (the Unity editor shares this Mac), always with a timeout.
# Usage: art/pipeline/run.sh <script.py> [-- args...]   (env TIMEOUT=seconds, default 1800)
# Log: art/raw/logs/<script>_<timestamp>.log ; a lock file prevents parallel runs.
HERE="$(cd "$(dirname "$0")" && pwd)"
LOGDIR="$HERE/../raw/logs"; mkdir -p "$LOGDIR"
LOCK="$LOGDIR/blender.lock"
if [ -e "$LOCK" ] && kill -0 "$(cat "$LOCK")" 2>/dev/null; then
  echo "another Blender job is running (pid $(cat "$LOCK")); refusing to start a second one" >&2; exit 3
fi
echo $$ > "$LOCK"
trap 'rm -f "$LOCK"' EXIT INT TERM
NAME="$(basename "$1" .py)_$(date +%Y%m%d_%H%M%S)"
echo "[run] $* -> $LOGDIR/$NAME.log (timeout ${TIMEOUT:-1800}s)"
timeout "${TIMEOUT:-1800}" "$HERE/blender.sh" "$@" > "$LOGDIR/$NAME.log" 2>&1
RC=$?
echo "[run] exit $RC"
grep -E "^\[(env|char_|anim_|char_npc|char_preview)|RENDERED|MONTAGE|Traceback|Error|FAILED" "$LOGDIR/$NAME.log" | grep -v "ERROR Image file" | tail -40
exit $RC
