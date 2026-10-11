import bpy, json, sys
from pathlib import Path
root=Path(__file__).resolve().parents[1]
reports={}
for name in ["snow","rain"]:
    path=next((root/"raw"/name).rglob("*.blend"))
    bpy.ops.wm.open_mainfile(filepath=str(path),use_scripts=False)
    report={"file":str(path.relative_to(root)),"objects":[],"images":[],"scenes":[]}
    for o in bpy.data.objects:
        if o.type not in {"MESH","ARMATURE"}: continue
        d={"name":o.name,"type":o.type,"hide_render":o.hide_render,"hide_viewport":o.hide_viewport,"visible":o.visible_get(),"dimensions":list(o.dimensions),"collections":[c.name for c in o.users_collection]}
        if o.type=="MESH":
            o.data.calc_loop_triangles()
            d.update(vertices=len(o.data.vertices),triangles=len(o.data.loop_triangles),materials=[m.name if m else None for m in o.data.materials],shape_keys=[k.name for k in o.data.shape_keys.key_blocks] if o.data.shape_keys else [],modifiers=[{"name":m.name,"type":m.type,"show_render":m.show_render,"show_viewport":m.show_viewport} for m in o.modifiers])
        else: d.update(bones=len(o.data.bones),deform_bones=sum(b.use_deform for b in o.data.bones))
        report["objects"].append(d)
    report["images"]=[{"name":i.name,"size":list(i.size),"filepath":i.filepath} for i in bpy.data.images]
    report["scenes"]=[{"name":s.name,"camera":s.camera.name if s.camera else None,"render_engine":s.render.engine} for s in bpy.data.scenes]
    reports[name]=report
(root/"inspection.json").write_text(json.dumps(reports,indent=2))
print("INSPECTION_WRITTEN",root/"inspection.json")
