#!/bin/sh
# Copy the rendered nod atlas into Resources/World/Island (keeps the .meta GUID; a new file gets the island sign textures'
# import settings: mipmaps, sRGB, clamp).
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"; REPO="$(cd "$HERE/../../.." && pwd)"
DST="$REPO/client/Assets/Resources/World/Island"; TPL="$REPO/client/Assets/World/Island/Textures/Signs/sign_rug_room.png.meta"
cp "$HERE/out/png/nod_atlas.png" "$DST/nod_atlas.png"
if [ ! -f "$DST/nod_atlas.png.meta" ]; then
  g="$(uuidgen | tr -d - | tr 'A-Z' 'a-z')"; sed -e "s/^guid: .*/guid: $g/" "$TPL" > "$DST/nod_atlas.png.meta"
fi
echo installed
