"""Isolated source-glove alignment experiment. Does not select or replace the working cast.
The matrix is a measured 20-ring fit, NOT a recovered exact source bind transform.
"""
import bpy,bmesh,copy,hashlib,json,math,sys,time,os
from pathlib import Path
from mathutils import Matrix,Vector,Quaternion
sys.path.insert(0,str(Path(__file__).resolve().parent))
import v2_common as C
import v2_spec as S
import v2_adapters as A
import v2_build as B
E=C.REPO.parent/'ct-life-evidence/claude-continuation-20261009/cast-hands'
OUT=C.V2/'candidates/tactical-hands-20261009/export'
EVID=E/'candidate-1'
SRC_SHA='e25e0ac0520ef669c324af2de88b9cb614dd01eeef8d6ed7db917f5f6f682f20'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def snapshot(root):return {str(p):sha(p) for p in root.rglob('*') if p.is_file()}
def rig_record(a):return {b.name:{'parent':b.parent.name if b.parent else None,'matrix':[list(r) for r in b.matrix_local],'head':list(b.head_local),'tail':list(b.tail_local)} for b in a.data.bones}
def apply_grip(a):
 # Synthetic flexion stress pose. Current CTS rest transforms are not modified.
 for side,sign in [('l',1),('r',-1)]:
  for finger in ['index','middle','ring','pinky','thumb']:
   for idx,deg in enumerate(([48,60,35] if finger!='thumb' else [20,30,25]),1):
    b=a.pose.bones[f'{finger}_0{idx}_{side}'];p=a.pose.bones[f'index_01_{side}'].head;q=a.pose.bones[f'pinky_01_{side}'].head
    axis=(q-p).normalized();m=b.matrix.copy();h=b.head.copy()
    rot=Quaternion(axis,math.radians(deg*sign)).to_matrix().to_4x4()
    b.matrix=Matrix.Translation(h)@rot@Matrix.Translation(-h)@m;bpy.context.view_layer.update()
def render_hands(R,a,meshes,sc,cam,name):
 from mathutils import Vector
 sc.render.resolution_x=800;sc.render.resolution_y=600;sc.render.resolution_percentage=100
 for side in ['l','r']:
  h=a.matrix_world@a.pose.bones[f'hand_{side}'].head;m=a.matrix_world@a.pose.bones[f'middle_02_{side}'].head;center=h.lerp(m,.57)
  for view,d in [('front',Vector((.5 if side=='l' else -.5,-1,.25))),('back',Vector((-.4 if side=='l' else .4,1,.45)))]:
   cam.data.ortho_scale=.30;cam.location=center+d;cam.rotation_euler=(-d).to_track_quat('-Z','Y').to_euler();sc.render.filepath=str(EVID/f'{name}-{side}-{view}.png');bpy.ops.render.render(write_still=True)
