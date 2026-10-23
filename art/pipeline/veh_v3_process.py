"""CT Life vehicles v3 (2026-10-21): Sketchfab models -> Seeker-ready FBX + atlas + LODs + review render.

Usage: Blender -b --factory-startup -P art/pipeline/veh_v3_process.py -- <name>[,<name>...] [--no-render]
Reads  /Users/mac/Documents/codes/projects/ct-life/art/raw/sketchfab/<uid>/model.glb (+ meta.json)
Writes art/processed/vehicles_v3/<name>/<name>.fbx, textures/<name>_{BaseColor,Normal,MaskMap}.png, manifest.json
       and the lookdev render ct-life-evidence/vehicles-v3-20261021/<name>.png

Output conventions match art/pipeline/veh_build.py (what client/Assets/Scripts/Editor/VehicleImport.cs and Vehicle.cs expect):
  metres, wheels on the ground (z = 0), body centred in X/Y, front = -Y, left = +X in Blender; the FBX export
  (-Z forward, Y up, bake_space_transform) turns that into Unity +Z forward.
  Parts: Body, Door_FL, Door_FR, Wheel_FL/FR/RL/RR (cars) or Wheel_F/Wheel_R (bikes), Steering, Rotor_Main + TailRotor
  (heli), anchors as empties (Seat_Driver, Seat_Passenger, Entry_Driver, Entry_Passenger, SteeringCenter, Exhaust,
  Handlebar, Light_Head_L/R, Light_Tail_L/R). Every movable part's origin is its pivot.
  LODs: LOD1_<part> / LOD2_<part> meshes parented under the matching LOD0 part at identity (prefix, not suffix, so
  Vehicle.cs's StartsWith("Rotor_") and exact-name lookups never pick them up; Unity's _LODn auto-grouping is not used).
  Materials: Body (atlas), Paint (same atlas, neutral grey paint, tint via _BaseColor), Windows (glass; VehicleImport
  swaps any material named Windows* for VehicleGlass).
  MaskMap: R metallic, G ambient occlusion, B paint mask, A smoothness (URP Lit: use as _MetallicGlossMap with
  smoothness from albedo alpha OFF / metallic alpha ON, and as _OcclusionMap (G)).
"""
import bpy, bmesh, sys, os, json, math, shutil, datetime
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import veh_v3_bake as VB

REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
RAW = '/Users/mac/Documents/codes/projects/ct-life/art/raw/sketchfab'
OUT = os.path.join(REPO, 'art', 'processed', 'vehicles_v3')
EVID = '/Users/mac/Documents/codes/projects/ct-life-evidence/vehicles-v3-20261021'
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
ONLY = argv[0].split(',') if argv and not argv[0].startswith('--') else None
RENDER = '--no-render' not in argv

DZ_DROP = ('Badges', 'Headlights_Glass', 'Glass_Lights', 'Lights_Glass')
# kind: car | heli | bike | hover. length: target body length (None = keep the author's metres).
SPECS = {
    'hovercar': dict(uid='3205b1075bb44ffc826bce0c2a04d74c', cls='hovercar (player flying car)', kind='hover', length=4.6,
                     budget=45000, paint_mats=['cdp_body'], paint_default=(0.56, 0.59, 0.64)),
    'compact': dict(uid='055ff8a21b8d4d279debca089e2fafcd', cls='compact hatch', kind='car', src='dz', length=4.0),
    'sedan': dict(uid='7fd6e15785fa4aa9bfd6e31eb7c97ba6', cls='sedan', kind='car', src='dz', length=4.55),
    'suv': dict(uid='2866efdfa943484391ef8313768e074d', cls='SUV / crossover', kind='car', src='dz', length=4.35),
    'sports': dict(uid='3ab977050c334c13b6abb5de078b4957', cls='sports coupe', kind='car', src='dz', length=4.45),
    'pickup': dict(uid='3b3ac59245c44872b06ad85673be9643', cls='pickup', kind='car', src='dz', length=5.0),
    'taxi': dict(uid='72d91f4bce814818b3cd1f2de60f8ef2', cls='taxi', kind='car', src='dz', length=5.05, livery=True),
    'van': dict(uid='09d718c9cf72401b8534d265a06a803f', cls='delivery van', kind='car', src='dz', length=5.0),
    'police': dict(uid='6a5c6c8a75764eea905541fd5b05e9bf', cls='police cruiser', kind='car', src='dz', length=4.9, livery=True),
    'heli': dict(uid='f25c041a8d004eb092e4aa41d1dc33f4', cls='civilian helicopter', kind='heli', length=9.6),
    'bike': dict(uid='c02dec85ac0541b8ab6dd56098706b50', cls='city motorbike', kind='bike', length=2.1, wheel_r=0.31),
    'scooter': dict(uid='2205f4e69bc64df3aab03c71ab9d7492', cls='scooter', kind='bike', length=1.8),
}
LOD_BUDGET = {0: 35000, 1: 10000, 2: 5000, 3: 1200}   # LOD2 5k (was 2.5k: the harder weld and collapse melted the silhouette and smeared the atlas, 2026-10-22)
# LOD3 ~1.2k: parked decor beyond ~70 m only (a car is a few dozen pixels there); same 2 mm weld, so the atlas holds
SF = 'https://sketchfab.com/3d-models/'
BACKUP = {   # second choice per slot (not downloaded unless noted)
    'hovercar': 'Cyberpunk car by 3d_Bob, CC-BY, 11k faces ' + SF + 'b4301ff99d214d16a7a43708a5866bf0 [Unverified quality]; '
                'lionsharp (FREE) Merc Hovercar rejected: Mercedes star + SLS body (tagged MERCEDES)',
    'compact': "Sigil '07 by DanielZhabotinsky, CC-BY " + SF + '22abe5284d4c4b55920b8462eb24a8c1',
    'sedan': "Trinity '03 by DanielZhabotinsky, CC-BY " + SF + '32ac065960334899afc8ff6fbb2d79ee',
    'suv': "Rancher '80 Lifted by DanielZhabotinsky, CC-BY " + SF + 'fbf1767c4c3a450caa59c6431af79263',
    'sports': "Roadster '00 by DanielZhabotinsky, CC-BY " + SF + '82fd2008ce304df688ec4dfcd42d41ce',
    'pickup': "Conquer '89 Pickup by DanielZhabotinsky, CC-BY " + SF + '6c48c94be8974dc7a5932f697c0ba68f',
    'taxi': "Fairheaven LT '80 Taxi by DanielZhabotinsky, CC-BY " + SF + '7da68ec2677f4abd8e31e9998b497a67',
    'van': "LCT 3000 '07 box truck by DanielZhabotinsky, CC-BY " + SF + '3be03b6a43aa41898c9ca806b8787052',
    'police': "Fairheaven LT '80 Cop Cruiser by DanielZhabotinsky, CC-BY " + SF + '61766c1e6e1e4b2b803c174f33433a74',
    'heli': 'EC 135 by GRIP420, CC-BY ' + SF + 'ec30f802c6ac43a8843948fbadb674d2 (real Airbus type + ADAC livery: would need a repaint)',
    'bike': 'Low-Poly Motorcycle #2 by Sidramax, CC-BY ' + SF + '9e79295e99654e2a9fa930b5139a7d84 (128-256 px textures: weak)',
    'scooter': 'Yellow scooter by Pavlo_Holubov, CC-BY ' + SF + '77a6c9b31b644a089decc7c7cbe61eb8 (Vespa-like silhouette)',
}


