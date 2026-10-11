#!/usr/bin/env python3
"""Red-market tension bed for music sets that are complete licensed compositions (no stems).

The spec's adaptive rule ("red low-passes the mix and adds a tension layer") needs a tension layer that sits under
ANY of the eight licensed tracks, whatever their key or tempo. So this layer has no pitch centre and no beat:
- storm wind: pink noise through a slowly sweeping band-pass (150-650 Hz),
- sub rumble: brown noise low-passed at 70 Hz,
- distant terminal alerts: sparse random high blips (tempo-free Poisson timing, random pitch 2-4 kHz),
- two far-off siren swells per loop (gliding, heavily filtered and reverberant, so no stable pitch).
Everything is rendered circularly (three copies, keep the middle) so the 24 s loop is seamless.
Own work (CC0). Output: processed/music/mood/mus_mood_tension_bed.ogg (stereo, 48 kHz, libvorbis q4),
loudness -26 LUFS integrated (under the -18 LUFS tracks; MusicDirector fades it in from mood 0 to -0.5).
"""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np

import dsp
from dsp import SR, ns, filt
from master import BUILD, OUT, encode_ogg, export_wav, lufs, true_peak_db

LOOP_S = 24.0
TARGET_LUFS = -26.0


def circular(fn, n: int) -> np.ndarray:
    """Render fn over 3n samples of periodic input and keep the middle third: exactly periodic, so the loop is seamless."""
    y = fn(3 * n)
    return y[n:2 * n]


def periodic_noise(L: int, color: str, rng: np.random.Generator) -> np.ndarray:
    """White noise of one loop length tiled three times, then coloured: the middle third is exactly periodic."""
    from scipy import signal
    w = np.tile(rng.standard_normal(L), 3)
    if color == "pink":
        y = signal.lfilter([0.049922035, -0.095993537, 0.050612699, -0.004408786], [1, -2.494956002, 2.017265875, -0.522189400], w)
    elif color == "brown":
        y = signal.lfilter([1], [1, -0.995], w)
        y = y - np.mean(y)
    else:
        y = w / 3.0
    return y / (np.max(np.abs(y)) + 1e-9) * 0.9


def bed() -> np.ndarray:
    L = ns(LOOP_S)
    rng = np.random.default_rng(2026_10_08)
    t3 = np.arange(3 * L) / SR

    def wind(n3, seed=1):
        x = periodic_noise(L, "pink", np.random.default_rng(seed))
        lfo = 0.5 + 0.5 * np.sin(2 * np.pi * t3[:n3] / (LOOP_S / 2) + 0.7)      # 2 cycles per loop: periodic
        fc = 150 * (650 / 150) ** lfo
        y = dsp.sweep_filter(x, "bp", fc, q=1.4, block=128)
        amp = 0.6 + 0.4 * np.sin(2 * np.pi * t3[:n3] / (LOOP_S / 3) + 1.9)
        return y * amp

    def rumble(n3):
        return filt(periodic_noise(L, "brown", np.random.default_rng(7)), "lp", 70, order=4) * 2.5

    sub = circular(rumble, L) * 0.8
    left = circular(lambda n3: wind(n3, 1), L) + sub
    right = circular(lambda n3: wind(n3, 2), L) * 0.9 + sub
    x = np.stack([left, right], axis=1)

    # sparse terminal alerts (tempo-free), placed circularly
    alerts = np.zeros((L, 2))
    t = 0.0
    while True:
        t += rng.exponential(1 / 1.3)
        if t >= LOOP_S:
            break
        f = rng.uniform(2000, 4000)
        n = ns(0.07)
        tt = np.arange(n) / SR
        b = np.sin(2 * np.pi * f * tt) * np.exp(-tt / 0.02)
        p = rng.uniform(-0.8, 0.8)
        s = ns(t)
        idx = (np.arange(n) + s) % L
        alerts[idx, 0] += b * np.sqrt((1 - p) / 2) * rng.uniform(0.04, 0.1)
        alerts[idx, 1] += b * np.sqrt((1 + p) / 2) * rng.uniform(0.04, 0.1)
    x += alerts

    # two distant siren swells (gliding; no stable pitch)
    for start in (3.0, 15.0):
        n = ns(5.0)
        tt = np.arange(n) / SR
        f = 620 + 260 * np.sin(2 * np.pi * 0.45 * tt) ** 2
        s_ = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.sin(np.pi * tt / 5.0) ** 2
        s_ = filt(s_, "lp", 1400) * 0.05
        idx = (np.arange(n) + ns(start)) % L
        x[idx, 0] += s_ * 0.8
        x[idx, 1] += s_
    y = dsp.reverb(np.concatenate([x, x, x], axis=0), wet=0.35, decay=3.0, predelay=0.02, damp=3000)
    return y[L:2 * L]


def main() -> None:
    x = bed()
    x = x * dsp.db(TARGET_LUFS - lufs(x))
    tp = true_peak_db(x)
    if tp > -3.0:
        x = x * dsp.db(-3.0 - tp)
    wav = BUILD / "music" / "mus_mood_tension_bed.wav"
    export_wav(wav, x, peak_db=None, gain_db=0, loop=True)
    ogg = OUT / "music" / "mood" / "mus_mood_tension_bed.ogg"
    ogg.parent.mkdir(parents=True, exist_ok=True)
    encode_ogg(wav, ogg, 4.0)
    rep = {"file": str(ogg.relative_to(OUT.parent)), "loop_seconds": LOOP_S, "loop_samples": len(x), "sr": SR,
           "lufs": round(lufs(x), 2), "true_peak_dbtp": round(true_peak_db(x), 2)}
    Path(__file__).with_name("build_mood_bed.json").write_text(json.dumps(rep, indent=2))
    print(rep)


if __name__ == "__main__":
    main()
