#!/usr/bin/env python3
"""
Pre-licence compile check for the CT Life Unity client.

Unity's batchmode needs a licence, but its bundled Roslyn compiler does not. This script compiles every client
assembly (and the third-party package sources it depends on, at the exact pinned versions) with the editor's own
`csc.dll`, against the editor's UnityEngine module reference assemblies and the .NET Standard 2.1 profile, using
Android IL2CPP player define symbols. It approximates Unity's player script compilation; it is not a substitute for
the real editor (no ILPP/codegen, no asset import), but it catches C# and API errors before the licence lands.

Usage:  python3 client/Tools~/compile_check.py [--editor] [--keep-going] [--release] [--warnaserror]
  (default)  Android player pass with DEVELOPMENT_BUILD (what a dev APK compiles).
  --release  player pass without DEVELOPMENT_BUILD and with CTLIFE_PROD (what a prod APK compiles).
  --editor   also an editor pass: every runtime assembly is recompiled with editor defines (UNITY_EDITOR...),
             exactly as the Unity editor does, and CTLife.Editor is compiled against those.
Warnings are on (-warn:4) and listed for CT Life assemblies; --warnaserror makes them fail the run.
Cache:  $CTLIFE_CC_CACHE (default ~/.cache/ctlife-compile-check): package sources fetched once.
Exit code 0 when every CT Life assembly compiles (and, with --warnaserror, has no warnings).
"""
import argparse, json, os, pathlib, shutil, subprocess, sys, tarfile, urllib.request

UNITY = pathlib.Path("/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents")
SCRIPTING = UNITY / "Resources/Scripting"
DOTNET = SCRIPTING / "NetCoreRuntime/dotnet"
CSC = SCRIPTING / "DotNetSdkRoslyn/csc.dll"
MODULES = SCRIPTING / "Managed/UnityEngine"
NETSTD = SCRIPTING / "NetStandard"
BUILTIN = UNITY / "Resources/PackageManager/BuiltInPackages"
EDITOR_TGZ = UNITY / "Resources/PackageManager/Editor"

CLIENT = pathlib.Path(__file__).resolve().parents[1]
ASSETS = CLIENT / "Assets"
CACHE = pathlib.Path(os.environ.get("CTLIFE_CC_CACHE", pathlib.Path.home() / ".cache/ctlife-compile-check"))
# Per-run output: several agents may run this harness at once, and a shared output dir made runs clobber each other.
OUT = pathlib.Path(os.environ.get("CTLIFE_CC_OUT", CACHE / "out" / f"run-{os.getpid()}"))

# Pins (keep in sync with client/Packages/manifest.json).
COLYSEUS = ("https://github.com/colyseus/colyseus-unity-sdk.git", "7306c8ee4be7e78984baf0837df5ea8125154820")
SOLANA = ("https://github.com/magicblock-labs/Solana.Unity-SDK.git", "bd0d30f648ed2368c0775e8c2eecd578eb6e4999")
LIVEKIT_TGZ = "https://package.openupm.com/io.livekit.livekit-sdk/-/io.livekit.livekit-sdk-2.1.0.tgz"
TGZS = ["com.unity.addressables-2.10.3", "com.unity.scriptablebuildpipeline-2.6.2", "com.unity.profiling.core-1.0.3",
        "com.unity.nuget.newtonsoft-json-3.2.2"]


RELEASE = False