# ---------------------------------------------------------------- scene helpers
def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def meshes():
    return [o for o in bpy.context.scene.objects if o.type == 'MESH']


def tris(objs):
    return sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs if o.type == 'MESH')


def bounds(objs):
    pts = [o.matrix_world @ v.co for o in objs for v in o.data.vertices]
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return mn, mx


def center(objs):
    mn, mx = bounds(objs)
    return (mn + mx) / 2


def bake_transforms():
    """Every mesh gets its world transform applied (instanced mesh data is made single-user first); empties removed."""
    for o in list(bpy.context.scene.objects):
        if o.type == 'MESH':
            if o.data.users > 1:
                o.data = o.data.copy()
            mw = o.matrix_world.copy()
            o.parent = None
            o.data.transform(mw)
            o.matrix_world = Matrix.Identity(4)
            if mw.determinant() < 0:
                o.data.flip_normals()
    for o in list(bpy.context.scene.objects):
        if o.type != 'MESH':
            bpy.data.objects.remove(o, do_unlink=True)
    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)


def drop_degenerate():
    """Collapsed meshes (every vertex within 1 mm: the hovercar's 'thruster' emitter quads, some 6 m ahead of the car) and
    vertices no face uses are invisible, but bounds() would count them: the length scale, the centring and the anchors
    placed from the bounds came out wrong (the hovercar's visible hull was 3 m long, its seat in the bonnet)."""
    for o in list(meshes()):
        vs = [v.co for v in o.data.vertices]
        if not vs or max(max(v[k] for v in vs) - min(v[k] for v in vs) for k in range(3)) < 0.001:
            bpy.data.objects.remove(o, do_unlink=True)
            continue
        bm = bmesh.new(); bm.from_mesh(o.data)
        loose = [v for v in bm.verts if not v.link_faces]
        if loose:
            bmesh.ops.delete(bm, geom=loose, context='VERTS')
            bm.to_mesh(o.data); o.data.update()
        bm.free()


def transform_all(m):
    for o in meshes():
        o.data.transform(m)
    for o in bpy.context.scene.objects:
        if o.type == 'EMPTY':
            o.location = m @ o.location


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
    o.data.transform(Matrix.Translation(-(point - o.location)))
    o.location = point


def empty(name, loc, parent=None):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = 'PLAIN_AXES'; e.empty_display_size = 0.15
    e.location = loc
    bpy.context.scene.collection.objects.link(e)
    return e


def tag_faces(o, value):
    """Integer face attribute ctl_part: 0 shell, 1 interior, 2 underbody (used to strip LOD2)."""
    a = o.data.attributes.get('ctl_part') or o.data.attributes.new('ctl_part', 'INT', 'FACE')
    for d in a.data:
        d.value = value


def separate_by(o, pred, name):
    """Split off the faces whose (material name, face) satisfy pred into a new object."""
    bm = bmesh.new(); bm.from_mesh(o.data)
    sel = 0
    for f in bm.faces:
        m = o.material_slots[f.material_index].material if f.material_index < len(o.material_slots) else None
        s = pred(m, f)
        f.select_set(s); sel += s
    bm.to_mesh(o.data); bm.free()
    if not sel:
        return None
    if sel == len(o.data.polygons):
        o.name = name; o.data.name = name
        return o
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_mode(type='FACE')
    bpy.ops.mesh.separate(type='SELECTED'); bpy.ops.object.mode_set(mode='OBJECT')
    new = [x for x in bpy.context.selected_objects if x != o][0]
    new.name = name; new.data.name = name
    return new


def is_glass(m):
    if m is None:
        return False
    n = m.name.lower()
    if 'transp' in n or 'glass' in n and 'light' not in n or 'window' in n or 'windshield' in n:
        return True
    return False


# ---------------------------------------------------------------- doors (after veh_build.cut_door)
def stash_normals(o):
    """Corner normals -> plain float corner attribute (bmesh interpolates it linearly when bisecting)."""
    me = o.data
    a = me.attributes.get('ctl_n') or me.attributes.new('ctl_n', 'FLOAT_VECTOR', 'CORNER')
    vals = [0.0] * (len(me.loops) * 3)
    me.corner_normals.foreach_get('vector', vals) if hasattr(me, 'corner_normals') else me.loops.foreach_get('normal', vals)
    a.data.foreach_set('vector', vals)


def apply_stashed_normals(o):
    me = o.data
    a = me.attributes.get('ctl_n')
    if a is None:
        return
    vals = [0.0] * (len(me.loops) * 3)
    a.data.foreach_get('vector', vals)
    ns = [Vector(vals[i:i + 3]).normalized() for i in range(0, len(vals), 3)]
    me.normals_split_custom_set([tuple(n) for n in ns])
    me.attributes.remove(me.attributes['ctl_n'])


