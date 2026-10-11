#!/bin/bash
# Runs the CTLife.Features.Money EditMode tests WITHOUT the Unity editor: compiles with the editor's csc
# (money_check.py), then executes every [Test]/[TestCase] with a small reflection runner (Runner.cs) on the editor's
# bundled .NET 6 runtime. The tests are pure logic (no UnityEngine calls), so this matches the EditMode result;
# the Unity Test Runner remains the reference. Usage: run_money_tests.sh [--head-deps]
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
U=/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/Resources/Scripting
OUT="${CTLIFE_MONEY_OUT:-$HOME/.cache/ctlife-money-tests}"
python3 "$HERE/money_check.py" "$@" | grep -E "NOTE|CTLife\.|error|RESULT"
R="$OUT/run"; rm -rf "$R"; mkdir -p "$R"
cp "$OUT"/player/CTLife.*.dll "$R/"
cp "$(ls -d "$HERE"/../../../../../Library/PackageCache/com.unity.ext.nunit@*/net40/unity-custom | head -1)/nunit.framework.dll" "$R/"
cp "$HOME/.cache/ctlife-compile-check/tgz/com.unity.nuget.newtonsoft-json-3.2.2/package/Runtime/Newtonsoft.Json.dll" "$R/"
"$U/NetCoreRuntime/dotnet" exec "$U/DotNetSdkRoslyn/csc.dll" -nologo -noconfig -nostdlib+ -target:exe -out:"$R/Runner.dll" \
  -r:"$U/NetStandard/ref/2.1.0/netstandard.dll" "$HERE/Runner.cs"
echo '{ "runtimeOptions": { "tfm": "net6.0", "framework": { "name": "Microsoft.NETCore.App", "version": "6.0.21" } } }' > "$R/Runner.runtimeconfig.json"
cd "$R" && "$U/NetCoreRuntime/dotnet" Runner.dll "$R/CTLife.Features.Money.Tests.dll"
