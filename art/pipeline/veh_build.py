"""CT Life vehicles (Feel lane F2, 2026-10-17): ready-made CC0 / CC-BY models rigged for driving and flight.

Usage: art/pipeline/blender.sh art/pipeline/veh_build.py -- [id,id,...]
Reads art/raw/polypizza/<source>.glb (provenance in art/raw/polypizza/_manifest.json), writes
art/processed/vehicles/<id>.fbx and art/processed/vehicles/vehicles.json (sizes, anchors, provenance).

Rigging done here, on the sourced meshes (no new modelling beyond a cabin block and a steering-wheel ring):
- every part re-pivoted where it turns (wheel centres, rotor hubs, door hinges);
- the rear-axle mesh split into its two wheels; the front doors cut out of the body along two clean planes (the
  side window goes with the door), so closed doors fill the hole exactly;
- anchors as empties: driver/passenger seats, entry points beside the doors, steering wheel, exhaust;
- scaled to real sizes, wheels on the ground (z = 0), body centred, front = -Y, left = +X (Blender), which the
  FBX export below turns into Unity's +Z forward, +X right.
"""
import bpy, bmesh, sys, os, json, math
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
SRC = os.path.join(REPO, 'art', 'raw', 'polypizza')
OUT = os.path.join(REPO, 'art', 'processed', 'vehicles')
ONLY = sys.argv[sys.argv.index('--') + 1].split(',') if '--' in sys.argv and len(sys.argv) > sys.argv.index('--') + 1 else None

# length = target body length in metres (real class sizes); paint = body colour override (RGB) for the body material.
SPECS = {
    'compact': dict(src='Car__unqqkULtRU.glb', kind='car', length=3.95, body_mats=['LightBlue'], paint=(0.93, 0.36, 0.24)),
    'suv': dict(src='SUV__xsMtZhBkxL.glb', kind='car', length=4.6),
    'sports': dict(src='Sports_Car__Gzj704DXdr.glb', kind='car', length=4.45),
    'roadstar': dict(src='Sports_Car__1mkmFkAz5v.glb', kind='car', length=4.3, body_mats=['White'], paint=(0.62, 0.02, 0.06)),
    'scooter': dict(src='Scooter__3vLV1QtLFP.glb', kind='scooter', length=1.85),
    'heli': dict(src='Helicopter__EQJ2MECUbx.glb', kind='heli', length=9.4),
    'drone': dict(src='Drone__DNbUoMtG3H.glb', kind='drone', length=0.62),
}


def manifest_entry(src):
    man = json.load(open(os.path.join(SRC, '_manifest.json')))
    for m in man:
        if m.get('local_path') == src:
            return {k: m.get(k) for k in ('title', 'creator', 'licence', 'source_page', 'download_url', 'downloaded')}
    return {}


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def meshes():
    return [o for o in bpy.context.scene.objects if o.type == 'MESH']


def bake_and_merge():
    """Apply every object's world transform to its mesh, unparent, merge split (flat-shaded) vertices."""
    for o in list(bpy.context.scene.objects):
        if o.type != 'MESH':
            continue
        mw = o.matrix_world.copy()
        o.parent = None
        o.data.transform(mw)
        o.matrix_world = Matrix.Identity(4)
        bm = bmesh.new(); bm.from_mesh(o.data)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0008)
        bm.to_mesh(o.data); bm.free()
    for o in list(bpy.context.scene.objects):
        if o.type != 'MESH':
            bpy.data.objects.remove(o, do_unlink=True)


def bounds(objs):
    pts = [o.matrix_world @ v.co for o in objs for v in o.data.vertices]
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return mn, mx


def transform_all(m):
    for o in meshes():
        o.data.transform(m)


def separate_loose(o):
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.separate(type='LOOSE')
    bpy.ops.object.mode_set(mode='OBJECT')
    return [x for x in bpy.context.selected_objects]


def join(objs, name):
    objs = [o for o in objs if o is not None]
    if not objs:
        return None
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = name; o.data.name = name
    return o


def set_origin(o, point):
    """Move the object origin to a world point without moving its geometry."""
    off = Matrix.Translation(-(point - o.location))
    o.data.transform(off)
    o.location = point


def center_origin(o):
    mn, mx = bounds([o])
    set_origin(o, (mn + mx) / 2)


def empty(name, loc, parent=None):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = 'PLAIN_AXES'; e.empty_display_size = 0.15
    e.location = loc
    bpy.context.scene.collection.objects.link(e)
    return e


