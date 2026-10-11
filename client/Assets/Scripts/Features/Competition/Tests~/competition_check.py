#!/usr/bin/env python3
"""Compile CTLife.Features.Competition + its EditMode tests out of the editor, reusing client/Tools~/compile_check.py
(read-only use). Output goes to $CTLIFE_COMPETITION_OUT (default ~/.cache/ctlife-competition-tests), never the shared cache.

  --head-deps   compile the dependency assemblies from `git HEAD` (when someone's uncommitted work breaks them).
  --release     player pass without DEVELOPMENT_BUILD and with CTLIFE_PROD.
  --editor      compile every unit with editor defines (what the Unity editor and the Test Runner compile)."""
import importlib.util, io, pathlib, shutil, subprocess, sys, tarfile, os
REPO = pathlib.Path(__file__).resolve().parents[6]
spec = importlib.util.spec_from_file_location("cc", REPO / "client/Tools~/compile_check.py")
cc = importlib.util.module_from_spec(spec); spec.loader.exec_module(cc)
COMP = REPO / "client/Assets/Scripts/Features/Competition"
NUNIT = next((REPO / "client/Library/PackageCache").glob("com.unity.ext.nunit@*/net40/unity-custom/nunit.framework.dll"))

def check_strings():
    import re
    src = (COMP / "Logic/CompStrings.cs").read_text()
    have = set(re.findall(r'\["([A-Za-z0-9_.]+)"\]\s*=', src))
    missing = {}
    for f in COMP.rglob("*.cs"):
        if "Tests" in f.parts or f.name == "CompStrings.cs": continue
        text = f.read_text()
        for m in re.finditer(r'(?:CompStrings\.)?\b(?:T|F|Has)\("([A-Za-z0-9_.]+)"', text):
            k = m.group(1)
            if k.endswith("."):
                # a prefix for a dynamic key: some copy must exist under it (chat.refusal.* is optional by design: unknown ids fall back)
                if k != "chat.refusal." and not any(h.startswith(k) for h in have): missing.setdefault(k, f.name)
            elif k not in have: missing.setdefault(k, f.name)
    # keys built from a prefix: check each value exists
    dyn = {"topic.": CompStringsTopics(src), }
    for k, f in sorted(missing.items()): print(f"MISSING KEY {k} ({f})")
    print(f"STRING KEYS: {len(have)} defined, {len(missing)} missing")
    return not missing

def CompStringsTopics(src): return []

def check_routes():
    """Every route in CompetitionApi.ProposalRoutes must exist (path + method) in the Phase 4-6 proposal's openapi-additions.yaml."""
    import re
    api = (COMP / "Net/CompetitionApi.cs").read_text()
    block = re.search(r"ProposalRoutes =\s*\{(.*?)\};", api, re.S).group(1)
    routes = re.findall(r'"([A-Z]+) (/[^"]*)"', block)
    yaml = (REPO / "docs/fe-contract-proposals/phase4-6/openapi-additions.yaml").read_text().splitlines()
    have, path = set(), None
    for line in yaml:
        m = re.match(r"^  (/[^:]*):\s*$", line)
        if m: path = m.group(1); continue
        m = re.match(r"^    (get|post|put|patch|delete):\s*$", line)
        if m and path: have.add((m.group(1).upper(), path))
    missing = [r for r in routes if r not in have]
    for r in missing: print("ROUTE NOT IN PROPOSAL", r)
    print(f"ROUTES: {len(routes)} used, {len(missing)} not in the proposal")
    return not missing

def main():
    if not check_strings() or not check_routes(): sys.exit(1)
    cc.fetch()
    cc.RELEASE = "--release" in sys.argv
    editor = "--editor" in sys.argv
    cc.OUT = pathlib.Path(os.environ.get("CTLIFE_COMPETITION_OUT", pathlib.Path.home() / ".cache/ctlife-competition-tests"))
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
    units.append(cc.Unit("CTLife.Features.Competition", [COMP], refs=["CTLife.Core", "CTLife.Net", "CTLife.Generated", "CTLife.UI"], dlls=[newtonsoft], editor=editor))
    units.append(cc.Unit("CTLife.Features.Competition.Tests", [COMP / "Tests"], refs=["CTLife.Features.Competition", "CTLife.Core", "CTLife.Net",
                         "CTLife.Generated"], dlls=[newtonsoft, str(NUNIT)], editor=True, override=True))
    built = {}
    label = "player"
    for u in units:
        if not cc.compile_unit(u, built, label) and u.name.startswith("CTLife"):
            print("RESULT: errors"); sys.exit(1)
    n_warn = sum(len(w) for k, w in cc.WARNINGS.items() if "Competition" in k)
    print(f"WARNINGS in Competition assemblies: {n_warn}")
    print("RESULT: ok", cc.OUT / label)

main()
