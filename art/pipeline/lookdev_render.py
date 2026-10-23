"""CT Life vehicle lookdev (vehicles v3, 2026-10-21): one consistent review scene for every vehicle.

EEVEE, warm golden-hour sun + sky fill, neutral asphalt ground, 3/4 front view (front = -Y, driver side = +X), 1600x900.

Usage (standalone):  Blender -b --factory-startup -P lookdev_render.py -- <model.fbx|.glb|.blend> <out.png> [--res 1600x900]
Library use:          import lookdev_render as L; L.setup_scene(); L.render(objs, out_png)
FBX files are imported with the veh_build/veh_v3 export axes (-Z forward, Y up), which restores Blender's -Y-forward layout.
Objects whose name starts with LOD1_/LOD2_ (lower LODs parented under the LOD0 parts) are hidden.
"""
import bpy, sys, os, math
from mathutils import Vector

RES = (1600, 900)


def _engine():
    for e in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
        try:
            bpy.context.scene.render.engine = e
            return e
        except TypeError:
            continue


def setup_scene(res=RES):
    sc = bpy.context.scene
    _engine()
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    try:
        sc.eevee.taa_render_samples = 64
        sc.eevee.use_shadows = True
    except AttributeError:
        pass
    try:
        sc.eevee.use_raytracing = True
    except AttributeError:
        pass
    sc.view_settings.view_transform = 'AgX'
    try:
        sc.view_settings.look = 'AgX - Medium High Contrast'
    except TypeError:
        pass
    # world: a soft blue-to-warm sky gradient (fill light), not an HDRI, so every render matches
    w = bpy.data.worlds.new('LookdevSky'); sc.world = w; w.use_nodes = True
    nt = w.node_tree; nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputWorld')
    bg = nt.nodes.new('ShaderNodeBackground'); bg.inputs['Strength'].default_value = 0.9
    tc = nt.nodes.new('ShaderNodeTexCoord'); sep = nt.nodes.new('ShaderNodeSeparateXYZ')
    ramp = nt.nodes.new('ShaderNodeValToRGB')
    ramp.color_ramp.elements[0].position = 0.0; ramp.color_ramp.elements[0].color = (0.95, 0.72, 0.50, 1)   # horizon, warm
    ramp.color_ramp.elements[1].position = 0.55; ramp.color_ramp.elements[1].color = (0.36, 0.52, 0.80, 1)  # zenith, blue
    nt.links.new(tc.outputs['Generated'], sep.inputs[0])
    mr = nt.nodes.new('ShaderNodeMapRange'); mr.inputs['From Min'].default_value = 0.5; mr.inputs['From Max'].default_value = 1.0
    nt.links.new(sep.outputs['Z'], mr.inputs['Value']); nt.links.new(mr.outputs['Result'], ramp.inputs['Fac'])
    nt.links.new(ramp.outputs['Color'], bg.inputs['Color']); nt.links.new(bg.outputs[0], out.inputs[0])
    # golden-hour sun: low, warm, from front-left of the car
    sun_d = bpy.data.lights.new('Sun', 'SUN'); sun_d.energy = 4.2; sun_d.color = (1.0, 0.80, 0.58); sun_d.angle = math.radians(2.5)
    sun = bpy.data.objects.new('Sun', sun_d); sc.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(62), 0, math.radians(-38))
    # asphalt ground
    me = bpy.data.meshes.new('Ground'); s = 400
    me.from_pydata([(-s, -s, 0), (s, -s, 0), (s, s, 0), (-s, s, 0)], [], [(0, 1, 2, 3)])
    g = bpy.data.objects.new('Ground', me); sc.collection.objects.link(g)
    m = bpy.data.materials.new('Asphalt'); m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    tco = m.node_tree.nodes.new('ShaderNodeTexCoord')
    noise = m.node_tree.nodes.new('ShaderNodeTexNoise'); noise.inputs['Scale'].default_value = 9; noise.inputs['Detail'].default_value = 12
    m.node_tree.links.new(tco.outputs['Object'], noise.inputs['Vector'])
    r2 = m.node_tree.nodes.new('ShaderNodeValToRGB')
    r2.color_ramp.elements[0].color = (0.075, 0.075, 0.078, 1); r2.color_ramp.elements[1].color = (0.15, 0.148, 0.145, 1)
    r2.color_ramp.elements[0].position = 0.35; r2.color_ramp.elements[1].position = 0.65
    m.node_tree.links.new(noise.outputs['Fac'], r2.inputs['Fac']); m.node_tree.links.new(r2.outputs['Color'], b.inputs['Base Color'])
    b.inputs['Roughness'].default_value = 0.88
    g.data.materials.append(m)
    cam_d = bpy.data.cameras.new('Cam'); cam_d.lens = 50; cam_d.clip_end = 2000
    cam = bpy.data.objects.new('Cam', cam_d); sc.collection.objects.link(cam); sc.camera = cam
    return cam


