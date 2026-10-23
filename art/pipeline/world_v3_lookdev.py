"""World v3 lookdev: assemble a Timeline Downtown street / aerial block / apartment interior from the processed kit and
render it in EEVEE at golden hour (phone landscape).

Usage: Blender -b --factory-startup -P world_v3_lookdev.py -- <street|aerial|interior> <out.png> [--res 2670x1200] [--samples 64]
Reads art/processed/world_v3/*/<name>.fbx (LOD0 only). Ground textures and the sky come from the CC0 Poly Haven files
already in ct-life/art/raw/polyhaven.
"""
import bpy, sys, os, math, random
import numpy as np
from mathutils import Vector, Euler

HERE = os.path.dirname(os.path.abspath(__file__))
K = os.path.abspath(os.path.join(HERE, '..', 'processed', 'world_v3'))
PH = os.environ.get('CTL_PH', '/Users/mac/Documents/codes/projects/ct-life/art/raw/polyhaven')
a = sys.argv[sys.argv.index('--') + 1:]
MODE, OUTP = a[0], a[1]
RES = tuple(int(x) for x in (a[a.index('--res') + 1] if '--res' in a else '1600x720').split('x'))
SAMPLES = int(a[a.index('--samples') + 1]) if '--samples' in a else 48
random.seed(7)

bpy.ops.wm.read_factory_settings(use_empty=True)
SC = bpy.context.scene
_cache = {}


def find(name):
    for fam in os.listdir(K):
        p = os.path.join(K, fam, name + '.fbx')
        if os.path.exists(p):
            return p
    raise FileNotFoundError(name)


def asset(name, lod=0):
    """Import once (hidden template); return template object (LOD<lod>)."""
    key = (name, lod)
    if key in _cache:
        return _cache[key]
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=find(name))
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == 'MESH']
    pick = [o for o in meshes if o.name.endswith(f'_LOD{lod}')] or [o for o in meshes if '_LOD' not in o.name] or meshes[:1]
    t = pick[0]
    for o in new:
        if o is not t:
            bpy.data.objects.remove(o)
    t.parent = None
    t.matrix_world = t.matrix_world.copy()
    # bake the importer's axis transform into the mesh so instances only need loc/rot
    t.data.transform(t.matrix_world)
    t.matrix_world = Euler((0, 0, 0)).to_matrix().to_4x4()
    t.hide_render = True
    t.hide_viewport = True
    vs = np.array([v.co[:] for v in t.data.vertices])
    t['size'] = (vs.max(0) - vs.min(0)).tolist()
    _cache[key] = t
    for m in t.data.materials:
        if m and m.node_tree:
            for n in m.node_tree.nodes:
                if n.type == 'BSDF_PRINCIPLED' and m.name.endswith('_glass'):
                    m.surface_render_method = 'BLENDED'
    return t


def place(name, x, y, yaw=0.0, z=0.0, s=1.0):
    t = asset(name)
    o = t.copy()
    o.hide_render = False
    o.hide_viewport = False
    SC.collection.objects.link(o)
    o.location = (x, y, z)
    o.rotation_euler = (0, 0, math.radians(yaw))
    o.scale = (s, s, s)
    return o


def size(name):
    return asset(name)['size']


def pbr(name, folder, tile):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes['Principled BSDF']
    tc = nt.nodes.new('ShaderNodeTexCoord')
    mp = nt.nodes.new('ShaderNodeMapping')
    mp.inputs['Scale'].default_value = (1 / tile, 1 / tile, 1 / tile)
    nt.links.new(tc.outputs['Object'], mp.inputs['Vector'])
    base = os.path.join(PH, 'textures', folder, folder)

    def tex(suffix, cs):
        t = nt.nodes.new('ShaderNodeTexImage')
        t.image = bpy.data.images.load(f'{base}_{suffix}_1k.jpg')
        t.image.colorspace_settings.name = cs
        t.projection = 'BOX'
        t.projection_blend = 0.25
        nt.links.new(mp.outputs['Vector'], t.inputs['Vector'])
        return t
    d = tex('diff', 'sRGB')
    nt.links.new(d.outputs['Color'], b.inputs['Base Color'])
    r = tex('rough', 'Non-Color')
    nt.links.new(r.outputs['Color'], b.inputs['Roughness'])
    n = tex('nor_gl', 'Non-Color')
    nm = nt.nodes.new('ShaderNodeNormalMap')
    nm.inputs['Strength'].default_value = 0.8
    nt.links.new(n.outputs['Color'], nm.inputs['Color'])
    nt.links.new(nm.outputs['Normal'], b.inputs['Normal'])
    return m


