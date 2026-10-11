"""Portable cloth detail attenuation: avoid film procedural micro-bump aliasing at 2048.
Retains original pigment/roughness, face normals and geometric folds. Raw source untouched.
"""
import bpy,sys,json,hashlib,shutil,numpy as np,time
from pathlib import Path
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'scripts'));from png_data_texture import write_rgba16
out=root/'vincent-adult-production';dest=out/'exports'/'VincentAdult_Normal.png';archive=out/'archive'/'prior-cloth-normal';archive.mkdir(exist_ok=True)
if not (archive/dest.name).exists():shutil.copy2(dest,archive/dest.name)
bpy.ops.wm.open_mainfile(filepath=str(out/'VincentAdult_AtlasBakeSource.blend'),use_scripts=False);scene=bpy.context.scene;mesh=bpy.data.objects['VincentAdult_CreatorMesh'];rig=bpy.data.objects['VincentAdult_Rig']
for b in rig.pose.bones:b.matrix_basis.identity()
for k in mesh.data.shape_keys.key_blocks:k.value=0
changed=[]
for mat in mesh.data.materials:
 if not any(x in mat.name for x in ['vincent_shirt','vincent_pants','vincent_socks']):continue
 tree=mat.node_tree
 for node in tree.nodes:
  if node.type=='DISPLACEMENT':
   inp=node.inputs['Scale']
   if inp.is_linked:
    prior=inp.links[0].from_socket;scale=tree.nodes.new('ShaderNodeMath');scale.operation='MULTIPLY';scale.inputs[1].default_value=.12;tree.links.new(prior,scale.inputs[0]);tree.links.new(scale.outputs[0],inp)
   else:inp.default_value*=.12
   changed.append(mat.name)
 bs=tree.nodes['Portable source surface'];inp=bs.inputs['Normal']
 if not inp.is_linked:continue
 authored=inp.links[0].from_socket;geo=tree.nodes.new('ShaderNodeNewGeometry');detail=tree.nodes.new('ShaderNodeVectorMath');detail.operation='SCALE';detail.inputs['Scale'].default_value=.12;tree.links.new(authored,detail.inputs[0]);smooth=tree.nodes.new('ShaderNodeVectorMath');smooth.operation='SCALE';smooth.inputs['Scale'].default_value=.88;tree.links.new(geo.outputs['Normal'],smooth.inputs[0]);mix=tree.nodes.new('ShaderNodeVectorMath');mix.operation='ADD';tree.links.new(detail.outputs[0],mix.inputs[0]);tree.links.new(smooth.outputs[0],mix.inputs[1]);norm=tree.nodes.new('ShaderNodeVectorMath');norm.operation='NORMALIZE';tree.links.new(mix.outputs[0],norm.inputs[0]);tree.links.new(norm.outputs[0],inp);changed.append(mat.name)
assert any('pants' in x for x in changed),changed
image=bpy.data.images.new('VincentPortableNormal',2048,2048,alpha=True,float_buffer=True);image.colorspace_settings.name='Non-Color';image.generated_color=(.5,.5,1,1)
for mat in mesh.data.materials:
 for n in mat.node_tree.nodes:n.select=False
 n=mat.node_tree.nodes.new('ShaderNodeTexImage');n.image=image;n.select=True;mat.node_tree.nodes.active=n
bpy.ops.object.select_all(action='DESELECT');mesh.select_set(True);bpy.context.view_layer.objects.active=mesh;scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=16;scene.render.threads_mode='FIXED';scene.render.threads=4;scene.render.bake.normal_space='TANGENT';scene.render.bake.normal_r='POS_X';scene.render.bake.normal_g='POS_Y';scene.render.bake.normal_b='POS_Z';scene.render.bake.use_selected_to_active=False
bpy.ops.object.bake(type='NORMAL',use_clear=True,margin=8,uv_layer='AtlasUV');pixels=np.array(image.pixels[:],dtype=np.float32).reshape(2048,2048,4);pixels[:,:,3]=1;expected=write_rgba16(dest,pixels);read=bpy.data.images.load(str(dest),check_existing=False);read.colorspace_settings.name='Non-Color';error=float(np.max(np.abs(np.array(read.pixels[:],dtype=np.float32).reshape(expected.shape)-expected)));assert error<=1/65535
report={'all_pass':True,'reason':'Source cloth micro-bump aliases visibly when converted into a 2048 runtime atlas; actual normal-disabled comparison isolated it.','changed_materials':changed,'portable_cloth_displacement_bump_scale_multiplier':.12,'unchanged':'Base colour, metallic/smoothness, all source geometry/weights/morphs and non-cloth normal inputs.','normal_space':'Tangent +Y, non-colour data','raw_rgba16_readback_max_error':error,'prior_normal_sha256':hashlib.sha256((archive/dest.name).read_bytes()).hexdigest(),'corrected_normal_sha256':hashlib.sha256(dest.read_bytes()).hexdigest(),'proof':'renders/creator_flatnormal_front.png isolates the former moire; final tier proof uses the attenuated map.'};(out/'cloth-normal-adaptation.json').write_text(json.dumps(report,indent=2)+'\n');print('PORTABLE_CLOTH_NORMAL_READY',json.dumps(report),flush=True)
# Update only the derived three-tier authoring file to load the exact delivered map.
bpy.ops.wm.open_mainfile(filepath=str(out/'VincentAdult_Tiers.blend'),use_scripts=False)
for mat in bpy.data.materials:
 if mat.name=='VincentAdult_Atlas':
  node=mat.node_tree.nodes.get('Normal');im=bpy.data.images.load(str(dest),check_existing=False);im.colorspace_settings.name='Non-Color';node.image=im
bpy.ops.wm.save_as_mainfile(filepath=str(out/'VincentAdult_Tiers.blend'))
