# /// script
# dependencies = ["pillow", "numpy"]
# ///
"""Preview a family atlas: albedo + tint masks rendered as false colour (8 regions)."""
import sys
import numpy as np
from PIL import Image
fam_dir, out = sys.argv[1], sys.argv[2]
fam = fam_dir.rstrip('/').split('/')[-1]
A = Image.open(f'{fam_dir}/{fam}_Albedo.png').convert('RGB').resize((1024, 1024))
m0 = np.asarray(Image.open(f'{fam_dir}/{fam}_TintMask0.png').convert('RGBA'), np.float32)[::2, ::2] / 255
m1 = np.asarray(Image.open(f'{fam_dir}/{fam}_TintMask1.png').convert('RGBA'), np.float32)[::2, ::2] / 255
cols = {(0, 0): (255, 200, 150), (0, 1): (120, 70, 30), (0, 2): (40, 90, 255), (0, 3): (40, 200, 60),
        (1, 0): (255, 255, 255), (1, 1): (255, 0, 200), (1, 2): (255, 230, 0), (1, 3): (0, 230, 230)}
F = np.zeros((1024, 1024, 3), np.float32)
for (k, ch), c in cols.items():
    m = (m0 if k == 0 else m1)[..., ch]
    F += m[..., None] * np.array(c, np.float32)
F = np.clip(F, 0, 255).astype(np.uint8)
sheet = Image.new('RGB', (2048, 1024))
sheet.paste(A, (0, 0)); sheet.paste(Image.fromarray(F), (1024, 0))
sheet.save(out, quality=88)
print(out)
