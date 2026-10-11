"""Independent 1024/512 world atlases from original Vincent material networks."""
import bpy,json,sys,time,hashlib,numpy as np
from pathlib import Path
out=Path(__file__).resolve().parent;root=out.parent;sys.path.insert(0,str(root/'scripts'));sys.path.insert(0,'/tmp/ct-xatlas-0.0.11');import xatlas
from snow_atlas_validation import validate_atlas
from png_data_texture import write_rgba16
bpy.ops.wm.open_mainfile(filepath=str(out/'VincentAdult_WorldSource.blend'),use_scripts=False);scene=bpy.context.scene;rig=bpy.data.objects['VincentAdult_Rig'];scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
for b in rig.pose.bones:b.matrix_basis.identity()
meshes=[bpy.data.objects['VincentAdult_'+t+'Mesh'] for t in ['WorldNear','WorldFar']]
for o in meshes:
 o.hide_render=True
 if o.data.shape_keys:
  for k in o.data.shape_keys.key_blocks:k.value=0
scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=8;scene.render.threads_mode='FIXED';scene.render.threads=4;scene.render.image_settings.media_type='IMAGE';scene.render.image_settings.file_format='PNG';scene.render.bake.use_selected_to_active=False;scene.render.bake.normal_space='TANGENT';scene.render.bake.normal_r='POS_X';scene.render.bake.normal_g='POS_Y';scene.render.bake.normal_b='POS_Z'
report={};ex=out/'exports';ex.mkdir(exist_ok=True)
for tier,size,pad in [('WorldNear',1024,8),('WorldFar',512,6)]:
 mesh=bpy.data.objects['VincentAdult_'+tier+'Mesh'];mesh.hide_render=False;mesh.hide_set(False);bpy.ops.object.select_all(action='DESELECT');mesh.select_set(True);bpy.context.view_layer.objects.active=mesh;me=mesh.data;me.calc_loop_triangles()
 vertices=np.array([v.co[:] for v in me.vertices],dtype=np.float32);triangles=np.array([t.vertices[:] for t in me.loop_triangles],dtype=np.uint32);ids=me.attributes['source_part_id'].data
 # Allocate face and eyes a little more texel density; far still has recognisable glasses/colour blocks.
 density=np.array([1.4 if ids[v.index].value==9 else 1.6 if ids[v.index].value in [5,6] else 1. for v in me.vertices],dtype=np.float32)
 layer=me.uv_layers.new(name='AtlasUV');isolated=set()
 for attempt in range(4):
  atlas=xatlas.Atlas();groups=[np.array([i for i in range(len(triangles)) if i not in isolated],dtype=np.int32)]+[np.array([i],dtype=np.int32) for i in sorted(isolated)];maps=[]
  for group in groups:
   used,remap=np.unique(triangles[group],return_inverse=True);atlas.add_mesh((vertices*density[:,None])[used],np.asarray(remap.reshape(-1,3),dtype=np.uint32));maps.append(used)
  chart=xatlas.ChartOptions();chart.max_iterations=4;pack=xatlas.PackOptions();pack.resolution=size;pack.padding=pad;pack.bilinear=True;print('PACK_START',tier,attempt,len(isolated),flush=True);atlas.generate(chart_options=chart,pack_options=pack);assert atlas.atlas_count==1;uvs=np.empty((len(me.loops),2),dtype=np.float32)
  for gi,group in enumerate(groups):
   vmapping,indices,coords=atlas[gi];assert np.array_equal(maps[gi][vmapping[indices]],triangles[group])
   for ti,mapped in zip(group,indices):
    for li,vi in zip(me.loop_triangles[int(ti)].loops,mapped):uvs[li]=coords[vi]
  layer.data.foreach_set('uv',uvs.ravel());me.uv_layers.active=layer;layer.active_render=True;audit=validate_atlas(me);(out/(tier+'-uv-audit.json')).write_text(json.dumps(audit,indent=2));print('PACK_AUDIT',tier,attempt,audit['overlapping_interior_samples'],flush=True)
  if audit['overlapping_interior_samples']==0:break
  for pair in audit['overlapping_triangle_pairs']:isolated.update(int(x) for x in pair.split('-'))
 assert audit['overlapping_interior_samples']==0
 margin=max(2,int(pad*size/max(atlas.width,atlas.height)*.75));base_mats=list(me.materials)
 def image(name,space):
  im=bpy.data.images.new('VincentAdult_'+tier+'_'+name,size,size,alpha=True,float_buffer=True);im.colorspace_settings.name=space;im.generated_color=(0,0,0,0);return im
 base=image('BaseColor','sRGB');normal=image('Normal','Non-Color');raw=image('RawMetalRough','Non-Color')
 def bake(kind,im):
  mats=[]
  for original in base_mats:
   cp=original.copy();cp.name=tier+'_'+kind+'_'+original.name;t=cp.node_tree;bs=t.nodes['Portable source surface']
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
  for i,m in enumerate(mats):me.materials[i]=m
  start=time.time();print('BAKE_START',tier,kind,flush=True);bpy.ops.object.bake(type='NORMAL' if kind=='normal' else 'EMIT',use_clear=True,margin=margin,uv_layer='AtlasUV');print('BAKE_DONE',tier,kind,time.time()-start,flush=True)
  for i,m in enumerate(base_mats):me.materials[i]=m
 def save(im,label,depth):
  scene.render.image_settings.color_mode='RGBA';scene.render.image_settings.color_depth=depth;im.filepath_raw=str(ex/f'VincentAdult_{tier}_{label}.png');im.file_format='PNG';im.save()
 bake('base',base);save(base,'BaseColor','8');bake('normal',normal);npix=np.array(normal.pixels[:],dtype=np.float32).reshape(size,size,4);npix[:,:,3]=1;write_rgba16(ex/f'VincentAdult_{tier}_Normal.png',npix);normal=bpy.data.images.load(str(ex/f'VincentAdult_{tier}_Normal.png'),check_existing=False);normal.colorspace_settings.name='Non-Color';bake('metalrough',raw)
 a=np.array(raw.pixels[:],dtype=np.float32).reshape(size,size,4);a[:,:,3]=1-np.clip(a[:,:,1],0,1);a[:,:,0]=np.clip(a[:,:,0],0,1);a[:,:,1]=1;a[:,:,2]=0;expected=write_rgba16(ex/f'VincentAdult_{tier}_MetallicSmoothness.png',a);packed=bpy.data.images.load(str(ex/f'VincentAdult_{tier}_MetallicSmoothness.png'));packed.colorspace_settings.name='Non-Color';err=float(np.max(np.abs(np.array(packed.pixels[:],dtype=np.float32).reshape(expected.shape)-expected)));assert err<=1/65535
 mat=bpy.data.materials.new('VincentAdult_'+tier+'_Atlas');mat.use_nodes=True;nodes=mat.node_tree.nodes;links=mat.node_tree.links;bs=nodes.get('Principled BSDF');uvnode=nodes.new('ShaderNodeUVMap');uvnode.uv_map='AtlasUV';tex={}
 for label,im in [('BaseColor',base),('Normal',normal),('MetallicSmoothness',packed)]:
  n=nodes.new('ShaderNodeTexImage');n.name=label;n.image=im;links.new(uvnode.outputs['UV'],n.inputs['Vector']);tex[label]=n
 links.new(tex['BaseColor'].outputs['Color'],bs.inputs['Base Color']);nm=nodes.new('ShaderNodeNormalMap');nm.uv_map='AtlasUV';links.new(tex['Normal'].outputs['Color'],nm.inputs['Color']);links.new(nm.outputs['Normal'],bs.inputs['Normal']);sep=nodes.new('ShaderNodeSeparateColor');links.new(tex['MetallicSmoothness'].outputs['Color'],sep.inputs['Color']);links.new(sep.outputs['Red'],bs.inputs['Metallic']);inv=nodes.new('ShaderNodeMath');inv.operation='SUBTRACT';inv.inputs[0].default_value=1;links.new(tex['MetallicSmoothness'].outputs['Alpha'],inv.inputs[1]);links.new(inv.outputs[0],bs.inputs['Roughness']);me.materials.clear();me.materials.append(mat)
 for p in me.polygons:p.material_index=0
 for l in list(me.uv_layers):
  if l.name!='AtlasUV':me.uv_layers.remove(l)
 me.uv_layers.active=me.uv_layers['AtlasUV'];me.uv_layers['AtlasUV'].active_render=True
 report[tier]={'size':size,'canvas_size':[atlas.width,atlas.height],'charts':sum(atlas.get_mesh_chart_count(i) for i in range(len(groups))),'isolated_overlap_triangles':sorted(isolated),'utilization':atlas.utilization,'packing_padding':pad,'effective_padding_px':pad*size/max(atlas.width,atlas.height),'bake_margin':margin,'uv_audit':audit,'metallic_smoothness_readback_max_error':err,'channels':'BaseColor sRGB; Normal linear tangent+Y; MetallicSmoothness linear independent Rmetal,Asmoothness','material':mat.name,'files':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in ex.glob('VincentAdult_'+tier+'_*.png')}}
 bpy.ops.object.select_all(action='DESELECT');mesh.select_set(True);rig.select_set(True);bpy.context.view_layer.objects.active=rig;bpy.ops.export_scene.fbx(filepath=str(ex/f'VincentAdult_{tier}.fbx'),use_selection=True,object_types={'MESH','ARMATURE'},add_leaf_bones=False,use_armature_deform_only=True,bake_anim=False,axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',use_mesh_modifiers=False,path_mode='STRIP');mesh.hide_render=True
bpy.ops.wm.save_as_mainfile(filepath=str(out/'VincentAdult_WorldAtlas.blend'));(out/'world-atlas.json').write_text(json.dumps(report,indent=2)+'\n');print('WORLD_ATLAS_READY',flush=True)
