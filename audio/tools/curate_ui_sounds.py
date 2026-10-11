#!/usr/bin/env python3
"""Build the six frequent UI cues from the existing CC0 Kenney pack, without synthesis.

2026-10-09 (audio integration lane): files are peak-normalised to -1 dBFS (was -3; error_001 ended at -6) and the
map gains below come from tools/mix_audit.py so every onboarding cue clears the title music in the 1-6 kHz band at
default settings (the old -14..-18 dB entry gains left them 10-20 dB under the music: inaudible on a Seeker)."""
import hashlib
import json
from pathlib import Path
import subprocess
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
CHOICES = {
    "sfx_ui_tap": ("click_003", -3, "short dry interaction click"),
    "sfx_ui_toast": ("tick_002", -2, "quiet notification tick"),
    "sfx_ui_toggle_on": ("toggle_001", -7, "toggle-on cue from paired Kenney family"),
    "sfx_ui_toggle_off": ("toggle_002", -12.5, "toggle-off cue from paired Kenney family"),
    "sfx_ui_success": ("confirmation_001", -3, "confirmation cue distinct from error and ordinary tap"),
    "sfx_ui_error": ("error_001", -1, "error cue distinct from success and ordinary tap"),
}


def decode(path):
    r = subprocess.run(["ffmpeg", "-nostdin", "-v", "error", "-i", str(path), "-ar", "48000", "-ac", "1", "-f", "f32le", "-"], capture_output=True, check=True)
    return np.frombuffer(r.stdout, dtype="<f4")


entries, evidence = {}, {}
# Presence EQ (ffmpeg `equalizer`) for cues whose energy sits under the music: error_001 is a low blip that the title
# music masked in the 1-6 kHz band (tools/mix_audit.py). +6 dB around 2.2 kHz keeps the timbre, adds edge.
PRESENCE = {"sfx_ui_error": "equalizer=f=2200:t=q:w=0.8:g=6"}

for sound_id, (name, bus_gain_db, description) in CHOICES.items():
    source = ROOT / "raw/kenney/interface-sounds/Audio" / (name + ".ogg")
    output = ROOT / "processed/sfx/licensed" / (sound_id + "_kenney.wav")
    output.parent.mkdir(parents=True, exist_ok=True)
    pcm = decode(source)
    # Lossy source decoding can exceed full scale. Attenuate in float before writing PCM16.
    gain_db = -1 - float(20*np.log10(np.abs(pcm).max())) - (6 if sound_id in PRESENCE else 0)
    for _ in range(4):  # the downmix/resample of the written file can land off target; correct from the actual output
        af = (PRESENCE[sound_id] + "," if sound_id in PRESENCE else "") + f"volume={gain_db:.6f}dB"
        subprocess.run(["ffmpeg", "-y", "-nostdin", "-v", "error", "-i", str(source), "-af", af,
                        "-ar", "48000", "-ac", "1", "-c:a", "pcm_s16le", str(output)], check=True)
        result = decode(output)
        off = -1 - float(20*np.log10(np.abs(result).max()))
        if abs(off) < 0.15:
            break
        gain_db += off - 0.05
    peak = float(20*np.log10(np.abs(result).max()))
    assert np.isfinite(result).all() and peak < -0.9 and len(result) == len(pcm)
    entries[sound_id] = {"files": [str(output.relative_to(ROOT / "processed"))], "volume": round(10**(bus_gain_db/20), 6),
                         "pitchJitter": 0.0, "category": "ui", "volume_db": bus_gain_db, "channels": 1, "loop": False,
                         "duration_s": len(result)/48000, "sample_peak_dbfs": peak, "ducks_music": False,
                         "status": "existing_FE_proposal_not_new_contract_id", "desc": description,
                         "sources": [str(source.relative_to(ROOT))], "source_page": "https://kenney.nl/assets/interface-sounds",
                         "license": "CC0-1.0", "processing": (f"{PRESENCE[sound_id]} then " if sound_id in PRESENCE else "") +
                         f"constant {gain_db:.6f}dB gain (peak -1 dBFS); 48kHz mono PCM16, duration unchanged",
                         "unity_import": {"loadType": "DecompressOnLoad", "compressionFormat": "Vorbis", "quality": 0.6, "preloadAudioData": True}}
    evidence[sound_id] = {"source_sha256": hashlib.sha256(source.read_bytes()).hexdigest(),
                          "processed_sha256": hashlib.sha256(output.read_bytes()).hexdigest(), "processed_bytes": output.stat().st_size,
                          "duration_s": len(result)/48000, "sample_peak_dbfs": peak,
                          "maximum_post_entry_gain_dbfs": peak+bus_gain_db,
                          "source_decode_peak_dbfs": float(20*np.log10(np.abs(pcm).max())),
                          "full_scale_samples": int(np.sum(np.abs(result)>=1))}
(ROOT / "sfx-curation.json").write_text(json.dumps(entries, indent=2)+"\n")
(ROOT / "sources/kenney-ui-metrics-20261009.json").write_text(json.dumps(evidence, indent=2)+"\n")
print(json.dumps(evidence, indent=2))
