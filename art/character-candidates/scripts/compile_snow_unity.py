"""Compile ONE detailed adult Snow into a grounded, skinned mesh and real Unity atlas.
CPU Cycles baking only. Raw sources and prior proofs are never overwritten.
"""
import bpy,bmesh,json,math,sys,time,argparse
import numpy as np
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'scripts'));import snow_hand_poses as poses
parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path,default=root/'unity-snow');args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
out=args.output.resolve();exports=out/'exports';exports.mkdir(parents=True,exist_ok=True)
from snow_atlas_validation import validate_atlas
bpy.ops.wm.open_mainfile(filepath=str(root/'snow-hand-quality/snow_adult_candidate.blend'),use_scripts=False)
scene=bpy.context.scene;rig=bpy.data.objects['SnowAdult_Rig'];poses.reset(rig);bpy.context.view_layer.update()
scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1.0
source_parts=[o for o in bpy.data.objects if o.type=='MESH' and o.name.startswith('Adult_REF_')];source_parts.sort(key=lambda o:o.name)
parts=[];records=[];material_copies={}
def clone_tree(tree):
 cp=tree.copy()
 for n in cp.nodes:
  if n.type=='GROUP' and n.node_tree:n.node_tree=clone_tree(n.node_tree)
 return cp

def prepare_tree(tree):
 # All source texture reads keep the authored UVs while AtlasUV becomes the bake target.
 uv=tree.nodes.new('ShaderNodeUVMap');uv.uv_map='SourceUV';uv.name='Source UV for atlas bake'
 for n in list(tree.nodes):
  if n.type=='TEX_IMAGE' and n.image and not n.inputs['Vector'].is_linked:tree.links.new(uv.outputs['UV'],n.inputs['Vector'])
  if n.type in ['NORMAL_MAP','UVMAP'] and getattr(n,'uv_map','') in ['', 'UVMap']:n.uv_map='SourceUV'
  if n.type=='GROUP' and n.node_tree:prepare_tree(n.node_tree)
 outputs=[n for n in tree.nodes if n.type=='OUTPUT_MATERIAL']
 if outputs:
  chosen=next((n for n in outputs if n.target=='CYCLES'),None) or next((n for n in outputs if n.target=='ALL'),outputs[0])
  for n in outputs:
   if n!=chosen:tree.nodes.remove(n)
  chosen.target='ALL';chosen.is_active_output=True

def material_copy(m):
 if m.name in material_copies:return material_copies[m.name]
 cp=m.copy();cp.name='BAKE_'+m.name
 for n in cp.node_tree.nodes:
  if n.type=='GROUP' and n.node_tree:n.node_tree=clone_tree(n.node_tree)
 prepare_tree(cp.node_tree);material_copies[m.name]=cp;return cp

for i,src in enumerate(source_parts):
 ob=src.copy();ob.data=src.data.copy();ob.name='BUILD_'+src.name.removeprefix('Adult_REF_');scene.collection.objects.link(ob);ob.hide_viewport=False;ob.hide_render=False;ob.hide_set(False)
 # Preserve the source's explicit UV layout (including UDIM coordinates).
 primary=next(x for x in ob.data.uv_layers if x.active_render);primary.name='SourceUV'
 mats=[material_copy(s.material) for s in src.material_slots];indices=[p.material_index for p in ob.data.polygons];ob.data.materials.clear()
 for m in mats:ob.data.materials.append(m)
 for p,idx in zip(ob.data.polygons,indices):p.material_index=idx
 bm=bmesh.new();bm.from_mesh(ob.data)
 hidden_faces=sum(f.hide for f in bm.faces);hidden_vertices=sum(v.hide for v in bm.verts)
 # Edit-hidden faces still render/export. Include every copied face in UV packing.
 for elem in list(bm.verts)+list(bm.edges)+list(bm.faces):elem.hide_set(False)
 loose=[v for v in bm.verts if not v.link_faces]
 if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
 bm.to_mesh(ob.data);bm.free();ob.data.validate(clean_customdata=False);ob.data.update();ob.data.calc_loop_triangles()
 a=ob.data.attributes.new(name='source_part_id',type='INT',domain='POINT');a.data.foreach_set('value',[i]*len(ob.data.vertices))
 records.append({'part':src.name.removeprefix('Adult_REF_'),'part_id':i,'triangles':len(ob.data.loop_triangles),'vertices':len(ob.data.vertices),'cleared_edit_hidden_faces':hidden_faces,'cleared_edit_hidden_vertices':hidden_vertices})
 parts.append(ob)
bpy.ops.object.select_all(action='DESELECT')
for o in parts:o.select_set(True)
bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();mesh=bpy.context.object;mesh.name='SnowAdult_CreatorMesh';mesh.data.name='SnowAdult_CreatorGeometry'
# Original Blender animation controls and modular preview meshes do not enter this file.
for ob in list(bpy.data.objects):
 if ob not in [mesh,rig]:bpy.data.objects.remove(ob,do_unlink=True)