def cut_door(body, side, y0, y1, z0, z1, xmin, name):
    """Bisect the body at y0/y1 and split off the door skin between them: outward-facing shell faces (|x| > xmin) plus the
    inward-facing interior door card, so an open door is not back-face culled. Normals ride along in ctl_n."""
    bm = bmesh.new(); bm.from_mesh(body.data)
    for y in (y0, y1):
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, y, 0), plane_no=(0, 1, 0))
    bm.faces.ensure_lookup_table(); bm.normal_update()
    part = bm.faces.layers.int.get('ctl_part')
    sel = []
    for f in bm.faces:
        c = f.calc_center_median(); n = f.normal
        if not (y0 < c.y < y1 and z0 < c.z < z1):
            continue
        tag = f[part] if part is not None else 0
        if tag == 0 and side * c.x > xmin and side * n.x > 0.25:
            sel.append(f)
        elif tag == 1 and side * c.x > xmin * 0.72 and side * n.x < -0.35:
            sel.append(f)
    for f in bm.faces:
        f.select_set(False)
    for f in sel:
        f.select_set(True)
    bm.to_mesh(body.data); bm.free()
    if not sel:
        return None
    bpy.ops.object.select_all(action='DESELECT'); body.select_set(True); bpy.context.view_layer.objects.active = body
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_mode(type='FACE')
    bpy.ops.mesh.separate(type='SELECTED'); bpy.ops.object.mode_set(mode='OBJECT')
    door = [o for o in bpy.context.selected_objects if o != body][0]
    door.name = name; door.data.name = name
    door.rotation_mode = 'XYZ'
    mn, mx = bounds([door])
    set_origin(door, Vector(((mx.x if side > 0 else mn.x), y0, (mn.z + mx.z) / 2)))   # hinge = front cut line (-Y forward)
    return door


# ---------------------------------------------------------------- DZ car build
def node_name(o):
    """glTF mesh objects are named <node>_<material>_<n>; drop the material part so roles come from the node name."""
    n = o.name
    for sl in o.material_slots:
        if sl.material:
            n = n.replace('_' + sl.material.name, '')
    return n


def dz_role(name):
    n = name.lower()
    if any(k.lower() in n for k in DZ_DROP) or ('glass' in n and 'light' in n):
        return 'drop'          # brand badges; transparent lamp covers (the opaque lamp meshes below stay)
    if 'wheel' in n and 'steering' not in n:
        return 'wheel'
    if 'steering' in n:
        return 'steering'
    if 'interior' in n:
        return 'interior'
    if 'suspension' in n or 'bottom' in n or 'chassis' in n or 'exhaust' in n:
        return 'under'
    return 'body'


def orient_by_lights(objs, front_kw=('headlight',), rear_kw=('brakelight', 'taillight', 'rearlight', 'reverse')):
    """Rotate so the car faces -Y with its length along Y. Returns the rotation applied (degrees)."""
    hl = [o for o in objs if any(k in node_name(o).lower() for k in front_kw)]
    rl = [o for o in objs if any(k in node_name(o).lower() for k in rear_kw)]
    if not hl or not rl:
        return None
    d = center(hl) - center(rl)
    d.z = 0
    ang = math.atan2(d.y, d.x)                      # direction of the nose
    rot = -math.pi / 2 - ang                        # turn the nose to -Y
    transform_all(Matrix.Rotation(rot, 4, 'Z'))
    return round(math.degrees(rot), 1)