def dark_material(name, rgb):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get('Principled BSDF')
    if bsdf:
        bsdf.inputs['Base Color'].default_value = (*rgb, 1)
        bsdf.inputs['Roughness'].default_value = 0.8
    return m


def paint(objs, names, rgb):
    for o in objs:
        for s in o.material_slots:
            if s.material and s.material.name in names:
                mat = s.material.copy(); mat.name = 'Paint'
                if mat.use_nodes:
                    b = mat.node_tree.nodes.get('Principled BSDF')
                    if b:
                        b.inputs['Base Color'].default_value = (*rgb, 1)
                        b.inputs['Metallic'].default_value = 0.35
                        b.inputs['Roughness'].default_value = 0.35
                mat.diffuse_color = (*rgb, 1)
                s.material = mat


def box(name, mn, mx, mat):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((mn.x + (v.co.x + 0.5) * (mx.x - mn.x), mn.y + (v.co.y + 0.5) * (mx.y - mn.y), mn.z + (v.co.z + 0.5) * (mx.z - mn.z)))
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    o.data.materials.append(mat)
    return o


def ring(name, center, radius, tube, tilt_deg, mat):
    bm = bmesh.new()
    bmesh.ops.create_circle(bm, segments=20, radius=radius)
    # thicken the circle into a ring of quads (a torus with 4 sides)
    me = bpy.data.meshes.new(name)
    verts = []
    for i in range(20):
        a = 2 * math.pi * i / 20
        for j in range(4):
            b = 2 * math.pi * j / 4
            r = radius + tube * math.cos(b)
            verts.append(Vector((r * math.cos(a), r * math.sin(a), tube * math.sin(b))))
    faces = []
    for i in range(20):
        for j in range(4):
            a, b = i * 4 + j, i * 4 + (j + 1) % 4
            c, d = ((i + 1) % 20) * 4 + (j + 1) % 4, ((i + 1) % 20) * 4 + j
            faces.append((a, b, c, d))
    bm.free()
    me.from_pydata([tuple(v) for v in verts], [], faces)
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    o.data.materials.append(mat)
    # the ring lies in the XY plane; stand it up facing the driver (-Y is forward), tilted back
    o.rotation_euler = (math.radians(90 - tilt_deg), 0, 0)
    o.location = center
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    return o


def cut_door(body, side, y0, y1, z0, z1, xmin, name):
    """Bisect the body at y0 and y1, then separate the side faces between them (|x| > xmin, facing out, z0..z1)."""
    bm = bmesh.new(); bm.from_mesh(body.data)
    for y in (y0, y1):
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, y, 0), plane_no=(0, 1, 0))
    bm.faces.ensure_lookup_table()
    bm.normal_update()
    sel = []
    for f in bm.faces:
        c = f.calc_center_median(); n = f.normal
        if y0 < c.y < y1 and side * c.x > xmin and side * n.x > 0.3 and z0 < c.z < z1:
            sel.append(f)
    for f in bm.faces:
        f.select_set(False)
    for f in sel:
        f.select_set(True)
    bm.select_mode = {'FACE'}
    bm.to_mesh(body.data); bm.free()
    if not sel:
        return None
    bpy.ops.object.select_all(action='DESELECT'); body.select_set(True); bpy.context.view_layer.objects.active = body
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_mode(type='FACE')
    bpy.ops.mesh.separate(type='SELECTED')
    bpy.ops.object.mode_set(mode='OBJECT')
    door = [o for o in bpy.context.selected_objects if o != body][0]
    door.name = name; door.data.name = name
    mn, mx = bounds([door])
    hinge = Vector(((mx.x if side > 0 else mn.x), mn.y, (mn.z + mx.z) / 2))   # front edge (-Y is forward)
    set_origin(door, hinge)
    return door, len(sel)


