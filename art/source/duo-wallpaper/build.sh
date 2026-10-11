#!/bin/sh
# Renders wallpaper.html to the Duo wallpaper texture (1024x1536) with headless Chrome.
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="$HERE/../../../client/Assets/Resources/UI/PhoneOS/Wallpaper/Aurora.png"
"/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" --headless=new --disable-gpu --hide-scrollbars \
  --window-size=1024,1536 --screenshot="$OUT" "file://$HERE/wallpaper.html"
echo "wrote $OUT"
