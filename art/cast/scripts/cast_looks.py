"""Stage 4: assemble looks (body + wardrobe parts) per tier, bake per-look atlases, export FBX.

usage: Blender -b ... -P cast_looks.py -- <fam> [lookId ...]
Inputs: EVID/families/<fam>/lods.blend (+ wardrobe maps), cast_spec.LOOKS
Outputs: art/cast/export/looks/<lookId>/..., EVID/families/<fam>/looks.blend, manifest fragments.
"""
import bpy, bmesh, sys, json, hashlib, time
import numpy as np
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from cast_common import *
import cast_tex as T
import cast_spec as S
import cast_morph as M
from cast_eye_surface import restore_missing_rain_pupil

args = sys.argv[sys.argv.index('--') + 1:]
fam = args[0]
only = set(args[1:])
d = fam_dir(fam)
meta = json.loads((d / 'meta.json').read_text())
BONES = meta['bones']
bpy.ops.wm.open_mainfile(filepath=str(d / 'lods.blend'), load_ui=False, use_scripts=False)
scene = bpy.context.scene
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
rig.data.pose_position = 'REST'
looks = [l for l in S.LOOKS if l['fam'] == fam and (not only or l['id'] in only)]
log('looks', [l['id'] for l in looks])
TIER_NAME = {'hero': 'Hero', 'lod0': 'LOD0', 'lod1': 'LOD1', 'lod2': 'LOD2'}

def part_obj(part, tier):
    return bpy.data.objects.get(f'{fam}|{part}|{tier}')

def assemble(look, tier):
    objs = []
    hides = S.hides_for(look) | set(look.get('hide_regions', []))
    tmap = (look.get('tier_map') or {}).get(tier, {})
    for part in S.resolved_parts(look):
        src = part_obj(part, tmap.get(part, tier))
        if src is None:
            raise RuntimeError(f'missing part {part} {tier}')
        o = duplicate(src, f'__{look["id"]}_{part}_{tier}')
        off = (look.get('part_offset') or {}).get(part)
        if off:   # (dx, dy, dz, scale): e.g. the crew cap lifted off the brows, a touch larger so the crown never pokes out
            co = np.empty(len(o.data.vertices) * 3, np.float32); o.data.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
            c = co.mean(axis=0); co = (co - c) * off[3] + c + np.array(off[:3], np.float32)
            o.data.vertices.foreach_set('co', co.ravel()); o.data.update()
        o.parent = rig
        hid_b = {REGION[h] for h in ('waist', 'hip') if h in hides}
        if part.startswith('bottom') and hid_b:
            reg = get_face_attr(o, 'region')
            keep_faces(o, lambda f: reg[f.index] not in hid_b)
        if part == 'skin' and hides:
            reg = get_face_attr(o, 'region')
            hid = {REGION[h] for h in hides if h in REGION}
            keep_faces(o, lambda f: reg[f.index] not in hid)
            if len(o.data.polygons) == 0:
                bpy.data.objects.remove(o, do_unlink=True)
                continue
        objs.append(o)
    select_only(objs)
    if len(objs) > 1:
        bpy.ops.object.join()
    o = bpy.context.object
    o.name = f'{look["id"]}_{TIER_NAME[tier]}'
    o.data.name = o.name
    for m in list(o.modifiers):
        if m.type != 'ARMATURE':
            o.modifiers.remove(m)
    if not any(m.type == 'ARMATURE' for m in o.modifiers):
        o.modifiers.new('Game skin', 'ARMATURE').object = rig
    # body type / face morphs (functions of rest position + weights; skeleton untouched)
    M.apply(o, look, meta, BONES)
    cap = (look.get('caps') or {}).get(tier, CAPS[tier])
    if tri_count(o) > cap:
        fit_budget(o, cap - 12)
    limit_weights(o, BONES, MAX_INFL[tier])
    for name in [a.name for a in o.data.attributes if a.name in ('Col', 'SSS', 'Viewport', 'Rain', 'Reflections', 'iris', 'Mask', 'source_part_id', 'crease_edge', 'bevel_weight_edge', 'face_maps')]:
        o.data.attributes.remove(o.data.attributes[name])
    for vc in list(o.data.color_attributes):
        o.data.color_attributes.remove(vc)
    if fam == 'f' and 'head' in S.resolved_parts(look):
        # Older cached LODs predate the front-pupil culling correction. Preserve that
        # surface on every rebuild, without replacing the user's wardrobe intermediates.
        restore_missing_rain_pupil(o)
    return o

