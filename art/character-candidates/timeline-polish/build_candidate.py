"""Isolated CT Life Timeline polish study. Never writes client/, navigation, or imported assets.
Blender 5.1: -b --factory-startup -t 2 -P this_file -- [--render]
Renders compare actual sourced geometry at the current game camera, in Blender lighting.
"""
import bpy, json, math, re, sys, hashlib, difflib
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion
from bpy_extras.object_utils import world_to_camera_view
from mathutils.geometry import tessellate_polygon
HERE=Path(__file__).resolve().parent
REPO=HERE.parents[2]
sys.path.insert(0,str(REPO/'art/pipeline'))
import ctl_render as R
MAN=json.loads((REPO/'art/processed/environment/manifest.json').read_text())
RENDER='--render' in sys.argv or '--geometry-only' in sys.argv
if '--geometry-only' in sys.argv:R.render=lambda path:None
OUT=HERE/'renders'; OUT.mkdir(exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)

# Two original, tiny color atlases. These do not copy any third-party image.
def atlas(name,colors):
    img=bpy.data.images.new(name,width=16,height=16,alpha=False)
    pixels=[]
    for y in range(16):
        rgb=colors[min(len(colors)-1,y*len(colors)//16)]
        for x in range(16): pixels.extend((*rgb,1))
    img.pixels=pixels
    img.filepath_raw=str(HERE/(name+'.png')); img.file_format='PNG';img.save()
    return img
stone_img=atlas('timeline_polish_stone',[(.54,.46,.35),(.73,.66,.54),(.86,.80,.68),(.96,.91,.80)])
water_img=atlas('timeline_polish_water',[(.13,.56,.64),(.20,.72,.77),(.42,.87,.87),(.88,.99,.98)])
def material(name,img,rough):
    m=bpy.data.materials.new(name);m.use_nodes=True
    nt=m.node_tree;b=nt.nodes.get('Principled BSDF');b.inputs['Roughness'].default_value=rough
    b.inputs['Specular IOR Level'].default_value=.25
    t=nt.nodes.new('ShaderNodeTexImage');t.image=img;t.interpolation='Closest';nt.links.new(t.outputs['Color'],b.inputs['Base Color'])
    return m
STONE=material('M_timeline_polish_stone',stone_img,.75)
WATER=material('M_timeline_polish_water',water_img,.24)

class Builder:
    def __init__(self,name):self.name=name;self.v=[];self.faces=[];self.mi=[];self.tint=[]
    def face(self,inds,mi,tint):self.faces.append(inds);self.mi.append(mi);self.tint.append(tint)
    def lathe(self,profile,n,mi=0,tints=None):
        start=len(self.v)
        for r,z in profile:
            for i in range(n):
                a=i*math.tau/n;self.v.append((r*math.cos(a),r*math.sin(a),z))
        for j in range(len(profile)-1):
            for i in range(n):
                a=start+j*n+i;b=start+j*n+(i+1)%n;c=b+n;d=a+n
                self.face((a,b,c,d),mi,(tints or [2])[min(j,len(tints or [2])-1)])
    def disc(self,r,z,n,mi=1,tint=1):
        start=len(self.v);self.v.append((0,0,z))
        for i in range(n):self.v.append((r*math.cos(i*math.tau/n),r*math.sin(i*math.tau/n),z))
        for i in range(n):self.face((start,start+1+i,start+1+(i+1)%n),mi,tint)
    def stream(self,angle,profile,widths,sides=4):
        # Closed curved tube. Its parabolic arc, tapered cross-section, aqua underside
        # and intermittent bright crests distinguish water from rigid white struts.
        start=len(self.v);radial=Vector((math.cos(angle),math.sin(angle),0));side=Vector((-math.sin(angle),math.cos(angle),0))
        points=[radial*r+Vector((0,0,z)) for r,z in profile]
        for j,p in enumerate(points):
            tangent=(points[min(j+1,len(points)-1)]-points[max(j-1,0)]).normalized()
            normal=tangent.cross(side).normalized()
            for k in range(sides):
                a=k*math.tau/sides;self.v.append(p+(side*math.cos(a)+normal*math.sin(a))*widths[j])
        for j in range(len(points)-1):
            for k in range(sides):
                self.face((start+j*sides+k,start+j*sides+(k+1)%sides,start+(j+1)*sides+(k+1)%sides,start+(j+1)*sides+k),1,3 if k==0 and j%3!=1 else 2 if k<2 else 1)
    def foam(self,x,y,r,z,n=8):
        start=len(self.v);self.v.append((x,y,z))
        for i in range(n):
            a=i*math.tau/n;rr=r*(1+.27*math.sin(i*2.3))
            self.v.append((x+rr*math.cos(a),y+rr*math.sin(a),z))
        for i in range(n):self.face((start,start+1+i,start+1+(i+1)%n),1,3 if i%3!=1 else 2)
        # Separate small flecks keep foam organic rather than bolt-like rings.
        for dx,dy in [(r*1.25,r*.25),(-r*.5,-r*1.4)]:
            j=len(self.v);self.v.extend([(x+dx-r*.12,y+dy,z),(x+dx+r*.15,y+dy,z),(x+dx,y+dy+r*.18,z)])
            self.face((j,j+1,j+2),1,3)
    def droplet(self,x,y,z,r=.035):
        start=len(self.v);self.v.extend([(x-r,y,z),(x,y-r,z),(x+r,y,z),(x,y+r,z),(x,y,z+r*1.6),(x,y,z-r)])
        for i in range(4):
            self.face((start+i,start+(i+1)%4,start+4),1,3 if i<2 else 2)
            self.face((start+(i+1)%4,start+i,start+5),1,2)
    def finish(self):
        mesh=bpy.data.meshes.new(self.name);mesh.from_pydata(self.v,[],self.faces);mesh.update()
        mesh.materials.append(STONE);mesh.materials.append(WATER)
        uv=mesh.uv_layers.new(name='UVMap')
        for p,mi,tint in zip(mesh.polygons,self.mi,self.tint):
            p.material_index=mi;p.use_smooth=True
            for li in p.loop_indices:uv.data[li].uv=(.5,(tint+.5)/4)
        mesh.calc_loop_triangles()
        o=bpy.data.objects.new(self.name,mesh);bpy.context.collection.objects.link(o)
        return o

def fountain(lod):
    b=Builder('plaza_fountain_polish_LOD'+str(lod))
    n=24 if lod==0 else 16
    # Kenney Fantasy Town Kit 2.0 fountain-round basin: the published radial
    # profile is retained and retopologized from 48 to 24/16 sides, scaled 2.4x.
    # Original ring tuples were measured from source/kenney/fountain-round.obj.
    profile=[(.70,0),(1,0),(1,.056),(.97,.056),(.97,.112),(1,.14),(1,.23),(.925,.28),(.70,.28),(.70,.14)]
    source_vertices=[tuple(map(float,line.split()[1:4])) for line in (HERE/'source/kenney/fountain-round.obj').read_text().splitlines() if line.startswith('v ')]
    source_profile={(round(math.hypot(x,z),3),round(y,3)) for x,y,z in source_vertices}
    assert set(profile).issubset(source_profile), 'Sourced basin profile differs from recorded OBJ'
    b.lathe([(r*2.4,0 if z==0 else z*2.4+.035) for r,z in profile],n,tints=[0,0,1,1,2,2,3,2,1])
    # The wider recessed pool and carved goblet neck have no conical finial.
    b.disc(1.68,.44,n,1,1)
    stem=[(.48,.43),(.52,.53),(.34,.64),(.22,1.04),(.32,1.22)]
    bowl=[(.25,1.12),(.54,1.15),(.82,1.31),(1.02,1.51),(1.06,1.63),(.99,1.72),(.89,1.72),(.73,1.51)]
    if lod==1:stem=[(.48,.43),(.34,.64),(.22,1.06),(.32,1.22)];bowl=[(.25,1.12),(.74,1.26),(1.06,1.62),(.95,1.73),(.73,1.51)]
    b.lathe(stem,12 if lod==0 else 8,tints=[1,3,2,2])
    b.lathe(bowl,20 if lod==0 else 12,tints=[1,1,2,2,3,3,1])
    b.disc(.91,1.66,20 if lod==0 else 12,1,2)
    # Three smaller rising parabolic arcs stay above the upper bowl. The first
    # revision hid most of these behind the bowl and exposed leg-like tails.
    for i in range(3):
        angle=i*math.tau/3+.28
        steps=9 if lod==0 else 5
        path=[];width=[]
        for j in range(steps):
            t=j/(steps-1);path.append((.06+.74*t,1.67+3.05*t*(1-t)));width.append(.027+.010*math.sin(t*math.pi))
        b.stream(angle,path,width,4 if lod==0 else 3)
        if lod==0:
            b.foam(.8*math.cos(angle),.8*math.sin(angle),.09,1.667,7)
            b.droplet(.51*math.cos(angle+.12),.51*math.sin(angle+.12),2.32,.033)
    # Three broad lip overflows visibly curl outward, taper, and break into foam.
    count=3 if lod==0 else 2
    for i in range(count):
        angle=i*math.tau/count+1.0
        b.stream(angle,[(1.00,1.70),(1.18,1.65),(1.33,1.45),(1.41,1.16),(1.46,.80),(1.48,.455)],[.055,.071,.060,.042,.034,.024],3)
        if lod==0:b.foam(1.48*math.cos(angle),1.48*math.sin(angle),.17,.449,7)
        if lod==0:b.droplet(1.52*math.cos(angle+.06),1.52*math.sin(angle+.06),.54,.03)
    return b.finish()
models=[fountain(0),fountain(1)]
costs=[]
for o,limit in zip(models,[1500,600]):
    count=len(o.data.loop_triangles);assert count<=limit,(o.name,count,limit)
    costs.append({'name':o.name,'triangles':count,'material_slots':len(o.data.materials),'vertices':len(o.data.vertices),'dimensions_m':list(o.dimensions)})
    o['candidate_only']=True
bpy.ops.object.select_all(action='DESELECT')
for o in models:o.select_set(True)
bpy.context.view_layer.objects.active=models[0]
bpy.ops.export_scene.fbx(filepath=str(HERE/'plaza_fountain_polish.fbx'),use_selection=True,object_types={'MESH'},add_leaf_bones=False,
    apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',axis_forward='-Z',axis_up='Y',bake_space_transform=True,
    use_mesh_modifiers=True,mesh_smooth_type='FACE',use_triangles=True,path_mode='COPY',embed_textures=False)
models[1].hide_render=True;models[1].hide_viewport=True
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'plaza_fountain_polish.blend'))
(HERE/'fountain-costs.json').write_text(json.dumps({'status':'isolated sourced/adapted candidate, not imported in Unity','lods':costs,
    'dimensions_m':list(models[0].dimensions),'materials':2,'water':'opaque fixed geometry; no runtime flow or particle claim',
    'budget_triangles':[1500,600],'source':'Kenney Fantasy Town Kit 2.0 fountain-round CC0 basin profile; CT Life retopology, upper bowl and water additions'},indent=2))

