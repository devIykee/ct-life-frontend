"""Locomotion v3 review renders: one character playing one clip FBX (same rig) at a playback rate while the body
travels forward at the game speed over a 0.5 m checker floor (so foot sliding is visible against the ground), side
or three-quarter camera tracking the body. EEVEE, PNG frames (the MP4 + side-by-side is assembled with ffmpeg).

  Blender -b --factory-startup -P tools/anim/loco_v3/render_loco_blender.py -- --char Snow.fbx --clip walk.fbx \
      --rate 1.0 --speed 1.6 --seconds 6 --out /dir/frame_ --res 640 720 [--view side|34|front] [--frames 0,5,10]
"""
import bpy, sys, os, math, argparse
from mathutils import Vector


def args():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument('--char', required=True)
    ap.add_argument('--clip', required=True)
    ap.add_argument('--rate', type=float, default=1.0)
    ap.add_argument('--speed', type=float, default=0.0)
    ap.add_argument('--seconds', type=float, default=6.0)
    ap.add_argument('--out', required=True)
    ap.add_argument('--res', type=int, nargs=2, default=[640, 720])
    ap.add_argument('--view', default='side')
    ap.add_argument('--frames', default='')
    ap.add_argument('--samples', type=int, default=16)
    ap.add_argument('--zoom', type=float, default=1.0)
    ap.add_argument('--track', default='object', help="object | hips (follow the hips bone in XY: root-motion clips)")
    return ap.parse_args(argv)


