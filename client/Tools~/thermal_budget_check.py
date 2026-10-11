#!/usr/bin/env python3
"""Run production thermal-policy transitions without Unity/ADB or a heated device."""
import json
from pathlib import Path
import subprocess
import tempfile

repo = Path(__file__).resolve().parents[2]
scripting = Path('/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/Resources/Scripting')
runtime = sorted((scripting / 'NetCoreRuntime/shared/Microsoft.NETCore.App').iterdir())[-1]
with tempfile.TemporaryDirectory(prefix='ct-thermal-check-') as temporary:
    work = Path(temporary)
    response = ['-nologo', '-nostdlib+', '-target:exe', '-langversion:9.0', f'-out:"{work}/checks.dll"']
    response += [f'-r:"{path}"' for path in runtime.glob('*.dll')]
    response += [f'"{repo}/client/Tools~/checks/ThermalBudgetChecks.cs"',
                 f'"{repo}/client/Assets/Scripts/World/Perf/ThermalBudget.cs"']
    (work / 'checks.rsp').write_text('\n'.join(response))
    (work / 'checks.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {
        'tfm': 'net' + '.'.join(runtime.name.split('.')[:2]),
        'framework': {'name': 'Microsoft.NETCore.App', 'version': runtime.name}}}))
    dotnet = str(scripting / 'NetCoreRuntime/dotnet')
    subprocess.run([dotnet, 'exec', str(scripting / 'DotNetSdkRoslyn/csc.dll'),
                    '-noconfig', '@' + str(work / 'checks.rsp')], check=True)
    subprocess.run([dotnet, str(work / 'checks.dll')], check=True)
