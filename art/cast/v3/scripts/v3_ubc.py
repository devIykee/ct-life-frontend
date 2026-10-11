"""Cast v3 UBC rebuild (ubc_m / ubc_f) with the v2 defects fixed, written to art/cast/v3/export/bases/<id>/.

Fixes on top of the unchanged v2 builder (monkey-patched, v2 files untouched):
  1. Holes at the neck/shoulders/ankles: coincident source vertices (glTF UV-seam splits) with identical weights are
     welded BEFORE the outfit cuts, and the four body parts are reduced jointly per tier then split again
     (approach of the 2026-10-09 repair candidate), so cuts/decimation no longer open the surface.
  2. Jagged (scalloped) neckline: the region boundary is re-evaluated per texel from a baked surface-position map
     with the same analytic neckline (plane + neck cylinder), the albedo is repainted only where the region
     changes, and cloth inflation fades to zero near the neckline (no stepped silhouette). Shoe soles likewise.
  3. Pale/white brows: brow texels are re-hued to a brow colour derived from the default hair colour and get their
     own tint channel (mask1.B), so hair tints never make brows white.
  4. No TV head (dropped from the starter set); accessories bind the base material (one material per character).
usage: Blender -b --factory-startup --disable-autoexec -P v3_ubc.py -- ubc_m ubc_f
"""
import bpy, bmesh, sys, json, math, time
import numpy as np
from pathlib import Path
from mathutils import Vector, Matrix
sys.path.insert(0, str(Path(__file__).resolve().parent))
import v3_common as K
import v2_build as B
import v2_adapters as A
import v2_geo as G
import v2_spec as SP
import v2_common as C
import cast_common as C1
import v3_spec as S3

STATE = {}
REPORT = {}

def log(*a):
    print('[v3]', *a, flush=True)

# ------------------------------------------------------------------ 1. weld + outfit with smooth neckline
def weld_equal(o):
    bm = bmesh.new(); bm.from_mesh(o.data)
    layer = bm.verts.layers.deform.active
    groups = {}
    for v in bm.verts:
        groups.setdefault(tuple(round(float(x), 6) for x in v.co), []).append(v)
    target, refused = {}, 0
    for vs in groups.values():
        if len(vs) < 2:
            continue
        w0 = dict(vs[0][layer]) if layer else {}
        for v in vs[1:]:
            w1 = dict(v[layer]) if layer else {}
            err = max([abs(w0.get(k, 0) - w1.get(k, 0)) for k in set(w0) | set(w1)] or [0])
            if err > 1e-5:
                refused += 1
                continue
            target[v] = vs[0]
    bmesh.ops.weld_verts(bm, targetmap=target)
    bm.to_mesh(o.data); bm.free(); o.data.update()
    return len(target), refused

def boundary_edges(o):
    bm = bmesh.new(); bm.from_mesh(o.data)
    n = sum(1 for e in bm.edges if len(e.link_faces) == 1)
    bm.free()
    return n

def neck_params(arm, o):
    neck = A.bone_w(arm, 'neck_01')
    P_neck = neck + Vector((0, 0, -0.012)); n_neck = Vector((0, -0.35, 1)).normalized()
    gi = {g.index: g.name for g in o.vertex_groups}
    mw = o.matrix_world
    nd = []
    for v in o.data.vertices:
        if v.groups:
            g = max(v.groups, key=lambda g: g.weight)
            if gi.get(g.group) == 'neck_01':
                p = mw @ v.co; nd.append(math.hypot(p.x - neck.x, p.y - neck.y))
    rr = sorted(nd) or [0.06]
    R = rr[int(0.85 * (len(rr) - 1))] + 0.012
    return {'P': np.array(P_neck), 'n': np.array(n_neck), 'c': np.array([neck.x, neck.y]), 'R': R}

def neck_signed(P, NP):
    """>0 inside the neck opening (skin), <0 on the shirt; metres-ish. Smooth analytic field."""
    a = (P - NP['P']) @ NP['n']                         # above the tilted plane
    r = NP['R'] - np.hypot(P[..., 0] - NP['c'][0], P[..., 1] - NP['c'][1])   # inside the neck cylinder
    return np.minimum(a, r)