for mod in list(mesh.modifiers):
 if mod.type!='ARMATURE':mesh.modifiers.remove(mod)
mesh.parent=rig;mesh.matrix_parent_inverse.identity();mesh.matrix_world.identity()
# Feet sit exactly at local ground, with identity object transforms and an origin Root bone.
offset=-min(v.co.z for v in mesh.data.vertices)
for v in mesh.data.vertices:v.co.z+=offset
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig;bpy.ops.object.mode_set(mode='EDIT')
for b in rig.data.edit_bones:
 if b.name!='Root':b.head.z+=offset;b.tail.z+=offset
bpy.ops.object.mode_set(mode='OBJECT');poses.reset(rig)
# Keep the authored UV islands, give faces/eyes/hands deliberate density, and repack uniquely.
source_uv=mesh.data.uv_layers['SourceUV'];atlas=mesh.data.uv_layers.new(name='AtlasUV');atlas.data.foreach_set('uv',[x for u in source_uv.data for x in u.uv]);mesh.data.uv_layers.active=atlas;atlas.active_render=True
bpy.ops.object.select_all(action='DESELECT');mesh.select_set(True);bpy.context.view_layer.objects.active=mesh
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.reveal(select=True);bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.select_all(action='SELECT');bpy.ops.uv.average_islands_scale();bpy.ops.object.mode_set(mode='OBJECT')
atlas=mesh.data.uv_layers['AtlasUV']
ids=mesh.data.attributes['source_part_id'].data;names={r['part_id']:r['part'] for r in records};density={'head':1.45,'eyes':1.8,'body':1.25,'eyebrows':1.2}
for loop in mesh.data.loops:atlas.data[loop.index].uv*=density.get(names[ids[loop.vertex_index].value],1.0)*.2
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.select_all(action='SELECT');bpy.ops.uv.pack_islands(udim_source='CLOSEST_UDIM',rotate=True,rotate_method='CARDINAL',scale=True,merge_overlap=False,margin_method='FRACTION',margin=16/2048,shape_method='CONCAVE');bpy.ops.object.mode_set(mode='OBJECT')
atlas=mesh.data.uv_layers['AtlasUV']
uvmin=[min(u.uv[i] for u in atlas.data) for i in range(2)];uvmax=[max(u.uv[i] for u in atlas.data) for i in range(2)]
assert min(uvmin)>-1e-5 and max(uvmax)<1.00001,(uvmin,uvmax)
# One authored hair UV corner folds across adjacent triangles. Relax only its target
# atlas coordinate to the mean of its three UV neighbours; source texture UVs stay intact.
ids=mesh.data.attributes['source_part_id'].data
source_uv=mesh.data.uv_layers['SourceUV'];atlas=mesh.data.uv_layers['AtlasUV'];hair_vids={l.vertex_index for l in mesh.data.loops if ids[l.vertex_index].value==3 and (source_uv.data[l.index].uv-Vector((.9360229969024658,.48456236720085144))).length<1e-7}
assert len(hair_vids)==1,hair_vids
hair_vid=next(iter(hair_vids));linked=[];neighbours=set()
for polygon in mesh.data.polygons:
 loops=list(polygon.loop_indices)
 for i,li in enumerate(loops):
  if mesh.data.loops[li].vertex_index==hair_vid:
   linked.append(li);neighbours.update([tuple(atlas.data[loops[(i-1)%len(loops)]].uv),tuple(atlas.data[loops[(i+1)%len(loops)]].uv)])
old_hair_uv=atlas.data[linked[0]].uv.copy();new_hair_uv=Vector(tuple(np.array(list(neighbours)).mean(0)))
for li in linked:atlas.data[li].uv=new_hair_uv
hair_adjustment={'source_part':'hair_base','vertex_index':hair_vid,'affected_uv_corners':len(linked),'old_atlas_uv':list(old_hair_uv),'new_atlas_uv':list(new_hair_uv),'distance_at_2048_px':(new_hair_uv-old_hair_uv).length*2048,'source_uv_unchanged':True,'geometry_unchanged':True,'reason':'Folded target UV corner across adjacent hair triangles; local target-only Laplacian relaxation.'}
(out/'atlas-target-adjustment.json').write_text(json.dumps(hair_adjustment,indent=2))
atlas_report=validate_atlas(mesh.data);(out/'atlas-uv-validation.json').write_text(json.dumps(atlas_report,indent=2));print('ATLAS_VALIDATION',json.dumps(atlas_report),flush=True)
if atlas_report['overlapping_interior_samples']:bpy.ops.wm.save_as_mainfile(filepath=str(out/'atlas-debug.blend'))
assert atlas_report['overlapping_interior_samples']==0,atlas_report
assert atlas_report['hidden_faces']==0 and atlas_report['hidden_vertices']==0,atlas_report
scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=16;scene.render.threads_mode='FIXED';scene.render.threads=4
scene.render.bake.margin=8;scene.render.bake.use_clear=True;scene.render.bake.use_selected_to_active=False;scene.render.bake.normal_space='TANGENT';scene.render.bake.normal_r='POS_X';scene.render.bake.normal_g='POS_Y';scene.render.bake.normal_b='POS_Z'
base_mats=list(mesh.data.materials)
def image(name,space):
 im=bpy.data.images.new(name,width=2048,height=2048,alpha=True,float_buffer=True);im.colorspace_settings.name=space;im.generated_color=(0,0,0,0);return im
