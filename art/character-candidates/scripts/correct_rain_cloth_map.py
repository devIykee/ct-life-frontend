"""One-map Rain cloth correction; frozen FBXs/base/normal/rig stay untouched."""
import bpy
import hashlib
import json
import sys
from pathlib import Path
import numpy as np

root = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(root / 'scripts'))
from png_data_texture import write_rgba16

source = root / 'rain-adult-production'
out = root / 'rain-cloth-correction'
(out / 'exports').mkdir(parents=True, exist_ok=True)
old_gate = json.loads((source / 'bundle-ready.json').read_text())
for name, digest in old_gate['files'].items():
    assert hashlib.sha256((source / 'exports' / name).read_bytes()).hexdigest() == digest

bpy.ops.wm.open_mainfile(filepath=str(source / 'RainAdult_Unity.blend'), use_scripts=False)
mesh = bpy.data.objects['RainAdult_CreatorMesh']
mesh.data.calc_loop_triangles()
uv = mesh.data.uv_layers['AtlasUV'].data
ids = mesh.data.attributes['source_part_id'].data
size = 2048
owners = np.full((size, size), -1, dtype=np.int16)
for tri in mesh.data.loop_triangles:
    p = np.array([uv[i].uv[:] for i in tri.loops]) * size
    lo = np.maximum(np.floor(p.min(0)).astype(int), 0)
    hi = np.minimum(np.ceil(p.max(0)).astype(int), size - 1)
    u, v = p[1] - p[0], p[2] - p[0]
    den = u[0] * v[1] - u[1] * v[0]
    if abs(den) < 1e-10:
        continue
    x, y = np.meshgrid(np.arange(lo[0], hi[0] + 1) + .5,
                       np.arange(lo[1], hi[1] + 1) + .5)
    dx, dy = x - p[0, 0], y - p[0, 1]
    a, b = (dx*v[1]-dy*v[0])/den, (u[0]*dy-u[1]*dx)/den
    inside = (a >= -1e-6) & (b >= -1e-6) & (a+b <= 1+1e-6)
    owners[lo[1]:hi[1]+1, lo[0]:hi[0]+1][inside] = ids[tri.vertices[0]].value

scarf = owners == 12
mask = scarf.copy()
for _ in range(8):
    pad = np.pad(mask, 1)
    mask = np.logical_or.reduce([pad[y:y+size, x:x+size] for y in range(3) for x in range(3)])
# Include atlas gutter, but never alter another part's covered surface.
mask &= (owners == -1) | scarf
name = 'RainAdult_MetallicSmoothness.png'
old = bpy.data.images.load(str(source/'exports'/name), check_existing=False)
old.colorspace_settings.name = 'Non-Color'
before = np.array(old.pixels[:], dtype=np.float32).reshape(size, size, 4)
after = before.copy()
after[mask, 0] = 0  # Ordinary cloth is dielectric, not source's metallic approximation.
after[mask, 3] = np.minimum(after[mask, 3], .4)  # Retain texture; roughness >= .6.
quantized = write_rgba16(out/'exports'/name, after)
loaded = bpy.data.images.load(str(out/'exports'/name), check_existing=False)
loaded.colorspace_settings.name = 'Non-Color'
readback = np.array(loaded.pixels[:], dtype=np.float32).reshape(size, size, 4)

# Independent fresh-Blender readback also covers a nonzero metal + low smoothness.
probe = np.array([[[.4, 1., 0., .25]]], dtype=np.float32)
write_rgba16(out/'packed-channel-probe.png', probe)
probe_im = bpy.data.images.load(str(out/'packed-channel-probe.png'), check_existing=False)
probe_im.colorspace_settings.name = 'Non-Color'
probe_readback = np.array(probe_im.pixels[:])
checks = {
    '2048_rgba16_channel_readback': bool(np.max(np.abs(readback-quantized)) <= 1/65535),
    'scarf_metal_zero': bool(readback[scarf, 0].max() == 0),
    'scarf_smoothness_at_most_point4': bool(readback[scarf, 3].max() <= .40002),
    'outside_scarf_and_gutter_unchanged': bool(np.max(np.abs(readback[~mask]-before[~mask])) <= 1/65535),
    'green_blue_channels_unchanged': bool(np.max(np.abs(readback[:, :, 1:3]-before[:, :, 1:3])) <= 1/65535),
    'packed_alpha_does_not_change_rgb': bool(np.max(np.abs(probe_readback-probe.ravel())) <= 1/65535),
    'all_six_original_import_files_unchanged': all(hashlib.sha256((source/'exports'/n).read_bytes()).hexdigest()==h for n,h in old_gate['files'].items())
}
report = {'all_pass': all(checks.values()), 'checks': checks,
          'file': 'exports/'+name, 'sha256': hashlib.sha256((out/'exports'/name).read_bytes()).hexdigest(),
          'source_file_sha256': old_gate['files'][name], 'source_bundle_files': old_gate['files'],
          'method': 'Scarf atlas surface plus eight-pixel gutter only; metal=0, smoothness=min(existing,.4). Explicit straight RGBA16 data writer bypasses Blender opacity conversion.',
          'scarf_surface_pixels': int(scarf.sum()), 'surface_and_gutter_pixels': int(mask.sum()),
          'scarf_metal_before_min_median_max': np.percentile(before[scarf, 0], [0,50,100]).tolist(),
          'scarf_metal_after_min_median_max': np.percentile(readback[scarf, 0], [0,50,100]).tolist(),
          'scarf_smoothness_after_min_median_max': np.percentile(readback[scarf, 3], [0,50,100]).tolist(),
          'channel_probe_expected': probe.ravel().tolist(), 'channel_probe_readback': probe_readback.tolist(),
          'maximum_quantized_readback_delta': float(np.max(np.abs(readback-quantized))),
          'scope': 'One material data-map candidate. No rig, mesh, UV, base-colour, normal, LOD or idle changes. Unity/device visual review pending.'}
(out/'map-validation.json').write_text(json.dumps(report, indent=2)+'\n')
assert report['all_pass'], report
print(json.dumps(report, indent=2), flush=True)