_old_outfit = A.ubc_outfit
def outfit(arm, o):
    joined, refused = weld_equal(o)
    b0 = boundary_edges(o)
    NP = neck_params(arm, o)
    STATE['neck'] = NP
    # remember pre-outfit coordinates to fade the cloth inflation near the neckline afterwards
    co0 = np.empty(len(o.data.vertices) * 3, np.float32); o.data.vertices.foreach_get('co', co0); co0 = co0.reshape(-1, 3)
    nv0 = len(o.data.vertices)
    regs = _old_outfit(arm, o)
    co1 = np.empty(len(o.data.vertices) * 3, np.float32); o.data.vertices.foreach_get('co', co1); co1 = co1.reshape(-1, 3)
    # bisect adds vertices (appended); inflation moved existing ones along normals by 3-3.5 mm
    mw = np.array(o.matrix_world)
    Pw = co1 @ mw[:3, :3].T + mw[:3, 3]
    d = neck_signed(Pw, NP)
    fade = np.clip(np.abs(d) / 0.025, 0, 1)            # 0 at the neckline -> 1 beyond 2.5 cm
    k = min(nv0, len(co1))
    moved = np.linalg.norm(co1[:k] - co0[:k], axis=1)
    infl = (moved > 1e-5) & (moved < 0.0045)           # cloth inflation only (shoe smoothing moves > 4.5 mm)
    co1[:k][infl] = co0[:k][infl] + (co1[:k][infl] - co0[:k][infl]) * fade[:k][infl, None]
    o.data.vertices.foreach_set('co', co1.ravel()); o.data.update()
    b1 = boundary_edges(o)
    REPORT.setdefault(STATE['bid'], {})['weld'] = {'joined_duplicate_vertices': joined, 'refused_unequal_weights': refused,
                                                   'boundary_edges_after_weld': b0, 'boundary_edges_after_outfit': b1,
                                                   'neck_radius_m': round(NP['R'], 4), 'inflation_faded_vertices': int((infl & (fade[:k] < 1)).sum())}
    log('weld', STATE['bid'], joined, 'refused', refused, 'boundary', b0, '->', b1)
    return regs

# ------------------------------------------------------------------ 1b. joint body reduction per tier
BODY = ('head', 'top', 'bottom', 'feet')
_old_tiers = B.tiers_for
def tiers(parts, reserve=None):
    STATE['parts'] = parts
    r = _old_tiers(parts, reserve)
    STATE['budgets'] = r[1]
    STATE['cache'] = {}
    return r

_old_make = B.make_tier
def make_tier(o, target, tier, arm):
    parts = STATE['parts']
    name = next((k for k, v in parts.items() if v == o), None)
    if name not in BODY:
        return _old_make(o, target, tier, arm)
    cache = STATE['cache']
    if tier not in cache:
        copies = [C1.duplicate(parts[k], '__shared_' + k + '_' + tier) for k in BODY if k in parts]
        C1.select_only(copies); bpy.ops.object.join(); whole = bpy.context.object
        weld_equal(whole)
        cap = sum(STATE['budgets'][tier][k] for k in BODY if k in parts)
        if C1.tri_count(whole) > cap:
            C1.decimate_to(whole, cap, symmetry=False)
        for p in whole.data.polygons:
            p.use_smooth = True
        C1.limit_weights(whole, C.CTS_BONES, C.MAX_INFL[tier]); whole.data.update()
        vec = np.array([tuple(whole.data.vertices[l.vertex_index].normal) for l in whole.data.loops], dtype=np.float32)
        attr = whole.data.attributes.new('__ct_n', type='FLOAT_VECTOR', domain='CORNER'); attr.data.foreach_set('vector', vec.ravel())
        ids = A.get_face_int(whole, 'part')
        pieces = {}
        for k in BODY:
            if k not in parts:
                continue
            p = C1.duplicate(whole, f'part_{k}_{tier}')
            C1.keep_faces(p, lambda f, key=A.PART_ID[k]: ids[f.index] == key)
            sv = p.data.attributes.get('__ct_n')
            nn = np.empty(len(p.data.loops) * 3, np.float32); sv.data.foreach_get('vector', nn)
            p.data.normals_split_custom_set(nn.reshape(-1, 3)); p.data.attributes.remove(sv)
            for l in list(p.data.uv_layers):
                if l.name != 'AtlasUV':
                    p.data.uv_layers.remove(l)
            p.parent = arm
            pieces[k] = p
        REPORT.setdefault(STATE['bid'], {}).setdefault('joint_reduction', {})[tier] = {
            'cap': cap, 'boundary_edges': boundary_edges(whole), 'parts': {k: C1.tri_count(v) for k, v in pieces.items()}}
        bpy.data.objects.remove(whole, do_unlink=True)
        cache[tier] = pieces
    return cache[tier][name]

