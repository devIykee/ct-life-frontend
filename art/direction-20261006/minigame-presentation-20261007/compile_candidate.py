"""Private compiler only. Every compiler output is outside the Unity project."""
from pathlib import Path
import re,json,subprocess,tempfile,hashlib
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[2];CLIENT=ROOT/'client'
OUT=Path(tempfile.mkdtemp(prefix='ct-clip-bench-compile-'))
SCRIPT=Path('/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/Resources/Scripting')
DOTNET=SCRIPT/'NetCoreRuntime/dotnet';CSC=SCRIPT/'DotNetSdkRoslyn/csc.dll';records=[]
outputs=re.compile(r'^[-/](out|refout|pdb|errorlog|generatedfilesout|doc):',re.I)
source_map={
 'Assets/Scripts/UI/Phone/Apps/ClipCut.cs':HERE/'gameplay-change/ClipCut.cs',
 'Assets/Scripts/UI/Text/ClipCutStrings.cs':HERE/'gameplay-change/ClipCutStrings.cs',
 'Assets/Scripts/App/Review/ClipCutPresentationReview.cs':HERE/'review/ClipCutPresentationReview.cs'}
new_sources={'CTLife.UI':[HERE/'runtime/UI/Phone/Apps/ClipCutStoryboard.cs'],'CTLife.Editor':[HERE/'editor/ClipCutStoryboardImport.cs']}
def compile_one(mode,assembly,dag,ui_dependency=None,actual=False,production=False):
 dst=OUT/mode;dst.mkdir(exist_ok=True);rsp=CLIENT/'Library/Bee/artifacts'/dag/(assembly+'.rsp')
 args=[];removed=[];replaced=[]
 for line in rsp.read_text().splitlines():
  raw=line.strip();plain=raw.strip('"')
  if raw.startswith('@'):raise RuntimeError('Nested response file refused')
  if outputs.match(raw):removed.append(raw);continue
  if production and raw.startswith('-define:'):
   # Same exact current UI source/references, player-only conditional branches.
   define=raw.split(':',1)[1]
   if define.startswith('UNITY_EDITOR') or define in ('DEVELOPMENT_BUILD','UNITY_INCLUDE_TESTS'):continue
  if plain in source_map:line='"'+str(source_map[plain])+'"';replaced.append(plain)
  if raw.startswith(('-r:','/r:','-reference:','/reference:')):
   ref=Path(raw.split(':',1)[1].strip('"'))
   if ref.name in ('CTLife.UI.dll','CTLife.UI.ref.dll') and ui_dependency:line='-r:"'+str(ui_dependency)+'"'
   elif actual and ref.name.startswith('CTLife.') and ref.name.endswith('.ref.dll'):
    actual_path=CLIENT/ref.with_name(ref.name.replace('.ref.dll','.dll'))
    assert actual_path.is_file(),actual_path
    line='-r:"'+str(actual_path)+'"'
  args.append(line)
 if production:args.append('-define:CTLIFE_PROD')
 args.extend('"'+str(x)+'"' for x in new_sources.get(assembly,[]))
 args.append('-out:"'+str(dst/(assembly+'.dll'))+'"')
 assert [a for a in args if outputs.match(a)]==[args[-1]]
 target=dst/(assembly+'.rsp');target.write_text('\n'.join(args)+'\n')
 result=subprocess.run([str(DOTNET),str(CSC),'@'+str(target)],cwd=CLIENT,text=True,capture_output=True)
 log=dst/(assembly+'.log');log.write_text(result.stdout+result.stderr)
 row={'mode':mode,'assembly':assembly,'exit_code':result.returncode,'source_response':str(rsp),'removed_outputs':removed,'sole_output':args[-1],'replaced_sources':replaced,'added_sources':[str(x) for x in new_sources.get(assembly,[])],'log':str(log)};records.append(row)
 print(mode,assembly,result.returncode,flush=True)
 if result.returncode:
  print(result.stdout+result.stderr);save('failed');raise SystemExit(result.returncode)
 return dst/(assembly+'.dll')
def save(status):
 (HERE/'compile-receipt.json').write_text(json.dumps({'status':status,'records':records,'scope':'Private original assembly scopes. All inherited outputs removed; no live/native Unity or layout proof.'},indent=2)+'\n')
ui=compile_one('editor-ui','CTLife.UI','1300b0aE.dag')
compile_one('editor-app-reference','CTLife.App','1300b0aE.dag',ui_dependency=ui)
compile_one('editor-app-actual','CTLife.App','1300b0aE.dag',ui_dependency=ui,actual=True)
compile_one('editor-importer','CTLife.Editor','1300b0aE.dag',ui_dependency=ui)
compile_one('development-ui','CTLife.UI','1300b0aPDevDbg.dag')
compile_one('production-ui','CTLife.UI','1300b0aPDevDbg.dag',production=True)
save('passed_private_source_compile_only')
