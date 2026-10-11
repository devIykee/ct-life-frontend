#!/usr/bin/env python3
"""Assemble audio/sfx-map.json and audio/LICENSES.md from the build reports, QA report and sources.json."""
from __future__ import annotations

import datetime as dt
import json
from pathlib import Path

TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parent
TODAY = dt.date.today().isoformat()


def load(name):
    p = TOOLS / name
    return json.loads(p.read_text()) if p.exists() else {}


sfx = load("build_sfx.json")
foot = load("build_footsteps.json")
amb = load("build_ambience.json")
mus = load("build_music.json")
qa = load("qa_report.json")
src = load("sources.json")
babble = json.loads((ROOT / "processed/babble/babble-banks.json").read_text())

QA = {Path(r["file"]).name: r for k in qa for r in qa[k]}
IN_CONTRACT = {"sfx_buy", "sfx_sell", "sfx_liquidation", "sfx_rug", "sfx_call_ring"}  # contract.md §2 examples
CONTRACT_VENUES = {"timeline_square", "spaces_studio", "terminal_cafe", "perp_pit", "rekt_clinic", "ore_refinery"}

UNITY = {
    "short": {"loadType": "DecompressOnLoad", "compressionFormat": "ADPCM", "preloadAudioData": True},
    "stinger": {"loadType": "CompressedInMemory", "compressionFormat": "Vorbis", "quality": 0.7, "preloadAudioData": True},
    "loop": {"loadType": "Streaming", "compressionFormat": "Vorbis", "quality": 0.6, "preloadAudioData": False},
    "music": {"loadType": "Streaming", "compressionFormat": "Vorbis", "quality": 0.7, "preloadAudioData": False},
}


def qa_of(path: str) -> dict:
    r = QA.get(Path(path).name, {})
    return {k: r.get(k) for k in ("dur", "I", "TP") if k in r}


ids: dict = {}

for id_, m in sfx.items():
    files = [f["path"] for f in m["files"]]
    q = qa_of(files[0])
    ids[id_] = {
        "category": m["category"], "files": files, "random_pick": len(files) > 1, "channels": m["channels"],
        "loop": m["loop"], "duration_s": q.get("dur"), "true_peak_dbtp": q.get("TP"), "volume_db": m["volume_db"],
        "ducks_music": m["category"] == "stinger", "status": "in_contract" if id_ in IN_CONTRACT else "proposed",
        "desc": m["desc"], "sources": m["sources"] or ["own work (synthesised, tools/gen_sfx.py)"],
        "unity_import": UNITY["stinger" if m["category"] == "stinger" or (q.get("dur") or 0) > 2.5 else "short"],
    }

for id_, m in foot.items():
    files = [f["path"] for f in m["files"]]
    ids[id_] = {
        "category": "footstep", "files": files, "random_pick": True, "channels": 1, "loop": False,
        "volume_db": -14, "status": "proposed",
        "desc": f"Single footsteps on {id_[5:].replace('_', ' ')}; pick at random, never the same twice in a row, "
                "pitch +-5 %, volume +-2 dB, one per foot plant (anim event).",
        "sources": m["sources"], "unity_import": UNITY["short"] | {"forceToMono": True},
    }