def acg(name, folder, tile):
    """ambientCG 1K material (CC0) from ct-life/art/raw/ambientcg/<folder>."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes['Principled BSDF']
    tc = nt.nodes.new('ShaderNodeTexCoord')
    mp = nt.nodes.new('ShaderNodeMapping')
    mp.inputs['Scale'].default_value = (1 / tile, 1 / tile, 1 / tile)
    nt.links.new(tc.outputs['Object'], mp.inputs['Vector'])
    base = os.path.join(os.path.dirname(PH), 'ambientcg', folder, f'{folder}_1K-JPG')

    def tex(suffix, cs):
        t = nt.nodes.new('ShaderNodeTexImage')
        t.image = bpy.data.images.load(f'{base}_{suffix}.jpg')
        t.image.colorspace_settings.name = cs
        t.projection = 'BOX'
        nt.links.new(mp.outputs['Vector'], t.inputs['Vector'])
        return t
    nt.links.new(tex('Color', 'sRGB').outputs['Color'], b.inputs['Base Color'])
    nt.links.new(tex('Roughness', 'Non-Color').outputs['Color'], b.inputs['Roughness'])
    nm = nt.nodes.new('ShaderNodeNormalMap')
    nt.links.new(tex('NormalGL', 'Non-Color').outputs['Color'], nm.inputs['Color'])
    nt.links.new(nm.outputs['Normal'], b.inputs['Normal'])
    return m


def flat(name, rgb, rough=0.6):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*rgb, 1)
    b.inputs['Roughness'].default_value = rough
    return m


def box(name, x0, x1, y0, y1, z0, z1, mat):
    bpy.ops.mesh.primitive_cube_add(size=1)
    o = bpy.context.active_object
    o.name = name
    o.scale = (x1 - x0, y1 - y0, z1 - z0)
    o.location = ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    o.data.materials.append(mat)
    return o


HAZE = {'amount': 0.0, 'color': (0.98, 0.80, 0.62)}


def lighting(sun_az=-35.0, sun_el=13.0, haze=0.35, warm=(1.0, 0.70, 0.45), sun_power=5.0, sky=0.55):
    w = bpy.data.worlds.new('sky')
    SC.world = w
    w.use_nodes = True
    nt = w.node_tree
    bg = nt.nodes['Background']
    env = nt.nodes.new('ShaderNodeTexEnvironment')
    env.image = bpy.data.images.load(os.path.join(PH, 'hdris', 'belfast_sunset_puresky', 'belfast_sunset_puresky_2k.hdr'))
    tc = nt.nodes.new('ShaderNodeTexCoord')
    mp = nt.nodes.new('ShaderNodeMapping')
    mp.inputs['Rotation'].default_value = (0, 0, math.radians(sun_az + float(os.environ.get('LD_HDRI', 0))))
    nt.links.new(tc.outputs['Generated'], mp.inputs['Vector'])
    nt.links.new(mp.outputs['Vector'], env.inputs['Vector'])
    nt.links.new(env.outputs['Color'], bg.inputs['Color'])
    bg.inputs['Strength'].default_value = sky
    haze = float(os.environ.get('LD_HAZE', haze))
    HAZE['amount'] = haze
    if False:
        vol = nt.nodes.new('ShaderNodeVolumePrincipled')
        vol.inputs['Density'].default_value = haze
        vol.inputs['Color'].default_value = (0.95, 0.85, 0.75, 1)
        vol.inputs['Anisotropy'].default_value = 0.55
        nt.links.new(vol.outputs['Volume'], nt.nodes['World Output'].inputs['Volume'])
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN'))
    sun.data.energy = sun_power
    sun.data.color = warm
    sun.data.angle = math.radians(1.5)
    sun.rotation_euler = (math.radians(90 - sun_el), 0, math.radians(sun_az))
    SC.collection.objects.link(sun)
    return sun


def render_setup(samples=SAMPLES):
    SC.render.engine = 'BLENDER_EEVEE'
    e = SC.eevee
    e.taa_render_samples = samples
    for attr, val in (('use_raytracing', True), ('use_shadows', True), ('use_volumetric_shadows', True),
                      ('volumetric_end', 400.0), ('volumetric_tile_size', '4'), ('shadow_resolution_scale', 1.0),
                      ('use_gtao', True), ('fast_gi_distance', 30.0)):
        if hasattr(e, attr):
            try:
                setattr(e, attr, val)
            except Exception:
                pass
    SC.render.resolution_x, SC.render.resolution_y = RES
    SC.render.resolution_percentage = 100
    SC.view_settings.view_transform = 'AgX'
    try:
        SC.view_settings.look = 'AgX - Medium High Contrast'
    except Exception:
        pass
    SC.view_settings.exposure = 0.0
    SC.render.filepath = OUTP
    SC.render.image_settings.file_format = 'PNG'
    compositor()


def compositor():
    """Aerial haze from the mist pass (EEVEE world volumes render black in this build) + a soft bloom."""
    vl = SC.view_layers[0]
    vl.use_pass_mist = True
    SC.world.mist_settings.start = 15.0
    SC.world.mist_settings.depth = 260.0
    SC.world.mist_settings.falloff = 'QUADRATIC'
    ng = bpy.data.node_groups.new('comp', 'CompositorNodeTree')
    ng.interface.new_socket('Image', in_out='OUTPUT', socket_type='NodeSocketColor')
    SC.compositing_node_group = ng
    N = ng.nodes
    rl = N.new('CompositorNodeRLayers')
    mul = N.new('ShaderNodeMath')
    mul.operation = 'MULTIPLY'
    mul.inputs[1].default_value = HAZE['amount']
    ng.links.new(rl.outputs['Mist'], mul.inputs[0])
    mix = N.new('ShaderNodeMix')
    mix.data_type = 'RGBA'
    mix.blend_type = 'MIX'
    ng.links.new(mul.outputs[0], mix.inputs['Factor'])
    ng.links.new(rl.outputs['Image'], mix.inputs['A'])
    mix.inputs['B'].default_value = (*HAZE['color'], 1)
    gl = N.new('CompositorNodeGlare')
    try:
        gl.glare_type = 'BLOOM'
    except Exception:
        pass
    for k, v in (('Threshold', 1.2), ('Strength', 0.25), ('Size', 0.5)):
        if k in gl.inputs:
            gl.inputs[k].default_value = v
    ng.links.new(mix.outputs['Result'], gl.inputs['Image'])
    out = N.new('NodeGroupOutput')
    ng.links.new(gl.outputs['Image'], out.inputs[0])


def camera(loc, look, lens=26):
    c = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
    SC.collection.objects.link(c)
    c.location = loc
    c.rotation_euler = (Vector(look) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    c.data.lens = lens
    c.data.clip_end = 2000
    c.data.sensor_width = 36
    SC.camera = c
    return c


# ------------------------------------------------------------------ the street
CAM = dict(loc=(-44.0, 8.2, 1.7), look=(20, -1.0, 5.0), lens=24)
ROAD_W = 4.6          # half width (two lanes + parking strip each way)
WALK = 4.8            # sidewalk width
KERB = 0.15


def street(x0=-70, x1=90, cross=(28.0,)):
    asph = pbr('asphalt', 'asphalt_02', 5.0)
    pave = acg('paving', os.environ.get('LD_PAVE', 'PavingStones128'), float(os.environ.get('LD_PAVE_TILE', 2.0))) if os.environ.get('LD_PAVE', 'PavingStones128')[0].isupper() else pbr('paving', os.environ['LD_PAVE'], float(os.environ.get('LD_PAVE_TILE', 1.6)))
    conc = flat('kerb', (0.62, 0.61, 0.58), 0.7)
    paint = flat('paint', (0.92, 0.92, 0.88), 0.5)
    yell = flat('paint_y', (0.95, 0.75, 0.25), 0.5)
    box('road', x0, x1, -ROAD_W, ROAD_W, -0.05, 0.0, asph)
    for sg in (1, -1):
        y0, y1 = sorted((sg * ROAD_W, sg * (ROAD_W + WALK + 8.0)))     # runs under the building line (recessed fronts)
        box(f'walk{sg}', x0, x1, y0, y1, -0.05, KERB, pave)
        ky0, ky1 = sorted((sg * ROAD_W, sg * (ROAD_W + 0.22)))
        box(f'kerb{sg}', x0, x1, ky0, ky1, 0.0, KERB + 0.01, conc)
        # edge line
        ey = sg * (ROAD_W - 0.35)
        box(f'edge{sg}', x0, x1, ey - 0.06, ey + 0.06, 0.0, 0.006, paint)
    # centre double yellow
    for dy in (-0.09, 0.09):
        box('centre', x0, x1, dy - 0.05, dy + 0.05, 0.0, 0.006, yell)
    for cx in cross:
        for k in range(-4, 5):
            box('zebra', cx - 1.6, cx + 1.6, k * 1.0 - 0.3, k * 1.0 + 0.3, 0.0, 0.007, paint)
        for sg in (1, -1):
            box('stop', cx - 2.4, cx - 2.1, 0 if sg > 0 else -ROAD_W + 0.4, ROAD_W - 0.4 if sg > 0 else 0, 0.0, 0.007, paint)
    # ground plane far away
    box('ground', -600, 600, -600, 600, -0.2, -0.06, flat('ground', (0.35, 0.36, 0.33), 0.9))


def frontage(ids, side, x0, x1, gap=(0.0, 0.6), setback=0.0):
    """Line a street side with buildings facing the road. side +1: north (fronts face -Y), -1: south."""
    x = x0
    i = 0
    yb = ROAD_W + WALK + setback
    placed = []
    while x < x1 and i < 200:
        n = ids[i % len(ids)]
        w, d, h = size(n)
        yaw = 0.0 if side > 0 else 180.0
        cx = x + w / 2
        cy = side * (yb + d / 2)
        placed.append(place(n, cx, cy, yaw, KERB))
        x += w + random.uniform(*gap)
        i += 1
    return placed


def street_scene():
    street()
    north = ['nc_building_01', 'nc_building_03', 'm99_chicago_a', 'nc_building_04', 'nc_building_02', 'nc_building_05', 'nc_building_01']
    south = ['nc_building_05', 'nc_building_02', 'nc_building_03', 'nc_building_04', 'nc_building_01', 'm99_apt_complex']
    frontage([n for n in north if exists(n)], 1, -60, 95)
    frontage([n for n in south if exists(n)], -1, -60, 95)
    if exists('m99_office_tower'):
        # skyline behind the north row, kept out of the low sun's path (a 105 m tower casts a 600 m shadow at 10 deg)
        place('m99_office_tower', 55, 78, 0, 0)
        place('m99_office_tower', 150, 95, 90, 0)
    # street furniture
    lamp = first('nc_prop_lamp_double_01', 'ph_street_lamp_01')
    for k, x in enumerate(range(-56, 92, 16)):
        for sg in (1, -1):
            place(lamp, x + (8 if sg < 0 else 0), sg * (ROAD_W + 0.55), 0 if sg > 0 else 180, KERB)
    trees = [n for n in ('lp_maple_01', 'lp_maple_03', 'lp_maple_05', 'nc_tree_01', 'nc_tree_03', 'nc_tree_04') if exists(n)]
    for x in range(-52, 92, 16):
        for sg in (1, -1):
            if random.random() < 0.9:
                place(trees[(x // 16 + (sg > 0)) % len(trees)], x + (8 if sg < 0 else 0) + 4, sg * (ROAD_W + 1.3), random.uniform(0, 360), KERB)
    bench = first('tj_city_prop_bench', 'ph_modular_street_seating')
    place(bench, -35.5, ROAD_W + 2.6, 180, KERB)
    place(bench, 12, -(ROAD_W + 2.3), 0, KERB)
    bin_ = first('tj_city_prop_bin', 'ph_barrel_03')
    place(bin_, -32.2, ROAD_W + 0.9, 0, KERB)
    place(bin_, 16, -(ROAD_W + 0.9), 0, KERB)
    if exists('ph_fire_hydrant'):
        place('ph_fire_hydrant', -27, ROAD_W + 0.7, 90, KERB)
    for k in range(5):
        place(first('nc_prop_delineator_01', 'ph_street_lamp_02'), 28 - 2 + k, ROAD_W + 0.45, 0, KERB)
    for pp, x in (('ph_potted_plant_02', -31), ('ph_potted_plant_01', -2.5), ('ph_potted_plant_02', 4.0)):
        if exists(pp):
            place(pp, x, ROAD_W + WALK - 0.5, 0, KERB)
    # a little road work at the far corner
    for k, n in enumerate(('nc_prop_cone_02', 'nc_prop_cone_02', 'nc_prop_cone_03', 'nc_prop_barricade_a_02')):
        if exists(n):
            place(n, 44 + k * 1.2, -(ROAD_W - 1.0), 20 * k)
    car = first('veh_hatchback_retro', None)
    if car:
        place(car, -14, -(ROAD_W - 1.2), 0, 0)
        place(car, 22, (ROAD_W - 1.2), 180, 0)
    lighting(sun_az=float(os.environ.get('LD_AZ', 112)), sun_el=float(os.environ.get('LD_EL', 12)))
    render_setup()
    camera(CAM['loc'], CAM['look'], lens=CAM['lens'])


def exists(n):
    try:
        find(n)
        return True
    except FileNotFoundError:
        return False


def first(*ns):
    for n in ns:
        if n and exists(n):
            return n
    return None


def road_y(x, y0, y1, mats):
    """A north-south road centred on x (same section as the main street)."""
    asph, pave, conc, paint, yell = mats
    box('road_y', x - ROAD_W, x + ROAD_W, y0, y1, -0.05, 0.0, asph)
    for sg in (1, -1):
        a0, a1 = sorted((x + sg * ROAD_W, x + sg * (ROAD_W + WALK)))
        box('walk_y', a0, a1, y0, y1, -0.05, KERB, pave)
    for dx in (-0.09, 0.09):
        box('centre_y', x + dx - 0.05, x + dx + 0.05, y0, y1, 0.0, 0.006, yell)


def frontage_y(ids, x_edge, side, y0, y1):
    """Buildings along a north-south road edge; side +1: east of the road (fronts face -X), -1: west."""
    y = y0
    i = 0
    while y < y1 and i < 100:
        n = ids[i % len(ids)]
        w, d, h = size(n)
        if y + w > y1 + 2:
            i += 1
            if i > len(ids) * 2:
                break
            continue
        yaw = -90.0 if side > 0 else 90.0
        place(n, x_edge + side * d / 2, y + w / 2, yaw, KERB)
        y += w + random.uniform(0.0, 0.8)
        i += 1


def aerial_scene():
    """One full Timeline Downtown block (about 100 x 46 m) ringed by streets, with a courtyard."""
    street(x0=-120, x1=140, cross=(-55.0, 55.0))
    asph = bpy.data.materials['asphalt']; pave = bpy.data.materials['paving']
    mats = (asph, pave, bpy.data.materials['kerb'], bpy.data.materials['paint'], bpy.data.materials['paint_y'])
    YB = 66.0                                                    # back street centre
    box('road_back', -120, 140, YB - ROAD_W, YB + ROAD_W, -0.05, 0.0, asph)
    box('walk_back', -120, 140, YB - ROAD_W - WALK, YB - ROAD_W, -0.05, KERB, pave)
    box('walk_back2', -120, 140, YB + ROAD_W, YB + ROAD_W + WALK, -0.05, KERB, pave)
    for xr in (-55.0, 55.0):
        road_y(xr, ROAD_W, YB - ROAD_W, mats)
    inner = ROAD_W + WALK
    # courtyard ground
    box('court', -55 + inner, 55 - inner, inner, YB - inner, -0.05, KERB - 0.02, acg('court', 'Grass004', 3.0))
    box('court_path', -2, 2, inner, YB - inner, -0.05, KERB - 0.01, bpy.data.materials['paving'])
    box('court_park', 20, 44, 26, 40, -0.05, KERB - 0.005, asph)
    north = [n for n in ('nc_building_01', 'nc_building_03', 'm99_chicago_a', 'nc_building_05', 'nc_building_02', 'nc_building_04') if exists(n)]
    frontage(north, 1, -55 + inner, 55 - inner)
    back = [n for n in ('nc_building_02', 'nc_building_04', 'nc_building_01', 'nc_building_03', 'nc_building_05') if exists(n)]
    x = -55 + inner
    i = 0
    while x < 55 - inner - 6:
        n = back[i % len(back)]
        w, d, h = size(n)
        if x + w > 55 - inner + 1:
            break
        place(n, x + w / 2, YB - ROAD_W - WALK - d / 2, 180, KERB)
        x += w + 0.4
        i += 1
    side = [n for n in ('nc_building_05', 'nc_building_03', 'nc_building_06', 'nc_building_01') if exists(n)]
    frontage_y(side, 55 - inner, -1, inner + 9, YB - inner - 9)
    frontage_y(side[::-1], -55 + inner, 1, inner + 9, YB - inner - 9)
    # south side of the main street and across the side streets: a hint of the next blocks
    south = [n for n in ('nc_building_04', 'nc_building_03', 'nc_building_06', 'nc_building_01', 'nc_building_05') if exists(n)]
    frontage(south, -1, -110, 20)
    if exists('m99_apt_complex'):
        place('m99_apt_complex', 50, -(ROAD_W + WALK + size('m99_apt_complex')[1] / 2), 180, KERB)
    if exists('m99_office_tower'):
        place('m99_office_tower', 110, 110, 0, 0)
    trees = [n for n in ('lp_maple_01', 'lp_maple_03', 'nc_tree_01', 'nc_tree_03', 'nc_tree_04') if exists(n)]
    for k in range(9):
        place(trees[k % len(trees)], -20 + (k % 3) * 18 + random.uniform(-3, 3), 30 + (k // 3) * 6 + random.uniform(-2, 2), random.uniform(0, 360), KERB)
    lamp = first('nc_prop_lamp_double_01', 'ph_street_lamp_01')
    for x in range(-110, 140, 16):
        for sg in (1, -1):
            place(lamp, x, sg * (ROAD_W + 0.55), 0 if sg > 0 else 180, KERB)
            place(lamp, x + 8, YB + sg * (ROAD_W + 0.55), 0 if sg > 0 else 180, KERB)
        place(trees[(x // 16) % len(trees)], x + 4, ROAD_W + 1.3, x * 7 % 360, KERB)
    car = first('veh_hatchback_retro', None)
    if car:
        for x, sg in ((-30, 1), (-8, -1), (14, 1), (33, -1)):
            place(car, x, sg * (ROAD_W - 1.2), 0 if sg < 0 else 180, 0)
    lighting(sun_az=float(os.environ.get('LD_AZ', 125)), sun_el=float(os.environ.get('LD_EL', 22)), haze=0.18)
    bpy.data.materials['ground'].node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.20, 0.21, 0.21, 1)
    render_setup()
    camera((-62, -36, 52), (4, 36, 0), lens=34)


def interior_scene():
    name = os.environ.get('LOOKDEV_ROOM', 'int_living_white')
    o = place(name, 0, 0, 0, 0)
    w, d, h = size(name)
    lighting(sun_az=-40, sun_el=18, haze=0.0, sun_power=4.0, sky=0.8)
    render_setup()
    camera((w * 0.38, -d * 0.40, 1.6), (-w * 0.15, d * 0.2, 1.1), lens=20)


def studio_scene():
    """Fallback interior proof from local CC0 furniture: a 6 x 5 m studio apartment shell sized for a building door."""
    W_, D_, H_ = 6.0, 5.0, 2.8
    floor = pbr('floor', 'wood_floor', 2.0)
    wall = flat('wall', (0.86, 0.82, 0.76), 0.85)
    trim = flat('trim', (0.92, 0.91, 0.88), 0.4)
    glass = bpy.data.materials.new('glass'); glass.use_nodes = True
    gb = glass.node_tree.nodes['Principled BSDF']; gb.inputs['Transmission Weight'].default_value = 1.0; gb.inputs['Roughness'].default_value = 0.02
    box('floor', -W_ / 2, W_ / 2, -D_ / 2, D_ / 2, -0.1, 0.0, floor)
    box('ceil', -W_ / 2, W_ / 2, -D_ / 2, D_ / 2, H_, H_ + 0.1, trim)
    t = 0.15
    box('wall_back', -W_ / 2, W_ / 2, D_ / 2, D_ / 2 + t, 0, H_, wall)
    box('wall_left', -W_ / 2 - t, -W_ / 2, -D_ / 2, D_ / 2, 0, H_, wall)
    # right wall with a big window (sun comes in here)
    wx0, wx1, wz0, wz1 = -1.4, 1.4, 0.55, 2.45
    box('wr_a', W_ / 2, W_ / 2 + t, -D_ / 2, wx0, 0, H_, wall)
    box('wr_b', W_ / 2, W_ / 2 + t, wx1, D_ / 2, 0, H_, wall)
    box('wr_c', W_ / 2, W_ / 2 + t, wx0, wx1, 0, wz0, wall)
    box('wr_d', W_ / 2, W_ / 2 + t, wx0, wx1, wz1, H_, wall)
    box('mullion', W_ / 2 + 0.05, W_ / 2 + 0.1, -0.03, 0.03, wz0, wz1, trim)
    box('pane', W_ / 2 + 0.07, W_ / 2 + 0.08, wx0, wx1, wz0, wz1, glass)
    # front wall with the entrance door opening (1.0 x 2.15 m, matches a building door_main cut)
    box('wf_a', -W_ / 2, -1.6, -D_ / 2 - t, -D_ / 2, 0, H_, wall)
    box('wf_b', -0.6, W_ / 2, -D_ / 2 - t, -D_ / 2, 0, H_, wall)
    box('wf_c', -1.6, -0.6, -D_ / 2 - t, -D_ / 2, 2.15, H_, wall)
    box('skirting', -W_ / 2, W_ / 2, D_ / 2 - 0.015, D_ / 2, 0, 0.09, trim)
    box('rug', -1.6, 1.0, -0.4, 1.6, 0.0, 0.012, flat('rug', (0.80, 0.74, 0.64), 0.95))
    place(first('ph_sofa_02', 'ph_sofa_01'), -0.3, D_ / 2 - 0.55, 180, 0)
    place('ph_modern_arm_chair_01', -2.2, 0.4, 250, 0)
    place('ph_modern_coffee_table_01', -0.3, 0.75, 90, 0)
    place('ph_modern_arm_chair_01', 1.6, -1.2, 120, 0)
    place('ph_potted_plant_01', 2.55, 1.9, 0, 0)
    place('ph_potted_plant_02', -2.6, D_ / 2 - 0.45, 0, 0)
    # warm practical lamp
    lamp = bpy.data.objects.new('lamp', bpy.data.lights.new('lamp', 'POINT'))
    lamp.data.energy = 60; lamp.data.color = (1.0, 0.78, 0.55); lamp.location = (-2.5, 2.0, 1.6)
    SC.collection.objects.link(lamp)
    place('ph_potted_plant_02', 2.6, 2.1, 30, 0)
    lighting(sun_az=float(os.environ.get('LD_AZ', 80)), sun_el=float(os.environ.get('LD_EL', 12)), haze=0.0, sun_power=8.0, sky=0.5)
    render_setup()
    SC.view_settings.exposure = -0.8
    camera((-2.55, -2.15, 1.6), (1.2, 1.5, 1.0), lens=18)


{'street': street_scene, 'aerial': aerial_scene, 'interior': interior_scene, 'studio': studio_scene}[MODE]()
print('[lookdev] objects', len([o for o in SC.objects if not o.hide_render]), 'cam', tuple(SC.camera.location))
bpy.ops.render.render(write_still=True)
print('[lookdev] saved', OUTP)
