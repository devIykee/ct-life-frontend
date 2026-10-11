import bpy, math, json
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[1]
(root/"renders").mkdir(exist_ok=True)
for name in ["snow","rain"]:
    path=next((root/"raw"/name).rglob("*.blend"))
    bpy.ops.wm.open_mainfile(filepath=str(path),use_scripts=False)
    scene=bpy.context.scene
    # Retain the supplied geometry, texture maps, rigs and shaders. Render the model itself.
    for o in list(bpy.data.objects):
        if o.type in {"LIGHT","CAMERA"}:
            bpy.data.objects.remove(o,do_unlink=True)
            continue
        elif o.type=="MESH" and not o.name.startswith("GEO-"): o.hide_render=True
        if o.type=="MESH":
            if "cornea" in o.name or "eye_dots" in o.name: o.hide_render=True
            for m in o.modifiers:
                if m.type=="SUBSURF": m.render_levels=1
    mats={o.name:[s.material.name if s.material else None for s in o.material_slots] for o in bpy.data.objects if o.type=="MESH" and o.name.startswith("GEO-")}
    (root/"renders"/(name+"_materials.json")).write_text(json.dumps(mats,indent=2))
    scene.render.engine="BLENDER_EEVEE"
    scene.cycles.samples=16
    scene.cycles.use_denoising=True
    scene.render.resolution_x=1000
    scene.render.resolution_y=1200
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format="PNG"
    scene.render.film_transparent=False
    world=bpy.data.worlds.new("Candidate neutral studio")
    world.use_nodes=True
    world.node_tree.nodes.get("Background").inputs[0].default_value=(.12,.14,.17,1)
    world.node_tree.nodes.get("Background").inputs[1].default_value=.4
    scene.world=world
    def area(label,loc,energy,size,color):
        d=bpy.data.lights.new(label,"AREA");d.energy=energy;d.shape="DISK";d.size=size;d.color=color
        o=bpy.data.objects.new(label,d);scene.collection.objects.link(o);o.location=loc
        o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat("-Z","Y").to_euler()
    area("Key",(3,-4,5),420,4,(1,.88,.76))
    area("Fill",(-3,-2,2.5),260,3,(.7,.83,1))
    area("Rim",(1,3,3.5),500,3,(1,.86,.72))
    camd=bpy.data.cameras.new("Candidate camera");cam=bpy.data.objects.new("Candidate camera",camd);scene.collection.objects.link(cam);scene.camera=cam
    camd.type="ORTHO"
    for view in ["full","face"]:
        target=Vector((0,0,.92 if name=="snow" else .84))
        if view=="full":
            cam.location=(.5,-5,1.7);camd.ortho_scale=2.12
        else:
            target=Vector((0,-.03,1.58 if name=="snow" else 1.49));cam.location=(.38,-4,1.75);camd.ortho_scale=.72
        cam.rotation_euler=(target-cam.location).to_track_quat("-Z","Y").to_euler()
        scene.render.filepath=str(root/"renders"/(name+"_"+view+".png"))
        bpy.ops.render.render(write_still=True)
print("CANDIDATE_RENDERS_DONE")