def build_car(vid, spec, rec):
    objs = meshes()
    wheels = {o: o for o in objs if 'wheel' in o.name.lower()}
    body_parts = [o for o in objs if o not in wheels]
    body = join(body_parts, 'Body')
    # scale to the class length, wheels on the ground, body centred
    mn, mx = bounds([body])
    k = spec['length'] / (mx.y - mn.y)
    transform_all(Matrix.Scale(k, 4))
    allmn, allmx = bounds(meshes())
    bmn, bmx = bounds([body])
    transform_all(Matrix.Translation(Vector((-(bmn.x + bmx.x) / 2, -(bmn.y + bmx.y) / 2, -allmn.z))))
    # wheels: split the rear axle, name by side (left = +X) and end (front = -Y)
    named = {}
    for o in list(wheels):
        parts = separate_loose(o)
        for p in parts:
            pmn, pmx = bounds([p]); c = (pmn + pmx) / 2
            key = ('F' if c.y < 0 else 'R') + ('L' if c.x > 0 else 'R')
            named.setdefault(key, []).append(p)
    wheel_info = {}
    for key, parts in named.items():
        w = join(parts, 'Wheel_' + key)
        center_origin(w)
        wmn, wmx = bounds([w])
        wheel_info[key] = {'center': list(map(lambda v: round(v, 4), w.location)), 'radius': round((wmx.z - wmn.z) / 2, 4),
                           'width': round(wmx.x - wmn.x, 4)}
    bmn, bmx = bounds([body])
    W = (bmx.x - bmn.x) / 2
    yf = (wheel_info['FL']['center'][1] + wheel_info['FR']['center'][1]) / 2
    yr = (wheel_info['RL']['center'][1] + wheel_info['RR']['center'][1]) / 2
    R = (wheel_info['FL']['radius'] + wheel_info['RL']['radius']) / 2
    wheelbase = yr - yf
    # front doors between the front arch and ~45 % of the wheelbase behind it
    y0 = yf + R + 0.08 * wheelbase
    y1 = y0 + 0.47 * wheelbase
    z0 = R * 0.62
    z1 = bmx.z - 0.12 * (bmx.z - bmn.z)
    doors = {}
    for side, key in ((1, 'L'), (-1, 'R')):
        res = cut_door(body, side, y0, y1, z0, z1, W * 0.62, 'Door_F' + key)
        if res:
            door, n = res
            doors[key] = {'hinge': [round(v, 4) for v in door.location], 'faces': n}
    cabin_mat = dark_material('Cabin', (0.06, 0.06, 0.07))
    cab = box('Cabin', Vector((-W * 0.86, y0 - 0.25, R * 0.75)), Vector((W * 0.86, y1 + 0.75 * (yr - y1), z0 + 0.55 * (z1 - z0))), cabin_mat)
    seat_z = R * 0.75 + 0.18
    driver = Vector((W * 0.42, y0 + 0.62 * (y1 - y0), seat_z))
    passenger = Vector((-W * 0.42, driver.y, seat_z))
    wheel_c = Vector((driver.x, driver.y - 0.48, seat_z + 0.40))
    ring('Steering', wheel_c, 0.17, 0.018, 24, dark_material('Steering', (0.08, 0.08, 0.09)))
    anchors = {
        'Seat_Driver': driver, 'Seat_Passenger': passenger,
        'Entry_Driver': Vector((W + 0.55, driver.y + 0.1, 0.0)), 'Entry_Passenger': Vector((-W - 0.55, driver.y + 0.1, 0.0)),
        'Exhaust': Vector((-W * 0.5, bmx.y - 0.05, R * 0.9)), 'SteeringCenter': wheel_c,
    }
    for name, p in anchors.items():
        empty(name, p)
    if spec.get('paint'):
        paint([body] + [o for o in meshes() if o.name.startswith('Door_')], spec.get('body_mats', []), spec['paint'])
    rec.update({'size': [round(2 * W, 3), round(bmx.y - bmn.y, 3), round(bmx.z, 3)], 'wheelbase': round(wheelbase, 3),
                'track': round(wheel_info['FL']['center'][0] - wheel_info['FR']['center'][0], 3), 'wheels': wheel_info, 'doors': doors,
                'anchors': {k: [round(v, 4) for v in p] for k, p in anchors.items()}})


