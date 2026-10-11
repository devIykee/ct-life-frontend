"""Cast v3 preview renderer. Renders cells described in a job JSON with one lighting rig and one toon shader
(region tints x neutral/default reference, 3-band ramp, rim, AO), arms-down pose.
usage: Blender -b ... -P v3_render.py -- <job.json>
job: {"name": str, "cells": [{"id", "base", "kind": family|ubc|hero|ubc_v2|hero_v2, "parts": {slot: key}, "hair",
       "facial": [], "tier": "Hero", "skin": "skin_05"|"#hex", "tints": {region: name|#hex}, "accessories": [],
       "views": ["body", "head", "neck", "face"], "brows_follow_hair": true}]}
Writes EVID/v3/renders/<name>/<id>_<view>.png and index.json. Blender EEVEE preview, not Unity output."""
import bpy, sys, json, math
import numpy as np
from pathlib import Path
from mathutils import Vector, Matrix
sys.path.insert(0, str(Path(__file__).resolve().parent))
import v3_common as K
import v3_look as L
from v3_common import EXPORT, EVID, HAIR_COLORS, CLOTH_COLORS, HAT_COLORS

V2EVID = Path('/Users/mac/Documents/codes/projects/ct-life-evidence/cast/v2')
PAL = {t['id']: t['hex'] for t in json.loads((K.V2EXPORT / 'skin_palette.json').read_text())['tones']}
CHANS = {'skin': (0, 'Red'), 'hair': (0, 'Green'), 'top': (0, 'Blue'), 'bottom': (0, 'Alpha'),
         'shoes': (1, 'Red'), 'accent': (1, 'Green'), 'brows': (1, 'Blue'), 'hat': (1, 'Alpha')}

def tint_hex(region, v):
    if v is None:
        return None
    if v.startswith('#'):
        return v
    if region in ('hair', 'brows'):
        return HAIR_COLORS[v]
    if region == 'skin':
        return PAL[v]
    if region == 'hat':
        return HAT_COLORS.get(v) or CLOTH_COLORS[v]
    return CLOTH_COLORS[v]

def lum_lin(hexc):
    c = np.array(L.hex_lin(hexc)[:3]); return float(c @ [0.2126, 0.7152, 0.0722])

def tint_material(name, tex, tints, refs, regions=CHANS, emission_strength=2.5):
    """tex: dict albedo/mask0/mask1/emission absolute paths; tints/refs: region -> hex."""
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; N, Lk = nt.nodes, nt.links
    for n in list(N):
        N.remove(n)
    uv = N.new('ShaderNodeUVMap'); uv.uv_map = 'AtlasUV'
    def img(path, cs):
        t = N.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(str(path), check_existing=True)
        t.image.colorspace_settings.name = cs
        if cs == 'Non-Color':
            t.image.alpha_mode = 'CHANNEL_PACKED'
        t.interpolation = 'Linear'; Lk.new(uv.outputs['UV'], t.inputs['Vector']); return t
    alb = img(tex['albedo'], 'sRGB'); col = alb.outputs['Color']
    ms = [img(tex['mask0'], 'Non-Color'), img(tex['mask1'], 'Non-Color') if tex.get('mask1') else None]
    seps = []
    for t in ms:
        if t is None:
            seps.append(None); continue
        s = N.new('ShaderNodeSeparateColor'); Lk.new(t.outputs['Color'], s.inputs['Color']); seps.append(s)
    for region, hexc in tints.items():
        if region not in regions or hexc is None:
            continue
        k, ch = regions[region]
        if ms[k] is None:
            continue
        fac = ms[k].outputs['Alpha'] if ch == 'Alpha' else seps[k].outputs[ch]
        dl = max(0.03, lum_lin(refs.get(region, '#9a9a9a')))
        bw = N.new('ShaderNodeRGBToBW'); Lk.new(col, bw.inputs['Color'])
        dv = N.new('ShaderNodeMath'); dv.operation = 'DIVIDE'; Lk.new(bw.outputs['Val'], dv.inputs[0]); dv.inputs[1].default_value = dl
        cl = N.new('ShaderNodeMath'); cl.operation = 'MINIMUM'; Lk.new(dv.outputs[0], cl.inputs[0]); cl.inputs[1].default_value = 1.6
        cc = N.new('ShaderNodeCombineColor')
        for i in range(3):
            Lk.new(cl.outputs[0], cc.inputs[i])
        mul = N.new('ShaderNodeMix'); mul.data_type = 'RGBA'; mul.blend_type = 'MULTIPLY'; mul.inputs['Factor'].default_value = 1.0
        mul.inputs['A'].default_value = L.hex_lin(hexc); Lk.new(cc.outputs[0], mul.inputs['B'])
        mix = N.new('ShaderNodeMix'); mix.data_type = 'RGBA'; Lk.new(fac, mix.inputs['Factor'])
        Lk.new(col, mix.inputs['A']); Lk.new(mul.outputs['Result'], mix.inputs['B'])
        col = mix.outputs['Result']
    em = None
    if tex.get('emission') and Path(tex['emission']).exists():
        em = img(tex['emission'], 'sRGB').outputs['Color']
    L.toon_tail(nt, col, em)
    return m