# Read the current Unity prefab placements. Actual source instances, not the old kit demo layout.
text=(REPO/'client/Assets/Art/Generated/Venues/TimelineSquare.prefab').read_text()
blocks=re.split(r'^--- !u!(\d+) &(-?\d+)\n',text,flags=re.M)
gos={};transforms={};bygo={}
def vec(block,field,keys):
    s=re.search(r'^  '+field+r': \{([^}]+)\}',block,re.M).group(1)
    d={k:float(v) for k,v in re.findall(r'(\w+): ([-+\deE.]+)',s)}
    return tuple(d[k] for k in keys)
for i in range(1,len(blocks),3):
    typ,ident,body=blocks[i:i+3];ident=int(ident)
    if typ=='1':
        m=re.search(r'^  m_Name: (.*)$',body,re.M)
        if m:gos[ident]=m.group(1)
    if typ=='4':
        go=int(re.search(r'm_GameObject: \{fileID: (-?\d+)\}',body).group(1))
        parent=int(re.search(r'm_Father: \{fileID: (-?\d+)\}',body).group(1))
        p=Vector(vec(body,'m_LocalPosition','xyz'));q=Quaternion(vec(body,'m_LocalRotation','wxyz'));s=vec(body,'m_LocalScale','xyz')
        transforms[ident]=(parent,Matrix.Translation(p)@q.to_matrix().to_4x4()@Matrix.Diagonal((*s,1)));bygo[go]=ident
