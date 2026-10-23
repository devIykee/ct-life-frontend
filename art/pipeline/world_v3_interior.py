"""World v3 walk-in interiors: a Sketchfab room -> one baked, collidable, enterable room at character scale.

Usage: Blender -b --factory-startup -P world_v3_interior.py -- <id> [--preview-only] [--samples N]
In:  ct-life/art/raw/sketchfab/<uid>/<uid>.glb (INTERIORS[id] below)
Out: art/processed/world_v3/v3_interiors/<id>.fbx, textures/<id>_baked.png, manifest.json entry (kind 'interior_room')

Steps
  1. import, uniform scale (the room's floor-to-ceiling to a real 2.8 m), crop to the flat (the pack's balcony and street
     strip go), cut the entrance (a 1.0 x 2.15 m opening at the given wall), then move it so the entrance's floor centre is
     the origin with +Y pointing into the room (the same convention as a building's door_main);
  2. decimate planar walls to the triangle budget;
  3. one lightmap-style UV layout over the whole room and a Cycles bake: albedo (4096, 1 sample), indirect + direct
     lighting (1024, many samples: ceiling area lights, daylight through the window, low sky), emission (lamps; window
     glass baked as soft daylight so it never shows the void behind the building's walls), alpha (leaf cut-outs);
     final = albedo x light + emission, one RGBA texture, one material (Unity: CT Life/World/Interior Baked, no runtime
     lights);
  4. colliders: everything between 0.25 m and 1.7 m above the floor rasterised on a 10 cm grid and merged into boxes
     (walls full height, furniture its own height), plus the floor slab;
  5. seats (sofa / chairs) from the config, in the same room space.
Coordinates in the manifest are Blender's (x right, y into the room, z up), metres, origin = entrance floor centre.
"""
import bpy, bmesh, sys, os, json, math, time
sys.path.insert(0, os.path.dirname(__file__))
import numpy as np
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
import world_v3_lib as W

RAW = os.environ.get('CTL_SF_RAW', '/Users/mac/Documents/codes/projects/ct-life/art/raw/sketchfab')
OUT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', 'processed', 'world_v3', 'v3_interiors'))

INTERIORS = {
    # Katydid "Modern apartment interior" (CC BY): a one-bedroom-sized living/dining/kitchen flat with an entrance hall.
    # SketchUp units: floor top at 86.7, ceiling soffit at 1439 -> 2.8 m ceiling at scale 0.002071.
    'int_apartment_modern': dict(
        uid='400c9069181a4342a7142433dfa3466e', scale=0.002071, floor=0.18,
        crop=(-20.75, 5.75, -10.6, 15.7),                 # x0, y0, x1, y1 (scaled source metres)
        door=dict(x=-15.66, y_probe=13.0, dir=(0, 1), width=1.1, height=2.2),   # = the building cut (cut_door through=True)
        tris=40000, light_px=2048, albedo_px=4096,
        lights=[  # (x, y, z, size, watts, kelvin) in scaled source metres; z is ceiling height
            (-17.6, 9.6, 2.92, 1.4, 260, 3600), (-17.6, 7.2, 2.92, 1.4, 220, 3600), (-13.3, 8.3, 2.92, 1.2, 220, 3600),
            (-13.3, 11.5, 2.92, 1.0, 160, 3600), (-15.7, 13.6, 2.92, 0.8, 90, 3400)],
        window=dict(y=5.9, x0=-20.4, x1=-11.0, z0=0.4, z1=2.6, watts=900),   # daylight panel outside the balcony glazing
        sky=0.35,
        backdrop=dict(x0=-5.9, x1=5.1, y=10.0, z0=-0.1, z1=3.2, strength=1.5),
        # sit points (room space, seat-top height, facing yaw: 0 = +Y, 90 = +X): the L-sofa's long run faces the balcony,
        # its short run the TV wall; the two dining chairs face the table
        # only seats a body can reach: the L-sofa's coffee table closes its front, so its open end (sofa_3) is the sofa seat;
        # approach = where the sitter stands before sitting (room space)
        seats=[dict(id='sofa_3', kind='sofa', at=[2.35, 4.75, 0.45], yaw=0, approach=[3.2, 5.3]),
               dict(id='chair_1', kind='seat', at=[-2.7, 8.4, 0.47], yaw=180, approach=[-2.7, 9.05]),
               dict(id='chair_2', kind='seat', at=[-2.7, 6.65, 0.47], yaw=0, approach=[-2.7, 6.0])],
        opaque=['auto_55'],                                  # the coffee table's glass: baked solid, not cut out
    ),
}


