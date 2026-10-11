"""Build and render CT Life title key art B ("Close duo") in Blender 5.1 (Cycles).

  Blender -b --factory-startup -P ka_build.py -- [--preview] [--samples N] [--out DIR] [--no-render] [--save-blend PATH]

Writes <out>/keyart-B-passes.exr (multilayer: Combined with alpha, Shadow Catcher). ka_post.py then
tone-maps, composites over the Unity clean plate and grades. See README.md.
"""
import math
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import bpy  # noqa: E402
from mathutils import Euler, Vector  # noqa: E402

import ka_look  # noqa: E402
import ka_params as P  # noqa: E402
import ka_pose  # noqa: E402
import ka_props  # noqa: E402
from ka_common import OUT, import_look, place_rig, u2b, upd, wmat  # noqa: E402

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []


def opt(name, default=None, cast=str):
    return cast(argv[argv.index(name) + 1]) if name in argv else default


PREVIEW = '--preview' in argv
OUTDIR = Path(opt('--out', str(OUT / 'render')))
SAMPLES = opt('--samples', 48 if PREVIEW else 512, int)
W, H = (1280, 575) if PREVIEW else (3840, 1726)
if '--res' in argv:
    W, H = (int(v) for v in opt('--res').split('x'))

t0 = time.time()
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
chars = bpy.data.collections.new('characters'); sc.collection.children.link(chars)
env = bpy.data.collections.new('environment'); sc.collection.children.link(env)
lights = bpy.data.collections.new('lights'); sc.collection.children.link(lights)


def move_to(ob, col):
    for c in list(ob.users_collection):
        c.objects.unlink(ob)
    col.objects.link(ob)


# ---------------------------------------------------------------- characters
fa, fm = import_look('player_f_kol', 'kol_f')
ma, mm = import_look('player_m_kol', 'kol_m')
report = {}
for arm, mesh, look, eye_z, smile, lens in ((fa, fm, 'player_f_kol', 1.555, P.SMILE_F, 'shades'),
                                            (ma, mm, 'player_m_kol', 1.670, P.SMILE_M, 'clear')):
    mat, _, _ = ka_look.character_material(mesh, look)
    eyes = ka_look.eye_faces(mesh)
    lensf = ka_look.lens_faces(mesh, eye_z)
    report[look] = dict(eye_faces=ka_look._assign(mesh, eyes, ka_look.eye_material(mat)),
                        lens_faces=ka_look._assign(mesh, lensf, ka_look.clear_lens_material() if lens == 'clear' else ka_look.shades_material(mat)))
    ka_look.smile(mesh, smile)
    if look == 'player_f_kol':
        ka_look.boot_fill(mesh, mat, **P.BOOT_FILL)
print('KEYART materials', report)

ka_pose.pose_female(fa, P.F)
phone, screen = ka_props.make_phone(fa, 'Hand.R', **P.PHONE)
ka_pose.pose_male(ma, P.M)
place_rig(fa, u2b(P.PLACE_F['pos']), P.PLACE_F['yaw'])
place_rig(ma, u2b(P.PLACE_M['pos']), P.PLACE_M['yaw'])
for ob in list(bpy.data.objects):
    if ob.type in ('MESH', 'ARMATURE'):
        move_to(ob, chars)


def ground(arm, mesh):
    """Like the Unity capture: drop/raise the rig so the lowest shoe vertex sits on the paving."""
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    me = ev.to_mesh()
    low = min((ev.matrix_world @ v.co).z for v in me.vertices)
    ev.to_mesh_clear()
    arm.location.z -= low
    upd()
    return round(low, 4)


print('KEYART grounded (gap before, m):', ground(fa, fm), ground(ma, mm))

# ---------------------------------------------------------------- ground (shadow catcher on the plaza paving, y = 0 in Unity)
bpy.ops.mesh.primitive_plane_add(size=60, location=(0, 0, 0))
ground = bpy.context.active_object
ground.name = 'ground_shadow_catcher'
ground.is_shadow_catcher = True
gm = bpy.data.materials.new('paving_bounce')
gm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.62, 0.47, 0.33, 1)
gm.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.8
ground.data.materials.append(gm)
move_to(ground, env)

