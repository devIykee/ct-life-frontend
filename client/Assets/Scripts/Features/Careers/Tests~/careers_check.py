#!/usr/bin/env python3
"""Compile CTLife.Features.Careers + its EditMode tests out of the editor, reusing client/Tools~/compile_check.py
(read-only use). Output goes to $CTLIFE_CAREERS_OUT (default ~/.cache/ctlife-careers-tests), never the shared cache.

  --head-deps   compile the dependency assemblies from `git HEAD` (when someone's uncommitted work breaks them).
  --release     player pass without DEVELOPMENT_BUILD and with CTLIFE_PROD.
  --editor      compile every unit with editor defines (what the Unity editor and the Test Runner compile)."""
import importlib.util, io, pathlib, shutil, subprocess, sys, tarfile, os
REPO = pathlib.Path(__file__).resolve().parents[6]
spec = importlib.util.spec_from_file_location("cc", REPO / "client/Tools~/compile_check.py")
cc = importlib.util.module_from_spec(spec); spec.loader.exec_module(cc)
CAREERS = REPO / "client/Assets/Scripts/Features/Careers"
NUNIT = next((REPO / "client/Library/PackageCache").glob("com.unity.ext.nunit@*/net40/unity-custom/nunit.framework.dll"))

def main():
    cc.fetch()
    cc.RELEASE = "--release" in sys.argv
    editor = "--editor" in sys.argv
    cc.OUT = pathlib.Path(os.environ.get("CTLIFE_CAREERS_OUT", pathlib.Path.home() / ".cache/ctlife-careers-tests"))
    shutil.rmtree(cc.OUT, ignore_errors=True)
    if "--head-deps" in sys.argv:
        snap = cc.OUT / "headsnap"
        data = subprocess.run(["git", "-C", str(REPO), "archive", "HEAD", "client/Assets/Scripts", "client/Assets/Generated"],
                              capture_output=True, check=True).stdout
        tarfile.open(fileobj=io.BytesIO(data)).extractall(snap)
        cc.ASSETS = snap / "client/Assets"
        print("NOTE: dependencies compiled from git HEAD")
    deps = {"UnityEngine.UI", "Unity.InternalAPIEngineBridge.004", "Unity.TextMeshPro", "UniTask", "UniTask.Linq", "UnityGifDecoder",
            "com.solana.unity_sdk", "colyseus.nativewebsocket", "ColyseusSDK", "LiveKit", "Unity.Profiling.Core", "Unity.ResourceManager",
            "Unity.Addressables", "Unity.RenderPipelines.Universal.Runtime", "CTLife.Generated", "CTLife.Core", "CTLife.Net", "CTLife.Audio",
            "CTLife.World", "CTLife.UI"}
    units = [u for u in cc.units(False) if u.name in deps]
    if editor:
        for u in units: u.editor = True
    newtonsoft_aot = str(cc.CACHE / "tgz/com.unity.nuget.newtonsoft-json-3.2.2/package/Runtime/AOT/Newtonsoft.Json.dll")
    newtonsoft = str(cc.CACHE / "tgz/com.unity.nuget.newtonsoft-json-3.2.2/package/Runtime/Newtonsoft.Json.dll") if editor else newtonsoft_aot
    if editor:
        for u in units: u.dlls = [newtonsoft if d == newtonsoft_aot else d for d in u.dlls]
    units.append(cc.Unit("CTLife.Features.Careers", [CAREERS], refs=["CTLife.Core", "CTLife.Net", "CTLife.Generated", "CTLife.UI"], dlls=[newtonsoft], editor=editor))
    units.append(cc.Unit("CTLife.Features.Careers.Tests", [CAREERS / "Tests"], refs=["CTLife.Features.Careers", "CTLife.Core", "CTLife.Net",
                         "CTLife.Generated"], dlls=[newtonsoft, str(NUNIT)], editor=True, override=True))
    built = {}
    label = "player"
    for u in units:
        if not cc.compile_unit(u, built, label) and u.name.startswith("CTLife"):
            print("RESULT: errors"); sys.exit(1)
    n_warn = sum(len(w) for k, w in cc.WARNINGS.items() if "Careers" in k)
    print(f"WARNINGS in Careers assemblies: {n_warn}")
    print("RESULT: ok", cc.OUT / label)

main()