def build_dz_car(name, spec, rec):
    objs = meshes()
    roles = {o: dz_role(node_name(o)) for o in objs}
    for o, r in list(roles.items()):
        if r == 'drop':
            rec.setdefault('removed', []).append(o.name)
            roles.pop(o)
            bpy.data.objects.remove(o, do_unlink=True)
    objs = list(roles)
    rec['orient_rot_deg'] = orient_by_lights(objs)
    wheels = [o for o in objs if roles[o] == 'wheel']
    # scale
    mn, mx = bounds(objs)
    natural = mx.y - mn.y
    k = spec['length'] / natural if spec.get('length') else 1.0
    rec['author_length_m'] = round(natural, 3); rec['scale_applied'] = round(k, 4)
    transform_all(Matrix.Scale(k, 4))
    wmn, _ = bounds(wheels)
    bmn, bmx = bounds([o for o in objs if roles[o] in ('body',)])
    transform_all(Matrix.Translation(Vector((-(bmn.x + bmx.x) / 2, -(bmn.y + bmx.y) / 2, -wmn.z))))
    for o in objs:
        tag_faces(o, {'interior': 1, 'under': 2}.get(roles[o], 0))
    # wheels named by where they are
    wheel_info = {}
    for w in wheels:
        c = center([w])
        key = ('F' if c.y < 0 else 'R') + ('L' if c.x > 0 else 'R')
        w.name = 'Wheel_' + key; w.data.name = w.name
        set_origin(w, c)
        a, b = bounds([w])
        wheel_info[key] = {'center': [round(v, 4) for v in c], 'radius': round((b.z - a.z) / 2, 4), 'width': round(b.x - a.x, 4)}
    steer = next((o for o in objs if roles[o] == 'steering'), None)
    # glass: own objects (excluded from the atlas bake)
    body_parts = [o for o in objs if roles[o] in ('body', 'interior', 'under')]
    body = join(body_parts, 'Body')
    glass = separate_by(body, lambda m, f: is_glass(m), 'Glass')
    # doors
    bmn, bmx = bounds([body])
    W = (bmx.x - bmn.x) / 2
    yf = (wheel_info['FL']['center'][1] + wheel_info['FR']['center'][1]) / 2
    yr = (wheel_info['RL']['center'][1] + wheel_info['RR']['center'][1]) / 2
    R = (wheel_info['FL']['radius'] + wheel_info['RL']['radius']) / 2
    wb = yr - yf
    df = spec.get('door', (0.10, 0.44))
    y0 = yf + R + df[0] * wb; y1 = y0 + df[1] * wb
    z0 = R * 0.55; z1 = bmx.z - 0.06 * (bmx.z - bmn.z)
    doors = {}
    stash_normals(body)
    for side, key in ((1, 'L'), (-1, 'R')):
        d = cut_door(body, side, y0, y1, z0, z1, W * 0.55, 'Door_F' + key)
        if d:
            doors[key] = d
    for o in [body] + list(doors.values()):
        apply_stashed_normals(o)
    # door glass follows the door
    if glass:
        for key, d in doors.items():
            side = 1 if key == 'L' else -1
            dg = separate_by(glass, lambda m, f, s=side: s * f.calc_center_median().x > W * 0.45 and y0 - 0.05 < f.calc_center_median().y < y1 + 0.02,
                             'DoorGlass_' + key)
            if dg is not None and dg is not glass:
                dg['ctl_join'] = d.name
            elif dg is glass:
                glass = None
    if steer:
        st_c = center([steer])
        steer.name = 'Steering'; steer.data.name = 'Steering'
        set_origin(steer, st_c)
    else:
        st_c = Vector((W * 0.4, yf + 0.55 * wb * 0.6, R + 0.75))
    seat_z = max(st_c.z - 0.42, R * 0.9)
    driver = Vector((st_c.x, st_c.y + 0.48, seat_z))
    passenger = Vector((-st_c.x, driver.y, seat_z))
    anchors = {
        'Seat_Driver': driver, 'Seat_Passenger': passenger,
        'Entry_Driver': Vector((math.copysign(W + 0.55, st_c.x), driver.y + 0.1, 0.0)),
        'Entry_Passenger': Vector((-math.copysign(W + 0.55, st_c.x), driver.y + 0.1, 0.0)),
        'SteeringCenter': st_c, 'Exhaust': Vector((-W * 0.5, bmx.y - 0.05, R * 0.75)),
        'Light_Head_L': Vector((W * 0.7, bmn.y + 0.05, R * 1.5)), 'Light_Head_R': Vector((-W * 0.7, bmn.y + 0.05, R * 1.5)),
        'Light_Tail_L': Vector((W * 0.75, bmx.y - 0.05, R * 1.7)), 'Light_Tail_R': Vector((-W * 0.75, bmx.y - 0.05, R * 1.7)),
    }
    rec.update({'size': [round(2 * W, 3), round(bmx.y - bmn.y, 3), round(bmx.z, 3)], 'wheelbase': round(wb, 3),
                'track': round(wheel_info['FL']['center'][0] - wheel_info['FR']['center'][0], 3), 'wheels': wheel_info,
                'doors': {k: [round(v, 4) for v in d.location] for k, d in doors.items()},
                'door_cut_y': [round(y0, 3), round(y1, 3)]})
    return anchors