def defines(editor=False):
    d = ["UNITY_6000_3_25", "UNITY_6000_3", "UNITY_6000", "UNITY_5_3_OR_NEWER", "UNITY_5_4_OR_NEWER", "UNITY_5_5_OR_NEWER",
         "UNITY_5_6_OR_NEWER"]
    for y in range(2017, 2024):
        for m in range(1, 5):
            d.append(f"UNITY_{y}_{m}_OR_NEWER")
    for m in range(0, 4):
        d.append(f"UNITY_6000_{m}_OR_NEWER")
    d += ["NET_STANDARD_2_0", "NET_STANDARD_2_1", "NET_STANDARD", "NETSTANDARD2_1", "NETSTANDARD", "CSHARP_7_3_OR_NEWER",
          "CSHARP_7_OR_LATER", "ENABLE_UNITYWEBREQUEST", "ENABLE_INPUT_SYSTEM", "ENABLE_LEGACY_INPUT_MANAGER", "UNITY_UGP_API",
          "ENABLE_PROFILER", "UNITY_ANALYTICS", "ENABLE_CLOUD_SERVICES", "ENABLE_UNITY_COLLECTIONS_CHECKS"]
    if editor:
        d += ["UNITY_EDITOR", "UNITY_EDITOR_64", "UNITY_EDITOR_OSX", "UNITY_ANDROID", "PLATFORM_ANDROID", "UNITY_ANDROID_API",
              "ENABLE_MONO", "ENABLE_IL2CPP"]
    else:
        d += ["UNITY_ANDROID", "UNITY_ANDROID_API", "PLATFORM_ANDROID", "ENABLE_IL2CPP", "UNITY_64"]
        d += ["CTLIFE_PROD"] if RELEASE else ["DEVELOPMENT_BUILD"]
    # Package presence defines that asmdef versionDefines would add for this manifest.
    d += ["UNITASK_ASSETBUNDLE_SUPPORT", "UNITASK_PHYSICS_SUPPORT", "UNITASK_PHYSICS2D_SUPPORT", "UNITASK_PARTICLESYSTEM_SUPPORT",
          "UNITASK_UGUI_SUPPORT", "UNITASK_WEBREQUEST_SUPPORT", "PACKAGE_PHYSICS", "PACKAGE_PHYSICS2D", "PACKAGE_ANIMATION",
          "PACKAGE_UITOOLKIT", "PACKAGE_INPUTSYSTEM", "ENABLE_ASYNC_ASSETBUNDLE_UWR"]
    return d


def run(cmd, **kw):
    return subprocess.run(cmd, check=True, **kw)


def tmp_copy():
    """Harness-only: TMP_FontAsset calls an internal FontEngine overload exposed to Unity.TextMeshPro through
    InternalsVisibleTo, which this out-of-editor csc run cannot see. Compile a cached copy with that one call
    neutralised. TMP is only here because Solana.Unity-SDK and UniTask reference it."""
    dst = OUT / "ugui_tmp"  # per run: concurrent runs raced on a shared copy
    src = BUILTIN / "com.unity.ugui/Runtime/TMP"
    shutil.rmtree(dst, ignore_errors=True)
    shutil.copytree(src, dst)
    f = dst / "TMP_FontAsset.cs"
    t = f.read_text(encoding="utf-8-sig")
    t = t.replace("GlyphPairAdjustmentRecord[] pairAdjustmentRecords = FontEngine.GetGlyphPairAdjustmentRecords(m_GlyphIndexList, out int recordCount);",
                  "GlyphPairAdjustmentRecord[] pairAdjustmentRecords = null; int recordCount = 0; /* compile-check stub */")
    f.write_text(t)
    return dst