cachemat={}
def world(t):
    if t in cachemat:return cachemat[t]
    p,m=transforms[t];cachemat[t]=(world(p)@m if p in transforms else m);return cachemat[t]
placements=[]
for go,name in gos.items():
    if name in MAN and go in bygo:
        m=world(bygo[go]);p,q,s=m.decompose()
        # Source axes are Blender Z-up; Unity yaw maps to the negative Blender Z angle.
        yaw=math.atan2(2*(q.w*q.y+q.x*q.z),1-2*(q.y*q.y+q.x*q.x))
        placements.append({'id':name,'position':[round(p.x,5),round(p.y,5),round(p.z,5)],'yaw':math.degrees(yaw),'scale':list(s)})
(HERE/'baseline-placements.json').write_text(json.dumps(placements,indent=2))

# Reviewable source diff only. This is not applied to the live generator.
source=REPO/'client/Assets/Scripts/Editor/VenueContentBuilder.cs'
before=source.read_text();after=before
old='bool border = x < -18 || x >= 18 || z < -14 || z >= 14;'
after=after.replace('Add("plaza_fountain", 0f, 7.5f);', 'Add("timeline_plaza_surface", 0f, 0f);\n            Add("plaza_fountain", 0f, 7.5f);')
after=after.replace('new Vector2(-12f, -7f), new Vector2(12f, -7f)', 'new Vector2(-4.8f, -4.6f), new Vector2(4.8f, -4.6f)')
after=after.replace('Add("street_lamp_single", x, z, parent: gardens);\n                    Contact(x, z, 0.6f, 0.6f, 0.3f);',
    'float lampX = z == -11f ? Mathf.Sign(x) * 3.2f : x;\n                    float lampZ = z == -11f ? -10.7f : z;\n                    Add("street_lamp_single", lampX, lampZ, parent: gardens);\n                    Contact(lampX, lampZ, 0.6f, 0.6f, 0.3f);')
