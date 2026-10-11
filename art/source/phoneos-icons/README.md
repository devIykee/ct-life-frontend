# CTOS app icons

Original layered artwork authored for CT Life using native macOS CoreGraphics. No Apple icon, SF Symbol, external SVG, glyph font or downloaded illustration is embedded in the output.

Regenerate to a scratch directory:

```sh
swift art/source/phoneos-icons/render-icons.swift /tmp/ctos-icons
```

This writes thirty 192×192 RGBA PNGs: all twenty-nine current PhoneCatalog routes plus a neutral fallback (`app`). It also writes large and 64px light/dark review sheets. Copy only the individual icon PNGs into `client/Assets/Resources/UI/PhoneOS/Icons`; keep contact sheets outside Assets. Preserve existing `.meta` GUIDs when replacing art.

Textures have their own continuous-corner enclosure and material rendering. Use `PhoneAppIcon(app.Id)` at the root of an app tile; do not tint or place them inside an opaque colored UiIcon container. Runtime assets are static, without blur shaders or per-frame painting. Android import uses ASTC 4×4, maximum 256, no mipmaps, transparent outer corners.

Reference research and limitations: `docs/research/ctos-app-icons-20261012.md`.