def fit_budget(o, target):
    """Trim a merged look that is slightly over its cap: collapse everything except face/eyes/hands."""
    ref = duplicate(o, '__fit_ref')
    subs = get_face_attr(o, 'sub')
    keep_ids = {SUB_IDS[k] for k in ('face', 'eyes', 'brows', 'lashes')}
    g = o.vertex_groups.new(name='protect')
    prot = set()
    for p in o.data.polygons:
        if subs[p.index] in keep_ids:
            prot.update(p.vertices)
    g.add(list(prot), 1.0, 'REPLACE')
    n = decimate_to(o, target, protect_group='protect', protect_factor=8.0, symmetry=False)
    o.vertex_groups.remove(o.vertex_groups['protect'])
    m = o.modifiers.new('nrm', 'DATA_TRANSFER')
    m.object = ref; m.use_loop_data = True; m.data_types_loops = {'CUSTOM_NORMAL'}; m.loop_mapping = 'POLYINTERP_NEAREST'
    select_only([o])
    with bpy.context.temp_override(object=o, active_object=o):
        bpy.ops.object.modifier_move_to_index(modifier=m.name, index=0)
        bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.data.objects.remove(ref, do_unlink=True)
    log('  fitted', o.name, n)

def make_material(look_id, paths, emissive):
    mat = bpy.data.materials.get(look_id + '_Mat') or bpy.data.materials.new(look_id + '_Mat')
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled')
    nt.links.new(bsdf.outputs[0], out.inputs['Surface'])
    uv = nt.nodes.new('ShaderNodeUVMap'); uv.uv_map = 'SlotUV'
    def tex(path, cs):
        t = nt.nodes.new('ShaderNodeTexImage')
        t.image = bpy.data.images.load(str(path), check_existing=True)
        t.image.colorspace_settings.name = cs
        t.image.alpha_mode = 'CHANNEL_PACKED'
        nt.links.new(uv.outputs['UV'], t.inputs['Vector'])
        return t
    b = tex(paths['base'], 'sRGB')
    nt.links.new(b.outputs['Color'], bsdf.inputs['Base Color'])
    n = tex(paths['normal'], 'Non-Color')
    nm = nt.nodes.new('ShaderNodeNormalMap'); nm.uv_map = 'SlotUV'
    nt.links.new(n.outputs['Color'], nm.inputs['Color']); nt.links.new(nm.outputs['Normal'], bsdf.inputs['Normal'])
    ms = tex(paths['ms'], 'Non-Color')
    sep = nt.nodes.new('ShaderNodeSeparateColor')
    nt.links.new(ms.outputs['Color'], sep.inputs['Color']); nt.links.new(sep.outputs['Red'], bsdf.inputs['Metallic'])
    inv = nt.nodes.new('ShaderNodeMath'); inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1
    nt.links.new(ms.outputs['Alpha'], inv.inputs[1]); nt.links.new(inv.outputs[0], bsdf.inputs['Roughness'])
    if emissive:
        e = tex(paths['emis'], 'sRGB')
        nt.links.new(e.outputs['Color'], bsdf.inputs['Emission Color'])
        bsdf.inputs['Emission Strength'].default_value = 2.0
    return mat

def sha(p):
    return hashlib.sha256(Path(p).read_bytes()).hexdigest()

