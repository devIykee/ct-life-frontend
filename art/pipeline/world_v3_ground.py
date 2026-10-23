"""CT Island ground (world polish, 2026-10-23): photo-scanned CC0 grass, soil and yard surfaces for the terrain shader.

The terrain's grass was a painted lime tint and its yards the sidewalk slab; this packs real scans into two texture
arrays the terrain shader (CT Life/World/Terrain) samples in world metres:

  slice  source (CC0)                                       use                               tile
  0      ambientCG Grass004 (Lennart Demes)                 lawn / meadow grass               1.6 m
  1      Poly Haven aerial_grass_rock (Rob Tuytel)          macro variation of the grass      18 m (and 61 m)
  2      Poly Haven forest_ground_04 (Rob Tuytel)           soil, worn verges, dirt           3.2 m
  3      Poly Haven concrete_floor_worn_001 (D. Savva, R. Cilliers)  The Trenches' yard slabs    3.0 m
  4      Poly Haven asphalt_02 (Rob Tuytel)                 The Trenches' patched asphalt     3.0 m

Licences: https://polyhaven.com/license ("Our assets are all licensed as CC0"), https://docs.ambientcg.com/license/
("All ambientCG assets are provided under the Creative Commons CC0 1.0 Universal License"), both checked 2026-10-23.

Writes client/Assets/World/Island/Textures/Ground/ground_albedo_array.png (5 x 1024 px columns; RGB = colour x
ambient occlusion, A = smoothness) and ground_normal_array.png (OpenGL normals); the Unity builder imports both as
Texture2DArray (5 columns). Raw downloads are cached under art/raw/ (gitignored).
Run: uv run --no-project --with numpy==2.4.2 --with pillow==12.3.0 python -I art/pipeline/world_v3_ground.py [fetch] [pack]
"""
import io, json, os, shutil, sys, urllib.request, zipfile
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
RAW_PH = os.path.join(REPO, 'art', 'raw', 'polyhaven')
RAW_ACG = os.path.join(REPO, 'art', 'raw', 'ambientcg')
OUT = os.path.join(REPO, 'client', 'Assets', 'World', 'Island', 'Textures', 'Ground')
UA = {'User-Agent': 'CTLife-artpipeline/1.0 (CC0 asset fetch)'}
PH = ['aerial_grass_rock', 'forest_ground_04', 'concrete_floor_worn_001', 'asphalt_02']
ACG = ['Grass004']
RES = 1024


def _get(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=120) as r:
        return r.read()


def fetch():
    for pid in PH:
        d = os.path.join(RAW_PH, 'tex_' + pid)
        os.makedirs(d, exist_ok=True)
        files = json.loads(_get('https://api.polyhaven.com/files/' + pid))
        for key, tag in (('Diffuse', 'diff'), ('nor_gl', 'nor_gl'), ('arm', 'arm')):
            p = os.path.join(d, f'{pid}_{tag}_1k.png')
            if not os.path.exists(p):
                print('[fetch] Poly Haven', pid, tag, flush=True)
                open(p, 'wb').write(_get(files[key]['1k']['png']['url']))
        mp = os.path.join(d, 'meta.json')
        if not os.path.exists(mp):
            inf = json.loads(_get('https://api.polyhaven.com/info/' + pid))
            json.dump(dict(id=pid, name=inf['name'], url=f'https://polyhaven.com/a/{pid}', authors=list(inf['authors']), licence='CC0',
                           dimensions_mm=inf.get('dimensions')), open(mp, 'w'), indent=1)
    for aid in ACG:
        d = os.path.join(RAW_ACG, aid)
        if os.path.exists(os.path.join(d, 'meta.json')):
            continue
        os.makedirs(d, exist_ok=True)
        print('[fetch] ambientCG', aid, flush=True)
        with zipfile.ZipFile(io.BytesIO(_get(f'https://ambientcg.com/get?file={aid}_1K-PNG.zip'))) as z:
            for n in z.namelist():
                if n.endswith('/') or os.path.basename(n) != n:
                    continue
                with z.open(n) as s, open(os.path.join(d, n), 'wb') as t:
                    shutil.copyfileobj(s, t)
        json.dump(dict(id=aid, url=f'https://ambientcg.com/view?id={aid}', author='ambientCG (Lennart Demes)', licence='CC0 1.0',
                       file=f'{aid}_1K-PNG.zip'), open(os.path.join(d, 'meta.json'), 'w'), indent=1)


