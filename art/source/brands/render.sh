#!/bin/sh
# Render each brand mark SVG (contract/ids/brands.json 'mark') to its 'mark_png' for Unity (transparent, 512 px wide).
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"; REPO="$(cd "$HERE/../../.." && pwd)"
CHROME="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
mkdir -p "$HERE/out"
python3 -c "
import json; j=json.load(open('$REPO/contract/ids/brands.json'))['brands']
for k,b in j.items(): print(b['mark'], b['mark_png'])" | while read svg png; do
  printf '<!doctype html><html><head><style>html,body{margin:0;background:transparent}svg{display:block;width:512px;height:430px}</style></head><body>%s</body></html>' "$(cat "$REPO/$svg")" > "$HERE/out/mark.html"
  mkdir -p "$(dirname "$REPO/$png")"
  "$CHROME" --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 --default-background-color=00000000 \
    --window-size=512,430 --screenshot="$REPO/$png" "file://$HERE/out/mark.html" >/dev/null 2>&1
  echo "$png"
done