after=after.replace('Add("plaza_fountain", 0f, 7.5f);','Add("plaza_fountain_polish", 0f, 7.5f);')
assert before!=after
(HERE/'candidate-placement.patch').write_text(''.join(difflib.unified_diff(before.splitlines(True),after.splitlines(True),
    fromfile='a/client/Assets/Scripts/Editor/VenueContentBuilder.cs',tofile='b/client/Assets/Scripts/Editor/VenueContentBuilder.cs')))
(HERE/'candidate-manifest-entry.json').write_text(json.dumps({'plaza_fountain_polish':{
    'fbx':'environment/timeline_square/plaza_fountain_polish.fbx','tris':[r['triangles'] for r in costs],
    'size_m':list(models[0].dimensions),'materials':['M_timeline_polish_stone','M_timeline_polish_water'],
    'textures':['environment/_shared/textures/timeline_polish_stone.png','environment/_shared/textures/timeline_polish_water.png'],
    'source':'kenney_fantasy_town_2.0+ctlife::timeline-polish-v2','status':'PROPOSAL - paths require explicit root-owned import; do not merge automatically'}},indent=2))
if not RENDER:print('CANDIDATE_ASSETS_READY',costs,flush=True);sys.exit()

# Controlled render comparison; no Unity baking/runtime-performance inference.
bpy.ops.wm.read_factory_settings(use_empty=True)
_templates={};material_fixed=set()
def fix_material(m):
    if m is None or m.name in material_fixed:return
    material_fixed.add(m.name)
    if not m.use_nodes:return
    nt=m.node_tree;b=nt.nodes.get('Principled BSDF')
    if b is None:return
    b.inputs['Roughness'].default_value=.82
    for n in nt.nodes:
        if n.type=='TEX_IMAGE' and n.image:
            name=Path(n.image.filepath).name
            if not Path(bpy.path.abspath(n.image.filepath)).is_file():
                for candidate in [REPO/'art/processed/environment/_shared/textures'/name,REPO/'art/processed/environment/_shared'/name]:
                    if candidate.is_file():n.image.filepath=str(candidate);n.image.reload();break
            if 'palette' in name.lower():n.interpolation='Closest'
            if any(s in name.lower() for s in ['leaves','flowers','grass','bush']):
                nt.links.new(n.outputs['Alpha'],b.inputs['Alpha']);m.surface_render_method='DITHERED'
    if m.name.startswith('KEEP_water'):
        for link in list(b.inputs['Base Color'].links):nt.links.remove(link)
        b.inputs['Base Color'].default_value=(.27,.73,.78,1);b.inputs['Roughness'].default_value=.88
        b.inputs['Specular IOR Level'].default_value=0
    if m.name.startswith('KEEP_ad') or m.name.startswith('KEEP_screen'):
        b.inputs['Base Color'].default_value=(.08,.13,.16,1)

