"""Print a summary of an FBX/GLB/blend: objects, tris, materials, armature bones, actions.
Usage: Blender -b -P inspect_file.py -- <file> [--bones]"""
import bpy, sys, os
argv = sys.argv[sys.argv.index('--') + 1:]
path = argv[0]
bpy.ops.wm.read_factory_settings(use_empty=True)
ext = os.path.splitext(path)[1].lower()
if ext == '.fbx':
    bpy.ops.import_scene.fbx(filepath=path)
elif ext in ('.glb', '.gltf'):
    bpy.ops.import_scene.gltf(filepath=path)
elif ext == '.blend':
    bpy.ops.wm.open_mainfile(filepath=path)
elif ext == '.obj':
    bpy.ops.wm.obj_import(filepath=path)
dg = bpy.context.evaluated_depsgraph_get()
for o in bpy.data.objects:
    line = f"{o.type:9s} {o.name:40s} parent={o.parent.name if o.parent else '-'}"
    if o.type == 'MESH':
        me = o.data
        tris = sum(len(p.vertices) - 2 for p in me.polygons)
        line += f" tris={tris} verts={len(me.vertices)} mats={[m.name if m else None for m in me.materials]} vgroups={len(o.vertex_groups)} uv={len(me.uv_layers)} shapekeys={len(me.shape_keys.key_blocks) if me.shape_keys else 0}"
        line += f" dims={tuple(round(d,3) for d in o.dimensions)}"
    if o.type == 'ARMATURE':
        line += f" bones={len(o.data.bones)} scale={tuple(round(s,3) for s in o.scale)} rot={tuple(round(r,3) for r in o.rotation_euler)}"
    print(line)
    if o.type == 'ARMATURE' and '--bones' in argv:
        for b in o.data.bones:
            print('   ', b.name, 'parent=', b.parent.name if b.parent else '-', 'head=', tuple(round(x, 3) for x in (o.matrix_world @ b.head_local)), 'deform=', b.use_deform)
print('ACTIONS', len(bpy.data.actions))
for a in bpy.data.actions:
    print('  ', a.name, tuple(round(f) for f in a.frame_range))
for img in bpy.data.images:
    print('IMG', img.name, img.size[:], img.filepath)
