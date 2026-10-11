#!/usr/bin/env python3
"""Compile every CT Life audio assembly out of the editor, reusing client/Tools~/compile_check.py (read-only use):

  player pass (Android IL2CPP dev defines; --release: CTLIFE_PROD):
    all dependencies + CTLife.Audio + CTLife.Audio.Cues + the assemblies that reference audio (World, UI, App)
  editor pass (--editor): the same with editor defines, plus CTLife.Audio.Editor and CTLife.Audio.Tests (NUnit).

compile_check.py does not know CTLife.Audio.Cues / .Editor / .Tests yet (request in docs/progress/requests.md),
so this adds them. Output goes to $CTLIFE_AUDIO_OUT (default ~/.cache/ctlife-audio-check/run-<pid>), never the
shared cache. Usage: compile_audio.py [--editor] [--release]"""
import importlib.util, os, pathlib, shutil, sys

REPO = pathlib.Path(__file__).resolve().parents[5]
spec = importlib.util.spec_from_file_location("cc", REPO / "client/Tools~/compile_check.py")
cc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cc)
AUDIO = REPO / "client/Assets/Scripts/Audio"


def passes():
    out = [("player", False)]
    if "--editor" in sys.argv:
        out.append(("editor", True))
    return out


def main():
    cc.RELEASE = "--release" in sys.argv
    cc.fetch()
    cc.OUT = pathlib.Path(os.environ.get("CTLIFE_AUDIO_OUT", pathlib.Path.home() / ".cache/ctlife-audio-check" / f"run-{os.getpid()}"))
    shutil.rmtree(cc.OUT, ignore_errors=True)
    nunit = sorted(REPO.glob("client/Library/PackageCache/com.unity.ext.nunit@*/net40/unity-custom/nunit.framework.dll"))
    ok = True
    for label, editor in passes():
        units = cc.units(editor)
        newtonsoft = str(cc.CACHE / ("tgz/com.unity.nuget.newtonsoft-json-3.2.2/package/Runtime/" + ("Newtonsoft.Json.dll" if editor else "AOT/Newtonsoft.Json.dll")))
        # Insert the audio assemblies right after CTLife.Audio so later units (World/UI/App) still build.
        idx = next(i for i, u in enumerate(units) if u.name == "CTLife.Audio") + 1
        cues = cc.Unit("CTLife.Audio.Cues", [AUDIO / "Cues"], refs=["CTLife.Core", "CTLife.Generated", "CTLife.Net", "CTLife.Audio"], dlls=[newtonsoft],
                       editor=editor, override=True)
        units.insert(idx, cues)
        if editor:
            units.append(cc.Unit("CTLife.Audio.Editor", [AUDIO / "Editor"], refs=["CTLife.Audio"], editor=True, override=False))
            if nunit:
                units.append(cc.Unit("CTLife.Audio.Tests", [AUDIO / "Tests"], refs=["CTLife.Audio"], dlls=[str(nunit[-1])], editor=True, override=True))
        # Only CT Life assemblies that touch audio (and their prerequisites) matter here; skip unrelated editor tooling.
        skip = {"CTLife.Editor", "CTLife.Tests.EditMode", "CTLife.Tests.Integration", "CTLife.Features.Money.Tests", "Unity.Addressables.Editor"}
        built = {}
        title = ("Editor" if editor else ("Release player" if cc.RELEASE else "Dev player")) + " pass"
        print(title + ":")
        for u in units:
            if u.name in skip:
                continue
            # The Tests unit compiles the Tests folder itself; compile_check's sources() skips folders named Tests.
            if u.name == "CTLife.Audio.Tests":
                files = sorted((AUDIO / "Tests").glob("*.cs"))
                saved = cc.sources
                cc.sources = lambda root, **kw: files if pathlib.Path(root) == AUDIO / "Tests" else saved(root, **kw)
                try:
                    good = cc.compile_unit(u, built, label)
                finally:
                    cc.sources = saved
            else:
                good = cc.compile_unit(u, built, label)
            if not good and u.name.startswith("CTLife"):
                if "Audio" in u.name:
                    ok = False
                else:
                    print(f"  NOTE: {u.name} failed (not an audio assembly; dependents of it are skipped)")
                    skip.add(u.name)
        if not ok:
            break
    n_warn = sum(len(w) for k, w in cc.WARNINGS.items() if "Audio" in k)
    print(f"WARNINGS in audio assemblies: {n_warn}")
    print("RESULT:", "ok" if ok else "errors")
    shutil.rmtree(cc.OUT, ignore_errors=True)
    sys.exit(0 if ok else 1)


main()
