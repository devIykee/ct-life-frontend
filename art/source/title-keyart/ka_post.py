"""Composite + grade for key art B. Pure Python (uv), no Blender needed after the render.

  uv run --with OpenEXR --with opencolorio --with numpy --with pillow --with scipy \
      python -I ka_post.py <passes.exr> <plate.png> <out-dir> [--tag NAME] [--check]

Steps: Cycles linear passes -> AgX (Blender's own OCIO config, so it matches the Blender viewport)
-> contact shadows multiplied into the Unity clean plate in linear light -> characters over ->
grade (contrast, saturation, split tone) -> bloom -> vignette. Writes:
  keyart-B.png (3840x1726), keyart-B-2670.png (2670x1200),
  layers/characters.png (RGBA, graded, no bloom/vignette), layers/background.png (graded plate +
  contact shadows + vignette), and with --check keyart-B-check.png (logo / bar / legal mock-up).
"""
import sys
from pathlib import Path

import numpy as np
import OpenEXR
import PyOpenColorIO as ocio
from PIL import Image, ImageDraw, ImageFont
from scipy.ndimage import gaussian_filter

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
OCIO_CFG = '/Applications/Blender.app/Contents/Resources/5.1/datafiles/colormanagement/config.ocio'

G = dict(
    view='Khronos PBR Neutral', look='', exposure=-0.05,               # character tone-map
    shadow_strength=0.85,                             # contact shadow density on the plate
    plate_blur=2.0,                                   # extra softening of the Unity plate (px at 3840)
    plate_exposure=-0.12,
    contrast=1.06, saturation=1.10,
    shadows_tint=(0.96, 0.98, 1.05), highlights_tint=(1.04, 1.0, 0.95),
    bloom_threshold=0.78, bloom_strength=0.22, bloom_sizes=(6, 18, 48), bloom_tint=(1.0, 0.88, 0.74),
    vignette=0.45, vignette_tint=(0.42, 0.30, 0.26),
)


def srgb_to_lin(x):
    return np.where(x <= 0.04045, x / 12.92, ((x + 0.055) / 1.055) ** 2.4)


def lin_to_srgb(x):
    x = np.clip(x, 0, None)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * x ** (1 / 2.4) - 0.055)


def read_exr(path):
    """Blender 5 writes multilayer EXR as multi-part files: one part per pass."""
    ch = {}
    with OpenEXR.File(str(path), separate_channels=True) as f:
        for part in f.parts:
            for k, v in part.channels.items():
                ch[k] = v.pixels.astype(np.float32)

    def get(layer):
        return {k.split('.')[-1]: v for k, v in ch.items() if k.split('.')[-2] == layer}
    comb = get('Combined')
    shad = get('Shadow Catcher')
    rgba = np.dstack([comb['R'], comb['G'], comb['B'], comb['A']])
    sh = np.dstack([shad['R'], shad['G'], shad['B']]) if shad else np.ones(rgba.shape[:2] + (3,), np.float32)
    return rgba, sh, sorted(ch)


def tonemap(rgb, view, look, exposure):
    cfg = ocio.Config.CreateFromFile(OCIO_CFG)
    t = ocio.DisplayViewTransform(src='Linear Rec.709', display='sRGB', view=view)
    x = (rgb * 2 ** exposure).astype(np.float32).copy()
    vp = ocio.LegacyViewingPipeline()
    vp.setDisplayViewTransform(t)
    if look:
        vp.setLooksOverrideEnabled(True)
        vp.setLooksOverride(look)
    proc = vp.getProcessor(cfg).getDefaultCPUProcessor()
    flat = x.reshape(-1, 3)
    proc.applyRGB(flat)
    return np.clip(flat.reshape(rgb.shape), 0, 1)


def grade(img, g):
    lum = (img * [0.2126, 0.7152, 0.0722]).sum(-1, keepdims=True)
    img = lum + (img - lum) * g['saturation']
    img = 0.5 + (img - 0.5) * g['contrast']
    w = np.clip(lum, 0, 1)
    img = img * (np.array(g['shadows_tint']) * (1 - w) + np.array(g['highlights_tint']) * w)
    return np.clip(img, 0, 1)


def bloom(img, g):
    lum = (img * [0.2126, 0.7152, 0.0722]).sum(-1)
    m = np.clip((lum - g['bloom_threshold']) / (1 - g['bloom_threshold']), 0, 1)[..., None] * img
    acc = np.zeros_like(img)
    scale = img.shape[1] / 3840
    for s in g['bloom_sizes']:
        acc += gaussian_filter(m, sigma=(s * scale, s * scale, 0))
    acc = acc / len(g['bloom_sizes']) * g['bloom_strength'] * np.array(g['bloom_tint'])
    return 1 - (1 - img) * (1 - np.clip(acc, 0, 1))      # screen


def vignette(img, g):
    h, w = img.shape[:2]
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    nx, ny = (x - w / 2) / (w / 2), (y - h / 2) / (h / 2)
    r = np.sqrt((nx * 0.85) ** 2 + ny ** 2)
    v = np.clip((r - 0.55) / 0.75, 0, 1) ** 1.6 * g['vignette']
    v = v[..., None]
    return img * (1 - v + v * np.array(g['vignette_tint']))


