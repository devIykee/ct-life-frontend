#!/usr/bin/env python3
"""Vehicle sounds for CT Life (Feel lane F2/F3, 2026-10-17): real recordings only, all CC0 on Freesound.

Downloads the HQ previews (see freesound.py: originals need a login; previews carry the same licence), cuts loops from the
steadiest stretch of each recording with an equal-power crossfade, trims one-shots, normalises (loops to an RMS target,
one-shots to a -1 dBFS peak), resamples to 48 kHz mono 16-bit and writes
client/Assets/Resources/Vehicles/Audio/<name>.wav plus provenance in client/Assets/Resources/Vehicles/Audio/sources.json.
The files live outside Resources/Audio on purpose: ship_audio.py prunes everything there that sfx-map.json does not list.

Usage: ../.venv/bin/python curate_vehicle_audio.py
"""
import json
import time
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy.signal import resample_poly

import freesound

TOOLS = Path(__file__).resolve().parent
AUDIO = TOOLS.parent
RAW = AUDIO / "raw" / "freesound"
OUT = AUDIO.parent / "client" / "Assets" / "Resources" / "Vehicles" / "Audio"
SR = 48000

# name: (freesound id, kind, seconds, extra)
SOURCES = {
    "engine_car": (369053, "loop", 2.4, "car idle, exterior; pitched with rpm at runtime"),
    "engine_start": (401558, "oneshot", 2.4, "ignition and catch"),
    "engine_ev": (832904, "loop", 2.6, "electric vehicle driving (the Roadstar on the road)"),
    "engine_scooter": (55035, "loop", 2.4, "moped engine"),
    "scooter_start": (127592, "oneshot", 1.8, "moped start (first event of the take)"),
    "rotor_heli": (854493, "loop", 2.4, "UH-60 hover loop"),
    "rotor_drone": (246351, "loop", 2.5, "Parrot AR.Drone 2.0 hovering"),
    "hover_hum": (700034, "loop", 2.2, "electric train motor idle (the Roadstar's hover pads)"),
    "jet_thrust": (534856, "loop", 2.6, "jet engine (the Roadstar's thrust)"),
    "skid": (71737, "loop", 2.0, "tyre squeal"),
    "impact": (592388, "oneshot", 2.2, "car crash with glass"),
    "door_open": (556691, "event1", 1.0, "car door open (first event)"),
    "door_close": (556691, "event_last", 1.0, "car door shut (loudest later event)"),
    # Feel lane F3: traffic
    "horn": (871628, "oneshot", 0.9, "car horn, Skoda Fabia (traffic honks at the player)"),
}
LOOP_RMS_DB = -20.0
XFADE = 0.25


def load(sid: int):
    RAW.mkdir(parents=True, exist_ok=True)
    path = RAW / f"{sid}.ogg"
    meta_path = RAW / f"{sid}.json"
    if not path.exists() or not meta_path.exists():
        meta = freesound.download(sid, str(path))
        meta_path.write_text(json.dumps(meta, indent=1))
        time.sleep(1.5)
    meta = json.loads(meta_path.read_text())
    x, sr = sf.read(str(path), always_2d=True)
    x = x.mean(axis=1)
    if sr != SR:
        g = np.gcd(sr, SR)
        x = resample_poly(x, SR // g, sr // g)
    return x.astype(np.float64), meta


def rms(x):
    return float(np.sqrt(np.mean(x * x) + 1e-12))


def steadiest(x, seconds):
    """Start of the window whose short-term loudness varies least (and is not near-silent)."""
    n = int(seconds * SR)
    if len(x) <= n + int(XFADE * SR):
        return 0
    hop = SR // 20
    frame = SR // 10
    env = np.array([rms(x[i:i + frame]) for i in range(0, len(x) - frame, hop)])
    level = np.median(env)
    best, best_s = 0, 1e9
    w = n // hop
    for s in range(0, len(env) - w):
        seg = env[s:s + w]
        if seg.mean() < 0.35 * level:
            continue
        score = seg.std() / (seg.mean() + 1e-9)
        if score < best_s:
            best_s, best = score, s
    return best * hop


def loop(x, seconds):
    n = int(seconds * SR); f = int(XFADE * SR)
    s = steadiest(x, seconds + XFADE)
    seg = x[s:s + n + f].copy()
    if len(seg) < n + f:
        seg = np.pad(seg, (0, n + f - len(seg)))
    head, tail = seg[:f], seg[n:n + f]
    t = np.linspace(0, 1, f)
    seg[:f] = head * np.sqrt(t) + tail * np.sqrt(1 - t)   # the tail fades into the head: seamless at the loop point
    out = seg[:n]
    out = out - out.mean()
    return out * (10 ** (LOOP_RMS_DB / 20) / rms(out)), {"window_s": [round(s / SR, 3), round((s + n) / SR, 3)], "xfade_s": XFADE}


def onsets(x):
    hop = SR // 100
    env = np.array([rms(x[i:i + hop]) for i in range(0, len(x) - hop, hop)])
    thr = max(env.max() * 0.25, np.median(env) * 3)
    on = []
    for i in range(1, len(env)):
        if env[i] >= thr and env[i - 1] < thr and (not on or i - on[-1] > 30):
            on.append(i)
    return [o * hop for o in on], env


def oneshot(x, seconds, which="first"):
    starts, env = onsets(x)
    if not starts:
        starts = [0]
    if which == "event_last" and len(starts) > 1:
        hop = SR // 100
        later = starts[1:]
        start = max(later, key=lambda s: env[s // hop: s // hop + 30].max())
    else:
        start = starts[0]
    start = max(0, start - int(0.02 * SR))
    seg = x[start:start + int(seconds * SR)].copy()
    fade = int(0.12 * SR)
    if len(seg) > fade:
        seg[-fade:] *= np.linspace(1, 0, fade)
    seg = seg - seg.mean()
    peak = np.abs(seg).max() + 1e-9
    return seg * (10 ** (-1 / 20) / peak), {"start_s": round(start / SR, 3), "events_found": len(starts)}


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    provenance = {}
    for name, (sid, kind, seconds, note) in SOURCES.items():
        x, meta = load(sid)
        if kind == "loop":
            y, edit = loop(x, seconds)
        else:
            y, edit = oneshot(x, seconds, "event_last" if kind == "event_last" else "first")
        y = np.clip(y, -1, 1)
        sf.write(str(OUT / f"{name}.wav"), y.astype(np.float32), SR, subtype="PCM_16")
        provenance[name] = {
            "file": f"Vehicles/Audio/{name}.wav", "kind": "loop" if kind == "loop" else "oneshot", "seconds": round(len(y) / SR, 3),
            "freesound_id": sid, "title": meta.get("title"), "author": meta.get("author"), "url": meta.get("url"),
            "license": meta.get("license"), "source_file": "Freesound HQ preview (Ogg Vorbis)", "note": note,
            "edit": dict(edit, sr=SR, channels=1, normalised=f"{LOOP_RMS_DB} dBFS RMS" if kind == "loop" else "-1 dBFS peak"),
            "checked": "2026-10-17",
        }
        print(name, sid, meta.get("license"), meta.get("title"), json.dumps(provenance[name]["edit"]))
    (OUT / "sources.json").write_text(json.dumps(provenance, indent=1))
    print("wrote", OUT / "sources.json")


if __name__ == "__main__":
    main()
