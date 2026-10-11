"""Bake one real Vincent character to an opaque atlas, retaining five facial morphs.
CPU Cycles only. No raw/canonical Rain/Snow writes or Unity invocation.
"""
import bpy,json,sys,math,time,hashlib,numpy as np
from pathlib import Path
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'scripts'))
from snow_atlas_validation import validate_atlas
from png_data_texture import write_rgba16
out=root/'vincent-adult-production';ex=out/'exports';ex.mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(out/'VincentAdult_SourceQuality.blend'),use_scripts=False)
scene=bpy.context.scene;rig=bpy.data.objects['VincentAdult_Rig'];scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
for b in rig.pose.bones:b.matrix_basis.identity()
parts=sorted([o for o in bpy.data.objects if o.type=='MESH' and o.name.startswith('Vincent_REF_')],key=lambda o:o.name)
for o in parts:
 if o.data.shape_keys:
  for k in o.data.shape_keys.key_blocks:k.value=0
material_copies={};conversion=[]
def clone_tree(tree):
 cp=tree.copy()
 for n in cp.nodes:
  if n.type=='GROUP' and n.node_tree:n.node_tree=clone_tree(n.node_tree)
 return cp

def modern_material(m):
 if m.name in material_copies:return material_copies[m.name]
 cp=m.copy();cp.name='BAKE_'+m.name;t=cp.node_tree;nodes=t.nodes;links=t.links
 for n in nodes:
  if n.type=='GROUP' and n.node_tree:n.node_tree=clone_tree(n.node_tree)
 uv=nodes.new('ShaderNodeUVMap');uv.uv_map='SourceUV'
 generated=nodes.new('ShaderNodeAttribute');generated.attribute_name='source_generated'
 objcoord=nodes.new('ShaderNodeAttribute');objcoord.attribute_name='source_object'
 for n in list(nodes):
  if n.type=='TEX_COORD':
   for output,replacement in [('UV',uv.outputs['UV']),('Generated',generated.outputs['Vector']),('Object',objcoord.outputs['Vector'])]:
    for l in list(n.outputs[output].links):links.new(replacement,l.to_socket)
  if n.type=='TEX_IMAGE' and n.image and not n.inputs['Vector'].is_linked:links.new(uv.outputs['UV'],n.inputs['Vector'])
  if n.type=='NORMAL_MAP':n.uv_map='SourceUV'
 diffuse=next((n for n in nodes if n.type=='BSDF_DIFFUSE'),None)
 assert diffuse is not None,('Missing original diffuse shader',m.name)
 glossy=nodes.get('Glossy BSDF');bs=nodes.new('ShaderNodeBsdfPrincipled');bs.name='Portable source surface'
 source=diffuse.inputs['Color'];notes=[]
 if m.name=='vincent_glasses':
  # Original diffuse Facing ramp varies with the camera; keep the authored dark frame pigment.
  bs.inputs['Base Color'].default_value=source.default_value;notes.append('Use original diffuse dark pigment; original view-dependent Facing ramp excluded from baked albedo.')
 elif source.is_linked:links.new(source.links[0].from_socket,bs.inputs['Base Color'])
 else:bs.inputs['Base Color'].default_value=source.default_value
 bs.inputs['Metallic'].default_value=1 if m.name=='vincent_belt_buckle' else 0
 rough=glossy.inputs['Roughness'] if glossy else None
 if rough and rough.is_linked:links.new(rough.links[0].from_socket,bs.inputs['Roughness'])
 else:bs.inputs['Roughness'].default_value=float(rough.default_value) if rough else .75
 if m.name in ['vincent_shirt','vincent_shirt.001','vincent_pants','vincent_socks','vincent_skin']:
  floor=nodes.new('ShaderNodeMath');floor.operation='MAXIMUM';floor.inputs[1].default_value=.60 if m.name=='vincent_skin' else .65
  inp=bs.inputs['Roughness']
  if inp.is_linked:links.new(inp.links[0].from_socket,floor.inputs[0])
  else:floor.inputs[0].default_value=inp.default_value
  links.new(floor.outputs[0],inp)
 if diffuse.inputs['Normal'].is_linked:links.new(diffuse.inputs['Normal'].links[0].from_socket,bs.inputs['Normal'])
 outputs=[n for n in nodes if n.type=='OUTPUT_MATERIAL'];chosen=next((n for n in outputs if n.target=='CYCLES'),None) or next((n for n in outputs if n.target=='ALL'),outputs[0])
 for n in outputs:
  if n!=chosen:nodes.remove(n)
 chosen.target='ALL';chosen.is_active_output=True;links.new(bs.outputs['BSDF'],chosen.inputs['Surface'])
 conversion.append({'source_material':m.name,'albedo':'Original diffuse shader colour/texture/procedural network','metallic':bs.inputs['Metallic'].default_value,'roughness':'Original primary glossy roughness, cloth/skin floor where specified','notes':notes});material_copies[m.name]=cp;return cp
