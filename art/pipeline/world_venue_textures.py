"""World fix-ups (2026-10-23): CC0 Poly Haven surface scans for the venue architecture (art/pipeline/world_venue_shells.py:
the CHECKPOINT 26 convention centre, the Perp Pit trading floor, the Basecamp Lodge and the boardwalk beach hut).

  key            Poly Haven id          use
  concrete       brushed_concrete       the convention centre's panels, plinths, the Perp Pit's base
  timber_dark    planks_brown_10        the lodge's weathered board cladding and beams
  timber_light   raw_plank_wall         the beach hut's pine boards
  wood_post      wood_planks_dirt       posts, rafters, the hut's deck
  slate          roof_slates_02         the lodge's slate roof
  thatch         reed_roof_04           the beach hut's reed roof
  stone          rustic_stone_wall      the lodge's stone plinth and chimney

Writes client/Assets/World/Island/Textures/Venues/<key>_albedo.png (RGB colour x AO), <key>_normal.png (OpenGL) and
<key>_mask.png (the Kit PBR mask: R metallic 0, G occlusion, A smoothness), 1024 px. Licence: https://polyhaven.com/license
("Our assets are all licensed as CC0", checked 2026-10-23). Downloads go through ph_fetch.py (https allowlist).
Run: uv run --no-project --with numpy==2.4.2 --with pillow==12.3.0 python -I art/pipeline/world_venue_textures.py
"""
import os, sys
import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import ph_fetch  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
RAW_PH = os.path.join(REPO, 'art', 'raw', 'polyhaven')
OUT = os.path.join(REPO, 'client', 'Assets', 'World', 'Island', 'Textures', 'Venues')
SETS = {'concrete': 'brushed_concrete', 'timber_dark': 'planks_brown_10', 'timber_light': 'raw_plank_wall', 'wood_post': 'wood_planks_dirt',
        'slate': 'roof_slates_02', 'thatch': 'reed_roof_04', 'stone': 'rustic_stone_wall'}
RES = 1024


def load(path, mode='RGB'):
    im = Image.open(path).convert(mode)
    if im.size != (RES, RES):
        im = im.resize((RES, RES), Image.Resampling.LANCZOS)
    return np.asarray(im, dtype=np.float32) / 255.0


def flatten_light(rgb, sigma_px=128):
    lo = np.stack([np.asarray(Image.fromarray((rgb[..., c] * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(sigma_px)), np.float32) / 255.0
                   for c in range(3)], -1)
    return np.clip(rgb * (rgb.reshape(-1, 3).mean(0) / np.maximum(lo, 1e-3)), 0, 1)


def main():
    os.makedirs(OUT, exist_ok=True)
    for key, pid in SETS.items():
        d = ph_fetch.ph_texture(pid, RAW_PH)
        diff = flatten_light(load(os.path.join(d, f'{pid}_diff_1k.png')))
        arm = load(os.path.join(d, f'{pid}_arm_1k.png'))
        nrm = load(os.path.join(d, f'{pid}_nor_gl_1k.png'))
        ao, rough = arm[..., 0:1], arm[..., 1:2]
        rgb = diff * (0.5 + 0.5 * ao)
        mask = np.concatenate([np.zeros_like(ao), ao, np.zeros_like(ao), 1.0 - rough], 2)
        save = lambda a, n, m: Image.fromarray(np.clip(a * 255 + 0.5, 0, 255).astype(np.uint8), m).save(os.path.join(OUT, f'{key}_{n}.png'), optimize=True)
        save(rgb, 'albedo', 'RGB'); save(nrm, 'normal', 'RGB'); save(mask, 'mask', 'RGBA')
        print(key, pid, 'mean', rgb.reshape(-1, 3).mean(0).round(3), 'smooth', round(float((1 - rough).mean()), 2))


if __name__ == '__main__':
    main()