def place(key,x,z,y=0,yaw=0,scale=1):
    if key not in _templates:
        old=set(bpy.data.objects)
        path=HERE/'plaza_fountain_polish.fbx' if key=='plaza_fountain_polish' else REPO/'art/processed'/MAN[key]['fbx']
        bpy.ops.import_scene.fbx(filepath=str(path))
        objs=[o for o in bpy.data.objects if o not in old];keep=[]
        for o in objs:
            if o.type=='MESH' and not re.search(r'_LOD[12](\.\d+)?$',o.name):
                for m in o.data.materials:fix_material(m)
                if key=='plaza_fountain':
                    # Match native RepairFountainSurface rather than show an unfair
                    # coplanar source defect that the current Unity generator repaired.
                    moved=set()
                    for face in o.data.polygons:
                        if o.data.materials[face.material_index].name.startswith('KEEP_water'):moved.update(face.vertices)
                    delta=o.matrix_world.inverted().to_3x3()@Vector((0,0,.04))
                    for vi in moved:o.data.vertices[vi].co+=delta
                keep.append(o)
            else:bpy.data.objects.remove(o,do_unlink=True)
        for o in keep:o.hide_render=True;o.hide_viewport=True
        _templates[key]=keep
    result=[]
    for src in _templates[key]:
        o=src.copy();bpy.context.collection.objects.link(o);o.hide_render=False;o.hide_viewport=False
        o.matrix_world=Matrix.Translation((x,z,y))@Matrix.Rotation(math.radians(-yaw),4,'Z')@Matrix.Diagonal((scale,scale,scale,1))@src.matrix_world
        result.append(o)
    return result

def plaza_surface():
    half=2.;radius=5.3;center=7.5;angle=math.acos(half/radius)
    pts=[(-half,-16),(half,-16),(half,-2),(20,-2),(20,2),(half,2)]
    for i in range(17):
        a=-angle+2*angle*i/16;pts.append((radius*math.cos(a),center+radius*math.sin(a)))
    pts.extend([(half,21),(-half,21)])
    for i in range(17):
        a=math.pi-angle+2*angle*i/16;pts.append((radius*math.cos(a),center+radius*math.sin(a)))
    pts.extend([(-half,2),(-20,2),(-20,-2),(-half,-2)])
    vectors=[Vector((x,z,.008)) for x,z in pts]
    index={(round(v.x,7),round(v.y,7)):i for i,v in enumerate(vectors)}
    faces=tessellate_polygon([vectors])  # Blender 5.1 returns index tuples (verified local __doc__).
    faces=[tri for tri in faces if (vectors[tri[1]]-vectors[tri[0]]).cross(vectors[tri[2]]-vectors[tri[0]]).length>1e-7]
    mesh=bpy.data.meshes.new('timeline_plaza_surface');mesh.from_pydata(vectors,[],faces);mesh.update()
    # Current hex paving material, same texture and two-meter UV repeat.
    source=_templates['plaza_hexpaving_2m'][0]
    mat=next(m for m in source.data.materials if 'hexagonal' in m.name)
    mesh.materials.append(mat);uv=mesh.uv_layers.new(name='UVMap')
    for poly in mesh.polygons:
        for li in poly.loop_indices:
            v=mesh.vertices[mesh.loops[li].vertex_index].co;uv.data[li].uv=(v.x/2,v.y/2)
    o=bpy.data.objects.new('timeline_plaza_surface',mesh);bpy.context.collection.objects.link(o)
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    bpy.ops.export_scene.fbx(filepath=str(HERE/'timeline_plaza_surface.fbx'),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_space_transform=True,use_triangles=True,path_mode='COPY')
    mesh.calc_loop_triangles()
    (HERE/'plaza-surface-costs.json').write_text(json.dumps({'triangles':len(mesh.loop_triangles),'material_slots':1,'height_above_ground_m':.008,'texture':'existing hexagonal_concrete_paving_diff_1k.png, CC0 Poly Haven','physical_uv_repeat_m':2,'connections':['south sidewalk z=-16','north approach z=21','west side x=-20','east side x=20'],'no_collider':True,'status':'isolated surface overlay; native z-fighting/navigation not tested'},indent=2))
    manifest=json.loads((HERE/'candidate-manifest-entry.json').read_text())
    manifest['timeline_plaza_surface']={'fbx':'environment/timeline_square/timeline_plaza_surface.fbx','tris':[len(mesh.loop_triangles)],'size_m':[40,37,0], 'materials':[mat.name],'textures':['environment/_shared/textures/hexagonal_concrete_paving_diff_1k.png'],'source':'ctlife_original_planar_layout+existing_PolyHaven_CC0_texture','status':'PROPOSAL - isolated planar visual mesh only, no collider/nav source; Repeat texture wrapping required'}
    (HERE/'candidate-manifest-entry.json').write_text(json.dumps(manifest,indent=2))
    return o

