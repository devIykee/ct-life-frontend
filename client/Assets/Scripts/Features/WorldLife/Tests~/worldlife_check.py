#!/usr/bin/env python3
"""Compile CTLife.Features.WorldLife + its EditMode tests out of the editor, reusing client/Tools~/compile_check.py
(read-only use). Output goes to $CTLIFE_WORLDLIFE_OUT (default ~/.cache/ctlife-worldlife-tests), never to the shared cache.

  --editor      also compile the assembly with editor defines (what the Unity editor compiles)
  --release     player pass without DEVELOPMENT_BUILD, with CTLIFE_PROD
  --head-deps   compile the dependency assemblies from `git HEAD` (when someone's uncommitted work breaks them)."""
import importlib.util, io, os, pathlib, shutil, subprocess, sys, tarfile
REPO = pathlib.Path(__file__).resolve().parents[6]
spec = importlib.util.spec_from_file_location("cc", REPO / "client/Tools~/compile_check.py")
cc = importlib.util.module_from_spec(spec); spec.loader.exec_module(cc)
LANE = REPO / "client/Assets/Scripts/Features/WorldLife"
NUNIT = next((REPO / "client/Library/PackageCache").glob("com.unity.ext.nunit@*/net40/unity-custom/nunit.framework.dll"))
REFS = ["CTLife.Core", "CTLife.Net", "CTLife.Generated", "CTLife.World", "CTLife.Audio", "ColyseusSDK"]


def run(label, editor):
    cc.fetch()
    newtonsoft = str(cc.CACHE / ("tgz/com.unity.nuget.newtonsoft-json-3.2.2/package/Runtime/" + ("Newtonsoft.Json.dll" if editor else "AOT/Newtonsoft.Json.dll")))
    # Only the assemblies this lane references (other lanes' units come and go in compile_check.py).
    keep = {"CTLife.Generated", "CTLife.Core", "CTLife.Net", "CTLife.Audio", "CTLife.World"}
    units = [u for u in cc.units(False) if not u.name.startswith("CTLife") or u.name in keep]
    if editor:
        for u in units:
            u.editor = True
            u.dlls = [newtonsoft if "Newtonsoft" in d else d for d in u.dlls]
    units.append(cc.Unit("CTLife.Features.WorldLife", [LANE], refs=REFS, dlls=[newtonsoft], editor=editor))
    if label == "player":
        units.append(cc.Unit("CTLife.Features.WorldLife.Tests", [LANE / "Tests"], refs=["CTLife.Features.WorldLife"],
                             dlls=[newtonsoft, str(NUNIT)], editor=True, override=True))
    built = {}
    for u in units:
        if not cc.compile_unit(u, built, label) and u.name.startswith("CTLife"):
            return False
    return True


def main():
    cc.OUT = pathlib.Path(os.environ.get("CTLIFE_WORLDLIFE_OUT", pathlib.Path.home() / ".cache/ctlife-worldlife-tests"))
    shutil.rmtree(cc.OUT, ignore_errors=True)
    if "--head-deps" in sys.argv:
        snap = cc.OUT / "headsnap"
        data = subprocess.run(["git", "-C", str(REPO), "archive", "HEAD", "client/Assets/Scripts", "client/Assets/Generated"],
                              capture_output=True, check=True).stdout
        tarfile.open(fileobj=io.BytesIO(data)).extractall(snap)
        cc.ASSETS = snap / "client/Assets"
        print("NOTE: dependencies compiled from git HEAD")
    cc.RELEASE = "--release" in sys.argv
    ok = run("player", False)
    if ok and "--editor" in sys.argv:
        ok = run("editor", True)
    n = sum(len(w) for k, w in cc.WARNINGS.items() if "WorldLife" in k)
    print(f"WARNINGS (WorldLife): {n}")
    print("RESULT: ok" if ok else "RESULT: errors", cc.OUT / "player")
    sys.exit(0 if ok else 1)

main()
