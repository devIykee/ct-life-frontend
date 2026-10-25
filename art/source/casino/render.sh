#!/bin/sh
# Render the Rug Room art pages (build.py) to transparent PNGs in out/png/ with headless Chrome.
#   art/source/casino/render.sh [id ...]
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
CHROME="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
python3 "$HERE/build.py" "$@" >/dev/null
mkdir -p "$HERE/out/png"
IDS="$*"; [ -n "$IDS" ] || IDS="$(python3 -c "import json;print(' '.join(json.load(open('$HERE/out/sizes.json'))))")"
for id in $IDS; do
  WH="$(python3 -c "import json;w,h=json.load(open('$HERE/out/sizes.json'))['$id'];print(f'{w},{h}')")"
  "$CHROME" --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 --default-background-color=00000000 \
    --virtual-time-budget=6000 --window-size="$WH" --screenshot="$HERE/out/png/$id.png" "file://$HERE/out/html/$id.html" >/dev/null 2>&1
  echo "$id $WH"
done
