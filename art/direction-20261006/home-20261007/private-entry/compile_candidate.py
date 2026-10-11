"""Compile staged replacements using actual assembly inputs, redirecting every output outside Unity's project."""
from pathlib import Path
import subprocess,json,hashlib
ROOT=Path(__file__).resolve().parents[4]; HERE=Path(__file__).resolve().parent;CLIENT=ROOT/'client';OUT=Path('/tmp/ct-home-private-entry-compile');OUT.mkdir(exist_ok=True)
UNITY=Path('/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/Resources/Scripting');dotnet=UNITY/'NetCoreRuntime/dotnet';csc=UNITY/'DotNetSdkRoslyn/csc.dll'
rows=[]
for mode,dag in [('editor','1300b0aE.dag'),('development','1300b0aPDevDbg.dag'),('player','1300b0aP.dag')]:
 dst=OUT/mode;dst.mkdir(exist_ok=True)
 assemblies=['CTLife.Core','CTLife.Generated','CTLife.Net','CTLife.Audio','CTLife.Solana','CTLife.World','CTLife.UI','CTLife.App'] if mode=='player' else ['CTLife.World','CTLife.UI','CTLife.App']
 for assembly in assemblies:
  source=CLIENT/'Library/Bee/artifacts'/dag/(assembly+'.rsp');args=[];removed=[];replacements=[];added_sources=[]
  source_lines=source.read_text().splitlines()
  if mode=='player':
   # The cached release RSP predates current live files. Retain its release defines/references,
   # and supplement only source paths present in the current native development assembly.
   current=CLIENT/'Library/Bee/artifacts/1300b0aPDevDbg.dag'/(assembly+'.rsp')
   existing={x.strip().strip(chr(34)) for x in source_lines if x.strip().strip(chr(34)).endswith('.cs')}
   for item in current.read_text().splitlines():
    name=item.strip().strip(chr(34))
    if name.endswith('.cs') and name not in existing:
     assert (CLIENT/name).is_file(),name
     source_lines.append(item);added_sources.append(name);existing.add(name)
  for line in source_lines:
   stripped=line.strip();lower=stripped.lower()
   if stripped.startswith('@'):raise RuntimeError('Nested response files are not allowed')
   if any(lower.startswith(x)for x in ['-out:','/out:','-refout:','/refout:','-pdb:','/pdb:','-errorlog:','/errorlog:','-generatedfilesout:','/generatedfilesout:','-doc:','/doc:']):removed.append(line);continue
   if stripped.strip('"').endswith('.cs'):
    old=Path(stripped.strip('"'))
    if str(old).startswith('Assets/Scripts/'):
     candidate=HERE/'runtime'/old.relative_to('Assets/Scripts')
     if candidate.exists():replacements.append(str(old));line='"'+str(candidate)+'"'
   if lower.startswith(('-r:','/r:','-reference:','/reference:')):
    ref=Path(stripped.split(':',1)[1].strip('"'))
    for dependency in (assemblies[:assemblies.index(assembly)]):
     if assembly!=dependency and ref.name in [dependency+'.dll',dependency+'.ref.dll']:
      if not (dst/(dependency+'.dll')).exists():raise RuntimeError('Private dependency not compiled')
      line='-r:"'+str(dst/(dependency+'.dll'))+'"'
   args.append(line)
  args.append('-out:"'+str(dst/(assembly+'.dll'))+'"')
  outputs=[a for a in args if any(a.lower().startswith(x) for x in ['-out:','/out:','-refout:','/refout:','-pdb:','/pdb:','-errorlog:','/errorlog:','-generatedfilesout:','/generatedfilesout:','-doc:','/doc:'])]
  assert outputs==[args[-1]]
  rsp=dst/(assembly+'.rsp');rsp.write_text('\n'.join(args)+'\n')
  run=subprocess.run([str(dotnet),str(csc),'@'+str(rsp)],cwd=CLIENT,text=True,capture_output=True)
  log=dst/(assembly+'.log');log.write_text(run.stdout+run.stderr)
  row={'mode':mode,'assembly':assembly,'exit_code':run.returncode,'source_response':str(source),'removed_output_flags':removed,'sole_output':outputs[0],'replaced_sources':replacements,'supplemental_native_development_sources':added_sources,'log':str(log)};rows.append(row);print(mode,assembly,run.returncode,flush=True)
  if run.returncode:print(run.stdout+run.stderr);(HERE/'compile-receipt.json').write_text(json.dumps({'status':'failed','rows':rows},indent=2)+'\n');raise SystemExit(run.returncode)
(HERE/'compile-receipt.json').write_text(json.dumps({'status':'passed_private_output_compile_not_native_or_lifecycle_acceptance','rows':rows},indent=2)+'\n')
