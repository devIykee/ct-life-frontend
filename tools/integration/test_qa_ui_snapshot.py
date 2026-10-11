#!/usr/bin/env python3
"""Execute pure snapshot NUnit tests using bundled Roslyn/.NET, without launching Unity.
Unity adapter tests must still run in EditMode. Output lives in TMPDIR, not Assets.
"""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
UNITY = Path('/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/Resources')
SCRIPTING = UNITY / 'Scripting'
DOTNET = SCRIPTING / 'NetCoreRuntime/dotnet'
CSC = SCRIPTING / 'DotNetSdkRoslyn/csc.dll'
RUNTIME = SCRIPTING / 'NetCoreRuntime/shared/Microsoft.NETCore.App/6.0.21'
NUNIT = UNITY / 'PackageManager/BuiltInPackages/com.unity.ext.nunit/net40/unity-custom/nunit.framework.dll'
JSON = Path.home() / '.cache/ctlife-compile-check/tgz/com.unity.nuget.newtonsoft-json-3.2.2/package/Runtime/Newtonsoft.Json.dll'


def main():
    with tempfile.TemporaryDirectory(prefix='qa-ui-', dir=os.environ['TMPDIR']) as folder:
        out = Path(folder)
        for lib in (NUNIT, JSON):
            shutil.copy(lib, out / lib.name)
        refs = ['-r:' + str(p) for p in RUNTIME.glob('*.dll')]
        refs += ['-r:' + str(out / lib.name) for lib in (NUNIT, JSON)]
        def compile(name, paths, target, extra=()):
            subprocess.run([str(DOTNET), 'exec', str(CSC), '-nologo', '-noconfig',
                            '-nostdlib+', '-define:UNITY_EDITOR', '-target:' + target,
                            '-out:' + str(out / name), *refs, *extra,
                            *map(str, paths)], check=True)
        data = ROOT / 'client/Assets/Scripts/UI/Dev/QaUiSnapshotData.cs'
        empty = out / 'Empty.cs'
        empty.write_text('namespace CTLife.UI.Dev {}')
        compile('CTLife.UI.dll', [data if data.exists() else empty], 'library')
        runner = out / 'Runner.cs'
        runner.write_text('''using System;
using System.Reflection;
class Runner {
 static int Main() {
  int failed = 0, passed = 0;
  foreach(var t in Assembly.GetExecutingAssembly().GetTypes())
   foreach(var m in t.GetMethods())
    if(m.GetCustomAttribute<NUnit.Framework.TestAttribute>() != null) {
     try { m.Invoke(Activator.CreateInstance(t), null); Console.WriteLine("PASS " + m.Name); passed++; }
     catch(Exception e) { Console.WriteLine("FAIL " + m.Name + ": " + (e.InnerException ?? e).Message); failed++; }
    }
  Console.WriteLine($"{passed} passed, {failed} failed");
  return failed == 0 ? 0 : 1;
 }
}''')
        compile('Runner.dll', [runner, ROOT / 'client/Assets/Tests/EditMode/QaUiSnapshotDataTests.cs'],
                'exe', ['-r:' + str(out / 'CTLife.UI.dll')])
        (out / 'Runner.runtimeconfig.json').write_text(json.dumps({
            'runtimeOptions': {'tfm': 'net6.0', 'framework': {'name': 'Microsoft.NETCore.App', 'version': '6.0.21'}}}))
        return subprocess.run([str(DOTNET), str(out / 'Runner.dll')]).returncode


if __name__ == '__main__':
    raise SystemExit(main())