def check_overlay(img_u8):
    """Title-screen mock-up: CT mark + LIFE (Lilita One), loading bar, Tap to play, legal line."""
    im = Image.fromarray(img_u8).convert('RGBA')
    W, H = im.size
    ov = Image.new('RGBA', im.size, (0, 0, 0, 0))
    mark = Image.open(REPO / 'art/branding/logo/ct-mark-transparent-1254.png').convert('RGBA')
    mh = int(H * 0.20)
    mark = mark.resize((int(mark.width * mh / mark.height), mh), Image.LANCZOS)
    font = ImageFont.truetype(str(REPO / 'client/Assets/Resources/UI/Fonts/LilitaOne-Regular.ttf'), int(H * 0.17))
    d = ImageDraw.Draw(ov)
    tw = d.textbbox((0, 0), 'LIFE', font=font)
    total = mark.width + int(W * 0.012) + (tw[2] - tw[0])
    x0 = (W - total) // 2
    cy = int(H * 0.47)
    ov.alpha_composite(mark, (x0, cy - mh // 2))
    tx = x0 + mark.width + int(W * 0.012)
    ty = cy - (tw[3] + tw[1]) // 2
    stroke = max(4, H // 90)
    d.text((tx, ty), 'LIFE', font=font, fill=(255, 255, 255, 255), stroke_width=stroke, stroke_fill=(46, 16, 78, 255))
    small = ImageFont.truetype(str(REPO / 'client/Assets/Resources/UI/Fonts/LilitaOne-Regular.ttf'), int(H * 0.028))
    t = 'TAP TO PLAY'
    b = d.textbbox((0, 0), t, font=small)
    d.text(((W - (b[2] - b[0])) // 2, int(H * 0.835)), t, font=small, fill=(255, 255, 255, 235))
    bw, bh = int(W * 0.27), max(6, H // 120)
    d.rounded_rectangle(((W - bw) // 2, int(H * 0.885), (W + bw) // 2, int(H * 0.885) + bh), radius=bh // 2, fill=(255, 255, 255, 230))
    legal = ImageFont.truetype(str(REPO / 'client/Assets/Resources/UI/Fonts/LilitaOne-Regular.ttf'), int(H * 0.018))
    t = '18+ only · Cash is game money and cannot be withdrawn'
    b = d.textbbox((0, 0), t, font=legal)
    d.text(((W - (b[2] - b[0])) // 2, int(H * 0.955)), t, font=legal, fill=(255, 255, 255, 200))
    # guides: logo safe zone (middle 40% x 45%) and calm bottom band (22%)
    g = Image.new('RGBA', im.size, (0, 0, 0, 0))
    gd = ImageDraw.Draw(g)
    gd.rectangle((int(W * 0.3), int(H * 0.275), int(W * 0.7), int(H * 0.725)), outline=(0, 255, 255, 110), width=2)
    gd.line((0, int(H * 0.78), W, int(H * 0.78)), fill=(0, 255, 255, 90), width=2)
    return Image.alpha_composite(Image.alpha_composite(im, ov), g).convert('RGB'), Image.alpha_composite(im, ov).convert('RGB')


def main():
    exr, plate_path, out = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
    tag = sys.argv[sys.argv.index('--tag') + 1] if '--tag' in sys.argv else 'keyart-B'
    out.mkdir(parents=True, exist_ok=True)
    rgba, shadow, names = read_exr(exr)
    h, w = rgba.shape[:2]
    plate = Image.open(plate_path).convert('RGB')
    if plate.size != (w, h):
        plate = plate.resize((w, h), Image.LANCZOS)
    p = np.asarray(plate).astype(np.float32) / 255
    if G['plate_blur']:
        p = gaussian_filter(p, sigma=(G['plate_blur'] * w / 3840,) * 2 + (0,))
    pl = srgb_to_lin(p) * 2 ** G['plate_exposure']
    s = np.clip(shadow, 0, 1)
    pl = pl * (1 - G['shadow_strength'] * (1 - s))
    bg = lin_to_srgb(pl)
    a = np.clip(rgba[..., 3:4], 0, 1)
    straight = np.where(a > 1e-4, rgba[..., :3] / np.maximum(a, 1e-4), 0)
    ch = tonemap(straight, G['view'], G['look'], G['exposure'])
    comp = ch * a + bg * (1 - a)
    comp = grade(comp, G)
    comp = bloom(comp, G)
    comp = vignette(comp, G)
    u8 = (np.clip(comp, 0, 1) * 255 + 0.5).astype(np.uint8)
    Image.fromarray(u8).save(out / f'{tag}.png', optimize=True)
    Image.fromarray(u8).resize((2670, 1200), Image.LANCZOS).save(out / f'{tag}-2670.png', optimize=True)
    lay = out / 'layers'
    lay.mkdir(exist_ok=True)
    chg = grade(ch, G)
    Image.fromarray(np.dstack([(chg * 255 + 0.5).astype(np.uint8), (a[..., 0] * 255 + 0.5).astype(np.uint8)]), 'RGBA').save(lay / 'characters.png', optimize=True)
    bgg = vignette(grade(bg, G), G)
    Image.fromarray((np.clip(bgg, 0, 1) * 255 + 0.5).astype(np.uint8)).save(lay / 'background.png', optimize=True)
    if '--check' in sys.argv:
        guides, clean = check_overlay(np.asarray(Image.fromarray(u8).resize((2670, 1200), Image.LANCZOS)))
        guides.save(out / f'{tag}-check-guides.png')
        clean.save(out / f'{tag}-check.png')
    print('post ok', out, w, h, 'channels', len(names))


if __name__ == '__main__':
    main()
