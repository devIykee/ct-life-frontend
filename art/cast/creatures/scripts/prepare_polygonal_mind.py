"""Preserve Polygonal Mind's rig/texture; create bounded LOD copies for CT Life.
Run Blender --background --disable-autoexec --python this_file -- SOURCE_DIRECTORY OUTPUT_DIRECTORY.
SOURCE_DIRECTORY contains the pinned publisher FBX/PNG/VRM JSON files, not a downloaded script.
"""
import bpy, json, sys, shutil, hashlib
from pathlib import Path
from mathutils import Vector
source, output = map(Path, sys.argv[sys.argv.index('--') + 1:])
output.mkdir(parents=True, exist_ok=True)
for number, source_name, model_id, label, species in [
    ('120', 'SharkPerson', 'pm_shark', 'Shark Person', 'shark'),
    ('010', 'Froggy', 'pm_froggy', 'Froggy', 'frog')]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    src = source / ('100Avatars_' + number) / ('100Avatars_' + number + '_' + source_name + '.fbx')
    bpy.ops.import_scene.fbx(filepath=str(src), use_anim=False)
    arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    body = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
    bone_names = {b.name for b in arm.data.bones}
    vrm = json.loads(src.with_suffix('.vrm.json').read_text())
    mapping = {h['bone'][0].upper() + h['bone'][1:]: vrm['nodes'][h['node']]['name']
        for h in vrm['extensions']['VRM']['humanoid']['humanBones']
        if vrm['nodes'][h['node']]['name'] in bone_names}
    required = ['Hips','Spine','Head','LeftUpperLeg','LeftLowerLeg','LeftFoot','RightUpperLeg','RightLowerLeg','RightFoot',
        'LeftUpperArm','LeftLowerArm','LeftHand','RightUpperArm','RightLowerArm','RightHand']
    assert all(k in mapping for k in required)
    dest = output / model_id
    dest.mkdir(exist_ok=True)
    points = [body.matrix_world @ Vector(c) for c in body.bound_box]
    height = max(v.z for v in points) - min(v.z for v in points)
    original_tris = sum(len(p.vertices)-2 for p in body.data.polygons)
    body.name = model_id + '_Hero'
    def normalize_weights(obj):
        for vertex in obj.data.vertices:
            weights=sorted([(g.group,g.weight) for g in vertex.groups if g.weight>1e-8],key=lambda x:-x[1])[:4]
            total=sum(w for _,w in weights)
            assert total>0,(model_id,vertex.index)
            # Copy indices before mutation: Blender's RNA elements are invalidated as groups are removed.
            for group_index in [g.group for g in vertex.groups]:obj.vertex_groups[group_index].remove([vertex.index])
            for group,weight in weights:obj.vertex_groups[group].add([vertex.index],weight/total,'REPLACE')
    normalize_weights(body)
    def export(path, meshes):
        bpy.ops.object.select_all(action='DESELECT')
        arm.select_set(True)
        for obj in meshes: obj.select_set(True)
        bpy.context.view_layer.objects.active = arm
        bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'ARMATURE','MESH'},
            add_leaf_bones=False,bake_anim=False,path_mode='STRIP',axis_forward='-Z',axis_up='Y',
            apply_unit_scale=True,use_space_transform=True,mesh_smooth_type='FACE')
    export(dest / (model_id + '_Hero.fbx'), [body])
    tiers=[]
    for index,target in enumerate([original_tris, min(original_tris,2400), min(original_tris,1100)]):
        obj=body.copy();obj.data=body.data.copy();bpy.context.collection.objects.link(obj);obj.name=model_id+'_LOD'+str(index)
        bpy.context.view_layer.objects.active=obj
        if obj.data.shape_keys:
            bpy.ops.object.shape_key_remove(all=True)
        if target<original_tris:
            mod=obj.modifiers.new('Distance reduction','DECIMATE');mod.ratio=target/original_tris
            bpy.ops.object.modifier_apply(modifier=mod.name)
        # QEM combines vertex groups. Normalize the strongest four explicitly after reduction,
        # retaining the source joint bindings instead of relying on Unity to truncate them.
        normalize_weights(obj)
        tiers.append(obj)
    export(dest / (model_id + '.fbx'), tiers)
    shutil.copy2(src.with_suffix('.png'),dest/(model_id+'_Albedo.png'))
    (dest/(model_id+'_humanoid.json')).write_text(json.dumps({'unity_humanoid':mapping},indent=2)+'\n')
    record={'id':model_id,'label':label,'species':species,'height':round(height,4),
        'source_url':f'https://github.com/PolygonalMind/100Avatars/tree/ff07c2ad0017819c4e5366656ee1e5bcc4029bd4/100Avatars_{number}',
        'credit':f'{source_name} by Polygonal Mind; CC0 in publisher VRM/release, attribution retained under repository CC BY 4.0 notice. CT Life adds LODs and Unity integration.',
        'source_sha256':hashlib.sha256(src.read_bytes()).hexdigest(),
        'humanoid_map':f'{model_id}/{model_id}_humanoid.json',
        'files':{'crowd_fbx':f'{model_id}/{model_id}.fbx','hero_fbx':f'{model_id}/{model_id}_Hero.fbx','albedo':f'{model_id}/{model_id}_Albedo.png'},
        'triangles':{'hero':original_tris,**{'lod'+str(i):sum(len(p.vertices)-2 for p in o.data.polygons) for i,o in enumerate(tiers)}}}
    record['source_hashes']={key:hashlib.sha256((output/value).read_bytes()).hexdigest() for key,value in record['files'].items()}
    (dest/(model_id+'.json')).write_text(json.dumps(record,indent=2)+'\n')
    print('CREATURE_EXPORT',json.dumps(record))
