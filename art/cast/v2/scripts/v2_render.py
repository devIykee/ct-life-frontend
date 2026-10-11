"""Cast v2 lineup: every base (default look) + example CT hero combos, rendered with a preview of the
intended toon shader (soft 3-band ramp + warm rim) and the tint system (region masks x palette).

usage: Blender -b ... -P v2_render.py -- lineup
Reads export/bases/*/<id>.json + EVID/v2/bases/<id>.blend; writes previews/cast_v2_lineup.jpg
"""
import bpy, sys, json, math
import numpy as np
from pathlib import Path
from mathutils import Vector, Matrix
sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(1, str(Path(__file__).resolve().parents[2] / 'scripts'))
from v2_common import *
import v2_spec as SPEC
import cast_common as C1

TMP = EVID / 'render_tmp'
TMP.mkdir(parents=True, exist_ok=True)
CW, BH, HH, LH = 260, 520, 260, 48
PAL = {t['id']: t['hex'] for t in json.loads((EXPORT / 'skin_palette.json').read_text())['tones']}

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE'
    sc.view_settings.view_transform = 'Standard'
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
    w.node_tree.nodes['Background'].inputs[0].default_value = (0.62, 0.70, 0.82, 1)
    w.node_tree.nodes['Background'].inputs[1].default_value = 0.55
    sun = bpy.data.lights.new('sun', 'SUN'); sun.energy = 3.2; sun.color = (1.0, 0.95, 0.86)
    so = bpy.data.objects.new('sun', sun); sc.collection.objects.link(so); so.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35))
    cam = bpy.data.cameras.new('cam'); cam.type = 'ORTHO'
    co = bpy.data.objects.new('cam', cam); sc.collection.objects.link(co); sc.camera = co
    return sc, co

def hex_lin(h):
    return tuple(float(x) for x in C1.srgb_to_lin(C1.hexrgb(h))) + (1.0,)

def toon_material(name, meta, tints):
    """Preview of the runtime shader: albedo re-hued per tint region, 3-band ramp, warm rim, emission."""
    tex = meta['textures']
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; N = nt.nodes; Lk = nt.links
    for n in list(N):
        N.remove(n)
    out = N.new('ShaderNodeOutputMaterial')
    uv = N.new('ShaderNodeUVMap'); uv.uv_map = 'AtlasUV'
    def img(key, cs):
        t = N.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(str(EXPORT / tex[key]), check_existing=True)
        t.image.colorspace_settings.name = cs; t.interpolation = 'Linear'
        Lk.new(uv.outputs['UV'], t.inputs['Vector']); return t
    alb = img('albedo', 'sRGB')
    m0 = img('mask0', 'Non-Color'); m1 = img('mask1', 'Non-Color')
    col = alb.outputs['Color']
    defaults = meta.get('tint_defaults', {})
    chans = {'skin': (m0, 'Red'), 'hair': (m0, 'Green'), 'top': (m0, 'Blue'), 'bottom': (m0, 'Alpha'), 'shoes': (m1, 'Red'), 'accent': (m1, 'Green')}
    sep0 = N.new('ShaderNodeSeparateColor'); Lk.new(m0.outputs['Color'], sep0.inputs['Color'])
    sep1 = N.new('ShaderNodeSeparateColor'); Lk.new(m1.outputs['Color'], sep1.inputs['Color'])
    for region, hexc in tints.items():
        if region not in chans:
            continue
        texn, ch = chans[region]
        if ch == 'Alpha':
            fac = texn.outputs['Alpha']
        else:
            fac = (sep0 if texn is m0 else sep1).outputs[ch]
        # re-hue: tint * (luminance(albedo) / luminance(default))
        d = C1.srgb_to_lin(C1.hexrgb(defaults.get(region, '#9a9a9a')))
        dl = float(max(0.03, C1.luminance(d)))
        bw = N.new('ShaderNodeRGBToBW'); Lk.new(col, bw.inputs['Color'])
        sc_ = N.new('ShaderNodeMath'); sc_.operation = 'DIVIDE'; Lk.new(bw.outputs['Val'], sc_.inputs[0]); sc_.inputs[1].default_value = dl
        cl = N.new('ShaderNodeMath'); cl.operation = 'MINIMUM'; Lk.new(sc_.outputs[0], cl.inputs[0]); cl.inputs[1].default_value = 1.6
        mul = N.new('ShaderNodeMix'); mul.data_type = 'RGBA'; mul.blend_type = 'MULTIPLY'; mul.inputs['Factor'].default_value = 1.0
        mul.inputs['A'].default_value = hex_lin(hexc)
        cc = N.new('ShaderNodeCombineColor'); Lk.new(cl.outputs[0], cc.inputs[0]); Lk.new(cl.outputs[0], cc.inputs[1]); Lk.new(cl.outputs[0], cc.inputs[2])
        Lk.new(cc.outputs[0], mul.inputs['B'])
        mix = N.new('ShaderNodeMix'); mix.data_type = 'RGBA'; Lk.new(fac, mix.inputs['Factor']); Lk.new(col, mix.inputs['A']); Lk.new(mul.outputs['Result'], mix.inputs['B'])
        col = mix.outputs['Result']
    # lambert from a fixed key direction (emission-only material: no lighting samplers needed)
    geo = N.new('ShaderNodeNewGeometry')
    dot = N.new('ShaderNodeVectorMath'); dot.operation = 'DOT_PRODUCT'
    Lk.new(geo.outputs['Normal'], dot.inputs[0]); dot.inputs[1].default_value = (-0.45, -0.62, 0.64)
    half = N.new('ShaderNodeMath'); half.operation = 'MULTIPLY_ADD'; Lk.new(dot.outputs['Value'], half.inputs[0])
    half.inputs[1].default_value = 0.5; half.inputs[2].default_value = 0.5
    ramp = N.new('ShaderNodeValToRGB'); Lk.new(half.outputs[0], ramp.inputs['Fac'])
    r = ramp.color_ramp; r.interpolation = 'EASE'
    r.elements[0].position = 0.25; r.elements[0].color = (0.46, 0.46, 0.56, 1)
    r.elements[1].position = 0.72; r.elements[1].color = (1.05, 1.0, 0.94, 1)
    e = r.elements.new(0.47); e.color = (0.74, 0.72, 0.78, 1)
    lit = N.new('ShaderNodeMix'); lit.data_type = 'RGBA'; lit.blend_type = 'MULTIPLY'; lit.inputs['Factor'].default_value = 1
    Lk.new(col, lit.inputs['A']); Lk.new(ramp.outputs['Color'], lit.inputs['B'])
    lw = N.new('ShaderNodeLayerWeight'); lw.inputs['Blend'].default_value = 0.25
    rim = N.new('ShaderNodeMath'); rim.operation = 'MULTIPLY'; Lk.new(lw.outputs['Facing'], rim.inputs[0]); rim.inputs[1].default_value = 0.35
    addr = N.new('ShaderNodeMix'); addr.data_type = 'RGBA'; addr.blend_type = 'ADD'; Lk.new(rim.outputs[0], addr.inputs['Factor'])
    Lk.new(lit.outputs['Result'], addr.inputs['A']); addr.inputs['B'].default_value = (1.0, 0.85, 0.65, 1)
    em = N.new('ShaderNodeEmission'); Lk.new(addr.outputs['Result'], em.inputs['Color'])
    final = em
    if 'emission' in tex:
        et = img('emission', 'sRGB')
        em2 = N.new('ShaderNodeEmission'); Lk.new(et.outputs['Color'], em2.inputs['Color']); em2.inputs['Strength'].default_value = 2.5
        add = N.new('ShaderNodeAddShader'); Lk.new(em.outputs[0], add.inputs[0]); Lk.new(em2.outputs[0], add.inputs[1])
        final = add
    Lk.new(final.outputs[0], out.inputs['Surface'])
    return m

