# Lookdev agent: progress log

Folder: `art/lookdev/` only. Goal: "target look" key art for CT Life (Sims 4-style stylized realism + CT parody).

## Status 2026-10-05: DONE (v1 target look)

Deliverables (all in `art/lookdev/`):
- `timeline-square-keyart.png`: 2400×1350 hero, Timeline Square at golden hour.
- `timeline-square-phone.png`: 1200×2670 (Seeker aspect), closer "in-game" camera + mock HUD (top bar: 16:20 / Mood: Bullish / online 1,204 / Cash $3,000 [+];
  toast "JUST IN: $4.2M longs liquidated"; "gm ser" bubble; bottom bar Map / Phone / Mic / Camera).
- `terminal-cafe.png`: 2400×1350 Sims-style cut-away interior.
- `timeline_square.blend`, `terminal_cafe.blend` (~11 MB each; textures referenced from `src/`, nothing packed).
- Scripts: `fetch_assets.py`, `make_textures.py`, `lookdev_common.py`, `timeline_square.py`, `terminal_cafe.py`, `post.py`.
- `LICENSES.md`: every asset, source URL, author, licence (CC0 / OFL / original), date.

## Reproduce (from repo root)
```
python3 art/lookdev/fetch_assets.py                         # ~1 GB into art/lookdev/src (gitignored)
python3 -m venv art/lookdev/.venv && art/lookdev/.venv/bin/pip install Pillow==12.3.0
art/lookdev/.venv/bin/python art/lookdev/make_textures.py   # screens/billboards/neon -> art/lookdev/tex/
B=/Applications/Blender.app/Contents/MacOS/Blender
$B -b -P art/lookdev/timeline_square.py -- --shot both      # ~3 min, M2 Pro, Eevee; add --preview for half-res
$B -b -P art/lookdev/terminal_cafe.py                       # ~1 min
art/lookdev/.venv/bin/python art/lookdev/post.py            # grade + HUD -> final PNGs
```
`fetch_assets.py` also unzips the two itch.io packs.

## What was done / decisions
- Tooling verified: Blender 5.1.2 (local), Pillow 12.3.0 (PyPI latest, 2026-07-01). Engine: Eevee only (Cycles not needed).
- Licences checked on live pages 2026-10-05 (Poly Haven /license; Quaternius pack pages + itch.io "CC0 License"
  + bundled License.txt). Only free tiers; downloads need no login (itch "just download" endpoint, public Drive folders).
- Sims look recipe: AgX "Punchy", warm sun 26° elevation from front-left + clamped HDRI sky, warm-sand paving,
  saturated outfits, smoothed (subsurf L1) Quaternius posed humans scaled to ~1.85 m, pink blossom trees
  (Quaternius TwistedTree leaves remapped to a pink ramp), Pillow post: bloom, top haze, light leak, vignette.
- Dubai towers: procedural boxes/twists/setbacks with a curtain-wall shader (floor slabs + mullions gated by normal,
  per-floor tint noise, camera-distance haze).
- Iterations: square ~8 render→review passes (fixed: plaza in shadow, hidden towers, washed-out glass, tiny crowd,
  stray siren hat at origin (stale matrix_world), bench orientation, billboard crops); café 5 passes (fixed: dark
  room, trees blocking window sun, lamp shades hiding neon, Eevee shadow-pool overflow -> pool 1024 MB).
- .blend size: the glTF importer packed 65 MB of textures; `L.save_blend()` unpacks/relinks so blends are ~11 MB.

## Gotchas for the next person
- Quaternius glTF materials multiply albedo by a vertex-colour wear mask -> red patches outside their engine shader.
  `L.fix_vertex_color_mix()` rewires texture -> Base Color.
- Quaternius posed humans face -Y; hair is separate. Head = largest "Skin" island in the upper half (see `_head_of`).
  `Standing_CoveringEyes` (female) gets no hair (hands merge with the head island).
- Poly Haven `painted_wooden_bench` is a pew: seat faces -Y at rot 0.
- New objects' `matrix_world` is stale until `bpy.context.view_layer.update()`.

## Scene stats (measured in the saved .blends)
- Timeline Square: 2.71 M triangles (whole scene incl. off-screen city), 535 mesh instances, 42 people, 2 lights + emissive screens.
- Terminal Café: 0.85 M triangles, 264 mesh instances, 15 people, 13 lights + volumetric haze.

## Not achievable as-is on the Seeker (vs spec "Performance budgets") — see final report
- [Inference] Triangle counts are ~20× (square) and ~6× (café) the 100–150k visible budget; the skyline needs
  impostors/skybox cards and the Quaternius modules need LODs + atlasing.
- Raytraced screen-space reflections, soft area/point-light shadows, 13 dynamic café lights, volumetric light
  shafts, subsurface skin, DOF and the Pillow bloom/haze grade are offline-only. In-engine: 1 directional light,
  baked lightmaps (golden-hour bake), blob shadows (so no long character shadows), bloom without HQ filtering,
  colour grading LUT, fog for haze, emissive screens (cheap).
- Characters are static posed meshes (no rig). Game needs the shared Humanoid skeleton + modular outfits from the spec.

## Next (if asked)
- Rigged CC0 character base (e.g. Quaternius Universal Base Characters free tier: 2 bodies + 5 hairstyles, CC0) +
  modular clothing to replace posed placeholders.
- A "mobile-budget" variant render: baked lighting, 1 sun, blob shadows, skyline cards, to show the real in-engine look.
- Pink blossom: a proper CC0 cherry tree (current one is a recoloured stylized tree).
