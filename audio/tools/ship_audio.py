#!/usr/bin/env python3
"""Ship exactly what sfx-map.json references - nothing else reaches the APK.

Everything under client/Assets/Resources/ is forced into the Android build, so the runtime folder must hold only
clips the game can play. This tool:
  1. lists every file sfx-map.json references (sfx, footsteps, ambience, music full mixes + stems, babble),
  2. --archive: moves unreferenced files out of audio/processed/ into audio/archive/processed/ (same relative path), so a
     later ContentImport.SyncAll cannot copy them back (tool inputs Music-Credits.txt and babble-banks.json stay),
  3. --sync: copies referenced files + sfx-map.json + Music-Credits.txt into client/Assets/Resources/Audio/ (if changed)
     and deletes everything else there (with its .meta; empty folders and their .meta too),
  4. always: reports counts and the estimated Android audio footprint.

Footprint estimate: per-clip sizes measured from a real APK (`--apk`, default the newest client/Builds/*.apk): Unity
writes one FSB5 `.resource` per clip named by the clip's GUID, which the .meta files map back to paths. Clips without
a measurement (new files) are estimated from the measured bytes per second of their import group. Import-setting
changes since that APK (ImportPolicy.cs) are applied as ratio ranges and labelled [Inference].
Usage: ship_audio.py [--archive] [--sync] [--check] [--apk PATH]   (--check exits 1 if Resources is out of sync)
"""
from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]                 # audio/
REPO = ROOT.parent
PROCESSED = ROOT / "processed"
ARCHIVE = ROOT / "archive" / "processed"
RES = REPO / "client/Assets/Resources/Audio"
KEEP_IN_PROCESSED = {"Music-Credits.txt", "babble/babble-banks.json"}   # tool inputs, not clips
SHIP_EXTRA = ["Music-Credits.txt"]                                     # shipped TextAsset besides sfx-map.json


def referenced(m: dict) -> set[str]:
    out: set[str] = set()

    def add(e):
        if isinstance(e, str):
            out.add(e)
            return
        if e.get("file"):
            out.add(e["file"])
        out.update(f for f in e.get("files", []) if f)

    for sec in ("sfx", "footsteps", "ambience", "babble"):
        for k, v in m.get(sec, {}).items():
            if not k.startswith(("_", "$")):
                add(v)
    for k, v in m.get("music", {}).items():
        if k.startswith(("_", "$")):
            continue
        if v.get("full_mix"):
            out.add(v["full_mix"])
        out.update(p for p in (v.get("stems") or {}).values() if p)
    return out


def group(rel: str) -> str:
    if rel.startswith("music/stems/") or rel.startswith("music/mood/"):
        return "music_layer"
    if rel.startswith("music/"):
        return "music"
    if rel.startswith("ambience/"):
        return "ambience"
    if rel.startswith("babble/"):
        return "babble"
    if Path(rel).name.startswith("foot_"):
        return "footstep"
    if Path(rel).stem in STINGERS:
        return "stinger"
    return "sfx"


# Mirrors ImportPolicy.Stingers (stereo by design); every other one-shot, footstep and babble file must be mono.
STINGERS = {"sfx_liquidation", "sfx_rug", "sfx_graduation", "sfx_moon_horn", "sfx_rank_up", "sfx_sunday_unlock", "sfx_rekt_phonk",
            "sfx_origin_insider", "sfx_origin_exit_liquidity", "sfx_creator_complete"}
MONO_GROUPS = {"sfx", "footstep", "babble"}


def probe(path: Path) -> tuple[float, int]:
    r = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "stream=duration,channels", "-of", "json", str(path)],
                       capture_output=True, text=True)
    s = json.loads(r.stdout or "{}").get("streams", [{}])[0]
    return float(s.get("duration") or 0), int(s.get("channels") or 1)


APK_RESOURCE_TOTAL = [0]


def apk_sizes(apk: Path) -> dict[str, int]:
    """rel path under Resources/Audio -> bytes of its FSB5 .resource in the APK."""
    if not apk or not apk.exists():
        return {}
    z = zipfile.ZipFile(apk)
    by_guid = {}
    for i in z.infolist():
        mm = re.match(r"assets/bin/Data/([0-9a-f]{32})\.resource$", i.filename)
        if mm:
            by_guid[mm.group(1)] = i.file_size
            APK_RESOURCE_TOTAL[0] += i.file_size
    out = {}
    for meta in RES.rglob("*.meta"):
        g = re.search(r"guid: ([0-9a-f]{32})", meta.read_text(errors="ignore"))
        if g and g.group(1) in by_guid:
            out[str(meta.relative_to(RES))[:-5]] = by_guid[g.group(1)]
    return out


