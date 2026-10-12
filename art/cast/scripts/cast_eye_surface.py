"""Keep the two authored Rain pupils present when exporting older cast intermediates.

Fresh extraction now retains the original cap. Cached LOD meshes made before that
correction can restore it from the symmetric surviving cap without changing any
existing geometry, UV, weight or shading data. Called after wardrobe assembly.
"""
import bpy
import numpy as np

def restore_missing_rain_pupil(mesh_object):
    mesh = mesh_object.data
    left = mesh_object.vertex_groups['Eye.L'].index
    right = mesh_object.vertex_groups['Eye.R'].index
    def owned(vertex, group): return any(g.group == group and g.weight > .99 for g in mesh.vertices[vertex].groups)
    # Native pupil cap: four coplanar vertices, separate from the recessed iris ring.
    faces = [p for p in mesh.polygons if p.normal.y < -.9999 and p.center.x > 0 and
             all(owned(i, left) for i in p.vertices) and
             max(mesh.vertices[i].co.y for i in p.vertices) - min(mesh.vertices[i].co.y for i in p.vertices) < 1e-7]
    vertices = sorted({i for p in faces for i in p.vertices})
    assert len(faces) == 2 and len(vertices) == 4, (mesh_object.name, len(faces), len(vertices))
    cap_y = mesh.vertices[vertices[0]].co.y
    assert -.099 < cap_y < -.097, (mesh_object.name, cap_y)
    existing_right = [p for p in mesh.polygons if all(owned(i, right) for i in p.vertices) and
                      all(abs(mesh.vertices[i].co.y - cap_y) < 1e-7 for i in p.vertices)]
    if existing_right:
        return {'mesh':mesh_object.name, 'added_vertices':0, 'added_triangles':0, 'already_complete':True}
    old_vertices = np.array([v.co[:] for v in mesh.vertices], dtype=np.float32)
    old_normals = np.array([n.vector[:] for n in mesh.corner_normals], dtype=np.float32)
    old_packed_normals = [v.value[:] for v in mesh.attributes['custom_normal'].data]
    old_sharp_edges = {tuple(sorted(e.vertices)):e.use_edge_sharp for e in mesh.edges}
    old_seams = {tuple(sorted(e.vertices)):e.use_seam for e in mesh.edges}
    old_polygons = [tuple(p.vertices) for p in mesh.polygons]
    old_edges = [tuple(edge.vertices) for edge in mesh.edges]
    old_uv = {uv.name: np.array([v.uv[:] for v in uv.data], dtype=np.float32) for uv in mesh.uv_layers}
    old_weights = [[(g.group, g.weight) for g in v.groups] for v in mesh.vertices]
    old_group_names = [g.name for g in mesh_object.vertex_groups]
    old_counts = (len(mesh.vertices), len(mesh.polygons), len(mesh.loops))
    source_faces = [(tuple(p.vertices), tuple(p.loop_indices), p.material_index, p.use_smooth) for p in faces]
    face_attributes = {a.name:[a.data[p.index].value for p in faces] for a in mesh.attributes if a.domain == 'FACE' and a.data_type == 'INT'}
    old_name = mesh.name
    old_materials = list(mesh.materials)
    old_smooth = [p.use_smooth for p in mesh.polygons]
    old_material_indices = [p.material_index for p in mesh.polygons]
    old_face_attributes = {a.name:[v.value for v in a.data] for a in mesh.attributes if a.domain == 'FACE' and a.data_type == 'INT'}
    mirror = {v: old_counts[0]+i for i,v in enumerate(vertices)}
    added_vertices = old_vertices[vertices].copy(); added_vertices[:,0] *= -1
    added_faces = [tuple(mirror[i] for i in reversed(indices)) for indices,_,_,_ in source_faces]
    added_normals = []
    new_uv = {name:[] for name in old_uv}
    for i,(indices,loops,material,smooth) in enumerate(source_faces):
        for k in (2,1,0):
            for name,values in old_uv.items(): new_uv[name].append(values[loops[k]])
            normal = old_normals[loops[k]].copy(); normal[0] *= -1; added_normals.append(normal)
    # Build a fresh mesh so Blender's packed custom-data layers are not reallocated while
    # their RNA views are alive. Vertex/face order remains byte-for-byte comparable.
    replacement = bpy.data.meshes.new(old_name + '_pupil_candidate')
    replacement.from_pydata(np.concatenate([old_vertices,added_vertices]).tolist(), old_edges, old_polygons+added_faces)
    for material in old_materials: replacement.materials.append(material)
    for p,smooth,material in zip(replacement.polygons,old_smooth+[f[3] for f in source_faces],old_material_indices+[f[2] for f in source_faces]):
        p.use_smooth=smooth; p.material_index=material
    for edge in replacement.edges:
        key=tuple(sorted(edge.vertices));edge.use_edge_sharp=old_sharp_edges.get(key,False);edge.use_seam=old_seams.get(key,False)
    for name,values in old_uv.items():
        layer=replacement.uv_layers.new(name=name)
        layer.data.foreach_set('uv',np.concatenate([values,np.array(new_uv[name])]).ravel())
    for name,values in old_face_attributes.items():
        attr=replacement.attributes.get(name) or replacement.attributes.new(name,'INT','FACE')
        attr.data.foreach_set('value',values+face_attributes[name])
    mesh_object.data=replacement
    if not mesh_object.vertex_groups:
        for name in old_group_names: mesh_object.vertex_groups.new(name=name)
    assert [g.name for g in mesh_object.vertex_groups] == old_group_names
    for i,weights in enumerate(old_weights):
        for group,weight in weights:mesh_object.vertex_groups[group].add([i],weight,'REPLACE')
    mesh_object.vertex_groups['Eye.R'].add(list(mirror.values()),1.0,'REPLACE')
    if mesh.users==0:bpy.data.meshes.remove(mesh)
    mesh=replacement;mesh.name=old_name
    # Copy encoded normal data directly. Re-encoding all normals can both round their
    # values and change sharp edges. Zero on the new planar cap means its exact face normal.
    packed=mesh.attributes.new('custom_normal','INT16_2D','CORNER').data
    packed.foreach_set('value',np.array(old_packed_normals+[(0,0)]*6,dtype=np.int32).ravel())
    mesh.update()
    assert np.array_equal(old_vertices, np.array([v.co[:] for v in mesh.vertices[:old_counts[0]]], dtype=np.float32))
    assert old_polygons == [tuple(p.vertices) for p in mesh.polygons[:old_counts[1]]]
    assert old_weights == [[(g.group,g.weight) for g in v.groups] for v in mesh.vertices[:old_counts[0]]]
    for name,values in old_uv.items():
        assert np.array_equal(values, np.array([v.uv[:] for v in mesh.uv_layers[name].data[:old_counts[2]]], dtype=np.float32))
    after_normals = np.array([n.vector[:] for n in mesh.corner_normals[:old_counts[2]]], dtype=np.float32)
    maximum_normal_change = float(np.max(np.abs(after_normals-old_normals)))
    assert maximum_normal_change < .000001, (mesh_object.name, maximum_normal_change)
    return {'mesh':mesh_object.name, 'added_vertices':4, 'added_triangles':2,
            'existing_positions_uvs_weights_faces_unchanged':True, 'maximum_existing_normal_change':maximum_normal_change}
