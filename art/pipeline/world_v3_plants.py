"""World polish (2026-10-23): semi-real ground cover and bushes for CT Island, replacing the toy-style grass clumps,
cartoon flowers and blob bushes, plus the Trenches yard props that need a CC0 source.

Two modes, one file:
  python3 -I art/pipeline/world_v3_plants.py fetch
      downloads the CC0 Poly Haven models below (glTF, 1k textures) into art/raw/polyhaven/<asset>/ (cached, gitignored)
  art/pipeline/blender.sh art/pipeline/world_v3_plants.py -- [cards] [bushes] [--sheet]
      builds art/processed/world_v3/v3_plants/: the ground-cover card atlas (each Poly Haven plant rendered top-lit as
      unlit albedo with alpha on a transparent film, front and side), the card-clump meshes that use it, and bushes
      / a hedge made from the LOLIPOP maple's leaf-cluster cards (already in the kit: one more draw of the same
      material); plus manifest.json for the island kit (dress.py, IslandV2Kit).

Method: a ground cover plant is two to three vertical cards crossed round its centre (4-6 triangles) whose normals are
bent up, so it lights like the ground it grows from; a bush is the maple's LOD1 crown cards (no trunk, no branches)
squashed to a shrub's proportions, its leaves scaled down to read as small-leaved shrub foliage, and a crossed-card
billboard for distance. No shadows from ground cover (IslandV2Kit gives it no shadow range).

Sources: the Poly Haven models in PH_MODELS (all CC0, https://polyhaven.com/license "Our assets are all licensed as
CC0", checked 2026-10-23); the bushes reuse the LOLIPOP maple pack already in the kit (CC Attribution, credited per
asset). Every asset's credit is in its manifest entry and art/LICENSES.md.
"""
import os, sys, json, math, shutil, urllib.request, urllib.parse

HERE = os.path.dirname(os.path.abspath(__file__))
ART = os.path.abspath(os.path.join(HERE, '..'))
RAW_PH = os.path.join(ART, 'raw', 'polyhaven')
OUT = os.path.join(ART, 'processed', 'world_v3', 'v3_plants')
TEX = os.path.join(OUT, 'textures')
UA = {'User-Agent': 'CTLife-artpipeline/1.0 (CC0 asset fetch)'}
SHEET = '/Users/mac/Documents/codes/projects/ct-life-evidence/world-polish-20261023/plants-sheet.png'

# model -> what it becomes
PH_MODELS = {
    'grass_medium_02': 'grass tuft cards (short meadow grass)',
    'grass_medium_01': 'grass tuft cards (taller grass)',
    'grass_bermuda_01': 'lawn-edge grass cards',
    'celandine_01': 'yellow wildflower cards',
    'periwinkle_plant': 'violet-blue flower cards',
    'dandelion_01': 'dandelion cards',
    'flower_ursinia': 'orange daisy cards',
    'fern_02': 'fern cards',
    'weed_plant_02': 'leafy weed cards',
    'nettle_plant': 'nettle cards',
    'modular_chainlink_fence': 'The Trenches: chain-link fence run',
    'concrete_road_barrier_02': 'The Trenches: concrete jersey barrier',
    'old_tyre': 'The Trenches: tyre stacks',
    'wooden_crate_02': 'The Trenches: crates',
}


PLANTS = [m for m in PH_MODELS if m not in ('modular_chainlink_fence', 'concrete_road_barrier_02', 'old_tyre', 'wooden_crate_02')]
EXTRA = [(m, ('Diffuse', 'opacity' if m == 'periwinkle_plant' else 'Alpha'), '2k') for m in PLANTS] + [('modular_chainlink_fence', ('wire_diff', 'wire_alpha', 'wire_nor_gl', 'wire_arm',
                                                                                   'posts_diff', 'posts_nor_gl', 'posts_arm'), '1k')]


def _get(url, path):
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req, timeout=120) as r, open(path + '.part', 'wb') as f:
        shutil.copyfileobj(r, f)
    os.replace(path + '.part', path)


def _json(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=60) as r:
        return json.load(r)