base=image('SnowAdult_BaseColor','sRGB');normal=image('SnowAdult_Normal','Non-Color');raw=image('BAKE_MetalRough','Non-Color')
def convert_shader(tree,kind):
 for n in list(tree.nodes):
  if n.type=='GROUP' and n.node_tree:convert_shader(n.node_tree,kind)
  if n.type!='BSDF_PRINCIPLED':continue
  em=tree.nodes.new('ShaderNodeEmission');em.inputs['Strength'].default_value=1
  if kind=='base':
   inp=n.inputs['Base Color']
   if inp.is_linked:tree.links.new(inp.links[0].from_socket,em.inputs['Color'])
   else:em.inputs['Color'].default_value=inp.default_value
  else:
   combine=tree.nodes.new('ShaderNodeCombineXYZ')
   for dst,src in [('X','Metallic'),('Y','Roughness')]:
    inp=n.inputs[src]
    if inp.is_linked:tree.links.new(inp.links[0].from_socket,combine.inputs[dst])
    else:combine.inputs[dst].default_value=inp.default_value
   combine.inputs['Z'].default_value=0;tree.links.new(combine.outputs['Vector'],em.inputs['Color'])
  for link in list(n.outputs['BSDF'].links):tree.links.new(em.outputs[0],link.to_socket)

def bake(kind,im):
 mats=[]
 for original in base_mats:
  cp=original.copy();cp.name=f'{kind}_{original.name}'
  for n in cp.node_tree.nodes:
   if n.type=='GROUP' and n.node_tree:n.node_tree=clone_tree(n.node_tree)
  if kind!='normal':convert_shader(cp.node_tree,kind)
  for n in cp.node_tree.nodes:n.select=False
  target=cp.node_tree.nodes.new('ShaderNodeTexImage');target.image=im;target.select=True;cp.node_tree.nodes.active=target;mats.append(cp)
 for i,m in enumerate(mats):mesh.data.materials[i]=m
 print('BAKE_START',kind,flush=True);start=time.time();bpy.ops.object.bake(type='NORMAL' if kind=='normal' else 'EMIT',use_clear=True,margin=8,uv_layer='AtlasUV');print('BAKE_FINISH',kind,round(time.time()-start,2),flush=True)
 for i,m in enumerate(base_mats):mesh.data.materials[i]=m

def save(im,name,depth='8'):
 scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA';scene.render.image_settings.color_depth=depth
 im.filepath_raw=str(exports/name);im.file_format='PNG';im.save();return str(im.filepath_raw)
bake('base',base);save(base,'SnowAdult_BaseColor.png')
bake('normal',normal);save(normal,'SnowAdult_Normal.png','16')
bake('metalrough',raw)
a=np.empty(2048*2048*4,dtype=np.float32);raw.pixels.foreach_get(a);a=a.reshape((-1,4));a[:,3]=1-np.clip(a[:,1],0,1);a[:,0]=np.clip(a[:,0],0,1);a[:,1]=1;a[:,2]=0
packed=image('SnowAdult_MetallicSmoothness','Non-Color');packed.pixels.foreach_set(a.ravel());packed.update();save(packed,'SnowAdult_MetallicSmoothness.png')
# A single ordinary PBR material now references only local atlas files.
material=bpy.data.materials.new('SnowAdult_Atlas');material.use_nodes=True;nodes=material.node_tree.nodes;links=material.node_tree.links;bsdf=nodes.get('Principled BSDF');bsdf.inputs['Subsurface Weight'].default_value=0
uv=nodes.new('ShaderNodeUVMap');uv.uv_map='AtlasUV'
textures={}
for label,im in [('BaseColor',base),('Normal',normal),('MetallicSmoothness',packed)]:
 tex=nodes.new('ShaderNodeTexImage');tex.name=label;tex.image=im;links.new(uv.outputs['UV'],tex.inputs['Vector']);textures[label]=tex
