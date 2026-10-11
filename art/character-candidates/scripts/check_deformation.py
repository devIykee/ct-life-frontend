import bpy,json
from pathlib import Path
root=Path(__file__).resolve().parents[1]
reports={}
for name in ["snow","rain"]:
    bpy.ops.wm.open_mainfile(filepath=str(next((root/"raw"/name).rglob("*.blend"))),use_scripts=False)
    report={"meshes":[],"shape_key_names":{},"export_operator_fields":[]}
    for o in bpy.data.objects:
        if o.type!="MESH" or not o.name.startswith("GEO-") or o.hide_render:continue
        o.hide_viewport=False
        for m in o.modifiers:
            if m.type=="SUBSURF":m.show_viewport=False
        o.data.calc_loop_triangles()
        deform_names={b.name for m in o.modifiers if m.type=="ARMATURE" and m.object for b in m.object.data.bones if b.use_deform}
        deform_groups={v.index for v in o.vertex_groups if v.name in deform_names}
        entry={"name":o.name,"stored_triangles":len(o.data.loop_triangles),"materials":[s.material.name if s.material else None for s in o.material_slots],"armature":[m.object.name for m in o.modifiers if m.type=="ARMATURE" and m.object],"max_deform_influences":max([sum(g.weight>0.0001 and g.group in deform_groups for g in v.groups) for v in o.data.vertices],default=0)}
        dg=bpy.context.evaluated_depsgraph_get();mesh=o.evaluated_get(dg).to_mesh();mesh.calc_loop_triangles();entry["evaluated_triangles_without_subdivision"]=len(mesh.loop_triangles);o.evaluated_get(dg).to_mesh_clear()
        report["meshes"].append(entry)
        report["shape_key_names"][o.name]=[k.name for k in o.data.shape_keys.key_blocks] if o.data.shape_keys else []
    report["export_operator_fields"]=[p.identifier for p in bpy.ops.export_scene.gltf.get_rna_type().properties]
    reports[name]=report
(root/"deformation-inspection.json").write_text(json.dumps(reports,indent=2))
