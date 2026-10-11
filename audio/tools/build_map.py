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


def rel(p: str) -> str:
    """Build reports store paths relative to audio/; the client wants them relative to audio/processed/."""
    return p[len("processed/"):] if p.startswith("processed/") else p


def lin(db_: float) -> float:
    return round(10 ** (db_ / 20), 3)


JITTER = {"ui": 0.0, "phone": 0.0, "voice_room": 0.0, "stinger": 0.0, "trading": 0.02, "ore": 0.04}

ids: dict = {}          # flat index: prefixed id -> "section.key" (also used for LICENSES.md usage)
sections = {"sfx": {}, "footsteps": {}, "ambience": {}, "music": {}, "babble": {}}

for id_, m in sfx.items():
    files = [f["path"] for f in m["files"]]
    q = qa_of(files[0])
    e = {
        "files": [rel(f) for f in files], "volume": lin(m["volume_db"]), "pitchJitter": JITTER.get(m["category"], 0.0),
        "category": m["category"], "volume_db": m["volume_db"], "channels": m["channels"], "loop": m["loop"],
        "duration_s": q.get("dur"), "true_peak_dbtp": q.get("TP"), "ducks_music": m["category"] == "stinger",
        "status": "in_contract" if id_ in IN_CONTRACT else "proposed", "desc": m["desc"],
        "sources": m["sources"] or ["own work (synthesised, tools/gen_sfx.py)"],
        "unity_import": UNITY["stinger" if m["category"] == "stinger" or (q.get("dur") or 0) > 2.5 else "short"],
    }
    sections["sfx"][id_] = e
    ids[id_] = {"section": "sfx", "key": id_, "sources": e["sources"]}

for id_, m in foot.items():
    surface = id_[len("foot_"):]
    e = {
        "id": id_, "files": [rel(f["path"]) for f in m["files"]], "volume": lin(-14), "pitchJitter": 0.05,
        "category": "footstep", "volume_db": -14, "channels": 1, "status": "proposed",
        "desc": f"Single footsteps on {surface.replace('_', ' ')}; pick at random, never the same twice in a row, "
                "volume +-2 dB, one per foot plant (anim event).",
        "sources": m["sources"], "unity_import": UNITY["short"] | {"forceToMono": True},
    }
    sections["footsteps"][surface] = e
    ids[id_] = {"section": "footsteps", "key": surface, "sources": e["sources"]}

AMB_DESC = {
    "amb_timeline_square": ("timeline_square", "City plaza crowd: chatter, footsteps, distant city"),
    "amb_terminal_cafe": ("terminal_cafe", "Cafe murmur + mechanical keyboard clatter (traders)"),
    "amb_airdrop_farm": ("airdrop_farm", "Farm birds and countryside"),
    "amb_dubai_marina": ("dubai_marina", "Marina water lapping, gulls, rigging in the wind"),
    "amb_ore_refinery": ("ore_refinery", "Industrial refinery: machine hum, compressor, furnace roar"),
    "amb_perp_pit": ("perp_pit", "Busy trading floor crowd + keyboards + server hum"),
    "amb_rekt_clinic": ("rekt_clinic", "Quiet ward: room tone, heart-monitor beeps, muffled voices"),
    "amb_spaces_studio": ("spaces_studio", "Treated studio room tone (voice-only venue, very quiet)"),
    "amb_club_doors": ("club_doors", "Outside a club: bass muffled behind closed doors + crowd (built from mus_club). "
                                     "Not a venue id; play it near club entrances."),
}
for id_, m in amb.items():
    key, desc = AMB_DESC[id_]
    q = qa_of(m["path"])
    e = {
        "id": id_, "file": rel(m["path"]), "volume": 1.0, "category": "ambience", "venue_status":
        "venue id in contract" if key in CONTRACT_VENUES else ("venue id (later phase)" if key in ("airdrop_farm", "dubai_marina") else "not a venue id"),
        "channels": 2, "loop": True, "loop_start_sample": 0, "loop_end_sample": m["samples"], "duration_s": q.get("dur"),
        "loudness_lufs": q.get("I"), "true_peak_dbtp": q.get("TP"), "status": "proposed", "desc": desc,
        "sources": m["sources"] or ["own work (synthesised)"], "unity_import": UNITY["loop"],
    }
    sections["ambience"][key] = e
    ids[id_] = {"section": "ambience", "key": key, "sources": e["sources"]}

# client district ids (client/Assets/Scripts/Core/Data/Registries.cs) + venue-specific sets from the spec's tempo table
MUS_KEY = {"timeline_square": ("timeline", "Timeline Square / Timeline District"), "trenches": ("trenches", "The Trenches"),
           "perp_pit": ("perp_pit", "Perp Pit (venue-specific, spec tempo table)"), "airdrop_farm": ("airdrop_farm", "Airdrop Farm"),
           "builder_hills": ("builder_hills", "Builder Hills"), "dubai_marina": ("dubai_marina", "Dubai Marina"),
           "club": ("clubs", "Clubs and parties")}
for d, m in mus.items():
    key, label = MUS_KEY[d]
    full = m["files"]["full"]
    q = qa_of(full)
    e = {
        "id": f"mus_{d}", "bpm": m["bpm"], "bars": m["bars"], "time_signature": "4/4", "district": label,
        "stems": {s: rel(m["files"][s]) for s in ("pad", "bass", "drums", "lead", "tension")},
        "full_mix_preview": rel(full), "loop": True, "loop_samples": m["loop_samples"], "sample_rate": m["sr"],
        "loop_seconds": m["loop_seconds"], "full_mix_lufs": q.get("I"), "full_mix_true_peak_dbtp": q.get("TP"),
        "status": "proposed", "sources": ["own work (composed and synthesised, tools/gen_music.py)"],
        "unity_import": UNITY["music"],
    }
    sections["music"][key] = e
    ids[f"mus_{d}"] = {"section": "music", "key": key, "sources": e["sources"]}