links.new(textures['BaseColor'].outputs['Color'],bsdf.inputs['Base Color'])
nm=nodes.new('ShaderNodeNormalMap');nm.uv_map='AtlasUV';nm.inputs['Strength'].default_value=1;links.new(textures['Normal'].outputs['Color'],nm.inputs['Color']);links.new(nm.outputs['Normal'],bsdf.inputs['Normal'])
split=nodes.new('ShaderNodeSeparateColor');split.mode='RGB';links.new(textures['MetallicSmoothness'].outputs['Color'],split.inputs['Color']);links.new(split.outputs['Red'],bsdf.inputs['Metallic'])
invert=nodes.new('ShaderNodeMath');invert.operation='SUBTRACT';invert.inputs[0].default_value=1;links.new(textures['MetallicSmoothness'].outputs['Alpha'],invert.inputs[1]);links.new(invert.outputs[0],bsdf.inputs['Roughness'])
mesh.data.materials.clear();mesh.data.materials.append(material)
for p in mesh.data.polygons:p.material_index=0
# Atlas is UV0 in the exported mesh, with no dependency on source UDIM layouts.
for layer in list(mesh.data.uv_layers):
 if layer.name!='AtlasUV':mesh.data.uv_layers.remove(layer)
mesh.data.uv_layers.active=mesh.data.uv_layers['AtlasUV'];mesh.data.uv_layers['AtlasUV'].active_render=True
mesh.data.calc_loop_triangles();bone_names={b.name for b in rig.data.bones}
for v in mesh.data.vertices:
 ws=[g for g in v.groups if mesh.vertex_groups[g.group].name in bone_names and g.weight>1e-5];assert 1<=len(ws)<=4;assert abs(sum(g.weight for g in ws)-1)<1e-5
bounds=[[min(v.co[i] for v in mesh.data.vertices),max(v.co[i] for v in mesh.data.vertices)] for i in range(3)]
manifest={'status':'Compiled candidate; Blender export validation and visual review pending. Unity/device acceptance owned by coordinating agent.','source':'Blender Studio Snow CC-BY4.0, adapted adult proportions. Raw source unchanged.','source_page':'https://studio.blender.org/characters/snow/v4/','attribution':'Snow Rig (CC) Blender Foundation | studio.blender.org','files':{'fbx':'exports/SnowAdult_Creator.fbx','base_color':'exports/SnowAdult_BaseColor.png','normal':'exports/SnowAdult_Normal.png','metallic_smoothness':'exports/SnowAdult_MetallicSmoothness.png'},'geometry':{'mesh':mesh.name,'triangles':len(mesh.data.loop_triangles),'vertices':len(mesh.data.vertices),'skinned_mesh_count':1,'material_slots':1,'bones':len(rig.data.bones),'max_influences':max(sum(g.weight>1e-5 and mesh.vertex_groups[g.group].name in bone_names for g in v.groups) for v in mesh.data.vertices),'bounds_blender_metres':bounds,'height_metres':bounds[2][1]-bounds[2][0],'ground_offset_applied_m':offset,'sole_blender_z_m':bounds[2][0],'object_scale':[1,1,1]},'textures':{'size':[2048,2048],'base_color_space':'sRGB','normal_space':'Tangent/OpenGL +Y, import as Unity Normal Map','metallic_smoothness_space':'Linear; R=Metallic, A=1-Roughness, G=1, B=0','atlas_padding_px':8,'island_gap_target_px':16,'uv_layer':'AtlasUV / exported UV0'},'unity_material':{'shader':'Universal Render Pipeline/Lit','name':'SnowAdult_Atlas','base_tint':[1,1,1,1],'normal_scale':1,'metallic_map_multiplier':1,'smoothness_multiplier':1,'surface':'Opaque'},'unity_import':{'avatar_setup':'CreateFromThisModel','mapping_file':'unity-humanoid-map.json','axis_forward':'-Z','axis_up':'Y','global_scale':1,'expected_sole_y_m':0,'animation_clips':[]},'source_parts':records,'atlas_validation':'atlas-uv-validation.json','limitations':['No production facial-expression or garment-customization system.','Jaw is an inactive placeholder.','One high-quality near-avatar export; crowd LOD/device budget not accepted.','Source skin subsurface and hair anisotropy are reduced to ordinary atlas PBR.']}
(out/'manifest.json').write_text(json.dumps(manifest,indent=2))
bpy.ops.object.select_all(action='DESELECT');mesh.select_set(True);rig.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.wm.save_as_mainfile(filepath=str(out/'SnowAdult_Unity.blend'))
bpy.ops.export_scene.fbx(filepath=str(exports/'SnowAdult_Creator.fbx'),use_selection=True,object_types={'MESH','ARMATURE'},add_leaf_bones=False,use_armature_deform_only=True,bake_anim=False,axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',use_mesh_modifiers=True,path_mode='STRIP')
print('UNITY_SNOW_COMPILED',json.dumps(manifest['geometry']),flush=True)
