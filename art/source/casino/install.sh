#!/bin/sh
# Copy the rendered Rug Room art into Resources/UI/Casino (keeps existing .meta GUIDs; new files get a UI texture meta
# cloned from the lobby icons: no mipmaps, alpha is transparency, Android ASTC 4x4 at the native size).
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"; REPO="$(cd "$HERE/../../.." && pwd)"
DST="$REPO/client/Assets/Resources/UI/Casino"; TPL="$REPO/client/Assets/Resources/UI/Lobby/Icons/goal.png.meta"
mkdir -p "$DST/Symbols"
for id in rocket diamond_hands ape laser_eyes rug wild coin_heads coin_tails crash_rocket crash_rug rugroom_logo; do
  case $id in coin_*|crash_*|rugroom_logo) d="$DST" ;; *) d="$DST/Symbols" ;; esac
  case $id in rugroom_logo) sz=1024 ;; crash_rocket) sz=256 ;; *) sz=512 ;; esac
  cp "$HERE/out/png/$id.png" "$d/$id.png"
  if [ ! -f "$d/$id.png.meta" ]; then
    g="$(uuidgen | tr -d - | tr 'A-Z' 'a-z')"
    sed -e "s/^guid: .*/guid: $g/" -e "s/maxTextureSize: 256/maxTextureSize: $sz/" "$TPL" > "$d/$id.png.meta"
  fi
done
# the facade board (IslandV2Shells sign texture; layout.py sign_art keeps signs.py off it)
cp "$HERE/out/png/sign_rug_room.png" "$REPO/client/Assets/World/Island/Textures/Signs/sign_rug_room.png"
echo installed
