#!/usr/bin/env python3
"""Venue ambience loops: stereo, 48 kHz, 16-bit WAV, seamless (equal-power crossfade loop).

Layers CC0 field recordings (freesound previews) with a little synthesis. Each layer's steadiest
stretch is picked automatically (lowest short-term loudness spread) so one-off bangs are avoided.
Level: -24 LUFS integrated (+/-0.5), true peak <= -3 dBTP, so ambience sits under -17 LUFS music.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

import numpy as np

import dsp
import instruments as ins
from dsp import SR, ns, filt, noise
from master import BUILD, OUT, RAW, export_wav, lufs, read_audio, true_peak_db

F = RAW / "freesound"
DST = OUT / "ambience"
TARGET = -24.0
TP_MAX = -3.0
XF = 3.0
USED: dict[str, list[str]] = {}


def steadiest(x: np.ndarray, length: int, avoid: list[tuple[int, int]] | None = None) -> int:
    """Start index of the window whose 1 s loudness varies least (skips windows in `avoid`)."""
    m = x.mean(axis=1) if x.ndim == 2 else x
    hop = SR // 2
    rms = np.array([np.sqrt(np.mean(m[i:i + SR] ** 2)) + 1e-9 for i in range(0, max(1, len(m) - SR), hop)])
    db = 20 * np.log10(rms)
    w = max(1, length // hop)
    best, best_s = None, 0
    for k in range(0, max(1, len(db) - w)):
        s = k * hop
        if avoid and any(not (s + length <= a or s >= b) for a, b in avoid):
            continue
        seg = db[k:k + w]
        score = (seg.max() - np.median(seg)) + 0.3 * seg.std()
        if best is None or score < best:
            best, best_s = score, s
    return best_s


def layer_src(amb: str, sid: int, length: int, gain_db: float, pan: float = 0.0, lp: float | None = None,
              hp: float | None = 60, ref: float = -24.0) -> np.ndarray:
    """Load a freesound source, pick its steadiest stretch, make it stereo, set it to ref+gain LUFS."""
    path = F / f"{sid}.ogg"
    USED.setdefault(amb, []).append(f"raw/freesound/{sid}.ogg")
    x = read_audio(path, mono=False)
    mono_src = np.allclose(x[:, 0], x[:, 1])
    need = length
    if len(x) < need:  # short source: loop it on itself first
        base = len(x) - ns(1.0)
        piece = dsp.crossfade_loop(x, base, ns(1.0))
        x = np.tile(piece, (int(np.ceil(need / base)) + 1, 1))
    if mono_src:
        # decorrelated stereo from two different stretches of the same mono take
        m = x[:, 0]
        s1 = steadiest(m, need)
        cand = [s for s in range(0, max(1, len(m) - need), SR // 2) if abs(s - s1) > need // 3]
        s2 = cand[len(cand) // 2] if cand else s1
        L, R = m[s1:s1 + need], m[s2:s2 + need]
        if len(R) < need:
            R = np.pad(R, (0, need - len(R)))
        y = np.stack([L * 0.8 + R * 0.2, R * 0.8 + L * 0.2], axis=1)
    else:
        s = steadiest(x, need)
        y = x[s:s + need]
    if pan:
        g = np.array([np.cos((pan + 1) * np.pi / 4), np.sin((pan + 1) * np.pi / 4)]) * np.sqrt(2)
        y = y * g
    if hp:
        y = filt(y, "hp", hp)
    if lp:
        y = filt(y, "lp", lp, order=4)
    return y * dsp.db(ref + gain_db - lufs(y))


def finish(name: str, mix: np.ndarray, length: int, target: float = TARGET) -> dict:
    loop = dsp.crossfade_loop(mix, length, ns(XF)) if len(mix) >= length + ns(XF) else mix[:length]
    loop = loop * dsp.db(target - lufs(loop))
    tp = true_peak_db(loop)
    if tp > TP_MAX:  # rare: tame isolated peaks with a short circular gain dip
        from gen_music import limiter_gain
        loop = loop * limiter_gain(loop, TP_MAX - 0.7)[:, None]
        loop = loop * dsp.db(target - lufs(loop))
    info = export_wav(DST / f"{name}.wav", loop, peak_db=None, gain_db=0, channels=2, loop=True)
    info["sources"] = sorted(set(USED.get(name, [])))
    print(f"{name:22} {length / SR:5.1f}s  layers: {', '.join(info['sources']) or 'synth only'}")
    return info


def hum(length: int, f0: float = 60.0, level: float = 0.2) -> np.ndarray:
    t = np.arange(length) / SR
    sec = length / SR
    x = np.zeros(length)
    for k, a in [(1, 1.0), (2, 0.5), (3, 0.35), (4, 0.2), (6, 0.1)]:
        f = round(f0 * k * sec) / sec
        x += a * np.sin(2 * np.pi * f * t + k)
    am = 1 + 0.1 * np.sin(2 * np.pi * t * round(0.3 * sec) / sec)
    return dsp.widen(x * am * level, 0.3)


def room_tone(length: int, lp: float = 400.0) -> np.ndarray:
    n = length + ns(XF)
    a = filt(noise(n=n, color="pink"), "lp", lp)
    b = filt(noise(n=n, color="pink"), "lp", lp)
    return np.stack([a, b], axis=1)


def recipes() -> dict:
    L = ns(30.0)
    LX = L + ns(XF)
    out = {}

    out["amb_timeline_square"] = finish("amb_timeline_square", (
        layer_src("amb_timeline_square", 640625, LX, 0.0)
        + layer_src("amb_timeline_square", 757838, LX, -4.0)), L)

    out["amb_terminal_cafe"] = finish("amb_terminal_cafe", (
        layer_src("amb_terminal_cafe", 437461, LX, -1.0)
        + layer_src("amb_terminal_cafe", 450282, LX, -3.0, pan=-0.5, hp=150)
        + layer_src("amb_terminal_cafe", 546167, LX, -4.0, pan=0.55, hp=150)
        + layer_src("amb_terminal_cafe", 626952, LX, -7.0, pan=0.1, hp=150)), L)

    out["amb_airdrop_farm"] = finish("amb_airdrop_farm", (
        layer_src("amb_airdrop_farm", 488050, LX, 0.0)
        + layer_src("amb_airdrop_farm", 488064, LX, -3.0)), L)

    out["amb_dubai_marina"] = finish("amb_dubai_marina", (
        layer_src("amb_dubai_marina", 457215, LX, 0.0, hp=40)
        + layer_src("amb_dubai_marina", 328808, LX, -7.0)
        + layer_src("amb_dubai_marina", 360633, LX, -9.0)), L)

    refinery = (layer_src("amb_ore_refinery", 478118, LX, -2.0, hp=30)
                + layer_src("amb_ore_refinery", 415156, LX, -3.0, hp=30)
                + layer_src("amb_ore_refinery", 450126, LX, -7.0, hp=30))
    out["amb_ore_refinery"] = finish("amb_ore_refinery", refinery, L)

    # Perp Pit trading floor: louder crowd + keyboards (recordings only since 2026-10-09; the synthesized server hum is gone)
    pit = (layer_src("amb_perp_pit", 757838, LX, 0.0)
           + layer_src("amb_perp_pit", 640630, LX, -2.0)
           + layer_src("amb_perp_pit", 546167, LX, -6.0, pan=-0.3, hp=150))
    out["amb_perp_pit"] = finish("amb_perp_pit", pit, L)

    # Rekt Clinic: muffled ward chatter over a ventilation drone (a refinery machine recording low-passed to an HVAC hum).
    # Recordings only (2026-10-09): the synthesized room tone and heart-monitor beeps are gone; no CC0 recorded monitor
    # beep was found in the packs checked.
    clinic = (layer_src("amb_rekt_clinic", 437461, LX, -9.0, lp=900)
              + layer_src("amb_rekt_clinic", 415156, LX, -10.0, lp=300, hp=40))
    out["amb_rekt_clinic"] = finish("amb_rekt_clinic", clinic, L)

    # Spaces Studio: near-silent treated room (voice-only venue): the café recording low-passed to a faint room murmur.
    studio = layer_src("amb_spaces_studio", 437461, LX, -12.0, lp=300, hp=60)
    out["amb_spaces_studio"] = finish("amb_spaces_studio", studio, L, target=-36.0)

    # amb_club_doors: the licensed club track through a door — tools/curate_real_audio.py (no synthesized music).
    return out


def main(only: list[str]) -> None:
    p = Path(__file__).with_name("build_ambience.json")
    rep = json.loads(p.read_text()) if p.exists() else {}
    keep = {k: v for k, v in rep.items() if k == "amb_club_doors"}  # built by curate_real_audio.py
    rep = recipes() | keep
    p.write_text(json.dumps(rep, indent=2))


if __name__ == "__main__":
    main(sys.argv[1:])
