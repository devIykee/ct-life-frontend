#!/bin/bash
# Runs the CTLife.Audio EditMode tests WITHOUT the Unity editor.
#  1. Compiles Audio/Logic/*.cs (pure C#, no UnityEngine) as CTLife.Audio.dll with the editor's bundled Roslyn
#     against .NET Standard 2.1, exactly the sources the player compiles inside the CTLife.Audio assembly.
#  2. Compiles Audio/Tests/*.cs (NUnit, the same files the Unity Test Runner runs) against it.
#  3. Executes every [Test]/[TestCase] with Runner.cs on the editor's bundled .NET 6 runtime.
# The Unity Test Runner (Window → General → Test Runner → EditMode → CTLife.Audio.Tests) remains the reference.
# Usage: client/Assets/Scripts/Audio/Tests~/run_audio_tests.sh
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
AUDIO="$(cd "$HERE/.." && pwd)"
CLIENT="$(cd "$AUDIO/../../.." && pwd)"
U=/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/Resources/Scripting
DOTNET="$U/NetCoreRuntime/dotnet"
CSC="$U/DotNetSdkRoslyn/csc.dll"
NETSTD="$U/NetStandard/ref/2.1.0/netstandard.dll"
NUNIT="$(ls -d "$CLIENT"/Library/PackageCache/com.unity.ext.nunit@*/net40/unity-custom 2>/dev/null | head -1)/nunit.framework.dll"
OUT="${CTLIFE_AUDIO_OUT:-$HOME/.cache/ctlife-audio-tests}/run-$$"
for f in "$DOTNET" "$CSC" "$NETSTD" "$NUNIT"; do [ -e "$f" ] || { echo "missing $f"; exit 2; }; done
rm -rf "$OUT"; mkdir -p "$OUT"
trap 'rm -rf "$OUT"' EXIT

# NUnit (unity-custom) is a .NET Framework build: reference the netfx facades (mscorlib, System…) like Unity does.
SHIMS=()
for d in "$U"/NetStandard/compat/2.1.0/shims/netfx/*.dll "$U"/NetStandard/compat/2.1.0/shims/netstandard/*.dll; do SHIMS+=("-r:$d"); done
csc() { "$DOTNET" exec "$CSC" -nologo -noconfig -nostdlib+ -langversion:9.0 -warn:4 -nowarn:CS1701,CS1702 "${SHIMS[@]}" "$@"; }

echo "compile CTLife.Audio (Logic only, no UnityEngine)"
csc -target:library -out:"$OUT/CTLife.Audio.dll" -r:"$NETSTD" "$AUDIO"/Logic/*.cs
echo "compile CTLife.Audio.Tests"
csc -target:library -out:"$OUT/CTLife.Audio.Tests.dll" -r:"$NETSTD" -r:"$NUNIT" -r:"$OUT/CTLife.Audio.dll" "$AUDIO"/Tests/*.cs
echo "compile runner"
csc -target:exe -out:"$OUT/Runner.dll" -r:"$NETSTD" "$HERE/Runner.cs"
cp "$NUNIT" "$OUT/"
echo '{ "runtimeOptions": { "tfm": "net6.0", "framework": { "name": "Microsoft.NETCore.App", "version": "6.0.21" } } }' > "$OUT/Runner.runtimeconfig.json"
cd "$OUT" && "$DOTNET" Runner.dll "$OUT/CTLife.Audio.Tests.dll"