records=[]
for i,o in enumerate(parts):
 part=o.name.removeprefix('Vincent_REF_');me=o.data;o.animation_data_clear();o.hide_render=False;o.hide_set(False)
 for v in me.vertices:v.hide=False
 for p in me.polygons:p.hide=False
 if not me.uv_layers:me.uv_layers.new(name='SourceUV')
 primary=next((u for u in me.uv_layers if u.active_render),me.uv_layers[0]);primary.name='SourceUV';atlas=me.uv_layers.new(name='AtlasUV');atlas.data.foreach_set('uv',[x for u in primary.data for x in u.uv]);me.uv_layers.active=atlas;atlas.active_render=True
 used=sorted({p.material_index for p in me.polygons});mats=[modern_material(o.material_slots[i].material) for i in used];slotmap={old:new for new,old in enumerate(used)};inds=[slotmap[p.material_index] for p in me.polygons];me.materials.clear()
 for m in mats:me.materials.append(m)
 for p,idx in zip(me.polygons,inds):p.material_index=idx
 attr=me.attributes.new(name='source_part_id',type='INT',domain='POINT');attr.data.foreach_set('value',[i]*len(me.vertices))
 me.calc_loop_triangles();records.append({'part':part,'part_id':i,'vertices':len(me.vertices),'triangles':len(me.loop_triangles)})

bpy.ops.object.select_all(action='DESELECT')
for o in parts:o.select_set(True)
bpy.context.view_layer.objects.active=bpy.data.objects['Vincent_REF_head'];bpy.ops.object.join();mesh=bpy.context.object;mesh.name='VincentAdult_CreatorMesh';mesh.data.name='VincentAdult_CreatorGeometry';mesh.parent=rig;mesh.matrix_parent_inverse.identity();mesh.matrix_world.identity()
for o in list(bpy.data.objects):
 if o not in [mesh,rig]:bpy.data.objects.remove(o,do_unlink=True)
for mod in list(mesh.modifiers):
 if mod.type!='ARMATURE':mesh.modifiers.remove(mod)
assert mesh.data.shape_keys and len(mesh.data.shape_keys.key_blocks)==6,[k.name for k in mesh.data.shape_keys.key_blocks]
# xatlas generates non-overlapping charts from the actual triangles. Original UV reads stay separate.
# The verified CPython3.13 wheel is a scratch build tool, not a runtime dependency.
sys.path.insert(0,'/tmp/ct-xatlas-0.0.11');import xatlas
bpy.context.view_layer.objects.active=mesh;bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.mesh.quads_convert_to_tris(quad_method='BEAUTY',ngon_method='BEAUTY');bpy.ops.object.mode_set(mode='OBJECT');mesh.data.calc_loop_triangles()
ids=mesh.data.attributes['source_part_id'].data;pnames={r['part_id']:r['part'] for r in records};density={'head':1.5,'eyeball_L':1.8,'eyeball_R':1.8,'eyeglasses':1.1,'body':1.1}
vertices=np.array([list(v.co*density.get(pnames[ids[v.index].value],1)) for v in mesh.data.vertices],dtype=np.float32);triangles=np.array([t.vertices[:] for t in mesh.data.loop_triangles],dtype=np.uint32)
atlas=xatlas.Atlas();atlas.add_mesh(vertices,triangles);chart=xatlas.ChartOptions();pack=xatlas.PackOptions();pack.resolution=2048;pack.padding=16;pack.bilinear=True
print('XATLAS_START',len(triangles),flush=True);atlas.generate(chart_options=chart,pack_options=pack);assert atlas.atlas_count==1,atlas.atlas_count;vmapping,indices,coords=atlas[0];assert np.array_equal(vmapping[indices],triangles),'xatlas triangle correspondence changed'
uv=mesh.data.uv_layers['AtlasUV'];uvs=np.empty((len(mesh.data.loops),2),dtype=np.float32)
for t,mapped in zip(mesh.data.loop_triangles,indices):
 for li,vi in zip(t.loops,mapped):uvs[li]=coords[vi]