sc=R.setup_scene(res=(720,1602),samples=32,strength=.70,bg_top=(.64,.75,.86),bg_bottom=(.71,.68,.61))
# Controlled golden-hour source study: lighting is identical for both, not the Unity lightmap.
R.add_sun((48,0,-32),energy=1.5,color=(1,.88,.73),angle_deg=12)
sc.render.threads_mode='FIXED';sc.render.threads=2
sc.view_settings.view_transform='AgX';sc.view_settings.look='AgX - Medium High Contrast';sc.view_settings.exposure=.3
scene_objs=[]
for row in placements:
    x,y,z=row['position'];scene_objs+=place(row['id'],x,z,y,row['yaw'],row['scale'][0])
# Paving was combined into generated native meshes; reconstruct its exact generator loops.
for x in range(-22,22,2):
    for z in range(-16,22,2):
        border=x < -18 or x>=18 or z< -14 or z>=14
        key='plaza_hexpaving_2m' if border else 'plaza_paving_2m'
        obs=place(key,x+1,z+1,-.101)
        for o in obs:o['floor_origin']=[x,z];o['floor_key']=key
        scene_objs+=obs
for x in range(-42,43,4):
    for z in range(-14,73,4):
        if z<22 and x> -22 and x<22:continue
        scene_objs+=place('floor_concrete_4m',x,z,-.105)
for x in range(-24,25,12):scene_objs+=place('street_2lane',x,-22,-.15,90)
for x in range(-21,22,3):scene_objs+=place('sidewalk_straight_3m',x,-17.5,-.15,90)
for x in range(-36,37,12):scene_objs+=place('street_2lane',x,35,-.145,90)
for x in range(-36,37,3):scene_objs+=place('sidewalk_straight_3m',x,30.5,-.145,90)

# Actual supplied relaxed Snow pose, appended read-only, used only as scale reference.
blend=REPO/'art/character-candidates/unity-snow/animations/SnowAdult_RelaxedIdle.blend'
with bpy.data.libraries.load(str(blend),link=False) as (src,dst):dst.objects=src.objects
actors=[]
for o in dst.objects:
    if o is not None:
        if o.type in {'MESH','ARMATURE'}:bpy.context.collection.objects.link(o);actors.append(o)
# Delete unrelated cameras/lights/empties imported only into data; no source file write.
for o in actors:
    if o.parent is None:o.rotation_euler.z+=math.pi;o.location+=Vector((0,-14,0))
    if o.type=='MESH':
        for m in o.data.materials:fix_material(m)
sc.frame_set(1)

