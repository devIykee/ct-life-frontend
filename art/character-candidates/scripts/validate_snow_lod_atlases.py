import bpy,sys,json
from pathlib import Path
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'scripts'));from snow_atlas_validation import validate_atlas
out=root/'unity-snow/calf-quality-candidate';bpy.ops.wm.open_mainfile(filepath=str(out/'SnowAdult_LODs.blend'),use_scripts=False);reports={}
for i in [1,2]:
 r=validate_atlas(bpy.data.objects[f'SnowAdult_LOD{i}Mesh'].data);reports[f'LOD{i}']=r;print('LOD_ATLAS',i,json.dumps(r),flush=True)
(out/'lod-atlas-uv-validation.json').write_text(json.dumps(reports,indent=2))