AMB_DESC = {
    "amb_timeline_square": ("timeline_square", "City plaza crowd: chatter, footsteps, distant city"),
    "amb_terminal_cafe": ("terminal_cafe", "Cafe murmur + mechanical keyboard clatter (traders)"),
    "amb_airdrop_farm": ("airdrop_farm", "Farm birds and countryside"),
    "amb_dubai_marina": ("dubai_marina", "Marina water lapping, gulls, rigging in the wind"),
    "amb_ore_refinery": ("ore_refinery", "Industrial refinery: machine hum, compressor, furnace roar"),
    "amb_perp_pit": ("perp_pit", "Busy trading floor crowd + keyboards + server hum"),
    "amb_rekt_clinic": ("rekt_clinic", "Quiet ward: room tone, heart-monitor beeps, muffled voices"),
    "amb_spaces_studio": ("spaces_studio", "Treated studio room tone (voice-only venue, very quiet)"),
    "amb_club_doors": ("clubs", "Club bass muffled behind closed doors + crowd outside (built from mus_club)"),
}
for id_, m in amb.items():
    venue, desc = AMB_DESC[id_]
    q = qa_of(m["path"])
    ids[id_] = {
        "category": "ambience", "venue": venue, "venue_status": "in_contract" if venue in CONTRACT_VENUES else "later phase",
        "files": [m["path"]], "channels": 2, "loop": True, "loop_start_sample": 0, "loop_end_sample": m["samples"],
        "duration_s": q.get("dur"), "loudness_lufs": q.get("I"), "true_peak_dbtp": q.get("TP"), "volume_db": 0,
        "status": "proposed", "desc": desc, "sources": m["sources"] or ["own work (synthesised)"],
        "unity_import": UNITY["loop"],
    }

MUS_NAME = {"timeline_square": "Timeline Square", "trenches": "The Trenches", "perp_pit": "Perp Pit",
            "airdrop_farm": "Airdrop Farm", "builder_hills": "Builder Hills", "dubai_marina": "Dubai Marina",
            "club": "Clubs and parties"}
for d, m in mus.items():
    full = m["files"]["full"]
    q = qa_of(full)
    ids[f"mus_{d}"] = {
        "category": "music", "district": MUS_NAME[d], "bpm": m["bpm"], "bars": m["bars"], "time_signature": "4/4",
        "loop": True, "loop_samples": m["loop_samples"], "sample_rate": m["sr"], "loop_seconds": m["loop_seconds"],
        "stems": {s: m["files"][s] for s in ("pad", "bass", "drums", "lead", "tension")},
        "full_mix_preview": full, "full_mix_lufs": q.get("I"), "full_mix_true_peak_dbtp": q.get("TP"),
        "status": "proposed", "sources": ["own work (composed and synthesised, tools/gen_music.py)"],
        "unity_import": UNITY["music"],
    }

for name, b in babble["banks"].items():
    ids[f"babble_{name}"] = {
        "category": "babble", "archetype": name if name != "generic" else None, "archetype_status": b["phase"],
        "files": b["files"], "channels": 1, "loop": False, "chars_per_second": b["chars_per_second"],
        "pitch_jitter_semitones": b["pitch_jitter_semitones"], "npc_pitch_range_semitones": b["npc_pitch_range_semitones"],
        "volume_db": b["volume_db"], "status": "proposed", "sources": ["own work (formant synthesis, tools/gen_babble.py)"],
        "unity_import": UNITY["short"],
    }

out = {
    "version": 1,
    "generated": TODAY,
    "paths_relative_to": "audio/",
    "notes": [
        "IDs are proposals until added to contract/ids/sfx.json (FE-owned). The five IDs marked in_contract are the "
        "examples already listed in docs/spec/contract.md section 2.",
        "One-shot SFX are normalised to -1 dBTP true peak (consistent peaks); set the mix with volume_db.",
        "Ambience loops: -24 LUFS integrated, true peak <= -3 dBTP. Spaces Studio room tone is -36 LUFS.",
        "Music: each district's full mix (pad+bass+drums+lead) is -17 LUFS integrated, true peak <= -2 dBTP. "
        "All stems of a district have identical sample length and start together; see audio/README.md for the "
        "market_mood rules.",
        "Babble: see audio/BABBLE.md and audio/unity/BabblePlayer.cs.",
        "Stingers duck the music bus (spec). LiveKit voice ducks the music bus by 6 dB (spec).",
    ],
    "ids": dict(sorted(ids.items())),
}
(ROOT / "sfx-map.json").write_text(json.dumps(out, indent=2, ensure_ascii=False) + "\n")