# ------------------------------------------------------------------ 2/3. bake position, texel neckline + soles, brows
_old_bake = B.bake_maps
def bake(objs, scene):
    res, reg, cov = _old_bake(objs, scene)
    # surface position per texel (EMIT bake of position + 2 m into a float image)
    bk = [C1.duplicate(o, '__pos_' + o.name) for o in objs]
    for b in bk:
        b.modifiers.clear(); b.hide_render = False
    C1.select_only(bk); bpy.ops.object.join(); J = bpy.context.object
    J.data.uv_layers.active = J.data.uv_layers['AtlasUV']
    m = bpy.data.materials.new('__pos'); m.use_nodes = True; nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new('ShaderNodeOutputMaterial'); em = nt.nodes.new('ShaderNodeEmission')
    geo = nt.nodes.new('ShaderNodeNewGeometry'); add = nt.nodes.new('ShaderNodeVectorMath'); add.operation = 'ADD'
    add.inputs[1].default_value = (2, 2, 2)
    nt.links.new(geo.outputs['Position'], add.inputs[0]); nt.links.new(add.outputs[0], em.inputs['Color'])
    nt.links.new(em.outputs[0], out.inputs['Surface'])
    im = C1.new_float_image('__bk_pos', B.RES)
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = im; nt.nodes.active = t
    J.data.materials.clear(); J.data.materials.append(m)
    for o in scene.objects:
        if o.type == 'MESH':
            o.hide_render = o is not J
    scene.cycles.samples = 1
    C1.select_only([J])
    bpy.ops.object.bake(type='EMIT', use_clear=True, margin=2, uv_layer='AtlasUV')
    pos = C1.image_array(im)[:, :, :3] - 2.0
    bpy.data.images.remove(im); bpy.data.objects.remove(J, do_unlink=True); bpy.data.materials.remove(m)
    STATE.update(POS=pos, REG=reg, COV=cov, PART=np.rint(res['part'][:, :, 0] * 16).astype(np.int32), AO=res['ao'][:, :, 0])
    return res, reg, cov

_old_painted = B.painted
def painted(albedo, ao, cov):
    out = _old_painted(albedo, ao, cov)
    reg, pos, part, NP = STATE['REG'], STATE['POS'], STATE['PART'], STATE['neck']
    shade = 0.72 + 0.28 * np.clip(STATE['AO'], 0, 1)
    rep = {}
    # neckline: body texels on head/top parts within 4 cm of the analytic neckline
    body = cov & np.isin(part, [A.PART_ID['head'], A.PART_ID['top']]) & np.isin(reg, [K.REGION_ID['skin'], K.REGION_ID['top']])
    d = neck_signed(pos, NP)
    pz = NP['P'][2]
    # only around the neck base: the cylinder test must not reach the jaw/face/ears (Head-dominated surface)
    zwin = (pos[..., 2] > pz - 0.12) & (pos[..., 2] < pz + 0.055)
    band = body & zwin & (np.abs(d) < 0.04)
    want_top = band & (d < 0)
    want_skin = band & (d >= 0)
    def ref_colour(sel):
        lin = C1.srgb_to_lin(out[sel]); return np.median(lin / np.maximum(shade[sel][:, None], 1e-3), axis=0)
    far_top = body & (reg == K.REGION_ID['top']) & (d < -0.04) & (d > -0.10)
    far_skin = body & (reg == K.REGION_ID['skin']) & (d > 0.005)
    to_top = want_top & (reg == K.REGION_ID['skin'])
    to_skin = want_skin & (reg == K.REGION_ID['top'])
    if far_top.sum() > 50 and far_skin.sum() > 50:
        ct, cs = ref_colour(far_top), ref_colour(far_skin)
        out[to_top] = C1.lin_to_srgb(np.clip(ct[None, :] * shade[to_top][:, None], 0, 1))
        out[to_skin] = C1.lin_to_srgb(np.clip(cs[None, :] * shade[to_skin][:, None], 0, 1))
        reg[to_top] = K.REGION_ID['top']; reg[to_skin] = K.REGION_ID['skin']
        # anti-aliased edge: 4 mm transition across the analytic neckline (albedo blend + soft masks)
        w = np.clip(0.5 - d / 0.004, 0, 1)
        edge = band & (w > 0) & (w < 1)
        we = w[edge][:, None]; se = shade[edge][:, None]
        out[edge] = C1.lin_to_srgb(np.clip(we * ct[None, :] * se + (1 - we) * cs[None, :] * se, 0, 1))
        STATE['soft_neck'] = (edge, w)
        rep['neckline_soft_edge_texels'] = int(edge.sum())
    rep['neckline_texels_to_top'] = int(to_top.sum()); rep['neckline_texels_to_skin'] = int(to_skin.sum())
    # shoe soles: analytic height instead of per-face
    feet = cov & (part == A.PART_ID['feet']) & np.isin(reg, [K.REGION_ID['shoes'], K.REGION_ID['none']])
    sole_px = feet & (pos[..., 2] < 0.018); upper_px = feet & (pos[..., 2] >= 0.018)
    f_sole = feet & (reg == K.REGION_ID['none']) & (pos[..., 2] < 0.012)
    f_up = feet & (reg == K.REGION_ID['shoes']) & (pos[..., 2] > 0.03)
    s2u = upper_px & (reg == K.REGION_ID['none']); u2s = sole_px & (reg == K.REGION_ID['shoes'])
    if f_sole.sum() > 20 and f_up.sum() > 20:
        cso, cup = ref_colour(f_sole), ref_colour(f_up)
        out[u2s] = C1.lin_to_srgb(np.clip(cso[None, :] * shade[u2s][:, None], 0, 1))
        out[s2u] = C1.lin_to_srgb(np.clip(cup[None, :] * shade[s2u][:, None], 0, 1))
        reg[u2s] = K.REGION_ID['none']; reg[s2u] = K.REGION_ID['shoes']
    rep['sole_texels_changed'] = int(u2s.sum() + s2u.sum())
    # brows: own region + dark default derived from the default hair colour
    brows = cov & (part == A.PART_ID['brows'])
    look = SP.BASES[STATE['bid']]['look']
    target_hex = K.brow_color(C.HAIR_COLORS[look.get('hair', 'dark_brown')])
    if brows.sum() > 20:
        lin = C1.srgb_to_lin(out[brows]); L = C1.luminance(lin); ref = float(np.median(L))
        tgt = C1.srgb_to_lin(C1.hexrgb(target_hex))
        out[brows] = C1.lin_to_srgb(np.clip((L / max(ref, 1e-4))[:, None] * tgt[None, :], 0, 1))
        reg[brows] = K.REGION_ID['brows']
        rep['brow_texels'] = int(brows.sum()); rep['brow_old_median_linear_luminance'] = round(ref, 4); rep['brow_default_hex'] = target_hex
    REPORT.setdefault(STATE['bid'], {})['texel_fixes'] = rep
    log('texel fixes', STATE['bid'], json.dumps(rep))
    return out