def pose_arms_down(rig):
    rig.data.pose_position = 'POSE'
    bpy.context.view_layer.update()
    for s, sgn in (('l', 1), ('r', -1)):
        pb = rig.pose.bones[f'upperarm_{s}']
        head = pb.head.copy(); cur = (pb.tail - pb.head).normalized()
        q = cur.rotation_difference(Vector((sgn * 0.26, 0.04, -0.96)))
        pb.matrix = Matrix.Translation(head) @ q.to_matrix().to_4x4() @ Matrix.Translation(-head) @ pb.matrix
        bpy.context.view_layer.update()

def load_base(base_id, tier='Hero', parts=None, accessories=(), hair=None, facial=()):
    """Append the base's armature + chosen tier objects from its build blend. hair: library style id
    (replaces the default hair) or 'none'; facial: facial-hair style ids."""
    blend = EVID / 'bases' / f'{base_id}.blend'
    with bpy.data.libraries.load(str(blend), link=False) as (src, dst):
        def want(n):
            if '__hair_Hair_' in n or '__facial_' in n:
                return any(n == f'{base_id}__{k}_{tier}' for k in ([f'hair_{hair}'] if hair and hair != 'none' else []) + [f'facial_{f}' for f in facial])
            if n == f'{base_id}__hair_{tier}' and hair:
                meta = json.loads((EXPORT / 'bases' / base_id / f'{base_id}.json').read_text())
                return meta.get('hair_library', {}).get('hair', {}).get(hair, {}).get('default', False)
            return True
        names = [n for n in src.objects if n == 'CTS' or n.endswith(f'_{tier}') and n.startswith(f'{base_id}__') and want(n)]
        names += [f'{base_id}__acc_{a}' for a in accessories if f'{base_id}__acc_{a}' in src.objects]
        dst.objects = names
    objs = [o for o in dst.objects if o]
    arm = next(o for o in objs if o.type == 'ARMATURE')
    meshes = [o for o in objs if o.type == 'MESH']
    if parts is not None:
        keep = []
        for o in meshes:
            n = o.name.split('__')[1].rsplit('_', 1)[0] if '__acc_' not in o.name else None
            if n is None or n in parts:
                keep.append(o)
            else:
                bpy.data.objects.remove(o, do_unlink=True)
        meshes = keep
    for o in [arm] + meshes:
        bpy.context.scene.collection.objects.link(o)
    for o in meshes:
        o.parent = arm
        for md in o.modifiers:
            if md.type == 'ARMATURE':
                md.object = arm
    return arm, meshes