def main():
 print('CT_HANDS_START',os.getpid(),time.time(),flush=True)
 if OUT.exists() or EVID.exists():raise FileExistsError('Prior output must remain; choose a new candidate version')
 original=snapshot(C.EXPORT/'bases');prior=snapshot(C.V2/'candidates/tactical-island-20261009');source=Path(S.BASES['hero_tactical']['file']);original[str(source)]=sha(source)
 assert sha(source)==SRC_SHA
 alignment=json.loads((E/'alignment-analysis.json').read_text());assert alignment['raw_vertex_max_error_m']<1e-6 and len(alignment['anchors'])==20 and alignment['max_joint_ring_error_m']<.016
 transform=Matrix(alignment['matrix_world_candidate']);assert transform.determinant()<0
 spec=copy.deepcopy(S.BASES['hero_tactical']);oldspec=S.BASES['hero_tactical'];oldsnap=A.snap_mesh_to_bones;oldadapter=A.ADAPTERS['hero'];done=False
 spec['drop_meshes']=[];spec['add_hands']=False;spec['source_component_exclusions']=[{'object':'Object_102','materials':['material'],'vertices':722,'source_sha256':SRC_SHA,'reason':'Previously reviewed isolated misplaced cap remains excluded; no reuse as anatomy.'}]
 receipt={'status':'started','source_sha256':SRC_SHA,'alignment_analysis_sha256':sha(E/'alignment-analysis.json'),'alignment':alignment,'script_sha256':sha(Path(__file__)),'scope':'Source-authored glove geometry and weights, one whole-mesh measured transform; no Unity or motion acceptance','original_hashes':original,'prior_candidate_hashes':prior,'warnings':['Measured similarity fit is not an exact recovered source bind transform.','Existing CTS rest transforms are preserved, including malformed terminal finger reference tails.','Grip is synthetic articulation stress, not an accepted gameplay animation.']}
 EVID.mkdir(parents=True);OUT.mkdir(parents=True)
 def snap(fx,meshes,arm):
  nonlocal done
  if not done:
   obj=next((o for o in meshes if o.name=='Object_94'),None)
   if obj is None or obj.data.name!='GlovesLowPoly_gloves_0' or len(obj.data.vertices)!=2400 or [m.name for m in obj.data.materials]!=['gloves']:raise ValueError('Authored glove identity differs')
   receipt['glove_source']={'object':obj.name,'mesh':obj.data.name,'vertices':len(obj.data.vertices),'triangles':sum(len(p.vertices)-2 for p in obj.data.polygons),'materials':[m.name for m in obj.data.materials], 'groups':[g.name for g in obj.vertex_groups]}
   before=[[(w.group,w.weight) for w in v.groups] for v in obj.data.vertices]
   local=obj.matrix_world.inverted()@transform@obj.matrix_world
   obj.data.transform(local)
   bm=bmesh.new();bm.from_mesh(obj.data);bmesh.ops.reverse_faces(bm,faces=list(bm.faces));bm.to_mesh(obj.data);bm.free();obj.data.update()
   after=[[(w.group,w.weight) for w in v.groups] for v in obj.data.vertices]
   if before!=after:raise ValueError('Alignment changed authored glove weights')
   done=True
  oldsnap(fx,meshes,arm)
 def adapter(base):
  arm,meshes=oldadapter(base);glove=next((o for o in meshes if o.name=='Object_94'),None)
  if glove is None or len(glove.data.vertices)!=2400:raise ValueError('Adapter removed authored glove geometry')
  if any('mitten' in o.name.lower() for o in meshes):raise ValueError('Placeholder hands still present')
  receipt['cts_pre_normalization']=rig_record(arm);receipt['glove_mapped_groups']={g.name:sum(w.weight for v in glove.data.vertices for w in v.groups if w.group==g.index) for g in glove.vertex_groups if any(w.group==g.index and w.weight>0 for v in glove.data.vertices for w in v.groups)}
  receipt['post_adapter_glove_vertices']=len(glove.data.vertices);return arm,meshes
 try:
  S.BASES['hero_tactical']=spec;A.snap_mesh_to_bones=snap;A.ADAPTERS['hero']=adapter;B.EXPORT=OUT;B.EVID=EVID
  B.build_base('hero_tactical')
  import v2_render as R
  for label,export,evid in [('prior',C.V2/'candidates/tactical-island-20261009/export',C.REPO.parent/'ct-life-evidence/claude-continuation-20261009/cast-reconcile/tactical-candidate-1'),('candidate',OUT,EVID)]:
   R.EXPORT=export;R.EVID=evid;sc,cam=R.reset();arm,meshes=R.load_base('hero_tactical');meta=json.loads((export/'bases/hero_tactical/hero_tactical.json').read_text());material=R.toon_material(label,meta,{})
   for o in meshes:o.data.materials.clear();o.data.materials.append(material)
   if label=='prior':receipt['prior_cts_rest']=rig_record(arm)
   else:receipt['cts_saved_rest']=rig_record(arm)
   R.render_cell(sc,cam,arm,meshes,'body',EVID/f'{label}-rest-body.png')
   render_hands(R,arm,meshes,sc,cam,label+'-open')
   if label=='candidate':
    apply_grip(arm);render_hands(R,arm,meshes,sc,cam,label+'-grip')
    receipt['grip_pose_bones']={b.name:[list(row) for row in b.matrix] for b in arm.pose.bones if any(s in b.name for s in ['thumb','index','middle','ring','pinky'])}
  # Rest comparison is exact native float data; both builders used unchanged CTS mapping.
  receipt['cts_rest_matches_prior']=receipt['cts_saved_rest']==receipt['prior_cts_rest']
  if not receipt['cts_rest_matches_prior']:raise ValueError('CTS rest differs from prior candidate')
  receipt['status']='exported_alignment_candidate_visual_review_required'
 except Exception as ex:
  receipt['status']='failed';receipt['error']=type(ex).__name__+': '+str(ex);raise
 finally:
  S.BASES['hero_tactical']=oldspec;A.snap_mesh_to_bones=oldsnap;A.ADAPTERS['hero']=oldadapter
  receipt['original_drift']=[p for p,h in original.items() if not Path(p).exists() or sha(Path(p))!=h];receipt['prior_candidate_drift']=[p for p,h in prior.items() if not Path(p).exists() or sha(Path(p))!=h]
  receipt['candidate_hashes']={str(p.relative_to(OUT)):sha(p) for p in OUT.rglob('*') if p.is_file()};(EVID/'receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
  print('CT_HANDS_END',os.getpid(),time.time(),receipt['status'],flush=True)
if __name__=='__main__':main()
