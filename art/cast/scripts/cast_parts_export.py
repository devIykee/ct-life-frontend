"""Stage 5: export every wardrobe part (all tiers) as FBX on the family's unchanged skeleton, plus its
slot textures (neutral/default colours) and an id mask for runtime tinting.

usage: Blender -b ... -P cast_parts_export.py -- <fam>
Outputs: art/cast/export/parts/<fam>/<part>.fbx, <part>_{BaseColor,Normal,MetallicSmoothness,Mask}.png
         art/cast/export/parts/<fam>/parts.json
Body skin is split into region chunks (skin_hand, skin_forearm, ...) so garments can hide them.
"""
import bpy, sys, json
import numpy as np
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from cast_common import *
import cast_tex as T
import cast_spec as S

_args = sys.argv[sys.argv.index('--') + 1:]
fam = _args[0]
ONLY = set(_args[1:])
d = fam_dir(fam)
out = EXPORT / 'parts' / fam
out.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(d / 'lods.blend'), load_ui=False, use_scripts=False)
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
rig.data.pose_position = 'REST'
meta = json.loads((d / 'meta.json').read_text())
BONES = meta['bones']
TN = {'hero': 'Hero', 'lod0': 'LOD0', 'lod1': 'LOD1', 'lod2': 'LOD2'}
parts = sorted({o.name.split('|')[1] for o in bpy.data.objects if '|' in o.name})
if ONLY:
    parts = [p for p in parts if p in ONLY]
exp = dict(use_selection=True, object_types={'MESH', 'ARMATURE'}, add_leaf_bones=False, use_armature_deform_only=True,
           bake_anim=False, axis_forward='-Z', axis_up='Y', global_scale=1, apply_unit_scale=True,
           apply_scale_options='FBX_SCALE_UNITS', use_mesh_modifiers=False, path_mode='STRIP', use_tspace=False)
REGION_NAMES = {v: k for k, v in REGION.items()}
reg = S.registry(fam)
index = {'family': fam, 'rig': rig.name, 'atlas_grid': GRID, 'slots': SLOTS, 'parts': {}}
neutral_look = {'id': 'neutral', 'fam': fam, 'parts': {}}
for part in parts:
    info = S.part_info(fam, part)
    chunks = {part: None}
    if part == 'skin':
        chunks = {f'skin_{REGION_NAMES[r]}': r for r in (1, 2, 3, 4, 5)}
    elif part.startswith('bottom'):
        chunks = {part: ('not', (REGION['waist'], REGION['hip'])), f'{part}_waist': REGION['waist'], f'{part}_hip': REGION['hip']}
    for cname, region in chunks.items():
        objs = []
        tris = {}
        for tier in TIERS:
            src = bpy.data.objects.get(f'{fam}|{part}|{tier}')
            if src is None:
                continue
            o = duplicate(src, f'{cname}_{TN[tier]}')
            o.parent = rig
            if region is not None:
                rg = get_face_attr(o, 'region')
                if isinstance(region, tuple):
                    keep_faces(o, lambda f: rg[f.index] not in region[1])
                else:
                    keep_faces(o, lambda f: rg[f.index] == region)
                if len(o.data.polygons) == 0:
                    bpy.data.objects.remove(o, do_unlink=True)
                    continue
            for a in [a.name for a in o.data.attributes if a.name in ('Col', 'SSS', 'Viewport', 'Rain', 'Reflections', 'iris', 'Mask', 'source_part_id', 'crease_edge', 'bevel_weight_edge', 'face_maps')]:
                o.data.attributes.remove(o.data.attributes[a])
            for vc in list(o.data.color_attributes):
                o.data.color_attributes.remove(vc)
            tris[tier] = tri_count(o)
            objs.append(o)
        if not objs:
            continue
        # Unity creates a LODGroup from _LOD0.._LOD2; the hero mesh ships in its own file
        lod = [o for o in objs if not o.name.endswith('_Hero')]
        hero = [o for o in objs if o.name.endswith('_Hero')]
        select_only(lod + [rig], rig)
        bpy.ops.export_scene.fbx(filepath=str(out / f'{cname}.fbx'), **exp)
        if hero:
            select_only(hero + [rig], rig)
            bpy.ops.export_scene.fbx(filepath=str(out / f'{cname}_Hero.fbx'), **exp)
        hides = list(info['hides']) + (['waist'] if part.startswith(('top', 'outer')) else []) + (['hip'] if part in ('top_jacket', 'top_coat', 'top_kaftan') else [])
        index['parts'][cname] = {'part': part, 'slot': info['slot'], 'hides_body_regions': hides, 'needs': info['needs'],
                                 'triangles': tris, 'fbx': f'{cname}.fbx', 'hero_fbx': f'{cname}_Hero.fbx' if hero else None,
                                 'license': reg.get(part, {}).get('license', 'derivative-ccby'), 'notes': reg.get(part, {}).get('notes', '')}
        for o in objs:
            bpy.data.objects.remove(o, do_unlink=True)
    # slot textures at hero layout resolution (1024 atlas) + sub-id mask for runtime tinting
    t = S.part_tex(dict(neutral_look, parts={part: {}}), part) if part not in ('head', 'skin', 'legs', 'hair') else \
        (S.head_tex(neutral_look, fam) if part == 'head' else S.skin_tex(dict(neutral_look, parts={part: {}}), fam) if part in ('skin', 'legs') else S.hair_tex(neutral_look, fam, 'hair'))
    f = T.RES // 1024
    for key, arr in (('BaseColor', t.base), ('Normal', t.normal), ('MetallicSmoothness', t.ms)):
        small, c = T._down(arr, t.cov, f)
        write_png(out / f'{part}_{key}.png', dilate(small, c > 0, 8))
    sub_small = t.sub[::f, ::f].astype(np.float32) / 64.0
    write_png(out / f'{part}_Mask.png', sub_small)
    for cname in list(index['parts']):
        if index['parts'][cname]['part'] == part:
            index['parts'][cname]['textures'] = {k: f'{part}_{k}.png' for k in ('BaseColor', 'Normal', 'MetallicSmoothness', 'Mask')}
    log('part exported', part)
if ONLY and (out / 'parts.json').exists():
    prev = json.loads((out / 'parts.json').read_text())
    for k, v in list(prev['parts'].items()):
        if v['part'] in ONLY:
            del prev['parts'][k]
    prev['parts'].update(index['parts'])
    index['parts'] = prev['parts']
index['region_note'] = 'hides_body_regions names skin chunks (skin_<region>); waist/hip name the <bottom>_waist / <bottom>_hip chunks of the worn bottom.'
index['mask_encoding'] = 'R = sub id / 64 (see cast_common.SUB_IDS); tint garments/accessories per sub id at runtime'
index['sub_ids'] = SUB_IDS
(out / 'parts.json').write_text(json.dumps(index, indent=1))
log('PARTS_DONE', fam, len(index['parts']))