# ---------------------------------------------------------------- camera: identical to the Unity plate camera
cam = bpy.data.objects.new('camera', bpy.data.cameras.new('camera'))
env.objects.link(cam)
sc.camera = cam
cpos, ctgt = u2b(P.CAMERA['pos']), u2b(P.CAMERA['target'])
cam.location = cpos
cam.rotation_euler = (ctgt - cpos).to_track_quat('-Z', 'Y').to_euler()
cam.data.sensor_fit = 'VERTICAL'
cam.data.sensor_height = 36.0 / (2670 / 1200)        # TitleKeyArtCapture: 36 mm wide sensor cropped to the frame
cam.data.lens = P.CAMERA['mm']
cam.data.clip_start = 0.08

# ---------------------------------------------------------------- world: warm plaza ambience (sky above, paving bounce below)
world = bpy.data.worlds.new('plaza')
sc.world = world
nt = world.node_tree
nt.nodes.clear()
wo = nt.nodes.new('ShaderNodeOutputWorld')
bg = nt.nodes.new('ShaderNodeBackground')
tc = nt.nodes.new('ShaderNodeTexCoord')
sep = nt.nodes.new('ShaderNodeSeparateXYZ')
ramp = nt.nodes.new('ShaderNodeValToRGB')
ramp.color_ramp.elements[0].position = 0.35
ramp.color_ramp.elements[0].color = (0.55, 0.40, 0.27, 1)     # paving / facades
ramp.color_ramp.elements[1].position = 0.75
ramp.color_ramp.elements[1].color = (*P.SKY, 1)     # evening sky
mr = nt.nodes.new('ShaderNodeMapRange')
mr.inputs['From Min'].default_value = -1.0
nt.links.new(tc.outputs['Generated'], sep.inputs[0])
nt.links.new(sep.outputs['Z'], mr.inputs['Value'])
nt.links.new(mr.outputs['Result'], ramp.inputs[0])
nt.links.new(ramp.outputs['Color'], bg.inputs['Color'])
bg.inputs['Strength'].default_value = P.WORLD_STRENGTH
nt.links.new(bg.outputs[0], wo.inputs[0])


# ---------------------------------------------------------------- lights
def area(name, loc, target, color, power, size, size_y=None, only=None, shape='DISK'):
    ld = bpy.data.lights.new(name, 'AREA')
    ld.shape = shape if size_y is None else 'ELLIPSE'
    ld.size = size
    if size_y is not None:
        ld.size_y = size_y
    ld.color = color
    ld.energy = power
    ob = bpy.data.objects.new(name, ld)
    lights.objects.link(ob)
    ob.location = loc
    ob.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    if only is not None:
        ob.light_linking.receiver_collection = only
    return ob


ux, uy, uz = P.SUN['unity_euler']
# Unity forward of Euler(x, y, z) (ZXY order): (sin y cos x, -sin x, cos y cos x)
d_u = Vector((math.sin(math.radians(uy)) * math.cos(math.radians(ux)), -math.sin(math.radians(ux)),
              math.cos(math.radians(uy)) * math.cos(math.radians(ux))))
sun_dir = u2b(d_u)
sd = bpy.data.lights.new('key_sun', 'SUN')
sd.color, sd.energy, sd.angle = P.SUN['color'], P.SUN['strength'], math.radians(P.SUN['angle'])
sun = bpy.data.objects.new('key_sun', sd)
lights.objects.link(sun)
sun.rotation_euler = sun_dir.to_track_quat('-Z', 'Y').to_euler()

fpos, mpos = u2b(P.PLACE_F['pos']), u2b(P.PLACE_M['pos'])
only_chars = bpy.data.collections.new('light_receivers'); only_chars.children.link(chars)
for name, base, off, aim_h, col, pw, sz in P.RIMS:
    b = fpos if base == 'F' else mpos
    area(name, b + Vector(off), b + Vector((0, 0, aim_h)), col, pw, sz, only=only_chars)
fc = P.FILL
bk = P.BEAUTY
area('beauty_key', Vector(bk['loc']), Vector(bk['target']), bk['color'], bk['power'], bk['size'], only=only_chars)
area('fill', Vector(fc['loc']), Vector(fc['target']), fc['color'], fc['power'], fc['size'], only=only_chars)
# contact occlusion: a broad overhead light that only the paving receives. The characters block it,
# so the shadow-catcher pass gets soft, dense contact shadows under the feet (no light on the characters).
ground_only = bpy.data.collections.new('contact_receivers')
ground_only.objects.link(ground)
ct = P.CONTACT
for who, base in (('f', fpos), ('m', mpos)):
    loc = base + Vector((0, 0, ct['height']))
    contact = area(f'contact_{who}', loc, loc - Vector((0, 0, 1)), (1, 1, 1), ct['power'], ct['size'], shape='SQUARE')
    contact.light_linking.receiver_collection = ground_only