pitch=math.radians(35);yaw=math.radians(-8);dist=18.5;fov=math.radians(42);height=1.;targetY=.39
q=(2*targetY-1)*math.tan(fov/2)
ahead=-(q*dist+height*(math.cos(pitch)+q*math.sin(pitch)))/(math.sin(pitch)-q*math.cos(pitch))
f=Vector((math.sin(yaw)*ahead,-14+math.cos(yaw)*ahead,height))
forward=Vector((math.sin(yaw)*math.cos(pitch),math.cos(yaw)*math.cos(pitch),-math.sin(pitch)))
cam=R.add_camera(f-forward*dist,f,lens=24/(2*math.tan(fov/2)))
cam.data.sensor_fit='VERTICAL';cam.data.sensor_height=24;cam.data.clip_end=170
bpy.context.view_layer.update()
foot=world_to_camera_view(sc,cam,Vector((0,-14,0)))
assert abs(foot.x-.5)<.0001 and abs(foot.y-.39)<.0001,tuple(foot)
(HERE/'camera-proof.json').write_text(json.dumps({'unity_parameters':{'pitch':35,'yaw':-8,'distance':18.5,'vertical_fov':42,'target_viewport_y':.39,'focus_height':1,'target':[0,0,-14]},'blender_position':list(cam.location),'blender_aim':list(f),'projected_foot':list(foot),'render_resolution':[720,1602],'lighting':'controlled Blender source study, not Unity baked lighting'},indent=2))
R.render(str(OUT/'entry-before.png'))

# Only candidate placement and paving changes; preserve original transforms for evidence.
changes=[]
for o in scene_objs:
    p=o.location.copy()
    # Source root copies keep names and their transformed origins.
    origin=p
    for old,new in [((-12,-7),(-4.8,-4.6)),((12,-7),(4.8,-4.6))]:
        dx,dz=new[0]-old[0],new[1]-old[1]
        selected=False
        name=o.name
        if name.startswith(('planter_round','tree_blossom')) and abs(origin.x-old[0])<.02 and abs(origin.y-old[1])<.02:selected=True
        if name.startswith('bush_large_flowers') and abs(origin.x-old[0]-1.7)<.02 and abs(origin.y-old[1]-.2)<.02:selected=True
        if name.startswith('flower_clump_2') and abs(origin.x-old[0]+.8)<.02 and abs(origin.y-old[1]+.72)<.02:selected=True
        if selected:o.location.x+=dx;o.location.y+=dz;changes.append({'asset':name,'before':list(p),'after':list(o.location)})
    if o.name.startswith('street_lamp_single') and abs(origin.y+11)<.02 and abs(abs(origin.x)-17)<.02:
        o.location.x=math.copysign(3.2,origin.x);o.location.y=-10.7;changes.append({'asset':o.name,'before':list(p),'after':list(o.location)})
    if o.name.startswith('plaza_fountain_'):o.hide_render=True
place('plaza_fountain_polish',0,7.5)
plaza_surface()
(HERE/'applied-study-transforms.json').write_text(json.dumps(changes,indent=2))
R.render(str(OUT/'entry-after.png'))
# Center inspection uses the same lens and viewing angle, now following the actual center anchor.
cam.location.y+=14;R.look_at(cam,f+Vector((0,14,0)))
for o in actors:
    if o.parent is None:o.location.y+=14
R.render(str(OUT/'center-after.png'))

# Isolate both fountain source candidates with the same controlled lighting and camera.
for o in bpy.data.objects:
    if o.type in {'MESH','ARMATURE'}:o.hide_render=True
candidate=place('plaza_fountain_polish',0,0)
sc.render.resolution_x=1200;sc.render.resolution_y=1000
cam.data.sensor_fit='AUTO';cam.data.lens=50;cam.location=(6,-8,6);R.look_at(cam,(0,0,1))
R.add_ground(size=50,color=(.32,.38,.38),z=-.006)
R.render(str(OUT/'fountain-after.png'))
for o in candidate:o.hide_render=True
before_fountain=place('plaza_fountain',0,0)
R.render(str(OUT/'fountain-before.png'))
print('POLISH_STUDY_DONE',json.dumps(costs),flush=True)
