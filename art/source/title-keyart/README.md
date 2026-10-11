# Title key art B ("Close duo"), Blender

The title-screen key art. It renders our real cast characters in Blender 5.1 Cycles and composites them over a clean Unity render of the shipped Timeline Square. There is no AI image generation and no paint-over.

- Left: `head_player_f_kol` (skin_tan), the phone hold. Right: `head_player_m_kol` (skin_dark), the arms-up win. Both looks are named in
  `ct-life-evidence/title-keyart-20261016/keyart-receipt.json`, under "B".
- Outputs go to `ct-life-evidence/title-keyart-20261016/blender/`:
  - `keyart-B.png`: 3840×1726.
  - `keyart-B-2670.png`: 2670×1200.
  - `layers/characters.png`: RGBA, graded. It has no bloom and no vignette.
  - `layers/background.png`: the graded plate, with contact shadows and vignette.
  - `keyart-B-check.png`: mock-up with the logo, LIFE (Lilita One), the bar, Tap to play and the legal line. `keyart-B-check-guides.png` is the same mock-up with the logo box and the bottom-22% guide drawn on.
  - `render/keyart-B-passes.exr`: Cycles passes (Combined plus Shadow Catcher).
  - `unity-bg/bg-dof.png`: the Unity backdrop.

## Files
| File | What it does |
| --- | --- |
| `ka_params.py` | All art-direction numbers: camera, placement, poses, smile, lights. Tune here. |
| `ka_build.py` | Builds the scene and renders the passes. It imports both hero FBXs, sets materials and fixes, poses, adds the phone, grounds the feet, and sets up the camera, lights and Cycles. |
| `ka_pose.py` | Hand-authored FK and analytic two-bone IK posing on the CTS rig. Hinge-aware: the elbows and knees fold on their real axes. |
| `ka_look.py` | Game material inputs: BaseColor, Normal, MetallicSmoothness (R = metallic, A = smoothness, as in Unity), plus a skin mask from TintMask0 R. Also the fixes: glossy eyes, clear lenses, shades, boot lining and the smile edit. |
| `ka_props.py` | Original phone prop: 72×151×8 mm rounded slab, glass screen with a 1.6 mm bezel and a faint glow, two-lens camera island. Parented to `Hand.R`. |
| `ka_common.py` | Paths, Unity-to-Blender axes (Unity (x,y,z) is Blender (x,z,y)), and the posing helpers. |
| `ka_post.py` | Composite and grade in plain Python. It uses Blender's own OCIO config, so the tone curve matches Blender. |
| `keyart-B.blend` | The built scene, saved before rendering (about 1.2 MB, textures referenced from `art/cast/export/looks`). |

## Re-render
```sh
cd art/source/title-keyart
# 1. Clean backdrop (Unity batchmode, about 1 min; close the editor first). Rewrites blender/unity-bg/.
/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath ../../../client \
  -buildTarget Android -executeMethod CTLife.EditorTools.TitleKeyArtCapture.Run \
  -ctlife-keyart "$PWD/../../../client/Tools~/keyart/title-keyart-blender-bg.json" \
  -ctlife-captureOut "$PWD/../../../../ct-life-evidence/title-keyart-20261016/blender/unity-bg" -logFile /tmp/keyart-bg.log
# 2. Characters and passes (Cycles, 512 samples, adaptive, OIDN: about 10 min on an M-series Mac)
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -P ka_build.py -- --samples 512 --save-blend "$PWD/keyart-B.blend"
#    quick look: add --preview (1280x575, 48 spp, about 10 s), or --res 3840x1726 --samples 64
#    review close-ups at the same perspective: --closeup mhead|fhead|phone|mfistL|mfistR|fhip|ffeet|mfeet
# 3. Composite, grade and the check mock-up (about 45 s)
E=../../../../ct-life-evidence/title-keyart-20261016/blender
uv run --with OpenEXR --with opencolorio --with numpy --with pillow --with scipy python -I ka_post.py \
  $E/render/keyart-B-passes.exr $E/unity-bg/bg-dof.png $E --check
```
The Blender camera matches the Unity camera exactly. The position and target come from the same JSON numbers. The vertical sensor is 36 mm / (2670/1200), as in `TitleKeyArtCapture`. So the shadow-catcher plane at y = 0 sits on the paving. The paving top is y = 0: the venue builder puts decals at +0.004.

If you change `CAMERA` in `ka_params.py`, change `client/Tools~/keyart/title-keyart-blender-bg.json` too and re-run step 1.

## Look decisions
- **Light.** The sun is the key. It is warm (1.0, 0.72, 0.46), comes from camera-left-high (Unity euler 30/40/0), and the Unity backdrop uses the same direction. A soft warm beauty key comes from camera-left. There is a weak warm fill from camera-right. Each character gets two cool rim lights from behind. The phone screen throws a cool glow. Light linking keeps the rims, fill, beauty key and phone glow off the ground, so the shadow-catcher pass only holds shadows. A small overhead light per character reaches only the paving; it gives soft contact occlusion under the feet.
- **Tone.** Khronos PBR Neutral, which keeps saturation like the game's URP Neutral tonemap. Then mild contrast and saturation, a cool-shadow/warm-highlight split, a warm bloom on highlights, and a warm vignette. The plate gets an extra 2 px blur and is pulled down 0.12 EV, so the characters pop.
- **Fixes over the game render.**
  - Cycles draws back faces, which closes the hem and cuff gaps that URP back-face culling leaves.
  - The eyes get a wet clear-coat, so they show a catchlight over the painted iris and pupil.
  - The m_kol glasses lenses become clear glass with a Fresnel glint. In Unity the lens faces point inward and are culled, so they read as clear there too.
  - The f_kol shades become glossy dark lenses.
  - The f_kol boot shafts are open tubes; the shin inside them is painted as dark lining.
  - Both mouths get smoothly lifted corners (a smirk on her, a grin on him). These heads have no blend shapes, so this is a small rest-mesh edit, made only in the key-art scene.
  - The phone is a new prop, replacing the grey slab.
  - The feet are grounded on the paving, like the Unity tool's grounding.

## Licences
- The characters are the project's Rain and Snow derivatives. They are **CC-BY 4.0**; the credit is "Rain Rig / Snow Rig (CC) Blender Foundation | studio.blender.org" (see `art/LICENSES.md`).
- The phone, the materials and the scripts are ours (CC0).
- The backdrop is our own Timeline Square venue, rendered by our editor tool.
- The Lilita One font (OFL, already in the repo) is used only in the check mock-up.
- The Python packages (OpenEXR, OpenColorIO, numpy, scipy, Pillow) are build tools only. They are not shipped.
- Rows for the key art and the phone are in `art/LICENSES.md` (Title screen table).