# phone screen glow on her face and hand
scr_m = wmat(fa, 'Hand.R')
glow = bpy.data.lights.new('phone_glow', 'AREA')
glow.shape, glow.size, glow.size_y = 'RECTANGLE', 0.07, 0.14
glow.color, glow.energy = (0.55, 0.62, 1.0), P.PHONE_GLOW
glow_ob = bpy.data.objects.new('phone_glow', glow)
lights.objects.link(glow_ob)
glow_ob.matrix_world = screen.matrix_world.copy()
glow_ob.location = screen.matrix_world @ Vector((0, 0, -0.004))
# the area light emits along its -Z, which is the screen's outward side (phone -Z)
glow_ob.light_linking.receiver_collection = only_chars

# ---------------------------------------------------------------- render settings
r = sc.render
r.engine = 'CYCLES'
r.resolution_x, r.resolution_y, r.resolution_percentage = W, H, 100
r.film_transparent = True
r.pixel_aspect_x = r.pixel_aspect_y = 1
cy = sc.cycles
prefs = bpy.context.preferences.addons['cycles'].preferences
try:
    prefs.compute_device_type = 'METAL'
    prefs.get_devices()
    for d in prefs.devices:
        d.use = True
    cy.device = 'GPU'
except Exception as e:  # CPU fallback
    print('KEYART gpu unavailable', e)
cy.samples = SAMPLES
cy.use_adaptive_sampling = True
cy.adaptive_threshold = 0.01
cy.use_denoising = True
cy.denoiser = 'OPENIMAGEDENOISE'
cy.max_bounces, cy.diffuse_bounces, cy.glossy_bounces, cy.transparent_max_bounces = 8, 3, 3, 16
cy.filter_width = 1.2
sc.view_settings.view_transform = 'AgX'
vl = sc.view_layers[0]
vl.cycles.use_pass_shadow_catcher = True
vl.use_pass_ambient_occlusion = False
r.image_settings.media_type = 'MULTI_LAYER_IMAGE'
r.image_settings.file_format = 'OPEN_EXR_MULTILAYER'
r.image_settings.color_depth = '16'
r.image_settings.exr_codec = 'ZIP'
OUTDIR.mkdir(parents=True, exist_ok=True)
r.filepath = str(OUTDIR / ('keyart-B-preview.exr' if PREVIEW else 'keyart-B-passes.exr'))

# review close-ups: same camera position (same perspective), longer lens aimed at one detail
CLOSE = {'mhead': ('kol_m_rig', 'Head', 260), 'fhead': ('kol_f_rig', 'Head', 260), 'phone': ('kol_f_rig', 'Hand.R', 200),
         'mfistL': ('kol_m_rig', 'Hand.L', 300), 'mfistR': ('kol_m_rig', 'Hand.R', 300), 'ffeet': ('kol_f_rig', 'Foot.L', 160),
         'mfeet': ('kol_m_rig', 'Foot.L', 140), 'fhip': ('kol_f_rig', 'Hand.L', 220)}
if '--closeup' in argv:
    rig, bone, lens = CLOSE[opt('--closeup')]
    tgt = wmat(bpy.data.objects[rig], bone).translation
    if bone.startswith('Foot'):
        tgt = tgt + Vector((-0.1 if rig == 'kol_f_rig' else -0.12, 0, 0))
    cam.rotation_euler = (tgt - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam.data.sensor_fit = 'AUTO'
    cam.data.lens = lens
    r.resolution_x = r.resolution_y = 1100
    r.filepath = str(OUTDIR / f'close-{opt("--closeup")}.exr')

blend = opt('--save-blend')
if blend:
    bpy.ops.wm.save_as_mainfile(filepath=blend, compress=True)
    print('KEYART saved', blend)
if '--no-render' not in argv:
    t1 = time.time()
    bpy.ops.render.render(write_still=True)
    print(f'KEYART rendered {r.filepath} {W}x{H} samples={SAMPLES} in {time.time() - t1:.1f}s (build {t1 - t0:.1f}s)')