manifest = {}
for look in looks:
    t0 = time.time()
    lid = look['id']
    odir = EXPORT / 'looks' / lid
    odir.mkdir(parents=True, exist_ok=True)
    # ---- textures
    slots = S.look_textures(look, fam)
    tiers_l = look.get('tiers', ['lod0', 'lod1', 'lod2'])
    atlas_res = 1024 if 'hero' in tiers_l else 512   # spec: 1024 hero avatars, 512 crowd
    atlas = T.compose_atlas(slots, atlas_res)
    emissive = bool(atlas['emis'].max() > 0.02)
    paths = {'base': odir / f'{lid}_BaseColor.png', 'normal': odir / f'{lid}_Normal.png',
             'ms': odir / f'{lid}_MetallicSmoothness.png'}
    write_png(paths['base'], atlas['base'])
    write_png(paths['normal'], atlas['normal'])
    write_png(paths['ms'], atlas['ms'])
    if emissive:
        paths['emis'] = odir / f'{lid}_Emission.png'
        write_png(paths['emis'], atlas['emis'])
    mat = make_material(lid, paths, emissive)
    # ---- meshes
    tiers = look.get('tiers', ['lod0', 'lod1', 'lod2'])
    objs = {}
    info = {}
    for tier in tiers:
        o = assemble(look, tier)
        o.data.materials.clear(); o.data.materials.append(mat)
        tris = tri_count(o)
        mi = max_influences(o)
        assert tris <= (look.get('caps') or {}).get(tier, CAPS[tier]), (lid, tier, tris)
        assert mi <= MAX_INFL[tier], (lid, tier, mi)
        objs[tier] = o
        info[tier] = {'triangles': tris, 'vertices': len(o.data.vertices), 'max_influences': mi}
    # ---- export
    files = {}
    crowd = [objs[t] for t in ('lod0', 'lod1', 'lod2') if t in objs]
    exp = dict(use_selection=True, object_types={'MESH', 'ARMATURE'}, add_leaf_bones=False, use_armature_deform_only=True,
               bake_anim=False, axis_forward='-Z', axis_up='Y', global_scale=1, apply_unit_scale=True,
               apply_scale_options='FBX_SCALE_UNITS', use_mesh_modifiers=False, path_mode='STRIP', use_tspace=False)
    if crowd:
        select_only(crowd + [rig], rig)
        f = odir / f'{lid}.fbx'
        bpy.ops.export_scene.fbx(filepath=str(f), **exp)
        files['crowd_fbx'] = str(f.relative_to(REPO))
    if 'hero' in objs:
        select_only([objs['hero'], rig], rig)
        f = odir / f'{lid}_Hero.fbx'
        bpy.ops.export_scene.fbx(filepath=str(f), **exp)
        files['hero_fbx'] = str(f.relative_to(REPO))
    files.update({k: str(v.relative_to(REPO)) for k, v in paths.items()})
    manifest[lid] = {
        'family': fam, 'rig': rig.name, 'archetype': look.get('archetype'), 'kind': look.get('kind'),
        'label': look.get('label'), 'parts': {p: (o or {}) for p, o in S.resolved_parts(look).items()},
        'slots': {p: S.part_info(fam, p)['slot'] for p in S.resolved_parts(look)},
        'hidden_body_regions': sorted(S.hides_for(look)),
        'skin_tone': look.get('skin_tone'), 'hair_color': look.get('hair_color'), 'body': look.get('body'),
        'face': look.get('face'), 'scale': look.get('scale', 1.0), 'tiers': info, 'files': files,
        'emissive': emissive, 'atlas_size': atlas_res, 'tribe': look.get('tribe'), 'notes': look.get('notes', ''),
    }
    log('LOOK', lid, json.dumps(info), round(time.time() - t0, 1), 's')

mp = d / f'manifest_{fam}.json'
allm = json.loads(mp.read_text()) if mp.exists() else {}
allm.update(manifest)
mp.write_text(json.dumps(allm, indent=1))
for o in list(bpy.data.objects):
    if o.type == 'MESH' and '|' in o.name:
        bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.outliner.orphans_purge(do_recursive=True)
lb = d / 'looks.blend'
if only and lb.exists():
    # merge: replace these looks inside the existing looks.blend
    bpy.ops.wm.save_as_mainfile(filepath=str(d / '__looks_new.blend'), compress=True)
    names = [n for lid in manifest for n in (f'{lid}_Hero', f'{lid}_LOD0', f'{lid}_LOD1', f'{lid}_LOD2')]
    bpy.ops.wm.open_mainfile(filepath=str(lb), load_ui=False, use_scripts=False)
    for o in list(bpy.data.objects):
        if o.name in names:
            bpy.data.objects.remove(o, do_unlink=True)
    rig2 = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    with bpy.data.libraries.load(str(d / '__looks_new.blend'), link=False) as (src, dst):
        dst.objects = [n for n in src.objects if n in names]
    for o in dst.objects:
        if o:
            bpy.context.scene.collection.objects.link(o)
            o.parent = rig2
            for m in o.modifiers:
                if m.type == 'ARMATURE':
                    m.object = rig2
    for o in list(bpy.data.objects):
        if o.type == 'ARMATURE' and o is not rig2:
            bpy.data.objects.remove(o, do_unlink=True)
    bpy.ops.outliner.orphans_purge(do_recursive=True)
    (d / '__looks_new.blend').unlink()
bpy.ops.wm.save_as_mainfile(filepath=str(lb), compress=True)
log('LOOKS_DONE', fam, len(manifest))
