#!/bin/sh
# Copy the rendered Rekt Burger art into Resources/UI/RektBurger (keeps existing .meta GUIDs; new files get the casino's
# UI texture meta: no mipmaps, alpha is transparency). The facade board goes to the island sign textures.
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"; REPO="$(cd "$HERE/../../.." && pwd)"
DST="$REPO/client/Assets/Resources/UI/RektBurger"; TPL="$REPO/client/Assets/Resources/UI/Lobby/Icons/goal.png.meta"
mkdir -p "$DST"
for id in logo bun_top bun_bottom patty cheese lettuce tomato onion pickle fries drink burger_done ticket_bg tray sticker_perfect sticker_wrong; do
  case $id in logo) sz=1024 ;; ticket_bg|tray) sz=512 ;; sticker_*|*) sz=256 ;; esac
  cp "$HERE/out/png/$id.png" "$DST/$id.png"
  if [ ! -f "$DST/$id.png.meta" ]; then
    g="$(uuidgen | tr -d - | tr 'A-Z' 'a-z')"
    sed -e "s/^guid: .*/guid: $g/" -e "s/maxTextureSize: 256/maxTextureSize: $sz/" "$TPL" > "$DST/$id.png.meta"
  fi
done
SIGN="$REPO/client/Assets/World/Island/Textures/Signs"
cp "$HERE/out/png/sign_rekt_burger.png" "$SIGN/sign_rekt_burger.png"
if [ ! -f "$SIGN/sign_rekt_burger.png.meta" ] && [ -f "$SIGN/sign_rug_room.png.meta" ]; then
  g="$(uuidgen | tr -d - | tr 'A-Z' 'a-z')"; sed -e "s/^guid: .*/guid: $g/" "$SIGN/sign_rug_room.png.meta" > "$SIGN/sign_rekt_burger.png.meta"
fi
echo installed
