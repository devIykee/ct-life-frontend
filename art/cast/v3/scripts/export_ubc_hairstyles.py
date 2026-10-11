"""Export preserved fitted Hermes hair as complete UBC bodies; never rewrite a source blend.

Blender -b -t 2 -P art/cast/v3/scripts/export_ubc_hairstyles.py
Only the hair part changes. Each output retains the original head, body, UVs, weights and four tiers.
"""
import bpy, hashlib, json
from pathlib import Path

REPO = Path(__file__).resolve().parents[4]
SOURCE = Path('/Users/mac/Documents/codes/projects/ct-life-evidence/cast-hermes-20261010/v3/bases')
OUTPUT = REPO / 'client/Assets/Art/characters/distinct/hairstyles'
OUTPUT.mkdir(parents=True, exist_ok=True)
STYLES = {
    'ubc_m': {'buzzed': 'hair_Hair_Buzzed', 'long': 'hair_Hair_Long', 'buns': 'hair_Hair_Buns'},
    'ubc_f': {'buns': 'hair_Hair_Buns', 'buzzed': 'hair_Hair_BuzzedFemale', 'parted': 'hair_Hair_SimpleParted'},
}
TIERS = ('Hero', 'LOD0', 'LOD1', 'LOD2')
UNCHANGED = ('head', 'eyes', 'brows', 'top', 'bottom', 'feet')
records = []

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def select(objects, active):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.hide_set(False)
        obj.select_set(True)
    bpy.context.view_layer.objects.active = active

for body, styles in STYLES.items():
    source = SOURCE / (body + '.blend')
    bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False, use_scripts=False)
    rig = bpy.data.objects['CTS']
    rig.data.pose_position = 'REST'
    for pose in rig.pose.bones:
        pose.matrix_basis.identity()
    for style, part in styles.items():
        name = body + '_' + style
        target = OUTPUT / name
        target.mkdir(exist_ok=True)
        created = []
        counts = {}
        for tier in TIERS:
            pieces = []
            for slot in (*UNCHANGED, part):
                src = bpy.data.objects[body + '__' + slot + '_' + tier]
                obj = src.copy()
                obj.data = src.data.copy()
                bpy.context.scene.collection.objects.link(obj)
                obj.name = name + '_piece_' + slot
                obj.parent = rig
                obj.hide_render = False
                for modifier in obj.modifiers:
                    if modifier.type == 'ARMATURE':
                        modifier.object = rig
                pieces.append(obj)
            # All parts were reduced jointly by the preserved v3 pipeline, so shared body seams stay aligned.
            select(pieces, pieces[0])
            bpy.ops.object.join()
            joined = bpy.context.object
            joined.name = name + '_' + tier
            joined.data.name = joined.name + '_Geometry'
            joined.data.calc_loop_triangles()
            counts[tier] = len(joined.data.loop_triangles)
            assert counts[tier] <= {'Hero': 12000, 'LOD0': 6000, 'LOD1': 3000, 'LOD2': 1200}[tier], (name, tier, counts[tier])
            for vertex in joined.data.vertices:
                weights = [g.weight for g in vertex.groups if g.weight > .00001]
                assert weights and abs(sum(weights) - 1) < .0001 and len(weights) <= (2 if tier == 'LOD2' else 4)
            created.append(joined)
        files = {}
        for role, meshes, suffix in [('world', created[1:], ''), ('hero', created[:1], '_Hero')]:
            path = target / (name + suffix + '.fbx')
            select(meshes + [rig], rig)
            bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True, object_types={'MESH', 'ARMATURE'},
                add_leaf_bones=False, use_armature_deform_only=True, bake_anim=False,
                axis_forward='-Z', axis_up='Y', global_scale=1, apply_unit_scale=True,
                apply_scale_options='FBX_SCALE_UNITS', use_mesh_modifiers=False, path_mode='STRIP', use_tspace=False)
            files[role] = str(path.relative_to(OUTPUT))
        records.append({'identity': body, 'style': style, 'hairId': 'hair_' + name,
            'files': files, 'triangles': counts, 'sourceBlendSHA256': sha(source),
            'sourcePart': part, 'unchangedParts': list(UNCHANGED),
            'sha256': {role: sha(OUTPUT / path) for role, path in files.items()}})
        for obj in created:
            bpy.data.objects.remove(obj, do_unlink=True)

(OUTPUT / 'manifest.json').write_text(json.dumps({'version': 1, 'source': 'Preserved Hermes v3 Quaternius UBC CC0',
    'models': records}, indent=2) + '\n')
print('EXPORTED_HAIRSTYLES', len(records))
