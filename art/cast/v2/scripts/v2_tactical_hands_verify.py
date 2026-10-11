"""Read-only persisted-candidate comparison; preserves failed author receipt."""
import bpy,sys,json,hashlib,math
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import v2_common as C
import v2_tactical_hands_candidate as H
E=H.E
OUT=E/'candidate-1-verification'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def read_rig(path):
 bpy.ops.wm.read_factory_settings(use_empty=True)
 with bpy.data.libraries.load(str(path),link=False) as (src,dst):dst.objects=['CTS']
 arm=dst.objects[0];bpy.context.scene.collection.objects.link(arm)
 return {'data':H.rig_record(arm),'object_matrix':[list(x) for x in arm.matrix_world]}
def main():
 OUT.mkdir(exist_ok=False)
 prior=C.REPO.parent/'ct-life-evidence/claude-continuation-20261009/cast-reconcile/tactical-candidate-1/bases/hero_tactical.blend';candidate=H.EVID/'bases/hero_tactical.blend'
 a=read_rig(prior);b=read_rig(candidate)
 r={'scope':'Persisted CTS identity only; no Humanoid/animation acceptance','prior_blend_sha256':sha(prior),'candidate_blend_sha256':sha(candidate),'prior_rig':a,'candidate_rig':b,'exact_equal':a==b,'differing_bones':[n for n in a['data'] if a['data'][n]!=b['data'].get(n)],'original_author_receipt_status':json.loads((H.EVID/'receipt.json').read_text())['status']}
 (OUT/'saved-rig-comparison.json').write_text(json.dumps(r,indent=2)+'\n');print('SAVED_RIG_EQUAL',r['exact_equal'],r['differing_bones'],flush=True)
 if not r['exact_equal']:raise ValueError('Persisted final CTS mismatch')
 # Existing strict validator, fresh imports, separate receipt, no metadata rewriting.
 import v2_validate as V
 old=sys.argv;sys.argv=['verify','--','--output',str(OUT),'--export-root',str(H.OUT)]
 try:V.main()
 finally:sys.argv=old
if __name__=='__main__':main()
