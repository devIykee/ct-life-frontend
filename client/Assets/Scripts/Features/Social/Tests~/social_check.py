#!/usr/bin/env python3
"""Compile CTLife.Features.Social + its EditMode tests out of the editor, reusing client/Tools~/compile_check.py
(read-only use). Output goes to $CTLIFE_SOCIAL_OUT (default ~/.cache/ctlife-social-tests), never to the shared cache.

  --editor      compile with editor defines (UNITY_EDITOR...) instead of the Android dev player defines.
  --release     player defines without DEVELOPMENT_BUILD, with CTLIFE_PROD.
  --head-deps   compile the dependency assemblies from `git HEAD` (when someone's uncommitted work breaks them)."""
import importlib.util, io, pathlib, shutil, subprocess, sys, tarfile, os
REPO = pathlib.Path(__file__).resolve().parents[6]
spec = importlib.util.spec_from_file_location("cc", REPO / "client/Tools~/compile_check.py")
cc = importlib.util.module_from_spec(spec); spec.loader.exec_module(cc)
SOCIAL = REPO / "client/Assets/Scripts/Features/Social"
NUNIT = next((REPO / "client/Library/PackageCache").glob("com.unity.ext.nunit@*/net40/unity-custom/nunit.framework.dll"))

def main():
    editor = "--editor" in sys.argv
    cc.RELEASE = "--release" in sys.argv
    cc.fetch()
    cc.OUT = pathlib.Path(os.environ.get("CTLIFE_SOCIAL_OUT", pathlib.Path.home() / ".cache/ctlife-social-tests"))
    shutil.rmtree(cc.OUT, ignore_errors=True)
    newtonsoft = str(cc.CACHE / "tgz/com.unity.nuget.newtonsoft-json-3.2.2/package/Runtime" / ("Newtonsoft.Json.dll" if editor else "AOT/Newtonsoft.Json.dll"))
    if "--head-deps" in sys.argv:
        snap = cc.OUT / "headsnap"
        data = subprocess.run(["git", "-C", str(REPO), "archive", "HEAD", "client/Assets/Scripts", "client/Assets/Generated"],
                              capture_output=True, check=True).stdout
        tarfile.open(fileobj=io.BytesIO(data)).extractall(snap)
        cc.ASSETS = snap / "client/Assets"
        print("NOTE: dependencies compiled from git HEAD")
    skip = ("CTLife.App", "CTLife.Solana", "CTLife.Editor", "Unity.Addressables.Editor", "CTLife.Tests.EditMode",
            "CTLife.Tests.Integration", "CTLife.Features.Money.Tests", "CTLife.Features.Social", "CTLife.Features.Social.Tests")
    # Only this assembly's dependencies: other feature assemblies (Money, Careers, CityLife, ...) are skipped so another
    # agent's work in progress cannot fail this check.
    units = [u for u in cc.units(editor) if u.name not in skip and not u.name.startswith("CTLife.Features.")]
    units.append(cc.Unit("CTLife.Features.Social", [SOCIAL], refs=["CTLife.Core", "CTLife.Net", "CTLife.Generated", "CTLife.World", "CTLife.UI",
                                                                  "ColyseusSDK", "LiveKit"], dlls=[newtonsoft], editor=editor))
    units.append(cc.Unit("CTLife.Features.Social.Tests", [SOCIAL / "Tests"], refs=["CTLife.Features.Social", "CTLife.Core", "CTLife.Net",
                         "CTLife.Generated"], dlls=[newtonsoft, str(NUNIT)], editor=True, override=True))
    built = {}
    label = "editor" if editor else "player"
    for u in units:
        if not cc.compile_unit(u, built, label) and u.name.startswith("CTLife"):
            print("RESULT: errors"); sys.exit(1)
    n = sum(len(w) for k, w in cc.WARNINGS.items() if "Social" in k)
    print(f"WARNINGS: {n} in CTLife.Features.Social*")
    print("RESULT: ok", cc.OUT / label)

main()
