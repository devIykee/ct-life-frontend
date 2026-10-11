import bpy,json
from pathlib import Path
root=Path(__file__).resolve().parents[1]
(root/"export-check").mkdir(exist_ok=True)
for name in ["snow","rain"]:
    bpy.ops.wm.open_mainfile(filepath=str(next((root/"raw"/name).rglob("*.blend"))),use_scripts=False)
    if bpy.context.object and bpy.context.object.mode!="OBJECT":bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    for o in bpy.data.objects:
        if (o.type=="MESH" and o.name.startswith("GEO-") and not o.hide_render) or (o.type=="ARMATURE" and o.name.startswith("RIG-")):
            o.hide_viewport=False;o.hide_set(False);o.select_set(True)
            for m in o.modifiers:
                if m.type=="SUBSURF":m.show_viewport=False;m.show_render=False
    bpy.ops.export_scene.gltf(filepath=str(root/"export-check"/(name+"_unoptimized.glb")),export_format="GLB",use_selection=True,export_animations=False,export_def_bones=True,export_skins=True,export_morph=True,export_apply=False)
