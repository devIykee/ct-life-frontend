#!/usr/bin/env python3
"""Fetch credited full compositions and measure/level-match them; never creates music.

No account, generated arrangement, tempo change, musical edit or listening approval.
Raw downloads and technical provenance stay under audio/; root syncs runtime assets.
"""
import concurrent.futures
import datetime
import hashlib
import json
from pathlib import Path
import re
import subprocess
import urllib.parse
import urllib.request

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
TODAY = datetime.date.today().isoformat()
EVIDENCE = ROOT / "sources/incompetech"
CHOICES = {
    "trenches": ("Got Funk", "mus_trenches", "The Trenches", "grounded bass-and-horns groove"),
    "perp_pit": ("Getting it Done", "mus_perp_pit", "Perp Pit", "light electronic dance pulse"),
    "builder_hills": ("Pamgaea", "mus_builder_hills", "Builder Hills", "relaxed contrasting synth textures"),
    "dubai_marina": ("Paradise Found", "mus_dubai_marina", "Dubai Marina", "bright tropical-house waterfront"),
    "clubs": ("Disco con Tutti", "mus_club", "Clubs and parties", "funk/disco horns for social energy"),
    "airdrop_farm": ("Easy Lemon", "mus_airdrop_farm", "Airdrop Farm", "light guitar/mallet contrast to city electronics"),
    "terminal_cafe": ("Airport Lounge", "mus_terminal_cafe", "Terminal Café", "airy electric-piano/vibraphone social background"),
}


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(args):
    result = subprocess.run(args, capture_output=True)
    if result.returncode:
        raise RuntimeError(result.stderr.decode(errors="replace"))
    return result


def meter(path):
    r = run(["ffmpeg", "-nostdin", "-hide_banner", "-i", str(path), "-af",
             "loudnorm=I=-18:TP=-2:LRA=11:print_format=json", "-f", "null", "-"])
    log = r.stderr.decode(errors="replace")
    match = re.findall(r'\{\s*"input_i".*?\}', log, re.S)[-1]
    return json.loads(match), log


def pcm_metrics(path):
    r = run(["ffmpeg", "-nostdin", "-hide_banner", "-v", "warning", "-i", str(path),
             "-ar", "48000", "-ac", "2", "-f", "f32le", "-"])
    a = np.frombuffer(r.stdout, dtype="<f4").reshape(-1, 2)
    assert len(a) > 48000 * 60 and np.isfinite(a).all()
    peak = float(np.abs(a).max())
    return {"duration_seconds": len(a) / 48000, "sample_peak_dbfs": float(20*np.log10(peak)),
            "sample_rms_dbfs": float(10*np.log10(np.mean(a.astype(np.float64)**2))),
            "full_scale_samples": int(np.sum(np.abs(a) >= 1)),
            "decoder_warnings": r.stderr.decode(errors="replace").strip()}


def fetch(row):
    path = ROOT / "raw/incompetech" / row["filename"]
    url = "https://incompetech.com/music/royalty-free/mp3-royaltyfree/" + urllib.parse.quote(row["filename"])
    path.parent.mkdir(parents=True, exist_ok=True)
    if not path.exists():
        with urllib.request.urlopen(url, timeout=60) as response:
            path.write_bytes(response.read())
    return path, url