def fetch():
    os.makedirs(RAW_PH, exist_ok=True)
    for m in PH_MODELS:
        d = os.path.join(RAW_PH, m)
        if os.path.exists(os.path.join(d, 'meta.json')):
            continue
        inf = _json('https://api.polyhaven.com/info/' + m)
        files = _json('https://api.polyhaven.com/files/' + m)
        g = files['gltf']['1k']['gltf']
        os.makedirs(d, exist_ok=True)
        print('[fetch] Poly Haven', m, flush=True)
        _get(g['url'], os.path.join(d, os.path.basename(urllib.parse.urlparse(g['url']).path)))
        for rel, v in g['include'].items():
            t = os.path.normpath(os.path.join(d, rel))
            if not t.startswith(d + os.sep):
                continue
            os.makedirs(os.path.dirname(t), exist_ok=True)
            _get(v['url'], t)
        json.dump(dict(id=m, name=inf['name'], url=f'https://polyhaven.com/a/{m}', authors=list(inf['authors']), licence='CC0',
                       dimensions_mm=inf.get('dimensions'), polycount=inf.get('polycount')), open(os.path.join(d, 'meta.json'), 'w'), indent=1)
    # the glTF's 1k JPGs carry no alpha: the cut-outs come from the separate maps (2k for the plants, the cards are
    # rendered from them)
    for m, keys, res in EXTRA:
        d = os.path.join(RAW_PH, m, 'maps')
        os.makedirs(d, exist_ok=True)
        files = None
        for k in keys:
            t = os.path.join(d, f'{m}_{k}_{res}.png')
            if os.path.exists(t):
                continue
            files = files or _json('https://api.polyhaven.com/files/' + m)
            print('[fetch] Poly Haven map', m, k, res, flush=True)
            _get(files[k][res]['png']['url'], t)
    print('[fetch] done', flush=True)


try:
    import bpy  # noqa
    IN_BLENDER = True
except ImportError:
    IN_BLENDER = False

if not IN_BLENDER:
    if __name__ == '__main__':
        fetch()
    raise SystemExit(0)

# ====================================================================== Blender part
import random
import bmesh
import numpy as np
from mathutils import Vector, Matrix
sys.path.insert(0, HERE)
import world_v3_lib as W

ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
YARD_OUT = os.path.join(ART, 'processed', 'world_v3', 'v3_yard')
YARD_TEX = os.path.join(YARD_OUT, 'textures')
TREES = os.path.join(ART, 'processed', 'world_v3', 'v3_trees')
CARD_W, CARD_H = 0.9, 0.45            # metres a card cell shows (2:1)
CELL = (1024, 512)                     # pixels a cell has in the 2048 atlas


def log(*a):
    print('[plants]', *a, flush=True)


def ph_credit(m):
    meta = json.load(open(os.path.join(RAW_PH, m, 'meta.json')))
    return f'"{meta["name"]}" by {", ".join(meta["authors"])} (Poly Haven, {meta["url"]}), CC0'


# ------------------------------------------------------------------ 1. ground-cover cards
# cell -> (proto id, [(model, object-name filter, count, scale range)], what)
CARDS = [
    ('pv_grass_short', [('grass_medium_01', 'large', 6, (1.5, 1.9)), ('grass_medium_01', 'mid', 6, (1.4, 1.8)), ('grass_medium_01', 'small', 5, (1.6, 2.0))],
     'lush short grass'),
    ('pv_grass_tall', [('grass_medium_01', 'tall', 8, (1.3, 1.7)), ('grass_medium_02', '', 5, (1.1, 1.4)), ('grass_medium_01', 'mid', 4, (1.5, 1.8))], 'taller grass'),
    ('pv_flowers_yellow', [('celandine_01', '', 5, (1.1, 1.4)), ('grass_medium_01', 'mid', 5, (1.4, 1.7)), ('grass_medium_02', '', 2, (1.0, 1.2))],
     'lesser celandine with grass'),
    ('pv_flowers_violet', [('periwinkle_plant', '', 5, (0.55, 0.75)), ('grass_medium_01', 'mid', 6, (1.4, 1.7))], 'periwinkle with grass'),
    ('pv_flowers_dandelion', [('dandelion_01', '', 4, (1.1, 1.4)), ('grass_medium_01', 'mid', 5, (1.4, 1.7)), ('grass_medium_02', '', 2, (1.0, 1.2)),
                              ('grass_bermuda_01', 'seedling', 4, (1.4, 1.8))], 'dandelions in grass'),
    ('pv_flowers_orange', [('flower_ursinia', '', 5, (1.0, 1.25)), ('grass_medium_01', 'mid', 5, (1.4, 1.7))], 'ursinia daisies with grass'),
    ('pv_fern', [('fern_02', '_b', 1, (0.85, 0.85)), ('fern_02', '_d', 1, (0.75, 0.75))], 'fern'),
    ('pv_weeds', [('weed_plant_02', '', 4, (1.4, 1.8)), ('nettle_plant', 'tall', 4, (1.6, 2.0)), ('grass_medium_01', 'mid', 4, (1.4, 1.7))], 'weeds and nettles'),
]