uv.data.foreach_set('uv',uvs.ravel());mesh.data.uv_layers.active=uv;uv.active_render=True
print('XATLAS_DONE',atlas.width,atlas.height,atlas.utilization,atlas.get_mesh_chart_count(0),flush=True)
(out/'xatlas-layout.json').write_text(json.dumps({'tool':'xatlas0.0.11','input_triangles':len(triangles),'width':atlas.width,'height':atlas.height,'utilization':atlas.utilization,'charts':atlas.get_mesh_chart_count(0),'atlas_count':atlas.atlas_count,'packing_padding_px':16,'effective_padding_at_2048_min_px':16*2048/max(atlas.width,atlas.height),'target_resolution':2048},indent=2))
audit=validate_atlas(mesh.data);(out/'atlas-uv-validation.json').write_text(json.dumps(audit,indent=2));assert audit['overlapping_interior_samples']==0,{k:v for k,v in audit.items() if k!='overlapping_triangle_pairs'};assert audit['hidden_faces']==audit['hidden_vertices']==0
bpy.ops.wm.save_as_mainfile(filepath=str(out/'VincentAdult_AtlasBakeSource.blend'))
scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=16;scene.render.threads_mode='FIXED';scene.render.threads=4;scene.render.image_settings.media_type='IMAGE';scene.render.image_settings.file_format='PNG'
scene.render.bake.margin=8;scene.render.bake.use_clear=True;scene.render.bake.use_selected_to_active=False;scene.render.bake.normal_space='TANGENT';scene.render.bake.normal_r='POS_X';scene.render.bake.normal_g='POS_Y';scene.render.bake.normal_b='POS_Z'
def new_image(name,space):
 im=bpy.data.images.new(name,2048,2048,alpha=True,float_buffer=True);im.colorspace_settings.name=space;im.generated_color=(0,0,0,0);return im
base_mats=list(mesh.data.materials);base=new_image('VincentAdult_BaseColor','sRGB');normal=new_image('VincentAdult_Normal','Non-Color');raw=new_image('RawMetalRough','Non-Color')
def bake(kind,im):
 mats=[]
 for original in base_mats:
  cp=original.copy();cp.name=kind+'_'+original.name;t=cp.node_tree;bs=t.nodes['Portable source surface']
  if kind!='normal':
   emission=t.nodes.new('ShaderNodeEmission');emission.inputs['Strength'].default_value=1
   if kind=='base':
    inp=bs.inputs['Base Color']
    if inp.is_linked:t.links.new(inp.links[0].from_socket,emission.inputs['Color'])
    else:emission.inputs['Color'].default_value=inp.default_value
   else:
    combine=t.nodes.new('ShaderNodeCombineXYZ')
    for dst,src in [('X','Metallic'),('Y','Roughness')]:
     inp=bs.inputs[src]
     if inp.is_linked:t.links.new(inp.links[0].from_socket,combine.inputs[dst])
     else:combine.inputs[dst].default_value=inp.default_value
    combine.inputs['Z'].default_value=0;t.links.new(combine.outputs['Vector'],emission.inputs['Color'])
   for link in list(bs.outputs['BSDF'].links):t.links.new(emission.outputs[0],link.to_socket)
  for n in t.nodes:n.select=False
  tex=t.nodes.new('ShaderNodeTexImage');tex.image=im;tex.select=True;t.nodes.active=tex;mats.append(cp)
 for i,m in enumerate(mats):mesh.data.materials[i]=m
 print('VINCENT_BAKE_START',kind,flush=True);start=time.time();bpy.ops.object.bake(type='NORMAL' if kind=='normal' else 'EMIT',use_clear=True,margin=8,uv_layer='AtlasUV');print('VINCENT_BAKE_DONE',kind,time.time()-start,flush=True)
 for i,m in enumerate(base_mats):mesh.data.materials[i]=m

def save_standard(im,name,depth):
 scene.render.image_settings.color_mode='RGBA';scene.render.image_settings.color_depth=depth;im.filepath_raw=str(ex/name);im.file_format='PNG';im.save();return im.filepath_raw