def main():
    EVIDENCE.mkdir(parents=True, exist_ok=True)
    catalog_bytes = urllib.request.urlopen("https://incompetech.com/music/royalty-free/pieces.json", timeout=30).read()
    catalog = json.loads(catalog_bytes)
    rows = {key: next(row for row in catalog if row.get("title") == choice[0]) for key, choice in CHOICES.items()}
    (EVIDENCE / "catalog-selection-20261006.json").write_text(json.dumps(rows, indent=2) + "\n")
    license_bytes = urllib.request.urlopen("https://incompetech.com/music/royalty-free/licenses/", timeout=30).read()
    (EVIDENCE / "license-page-20261006.html").write_bytes(license_bytes)
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
        fetched = dict(zip(rows, pool.map(fetch, rows.values())))
    curation = json.loads((ROOT / "music-curation.json").read_text())
    report = {}
    for key, row in rows.items():
        source, url = fetched[key]
        slug = re.sub(r"[^a-z0-9]+", "_", row["title"].lower()).strip("_")
        dest = ROOT / "processed/music/licensed" / ("mus_" + key + "_" + slug + ".ogg")
        before, before_log = meter(source)
        gain = min(-18 - float(before["input_i"]), -2.5 - float(before["input_tp"]))
        converted = run(["ffmpeg", "-y", "-nostdin", "-hide_banner", "-i", str(source), "-map_metadata", "-1",
                         "-af", f"volume={gain:.5f}dB", "-ar", "48000", "-ac", "2", "-c:a", "libvorbis", "-q:a", "6", str(dest)])
        after, after_log = meter(dest)
        pcm = pcm_metrics(dest)
        assert pcm["full_scale_samples"] == 0 and pcm["sample_peak_dbfs"] < -1
        assert -19 <= float(after["input_i"]) <= -17
        # A warning must be reviewed; the script must not silently publish a corrupted candidate.
        source_decode = run(["ffmpeg", "-nostdin", "-hide_banner", "-v", "warning", "-i", str(source), "-f", "null", "-"])
        warnings = source_decode.stderr.decode(errors="replace").strip()
        log_name = dest.stem + "-conversion-20261006.log"
        (EVIDENCE / log_name).write_text(before_log + converted.stderr.decode(errors="replace") + after_log)
        entry = {"id": CHOICES[key][1], "bpm": float(row["bpm"]), "district": CHOICES[key][2],
                 "full_mix": str(dest.relative_to(ROOT / "processed")), "volume": 1.0, "loop": True,
                 "loop_kind": "complete_composition_with_original_ending_and_intro",
                 "duration_seconds": pcm["duration_seconds"], "sample_rate": 48000, "channels": 2,
                 "title": row["title"], "artist": "Kevin MacLeod", "license": "CC-BY-4.0",
                 "license_url": "https://creativecommons.org/licenses/by/4.0/",
                 "source_url": "https://incompetech.com/music/royalty-free/index.html?isrc=" + row["isrc"],
                 "source_file": str(source.relative_to(ROOT)), "download_url": url,
                 "processing": f"Constant {gain:.5f}dB gain, 48kHz stereo Vorbis q6; no tempo or arrangement change",
                 "selection_rationale": CHOICES[key][3] + "; based on composer metadata, not audition approval",
                 "sources": [str(source.relative_to(ROOT))], "date_downloaded": TODAY,
                 "unity_import": {"loadType": "Streaming", "compressionFormat": "Vorbis", "quality": 0.45,
                                  "preloadAudioData": False, "loadInBackground": True}}
        report[key] = {"title": row["title"], "source_bytes": source.stat().st_size, "source_sha256": sha(source),
                       "processed_bytes": dest.stat().st_size, "processed_sha256": sha(dest),
                       "source_loudness": before, "processed_loudness": after, "constant_gain_db": gain,
                       "source_decode_warnings": warnings, **pcm}
        curation[key] = entry
        print(key, row["title"], round(pcm["duration_seconds"], 3), "seconds", after["input_i"], "LUFS", "WARNINGS=" + str(bool(warnings)), flush=True)
    for entry in curation.values():
        entry["unity_import"] = {"loadType": "Streaming", "compressionFormat": "Vorbis", "quality": 0.45,
                                 "preloadAudioData": False, "loadInBackground": True}
    (EVIDENCE / "district-music-metrics-20261006.json").write_text(json.dumps(report, indent=2) + "\n")
    (ROOT / "music-curation.json").write_text(json.dumps(curation, indent=2) + "\n")
    credits = ["CT Life — licensed music", "", "Kevin MacLeod (incompetech.com)",
               "Licensed under Creative Commons: By Attribution 4.0 License", "https://creativecommons.org/licenses/by/4.0/", ""]
    for path in dict.fromkeys(e["full_mix"] for e in curation.values()):
        matches = [(key, e) for key, e in curation.items() if e["full_mix"] == path]
        entry = matches[0][1]
        credits.extend([entry["title"], entry["source_url"], "Used in: " + ", ".join(k for k, _ in matches),
                        "Modifications: " + entry["processing"], ""])
    credits.extend(["Complete original arrangements, tempo, intros and endings are retained. The game repeats each composition and applies user gain, venue transitions and voice ducking.",
                    "No composer endorsement is implied. Music remains available under CC BY 4.0 independently of the game. Licence text is included with the game's Creative Commons notices."])
    (ROOT / "processed/Music-Credits.txt").write_text("\n".join(credits) + "\n")


if __name__ == "__main__":
    main()