# [Inference] size ratios for import changes made after the measured APK (ImportPolicy.cs, 2026-10-08):
# ambience Vorbis 0.45 -> 0.35; babble 48 kHz q0.6 -> 24 kHz q0.5; footsteps 48 kHz q0.6 -> 32 kHz q0.5.
# Not measured: Unity's Vorbis quality scale is not documented against bitrates. Low/high bounds.
SETTING_RATIOS = {"ambience": (0.75, 0.90), "babble": (0.50, 0.70), "footstep": (0.70, 0.85)}


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--archive", action="store_true")
    ap.add_argument("--sync", action="store_true")
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--apk", type=Path, default=None)
    a = ap.parse_args()
    m = json.loads((ROOT / "sfx-map.json").read_text())
    ref = referenced(m)
    missing = sorted(r for r in ref if not (PROCESSED / r).exists())
    if missing:
        print("MISSING referenced files in audio/processed:", *missing, sep="\n  ")
        return 1

    if a.archive:
        moved = 0
        for p in sorted(PROCESSED.rglob("*")):
            if not p.is_file() or p.name.startswith("."):
                continue
            rel = str(p.relative_to(PROCESSED))
            if rel in ref or rel in KEEP_IN_PROCESSED:
                continue
            dst = ARCHIVE / rel
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.move(str(p), str(dst))
            moved += 1
        for d in sorted((x for x in PROCESSED.rglob("*") if x.is_dir()), key=lambda x: -len(x.parts)):
            if not any(d.iterdir()):
                d.rmdir()
        print(f"archived {moved} unreferenced files -> {ARCHIVE.relative_to(REPO)}")

    apk = a.apk or max((REPO / "client/Builds").glob("*.apk"), key=lambda p: p.stat().st_mtime, default=None)
    measured = apk_sizes(apk) if apk else {}   # read before pruning: the metas map GUIDs to paths

    ship = set(ref) | {"sfx-map.json"} | set(SHIP_EXTRA)
    stale = []
    for p in RES.rglob("*"):
        if p.is_file() and p.suffix != ".meta" and not p.name.startswith("."):
            rel = str(p.relative_to(RES))
            if rel not in ship:
                stale.append(rel)
    changed = []
    for rel in sorted(ship):
        src = ROOT / "sfx-map.json" if rel == "sfx-map.json" else PROCESSED / rel
        dst = RES / rel
        if not dst.exists() or dst.stat().st_size != src.stat().st_size or dst.read_bytes() != src.read_bytes():
            changed.append(rel)
    if a.sync:
        for rel in changed:
            src = ROOT / "sfx-map.json" if rel == "sfx-map.json" else PROCESSED / rel
            (RES / rel).parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, RES / rel)
        for rel in stale:
            (RES / rel).unlink()
            meta = RES / (rel + ".meta")
            if meta.exists():
                meta.unlink()
        for d in sorted((x for x in RES.rglob("*") if x.is_dir()), key=lambda x: -len(x.parts)):
            if not any(d.iterdir()):
                d.rmdir()
                meta = d.parent / (d.name + ".meta")
                if meta.exists():
                    meta.unlink()
        print(f"synced {len(changed)} files into {RES.relative_to(REPO)}, removed {len(stale)} unreferenced (+ .meta)")
    else:
        print(f"Resources/Audio: {len(changed)} files to copy/update, {len(stale)} unreferenced files to remove")

    # ---------------------------------------------------------------- footprint
    rates: dict[str, list[float]] = {}
    rows = []
    for rel in sorted(ref):
        dur, ch = probe(PROCESSED / rel)
        g = group(rel)
        if g in MONO_GROUPS and ch != 1:
            print(f"WARNING stereo file in a mono group (import does not downmix): {rel}")
        rows.append((rel, g, dur, ch))
        if rel in measured and dur > 0:
            rates.setdefault(g, []).append(measured[rel] / (dur * ch))
    rate = {g: sum(v) / len(v) for g, v in rates.items()}
    rate.setdefault("music_layer", rate.get("music", 9000 / 2))
    totals: dict[str, list[float]] = {}
    est_new = 0
    for rel, g, dur, ch in rows:
        if rel in measured:
            base = measured[rel]
        else:
            base = rate.get(g, 8000.0) * dur * ch
            est_new += 1
        lo, hi = SETTING_RATIOS.get(g, (1.0, 1.0))
        t = totals.setdefault(g, [0, 0.0, 0.0])
        t[0] += 1
        t[1] += base * lo
        t[2] += base * hi
    before = APK_RESOURCE_TOTAL[0] or sum(measured.values())
    print(f"\nMeasured APK: {apk.name if apk else 'none'} - all audio .resource files {before / 1e6:.2f} MB "
          f"({len(measured)} still map to a shipping clip)")
    print(f"Shipping set from sfx-map.json: {len(ref)} clips ({est_new} without an APK measurement, estimated by group rate)")
    print(f"{'group':12s} {'clips':>5s} {'est. MB (low..high)':>22s}")
    lo_sum = hi_sum = 0.0
    for g, (n, lo, hi) in sorted(totals.items()):
        lo_sum += lo
        hi_sum += hi
        print(f"{g:12s} {n:5d} {lo / 1e6:10.2f} .. {hi / 1e6:6.2f}")
    print(f"{'TOTAL':12s} {len(ref):5d} {lo_sum / 1e6:10.2f} .. {hi_sum / 1e6:6.2f} MB  (was {before / 1e6:.2f} MB measured)")
    if a.check and (stale or changed):
        print("CHECK FAILED: Resources/Audio differs from the shipping set (run ship_audio.py --sync)")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