def card_material(name, diff_path, alpha_path):
    """Unlit albedo with the plant's cut-out: emission (the diffuse map) over transparent by its alpha map."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    td = nt.nodes.new('ShaderNodeTexImage'); td.image = bpy.data.images.load(diff_path, check_existing=True)
    ta = nt.nodes.new('ShaderNodeTexImage'); ta.image = bpy.data.images.load(alpha_path, check_existing=True)
    ta.image.colorspace_settings.name = 'Non-Color'
    sep = nt.nodes.new('ShaderNodeSeparateColor')
    nt.links.new(ta.outputs['Color'], sep.inputs[0])
    em = nt.nodes.new('ShaderNodeEmission'); em.inputs['Strength'].default_value = 1.0
    nt.links.new(td.outputs['Color'], em.inputs['Color'])
    tr = nt.nodes.new('ShaderNodeBsdfTransparent')
    mix = nt.nodes.new('ShaderNodeMixShader')
    nt.links.new(sep.outputs['Red'], mix.inputs[0])
    nt.links.new(tr.outputs[0], mix.inputs[1])
    nt.links.new(em.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs[0])
    return m


def load_plant(model):
    """The model's objects (world transforms applied), each with the unlit cut-out material."""
    before = set(bpy.data.objects)
    W.import_glb(os.path.join(RAW_PH, model, f'{model}_1k.gltf'))
    objs = [o for o in bpy.data.objects if o not in before and o.type == 'MESH']
    maps = os.path.join(RAW_PH, model, 'maps')
    alpha = [f for f in os.listdir(maps) if 'Alpha' in f or 'opacity' in f][0]
    mat = card_material('card_' + model, os.path.join(maps, f'{model}_Diffuse_2k.png'), os.path.join(maps, alpha))
    out = []
    for o in objs:
        c = W.join_copy([o], o.name + '_src')
        c.data.materials.clear()
        c.data.materials.append(mat)
        c.hide_render = True
        out.append(c)
    for o in objs:
        bpy.data.objects.remove(o)
    return out


