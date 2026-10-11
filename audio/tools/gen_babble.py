#!/usr/bin/env python3
"""NPC voice babble (Animal-Crossing style): one 26-syllable bank per NPC archetype.

Every bank maps the letters a..z to a short synthetic syllable (formant synthesis: Rosenberg
glottal source -> 3 vowel formants, plus plosive bursts, nasals, liquids and fricatives). Each
archetype gets its own pitch, vocal-tract size, source and colour. Own work, CC0.

Output: processed/babble/babble_<archetype>_<letter>.wav (48 kHz, 16-bit mono, every syllable at the
same RMS, true peak <= -1 dBTP) and processed/babble/babble-banks.json (playback parameters).
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

import numpy as np
from scipy import signal

import dsp
from dsp import SR, ns, filt, noise
from master import OUT, export_wav, true_peak_db

DST = OUT / "babble"

VOWELS = {  # adult formants (Hz) and bandwidths
    "a": ((730, 1090, 2440), (90, 110, 160)),
    "e": ((530, 1840, 2480), (70, 100, 150)),
    "i": ((300, 2250, 3000), (60, 90, 150)),
    "o": ((570, 840, 2410), (80, 90, 150)),
    "u": ((320, 870, 2240), (60, 90, 140)),
}
# letter -> (consonant, vowel). Vowel follows the English letter name so lines sound varied.
LETTERS = {
    "a": ("", "a"), "b": ("b", "i"), "c": ("s", "i"), "d": ("d", "i"), "e": ("", "e"), "f": ("f", "e"),
    "g": ("g", "i"), "h": ("h", "a"), "i": ("", "i"), "j": ("j", "a"), "k": ("k", "a"), "l": ("l", "e"),
    "m": ("m", "e"), "n": ("n", "e"), "o": ("", "o"), "p": ("p", "i"), "q": ("k", "u"), "r": ("r", "a"),
    "s": ("s", "e"), "t": ("t", "i"), "u": ("", "u"), "v": ("v", "i"), "w": ("w", "a"), "x": ("z", "e"),
    "y": ("y", "a"), "z": ("z", "i"),
}
LOCUS_F2 = {"b": 800, "p": 800, "m": 900, "f": 1000, "v": 1000, "w": 700, "d": 1800, "t": 1800, "n": 1700,
            "l": 1300, "s": 1800, "z": 1800, "r": 1250, "g": 2300, "k": 2300, "y": 2200, "j": 2100, "h": None}


# ------------------------------------------------------------------ voice profiles
# f0 Hz, tract (formant scale), src: glottal|square|whisper|beep, breath 0..1, vib (semitones),
# vib_rate Hz, contour (semitones over the syllable), speed (duration scale, <1 = faster),
# fx: list of post-processing names. cps / jitter / pitch_range are runtime hints for Unity.
P = {
    "town_crier":          dict(f0=165, tract=1.05, src="glottal", breath=0.05, contour=-1.0, speed=0.85, fx=["megaphone"], cps=18, jitter=1.0, vol=-4),
    "giveaway_guy":        dict(f0=185, tract=1.10, src="glottal", breath=0.05, contour=1.0, speed=0.85, fx=["bright"], cps=18, jitter=3.0, vol=-6),
    "reply_guy":           dict(f0=215, tract=1.22, src="glottal", breath=0.05, contour=0.0, speed=0.8, fx=["nasal"], cps=20, jitter=1.5, vol=-7),
    "perp_degen_npc":      dict(f0=175, tract=1.02, src="glottal", breath=0.08, contour=1.5, speed=0.75, fx=["drive"], cps=22, jitter=2.5, vol=-6),
    "rekt_guy":            dict(f0=140, tract=1.00, src="glottal", breath=0.35, vib=0.8, vib_rate=7.0, contour=-2.5, speed=1.25, fx=[], cps=12, jitter=1.0, vol=-7),
    "wallet_tracker":      dict(f0=235, tract=1.25, src="glottal", breath=0.03, contour=0.5, speed=0.85, fx=["bright"], cps=18, jitter=1.0, vol=-7),
    "doomposter":          dict(f0=95, tract=0.90, src="glottal", breath=0.15, contour=-2.0, speed=1.3, fx=["dark"], cps=11, jitter=0.8, vol=-6),
    "spaces_host":         dict(f0=120, tract=0.96, src="glottal", breath=0.08, contour=-0.5, speed=1.0, fx=["radio"], cps=15, jitter=1.2, vol=-6),
    "ore_miner":           dict(f0=105, tract=0.92, src="glottal", breath=0.12, contour=-1.0, speed=1.05, fx=["gravel"], cps=14, jitter=1.0, vol=-6),
    "alpha_caller":        dict(f0=165, tract=1.05, src="glottal", breath=0.05, contour=1.0, speed=0.8, fx=["bright"], cps=20, jitter=2.0, vol=-6),
    "cabal_insider":       dict(f0=150, tract=1.00, src="whisper", breath=1.0, contour=0.0, speed=0.95, fx=[], cps=16, jitter=1.0, vol=-9),
    "rug_dev":             dict(f0=150, tract=1.00, src="glottal", breath=0.10, contour=-0.5, speed=0.85, fx=["phase"], cps=19, jitter=2.0, vol=-6),
    "sniper_bot":          dict(f0=1200, tract=1.0, src="beep", breath=0.0, contour=0.0, speed=0.6, fx=[], cps=14, jitter=4.0, vol=-12),
    "laser_eyes_maxi":     dict(f0=135, tract=0.95, src="glottal", breath=0.05, contour=-0.5, speed=0.95, fx=["drive", "phase"], cps=17, jitter=1.5, vol=-6),
    "chain_tribalist":     dict(f0=175, tract=1.05, src="glottal", breath=0.06, contour=1.0, speed=0.8, fx=["bright"], cps=21, jitter=3.0, vol=-6),
    "sleuth":              dict(f0=110, tract=0.95, src="glottal", breath=0.12, contour=-1.0, speed=1.15, fx=["dark"], cps=13, jitter=1.0, vol=-6),
    "auditor":             dict(f0=145, tract=1.00, src="glottal", breath=0.03, contour=0.0, speed=0.95, fx=[], cps=16, jitter=0.3, vol=-7),
    "vc":                  dict(f0=125, tract=0.97, src="glottal", breath=0.06, contour=-0.5, speed=1.0, fx=["radio"], cps=15, jitter=1.0, vol=-6),
    "farmer":              dict(f0=140, tract=1.00, src="glottal", breath=0.08, contour=0.5, speed=1.05, fx=["twang"], cps=14, jitter=1.5, vol=-6),
    "sybil_farmer":        dict(f0=310, tract=1.45, src="glottal", breath=0.05, contour=1.0, speed=0.7, fx=["bright"], cps=22, jitter=2.0, vol=-8),
    "fake_rich_larp":      dict(f0=155, tract=1.03, src="glottal", breath=0.05, contour=1.0, speed=0.85, fx=["bright", "drive"], cps=18, jitter=2.0, vol=-6),
    "dubai_relocator":     dict(f0=130, tract=0.98, src="glottal", breath=0.10, contour=-0.5, speed=1.1, fx=[], cps=14, jitter=1.2, vol=-6),
    "ai_agent":            dict(f0=150, tract=1.00, src="square", breath=0.0, contour=0.0, speed=0.9, fx=["robot"], cps=18, jitter=0.0, vol=-8),
    "drainer":             dict(f0=135, tract=1.00, src="glottal", breath=0.45, contour=-0.5, speed=1.0, fx=[], cps=15, jitter=1.0, vol=-7),
    "community_mod":       dict(f0=160, tract=1.05, src="glottal", breath=0.05, contour=-0.5, speed=0.9, fx=["phone"], cps=17, jitter=1.0, vol=-6),
    "thread_boy":          dict(f0=200, tract=1.20, src="glottal", breath=0.05, contour=0.5, speed=0.8, fx=[], cps=20, jitter=1.5, vol=-7),
    "engagement_farmer":   dict(f0=245, tract=1.25, src="glottal", breath=0.06, contour=1.5, speed=0.85, fx=["bright"], cps=19, jitter=3.0, vol=-7),
    "normie":              dict(f0=175, tract=1.10, src="glottal", breath=0.08, contour=2.0, speed=1.05, fx=[], cps=14, jitter=1.5, vol=-6),
    "doxxed_founder":      dict(f0=150, tract=1.02, src="glottal", breath=0.05, contour=0.0, speed=0.95, fx=[], cps=16, jitter=1.2, vol=-6),
    "prediction_shark_npc": dict(f0=115, tract=0.95, src="glottal", breath=0.08, contour=-0.5, speed=0.95, fx=["dark"], cps=16, jitter=1.0, vol=-6),
    "generic":             dict(f0=170, tract=1.10, src="glottal", breath=0.06, contour=0.0, speed=0.95, fx=[], cps=16, jitter=1.5, vol=-6),
}
PHASE1 = ["town_crier", "giveaway_guy", "reply_guy", "perp_degen_npc", "rekt_guy", "wallet_tracker", "doomposter",
          "spaces_host", "ore_miner"]


# ------------------------------------------------------------------ synthesis

def glottal(f0: np.ndarray, rng) -> np.ndarray:
    """Rosenberg glottal-flow derivative pulse train (open 40 %, closing 16 %)."""
    n = len(f0)
    jit = 1 + 0.004 * rng.standard_normal(n).cumsum() / np.sqrt(np.arange(1, n + 1))
    ph = np.cumsum(f0 * jit) / SR % 1.0
    g = np.where(ph < 0.4, 0.5 * (1 - np.cos(np.pi * ph / 0.4)),
                 np.where(ph < 0.56, np.cos(np.pi * (ph - 0.4) / 0.32), 0.0))
    d = np.diff(g, prepend=g[0])
    return d / (np.max(np.abs(d)) + 1e-9)


def source(kind: str, f0: np.ndarray, breath: float, rng) -> np.ndarray:
    n = len(f0)
    nz = filt(rng.standard_normal(n), "hp", 300) * 0.25
    if kind == "whisper":
        return nz * 2.0
    if kind == "square":
        s = dsp.osc("square", f0, n=n) * 0.5
    else:
        s = glottal(f0, rng)
    return s * (1 - breath * 0.6) + nz * breath


def formant_filter(x: np.ndarray, F: np.ndarray, B: np.ndarray, gains=(1.0, 0.6, 0.3), block: int = 48) -> np.ndarray:
    """Parallel 2-pole resonators with per-block time-varying centre frequencies. F, B: (n, 3)."""
    y = np.zeros_like(x)
    for k in range(3):
        zi = np.zeros(2)
        out = np.zeros_like(x)
        for i in range(0, len(x), block):
            f, bw = F[i, k], B[i, k]
            r = np.exp(-np.pi * bw / SR)
            th = 2 * np.pi * min(f, SR * 0.45) / SR
            a = [1.0, -2 * r * np.cos(th), r * r]
            b = [(1 - r * r) * np.sin(th), 0.0, 0.0]
            out[i:i + block], zi = signal.lfilter(b, a, x[i:i + block], zi=zi)
        y += gains[k] * out
    return y


def formant_track(n: int, vowel: str, tract: float, onset_f: tuple | None, trans: int) -> tuple[np.ndarray, np.ndarray]:
    Fv, Bv = VOWELS[vowel]
    Fv = np.array(Fv) * tract
    Bv = np.array(Bv) * (0.9 + 0.1 * tract)
    F = np.tile(Fv, (n, 1)).astype(float)
    if onset_f is not None and trans > 0:
        F0 = np.array(onset_f, dtype=float)
        w = np.clip(np.arange(n) / trans, 0, 1)[:, None]
        w = 0.5 - 0.5 * np.cos(np.pi * w)
        F = F0 * (1 - w) + F * w
    return F, np.tile(Bv, (n, 1))


def syllable(letter: str, prof: dict, rng) -> np.ndarray:
    cons, vowel = LETTERS[letter]
    tract, f0b, speed = prof["tract"], prof["f0"], prof["speed"]
    if prof["src"] == "beep":
        return beep(letter, prof, rng)
    vdur = 0.085 * speed * rng.uniform(0.92, 1.08)
    parts = []
    # --- consonant onset
    if cons in ("p", "t", "k", "b", "d", "g", "j"):
        burst_f = {"p": 900, "b": 900, "t": 4200, "d": 3800, "k": 2200, "g": 2000, "j": 3000}[cons] * tract
        clos = np.zeros(ns(0.012 * speed))
        if cons in ("b", "d", "g"):
            vb = filt(source("glottal", np.full(len(clos), f0b), 0.0, rng), "lp", 400) * 0.25
            clos = clos + vb
        burst = filt(rng.standard_normal(ns(0.008)), "bp", burst_f, q=1.2) * np.linspace(1, 0.2, ns(0.008)) * 0.9
        asp = np.zeros(0)
        if cons in ("p", "t", "k"):
            asp = filt(rng.standard_normal(ns(0.025 * speed)), "bp", 1500 * tract, q=0.6) * np.linspace(0.5, 0.05, ns(0.025 * speed))
        if cons == "j":
            asp = filt(rng.standard_normal(ns(0.03 * speed)), "bp", 2800 * tract, q=1.5) * np.linspace(0.6, 0.1, ns(0.03 * speed))
        parts += [(clos, 0.15 if cons in ('b', 'd', 'g') else 0.0), (burst, 0.55), (asp, 0.28)]
    elif cons in ("s", "z", "f", "v", "h"):
        d = ns((0.055 if cons in ("s", "z") else 0.045) * speed)
        nz = rng.standard_normal(d)
        if cons in ("s", "z"):
            fr = filt(filt(nz, "hp", 4200 * min(tract, 1.3)), "lp", 9500) * 0.7
        elif cons in ("f", "v"):
            fr = filt(nz, "bp", 3500, q=0.4) * 0.3
        else:  # h: aspiration shaped by the following vowel
            Fh, Bh = formant_track(d, vowel, tract, None, 0)
            fr = formant_filter(nz * 0.5, Fh, Bh * 2.0) * 0.6
        env = np.sin(np.linspace(0, np.pi, d)) ** 0.7
        fr = fr * env
        if cons in ("z", "v"):
            fr = fr + filt(source("glottal", np.full(d, f0b), 0.0, rng), "lp", 600) * 0.2 * env
        parts.append((fr, {'s': 0.38, 'z': 0.32, 'f': 0.22, 'v': 0.22, 'h': 0.3}[cons]))
    elif cons in ("m", "n"):
        d = ns(0.045 * speed)
        src = source(prof["src"], np.full(d, f0b), prof["breath"] * 0.5, rng)
        mur = filt(src, "lp", 350 * tract) * 1.6
        mur *= np.minimum(1, np.linspace(0, 4, d))
        parts.append((mur, 0.6))
    # --- voiced part (glides l, r, w, y and the vowel share one continuous source)
    glide = cons in ("l", "r", "w", "y", "m", "n", "b", "d", "g", "j")
    gdur = ns(0.04 * speed) if cons in ("l", "r", "w", "y") else 0
    n = gdur + ns(vdur)
    t = np.arange(n) / SR
    cont = prof["contour"] * (t / t[-1])
    vib = prof.get("vib", 0.0) * np.sin(2 * np.pi * prof.get("vib_rate", 5.5) * t)
    f0 = f0b * 2 ** ((cont + vib + rng.normal(0, 0.15)) / 12)
    src = source(prof["src"], f0, prof["breath"], rng)
    onset = None
    trans = 0
    if cons == "l":
        onset = (360 * tract, 1300 * tract, 2700 * tract)
    elif cons == "r":
        onset = (420 * tract, 1250 * tract, 1600 * tract)
    elif cons == "w":
        onset = (300 * tract, 700 * tract, 2200 * tract)
    elif cons == "y":
        onset = (280 * tract, 2250 * tract, 3000 * tract)
    elif cons in LOCUS_F2 and LOCUS_F2[cons]:
        Fv = np.array(VOWELS[vowel][0]) * tract
        onset = (Fv[0] * 0.6, LOCUS_F2[cons] * tract * 0.5 + Fv[1] * 0.5, Fv[2])
    if onset is not None:
        trans = gdur + ns(0.03 * speed)
    F, B = formant_track(n, vowel, tract, onset, trans)
    v = formant_filter(src, F, B)
    env = np.ones(n)
    a = ns(0.012 if not glide else 0.006)
    env[:a] = np.linspace(0, 1, a)
    r = ns(0.035 * speed)
    env[-r:] *= np.linspace(1, 0, r) ** 1.5
    v *= env
    vr = np.sqrt(np.mean(v ** 2)) + 1e-9
    # consonants sit at a fixed RMS ratio below the vowel so syllables stay soft and even
    segs = [p * (vr * rel / (np.sqrt(np.mean(p ** 2)) + 1e-9)) if rel > 0 else p * 0 for p, rel in parts if len(p)]
    x = np.concatenate(segs + [v]) if segs else v
    return post(x, prof)


def beep(letter: str, prof: dict, rng) -> np.ndarray:
    """Sniper bot: pitched blips; letter picks the pitch (pentatonic over two octaves)."""
    idx = ord(letter) - 97
    scale = [0, 2, 4, 7, 9]
    semis = scale[idx % 5] + 12 * ((idx // 5) % 3)
    f = prof["f0"] * 2 ** (semis / 12)
    n = ns(rng.uniform(0.045, 0.07))
    t = np.arange(n) / SR
    x = dsp.osc("square", f * (1 + 0.02 * (idx % 3 - 1) * t / t[-1]), n=n) * 0.4 + np.sin(2 * np.pi * f * 2 * t) * 0.3
    x = filt(x, "lp", 6000)
    e = np.ones(n)
    e[: ns(0.003)] = np.linspace(0, 1, ns(0.003))
    e[-ns(0.01):] = np.linspace(1, 0, ns(0.01))
    return x * e


def post(x: np.ndarray, prof: dict) -> np.ndarray:
    for fx in prof["fx"]:
        if fx == "megaphone":
            x = filt(filt(x, "hp", 500), "lp", 3800)
            x = dsp.eq(x, "peak", 1800, 6, q=1.5)
            x = dsp.sat(x / (np.max(np.abs(x)) + 1e-9) * 2.5, 2.5)
        elif fx == "bright":
            x = dsp.eq(x, "highshelf", 3000, 4)
        elif fx == "nasal":
            x = dsp.eq(dsp.eq(x, "peak", 1100, 8, q=3), "peak", 500, -6, q=2)
        elif fx == "drive":
            x = dsp.sat(x / (np.max(np.abs(x)) + 1e-9) * 1.8, 1.8)
        elif fx == "dark":
            x = filt(x, "lp", 2200)
        elif fx == "radio":
            x = dsp.eq(dsp.eq(x, "peak", 3000, 3, q=1), "lowshelf", 200, 3)
        elif fx == "gravel":
            n = len(x)
            am = 1 + 0.35 * np.sign(np.sin(2 * np.pi * 38 * np.arange(n) / SR)) * np.random.default_rng(n).uniform(0.5, 1, n)
            x = x * am
        elif fx == "phase":
            d = ns(0.0015)
            x = x + 0.6 * np.r_[np.zeros(d), x[:-d]]
        elif fx == "twang":
            x = dsp.eq(x, "peak", 1600, 7, q=2.5)
        elif fx == "robot":
            n = len(x)
            x = x * np.sin(2 * np.pi * 55 * np.arange(n) / SR)
            x = dsp.bitcrush(x / (np.max(np.abs(x)) + 1e-9), bits=6, down=3)
            x = filt(x, "lp", 7000)
        elif fx == "phone":
            x = filt(filt(x, "hp", 350), "lp", 3400)
    return x


def build(name: str, prof: dict) -> dict:
    rng = np.random.default_rng(sum(map(ord, name)) * 7919)
    files = {}
    clips = {}
    for letter in LETTERS:
        x = syllable(letter, prof, rng)
        x = signal.sosfiltfilt(signal.butter(2, 70, btype="high", fs=SR, output="sos"), x)
        x = dsp.fade(x, 0.002, 0.01)
        clips[letter] = x
    # every syllable to the same RMS, then one shared gain so the loudest true peak is -1 dBTP
    target_rms = 0.12
    for k, x in clips.items():
        clips[k] = x * target_rms / (np.sqrt(np.mean(x ** 2)) + 1e-9)
    worst = max(true_peak_db(x) for x in clips.values())
    g = -1.5 - worst
    for k, x in clips.items():
        info = export_wav(DST / f"babble_{name}_{k}.wav", x, peak_db=None, gain_db=g, channels=1)
        files[k] = info["path"]
    lens = [len(x) / SR for x in clips.values()]
    return {
        "files": files,
        "phase": 1 if name in PHASE1 else ("fallback" if name == "generic" else "later (proposed id)"),
        "base_f0_hz": prof["f0"], "chars_per_second": prof["cps"], "pitch_jitter_semitones": prof["jitter"],
        "npc_pitch_range_semitones": [-2.0, 2.0], "volume_db": prof["vol"],
        "syllable_ms": [round(min(lens) * 1000), round(max(lens) * 1000)],
        "voice": {k: v for k, v in prof.items() if k not in ("cps", "jitter", "vol")},
    }


def main(only: list[str]) -> None:
    banks = {}
    for name, prof in P.items():
        if only and name not in only:
            continue
        banks[name] = build(name, prof)
        print(f"babble_{name:22} 26 syllables {banks[name]['syllable_ms']} ms")
    meta = {
        "format": "48 kHz, 16-bit PCM mono WAV; one file per letter a-z",
        "how_to_play": "See audio/BABBLE.md (rules) and audio/unity/BabblePlayer.cs (reference implementation)",
        "banks": banks,
    }
    p = DST / "babble-banks.json"
    if only and p.exists():
        old = json.loads(p.read_text())
        old["banks"].update(banks)
        meta = old
    p.write_text(json.dumps(meta, indent=2))


if __name__ == "__main__":
    main(sys.argv[1:])