def log(*a):
    print('[interior]', *a, flush=True)


def kelvin_rgb(k):
    t = k / 100.0
    r = 1.0 if t <= 66 else min(1.0, 1.292936186 * ((t - 60) ** -0.1332047592))
    g = (0.39008157876 * math.log(t) - 0.63184144378) if t <= 66 else 1.129890861 * ((t - 60) ** -0.0755148492)
    b = 1.0 if t >= 66 else (0.0 if t <= 19 else 0.54320678911 * math.log(t - 10) - 1.19625408914)
    return (max(0, min(1, r)), max(0, min(1, g)), max(0, min(1, b)))


def import_room(cfg):
    W.reset()
    W.import_glb(os.path.join(RAW, cfg['uid'], cfg['uid'] + '.glb'))
    s = cfg['scale']
    for o in bpy.data.objects:
        if o.parent is None:
            o.scale = o.scale * s
            o.location = o.location * s
    bpy.context.view_layer.update()
    ms = [o for o in bpy.data.objects if o.type == 'MESH' and o.data.polygons]
    parts = []
    for i, o in enumerate(ms):
        c = o.copy(); c.data = o.data.copy(); c.parent = None
        c.matrix_world = o.matrix_world.copy()
        bpy.context.scene.collection.objects.link(c)
        c.data.transform(c.matrix_world); c.matrix_world = Matrix.Identity(4)
        parts.append(c)
    for o in ms:
        bpy.data.objects.remove(o)
    room = W.join_parts(parts, 'room')
    log('imported', W.tris(room), 'tris', len(room.material_slots), 'materials')
    return room


def crop(room, box):
    x0, y0, x1, y1 = box
    bm = bmesh.new(); bm.from_mesh(room.data)
    gone = [f for f in bm.faces if not (x0 <= f.calc_center_median().x <= x1 and y0 <= f.calc_center_median().y <= y1)]
    bmesh.ops.delete(bm, geom=gone, context='FACES')
    bm.to_mesh(room.data); bm.free()
    log('cropped', len(gone), 'faces ->', W.tris(room))