def render_cell(parts, seed, px):
    """Scatter copies of the parts along a 0.9 m strip and render it front-on: RGBA float array (row 0 = top)."""
    rnd = random.Random(seed)
    sc = bpy.context.scene
    placed = []
    for i, (src, sx) in enumerate(parts):
        o = src.copy()
        sc.collection.objects.link(o)
        o.hide_render = False
        s = rnd.uniform(*sx)
        o.scale = (s, s, s)
        o.rotation_euler = (0, 0, rnd.uniform(0, 2 * math.pi))
        bpy.context.view_layer.update()
        mn, mx = W.world_bounds([o])
        half = (mx.x - mn.x) / 2
        span = max(0.0, CARD_W / 2 - half - 0.02)
        x = (i + 0.5) / len(parts) * 2 * span - span + rnd.uniform(-0.05, 0.05) if len(parts) > 1 else 0.0
        o.location = (o.location.x + x - (mn.x + mx.x) / 2, o.location.y + rnd.uniform(-0.18, 0.18) - (mn.y + mx.y) / 2, o.location.z - mn.z)
        placed.append(o)
    cam = bpy.data.objects.new('cardcam', bpy.data.cameras.new('cardcam'))
    sc.collection.objects.link(cam)
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = CARD_W
    cam.location = (0, -6, CARD_H / 2)
    cam.rotation_euler = (math.radians(90), 0, 0)
    sc.camera = cam
    sc.render.engine = 'CYCLES'
    sc.cycles.samples = 24
    sc.cycles.use_denoising = False
    sc.cycles.max_bounces = 0
    sc.cycles.transparent_max_bounces = 64
    sc.cycles.pixel_filter_type = 'BLACKMAN_HARRIS'
    sc.cycles.filter_width = 1.2
    sc.render.film_transparent = True
    sc.render.resolution_x, sc.render.resolution_y = px
    sc.render.resolution_percentage = 100
    sc.view_settings.view_transform = 'Standard'
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGBA'
    path = os.path.join(TEX, f'_cell_{seed}.png')
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(path)
    arr = np.array(img.pixels[:], np.float32).reshape(px[1], px[0], 4)[::-1]
    bpy.data.images.remove(img)
    os.remove(path)
    for o in placed:
        bpy.data.objects.remove(o)
    bpy.data.objects.remove(cam)
    return arr


def bleed(rgba, iters=24):
    """Spread colour into the transparent texels (no dark fringe once mipmapped)."""
    rgb = rgba[..., :3] * (rgba[..., 3:4] > 0.02)
    w = (rgba[..., 3:4] > 0.02).astype(np.float32)
    for _ in range(iters):
        if w.min() > 0:
            break
        s = np.zeros_like(rgb); n = np.zeros_like(w)
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            s += np.roll(rgb * w, (dy, dx), (0, 1)); n += np.roll(w, (dy, dx), (0, 1))
        fill = (w == 0) & (n > 0)
        rgb = np.where(fill, s / np.maximum(n, 1e-6), rgb)
        w = np.where(fill, 1.0, w)
    out = rgba.copy()
    out[..., :3] = np.where(rgba[..., 3:4] > 0.02, rgba[..., :3], rgb)
    return out


