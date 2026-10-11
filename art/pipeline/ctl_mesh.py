"""Mesh/LOD/FBX helpers shared by the character export scripts."""
import bpy, os
from mathutils import Matrix
import ctl_config as C


def tris(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def select_only(objs, active):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active


def clean_weights(o, max_inf):
    deform = set(C.BONE_NAMES)
    for vg in list(o.vertex_groups):
        if vg.name not in deform:
            o.vertex_groups.remove(vg)
    for v in o.data.vertices:
        ws = sorted([(g.weight, g.group) for g in v.groups if g.weight > 0], reverse=True)
        keep = ws[:max_inf]
        tot = sum(w for w, _ in keep) or 1
        for w, gi in ws[max_inf:]:
            o.vertex_groups[gi].remove([v.index])
        for w, gi in keep:
            o.vertex_groups[gi].add([v.index], w / tot, 'REPLACE')


def dup(o, name):
    d = o.copy()
    d.data = o.data.copy()
    d.name = d.data.name = name
    bpy.context.scene.collection.objects.link(d)
    for k in list(d.keys()):
        del d[k]
    uvs = d.data.uv_layers
    act = uvs.active.name if uvs.active else None
    for uv in list(uvs):
        if uv.name != act:
            uvs.remove(uv)
    return d


def decimate(o, target_tris=None, ratio=None, sym=True):
    t = tris(o)
    if ratio is None:
        if not target_tris or t <= target_tris:
            return
        ratio = target_tris / t
    m = o.modifiers.new('Dec', 'DECIMATE')
    m.ratio = ratio
    m.use_symmetry = sym
    m.symmetry_axis = 'X'
    select_only([o], o)
    bpy.ops.object.modifier_move_to_index(modifier='Dec', index=0)
    bpy.ops.object.modifier_apply(modifier='Dec')


def ensure_skinned(o, rig):
    o.parent = rig
    o.matrix_parent_inverse = Matrix.Identity(4)
    if not any(m.type == 'ARMATURE' for m in o.modifiers):
        o.modifiers.new('Armature', 'ARMATURE')
    for m in o.modifiers:
        if m.type == 'ARMATURE':
            m.object = rig


def lods_for(o, key, slot, rig):
    budget = C.LOD_BUDGET.get(slot, (1500, 750, 300))
    out = []
    for i, target in enumerate(budget):
        d = dup(o, f'{key}_LOD{i}')
        decimate(d, target_tris=target)
        clean_weights(d, 2 if i == 2 else 4)
        ensure_skinned(d, rig)
        out.append(d)
    return out


def export_fbx(path, objs, rig):
    """Unity-friendly skinned FBX: metres, -Z forward / Y up, no leaf bones, deform bones only,
    no Apply Transform (broken for armatures). Tick 'Bake Axis Conversion' on import."""
    select_only([rig] + objs, rig)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        use_mesh_modifiers=False,   # only Armature modifiers remain; False keeps shape keys (blink/visemes) mesh_smooth_type='FACE', use_tspace=False, use_triangles=True,
        add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X',
        use_armature_deform_only=True, armature_nodetype='NULL', bake_anim=False,
        path_mode='RELATIVE', embed_textures=False)