# ---------------------------------------------------------------- shared helpers for the non-DZ builders
def prep_skinned():
    """Freeze skinned meshes in their frame-0 pose (the heli's rotors are bone-driven); remember each mesh's dominant bone."""
    # the animation's first frame is the authored "parked" pose (the heli's rest pose leaves the blades tilted)
    bpy.context.scene.frame_set(0)
    bpy.context.view_layer.update()
    for a in [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']:
        for pb in a.pose.bones:     # 2-vertex marker (head, tail) per bone: rides through every later transform
            me = bpy.data.meshes.new('PIVOT_' + pb.name)
            me.from_pydata([tuple(a.matrix_world @ pb.head), tuple(a.matrix_world @ pb.tail)], [(0, 1)], [])
            ob = bpy.data.objects.new('PIVOT_' + pb.name, me); bpy.context.scene.collection.objects.link(ob)
    for o in [o for o in bpy.context.scene.objects if o.type == 'MESH']:
        if o.vertex_groups:
            cnt = {}
            for v in o.data.vertices:
                for g in v.groups:
                    if g.weight > 0.5:
                        cnt[o.vertex_groups[g.group].name] = cnt.get(o.vertex_groups[g.group].name, 0) + 1
            if cnt:
                o['ctl_bone'] = max(cnt, key=cnt.get)
        for m in list(o.modifiers):
            if m.type == 'ARMATURE':
                bpy.context.view_layer.objects.active = o
                bpy.ops.object.select_all(action='DESELECT'); o.select_set(True)
                bpy.ops.object.modifier_apply(modifier=m.name)
        o.vertex_groups.clear()
        if o.data.shape_keys:
            o.shape_key_clear()


def face_nose(v):
    """Rotate everything about Z so the horizontal direction v points to -Y."""
    v = Vector((v.x, v.y, 0))
    rot = -math.pi / 2 - math.atan2(v.y, v.x)
    transform_all(Matrix.Rotation(rot, 4, 'Z'))
    return round(math.degrees(rot), 1)


def long_axis_to_y(objs):
    mn, mx = bounds(objs)
    if (mx.x - mn.x) > (mx.y - mn.y):
        transform_all(Matrix.Rotation(math.pi / 2, 4, 'Z'))
        return 90.0
    return 0.0


def scale_center_ground(objs_len, length, ground_objs=None):
    mn, mx = bounds(objs_len)
    k = length / (mx.y - mn.y) if length else 1.0
    transform_all(Matrix.Scale(k, 4))
    gmn, _ = bounds(ground_objs or meshes())
    bmn, bmx = bounds(objs_len)
    transform_all(Matrix.Translation(Vector((-(bmn.x + bmx.x) / 2, -(bmn.y + bmx.y) / 2, -gmn.z))))
    return round(mx.y - mn.y, 3), round(k, 4)


def drop(objs_or_pred):
    for o in list(meshes()):
        if objs_or_pred(o):
            bpy.data.objects.remove(o, do_unlink=True)


def mats_of(o):
    return [s.material.name.lower() for s in o.material_slots if s.material]


# ---------------------------------------------------------------- helicopter
def build_heli(name, spec, rec):
    drop(lambda o: not o.name.startswith('PIVOT_') and (not o.material_slots or all(s.material is None for s in o.material_slots)))
    pivots = [o for o in meshes() if o.name.startswith('PIVOT_')]
    objs = [o for o in meshes() if o not in pivots]
    rotor = [o for o in objs if o.get('ctl_bone', '').lower().startswith('blade')]
    tail = [o for o in objs if o.get('ctl_bone', '').lower().startswith('screw')]
    body = [o for o in objs if o not in rotor and o not in tail]
    long_axis_to_y(body)
    rec['orient_rot_deg'] = face_nose(center(body) - center(tail))     # tail rotor is at the back
    rec['author_length_m'], rec['scale_applied'] = scale_center_ground(body, spec['length'], body)
    for o in objs:
        tag_faces(o, 0)
    body = join(body, 'Body')
    rotor = join(rotor, 'Rotor_Main'); tail = join(tail, 'TailRotor')
    rmn, rmx = bounds([rotor])
    # the hub is the rotor bone's head (3 blades make the bounding-box centre useless)
    pv = {o.name[6:].lower(): (o.data.vertices[0].co.copy(), o.data.vertices[1].co.copy()) for o in pivots}
    bh = next((v for k, v in pv.items() if k.startswith('blade') and not k.startswith('blades_end')), None)
    hub = Vector((bh[0].x, bh[0].y, (rmn.z + rmx.z) / 2)) if bh else (rmn + rmx) / 2
    rec['rotor_bone_axis'] = [round(c, 3) for c in (bh[1] - bh[0]).normalized()] if bh else None
    for o in pivots:
        bpy.data.objects.remove(o, do_unlink=True)
    set_origin(rotor, hub)
    tmn, tmx = bounds([tail])
    set_origin(tail, (tmn + tmx) / 2)
    for o in (rotor, tail):
        o.rotation_mode = 'XYZ'
    glass = separate_by(body, lambda m, f: is_glass(m), 'Glass')
    bmn, bmx = bounds([body])
    W = (bmx.x - bmn.x) / 2
    pilot = Vector((0.3, bmn.y + 0.24 * (bmx.y - bmn.y), bmn.z + 0.42 * (bmx.z - bmn.z)))
    rec.update({'size': [round(bmx.x - bmn.x, 3), round(bmx.y - bmn.y, 3), round(bmx.z, 3)],
                'rotor': {'center': [round(v, 4) for v in hub], 'radius': round(max(rmx.x - rmn.x, rmx.y - rmn.y) / 2, 3),
                          'axis': 'local +Z (vehicle up)'},
                'tail_rotor': {'center': [round(v, 4) for v in tail.location],
                               'radius': round(max(tmx.y - tmn.y, tmx.z - tmn.z) / 2, 3), 'axis': 'local X (vehicle side)'}})
    return {'Seat_Driver': pilot, 'Seat_Passenger': Vector((-pilot.x, pilot.y, pilot.z)),
            'Entry_Driver': Vector((W + 0.9, pilot.y, 0)), 'Entry_Passenger': Vector((-W - 0.9, pilot.y, 0))}


# ---------------------------------------------------------------- motorbike / scooter
def island_faces(bm):
    seen = set(); out = []
    for f in bm.faces:
        if f.index in seen:
            continue
        stack = [f]; isl = []
        seen.add(f.index)
        while stack:
            g = stack.pop(); isl.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen:
                        seen.add(h.index); stack.append(h)
        out.append(isl)
    return out


def pull_wheel_parts(objs, c, r, half_w):
    """Move every loose part that sits inside the wheel cylinder (axis = X) into a new object; returns them."""
    got = []
    for o in objs:
        bm = bmesh.new(); bm.from_mesh(o.data)
        bm.faces.ensure_lookup_table()
        sel = set()
        for isl in island_faces(bm):
            vs = {v for f in isl for v in f.verts}
            if all(((v.co.y - c.y) ** 2 + (v.co.z - c.z) ** 2) ** 0.5 <= r * 1.02 and abs(v.co.x - c.x) <= half_w for v in vs):
                sel.update(f.index for f in isl)
        for f in bm.faces:
            f.select_set(f.index in sel)
        bm.to_mesh(o.data); bm.free()
        if sel:
            new = separate_by(o, lambda m, f: f.select, o.name + '_wheelpart')
            got.append(new)
    return got


def build_bike(name, spec, rec):
    drop(lambda o: any('sticker' in m or 'badge' in m or 'logo' in m for m in mats_of(o)))
    objs = meshes()
    tires = [o for o in objs if any('tire' in m or 'tyre' in m for m in mats_of(o)) or 'wheel' in o.name.lower()]
    long_axis_to_y(objs)
    # the handlebar/mirrors are the highest points and sit over the front wheel
    pts = sorted((v.co for o in objs for v in o.data.vertices), key=lambda p: -p.z)[:200]
    top = sum(pts, Vector()) / len(pts)
    mn, mx = bounds(objs)
    front_sign = spec.get('front', -1 if top.y < (mn.y + mx.y) / 2 else 1)
    rec['orient_rot_deg'] = face_nose(Vector((0, front_sign, 0)))
    rec['author_length_m'], rec['scale_applied'] = scale_center_ground(objs, spec['length'], tires)
    for o in objs:
        tag_faces(o, 0)
    # tires: join per end, then pull rims/discs/hubs inside each tire's cylinder
    ends = {}
    for t in tires:
        ends.setdefault('F' if center([t]).y < 0 else 'R', []).append(t)
    if not tires:
        # One merged mesh with no tire material (e.g. the Cafe Racer): find each wheel by shape instead,
        # the loose parts inside a ground-touching cylinder of spec['wheel_r'] at each end.
        r0 = spec.get('wheel_r', 0.31)
        mn, mx = bounds(meshes())
        for key, y in (('F', mn.y + r0 + 0.02), ('R', mx.y - r0 - 0.02)):
            parts_ = pull_wheel_parts(meshes(), Vector((0, y, r0)), r0 * 1.08, 0.12)
            # keep only round parts (a wheel's y and z extents match); the rest goes back to the body
            ends[key] = [o for o in parts_ if abs(o.dimensions.y - o.dimensions.z) <= 0.12 * max(o.dimensions.y, o.dimensions.z, 1e-6)]
            # the whole wheel must be round too: drop the part that stretches it most until y and z extents agree
            while len(ends[key]) > 1:
                wmn, wmx = bounds(ends[key]); ext = wmx - wmn
                if abs(ext.y - ext.z) <= 0.04 * max(ext.y, ext.z):
                    break
                long_ax = 'y' if ext.y > ext.z else 'z'
                ends[key].remove(max(ends[key], key=lambda o: getattr(o.dimensions, long_ax)))
            if not ends[key]:
                raise RuntimeError(f'{name}: no wheel parts found at the {key} end; set spec wheel_r')
    others = [o for o in meshes() if not any(o in v for v in ends.values())]
    info = {}
    for key in ('F', 'R'):
        t = join(ends[key], 'Wheel_' + key)
        others = [o for o in meshes() if not o.name.startswith('Wheel_') and not any(o in v for v in ends.values())]
        tmn, tmx = bounds([t]); c = (tmn + tmx) / 2
        # hub from the round part only: a sprocket or disc modelled into the rim sticks out toward the bike's middle
        # and stretches the bounds, so the radius is the shorter extent and the hub sits that far from the outer edge
        # (a bounds-centre pivot would make the wheel wobble)
        r = min(tmx.y - tmn.y, tmx.z - tmn.z) / 2
        if (tmx.y - tmn.y) > (tmx.z - tmn.z) * 1.04:
            c.y = tmn.y + r if key == 'F' else tmx.y - r
        hw = max((tmx.x - tmn.x) / 2 + 0.03, 0.08)
        parts = pull_wheel_parts(others, c, r, hw)
        t = join([t] + parts, 'Wheel_' + key)
        set_origin(t, c); t.rotation_mode = 'XYZ'
        info[key] = {'center': [round(v, 4) for v in c], 'radius': round(r, 4)}
        others = [o for o in meshes() if not o.name.startswith('Wheel_')]
    body = join(others, 'Body')
    glass = separate_by(body, lambda m, f: is_glass(m), 'Glass')
    bmn, bmx = bounds([body])
    L_ = bmx.y - bmn.y
    seat = Vector((0, bmn.y + 0.58 * L_, spec.get('seat_z', 0.74)))
    rec.update({'size': [round(bmx.x - bmn.x, 3), round(L_, 3), round(bmx.z, 3)], 'wheels': info,
                'wheelbase': round(info['R']['center'][1] - info['F']['center'][1], 3)})
    return {'Seat_Driver': seat, 'Entry_Driver': Vector((0.75, seat.y, 0)),
            'Handlebar': Vector((0, bmn.y + 0.30 * L_, bmx.z * 0.85)), 'Exhaust': Vector((-0.15, bmx.y - 0.1, 0.3))}


# ---------------------------------------------------------------- hovercar
def build_hover(name, spec, rec):
    drop(lambda o: any(m in ('gloss',) or m.startswith('plane.') for m in mats_of(o)))   # clear-coat shells, shadow plane
    objs = meshes()
    long_axis_to_y(objs)
    if spec.get('nose_sign'):
        rec['orient_rot_deg'] = face_nose(Vector((0, spec['nose_sign'], 0)))
    rec['author_length_m'], rec['scale_applied'] = scale_center_ground(objs, spec['length'])
    hover = spec.get('hover_height', 0.45)
    transform_all(Matrix.Translation(Vector((0, 0, hover))))
    for o in objs:
        tag_faces(o, 0)
    thr = [o for o in objs if any('thruster' in m for m in mats_of(o))]
    thr_c = [center([o]) for o in thr]
    body = join(objs, 'Body')
    glass = separate_by(body, lambda m, f: is_glass(m), 'Glass')
    bmn, bmx = bounds([body])
    W = (bmx.x - bmn.x) / 2
    seat = Vector((0.36, bmn.y + 0.47 * (bmx.y - bmn.y), bmn.z + 0.30 * (bmx.z - bmn.z)))
    anchors = {'Seat_Driver': seat, 'Seat_Passenger': Vector((-seat.x, seat.y, seat.z)),
               'Entry_Driver': Vector((W + 0.6, seat.y, 0)), 'Entry_Passenger': Vector((-W - 0.6, seat.y, 0)),
               'SteeringCenter': Vector((seat.x, seat.y - 0.5, seat.z + 0.35))}
    # thruster nozzles (glowing discs), merged when close
    pts = []
    for c in thr_c:
        if all((c - p).length > 0.25 for p in pts):
            pts.append(c)
    for i, c in enumerate(sorted(pts, key=lambda p: (p.y, p.x))):
        anchors[f'Thruster_{i}'] = c
    rec.update({'size': [round(2 * W, 3), round(bmx.y - bmn.y, 3), round(bmx.z, 3)], 'hover_height': hover,
                'note': 'no wheels; body origin on the ground under the car, hull floats hover_height above it'})
    return anchors


# ---------------------------------------------------------------- material roles, bake, LODs, export
def paint_material_names(spec):
    if spec.get('livery'):
        return []
    if spec.get('paint_mats'):
        return spec['paint_mats']
    return [m.name for m in bpy.data.materials if m.name.lower().endswith('bodymat') or m.name.lower() in ('paint', 'carpaint', 'body_paint')]


def specgloss_names():
    out = []
    for m in bpy.data.materials:
        b = VB._principled(m)
        if b and b.inputs.get('Specular Tint') is not None and b.inputs['Specular Tint'].is_linked:
            out.append(m.name)
    return out


def smooth_by_angle(o, deg=40):
    """Drop custom normals (welding/decimation scrambles them) and shade smooth with sharp edges above deg."""
    me = o.data
    if me.has_custom_normals:
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.select_all(action='DESELECT'); o.select_set(True)
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    me.shade_smooth()
    if hasattr(me, 'set_sharp_from_angle'):
        me.set_sharp_from_angle(angle=math.radians(deg))


def lod_children(parts, level, ratio, drop_parts=(), drop_tags=()):
    made = []
    span = max((p.dimensions.length for p in parts), default=1.0)
    for p in parts:
        if p.name in drop_parts:
            continue
        c = p.copy(); c.data = p.data.copy()
        c.name = f'LOD{level}_{p.name}'; c.data.name = c.name
        bpy.context.scene.collection.objects.link(c)
        c.parent = p; c.matrix_parent_inverse = Matrix.Identity(4); c.matrix_basis = Matrix.Identity(4)
        bm = bmesh.new(); bm.from_mesh(c.data)
        if drop_tags:
            lay = bm.faces.layers.int.get('ctl_part')
            if lay is not None:
                bmesh.ops.delete(bm, geom=[f for f in bm.faces if f[lay] in drop_tags], context='FACES')
        # weld (split normals, panel pieces) so collapse can work across them
        # (LOD2 used to weld at 12 mm, which merged across UV seams: the far car showed a smeared atlas, read as a
        # different object, and morphed on the switch; every LOD now welds the same 2 mm so the atlas stays put)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.002)
        if level >= 2:
            # small loose bits (badges, bolts, mirrors' arms) only cost triangles at distance
            for isl in island_faces(bm):
                vs = {v for f in isl for v in f.verts}
                xs = [v.co.x for v in vs]; ys = [v.co.y for v in vs]; zs = [v.co.z for v in vs]
                diag = ((max(xs) - min(xs)) ** 2 + (max(ys) - min(ys)) ** 2 + (max(zs) - min(zs)) ** 2) ** 0.5
                if diag < 0.06 * span:
                    bmesh.ops.delete(bm, geom=isl, context='FACES')
        bm.to_mesh(c.data); bm.free()
        if len(c.data.polygons) == 0:
            bpy.data.objects.remove(c, do_unlink=True)
            continue
        if ratio < 0.999:
            mod = c.modifiers.new('dec', 'DECIMATE'); mod.decimate_type = 'COLLAPSE'; mod.ratio = ratio; mod.use_collapse_triangulate = True
            if p.name == 'Body':
                mod.use_symmetry = True; mod.symmetry_axis = 'X'
            bpy.context.view_layer.objects.active = c
            bpy.ops.object.select_all(action='DESELECT'); c.select_set(True)
            bpy.ops.object.modifier_apply(modifier='dec')
        smooth_by_angle(c)
        made.append(c)
    return made


def fit_lod(parts, level, budget, drop_parts=(), drop_tags=()):
    probe = lod_children(parts, level, 1.0, drop_parts, drop_tags)      # what is left after stripping, undecimated
    base = tris(probe)
    for c in probe:
        bpy.data.objects.remove(c, do_unlink=True)
    ratio = min(1.0, budget * 0.97 / max(base, 1))
    for it in range(5):
        made = lod_children(parts, level, ratio, drop_parts, drop_tags)
        t = tris(made)
        if t <= budget or it == 4:
            break
        for c in made:
            bpy.data.objects.remove(c, do_unlink=True)
        ratio *= budget * 0.97 / t
    if t <= budget:
        return made, t
    # still over: collapse cannot shrink lots of tiny loose parts, so drop the smallest islands until it fits
    isl_all = []
    for c in made:
        bm = bmesh.new(); bm.from_mesh(c.data)
        isls = sorted(island_faces(bm), key=len)
        for isl in isls[:-1]:              # every part keeps its largest island (tyre, hull, blade set)
            vs = {v.co.copy().freeze() for f in isl for v in f.verts}
            mn = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
            mx = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
            isl_all.append(((mx - mn).length, c.name, {f.index for f in isl}, sum(len(f.verts) - 2 for f in isl)))
        bm.free()
    isl_all.sort(key=lambda r: r[0])
    kill = {}
    for diag, cname, faces, n in isl_all:
        if t <= budget:
            break
        kill.setdefault(cname, set()).update(faces); t -= n
    for cname, faces in kill.items():
        c = bpy.data.objects[cname]
        bm = bmesh.new(); bm.from_mesh(c.data); bm.faces.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[bm.faces[i] for i in faces], context='FACES')
        bm.to_mesh(c.data); bm.free()
    return made, tris(made)