def append(blend, names):
    with bpy.data.libraries.load(str(blend), link=False) as (src, dst):
        have = set(src.objects)
        dst.objects = [n for n in names if n in have]
    objs = [o for o in dst.objects if o]
    arm = next(o for o in objs if o.type == 'ARMATURE')
    meshes = [o for o in objs if o.type == 'MESH']
    for o in [arm] + meshes:
        bpy.context.scene.collection.objects.link(o)
    for o in meshes:
        o.parent = arm; o.hide_render = False; o.hide_viewport = False
        for md in o.modifiers:
            if md.type == 'ARMATURE':
                md.object = arm
    return arm, meshes

def import_acc(path, arm):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(path), automatic_bone_orientation=False, ignore_leaf_bones=False)
    new = [o for o in bpy.data.objects if o not in before]
    out = []
    for o in new:
        if o.type == 'MESH':
            mw = o.matrix_world.copy(); o.parent = arm; o.matrix_world = mw
            for md in list(o.modifiers):
                o.modifiers.remove(md)
            o.modifiers.new('arm', 'ARMATURE').object = arm
            out.append(o)
    for o in new:
        if o.type != 'MESH':
            bpy.data.objects.remove(o, do_unlink=True)
    return out

def load_cell(c):
    kind = c.get('kind', 'family')
    tier = c.get('tier', 'Hero')
    bid = c['base']
    accs = []
    if kind == 'family':
        meta = json.loads((EXPORT / 'bases' / bid / f'{bid}.json').read_text())
        fam = meta['family']
        parts = {s: v.split('/')[1] for s, v in meta['default_parts'].items()}
        parts.update(c.get('parts', {}))
        names = ['CTS'] + [f'{fam}__{s}_{k}_{tier}' for s, k in parts.items() if k]
        arm, meshes = append(EVID / 'v3' / 'families' / f'{fam}.blend', names)
        for a in c.get('accessories', []):
            if a == 'backpack':
                a_arm, a_m = append(EVID / 'v3' / 'families' / f'{fam}.blend', ['CTS', f'{fam}__extra_backpack_{tier}'])
                for o in a_m:
                    o.parent = arm
                    for md in o.modifiers:
                        if md.type == 'ARMATURE':
                            md.object = arm
                bpy.data.objects.remove(a_arm, do_unlink=True)
                accs += a_m
            else:
                accs += import_acc(EXPORT / meta['accessories'][a]['fbx'], arm)
        tex = {k: EXPORT / v for k, v in meta['textures'].items()}
        refs = meta['tint_reference']
        fj = json.loads((EXPORT / 'families' / fam / f'{fam}.json').read_text())
        look = {}
        for s in ('feet', 'bottom', 'top', 'head'):      # manifest tint_resolution
            look.update(fj['library'][s][parts[s]].get('default_tints', {}))
        look.update(meta['look_defaults'])
    elif kind in ('ubc', 'ubc_v2', 'hero', 'hero_v2'):
        v2 = kind.endswith('_v2') or kind == 'hero'
        root = K.V2EXPORT if kind in ('ubc_v2', 'hero_v2', 'hero') else EXPORT
        blend = (V2EVID if v2 else EVID / 'v3') / 'bases' / f'{bid}.blend'
        meta = json.loads((root / 'bases' / bid / f'{bid}.json').read_text())
        hair, facial = c.get('hair'), c.get('facial', [])
        lib = meta.get('hair_library', {})
        def want(n):
            if '__hair_Hair_' in n or '__facial_' in n:
                ks = ([f'hair_{hair}'] if hair and hair != 'none' else []) + [f'facial_{f}' for f in facial]
                return any(n == f'{bid}__{k}_{tier}' for k in ks)
            if n == f'{bid}__hair_{tier}' and hair:
                return lib.get('hair', {}).get(hair, {}).get('default', False)
            return True
        with bpy.data.libraries.load(str(blend), link=False) as (src, dst):
            names = [n for n in src.objects if n == 'CTS' or (n.endswith(f'_{tier}') and n.startswith(f'{bid}__') and want(n))]
            names += [f'{bid}__acc_{a}' for a in c.get('accessories', []) if f'{bid}__acc_{a}' in src.objects]
        arm, meshes = append(blend, names)
        tex = {k: root / v for k, v in meta['textures'].items()}
        if kind == 'hero':     # v2 geometry + v3 material pass textures
            hm = json.loads((EXPORT / 'bases' / bid / f'{bid}.json').read_text())
            tex = {k: EXPORT / v for k, v in hm['textures'].items()}
            refs = hm['tint_reference']; look = dict(hm['look_defaults'])
        elif kind == 'ubc':
            refs = meta['tint_reference']; look = dict(meta['look_defaults'])
        else:                  # faithful v2 "before": v2 defaults, v2 region set (no brows/hat channels)
            refs = dict(meta.get('tint_defaults', {})); look = dict(meta.get('tint_defaults', {}))
            for k, v in meta.get('look_defaults', {}).items():
                look[k] = tint_hex(k, v)
    else:
        raise ValueError(kind)
    tints = {r: look.get(r) for r in CHANS if look.get(r)}
    if c.get('skin'):
        tints['skin'] = tint_hex('skin', c['skin'])
    for r, v in c.get('tints', {}).items():
        tints[r] = tint_hex(r, v)
    if kind != 'ubc_v2' and c.get('brows_follow_hair', True) and 'hair' in c.get('tints', {}):
        tints['brows'] = K.brow_color(tints['hair'])
    regions = CHANS if kind != 'ubc_v2' and kind != 'hero_v2' else {k: v for k, v in CHANS.items() if k not in ('brows', 'hat')}
    mat = tint_material('m_' + c['id'], tex, tints, refs, regions)
    for o in meshes + accs:
        o.data.materials.clear(); o.data.materials.append(mat)
    return arm, meshes + accs, tints

