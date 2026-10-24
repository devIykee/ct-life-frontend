"""The daily chest (2026-10-24): Poly Haven "Treasure Chest" by Rico Cilliers (CC0, https://polyhaven.com/a/treasure_chest,
licence https://polyhaven.com/license checked 2026-10-24) for the gm streak's chest-open animation.

  art/pipeline/blender.sh art/pipeline/ui_chest.py

Reads art/raw/polyhaven/treasure_chest/ (1k glTF + textures, fetched 2026-10-24) and writes
art/processed/ui/chest/: ui_chest.fbx (the body, lock and handles joined as "base"; the lid kept separate as "lid" with
its origin on the hinge line so Unity can swing it) decimated to ~9k triangles, plus ui_chest_BaseColor.png,
ui_chest_Normal.png (OpenGL +Y) and ui_chest_MaskMap.png (R metallic, G occlusion, B 0, A smoothness) at 1024 px.
"""
import os
import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.abspath(os.path.join(HERE, '..', 'raw', 'polyhaven', 'treasure_chest'))
OUT = os.path.abspath(os.path.join(HERE, '..', 'processed', 'ui', 'chest'))
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=os.path.join(RAW, 'treasure_chest_1k.gltf'))
objs = {o.name: o for o in bpy.context.scene.objects if o.type == 'MESH'}
print('[chest] imported', {n: len(o.data.polygons) for n, o in objs.items()})

def decimate(o, ratio):
    bpy.context.view_layer.objects.active = o
    m = o.modifiers.new('dec', 'DECIMATE'); m.ratio = ratio; m.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=m.name)

def apply_xf(o):
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

for o in objs.values():
    apply_xf(o)

decimate(objs['treasure_chest_bottom'], 0.085)
decimate(objs['treasure_chest_lid'], 0.09)
for n in ('treasure_chest_handle_left', 'treasure_chest_handle_right', 'treasure_chest_lock'):
    decimate(objs[n], 0.35)

# base = bottom + handles + lock (the lock stays on the body: the hasp reads as unlatched when the lid swings)
bpy.ops.object.select_all(action='DESELECT')
for n in ('treasure_chest_bottom', 'treasure_chest_handle_left', 'treasure_chest_handle_right', 'treasure_chest_lock'):
    objs[n].select_set(True)
bpy.context.view_layer.objects.active = objs['treasure_chest_bottom']
bpy.ops.object.join()
base = bpy.context.view_layer.objects.active
base.name = 'base'; base.data.name = 'base'
lid = objs['treasure_chest_lid']; lid.name = 'lid'; lid.data.name = 'lid'

# the lid's origin on the hinge: the back-bottom edge of the lid mesh (glTF origin is near it already; make it exact)
import mathutils
ws = [lid.matrix_world @ v.co for v in lid.data.vertices]
zmin = min(w.z for w in ws)   # Blender Z up; glTF -Z forward became +Y back
ymax = max(w.y for w in ws)
hinge = mathutils.Vector((0.0, ymax, zmin))
cur = lid.matrix_world.translation.copy()
lid.data.transform(mathutils.Matrix.Translation(cur - hinge))
lid.matrix_world.translation = hinge
print('[chest] hinge', tuple(round(c, 4) for c in hinge))

for o in (base, lid):
    for p in o.data.polygons: p.use_smooth = True

tris = sum(len(p.vertices) - 2 for o in (base, lid) for p in o.data.polygons)
print('[chest] triangles', tris)

bpy.ops.object.select_all(action='DESELECT'); base.select_set(True); lid.select_set(True)
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'ui_chest.fbx'), use_selection=True, apply_scale_options='FBX_SCALE_ALL',
                         axis_forward='-Z', axis_up='Y', object_types={'MESH'}, use_mesh_modifiers=True, mesh_smooth_type='FACE',
                         add_leaf_bones=False, bake_space_transform=True, path_mode='STRIP', embed_textures=False)

def load(name):
    img = bpy.data.images.load(os.path.join(RAW, 'textures', name))
    img.colorspace_settings.name = 'Non-Color'
    a = np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], img.size[0], 4)
    return img, a

def save(arr, name):
    h, w = arr.shape[:2]
    img = bpy.data.images.new(name, w, h, alpha=True)
    img.colorspace_settings.name = 'Non-Color'
    img.pixels[:] = arr.ravel()
    img.filepath_raw = os.path.join(OUT, name + '.png'); img.file_format = 'PNG'; img.save()

_, diff = load('treasure_chest_diff_1k.jpg'); diff[..., 3] = 1.0; save(diff, 'ui_chest_BaseColor')
_, nor = load('treasure_chest_nor_gl_1k.jpg'); nor[..., 3] = 1.0; save(nor, 'ui_chest_Normal')
_, arm = load('treasure_chest_arm_1k.jpg')
mask = np.zeros_like(arm)
mask[..., 0] = arm[..., 2]          # metallic
mask[..., 1] = arm[..., 0]          # occlusion
mask[..., 2] = 0.0
mask[..., 3] = 1.0 - arm[..., 1]    # smoothness
save(mask, 'ui_chest_MaskMap')
print('[chest] wrote', OUT)