bake('base',base);save_standard(base,'VincentAdult_BaseColor.png','8');bake('normal',normal);save_standard(normal,'VincentAdult_Normal.png','16');bake('metalrough',raw)
a=np.array(raw.pixels[:],dtype=np.float32).reshape(2048,2048,4);a[:,:,3]=1-np.clip(a[:,:,1],0,1);a[:,:,0]=np.clip(a[:,:,0],0,1);a[:,:,1]=1;a[:,:,2]=0
expected=write_rgba16(ex/'VincentAdult_MetallicSmoothness.png',a);packed=bpy.data.images.load(str(ex/'VincentAdult_MetallicSmoothness.png'));packed.colorspace_settings.name='Non-Color';actual=np.array(packed.pixels[:],dtype=np.float32).reshape(expected.shape);packed_error=float(np.max(np.abs(actual-expected)));assert packed_error<=1/65535,packed_error
mat=bpy.data.materials.new('VincentAdult_Atlas');mat.use_nodes=True;nodes=mat.node_tree.nodes;links=mat.node_tree.links;bs=nodes.get('Principled BSDF');uvnode=nodes.new('ShaderNodeUVMap');uvnode.uv_map='AtlasUV';tex={}
for label,im in [('BaseColor',base),('Normal',normal),('MetallicSmoothness',packed)]:
 n=nodes.new('ShaderNodeTexImage');n.name=label;n.image=im;links.new(uvnode.outputs['UV'],n.inputs['Vector']);tex[label]=n
links.new(tex['BaseColor'].outputs['Color'],bs.inputs['Base Color']);nm=nodes.new('ShaderNodeNormalMap');nm.uv_map='AtlasUV';links.new(tex['Normal'].outputs['Color'],nm.inputs['Color']);links.new(nm.outputs['Normal'],bs.inputs['Normal']);sep=nodes.new('ShaderNodeSeparateColor');links.new(tex['MetallicSmoothness'].outputs['Color'],sep.inputs['Color']);links.new(sep.outputs['Red'],bs.inputs['Metallic']);inv=nodes.new('ShaderNodeMath');inv.operation='SUBTRACT';inv.inputs[0].default_value=1;links.new(tex['MetallicSmoothness'].outputs['Alpha'],inv.inputs[1]);links.new(inv.outputs[0],bs.inputs['Roughness'])
mesh.data.materials.clear();mesh.data.materials.append(mat)
for p in mesh.data.polygons:p.material_index=0
for layer in list(mesh.data.uv_layers):
 if layer.name!='AtlasUV':mesh.data.uv_layers.remove(layer)
mesh.data.uv_layers.active=mesh.data.uv_layers['AtlasUV'];mesh.data.uv_layers['AtlasUV'].active_render=True
mesh.data.calc_loop_triangles();manifest={'status':'Compiled male candidate, atlas/morph visual and fresh-FBX gates pending. No Unity/device claim.','source':'https://studio.blender.org/characters/vincent/v1/','attribution':'Vincent (CC) Blender Foundation | studio.blender.org','licence':'https://creativecommons.org/licenses/by/4.0/','geometry':{'mesh':mesh.name,'triangles':len(mesh.data.loop_triangles),'vertices':len(mesh.data.vertices),'material_slots':1,'bones':len(rig.data.bones),'max_influences':max(len(v.groups) for v in mesh.data.vertices),'height_m':max(v.co.z for v in mesh.data.vertices),'sole_z_m':min(v.co.z for v in mesh.data.vertices)},'source_parts':records,'facial_morphs':[k.name for k in mesh.data.shape_keys.key_blocks if k.name!='Basis'],'source_material_conversion':conversion,'atlas':{'size':[2048,2048],'uv':'AtlasUV / UV0','base_color':'sRGB','normal':'Non-Color tangent +Y','metallic_smoothness':'Linear R=Metallic, A=Smoothness; independent raw16-bit channels','packed_channel_readback_max_error':packed_error,'packing_padding_px':16,'packing_canvas_px':[atlas.width,atlas.height],'effective_padding_at_2048_min_px':16*2048/max(atlas.width,atlas.height),'bake_padding_px':8},'export':{'axis_forward':'-Z','axis_up':'Y','global_scale':1,'own_humanoid_mapping':'unity-humanoid-map.json','apply_mesh_modifiers':False,'morph_export':True},'limitations':['Source facial poses are independent morphs; no runtime blink/lipsync/controller hookup.','Film skin/subsurface/glossy blend approximated with ordinary PBR.','Transparent lens/cornea reflection overlays omitted; real source iris and frame retained.','Creator source-quality geometry still requires world LODs and device measurement.']}
(out/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
bpy.ops.object.select_all(action='DESELECT');mesh.select_set(True);rig.select_set(True);bpy.context.view_layer.objects.active=rig;bpy.ops.wm.save_as_mainfile(filepath=str(out/'VincentAdult_Unity.blend'));bpy.ops.export_scene.fbx(filepath=str(ex/'VincentAdult_Creator.fbx'),use_selection=True,object_types={'MESH','ARMATURE'},add_leaf_bones=False,use_armature_deform_only=True,bake_anim=False,axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',use_mesh_modifiers=False,path_mode='STRIP');print('VINCENT_COMPILED',json.dumps(manifest['geometry']),flush=True)
