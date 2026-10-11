#!/bin/bash
# usage: run.sh <script.py> <log> [args...]   (one Blender at a time; full log kept in the evidence folder)
cd "$(dirname "$0")"
script="$1"; log="$2"; shift 2
timeout 3000 /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --disable-autoexec -t 6 \
  --python-exit-code 1 -P "$script" -- "$@" > "$log" 2>&1
code=$?
grep -E "\[v2\]|\[v3\]|\[cast\]|Error|Traceback|Exception|Assert" "$log" | cut -c1-400 | tail -60
echo "exit $code"
exit $code