VIEWS = {'body': ((360, 720), None), 'head': ((360, 300), 0.46), 'neck': ((360, 300), 0.34), 'face': ((300, 300), 0.36),
         'bodyback': ((360, 720), None)}

def smooth_meshes(meshes, angle_deg):
    """preview test: replace Quaternius authored flat normals by smooth-by-angle normals"""
    import math
    for o in meshes:
        me = o.data
        with bpy.context.temp_override(object=o, active_object=o, selected_objects=[o]):
            if me.has_custom_normals:
                bpy.ops.mesh.customdata_custom_splitnormals_clear()
        me.shade_smooth()
        me.set_sharp_from_angle(angle=math.radians(angle_deg))

def render_cell(sc, cam, c, outdir):
    arm, meshes, tints = load_cell(c)
    if c.get('smooth'):
        smooth_meshes(meshes, c['smooth'])
    L.pose_arms_down(arm)
    for o in sc.objects:
        if o.type == 'MESH':
            o.hide_render = o not in meshes
    hp = arm.matrix_world @ arm.pose.bones['Head'].head
    np_ = arm.matrix_world @ arm.pose.bones['neck_01'].head
    rec = {'id': c['id'], 'files': {}, 'tints': tints}
    for v in c.get('views', ['body', 'head']):
        res, scale = VIEWS[v]
        if v == 'body':
            P = L.frame(sc, cam, meshes, 'body', res)
        elif v == 'bodyback':
            P = L.frame(sc, cam, meshes, 'body', res, az=162, el=8)
        elif v == 'head':
            L.frame(sc, cam, meshes, 'head', res, head_point=(hp.x, hp.y, hp.z + 0.1), head_scale=scale)
        elif v == 'face':
            L.frame(sc, cam, meshes, 'head', res, head_point=(hp.x, hp.y, hp.z + 0.09), az=-8, el=6, head_scale=scale)
        elif v == 'neck':
            L.frame(sc, cam, meshes, 'head', res, head_point=(np_.x, np_.y, np_.z - 0.03), az=-12, el=12, head_scale=scale)
        f = outdir / f"{c['id']}_{v}.png"
        L.render(sc, f)
        rec['files'][v] = str(f)
    deps = bpy.context.evaluated_depsgraph_get()
    tris = 0
    for o in meshes:
        o.data.calc_loop_triangles(); tris += len(o.data.loop_triangles)
    rec['triangles'] = tris
    for o in [arm] + meshes:
        bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.materials):
        if m.users == 0:
            bpy.data.materials.remove(m)
    for me in list(bpy.data.meshes):
        if me.users == 0:
            bpy.data.meshes.remove(me)
    for a in list(bpy.data.armatures):
        if a.users == 0:
            bpy.data.armatures.remove(a)
    return rec

if __name__ == '__main__':
    job = json.loads(Path(sys.argv[sys.argv.index('--') + 1]).read_text())
    outdir = EVID / 'v3' / 'renders' / job['name']
    outdir.mkdir(parents=True, exist_ok=True)
    sc, cam = L.reset()
    index = []
    for c in job['cells']:
        try:
            index.append(render_cell(sc, cam, c, outdir))
            print('[v3] cell', c['id'], flush=True)
        except Exception as e:
            import traceback; traceback.print_exc()
            index.append({'id': c['id'], 'error': f'{type(e).__name__}: {e}'})
            print('[v3] FAILED', c['id'], e, flush=True)
    (outdir / 'index.json').write_text(json.dumps(index, indent=1))
    print('[v3] RENDER DONE', job['name'], len(index), flush=True)