def build_scooter(vid, spec, rec):
    objs = meshes()
    o = join(objs, 'Body')
    mn, mx = bounds([o])
    k = spec['length'] / (mx.y - mn.y)
    transform_all(Matrix.Scale(k, 4))
    parts = separate_loose(o)
    front, rear, body = [], [], []
    for p in parts:
        pmn, pmx = bounds([p]); c = (pmn + pmx) / 2; sx = pmx.x - pmn.x; sz = pmx.z - pmn.z; sy = pmx.y - pmn.y
        wheel_like = pmn.z < 0.05 * k + 0.02 and abs(sz - sy) < 0.25 * max(sz, sy) and sx < 0.35 * k
        if wheel_like and c.y < 0:
            front.append(p)
        elif wheel_like and c.y > 0:
            rear.append(p)
        else:
            body.append(p)
    body = join(body, 'Body')
    wf = join(front, 'Wheel_F'); wr = join(rear, 'Wheel_R')
    allmn, _ = bounds(meshes())
    bmn, bmx = bounds([body])
    transform_all(Matrix.Translation(Vector((-(bmn.x + bmx.x) / 2, -(bmn.y + bmx.y) / 2, -allmn.z))))
    info = {}
    for w, key in ((wf, 'F'), (wr, 'R')):
        if w is None:
            continue
        center_origin(w)
        wmn, wmx = bounds([w])
        info[key] = {'center': [round(v, 4) for v in w.location], 'radius': round((wmx.z - wmn.z) / 2, 4)}
    bmn, bmx = bounds([body])
    seat = Vector((0, 0.12 * (bmx.y - bmn.y), 0.70))   # seat top of a 125 cc scooter is ~0.78 m; hips sit just above
    anchors = {'Seat_Driver': seat, 'Entry_Driver': Vector((0.75, seat.y, 0)), 'Handlebar': Vector((0, bmn.y + 0.30 * (bmx.y - bmn.y), bmx.z * 0.62)),
               'Exhaust': Vector((-0.12, bmx.y - 0.1, 0.25))}
    for name, p in anchors.items():
        empty(name, p)
    rec.update({'size': [round(bmx.x - bmn.x, 3), round(bmx.y - bmn.y, 3), round(bmx.z, 3)], 'wheels': info,
                'anchors': {k2: [round(v, 4) for v in p] for k2, p in anchors.items()}})


def build_heli(vid, spec, rec):
    o = join(meshes(), 'Body')
    mn, mx = bounds([o])
    k = spec['length'] / (mx.y - mn.y)
    transform_all(Matrix.Scale(k, 4))
    parts = separate_loose(o)
    allmn, allmx = bounds(parts)
    top = allmx.z
    rotor, body = [], []
    for p in parts:
        pmn, pmx = bounds([p])
        thin_top = pmn.z > top - 0.18 * (top - allmn.z) and (pmx.z - pmn.z) < 0.3 * k
        hub = (pmx.x - pmn.x) < 0.2 * k and pmn.z > allmn.z + 0.7 * (top - allmn.z) and (pmx.z - pmn.z) < 0.5 * k
        (rotor if thin_top or hub else body).append(p)
    body = join(body, 'Body'); rotor = join(rotor, 'Rotor_Main')
    bmn, bmx = bounds([body])
    transform_all(Matrix.Translation(Vector((-(bmn.x + bmx.x) / 2, -(bmn.y + bmx.y) / 2, -bmn.z))))
    info = {}
    if rotor:
        rmn, rmx = bounds([rotor])
        # the hub: the narrow, tall part of the rotor group sits at the shaft; use the rotor bounds' x/y centre of the hub band
        hub_pts = [rotor.matrix_world @ v.co for v in rotor.data.vertices if v.co.z < rmn.z + 0.6 * (rmx.z - rmn.z)]
        hc = Vector((sum(p.x for p in hub_pts) / len(hub_pts), sum(p.y for p in hub_pts) / len(hub_pts), rmn.z)) if hub_pts else (rmn + rmx) / 2
        set_origin(rotor, Vector((hc.x, hc.y, (rmn.z + rmx.z) / 2)))
        info['rotor'] = {'center': [round(v, 4) for v in rotor.location], 'radius': round(max(rmx.x - rmn.x, rmx.y - rmn.y) / 2, 3)}
    bmn, bmx = bounds([body])
    pilot = Vector((0.22 * (bmx.x - bmn.x) / 2, bmn.y + 0.26 * (bmx.y - bmn.y), bmn.z + 0.30 * (bmx.z - bmn.z)))
    anchors = {'Seat_Driver': pilot, 'Seat_Passenger': Vector((-pilot.x, pilot.y, pilot.z)),
               'Entry_Driver': Vector(((bmx.x - bmn.x) / 2 + 0.9, pilot.y, 0)), 'TailRotor': Vector((0, bmx.y - 0.25, bmn.z + 0.55 * (bmx.z - bmn.z)))}
    for name, p in anchors.items():
        empty(name, p)
    rec.update({'size': [round(bmx.x - bmn.x, 3), round(bmx.y - bmn.y, 3), round(bmx.z, 3)], 'rotor': info.get('rotor'),
                'anchors': {k2: [round(v, 4) for v in p] for k2, p in anchors.items()}})