# ------------------------------------------------------------------ LICENSES.md
used_by: dict[str, set] = {}
for id_, e in ids.items():
    for s in e.get("sources", []):
        if s.startswith("raw/"):
            used_by.setdefault(s, set()).add(id_)

lines = [
    "# Audio licences",
    "",
    f"Generated by `tools/build_map.py` on {TODAY}. Every third-party file used in `processed/` is listed here.",
    "Rule (docs/AGENTS-BRIEF.md): CC0 only; CC-BY only if unavoidable. **Result: everything is CC0 or our own work. "
    "No CC-BY, no attribution required.**",
    "",
    "## Own work (CC0 dedication by the CT Life project)",
    "",
    "Synthesised in Python (numpy/scipy) by the scripts in `audio/tools/`; no samples involved unless listed below:",
    "",
    "- All music (`processed/music/*`, 7 districts x 5 stems + full-mix previews) — `tools/gen_music.py`.",
    "- All NPC babble (`processed/babble/*`, 31 banks x 26 syllables) — `tools/gen_babble.py`.",
    "- Synthesised SFX layers (UI, trading, phone, voice rooms, stingers, ORE) — `tools/gen_sfx.py`.",
    "- Synthetic layers in ambience (hum, room tone, monitor beeps, club-through-wall) — `tools/gen_ambience.py`.",
    "",
    "## Kenney (kenney.nl) — CC0 1.0",
    "",
    "Licence evidence: each zip contains `License.txt`: \"License: (Creative Commons Zero, CC0) "
    "http://creativecommons.org/publicdomain/zero/1.0/\". Crediting Kenney is optional; we credit anyway.",
    "",
    "| Pack | Page | Download | Licence | Author | Date downloaded | Files used → where |",
    "| --- | --- | --- | --- | --- | --- | --- |",
]
for name, k in src.get("kenney", {}).items():
    files = sorted(f for f in used_by if f.startswith(f"raw/kenney/{name}/"))
    if files:
        use = "<br>".join(f"`{Path(f).name}` → {', '.join(sorted(used_by[f]))}" for f in files)
    else:
        use = "downloaded, not used in the current build"
    lines.append(f"| {k['title']} | {k['url']} | [zip]({k['download']}) | CC0 1.0 | Kenney | {k['date']} | {use} |")
lines += [
    "",
    "## Freesound (freesound.org) — CC0 1.0",
    "",
    "Found with the search filter `license:\"Creative Commons 0\"`; `tools/fetch_sources.py` re-checks each sound page "
    "for the CC0 deed link before downloading. Freesound only gives original files to logged-in users, and we may not "
    "create accounts, so these are the public **HQ previews** (Ogg Vorbis, 192 kbps) of each sound. Same sound, same licence.",
    "",
    "| ID | Title | Author | Page | Licence | Date downloaded | Used in |",
    "| --- | --- | --- | --- | --- | --- | --- |",
]
for sid, f in sorted(src.get("freesound", {}).items(), key=lambda kv: int(kv[0])):
    used = ", ".join(sorted(used_by.get(f"raw/freesound/{sid}.ogg", []))) or "downloaded, not used"
    title = f["title"].replace("|", "/")
    lines.append(f"| {sid} | {title} | {f['author']} | {f['url']} | CC0 1.0 | {f['date']} | {used} |")
lines += [
    "",
    "## Not used (and why)",
    "",
    "- Sonniss GDC bundle: royalty-free but not CC0, multi-GB; not needed.",
    "- ElevenLabs / Suno: paid plans, account needed (we may not sign up). Udio: banned by spec.",
    "- Any freesound sound under CC-BY / CC-BY-NC: excluded by the search filter.",
    "",
]
(ROOT / "LICENSES.md").write_text("\n".join(lines))
print(f"sfx-map.json: {len(ids)} ids; LICENSES.md: {len(src.get('freesound', {}))} freesound + {len(src.get('kenney', {}))} kenney rows")