def render_cell(sc, cam, arm, meshes, kind, path):
    for o in sc.objects:
        if o.type == 'MESH':
            o.hide_render = o not in meshes
    deps = bpy.context.evaluated_depsgraph_get()
    pts = []
    for o in meshes:
        ev = o.evaluated_get(deps)
        pts += [o.matrix_world @ v.co for v in ev.data.vertices]
    P = np.array(pts)
    zmin, zmax = float(P[:, 2].min()), float(P[:, 2].max())
    cx = float(np.median(P[:, 0]))
    if kind == 'body':
        sc.render.resolution_x, sc.render.resolution_y = CW, BH
        h = zmax - zmin
        cam.data.ortho_scale = h * 1.1
        tgt = Vector((cx, 0, zmin + h / 2)); el, az = 8, -18
    else:
        sc.render.resolution_x, sc.render.resolution_y = CW, HH
        hp = arm.matrix_world @ arm.pose.bones['Head'].head
        cam.data.ortho_scale = 0.46
        tgt = Vector((hp.x, hp.y, hp.z + 0.1)); el, az = 22, -30
    d = Vector((math.sin(math.radians(az)) * math.cos(math.radians(el)), -math.cos(math.radians(az)) * math.cos(math.radians(el)), math.sin(math.radians(el))))
    cam.location = tgt + d * 8
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    return C1.read_image(path, 3)

def grid(cells, title, path, ncols, footer):
    nrows = (len(cells) + ncols - 1) // ncols
    rowh = BH + HH + LH
    W = CW * ncols; H = 70 + rowh * nrows + 30
    img = np.zeros((H, W, 3), dtype=np.float32); img[:] = (0.09, 0.11, 0.15)
    import v2_text as TX
    for i, (lines, body, hd) in enumerate(cells):
        r, c = divmod(i, ncols); x = c * CW; top = H - 70 - r * rowh
        img[top - BH:top, x:x + CW] = body
        img[top - BH - HH:top - BH, x:x + CW] = hd
        y = top - BH - HH
        for j, ln in enumerate(lines[:2]):
            TX.label(img[:, x:x + CW], ln, y - (j + 1) * (LH // 2), LH // 2, scale_h=13 if j == 0 else 10,
                     color=(0.95, 0.96, 0.98) if j == 0 else (0.7, 0.76, 0.84))
    TX.label(img, title, H - 64, 60, scale_h=24)
    TX.label(img, footer, 4, 24, scale_h=10, color=(0.6, 0.64, 0.7))
    TX.save_jpg(img, path)
    log('SHEET', path, len(cells))

def look_cells(sc, cam, entries):
    cells = []
    for e in entries:
        meta = json.loads((EXPORT / 'bases' / e['base'] / f"{e['base']}.json").read_text())
        arm, meshes = load_base(e['base'], e.get('tier', 'Hero'), e.get('parts'), e.get('accessories', ()), e.get('hair'), e.get('facial', ()))
        tints = dict(meta.get('tint_defaults', {}))
        for k, v in list(meta.get('look_defaults', {}).items()) + list(e.get('tints', {}).items()):
            tints[k] = v if v.startswith('#') else (HAIR_COLORS[v] if k == 'hair' else CLOTH_COLORS[v])
        if e.get('skin'):
            tints['skin'] = PAL[e['skin']]
        mat = toon_material('m_' + e['label'], meta, tints)
        for o in meshes:
            o.data.materials.clear(); o.data.materials.append(mat)
        pose_arms_down(arm)
        b = render_cell(sc, cam, arm, meshes, 'body', TMP / f"{e['label']}_b.png")
        h = render_cell(sc, cam, arm, meshes, 'head', TMP / f"{e['label']}_h.png")
        tri = sum(C1.tri_count(o) for o in meshes)
        cells.append(([e['label'].upper()[:24], e.get('sub', f"{meta['gender'].upper()} {tri:,} TRIS")[:34]], b, h))
        for o in [arm] + meshes:
            bpy.data.objects.remove(o, do_unlink=True)
    return cells

if __name__ == '__main__':
    mode = sys.argv[sys.argv.index('--') + 1]
    sc, cam = reset()
    if mode == 'test':
        cells = look_cells(sc, cam, SPEC.TEST_ENTRIES)
        grid(cells, 'CAST V2 TEST', EVID / 'test_sheet.jpg', 8, 'TEST RENDER (EVIDENCE ONLY)')
    if mode == 'lineup':
        entries = SPEC.lineup_entries()
        cells = look_cells(sc, cam, entries)
        grid(cells, 'CT CAST V2 - BASES + CT HERO COMBOS (TOON PREVIEW)', PREVIEWS / 'cast_v2_lineup.jpg', 8,
             'QUATERNIUS (CC0/QAL) BASES. ACCESSORIES: CT LIFE ORIGINALS. BLENDER EEVEE PREVIEW OF THE TOON SHADER, NOT IN-GAME.')