def cut_entrance(room, cfg):
    d = cfg['door']
    bvh = BVHTree.FromObject(room, bpy.context.evaluated_depsgraph_get())
    z = cfg['floor'] + 1.2
    hit = bvh.ray_cast(Vector((d['x'], d['y_probe'], z)), Vector((d['dir'][0], d['dir'][1], 0)), 10.0)
    if hit[0] is None:
        raise RuntimeError('no wall for the entrance')
    wy = hit[0].y
    # the far face of the wall (it is a solid wall: two faces)
    hit2 = bvh.ray_cast(Vector((d['x'], wy + 0.002, z)), Vector((0, 1, 0)), 1.0)
    wy2 = hit2[0].y if hit2[0] is not None and hit2[0].y - wy < 0.6 else wy + 0.2
    log('entrance wall at y', round(wy, 3), '..', round(wy2, 3))
    f = cfg['floor']
    x0, x1, z1 = d['x'] - d['width'] / 2, d['x'] + d['width'] / 2, f + d['height']
    # no boolean (the room is not manifold: EXACT dropped 90 % of it): split the wall faces on the opening's three edges,
    # delete what lies inside, and close the wall's thickness with jamb and head faces
    bm = bmesh.new(); bm.from_mesh(room.data)
    uvl = bm.loops.layers.uv.active

    def wall_faces():
        return [q for q in bm.faces if abs(q.normal.y) > 0.9 and wy - 0.03 <= q.calc_center_median().y <= wy2 + 0.03
                and min(v.co.x for v in q.verts) < x1 + 0.01 and max(v.co.x for v in q.verts) > x0 - 0.01
                and min(v.co.z for v in q.verts) < z1 + 0.01]
    for co, no in (((x0, 0, 0), (1, 0, 0)), ((x1, 0, 0), (1, 0, 0)), ((0, 0, z1), (0, 0, 1))):
        fs = wall_faces()
        geom = list({e for q in fs for e in q.edges}) + fs + list({v for q in fs for v in q.verts})
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no)
    inside = [q for q in wall_faces() if x0 - 0.005 < q.calc_center_median().x < x1 + 0.005 and q.calc_center_median().z < z1]
    if not inside:
        raise RuntimeError('entrance: no wall faces inside the opening')
    mat = inside[0].material_index
    uv0 = inside[0].loops[0][uvl].uv.copy() if uvl else None
    bmesh.ops.delete(bm, geom=inside, context='FACES')
    def quad(pts):
        vs = [bm.verts.new(p) for p in pts]
        q = bm.faces.new(vs); q.material_index = mat
        if uvl:
            for l in q.loops:
                l[uvl].uv = uv0
        return q
    quad([(x0, wy, f), (x0, wy2, f), (x0, wy2, z1), (x0, wy, z1)])
    quad([(x1, wy, f), (x1, wy, z1), (x1, wy2, z1), (x1, wy2, f)])
    quad([(x0, wy, z1), (x0, wy2, z1), (x1, wy2, z1), (x1, wy, z1)])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[-3:])
    bm.to_mesh(room.data); bm.free()
    log('entrance opening: removed', len(inside), 'wall faces')
    # room space: entrance floor centre at the origin, +Y into the room (the room lay towards -Y of the wall)
    T = Matrix.Rotation(math.pi, 4, 'Z') @ Matrix.Translation(Vector((-d['x'], -wy2, -f)))
    room.data.transform(T); room.data.update()
    log('entrance cut; wall thickness', round(wy2 - wy, 3), 'tris', W.tris(room))
    return T


def backdrop(room, b):
    """A daylight panel just outside the window wall (room space): the pack's glazing is open, so in the game the window
    would show the inside of the building's shell; the panel is baked as soft daylight (emission) and lights the room too."""
    x0, x1, y, z0, z1 = b['x0'], b['x1'], b['y'], b['z0'], b['z1']
    me = bpy.data.meshes.new('backdrop')
    me.from_pydata([(x0, y, z0), (x1, y, z0), (x1, y, z1), (x0, y, z1)], [], [(0, 1, 2, 3)])   # faces -Y, into the room
    uv = me.uv_layers.new(name=room.data.uv_layers[0].name)
    for li, (u, v) in zip(range(4), ((0, 0), (0, 1), (1, 1), (1, 0))):
        uv.data[li].uv = (u, v)
    m = bpy.data.materials.new('backdrop_daylight'); m.use_nodes = True
    bsdf = m.node_tree.nodes['Principled BSDF']
    bsdf.inputs['Base Color'].default_value = (0, 0, 0, 1)
    bsdf.inputs['Emission Color'].default_value = (0.84, 0.89, 0.96, 1)
    bsdf.inputs['Emission Strength'].default_value = b.get('strength', 1.4)
    me.materials.append(m)
    o = bpy.data.objects.new('backdrop', me); bpy.context.scene.collection.objects.link(o)
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); room.select_set(True)
    bpy.context.view_layer.objects.active = room
    bpy.ops.object.join()
    log('daylight backdrop at y', y)


