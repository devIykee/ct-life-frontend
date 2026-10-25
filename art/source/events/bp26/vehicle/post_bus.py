"""After world_v3_process.py: the bus's baked paint (a dull maroon from the source's dark red factor) becomes a London
bus red in its atlas tile. Run: art/pipeline/blender.sh art/source/events/bp26/vehicle/post_bus.py
Edits art/processed/world_v3/v3_events/textures/bp26_bus_BaseColor.png in place (only pixels near the paint colour in the
lower-left quadrant, the paint's tile)."""
import bpy, os
import numpy as np
HERE = os.path.dirname(os.path.abspath(__file__))
P = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..', 'processed', 'world_v3', 'v3_events', 'textures', 'bp26_bus_BaseColor.png'))
im = bpy.data.images.load(P)
w, h = im.size
a = np.array(im.pixels[:], np.float32).reshape(h, w, 4)        # rows bottom-up; the image's pixels are in its colour space
q = a[0:h // 2, 0:w // 2]                                        # lower-left quadrant as stored = bottom rows in Blender
ref = np.median(q[h // 8:h // 4, w // 8:w // 4, :3].reshape(-1, 3), axis=0)
d = np.linalg.norm(q[..., :3] - ref, axis=-1)
m = d < 0.12
target = np.array([0.74, 0.07, 0.08], np.float32)
q[m, :3] = np.clip(q[m, :3] - ref + target, 0, 1)
a[0:h // 2, 0:w // 2] = q
im.pixels.foreach_set(a.ravel())
im.filepath_raw = P; im.file_format = 'PNG'; im.save()
print('[bp26] bus paint', ref, '->', target, int(m.sum()), 'px')
