#!/bin/bash
cd "$(dirname "$0")"
script="$1"; shift
timeout 1800 /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --disable-autoexec -t 4 \
  --python-exit-code 1 -P "$script" -- "$@" 2>&1 | grep -E "\[v2\]|\[cast\]|Error|Traceback|line [0-9]+|Exception|Assert" | cut -c1-600
