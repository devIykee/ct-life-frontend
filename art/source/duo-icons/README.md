# Solana Duo app icons (iOS 26 style), 2026-10-16

Replaces the 2026-10-12 CoreGraphics set (`art/source/phoneos-icons/`). Studied against the real iOS 26.5 home screen in
the Xcode Simulator: light or saturated gradient tiles, continuous-corner squircle, bright inner rim, top sheen, and
large layered "glass" glyphs (gradient fill, specular top half, light edge, soft shadow).

- Glyph geometry: Phosphor Icons fill set v2.1.1 (MIT, `ph/LICENSE-phosphor.txt`). Custom art (Messages, Wallet, Maps,
  Perp Pit, Timeline, Settings gear, Pulse, ORE, Profile, Week) and all materials are original.
- No Apple icon, SF Symbol or Apple font is used. "WEEK" is set in Inter (OFL).

Regenerate:

```sh
python3 art/source/duo-icons/build.py            # writes icons.html + layout.json next to it
"/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" --headless=new --disable-gpu --hide-scrollbars \
  --force-device-scale-factor=1 --default-background-color=00000000 --virtual-time-budget=5000 \
  --window-size=1704,1424 --screenshot=sheet.png "file://$PWD/art/source/duo-icons/icons.html"
# then crop each 256x256 cell (layout.json: 6 columns, 24 px gap) into client/Assets/Resources/UI/PhoneOS/Icons/<id>.png
```

Keep the existing `.meta` files (GUIDs). Icons are 256×256 RGBA with transparent corners.
