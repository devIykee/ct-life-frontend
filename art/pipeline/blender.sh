#!/bin/sh
# Run Blender 5.1.2 headless with the isolated CT Life user resources (MPFB + CC0 asset packs
# live in art/raw/blender_user, so nothing touches the user's own Blender config).
# Usage: art/pipeline/blender.sh <script.py> [-- args...]
HERE="$(cd "$(dirname "$0")" && pwd)"
export BLENDER_USER_RESOURCES="$HERE/../raw/blender_user"
BLENDER="${BLENDER:-/Applications/Blender.app/Contents/MacOS/Blender}"
SCRIPT="$1"; shift
exec "$BLENDER" -b --factory-startup -P "$SCRIPT" "$@"