# Authored/licensed music choices survive regeneration of the older synthesized build reports.
curation_path = ROOT / "music-curation.json"
curated_music = json.loads(curation_path.read_text()) if curation_path.exists() else {}
for key, entry in curated_music.items():
    sections["music"][key] = entry
    ids[entry["id"]] = {"section": "music", "key": key, "sources": entry["sources"]}

# Sourced UI replacements keep the existing IDs and survive regeneration of synthetic reports.
sfx_curation_path = ROOT / "sfx-curation.json"
for key, entry in (json.loads(sfx_curation_path.read_text()) if sfx_curation_path.exists() else {}).items():
    sections["sfx"][key] = entry
    ids[key] = {"section": "sfx", "key": key, "sources": entry["sources"]}

for name, b in babble["banks"].items():
    key = "default" if name == "generic" else name
    letters = {k: rel(v) for k, v in sorted(b["files"].items())}
    e = {
        "id": f"babble_{name}", "files": list(letters.values()), "letters": letters,
        "volume": lin(b["volume_db"]), "volume_db": b["volume_db"], "chars_per_second": b["chars_per_second"],
        "pitch_jitter_semitones": b["pitch_jitter_semitones"], "npc_pitch_range_semitones": b["npc_pitch_range_semitones"],
        "syllable_ms": b["syllable_ms"], "archetype_status": b["phase"], "status": "proposed",
        "sources": ["own work (formant synthesis, tools/gen_babble.py)"], "unity_import": UNITY["short"],
    }
    sections["babble"][key] = e
    ids[f"babble_{name}"] = {"section": "babble", "key": key, "sources": e["sources"]}

out = {
    "version": 1,
    "generated": TODAY,
    "paths_relative_to": "audio/processed/",
    "_notes": [
        "Shape follows the unity -> audio request (docs/progress/requests.md); extra keys are informational.",
        "sfx keys are sfx ids. Only the five marked status=in_contract exist in contract/ids/sfx.json so far; the rest "
        "are proposals for FE to add. footsteps keys = surface, ambience keys = venue ids (plus club_doors), music keys = "
        "client district ids (timeline, trenches, builder_hills, airdrop_farm, dubai_marina) plus perp_pit and clubs, "
        "babble keys = archetype ids plus default.",
        "volume is linear (0..1) = 10^(volume_db/20). Every entry also carries its prefixed id (sfx_, foot_, amb_, mus_, babble_).",
        "One-shot SFX and footsteps are normalised to -1 dBTP true peak (consistent peaks); mix with volume.",
        "Ambience loops: -24 LUFS integrated, true peak <= -3 dBTP (Spaces Studio room tone -36 LUFS).",
        "Music: each set's full mix (pad+bass+drums+lead) is -17 LUFS integrated, true peak <= -2 dBTP; all stems of a "
        "set have identical sample length. market_mood rules: audio/README.md.",
        "Entries with full_mix use a complete licensed composition, not stems; music-curation.json owns these overrides.",
        "Babble: files are ordered a..z (letters map); play rules in audio/BABBLE.md and audio/unity/BabblePlayer.cs.",
    ],
    "index": {k: f"{v['section']}.{v['key']}" for k, v in sorted(ids.items())},
    **{k: dict(sorted(v.items())) for k, v in sections.items()},
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
    "Rule (docs/AGENTS-BRIEF.md): prefer CC0; record attribution for CC-BY assets. Licensed composed music below "
    "replaces the rejected synthesized district selections and requires its shipped credit.",
    "",
    "## Own work (CC0 dedication by the CT Life project)",
    "",
    "Synthesised in Python (numpy/scipy) by the scripts in `audio/tools/`; no samples involved unless listed below:",
    "",
    "- Original synthesized district stems/previews (`processed/music/mus_*.ogg`) — `tools/gen_music.py`. "
    "Licensed full compositions in `processed/music/licensed/` are excluded from this dedication.",
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
if curated_music:
    lines += ["", "## Licensed complete compositions — CC-BY 4.0", "",
              "[Incompetech licence page](https://incompetech.com/music/royalty-free/licenses/) offers free use with credit. "
              "[CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) permits commercial reuse with attribution and a modification notice.",
              "", "| Track | Author | Source | Licence | Downloaded | Use / modifications |",
              "| --- | --- | --- | --- | --- | --- |"]
    seen_music = set()
    for entry in curated_music.values():
        if entry["full_mix"] in seen_music:
            continue
        seen_music.add(entry["full_mix"])
        uses = ", ".join(key for key, other in curated_music.items() if other["full_mix"] == entry["full_mix"])
        lines.append(f"| {entry['title']} | {entry['artist']} | {entry['source_url']} | {entry['license']} | "
                     f"{entry.get('date_downloaded', '2026-10-05')} | {uses}; {entry['processing']} |")
    lines.append("| Funkorama | Kevin MacLeod | https://incompetech.com/music/royalty-free/index.html?isrc=USUAN1100474 | "
                 "CC-BY-4.0 | 2026-10-05 | Downloaded candidate only, not shipped; source MP3 decoder overread warnings. |")
    lines += ["", "Required attribution ships in `processed/Music-Credits.txt` and the app's Settings → Credits & licenses. "
              "Original download hashes and current primary-source evidence are in `sources/incompetech/`.", ""]
(ROOT / "LICENSES.md").write_text("\n".join(lines))
print(f"sfx-map.json: {len(ids)} ids; LICENSES.md: {len(src.get('freesound', {}))} freesound + {len(src.get('kenney', {}))} kenney rows")
