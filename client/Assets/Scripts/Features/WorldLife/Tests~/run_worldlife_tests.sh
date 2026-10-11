#!/bin/bash
# Runs the CTLife.Features.WorldLife EditMode tests WITHOUT the Unity editor: compiles with the editor's csc
# (worldlife_check.py), then executes every [Test]/[TestCase] with a small reflection runner (Runner.cs, copied from the
# Money lane) on the editor's bundled .NET 6 runtime. The tested code is the pure Logic/ layer (no UnityEngine calls) and
# the presentation JSON in Resources/WorldLife; the Unity Test Runner remains the reference.
# Usage: run_worldlife_tests.sh [--head-deps]
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
U=/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/Resources/Scripting
OUT="${CTLIFE_WORLDLIFE_OUT:-$HOME/.cache/ctlife-worldlife-tests}"
python3 "$HERE/worldlife_check.py" "$@" | grep -E "NOTE|CTLife\.|error|warning|RESULT|WARNINGS"
R="$OUT/run"; rm -rf "$R"; mkdir -p "$R"
cp "$OUT"/player/CTLife.*.dll "$R/"
cp "$(ls -d "$HERE"/../../../../../Library/PackageCache/com.unity.ext.nunit@*/net40/unity-custom | head -1)/nunit.framework.dll" "$R/"
cp "$HOME/.cache/ctlife-compile-check/tgz/com.unity.nuget.newtonsoft-json-3.2.2/package/Runtime/Newtonsoft.Json.dll" "$R/"
"$U/NetCoreRuntime/dotnet" exec "$U/DotNetSdkRoslyn/csc.dll" -nologo -noconfig -nostdlib+ -target:exe -out:"$R/Runner.dll" \
  -r:"$U/NetStandard/ref/2.1.0/netstandard.dll" "$HERE/Runner.cs"
echo '{ "runtimeOptions": { "tfm": "net6.0", "framework": { "name": "Microsoft.NETCore.App", "version": "6.0.21" } } }' > "$R/Runner.runtimeconfig.json"
export CTLIFE_WORLDLIFE_DATA="$HERE/../Resources/WorldLife"
export CTLIFE_CONTRACT_DIR="$HERE/../../../../../../contract"
cd "$R" && "$U/NetCoreRuntime/dotnet" Runner.dll "$R/CTLife.Features.WorldLife.Tests.dll"