def decimate_inplace(objs, budget):
    t = tris(objs)
    if t <= budget:
        return t
    for o in objs:
        bm = bmesh.new(); bm.from_mesh(o.data)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0002)
        bm.to_mesh(o.data); bm.free()
    t = tris(objs)
    ratio = budget * 0.97 / t
    for o in objs:
        if ratio >= 1:
            break
        mod = o.modifiers.new('dec', 'DECIMATE'); mod.decimate_type = 'COLLAPSE'; mod.ratio = ratio; mod.use_collapse_triangulate = True
        bpy.context.view_layer.objects.active = o; bpy.ops.object.select_all(action='DESELECT'); o.select_set(True)
        bpy.ops.object.modifier_apply(modifier='dec')
        smooth_by_angle(o)
    return tris(objs)


def assign_atlas_materials(objs, body_mat, paint_mat, glass_mat, paint_names):
    for o in objs:
        old = [s.material for s in o.material_slots]
        mapping = []
        for m in old:
            if m is not None and m.name == 'Windows':
                mapping.append('Windows')
            elif m is not None and is_glass(m):
                mapping.append('Windows')
            elif paint_mat is not None and m is not None and m.name in paint_names:
                mapping.append('Paint')
            else:
                mapping.append('Body')
        order = [n for n in ('Body', 'Paint', 'Windows') if n in mapping]
        idx = [order.index(n) for n in mapping]
        new_idx = [idx[p.material_index] if p.material_index < len(idx) else 0 for p in o.data.polygons]
        o.data.materials.clear()          # (this also drops the material_index attribute, so reassign afterwards)
        for n in order:
            o.data.materials.append({'Body': body_mat, 'Paint': paint_mat, 'Windows': glass_mat}[n])
        for p, i in zip(o.data.polygons, new_idx):
            p.material_index = i