def build_drone(vid, spec, rec):
    objs = meshes()
    rotors = [o for o in objs if o.name.lower().startswith('rotor')]
    body = join([o for o in objs if o not in rotors], 'Body')
    mn, mx = bounds([body] + rotors)
    k = spec['length'] / (mx.y - mn.y)
    transform_all(Matrix.Scale(k, 4))
    allmn, _ = bounds(meshes())
    bmn, bmx = bounds([body])
    transform_all(Matrix.Translation(Vector((-(bmn.x + bmx.x) / 2, -(bmn.y + bmx.y) / 2, -allmn.z))))
    info = {}
    for r in rotors:
        center_origin(r)
        rmn, rmx = bounds([r]); c = r.location
        key = ('F' if c.y < 0 else 'B') + ('L' if c.x > 0 else 'R')
        r.name = 'Rotor_' + key; r.data.name = r.name
        info[key] = {'center': [round(v, 4) for v in c], 'radius': round(max(rmx.x - rmn.x, rmx.y - rmn.y) / 2, 4)}
    bmn, bmx = bounds([body])
    empty('Camera', Vector((0, bmn.y + 0.05, bmn.z + 0.2 * (bmx.z - bmn.z))))
    rec.update({'size': [round(bmx.x - bmn.x, 3), round(bmx.y - bmn.y, 3), round(bmx.z, 3)], 'rotors': info})


def save_textures(vid):
    """Every image the materials use, written as textures/<vid>_<name>.png beside the FBX. The FBX references them by
    relative path (Unity resolves them from Assets/Art/vehicles/textures/); embedded textures did not reach URP."""
    tdir = os.path.join(OUT, 'textures')
    os.makedirs(tdir, exist_ok=True)
    used = []
    for m in bpy.data.materials:
        if m.use_nodes and m.node_tree:
            for n in m.node_tree.nodes:
                # only maps wired straight into the shader (base colour, emission); the FBX exporter drops the rest
                direct = any(l.to_node.type == 'BSDF_PRINCIPLED' for o in n.outputs for l in o.links) if n.type == 'TEX_IMAGE' else False
                if direct and n.image is not None and n.image not in used:
                    used.append(n.image)
    out = []
    for img in used:
        base = os.path.splitext(bpy.path.basename(img.filepath) or img.name)[0] or img.name
        safe = ''.join(c if c.isalnum() or c in '-_' else '_' for c in base)
        path = os.path.join(tdir, f'{vid}_{safe}.png')
        img.file_format = 'PNG'
        try:
            img.save(filepath=path)
        except TypeError:
            img.filepath_raw = path
            img.save()
        img.filepath = path
        out.append({'file': os.path.relpath(path, REPO), 'size': list(img.size)})
    return out


def export(vid):
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.object.select_all(action='SELECT')
    path = os.path.join(OUT, vid + '.fbx')
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'MESH', 'EMPTY'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        bake_space_transform=True, use_mesh_modifiers=True, mesh_smooth_type='FACE', use_triangles=True,
        path_mode='RELATIVE', embed_textures=False, bake_anim=False, add_leaf_bones=False)
    return path


def main():
    index_path = os.path.join(OUT, 'vehicles.json')
    index = json.load(open(index_path)) if os.path.exists(index_path) else {}
    for vid, spec in SPECS.items():
        if ONLY and vid not in ONLY:
            continue
        reset()
        bpy.ops.import_scene.gltf(filepath=os.path.join(SRC, spec['src']))
        bake_and_merge()
        rec = {'id': vid, 'kind': spec['kind'], 'source': manifest_entry(spec['src']), 'source_file': 'art/raw/polypizza/' + spec['src']}
        {'car': build_car, 'scooter': build_scooter, 'heli': build_heli, 'drone': build_drone}[spec['kind']](vid, spec, rec)
        rec['objects'] = sorted(o.name for o in bpy.context.scene.objects)
        rec['textures'] = save_textures(vid)
        rec['fbx'] = os.path.relpath(export(vid), REPO)
        index[vid] = rec
        print('[veh_build]', vid, json.dumps({k: rec.get(k) for k in ('size', 'wheelbase', 'doors', 'objects')}), flush=True)
    os.makedirs(OUT, exist_ok=True)
    json.dump(index, open(index_path, 'w'), indent=1)


main()