def load(path, mode='RGB'):
    im = Image.open(path).convert(mode)
    if im.size != (RES, RES):
        im = im.resize((RES, RES), Image.Resampling.LANCZOS)
    return np.asarray(im, dtype=np.float32) / 255.0


def ph_set(pid):
    d = os.path.join(RAW_PH, 'tex_' + pid)
    diff = load(os.path.join(d, f'{pid}_diff_1k.png'))
    arm = load(os.path.join(d, f'{pid}_arm_1k.png'))
    nrm = load(os.path.join(d, f'{pid}_nor_gl_1k.png'))
    return diff, arm[..., 0:1], 1.0 - arm[..., 1:2], nrm


def acg_set(aid):
    d = os.path.join(RAW_ACG, aid)
    f = lambda k: os.path.join(d, f'{aid}_1K-PNG_{k}.png')
    diff = load(f('Color'))
    ao = load(f('AmbientOcclusion'), 'L')[..., None] if os.path.exists(f('AmbientOcclusion')) else np.ones((RES, RES, 1), np.float32)
    rough = load(f('Roughness'), 'L')[..., None]
    nrm = load(f('NormalGL'))
    return diff, ao, 1.0 - rough, nrm


def flatten_light(rgb, sigma_px=96):
    """Scans carry a little uneven lighting across the tile, which shows as a repeating blotch; divide out the very low
    frequencies (keeps the mean colour)."""
    from PIL import ImageFilter
    lo = np.stack([np.asarray(Image.fromarray((rgb[..., c] * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(sigma_px)), np.float32) / 255.0
                   for c in range(3)], -1)
    return np.clip(rgb * (rgb.reshape(-1, 3).mean(0) / np.maximum(lo, 1e-3)), 0, 1)


def pack():
    os.makedirs(OUT, exist_ok=True)
    sets = [acg_set('Grass004'), ph_set('aerial_grass_rock'), ph_set('forest_ground_04'), ph_set('concrete_floor_worn_001'), ph_set('asphalt_02')]
    albedo, normal = [], []
    for i, (diff, ao, smooth, nrm) in enumerate(sets):
        if i in (0, 2):
            diff = flatten_light(diff)
        if i == 0:
            smooth = 0.10 + 0.18 * smooth            # the scan's roughness reads glossy on a lawn: a matt blade sheen
        rgb = diff * (0.35 + 0.65 * ao)
        albedo.append(np.concatenate([rgb, smooth], 2))
        normal.append(nrm)
        lum = (rgb @ np.array([0.2126, 0.7152, 0.0722], np.float32)).mean()
        print(f'slice {i}: mean luminance {lum:.3f}, smoothness {smooth.mean():.2f}')
    A = np.concatenate(albedo, 1)
    N = np.concatenate(normal, 1)
    Image.fromarray(np.clip(A * 255 + 0.5, 0, 255).astype(np.uint8), 'RGBA').save(os.path.join(OUT, 'ground_albedo_array.png'), optimize=True)
    Image.fromarray(np.clip(N * 255 + 0.5, 0, 255).astype(np.uint8), 'RGB').save(os.path.join(OUT, 'ground_normal_array.png'), optimize=True)
    print('wrote', OUT)


if __name__ == '__main__':
    args = sys.argv[1:] or ['fetch', 'pack']
    if 'fetch' in args:
        fetch()
    if 'pack' in args:
        pack()