def card_mesh(pid, uv, quads=3, w=CARD_W, h=CARD_H):
    """quads vertical cards crossed round the centre; normals bent straight up (lit like the ground they grow from)."""
    verts, faces, uvs = [], [], []
    inset = 2.0 / 2048
    u0, v0, u1, v1 = uv[0] + inset, uv[1] + inset, uv[2] - inset, uv[3] - inset
    for k in range(quads):
        a = math.radians(180.0 * k / quads + 15.0)
        dx, dy = math.cos(a) * w / 2, math.sin(a) * w / 2
        b = len(verts)
        verts += [(-dx, -dy, 0.0), (dx, dy, 0.0), (dx, dy, h), (-dx, -dy, h)]
        faces.append((b, b + 1, b + 2, b + 3))
        uvs += [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
    me = bpy.data.meshes.new(pid + '_LOD0')
    me.from_pydata(verts, [], faces)
    uvl = me.uv_layers.new(name='UVMap')
    for p in me.polygons:
        for li in p.loop_indices:
            uvl.data[li].uv = uvs[me.loops[li].vertex_index]
    me.normals_split_custom_set_from_vertices([(0.0, 0.0, 1.0)] * len(verts))
    o = bpy.data.objects.new(pid + '_LOD0', me)
    bpy.context.scene.collection.objects.link(o)
    return o


def build_cards():
    W.reset()
    os.makedirs(TEX, exist_ok=True)
    cache = {}
    atlas = np.zeros((2048, 2048, 4), np.float32)
    man = {}
    for i, (pid, recipe, what) in enumerate(CARDS):
        parts = []
        for model, filt, count, sx in recipe:
            if model not in cache:
                cache[model] = load_plant(model)
            pool = [o for o in cache[model] if filt in o.name] or cache[model]
            rnd = random.Random(hash(pid) & 0xffff)
            parts += [(pool[(k + rnd.randrange(len(pool))) % len(pool)], sx) for k in range(count)]
        random.Random(i).shuffle(parts)
        log('card', pid, len(parts), 'plants')
        cell = render_cell(parts, 100 + i, CELL)
        # a little ground AO at the stems
        v = np.linspace(1.0, 0.0, CELL[1])[:, None, None]          # 1 at the top row
        cell[..., :3] *= 0.62 + 0.38 * np.clip(v / 0.35, 0, 1)
        col, row = i % 2, i // 2
        atlas[row * 512:(row + 1) * 512, col * 1024:(col + 1) * 1024] = cell
        uv = (col * 0.5, 1.0 - (row + 1) * 0.25, col * 0.5 + 0.5, 1.0 - row * 0.25)
        o = card_mesh(pid, uv)
        mat = bpy.data.materials.get('pv_ground_cards') or bpy.data.materials.new('pv_ground_cards')
        o.data.materials.append(mat)
        W.export_fbx([o], os.path.join(OUT, pid + '.fbx'))
        models = sorted({m for m, *_ in recipe})
        man[pid] = dict(fbx=f'world_v3/v3_plants/{pid}.fbx', kind='grass', tris=[W.tris(o)], size_m=[CARD_W, CARD_W, CARD_H],
                        materials=['pv_ground_cards'], textures=['pv_ground_cards.png'],
                        tags=['grass', 'ground_detail', 'kit:v3', 'pack:pv'] + (['flower'] if 'flowers' in pid else []),
                        source='; '.join(f'https://polyhaven.com/a/{m}' for m in models), source_uid='ph_' + '+'.join(models), licence='CC0',
                        credit='; '.join(ph_credit(m) for m in models) + f'; rendered to cut-out cards ({what}) by CT Life '
                               '(art/pipeline/world_v3_plants.py)')
        bpy.data.objects.remove(o)
    atlas = bleed(atlas)
    W.save_png(atlas[::-1], os.path.join(TEX, 'pv_ground_cards.png'), 4)
    log('atlas written')
    return man


# ------------------------------------------------------------------ 2. bushes and a hedge from the maple's leaf cards
BUSHES = [  # id, maple, (w, d, h) m
    ('pv_bush_round', 'lp_maple_medium_1', (1.7, 1.6, 1.25)),
    ('pv_bush_low', 'lp_maple_medium_2', (1.5, 1.4, 0.95)),
    ('pv_bush_large', 'lp_maple_large_1', (2.3, 2.1, 1.6)),
    ('pv_hedge', 'lp_maple_medium_3', (3.0, 0.95, 1.15)),
]


def leaves_material():
    m = bpy.data.materials.get('lp_maple_leaves')
    if m and m.get('ctl'):
        return m
    m = m or bpy.data.materials.new('lp_maple_leaves')
    m['ctl'] = 1
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    b = nt.nodes.new('ShaderNodeBsdfPrincipled')
    t = nt.nodes.new('ShaderNodeTexImage')
    t.image = bpy.data.images.load(os.path.join(TREES, 'textures', 'lp_maple_leaves.png'), check_existing=True)
    nt.links.new(t.outputs['Color'], b.inputs['Base Color'])
    nt.links.new(t.outputs['Alpha'], b.inputs['Alpha'])
    nt.links.new(b.outputs[0], out.inputs[0])
    if hasattr(m, 'surface_render_method'):
        m.surface_render_method = 'DITHERED'
    return m


def islands(me):
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    seen, out = set(), []
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, isl = [f], []
        seen.add(f.index)
        while stack:
            g = stack.pop()
            isl.append(g.index)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen:
                        seen.add(h.index); stack.append(h)
        out.append(isl)
    bm.free()
    return out


def thin_cards(o, keep, grow, seed, name):
    """LOD1: a random share of the leaf cards (whole islands), each grown about its centre to keep the coverage."""
    c = W.dup(o, name)
    me = c.data
    isl = islands(me)
    rnd = random.Random(seed)
    drop = set()
    for f in isl:
        if rnd.random() > keep:
            drop.update(f)
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    for f in isl:
        if set(f) & drop:
            continue
        vs = {v for i in f for v in bm.faces[i].verts}
        cen = sum((v.co for v in vs), Vector()) / len(vs)
        for v in vs:
            v.co = cen + (v.co - cen) * grow
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in drop], context='FACES')
    bm.to_mesh(me)
    bm.free()
    return c


