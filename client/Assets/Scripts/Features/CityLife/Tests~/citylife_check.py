#!/usr/bin/env python3
"""Compile CTLife.Features.CityLife + its EditMode tests out of the editor, reusing client/Tools~/compile_check.py
(read-only use). Output goes to $CTLIFE_CITYLIFE_OUT (default ~/.cache/ctlife-citylife-tests), never to the shared cache.

  --head-deps   compile the dependency assemblies from `git HEAD` (when someone's uncommitted work breaks them).
  --release     player pass without DEVELOPMENT_BUILD, with CTLIFE_PROD.
  --editor      compile CityLife with editor defines too (what the Unity editor compiles).
  --head-unit X compile only dependency unit X (e.g. CTLife.Audio) from `git HEAD` while another agent is mid-edit there."""
import importlib.util, io, pathlib, shutil, subprocess, sys, tarfile, os
REPO = pathlib.Path(__file__).resolve().parents[6]
spec = importlib.util.spec_from_file_location("cc", REPO / "client/Tools~/compile_check.py")
cc = importlib.util.module_from_spec(spec); spec.loader.exec_module(cc)
CITY = REPO / "client/Assets/Scripts/Features/CityLife"
NUNIT = next((REPO / "client/Library/PackageCache").glob("com.unity.ext.nunit@*/net40/unity-custom/nunit.framework.dll"))
REFS = ["CTLife.Core", "CTLife.Net", "CTLife.Generated", "CTLife.World", "CTLife.UI", "ColyseusSDK"]


def run_pass(label, editor):
    newtonsoft = str(cc.CACHE / ("tgz/com.unity.nuget.newtonsoft-json-3.2.2/package/Runtime/" + ("Newtonsoft.Json.dll" if editor else "AOT/Newtonsoft.Json.dll")))
    units = [u for u in cc.units(editor) if u.name.startswith(("UnityEngine.UI", "Unity.", "UniTask", "colyseus", "ColyseusSDK", "LiveKit",
                                                                 "CTLife.Generated", "CTLife.Core", "CTLife.Net", "CTLife.Audio",
                                                                 "CTLife.World", "CTLife.UI", "com.solana", "UnityGifDecoder"))
             and u.name not in ("Unity.Addressables.Editor",)]
    units.append(cc.Unit("CTLife.Features.CityLife", [CITY], refs=REFS, dlls=[newtonsoft], editor=editor))
    if label == "player" and any((CITY / "Tests").glob("*.cs")):
        units.append(cc.Unit("CTLife.Features.CityLife.Tests", [CITY / "Tests"], refs=["CTLife.Features.CityLife", "CTLife.Core", "CTLife.Net",
                             "CTLife.Generated"], dlls=[newtonsoft, str(NUNIT)], editor=True, override=True))
    for name in HEAD_UNITS:
        for u in units:
            if u.name == name:
                u.roots = [HEAD_SNAP / pathlib.Path(r).relative_to(REPO) for r in u.roots]
                print("NOTE:", name, "compiled from git HEAD")
    built = {}
    for u in units:
        if not cc.compile_unit(u, built, label) and u.name.startswith("CTLife"):
            return False
    return True


HEAD_UNITS = [sys.argv[i + 1] for i, a in enumerate(sys.argv[:-1]) if a == "--head-unit"]
HEAD_SNAP = None


def main():
    global HEAD_SNAP
    cc.fetch()
    cc.RELEASE = "--release" in sys.argv
    cc.OUT = pathlib.Path(os.environ.get("CTLIFE_CITYLIFE_OUT", pathlib.Path.home() / ".cache/ctlife-citylife-tests"))
    shutil.rmtree(cc.OUT, ignore_errors=True)
    if "--head-deps" in sys.argv:
        snap = cc.OUT / "headsnap"
        data = subprocess.run(["git", "-C", str(REPO), "archive", "HEAD", "client/Assets/Scripts", "client/Assets/Generated"],
                              capture_output=True, check=True).stdout
        tarfile.open(fileobj=io.BytesIO(data)).extractall(snap)
        cc.ASSETS = snap / "client/Assets"
        print("NOTE: dependencies compiled from git HEAD")
    if HEAD_UNITS:
        HEAD_SNAP = cc.OUT / "headunits"
        data = subprocess.run(["git", "-C", str(REPO), "archive", "HEAD", "client/Assets/Scripts"], capture_output=True, check=True).stdout
        tarfile.open(fileobj=io.BytesIO(data)).extractall(HEAD_SNAP)
    ok = run_pass("player", False)
    if ok and "--editor" in sys.argv:
        ok = run_pass("editor", True)
    n_warn = sum(len(w) for k, w in cc.WARNINGS.items() if "CityLife" in k)
    print(f"WARNINGS: {n_warn} in CTLife.Features.CityLife*")
    print("RESULT: ok" if ok else "RESULT: errors", cc.OUT / "player")
    sys.exit(0 if ok else 1)


main()
