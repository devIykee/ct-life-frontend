#!/usr/bin/env python3
"""Compile CTLife.Features.Money + its EditMode tests out of the editor, reusing client/Tools~/compile_check.py
(read-only use). Output goes to $CTLIFE_MONEY_OUT (default ~/.cache/ctlife-money-tests), never to the shared cache.

  --head-deps   compile the dependency assemblies from `git HEAD` (when someone's uncommitted work breaks them)."""
import importlib.util, io, pathlib, shutil, subprocess, sys, tarfile, os
REPO = pathlib.Path(__file__).resolve().parents[6]
spec = importlib.util.spec_from_file_location("cc", REPO / "client/Tools~/compile_check.py")
cc = importlib.util.module_from_spec(spec); spec.loader.exec_module(cc)
MONEY = REPO / "client/Assets/Scripts/Features/Money"
NUNIT = next((REPO / "client/Library/PackageCache").glob("com.unity.ext.nunit@*/net40/unity-custom/nunit.framework.dll"))

def main():
    cc.fetch()
    cc.OUT = pathlib.Path(os.environ.get("CTLIFE_MONEY_OUT", pathlib.Path.home() / ".cache/ctlife-money-tests"))
    shutil.rmtree(cc.OUT, ignore_errors=True)
    newtonsoft = str(cc.CACHE / "tgz/com.unity.nuget.newtonsoft-json-3.2.2/package/Runtime/AOT/Newtonsoft.Json.dll")
    if "--head-deps" in sys.argv:
        snap = cc.OUT / "headsnap"
        data = subprocess.run(["git", "-C", str(REPO), "archive", "HEAD", "client/Assets/Scripts", "client/Assets/Generated"],
                              capture_output=True, check=True).stdout
        tarfile.open(fileobj=io.BytesIO(data)).extractall(snap)
        cc.ASSETS = snap / "client/Assets"
        print("NOTE: dependencies compiled from git HEAD")
    units = [u for u in cc.units(False) if u.name not in ("CTLife.Features.Money", "CTLife.App", "CTLife.Solana")]
    units.append(cc.Unit("CTLife.Features.Money", [MONEY], refs=["CTLife.Core", "CTLife.Net", "CTLife.Generated", "CTLife.World", "CTLife.UI",
                                                                "ColyseusSDK"], dlls=[newtonsoft]))
    units.append(cc.Unit("CTLife.Features.Money.Tests", [MONEY / "Tests"], refs=["CTLife.Features.Money", "CTLife.Core", "CTLife.Net",
                         "CTLife.Generated"], dlls=[newtonsoft, str(NUNIT)], editor=True, override=True))
    built = {}
    for u in units:
        if not cc.compile_unit(u, built, "player") and u.name.startswith("CTLife"):
            print("RESULT: errors"); sys.exit(1)
    print("RESULT: ok", cc.OUT / "player")

main()
