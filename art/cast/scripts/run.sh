#!/bin/bash
# Run one cast pipeline stage headless (one Blender job at a time).
# usage: art/cast/scripts/run.sh <script.py> [args...]
cd "$(dirname "$0")"
script="$1"; shift
timeout 1800 /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --disable-autoexec -t 4 \
  --python-exit-code 1 -P "$script" -- "$@" 2>&1 | grep -E "\[cast\]|Error|Traceback|line [0-9]+|Exception" | cut -c1-600
