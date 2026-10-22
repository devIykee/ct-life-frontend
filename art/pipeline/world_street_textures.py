"""CT Island street surfaces (2026-10-22): CC0 Poly Haven PBR scans packed for the island's road and ground shaders.

Sources (1k PNG, cached under art/raw/polyhaven/tex_<id>/, gitignored; all CC0, https://polyhaven.com/license):
  asphalt_02               Rob Tuytel                           roads (3.0 m a tile)
  concrete_pavement_02     Charlotte Baglioni                   sidewalks and downtown forecourts (1.8 m: 0.45 m slabs)
  concrete_floor_worn_001  Dimitrios Savva, Rico Cilliers       kerbs, bridge and road skirts (3.0 m)
  granite_tile_04          Amal Kumar                           plazas and squares (2.0 m)
  painted_plaster_wall     Amal Kumar                           venue shells' walls (2.0 m), tinted by their palette
  clay_roof_tiles          Amal Kumar                           venue shells' roofs (4.0 m), tinted by their palette

Writes client/Assets/World/Island/Textures/Street/<name>_albedo.png (RGB = colour x ambient occlusion, A = smoothness =
1 - roughness) and <name>_normal.png (OpenGL normal, as published), 1024 px.
Run: uv run --no-project --with numpy==2.4.2 --with pillow==12.3.0 python -I art/pipeline/world_street_textures.py
"""
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
RAW = os.path.join(REPO, 'art', 'raw', 'polyhaven')
OUT = os.path.join(REPO, 'client', 'Assets', 'World', 'Island', 'Textures', 'Street')
SETS = {
    'asphalt': 'asphalt_02',
    'pavers': 'concrete_pavement_02',
    'concrete': 'concrete_floor_worn_001',
    'granite': 'granite_tile_04',
    'plaster': 'painted_plaster_wall',
    'rooftiles': 'clay_roof_tiles',
}


def load(path):
    return np.asarray(Image.open(path).convert('RGB'), dtype=np.float32) / 255.0


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, pid in SETS.items():
        d = os.path.join(RAW, 'tex_' + pid)
        diff = load(os.path.join(d, f'{pid}_diff_1k.png'))
        arm = load(os.path.join(d, f'{pid}_arm_1k.png'))            # R ambient occlusion, G roughness, B metal
        ao = arm[..., 0:1]
        rgb = diff * (0.35 + 0.65 * ao)                              # cavities darker, not black
        smooth = 1.0 - arm[..., 1:2]
        out = np.concatenate([rgb, smooth], axis=2)
        Image.fromarray(np.clip(out * 255 + 0.5, 0, 255).astype(np.uint8), 'RGBA').save(os.path.join(OUT, f'{name}_albedo.png'), optimize=True)
        Image.open(os.path.join(d, f'{pid}_nor_gl_1k.png')).convert('RGB').save(os.path.join(OUT, f'{name}_normal.png'), optimize=True)
        lum = (rgb @ np.array([0.2126, 0.7152, 0.0722], np.float32)).mean()
        print(f'{name:9s} <- {pid:24s} mean linear-ish luminance {lum:.3f}, smoothness {smooth.mean():.2f}')


if __name__ == '__main__':
    main()
