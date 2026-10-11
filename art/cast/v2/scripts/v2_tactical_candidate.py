"""Single-base review candidate; preserves all original source/export/Blender files.
Requires a scheduled Blender slot. Never imports or registers anything in Unity.
"""
import bpy, copy, hashlib, json, sys, time
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import v2_build as B
import v2_common as C
import v2_spec as SPEC

def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()

def main():
    target=C.V2/'candidates/tactical-island-20261009/export'
    evidence=C.REPO.parent/'ct-life-evidence/claude-continuation-20261009/cast-reconcile/tactical-candidate-1'
    if target.exists() or evidence.exists():raise FileExistsError('Preserve prior candidate; use a reviewed new version')
    protected={str(p):sha(p) for p in (C.EXPORT/'bases').rglob('*') if p.is_file()}
    source=Path(SPEC.BASES['hero_tactical']['file']);protected[str(source)]=sha(source)
    for p in (C.EVID/'bases').glob('*.blend'):protected[str(p)]=sha(p)
    exclusion={'object':'Object_102','materials':['material'],'vertices':722,
       'source_sha256':'e25e0ac0520ef669c324af2de88b9cb614dd01eeef8d6ed7db917f5f6f682f20',
       'reason':'Isolated source component stays at shin height in source POSE and REST, 98.48% Head weight; candidate exclusion pending before/after visual review.'}
    if sha(source)!=exclusion['source_sha256']:raise ValueError('Unexpected source hash')
    old=SPEC.BASES['hero_tactical'];spec=copy.deepcopy(old);spec['source_component_exclusions']=[exclusion]
    SPEC.BASES['hero_tactical']=spec
    evidence.mkdir(parents=True);target.mkdir(parents=True)
    receipt={'status':'started','candidate_export':str(target),'source':str(source),'exclusion':exclusion,
             'original_hashes':protected,'script_hashes':{p.name:sha(p) for p in C.V2.joinpath('scripts').glob('*.py')}}
    try:
        # Show the exact excluded primitive itself before interpreting its identity.
        # Neutral diagnostic material makes its shape readable; geometry is untouched.
        import v2_render as R
        from mathutils import Vector
        sc,cam=R.reset()
        bpy.ops.import_scene.gltf(filepath=str(source))
        isolated=bpy.context.scene.objects.get(exclusion['object'])
        if isolated is None or isolated.type!='MESH' or len(isolated.data.vertices)!=exclusion['vertices']:
            raise ValueError('Unexpected component during isolated visual review')
        for o in sc.objects:
            if o.type=='MESH':o.hide_render=o!=isolated
            elif o.type=='ARMATURE':o.data.pose_position='REST';o.animation_data_clear()
        diagnostic=bpy.data.materials.new('Diagnostic neutral, original geometry');diagnostic.diffuse_color=(.55,.6,.66,1)
        isolated.data.materials.clear();isolated.data.materials.append(diagnostic)
        center=sum((isolated.matrix_world@v.co for v in isolated.data.vertices),Vector())/len(isolated.data.vertices)
        sc.render.resolution_x=sc.render.resolution_y=512;cam.data.ortho_scale=.30
        for view,direction in [('front',Vector((0,-1,.15))),('oblique',Vector((.4,-1,.6)))]:
            cam.location=center+direction;cam.rotation_euler=(-direction).to_track_quat('-Z','Y').to_euler()
            sc.render.filepath=str(evidence/('source-component-'+view+'.png'));bpy.ops.render.render(write_still=True)
        bpy.ops.wm.read_factory_settings(use_empty=True)
        B.EXPORT=target;B.EVID=evidence
        B.build_base('hero_tactical')
        # A separate diagnostic sheet retains the original/candidate materials and poses.
        # Same preview shader and same camera recipe as the existing cast sheet, not in-game proof.
        for name,export,blends in [('before',C.EXPORT,C.EVID),('after',target,evidence)]:
            R.EXPORT=export;R.EVID=blends
            meta=json.loads((export/'bases/hero_tactical/hero_tactical.json').read_text())
            sc,cam=R.reset();arm,meshes=R.load_base('hero_tactical')
            R.pose_arms_down(arm)
            material=R.toon_material(name,meta,{})
            for o in meshes:o.data.materials.clear();o.data.materials.append(material)
            R.render_cell(sc,cam,arm,meshes,'body',evidence/(name+'-body.png'))
            R.render_cell(sc,cam,arm,meshes,'head',evidence/(name+'-head.png'))
        receipt['status']='exported_and_previewed_not_accepted'
    except Exception as exc:
        receipt['status']='failed';receipt['error']=type(exc).__name__+': '+str(exc)
        raise
    finally:
        SPEC.BASES['hero_tactical']=old
        receipt['original_drift']=[p for p,h in protected.items() if not Path(p).exists() or sha(Path(p))!=h]
        receipt['candidate_hashes']={str(p.relative_to(target)):sha(p) for p in target.rglob('*') if p.is_file()}
        (evidence/'receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
        if receipt['original_drift']:raise RuntimeError('Original source drift detected')

if __name__=='__main__':main()