def main():
    a = args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.fps = 30
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=a.char, ignore_leaf_bones=False, automatic_bone_orientation=False)
    new = [o for o in bpy.data.objects if o not in before]
    arm = next(o for o in new if o.type == 'ARMATURE')
    meshes = [o for o in new if o.type == 'MESH']
    for o in new:
        if o.type not in ('ARMATURE', 'MESH'):
            bpy.data.objects.remove(o, do_unlink=True)
    # textures next to the character FBX (Snow/Rain atlases) when the FBX did not resolve them
    d = os.path.dirname(a.char)
    for m in [m for o in meshes for m in o.data.materials if m]:
        if not m.use_nodes:
            continue
        nt = m.node_tree
        bsdf = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        for n in nt.nodes:   # images the FBX referenced but that are not on disk (sources renamed on import)
            if n.type == 'TEX_IMAGE' and n.image and not n.image.has_data and not os.path.exists(bpy.path.abspath(n.image.filepath)):
                n.image = None
        has_tex = any(n.type == 'TEX_IMAGE' and n.image for n in nt.nodes)
        if bsdf and not has_tex:
            cands = [f for f in os.listdir(d) if f.lower().endswith(('basecolor.png', 'albedo.png'))]
            if cands:
                tex = nt.nodes.new('ShaderNodeTexImage')
                tex.image = bpy.data.images.load(os.path.join(d, cands[0]))
                nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
        if bsdf:
            bsdf.inputs['Roughness'].default_value = 0.7
    arm.animation_data_clear()
    # clip
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=a.clip, use_anim=True, ignore_leaf_bones=False, automatic_bone_orientation=False)
    cnew = [o for o in bpy.data.objects if o not in before]
    carm = next(o for o in cnew if o.type == 'ARMATURE')
    act = carm.animation_data.action
    for o in cnew:
        bpy.data.objects.remove(o, do_unlink=True)
    f0, f1 = act.frame_range
    # our clips key the rest pose one frame before the take
    take0 = f0 + 1 if f1 - f0 > 2 else f0
    arm.animation_data_create()
    tr = arm.animation_data.nla_tracks.new()
    st = tr.strips.new('clip', 0, act)
    st.action_frame_start, st.action_frame_end = take0, f1
    st.scale = 1.0 / max(0.05, a.rate)
    total = int(round(a.seconds * 30))
    st.repeat = max(1.0, total * a.rate / max(1.0, f1 - take0) + 1)
    st.extrapolation = 'HOLD'
    st.blend_type = 'REPLACE'
    if hasattr(st, 'action_slot') and act.slots:
        st.action_slot = act.slots[0]
    # travel: the armature object moves along its facing (-Y)
    arm.location = (0, 0, 0)
    arm.keyframe_insert('location', index=1, frame=0)
    arm.location.y = -a.speed * (total / 30.0)
    arm.keyframe_insert('location', index=1, frame=total)
    try:
        from bpy_extras import anim_utils
        cb = anim_utils.action_get_channelbag_for_slot(arm.animation_data.action, arm.animation_data.action_slot)
        for fc in cb.fcurves:
            for kp in fc.keyframe_points:
                kp.interpolation = 'LINEAR'
    except Exception:
        pass
    # floor: 0.5 m checker + 1 m lines
    bpy.ops.mesh.primitive_plane_add(size=400, location=(0, 0, 0))
    fl = bpy.context.active_object
    mat = bpy.data.materials.new('floor')
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes['Principled BSDF']
    chk = nt.nodes.new('ShaderNodeTexChecker')
    chk.inputs['Color1'].default_value = (0.42, 0.42, 0.44, 1)
    chk.inputs['Color2'].default_value = (0.30, 0.30, 0.32, 1)
    chk.inputs['Scale'].default_value = 2.0   # object coords in metres -> 0.5 m cells
    tc = nt.nodes.new('ShaderNodeTexCoord')
    nt.links.new(tc.outputs['Object'], chk.inputs['Vector'])
    nt.links.new(chk.outputs['Color'], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = 0.9
    fl.data.materials.append(mat)
    # light + world
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN'))
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35))
    sc.collection.objects.link(sun)
    w = bpy.data.worlds.new('w')
    w.use_nodes = True
    w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.55, 0.6, 0.68, 1)
    w.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.9
    sc.world = w
    # camera tracking the body (follows the travel only)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.lens = 50
    tgt = bpy.data.objects.new('camtgt', None)
    sc.collection.objects.link(tgt)
    tgt.parent = arm
    tgt.location = (0, 0, 0.95)
    z = a.zoom
    if a.view == 'side':
        off = Vector((5.2, 0, 1.0)) * z
    elif a.view == 'front':
        off = Vector((0.0, -5.2, 1.0)) * z
    else:
        off = Vector((3.8, -3.6, 1.2)) * z
    holder = bpy.data.objects.new('holder', None)
    sc.collection.objects.link(holder)
    holder.parent = arm
    if a.track == 'hips':
        hips = next(b.name for b in arm.data.bones if b.name in ('Hips', 'pelvis'))
        for o in (holder, tgt):
            cl = o.constraints.new('COPY_LOCATION')
            cl.target = arm
            cl.subtarget = hips
            cl.use_z = False
            cl.use_offset = True
    cam.parent = holder
    cam.location = off
    con = cam.constraints.new('TRACK_TO')
    con.target = tgt
    con.track_axis = 'TRACK_NEGATIVE_Z'
    con.up_axis = 'UP_Y'
    # render
    eng = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
    sc.render.engine = eng
    try:
        sc.eevee.taa_render_samples = a.samples
    except Exception:
        pass
    sc.render.resolution_x, sc.render.resolution_y = a.res
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = 'PNG'
    sc.view_settings.view_transform = 'AgX' if 'AgX' in [x.identifier for x in sc.view_settings.bl_rna.properties['view_transform'].enum_items] else 'Filmic'
    frames = [int(x) for x in a.frames.split(',')] if a.frames else list(range(total))
    os.makedirs(os.path.dirname(a.out), exist_ok=True)
    for f in frames:
        sc.frame_set(f)
        sc.render.filepath = '%s%04d.png' % (a.out, f)
        bpy.ops.render.render(write_still=True)
    print('RENDER_OK', len(frames), eng)


main()