def fetch():
    CACHE.mkdir(parents=True, exist_ok=True)
    for name, (url, sha) in {"colyseus": COLYSEUS, "solana": SOLANA}.items():
        d = CACHE / name
        if (d / ".git").exists() and subprocess.run(["git", "-C", d, "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip() == sha:
            continue
        shutil.rmtree(d, ignore_errors=True)
        run(["git", "init", "-q", d])
        run(["git", "-C", d, "remote", "add", "origin", url])
        run(["git", "-C", d, "fetch", "-q", "--depth", "1", "origin", sha])
        run(["git", "-C", d, "checkout", "-q", "FETCH_HEAD"])
    lk = CACHE / "livekit"
    if not (lk / "package/package.json").exists():
        lk.mkdir(parents=True, exist_ok=True)
        tgz = lk / "lk.tgz"
        urllib.request.urlretrieve(LIVEKIT_TGZ, tgz)
        with tarfile.open(tgz) as t:
            t.extractall(lk)
    for name in TGZS:
        d = CACHE / "tgz" / name
        if not (d / "package/package.json").exists():
            d.mkdir(parents=True, exist_ok=True)
            with tarfile.open(EDITOR_TGZ / f"{name}.tgz") as t:
                t.extractall(d)


def sources(root, exclude_asmdef_dirs=True, extra_exclude=()):
    """C# files under root, excluding subtrees owned by another asmdef, `~` folders, and tests."""
    root = pathlib.Path(root)
    files = []
    for dirpath, dirnames, filenames in os.walk(root):
        p = pathlib.Path(dirpath)
        dirnames[:] = [n for n in dirnames if not n.endswith("~") and not n.startswith(".") and n not in ("Tests",)]
        if exclude_asmdef_dirs and p != root and any(f.endswith(".asmdef") for f in filenames):
            dirnames[:] = []
            continue
        if any(str(p).startswith(str(root / e)) for e in extra_exclude):
            continue
        files += [p / f for f in filenames if f.endswith(".cs")]
    return files


def meta_auto_referenced(dll, editor):
    """Unity PluginImporter rules: auto-referenced if isExplicitlyReferenced is 0 (or no importer section) and the
    plugin is enabled for the target (Any Platform without an Exclude flag, or the platform entry enabled)."""
    meta = pathlib.Path(str(dll) + ".meta")
    if not meta.exists():
        return True
    t = meta.read_text(errors="ignore")
    if "PluginImporter" not in t:
        return True
    if "isExplicitlyReferenced: 1" in t:
        return False
    plat = "Editor" if editor else "Android"
    any_on = "Any: \n    second:\n      enabled: 1" in t or "Any:\n    second:\n      enabled: 1" in t
    if any_on:
        return f"Exclude {plat}: 1" not in t
    return f"{plat}: {plat}\n    second:\n      enabled: 1" in t


def auto_dlls(editor):
    S, T = CACHE / "solana", CACHE / "tgz"
    cands = list((S / "Packages").glob("*.dll")) + list((CACHE / "livekit/package/Runtime/Plugins").glob("*.dll"))
    nj = T / "com.unity.nuget.newtonsoft-json-3.2.2/package/Runtime"
    cands += [nj / ("Newtonsoft.Json.dll" if editor else "AOT/Newtonsoft.Json.dll")]
    return [str(d) for d in cands if meta_auto_referenced(d, editor)]


def asmdef_override(root):
    for f in pathlib.Path(root).glob("*.asmdef"):
        try:
            return bool(json.loads(f.read_text(encoding="utf-8-sig")).get("overrideReferences", False))
        except Exception:
            return False
    return False


class Unit:
    def __init__(self, name, roots, refs=(), dlls=(), unsafe=False, extra_defines=(), exclude=(), editor=False, nowarn=(), override=None):
        self.name, self.roots, self.refs, self.dlls = name, roots, list(refs), list(dlls)
        self.override = asmdef_override(roots[0]) if override is None else override
        self.unsafe, self.extra_defines, self.exclude, self.editor, self.nowarn = unsafe, list(extra_defines), exclude, editor, list(nowarn)


def reference_set(editor):
    refs = [NETSTD / "ref/2.1.0/netstandard.dll"]
    refs += sorted((NETSTD / "compat/2.1.0/shims/netfx").glob("*.dll"))
    refs += sorted((NETSTD / "compat/2.1.0/shims/netstandard").glob("*.dll"))
    refs += sorted(p for p in MODULES.glob("UnityEngine.*Module.dll"))
    refs += [MODULES / "UnityEngine.dll"]  # 190 KB type-forwarding facade (Managed/UnityEngine.dll is the monolith)
    if editor:
        refs += [MODULES / "UnityEditor.dll"]  # facade; Managed/UnityEditor.dll is the monolith
        refs += sorted(p for p in MODULES.glob("UnityEditor.*Module.dll"))
    return refs


WARNINGS = {}


def compile_unit(u, built, label):
    out = OUT / label / f"{u.name}.dll"
    out.parent.mkdir(parents=True, exist_ok=True)
    files = []
    for r in u.roots:
        files += sources(r, extra_exclude=u.exclude)
    if not files:
        print(f"  -- {u.name}: no sources yet (skipped)")
        return True
    dlls = list(u.dlls)
    if not u.override:
        names = {pathlib.Path(d).name for d in dlls}
        dlls += [d for d in auto_dlls(u.editor) if pathlib.Path(d).name not in names]
    refs = reference_set(u.editor) + [built[r] for r in u.refs if r in built] + [pathlib.Path(d) for d in dlls]
    missing = [r for r in u.refs if r not in built]
    if missing:
        print(f"  !! {u.name}: missing built refs {missing}")
    ours = u.name.startswith("CTLife")
    rsp = OUT / f"{u.name}.{label}.rsp"
    lines = ["-nologo", "-noconfig", "-nostdlib+", "-target:library", "-langversion:9.0", "-deterministic", "-debug-",
             "-warn:4" if ours else "-warn:0", "-nowarn:CS0436,CS1701,CS1702" + "".join("," + w for w in u.nowarn), f"-out:{out}"]
    if u.unsafe:
        lines.append("-unsafe")
    lines.append("-define:" + ";".join(defines(u.editor) + u.extra_defines))
    lines += [f'-r:"{r}"' for r in refs]
    lines += [f'"{f}"' for f in files]
    rsp.write_text("\n".join(lines))
    res = subprocess.run([str(DOTNET), "exec", str(CSC), f"@{rsp}"], capture_output=True, text=True)
    output = (res.stdout + res.stderr).splitlines()
    errs = [l for l in output if " error " in l]
    warns = sorted({l for l in output if " warning " in l}) if ours else []
    status = "ok" if res.returncode == 0 else f"FAILED ({len(errs)} errors)"
    if warns:
        status += f", {len(warns)} warnings"
        WARNINGS[f"{label}/{u.name}"] = warns
    print(f"  {u.name:40s} {len(files):4d} files  {status}")
    clean = lambda l: l.replace(str(CACHE), "$CACHE").replace(str(CLIENT), "client")
    if res.returncode != 0:
        for l in (errs or output[-8:] or ["(no compiler output)"])[:60]:
            print("     " + clean(l))
        return False
    for l in warns[:40]:
        print("     " + clean(l))
    built[u.name] = out
    return True


def units(editor):
    """editor=True: the editor pass. Every unit (third-party and CT Life runtime) is compiled with editor defines
    and UnityEditor references, as the Unity editor does, then CTLife.Editor is compiled against those."""
    S, C, LK, T = CACHE / "solana", CACHE / "colyseus", CACHE / "livekit/package", CACHE / "tgz"
    COLY = C if os.environ.get("CTLIFE_CC_COLYSEUS_UPSTREAM") else CLIENT / "Packages/io.colyseus.sdk"
    sol_dlls = sorted(str(p) for p in (S / "Packages").glob("*.dll"))
    newtonsoft = str(T / "com.unity.nuget.newtonsoft-json-3.2.2/package/Runtime/AOT/Newtonsoft.Json.dll")
    ugui = BUILTIN / "com.unity.ugui/Runtime"
    addr = T / "com.unity.addressables-2.10.3/package/Runtime"
    S_ = "Scripts"
    u = [
        # ---- third party (pinned sources) ----
        Unit("UnityEngine.UI", [ugui / "UGUI"]),
        Unit("Unity.InternalAPIEngineBridge.004", [ugui / "InternalBridge"], refs=["UnityEngine.UI"]),
        Unit("Unity.TextMeshPro", [tmp_copy()], refs=["UnityEngine.UI", "Unity.InternalAPIEngineBridge.004"], unsafe=True),
        Unit("UniTask", [S / "Runtime/Plugins/UniTask/Runtime"], refs=["UnityEngine.UI"], unsafe=True),
        Unit("UniTask.Linq", [S / "Runtime/Plugins/UniTask/Runtime/Linq"], refs=["UniTask"]),
        Unit("UnityGifDecoder", [S / "Runtime/Plugins/Unity-GifDecoder"]),
        Unit("com.solana.unity_sdk", [S / "Runtime"], refs=["UniTask", "UniTask.Linq", "UnityGifDecoder", "Unity.TextMeshPro", "UnityEngine.UI"],
             dlls=sol_dlls + [newtonsoft], unsafe=True),
        # Vendored at client/Packages/io.colyseus.sdk with overrideReferences (see VENDORED.txt). Set
        # CTLIFE_CC_COLYSEUS_UPSTREAM=1 to compile the unmodified upstream asmdefs and reproduce the CS0433 clash.
        Unit("colyseus.nativewebsocket", [COLY / "Runtime/WebSocket"]),
        Unit("ColyseusSDK", [COLY / "Runtime"], refs=["colyseus.nativewebsocket"]),
        Unit("LiveKit", [LK / "Runtime"], dlls=[str(LK / "Runtime/Plugins/Google.Protobuf.dll"),
                                                 str(LK / "Runtime/Plugins/System.Runtime.CompilerServices.Unsafe.dll"), newtonsoft], unsafe=True),
        Unit("Unity.Profiling.Core", [T / "com.unity.profiling.core-1.0.3/package/Runtime"], unsafe=True),
        Unit("Unity.ResourceManager", [addr / "ResourceManager"], refs=["Unity.Profiling.Core"], unsafe=True),
        Unit("Unity.Addressables", [addr], refs=["Unity.ResourceManager"], extra_defines=[]),
        Unit("Unity.RenderPipelines.Universal.Runtime", [pathlib.Path(__file__).parent / "compile_check_stubs/urp"]),
        # ---- CT Life ----
        Unit("CTLife.Generated", [ASSETS / "Generated"], refs=["ColyseusSDK"], dlls=[newtonsoft]),
        Unit("CTLife.Core", [ASSETS / S_ / "Core"], dlls=[newtonsoft]),
        Unit("CTLife.Net", [ASSETS / S_ / "Net"], refs=["CTLife.Core", "CTLife.Generated", "ColyseusSDK", "LiveKit"], dlls=[newtonsoft]),
        Unit("CTLife.Audio", [ASSETS / S_ / "Audio"], refs=["CTLife.Core"], dlls=[newtonsoft]),
        Unit("CTLife.World", [ASSETS / S_ / "World"], refs=["CTLife.Core", "CTLife.Net", "CTLife.Generated", "CTLife.Audio", "ColyseusSDK",
                                                            "Unity.Addressables", "Unity.ResourceManager", "Unity.RenderPipelines.Universal.Runtime"],
             dlls=[newtonsoft]),
        Unit("CTLife.Solana", [ASSETS / S_ / "Solana"], refs=["CTLife.Core", "com.solana.unity_sdk", "UniTask"], dlls=sol_dlls + [newtonsoft]),
        Unit("CTLife.UI", [ASSETS / S_ / "UI"], refs=["CTLife.Core", "CTLife.Net", "CTLife.Generated", "CTLife.World", "CTLife.Audio", "ColyseusSDK", "Unity.RenderPipelines.Universal.Runtime"],
             dlls=[newtonsoft]),
    ]
    money = ASSETS / S_ / "Features/Money"
    # Feature lanes: every client/Assets/Scripts/Features/<X>/*.asmdef, with references read from the asmdef
    # (Money first: other lanes may reference it). Their test asmdefs are handled below.
    features = sorted((ASSETS / S_ / "Features").glob("*/*.asmdef"), key=lambda a: (a.parent.name != "Money", a.parent.name))
    for asm in features:
        meta = json.loads(asm.read_text(encoding="utf-8-sig"))
        u.append(Unit(meta["name"], [asm.parent], refs=list(meta.get("references", [])), dlls=[newtonsoft]))
    app = json.loads((ASSETS / S_ / "App/CTLife.App.asmdef").read_text(encoding="utf-8-sig"))
    u.append(Unit("CTLife.App", [ASSETS / S_ / "App"], refs=list(app.get("references", [])), dlls=[newtonsoft]))
    if editor:
        for unit in u:
            unit.editor = True
        newtonsoft_editor = str(T / "com.unity.nuget.newtonsoft-json-3.2.2/package/Runtime/Newtonsoft.Json.dll")
        for unit in u:
            unit.dlls = [newtonsoft_editor if d == newtonsoft else d for d in unit.dlls]
        u.append(Unit("Unity.Addressables.Editor", [pathlib.Path(__file__).parent / "compile_check_stubs/addressables_editor"], editor=True))
        u.append(Unit("CTLife.Editor", [ASSETS / S_ / "Editor"], refs=["CTLife.Core", "CTLife.World", "CTLife.UI", "CTLife.App", "Unity.Addressables",
                                                                       "Unity.Addressables.Editor", "Unity.RenderPipelines.Universal.Runtime"],
                      dlls=[newtonsoft], editor=True))
        # EditMode test assemblies (NUnit from the installed com.unity.ext.nunit; the test runner itself is not needed
        # to compile [Test]/[TestCase] code).
        integration = ASSETS / "Tests/Integration"
        if integration.exists():
            u.append(Unit("CTLife.Tests.Integration", [integration], refs=["CTLife.Core", "CTLife.Net", "CTLife.Generated", "CTLife.World", "CTLife.UI",
                                                                          "CTLife.App", "ColyseusSDK"], dlls=[newtonsoft], editor=True, override=True))
        nunit = sorted(CLIENT.glob("Library/PackageCache/com.unity.ext.nunit@*/net40/unity-custom/nunit.framework.dll"))
        if nunit:
            tests = ASSETS / "Tests/EditMode"
            if tests.exists():
                u.append(Unit("CTLife.Tests.EditMode", [tests], refs=["CTLife.Core", "CTLife.Net", "CTLife.Generated", "CTLife.World", "CTLife.UI",
                                                                      "CTLife.Editor", "ColyseusSDK"],
                              dlls=[newtonsoft, str(nunit[-1])], editor=True, override=True))
            for tasm in sorted((ASSETS / S_ / "Features").glob("*/Tests/*.asmdef")):
                meta = json.loads(tasm.read_text(encoding="utf-8-sig"))
                refs = [r for r in meta.get("references", []) if not r.startswith(("UnityEngine.TestRunner", "UnityEditor.TestRunner"))]
                u.append(Unit(meta["name"], [tasm.parent], refs=refs, dlls=[newtonsoft, str(nunit[-1])], editor=True, override=True))
        else:
            print("  !! nunit.framework.dll not found in client/Library/PackageCache; test assemblies skipped (open the project once)")
    return u


def main():
    global RELEASE
    ap = argparse.ArgumentParser()
    ap.add_argument("--editor", action="store_true", help="also run the editor pass (runtime assemblies with editor defines + CTLife.Editor)")
    ap.add_argument("--release", action="store_true", help="player pass for a prod APK: no DEVELOPMENT_BUILD, CTLIFE_PROD defined")
    ap.add_argument("--warnaserror", action="store_true", help="fail when a CT Life assembly has warnings")
    ap.add_argument("--keep-going", action="store_true")
    a = ap.parse_args()
    RELEASE = a.release
    for p in (DOTNET, CSC, MODULES):
        if not p.exists():
            sys.exit(f"missing {p}")
    fetch()
    shutil.rmtree(OUT, ignore_errors=True)
    ok_all = True
    passes = [("player", False)] + ([("editor", True)] if a.editor else [])
    for label, editor in passes:
        built = {}
        title = "Editor pass (editor defines on every assembly)" if editor else \
            ("Android IL2CPP release player pass (CTLIFE_PROD)" if RELEASE else "Android IL2CPP dev player pass (DEVELOPMENT_BUILD)")
        print(f"{title}, Unity 6000.3.25f1 csc, .NET Standard 2.1:")
        for u in units(editor):
            ok = compile_unit(u, built, label)
            if not ok and u.name.startswith("CTLife"):
                ok_all = False
                if not a.keep_going:
                    break
        if not ok_all and not a.keep_going:
            break
    n_warn = sum(len(w) for w in WARNINGS.values())
    print(f"WARNINGS: {n_warn} in CT Life assemblies" + (" (" + ", ".join(f"{k}: {len(v)}" for k, v in WARNINGS.items()) + ")" if n_warn else ""))
    if a.warnaserror and n_warn:
        ok_all = False
    print("RESULT:", "all CT Life assemblies compile" if ok_all else "errors above")
    if "CTLIFE_CC_OUT" not in os.environ:
        shutil.rmtree(OUT, ignore_errors=True)
    sys.exit(0 if ok_all else 1)


if __name__ == "__main__":
    main()
