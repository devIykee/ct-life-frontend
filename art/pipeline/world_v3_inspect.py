"""World v3: inspect a Sketchfab glb (hierarchy roots, bounds, tris, materials, textures) -> JSON.

Usage: Blender -b --factory-startup -P world_v3_inspect.py -- <in.glb> <out.json> [--depth N]
Groups meshes by their ancestor at --depth below the glTF root (default: deepest node that still has >1 mesh child).
"""
import bpy, sys, json, os
from mathutils import Vector

a = sys.argv[sys.argv.index('--') + 1:]
src, dst = a[0], a[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
dg = bpy.context.evaluated_depsgraph_get()


def wbounds(o):
    pts = [o.matrix_world @ Vector(c) for c in o.bound_box]
    return [min(p[i] for p in pts) for i in range(3)], [max(p[i] for p in pts) for i in range(3)]


meshes = [o for o in bpy.data.objects if o.type == 'MESH']
out = {'file': os.path.basename(src), 'meshes': []}
for o in meshes:
    mn, mx = wbounds(o)
    chain = []
    p = o.parent
    while p:
        chain.append(p.name)
        p = p.parent
    tris = sum(len(poly.vertices) - 2 for poly in o.data.polygons)
    mats = [s.material.name for s in o.material_slots if s.material]
    out['meshes'].append(dict(name=o.name, data=o.data.name, parents=chain[::-1], min=mn, max=mx, tris=tris, mats=mats))
imgs = []
for im in bpy.data.images:
    imgs.append(dict(name=im.name, size=list(im.size)))
out['images'] = imgs
mats = {}
for m in bpy.data.materials:
    tex = []
    if m.use_nodes:
        for n in m.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image:
                tex.append(n.image.name)
    mats[m.name] = tex
out['materials'] = mats
allmn = [min(m['min'][i] for m in out['meshes']) for i in range(3)]
allmx = [max(m['max'][i] for m in out['meshes']) for i in range(3)]
out['bounds'] = [allmn, allmx]
out['tris'] = sum(m['tris'] for m in out['meshes'])
json.dump(out, open(dst, 'w'), indent=0)
print('[inspect]', len(meshes), 'meshes', out['tris'], 'tris', 'bounds', [round(x, 2) for x in allmn], [round(x, 2) for x in allmx])