def export(name, out_dir):
    for o in bpy.context.scene.objects:
        o.select_set(o.type in ('MESH', 'EMPTY'))
    path = os.path.join(out_dir, name + '.fbx')
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'MESH', 'EMPTY'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        bake_space_transform=True, use_mesh_modifiers=True, mesh_smooth_type='FACE', use_triangles=True,
        path_mode='RELATIVE', embed_textures=False, bake_anim=False, add_leaf_bones=False)
    return path


def process(name, spec):
    reset()
    uid = spec['uid']
    src = os.path.join(RAW, uid, 'model.glb')
    meta = json.load(open(os.path.join(RAW, uid, 'meta.json')))
    bpy.ops.import_scene.gltf(filepath=src)
    prep_skinned()
    bake_transforms()
    drop_degenerate()
    rec = {'name': name, 'class': spec['cls'], 'uid': uid, 'url': meta.get('viewerUrl') or f'https://sketchfab.com/3d-models/{uid}',
           'title': meta.get('name'), 'creator': meta['user']['username'], 'creator_name': meta['user'].get('displayName'),
           'licence': meta['license']['label'], 'licence_slug': meta['license'].get('slug'),
           'licence_checked': 'Sketchfab API v3 /models/' + uid + ' on ' + datetime.date.today().isoformat(),
           'source_tris': tris(meshes()), 'backup': BACKUP.get(name)}
    builder = {'car': build_dz_car}.get(spec['kind'])
    if spec['kind'] in ('heli', 'bike', 'hover'):
        builder = globals()['build_' + spec['kind']]
    anchors = builder(name, spec, rec)
    for k, p in anchors.items():
        empty(k, p)
    out_dir = os.path.join(OUT, name); tex_dir = os.path.join(out_dir, 'textures')
    if os.path.isdir(out_dir):
        shutil.rmtree(out_dir)
    os.makedirs(tex_dir)
    # LOD0 budget
    lod0 = [o for o in meshes()]
    budget0 = spec.get('budget', LOD_BUDGET[0])
    rec['lod0_decimated'] = tris(lod0) > budget0
    decimate_inplace([o for o in lod0 if not o.name.startswith(('Glass', 'DoorGlass'))], budget0 - tris([o for o in lod0 if o.name.startswith(('Glass', 'DoorGlass'))]))
    # bake
    paint_names = paint_material_names(spec)
    sg = specgloss_names()
    bake_objs = [o for o in meshes() if not o.name.startswith(('Glass', 'DoorGlass'))]
    tex, paint_rgb = VB.bake_atlas(bake_objs, tex_dir, name, size=spec.get('atlas', 2048), paint_mats=paint_names, specgloss_mats=sg)
    if paint_rgb is not None and spec.get('paint_default'):
        paint_rgb = list(spec['paint_default'])
    body_mat, paint_mat = VB.atlas_materials(tex, paint_rgb)
    glass_mat = VB.glass_material()
    assign_atlas_materials(meshes(), body_mat, paint_mat, glass_mat, paint_names)
    # glass back into its owners
    for o in list(meshes()):
        if o.name.startswith('DoorGlass') and o.get('ctl_join'):
            door = bpy.data.objects[o['ctl_join']]
            loc = door.location.copy()
            join([door, o], door.name)
    gl = bpy.data.objects.get('Glass')
    if gl is not None:
        join([bpy.data.objects['Body'], gl], 'Body')
    # LODs
    parts = [o for o in meshes() if not o.name.startswith('LOD')]
    small = [p.name for p in parts if p.name in ('Steering',) or p.name.startswith('Rotor_Tail_detail')]
    lod1, t1 = fit_lod(parts, 1, LOD_BUDGET[1])
    lod2, t2 = fit_lod(parts, 2, LOD_BUDGET[2], drop_parts=small, drop_tags=(1, 2))
    lod3, t3 = fit_lod(parts, 3, LOD_BUDGET[3], drop_parts=small, drop_tags=(1, 2))
    rec['tris'] = {'LOD0': tris(parts), 'LOD1': t1, 'LOD2': t2, 'LOD3': t3}
    rec['materials'] = sorted({s.material.name for o in meshes() for s in o.material_slots if s.material})
    rec['textures'] = {k: {'file': os.path.relpath(v, out_dir), 'size': 2048} for k, v in tex.items()}
    rec['paint'] = {'tintable': paint_mat is not None, 'default_srgb': [round(x, 3) for x in paint_rgb] if paint_rgb else None,
                    'source_materials': paint_names}
    rec['parts'] = sorted(o.name for o in bpy.context.scene.objects if not o.name.startswith('LOD'))
    rec['lod_objects'] = sorted(o.name for o in bpy.context.scene.objects if o.name.startswith('LOD'))
    rec['axes'] = 'Blender: front -Y, left +X, up +Z, metres, origin on the ground at the body centre; FBX: -Z forward, Y up (Unity +Z forward)'
    lic = rec['licence']
    rec['credit'] = f"\"{rec['title']}\" by {rec['creator']} ({rec['url']}), {lic}; modified by CT Life (decimated, re-pivoted, atlas)" + \
        (' - the modified model is also CC-BY-SA 4.0' if 'ShareAlike' in lic else '')
    rec['fbx'] = os.path.relpath(export(name, out_dir), REPO)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(os.environ.get('CTL_SCRATCH', '/tmp'), name + '.blend'), compress=True)
    json.dump(rec, open(os.path.join(out_dir, 'manifest.json'), 'w'), indent=1)
    print('[veh_v3]', name, json.dumps({k: rec.get(k) for k in ('tris', 'size', 'parts', 'materials', 'paint')}), flush=True)
    if RENDER:
        import lookdev_render as L
        for o in bpy.context.scene.objects:
            if o.type == 'EMPTY':
                o.hide_render = True
        objs = list(bpy.context.scene.objects)
        L.setup_scene()
        L.render(objs, os.path.join(EVID, name + '.png'))
    return rec


def main():
    for name, spec in SPECS.items():
        if ONLY and name not in ONLY:
            continue
        if not os.path.exists(os.path.join(RAW, spec['uid'], 'model.glb')):
            print('[veh_v3] missing download', name, flush=True)
            continue
        process(name, spec)


main()