def dedupe_coplanar(room):
    """SketchUp exports carry a second, untextured copy of most faces (the default 'material_1' layer): they z-fight the
    textured ones and, once vertices are merged, bmesh keeps whichever comes first. Drop a face when another face with
    the same corners (1 mm) has a textured material, else a less used (more specific) one."""
    tex = []
    for sl in room.material_slots:
        m = sl.material
        tex.append(bool(m and any(n.type == 'TEX_IMAGE' for n in m.node_tree.nodes)))
    bm = bmesh.new(); bm.from_mesh(room.data)
    use = {}
    for f in bm.faces:
        use[f.material_index] = use.get(f.material_index, 0) + 1      # the blanket layer is the most used material
    seen = {}
    for f in bm.faces:
        key = tuple(sorted(tuple(round(c * 1000) for c in v.co) for v in f.verts))
        seen.setdefault(key, []).append(f)
    gone = []
    for fs in seen.values():
        if len(fs) < 2:
            continue
        fs.sort(key=lambda f: (not tex[f.material_index] if f.material_index < len(tex) else True, use[f.material_index]))
        gone += fs[1:]
    bmesh.ops.delete(bm, geom=gone, context='FACES')
    bm.to_mesh(room.data); bm.free()
    log('coplanar duplicates removed', len(gone))