def leaf_cards(maple):
    """The maple LOD1's leaf cards as (vertex offsets from the card centre, card normal, uvs, faces) - one per island."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(TREES, maple + '.fbx'))
    objs = [o for o in bpy.data.objects if o not in before]
    src = next(o for o in objs if o.name.endswith('_LOD1'))
    o = W.join_copy([src], 'leafsrc')
    for x in objs:
        bpy.data.objects.remove(x)
    me = o.data
    li = next(i for i, m in enumerate(me.materials) if m and 'leaves' in m.name)
    uvl = me.uv_layers[0].data
    cards = []
    for isl in islands(me):
        polys = [me.polygons[i] for i in isl if me.polygons[i].material_index == li]
        if not polys:
            continue
        vids = sorted({v for p in polys for v in p.vertices})
        co = np.array([me.vertices[v].co[:] for v in vids])
        c = co.mean(0)
        n = sum((p.normal for p in polys), Vector())
        if n.length < 1e-6:
            continue
        n.normalize()
        ext = np.ptp(co, 0).max()
        if ext < 0.2:
            continue
        idx = {v: k for k, v in enumerate(vids)}
        uv = {}
        for p in polys:
            for v, l in zip(p.vertices, p.loop_indices):
                uv[idx[v]] = tuple(uvl[l].uv)
        cards.append(((co - c) / ext, n, [uv.get(k, (0.0, 0.0)) for k in range(len(vids))], [[idx[v] for v in p.vertices] for p in polys]))
    bpy.data.objects.remove(o)
    return cards


# leafy sprays of the maple leaf atlas (pixels of its 768 preview, origin top left), cropped clear of the thick stems
LEAF_RECTS = [(20, 100, 185, 330), (200, 95, 370, 300), (430, 50, 570, 330), (585, 50, 700, 330), (60, 400, 420, 565),
              (80, 590, 420, 750), (460, 390, 700, 470), (470, 485, 700, 600), (540, 620, 720, 760)]


def card_bush(name, size, n_cards, card_m, seed, core=0.55):
    """Leaf-spray cards (crops of the maple atlas) over and just inside an ellipsoid, each turned out from the centre
    with a random roll, normals radial (the volume shades like a shrub, not like flat planes); flattened on the ground."""
    rnd = random.Random(seed)
    w, d, h = size
    verts, faces, uvs, nrms = [], [], [], []
    for k in range(n_cards):
        x0, y0, x1, y1 = LEAF_RECTS[rnd.randrange(len(LEAF_RECTS))]
        uv = [(x0 / 768, 1 - y1 / 768), (x1 / 768, 1 - y1 / 768), (x1 / 768, 1 - y0 / 768), (x0 / 768, 1 - y0 / 768)]
        aspect = (x1 - x0) / (y1 - y0)
        while True:
            v = Vector((rnd.gauss(0, 1), rnd.gauss(0, 1), rnd.gauss(0, 1)))
            if v.length > 1e-3:
                v.normalize()
                if v.z > -0.45:
                    break
        r = core + (1.0 - core) * math.sqrt(rnd.random())
        p = Vector((v.x * w / 2 * r, v.y * d / 2 * r, h * 0.47 + v.z * h / 2 * r))
        p.z = max(p.z, 0.1 + rnd.random() * 0.1)
        out = Vector((p.x / (w / 2), p.y / (d / 2), (p.z - h * 0.45) / (h / 2))).normalized()
        # the card's plane faces out (tilted a little at random), rolled about its normal
        nrm = (out + Vector((rnd.gauss(0, 0.35), rnd.gauss(0, 0.35), rnd.gauss(0, 0.35)))).normalized()
        rot = Vector((0, 0, 1)).rotation_difference(nrm).to_matrix() @ Matrix.Rotation(rnd.uniform(0, 2 * math.pi), 3, 'Z')
        s = card_m * rnd.uniform(0.8, 1.2)
        hw, hh = s * math.sqrt(aspect) / 2, s / math.sqrt(aspect) / 2
        base = len(verts)
        for (qx, qy), t in zip(((-hw, -hh), (hw, -hh), (hw, hh), (-hw, hh)), uv):
            vv = p + rot @ Vector((qx, qy, 0.0))
            vv.z = max(vv.z, -0.03)
            verts.append(vv); uvs.append(t)
            nrms.append((vv - Vector((0, 0, h * 0.25))).normalized())
        faces.append([base, base + 1, base + 2, base + 3])
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], faces)
    uvl = me.uv_layers.new(name='UVMap')
    for p in me.polygons:
        for li in p.loop_indices:
            uvl.data[li].uv = uvs[me.loops[li].vertex_index]
    me.normals_split_custom_set_from_vertices([tuple(n) for n in nrms])
    lm = bpy.data.materials.get('pv_bush_leaves') or leaves_material()
    lm.name = 'pv_bush_leaves'
    me.materials.append(lm)
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    return o


def build_bushes():
    man = {}
    for pid, maple, (w, d, h) in BUSHES:
        W.reset()
        area = (w * d + w * h + d * h) / 1.5
        n0 = int(min(300, max(110, area * 70)))
        seed = sum(map(ord, pid))
        o = card_bush(pid + '_LOD0', (w, d, h), n0, 0.55, seed)
        l1 = card_bush(pid + '_LOD1', (w, d, h), n0 // 3, 0.85, seed + 1, core=0.7)
        bb = W.billboard(o, pid, TEX, px=256)
        W.export_fbx([o, l1, bb], os.path.join(OUT, pid + '.fbx'))
        lic = json.load(open(os.path.join(TREES, 'manifest.json')))[maple]
        man[pid] = dict(fbx=f'world_v3/v3_plants/{pid}.fbx', kind='bush', tris=[W.tris(o), W.tris(l1), W.tris(bb)], size_m=[w, d, h],
                        materials=['pv_bush_leaves'], lod2_material=f'M_{pid}_billboard',
                        textures=['world_v3/v3_trees/textures/lp_maple_leaves.png', f'{pid}_billboard.png'],
                        tags=['bush', 'kit:v3', 'pack:pv'] + (['hedge'] if 'hedge' in pid else []),
                        source=lic['source'], source_uid=lic['source_uid'], licence=lic['licence'],
                        credit=lic['credit'].replace('modified by CT Life (split, decimated, atlased)',
                                                     'modified by CT Life (its leaf cards rebuilt into shrubs: art/pipeline/world_v3_plants.py)'))
        log('bush', pid, man[pid]['tris'])
    return man


# ------------------------------------------------------------------ 3. The Trenches: chain-link fence panel
def ph_maps(model, part, res='1k'):
    d = os.path.join(RAW_PH, model, 'maps')
    return {k: os.path.join(d, f'{model}_{part}_{k}_{res}.png') for k in ('diff', 'nor_gl', 'arm', 'alpha')}


def load_px(path, cs='sRGB'):
    img = bpy.data.images.load(path)
    img.colorspace_settings.name = 'Non-Color'
    a = np.array(img.pixels[:], np.float32).reshape(img.size[1], img.size[0], img.channels)
    bpy.data.images.remove(img)
    return a


def unity_set(stem, model, part, alpha=False):
    """Poly Haven maps -> <stem>_BaseColor (A = cut-out), _Normal (OpenGL), _MaskMap (R metal, G AO, A smooth)."""
    os.makedirs(YARD_TEX, exist_ok=True)
    m = ph_maps(model, part)
    diff = load_px(m['diff']); arm = load_px(m['arm']); nrm = load_px(m['nor_gl'])
    base = np.ones(diff.shape[:2] + (4,), np.float32)
    base[..., :3] = diff[..., :3]
    if alpha:
        base[..., 3] = load_px(m['alpha'])[..., 0]
    mask = np.zeros_like(base)
    mask[..., 0] = arm[..., 2]; mask[..., 1] = 0.3 + 0.7 * arm[..., 0]; mask[..., 3] = 1.0 - arm[..., 1]
    paths = {k: os.path.join(YARD_TEX, f'{stem}_{k}.png') for k in ('BaseColor', 'Normal', 'MaskMap')}
    W.save_png(base, paths['BaseColor'], 4)
    W.save_png(nrm[..., :3], paths['Normal'], 3)
    W.save_png(mask, paths['MaskMap'], 4)
    return [os.path.basename(p) for p in paths.values()]


def build_fence():
    W.reset()
    model = 'modular_chainlink_fence'
    objs = W.import_glb(os.path.join(RAW_PH, model, f'{model}_1k.gltf'))
    src = next(o for o in objs if o.type == 'MESH' and o.name == f'{model}_double')
    o = W.join_copy([src], 'yd_chainlink_fence_LOD0')
    for x in objs:
        if x.name in bpy.data.objects:
            bpy.data.objects.remove(x)
    me = o.data
    vs = np.array([v.co[:] for v in me.vertices]); mn, mx = vs.min(0), vs.max(0)
    me.transform(Matrix.Translation(Vector((-(mn[0] + mx[0]) / 2, -(mn[1] + mx[1]) / 2, -mn[2]))))
    names = [m.name for m in me.materials]
    log('fence materials', names, 'tris', W.tris(o))
    wire_i = next(i for i, n in enumerate(names) if 'wire' in n)
    # split: wire as it is, posts decimated
    post = W.dup(o, 'posts'); wire = W.dup(o, 'wire')
    for ob, keep in ((post, lambda f: f.material_index != wire_i), (wire, lambda f: f.material_index == wire_i)):
        bm = bmesh.new(); bm.from_mesh(ob.data)
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if not keep(f)], context='FACES')
        bm.to_mesh(ob.data); bm.free()
    log('fence wire tris', W.tris(wire), 'posts tris', W.tris(post))
    pm = bpy.data.materials.new('M_yd_fence_posts'); wm = bpy.data.materials.new('M_yd_fence_wire_cutout')
    out = []
    for lod, target in ((0, 260), (1, 90)):
        p = W.lod_under(post, target, f'p{lod}', planar_first=False)
        wcopy = W.dup(wire, f'w{lod}')
        for ob, mat in ((p, pm), (wcopy, wm)):
            ob.data.materials.clear(); ob.data.materials.append(mat)
        j = W.join_copy([p, wcopy], f'yd_chainlink_fence_LOD{lod}')
        bpy.data.objects.remove(p); bpy.data.objects.remove(wcopy)
        out.append(j)
    for x in (o, post, wire):
        bpy.data.objects.remove(x)
    W.export_fbx(out, os.path.join(YARD_OUT, 'yd_chainlink_fence.fbx'))
    tex = unity_set('yd_fence_posts', model, 'posts') + unity_set('yd_fence_wire_cutout', model, 'wire', alpha=True)
    sz = [round(float(x), 3) for x in (mx - mn)]
    return {'yd_chainlink_fence': dict(fbx='world_v3/v3_yard/yd_chainlink_fence.fbx', kind='prop', tris=[W.tris(x) for x in out], size_m=sz,
                                       materials=['M_yd_fence_posts', 'M_yd_fence_wire_cutout'], textures=tex,
                                       tags=['prop', 'kit:v3', 'yard', 'fence'], source=f'https://polyhaven.com/a/{model}', source_uid='ph_' + model,
                                       licence='CC0', credit=ph_credit(model) + '; modified by CT Life (the double panel, posts decimated)')}


def main():
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(YARD_OUT, exist_ok=True)
    todo = [a for a in ARGS if not a.startswith('--')] or ['cards', 'bushes', 'fence']
    man, yard = {}, {}
    if 'cards' in todo:
        man.update(build_cards())
    if 'bushes' in todo:
        man.update(build_bushes())
    if 'fence' in todo:
        yard.update(build_fence())
    for v in man.values():
        v['family'] = 'v3_plants'
    for v in yard.values():
        v['family'] = 'v3_yard'
    if man:
        W.write_manifest(os.path.join(OUT, 'manifest.json'), man)
    if yard:
        W.write_manifest(os.path.join(YARD_OUT, 'manifest.json'), yard)
    log('done', sorted(man), sorted(yard))


main()