def masks(reg, cov):
    m0 = np.zeros(reg.shape + (4,), np.float32); m1 = np.zeros(reg.shape + (4,), np.float32)
    for name, (k, ch) in K.MASK_CHANNEL.items():
        (m0 if k == 0 else m1)[(reg == K.REGION_ID[name]) & cov, ch] = 1.0
    if 'soft_neck' in STATE:
        edge, w = STATE['soft_neck']
        m0[edge, 2] = w[edge]; m0[edge, 0] = 1 - w[edge]      # top (B) / skin (R)
    return m0, m1

def acc_mat():
    return bpy.data.materials[f"{STATE['bid']}_Mat"]

def run(bid):
    STATE.clear(); STATE['bid'] = bid
    B.EXPORT = K.EXPORT; B.EVID = K.EVID / 'v3'
    A.ubc_outfit = outfit; B.tiers_for = tiers; B.make_tier = make_tier; B.bake_maps = bake; B.painted = painted
    B.masks_from_region = masks; B.acc_material = acc_mat
    G.STARTER_ACCESSORIES = list(K.STARTER_ACCESSORIES)
    B.TINT_REGIONS = list(K.TINT_REGIONS); B.REGION_ID = dict(K.REGION_ID)
    t0 = time.time()
    B.build_base(bid)
    p = K.EXPORT / 'bases' / bid / f'{bid}.json'
    meta = json.loads(p.read_text())
    spec = S3.UBC[bid]
    pal = {t['id']: t['hex'] for t in json.loads((K.V2EXPORT / 'skin_palette.json').read_text())['tones']}
    lk = dict(meta.get('tint_defaults', {}))
    for r, v in spec['look'].items():
        if r == 'skin':
            continue
        lk[r] = C.HAIR_COLORS[v] if r == 'hair' else C.CLOTH_COLORS[v]
    lk['brows'] = K.brow_color(lk.get('hair', C.HAIR_COLORS['dark_brown']))
    lk['skin'] = pal[spec['look']['skin']]
    meta.update({'label': spec['label'], 'archetypes': spec['archetypes'], 'family': None,
                 'licence': 'CC0 1.0 (bundled License_Standard.txt; quaternius.com pack page)',
                 'tint_reference': dict(meta.get('tint_defaults', {})), 'look_defaults': {'skin_tone': spec['look']['skin'], **lk},
                 'v3_fixes': REPORT.get(bid, {}), 'material': f'{bid}_Mat (accessories bind the same material)'})
    p.write_text(json.dumps(meta, indent=1))
    log('UBC', bid, 'done', round(time.time() - t0, 1), 's')

if __name__ == '__main__':
    for b in sys.argv[sys.argv.index('--') + 1:]:
        run(b)
    (K.EVID / 'v3').mkdir(parents=True, exist_ok=True)
    (K.EVID / 'v3' / 'ubc_fix_report.json').write_text(json.dumps(REPORT, indent=1, default=str))