def bake_uvs(room):
    me = room.data
    src = me.uv_layers[0]
    src.active_render = True
    uv = me.uv_layers.new(name='bake')
    me.uv_layers.active = uv
    bpy.ops.object.select_all(action='DESELECT'); room.select_set(True); bpy.context.view_layer.objects.active = room
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.002, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.uv.pack_islands(margin=0.0005, rotate=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    src.active_render = True
    uv = me.uv_layers['bake']
    a = np.empty(len(me.loops) * 2, np.float32); uv.data.foreach_get('uv', a); a = a.reshape(-1, 2)
    if not np.isfinite(a).all():
        log('bake UVs: non-finite', int((~np.isfinite(a)).sum() // 2), 'loops -> 0')
        a[~np.isfinite(a)] = 0.0
        uv.data.foreach_set('uv', a.ravel())
    cov = sum(0.5 * abs(np.dot(q[:, 0], np.roll(q[:, 1], 1)) - np.dot(q[:, 1], np.roll(q[:, 0], 1)))
              for q in (a[p.loop_start:p.loop_start + p.loop_total] for p in me.polygons))
    log('bake UVs packed, coverage', round(float(cov), 3))


def is_glass(m):
    n = m.name.lower()
    if any(k in n for k in ('glass', 'window', 'mirror')):
        return True
    return getattr(m, 'surface_render_method', '') == 'BLENDED' and not any(
        l.to_socket.name == 'Alpha' and l.from_node.type == 'TEX_IMAGE' for l in m.node_tree.links)


def prepare_materials(room, img, src_uv):
    """Every material: the bake target node; glass -> daylight emission; keeps a copy of the original surface link."""
    info = {}
    for sl in room.material_slots:
        m = sl.material
        if m is None or m.name in info:
            continue
        nt = m.node_tree
        # Cycles bakes into the active *render* UV map, which also feeds every texture without an explicit UV input:
        # pin the material's textures to the source UVs first, then the bake layout becomes the render UVs
        for n in list(nt.nodes):
            if n.type == 'TEX_IMAGE' and not n.inputs['Vector'].is_linked:
                uvn = nt.nodes.new('ShaderNodeUVMap'); uvn.uv_map = src_uv
                nt.links.new(uvn.outputs['UV'], n.inputs['Vector'])
        t = nt.nodes.new('ShaderNodeTexImage'); t.name = 'BAKE'; t.image = img
        nt.nodes.active = t
        bsdf = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        alpha_link = None
        if bsdf is not None:
            for l in nt.links:
                if l.to_node == bsdf and l.to_socket.name == 'Alpha':
                    alpha_link = (l.from_node, l.from_socket.name)
        info[m.name] = dict(glass=is_glass(m), bsdf=bsdf, alpha=alpha_link)
    log('materials', len(info), 'glass', [k for k, v in info.items() if v['glass']], 'alpha', [k for k, v in info.items() if v['alpha'] and not v['glass']])
    return info


def set_target(room, img):
    for sl in room.material_slots:
        if sl.material:
            n = sl.material.node_tree.nodes.get('BAKE'); n.image = img; sl.material.node_tree.nodes.active = n


def glass_as_daylight(room, info, on):
    for sl in room.material_slots:
        m = sl.material
        if not m or not info.get(m.name, {}).get('glass') or info[m.name]['bsdf'] is None:
            continue
        b = info[m.name]['bsdf']
        if on:
            b.inputs['Base Color'].default_value = (0, 0, 0, 1)
            for l in list(m.node_tree.links):
                if l.to_node == b and l.to_socket.name in ('Base Color', 'Alpha', 'Transmission Weight', 'Emission Color'):
                    m.node_tree.links.remove(l)
            b.inputs['Alpha'].default_value = 1.0
            if 'Transmission Weight' in b.inputs:
                b.inputs['Transmission Weight'].default_value = 0.0
            b.inputs['Emission Color'].default_value = (0.82, 0.88, 0.95, 1)
            b.inputs['Emission Strength'].default_value = 1.6


def bake(kind, img, samples, margin, pass_filter=None):
    sc = bpy.context.scene
    sc.cycles.samples = samples
    sc.render.bake.margin = margin
    sc.render.bake.use_clear = True
    t0 = time.time()
    if pass_filter is None:
        bpy.ops.object.bake(type=kind, margin=margin, use_clear=True)
    else:
        bpy.ops.object.bake(type=kind, pass_filter=pass_filter, margin=margin, use_clear=True)
    log('baked', kind, img.size[:], f'{time.time() - t0:.0f}s')


def img_np(img):
    a = np.empty(img.size[0] * img.size[1] * 4, np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(img.size[1], img.size[0], 4)


def add_lights(cfg, T):
    sc = bpy.context.scene
    for i, (x, y, z, size, watts, k) in enumerate(cfg['lights']):
        L = bpy.data.lights.new(f'ceil{i}', 'AREA'); L.size = size; L.energy = watts; L.color = kelvin_rgb(k)
        o = bpy.data.objects.new(f'ceil{i}', L); sc.collection.objects.link(o)
        o.location = T @ Vector((x, y, z - 0.05)); o.rotation_euler = (0, 0, 0)          # area lights face -Z
    w = cfg['window']
    L = bpy.data.lights.new('daylight', 'AREA'); L.shape = 'RECTANGLE'; L.size = w['x1'] - w['x0']; L.size_y = w['z1'] - w['z0']
    L.energy = w['watts']; L.color = (0.86, 0.92, 1.0)
    o = bpy.data.objects.new('daylight', L); sc.collection.objects.link(o)
    p = T @ Vector(((w['x0'] + w['x1']) / 2, w['y'] - 0.35, cfg['floor'] + (w['z0'] + w['z1']) / 2))
    o.location = p
    # face into the room: the window was at low source y; room space flipped it to high y -> light points -Y
    o.rotation_euler = (math.radians(-90), 0, 0) if (T @ Vector((0, w['y'], 0))).y > (T @ Vector((0, w['y'] + 1, 0))).y else (math.radians(90), 0, 0)
    wd = bpy.data.worlds.new('w'); sc.world = wd; wd.use_nodes = True
    wd.node_tree.nodes['Background'].inputs[0].default_value = (0.7, 0.78, 0.9, 1)
    wd.node_tree.nodes['Background'].inputs[1].default_value = cfg['sky']


def colliders(room, floor_top=0.0):
    """Boxes [cx, cy, cz, sx, sy, sz] (room space) from a 10 cm occupancy grid of the geometry 0.25-1.7 m up."""
    me = room.data
    me.calc_loop_triangles()
    V = np.array([v.co[:] for v in me.vertices])
    tri = np.array([t.vertices[:] for t in me.loop_triangles])
    A, B, C = V[tri[:, 0]], V[tri[:, 1]], V[tri[:, 2]]
    pts = []
    step = 0.04
    for a, b, c in zip(A, B, C):
        la, lb = np.linalg.norm(b - a), np.linalg.norm(c - a)
        n1, n2 = max(1, int(la / step)), max(1, int(lb / step))
        u, v = np.meshgrid(np.linspace(0, 1, n1 + 1), np.linspace(0, 1, n2 + 1))
        m = (u + v) <= 1.0
        p = a + np.outer(u[m], b - a) + np.outer(v[m], c - a)
        pts.append(p)
    P = np.concatenate(pts)
    lo, hi = floor_top + 0.25, floor_top + 1.7
    P = P[(P[:, 2] >= lo) & (P[:, 2] <= hi)]
    # the top of each column, from all points (for furniture heights)
    Pall = np.concatenate(pts)
    cell = 0.1
    mn = P[:, :2].min(0) - cell
    ix = ((P[:, :2] - mn) / cell).astype(int)
    nx, ny = ix.max(0) + 2
    occ = np.zeros((nx, ny), bool)
    occ[ix[:, 0], ix[:, 1]] = True
    top = np.zeros((nx, ny), np.float32)
    ia = ((Pall[:, :2] - mn) / cell).astype(int)
    ok = (ia[:, 0] >= 0) & (ia[:, 1] >= 0) & (ia[:, 0] < nx) & (ia[:, 1] < ny) & (Pall[:, 2] < 2.55)
    np.maximum.at(top, (ia[ok, 0], ia[ok, 1]), Pall[ok, 2].astype(np.float32))
    # height class per cell: walls / tall furniture run to the ceiling, the rest to their own top (0.1 m steps)
    hcls = np.where(top > 2.0, 28, np.ceil(np.maximum(top, lo) * 10)).astype(int)
    hcls[~occ] = 0
    boxes = []
    used = np.zeros_like(occ)
    for i in range(nx):
        for j in range(ny):
            if not occ[i, j] or used[i, j]:
                continue
            h = hcls[i, j]
            j1 = j
            while j1 + 1 < ny and occ[i, j1 + 1] and not used[i, j1 + 1] and hcls[i, j1 + 1] == h:
                j1 += 1
            i1 = i
            while i1 + 1 < nx and all(occ[i1 + 1, k] and not used[i1 + 1, k] and hcls[i1 + 1, k] == h for k in range(j, j1 + 1)):
                i1 += 1
            used[i:i1 + 1, j:j1 + 1] = True
            x0, y0 = mn + np.array([i, j]) * cell
            x1, y1 = mn + np.array([i1 + 1, j1 + 1]) * cell
            hz = h / 10.0
            boxes.append([round((x0 + x1) / 2, 3), round((y0 + y1) / 2, 3), round(hz / 2, 3), round(x1 - x0, 3), round(y1 - y0, 3), round(hz, 3)])
    log('colliders', len(boxes), 'boxes from', int(occ.sum()), 'cells')
    return boxes


def main():
    a = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    rid = a[0]
    preview = '--preview-only' in a
    samples = int(a[a.index('--samples') + 1]) if '--samples' in a else 256
    cfg = INTERIORS[rid]
    room = import_room(cfg)
    crop(room, cfg['crop'])
    T = cut_entrance(room, cfg)
    # one connected surface per object (the glTF import split every vertex at UV and normal seams: thousands of loose
    # triangles made 98 % of the bake atlas margin), then normals by angle for the bake
    dedupe_coplanar(room)
    W.clean(room, merge=0.001)
    bm = bmesh.new(); bm.from_mesh(room.data)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=2e-4)
    bad = [f for f in bm.faces if f.calc_area() < 1e-7]
    bmesh.ops.delete(bm, geom=bad, context='FACES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    bm.to_mesh(room.data); bm.free()
    bpy.ops.object.select_all(action='DESELECT'); room.select_set(True); bpy.context.view_layer.objects.active = room
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
    if W.tris(room) > cfg['tris']:
        parts = [room]
        red = W.decimate_parts(parts, cfg['tris'], min_part=0.03, min_tris=4, planar=True)
        room = W.join_parts(red, 'room')
    log('room tris', W.tris(room))
    if cfg.get('backdrop'):
        backdrop(room, cfg['backdrop'])
    vs = np.array([v.co[:] for v in room.data.vertices])
    bmin, bmax = vs.min(0), vs.max(0)
    log('room bounds', bmin.round(2).tolist(), bmax.round(2).tolist())
    os.makedirs(os.path.join(OUT, 'textures'), exist_ok=True)
    boxes = colliders(room)
    if preview:
        json.dump(dict(bounds=[bmin.tolist(), bmax.tolist()], colliders=boxes), open(os.path.join(OUT, rid + '_preview.json'), 'w'), indent=0)
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, rid + '_preview.blend'))
        return
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    try:
        prefs = bpy.context.preferences.addons['cycles'].preferences
        prefs.compute_device_type = 'METAL'
        prefs.get_devices()
        for dv in prefs.devices:
            dv.use = True
        sc.cycles.device = 'GPU'
        log('cycles device', [d.name for d in prefs.devices if d.use])
    except Exception as e:
        log('cycles GPU unavailable', e)
    bake_uvs(room)
    ap, lp = cfg['albedo_px'], cfg['light_px']
    alb = bpy.data.images.new('alb', ap, ap, alpha=True, float_buffer=True)
    lit = bpy.data.images.new('lit', lp, lp, alpha=False, float_buffer=True)
    emi = bpy.data.images.new('emi', lp, lp, alpha=False, float_buffer=True)
    info = prepare_materials(room, alb, room.data.uv_layers[0].name)
    room.data.uv_layers['bake'].active_render = True
    room.data.uv_layers.active = room.data.uv_layers['bake']
    glass_as_daylight(room, info, True)
    bpy.ops.object.select_all(action='DESELECT'); room.select_set(True); bpy.context.view_layer.objects.active = room
    add_lights(cfg, T)
    if dbg_dir := os.environ.get('CTL_INTERIOR_DEBUG'):
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(dbg_dir, 'prebake.blend'), copy=True)
    set_target(room, alb); bake('DIFFUSE', alb, 1, 16, {'COLOR'})
    set_target(room, lit); bake('DIFFUSE', lit, samples, 6, {'DIRECT', 'INDIRECT'})
    set_target(room, emi); bake('EMIT', emi, 1, 6)
    # alpha: the cut-out materials' alpha as emission
    am = None
    # cut-outs only where the alpha map is mostly clear (plant leaves); blinds and decals with a nearly opaque map stay
    # solid (a cut-out there shows the building's inside through the slats)
    def alpha_mean(v):
        fn = v['alpha'][0]
        im = fn.image if fn.type == 'TEX_IMAGE' else None
        if im is None or not im.size[0]:
            return 1.0
        return float(np.array(im.pixels[:], np.float32).reshape(-1, 4)[:, 3].mean())
    cut = [k for k, v in info.items() if v['alpha'] and not v['glass'] and alpha_mean(v) < 0.6 and k not in cfg.get('opaque', [])]
    log('cut-out materials', cut)
    if cut:
        aim = bpy.data.images.new('alpha', ap, ap, alpha=False, float_buffer=False)
        for sl in room.material_slots:
            m = sl.material
            if not m:
                continue
            b = info[m.name]['bsdf']
            nt = m.node_tree
            em = nt.nodes.new('ShaderNodeEmission'); out = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')
            if m.name in cut:
                fn, fs = info[m.name]['alpha']; nt.links.new(fn.outputs[fs], em.inputs['Strength'])
            else:
                em.inputs['Strength'].default_value = 1.0
            em.inputs['Color'].default_value = (1, 1, 1, 1)
            nt.links.new(em.outputs[0], out.inputs['Surface'])
        set_target(room, aim); bake('EMIT', aim, 1, 16)
        am = img_np(aim)[..., 0]
    A = img_np(alb)
    dbg = os.environ.get('CTL_INTERIOR_DEBUG')
    if dbg:
        os.makedirs(dbg, exist_ok=True)
        for nm, im in (('alb', alb), ('lit', lit), ('emi', emi)):
            x = img_np(im)
            np.save(os.path.join(dbg, nm + '.npy'), x[::4, ::4] if x.shape[0] > 2048 else x)
            log('debug', nm, x.shape, 'mean', x[..., :3].mean().round(4), 'max', x[..., :3].max().round(3))
    from math import ceil
    def up(x):
        r = ap // x.shape[0]
        return np.repeat(np.repeat(x, r, 0), r, 1)
    # bilinear-ish upsample: repeat then a small box blur of the light (it is low frequency)
    Lt = img_np(lit)[..., :3]
    E = img_np(emi)[..., :3]
    try:
        from scipy import ndimage as nd
        Lu = nd.zoom(Lt, (ap / lp, ap / lp, 1), order=1)
        Eu = nd.zoom(E, (ap / lp, ap / lp, 1), order=1)
    except Exception:
        Lu, Eu = up(Lt), up(E)
    rgb = A[..., :3] * Lu + Eu
    alpha = am if am is not None else np.ones(rgb.shape[:2], np.float32)
    # store in display-referred sRGB at 1.0 = the room's white wall under the lights (Unity: unlit, x exposure)
    exposure = cfg.get('exposure', 1.0)
    rgb = rgb * exposure
    out = np.concatenate([rgb, alpha[..., None]], axis=2)
    stats = np.percentile(rgb[A[..., 3] > 0.5].mean(1), [50, 90, 99]).round(3).tolist() if (A[..., 3] > 0.5).any() else []
    log('baked radiance percentiles 50/90/99', stats)
    img = bpy.data.images.new(rid + '_baked', ap, ap, alpha=True, float_buffer=True)
    img.pixels.foreach_set(np.clip(out, 0, 64).astype(np.float32).ravel())
    p = os.path.join(OUT, 'textures', f'{rid}_baked.png')
    img.filepath_raw = p; img.file_format = 'PNG'
    sc.render.image_settings.color_depth = '8'
    img.save_render(p)
    # final mesh: bake UVs only, one material
    me = room.data
    keep = me.uv_layers['bake']
    for l in [l for l in me.uv_layers if l.name != 'bake']:
        me.uv_layers.remove(l)
    keep.name = 'UVMap'
    me.materials.clear()
    mat = bpy.data.materials.new(f'M_{rid}_baked'); me.materials.append(mat)
    room.name = room.data.name = rid
    for o in [o for o in bpy.data.objects if o.type == 'LIGHT']:
        bpy.data.objects.remove(o)
    W.export_fbx([room], os.path.join(OUT, rid + '.fbx'))
    vs = np.array([v.co[:] for v in room.data.vertices])
    entry = {rid: dict(fbx=f'world_v3/v3_interiors/{rid}.fbx', kind='interior_room', tris=[W.tris(room)],
                       size_m=[round(float(x), 3) for x in (vs.max(0) - vs.min(0))], bounds=[vs.min(0).round(3).tolist(), vs.max(0).round(3).tolist()],
                       materials=[mat.name], textures=[f'{rid}_baked.png'], door=dict(at=[0, 0, 0], into='+y', width=cfg['door']['width'], height=cfg['door']['height']),
                       colliders=boxes, seats=cfg.get('seats', []), tags=['interior', 'kit:v3', 'enterable'],
                       source=f'https://sketchfab.com/3d-models/{cfg["uid"]}', source_uid=cfg['uid'])}
    m = json.load(open(os.path.join(RAW, cfg['uid'], 'meta.json')))
    entry[rid]['licence'] = 'CC Attribution' if 'Attribution' in str(m.get('license')) else str(m.get('license'))
    import ast
    user = m.get('user'); user = ast.literal_eval(user) if isinstance(user, str) and user.startswith('{') else (user or {})   # meta.json stores the API's dicts as Python reprs
    entry[rid]['credit'] = f'"{m.get("name")}" by {user.get("displayName", "?")} ({m.get("viewerUrl", "")}), CC BY 4.0; modified by CT Life (cropped, entrance cut, decimated, lighting baked)'
    W.write_manifest(os.path.join(OUT, 'manifest.json'), entry)
    log('done', rid, entry[rid]['tris'], entry[rid]['size_m'])


main()
