#!/usr/bin/env python3
"""Download every third-party CC0 source into audio/raw/ and record provenance in tools/sources.json.

Re-runnable: skips files that already exist. raw/ is gitignored; sources.json is committed so
LICENSES.md and the build are reproducible.
"""
from __future__ import annotations

import datetime as dt
import json
import time
import urllib.request
import zipfile
from pathlib import Path

import freesound

ROOT = Path(__file__).resolve().parents[1]
RAW = ROOT / "raw"
MANIFEST = Path(__file__).with_name("sources.json")

KENNEY = {
    "impact-sounds": "https://kenney.nl/media/pages/assets/impact-sounds/87b4ddecda-1677589768/kenney_impact-sounds.zip",
    "interface-sounds": "https://kenney.nl/media/pages/assets/interface-sounds/fa43c1dd4d-1677589452/kenney_interface-sounds.zip",
    "casino-audio": "https://kenney.nl/media/pages/assets/casino-audio/2472606a04-1721639069/kenney_casino-audio.zip",
    "ui-audio": "https://kenney.nl/media/pages/assets/ui-audio/490d233f68-1677590494/kenney_ui-audio.zip",
    "rpg-audio": "https://kenney.nl/media/pages/assets/rpg-audio/8e99002d76-1677590336/kenney_rpg-audio.zip",
    "digital-audio": "https://kenney.nl/media/pages/assets/digital-audio/216eac4753-1677590265/kenney_digital-audio.zip",
    "sci-fi-sounds": "https://kenney.nl/media/pages/assets/sci-fi-sounds/6b296f9ecf-1677589334/kenney_sci-fi-sounds.zip",
    "music-jingles": "https://kenney.nl/media/pages/assets/music-jingles/f37e530b9e-1677590399/kenney_music-jingles.zip",
}

# freesound id -> purpose (all filtered to CC0 at search time and re-checked on download)
FREESOUND = {
    # footsteps
    698697: "foot_metal_grate", 834029: "foot_metal_grate", 786977: "foot_metal_grate",
    834027: "foot_neon_tile", 170506: "foot_neon_tile", 218295: "foot_neon_tile",
    # ambience
    640625: "amb_timeline_square", 757838: "amb_timeline_square",
    437461: "amb_terminal_cafe", 450282: "amb_terminal_cafe", 546167: "amb_terminal_cafe", 626952: "amb_terminal_cafe",
    488050: "amb_airdrop_farm", 488064: "amb_airdrop_farm",
    457215: "amb_dubai_marina", 328808: "amb_dubai_marina", 360633: "amb_dubai_marina",
    450126: "amb_ore_refinery", 478118: "amb_ore_refinery", 415156: "amb_ore_refinery",
    640630: "amb_club_doors",
    # one-shots
    555161: "sfx_rug", 688589: "sfx_rug",
    221528: "sfx_liquidation", 758207: "sfx_liquidation",
    181949: "sfx_whale_horn", 636075: "sfx_whale_horn",
    857938: "sfx_sunday_unlock", 378799: "sfx_sunday_unlock",
    447941: "sfx_ore_furnace_whoosh", 459884: "sfx_ore_furnace_whoosh",
    131930: "sfx_moon_horn",
}


def main() -> None:
    RAW.mkdir(exist_ok=True)
    manifest = json.loads(MANIFEST.read_text()) if MANIFEST.exists() else {"kenney": {}, "freesound": {}}
    today = dt.date.today().isoformat()

    kdir = RAW / "kenney"
    kdir.mkdir(exist_ok=True)
    for name, url in KENNEY.items():
        z = kdir / f"{name}.zip"
        if not z.exists():
            urllib.request.urlretrieve(url, z)
        with zipfile.ZipFile(z) as zf:
            zf.extractall(kdir / name)
        lic = (kdir / name / "License.txt").read_text(errors="replace")
        assert "Creative Commons Zero, CC0" in lic, f"{name}: licence text changed"
        manifest["kenney"].setdefault(name, {
            "title": f"Kenney {name.replace('-', ' ').title()}", "author": "Kenney (kenney.nl)",
            "url": f"https://kenney.nl/assets/{name}", "download": url, "license": "CC0-1.0",
            "license_evidence": "License.txt in zip: 'License: (Creative Commons Zero, CC0)'", "date": today})

    fdir = RAW / "freesound"
    fdir.mkdir(exist_ok=True)
    for sid, purpose in FREESOUND.items():
        key = str(sid)
        dest = fdir / f"{sid}.ogg"
        if dest.exists() and key in manifest["freesound"]:
            continue
        meta = freesound.download(sid, str(dest))
        meta.update({"purpose": purpose, "date": today, "file": str(dest.relative_to(ROOT)),
                     "note": "Freesound HQ preview (Ogg Vorbis 192 kbps); originals need a login we do not have."})
        manifest["freesound"][key] = meta
        print(f"{sid} {meta['license']} {meta['author']}: {meta['title']}")
        MANIFEST.write_text(json.dumps(manifest, indent=2, ensure_ascii=False))
        time.sleep(2)
    MANIFEST.write_text(json.dumps(manifest, indent=2, ensure_ascii=False))
    print("kenney packs:", len(manifest["kenney"]), "freesound sounds:", len(manifest["freesound"]))


if __name__ == "__main__":
    main()