def import_model(path):
    ext = os.path.splitext(path)[1].lower()
    before = set(bpy.data.objects)
    if ext == '.fbx':
        bpy.ops.import_scene.fbx(filepath=path, axis_forward='-Z', axis_up='Y')
    elif ext in ('.glb', '.gltf'):
        bpy.ops.import_scene.gltf(filepath=path)
    elif ext == '.blend':
        with bpy.data.libraries.load(path) as (src, dst):
            dst.objects = list(src.objects)
        for o in dst.objects:
            if o is not None:
                bpy.context.scene.collection.objects.link(o)
    return [o for o in bpy.data.objects if o not in before]


def visible_meshes(objs):
    out = []
    for o in objs:
        lod = o.name.startswith('LOD1_') or o.name.startswith('LOD2_')
        o.hide_render = lod or o.hide_render
        if o.type == 'MESH' and not o.hide_render:
            out.append(o)
    return out


def bounds(objs):
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return mn, mx


VIEWS = {'front34': (0.62, -0.78, 0.20), 'side': (1.0, 0.0, 0.08), 'rear34': (0.62, 0.78, 0.22), 'top': (0.3, -0.4, 1.0), 'side_y': (0.0, -1.0, 0.08)}


def render(objs, out_png, fill=0.86, view='front34'):
    bpy.context.view_layer.update()
    meshes = visible_meshes(objs)
    mn, mx = bounds(meshes)
    c = (mn + mx) / 2
    cam = bpy.context.scene.camera
    # 3/4 front, driver side (+X), slightly above
    d = Vector(VIEWS[view]).normalized()
    sc = bpy.context.scene
    tan_h = math.tan(2 * math.atan(36 / 2 / cam.data.lens) / 2)
    tan_v = tan_h * sc.render.resolution_y / sc.render.resolution_x
    # fit the actual silhouette (sampled vertices), not the bounding box corners
    corners = []
    for o in meshes:
        step = max(1, len(o.data.vertices) // 1500)
        corners += [o.matrix_world @ o.data.vertices[i].co for i in range(0, len(o.data.vertices), step)]

    def extent(dist):
        loc = c + d * dist
        rot = (c - loc).to_track_quat('-Z', 'Y')
        inv = rot.to_matrix().inverted()
        m = 0.0
        for p in corners:
            q = inv @ (p - loc)
            if -q.z <= 1e-3:
                return 99.0
            m = max(m, abs(q.x) / (-q.z) / tan_h, abs(q.y) / (-q.z) / tan_v)
        return m
    lo, hi = 0.1, 2000.0
    for _ in range(60):
        mid = (lo + hi) / 2
        if extent(mid) > fill:
            lo = mid
        else:
            hi = mid
    cam.location = c + d * hi
    cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
    os.makedirs(os.path.dirname(out_png), exist_ok=True)
    sc.render.filepath = out_png
    bpy.ops.render.render(write_still=True)
    print('[lookdev] wrote', out_png, 'size', tuple(round(v, 2) for v in (mx - mn)), flush=True)


if __name__ == '__main__':
    argv = sys.argv[sys.argv.index('--') + 1:]
    res = RES
    if '--res' in argv:
        res = tuple(int(x) for x in argv[argv.index('--res') + 1].split('x'))
    bpy.ops.wm.read_factory_settings(use_empty=True)
    setup_scene(res)
    objs = import_model(argv[0])
    render(objs, argv[1])
