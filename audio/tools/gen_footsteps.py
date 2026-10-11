#!/usr/bin/env python3
"""Footsteps: six surfaces, several single-step variations each, mono 48 kHz 16-bit, -1 dBTP.

concrete / wood / carpet / grass: Kenney Impact Sounds (CC0), 5 each.
metal_grate / neon_tile: single steps sliced from CC0 freesound recordings by onset detection.
"""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np

import dsp
from dsp import SR, ns
from master import OUT, RAW, export_wav, read_audio

K = RAW / "kenney" / "impact-sounds" / "Audio"
F = RAW / "freesound"
DST = OUT / "sfx"


def tidy(x: np.ndarray, pre: float = 0.004, max_len: float = 0.45, tail_db: float = -45) -> np.ndarray:
    a = np.abs(x)
    m = a.max() or 1
    on = np.where(a > m * dsp.db(-30))[0]
    s = max(0, on[0] - ns(pre)) if len(on) else 0
    x = x[s: s + ns(max_len)]
    a = np.abs(x)
    idx = np.where(a > m * dsp.db(tail_db))[0]
    end = min(len(x), (idx[-1] if len(idx) else len(x) - 1) + ns(0.01))
    return dsp.fade(x[:end], 0.001, min(0.04, end / SR / 3))


def slice_steps(x: np.ndarray, min_gap: float = 0.22, thresh_db: float = -18, win: float = 0.35) -> list[np.ndarray]:
    """Split a walking recording into single steps (envelope peak picking)."""
    env = np.sqrt(np.convolve(x ** 2, np.ones(ns(0.005)) / ns(0.005), mode="same"))
    env_db = 20 * np.log10(env + 1e-9)
    thr = env_db.max() + thresh_db
    steps, i, gap = [], 0, ns(min_gap)
    rising = np.where((env_db[1:] > thr) & (env_db[:-1] <= thr))[0]
    last = -gap
    for r in rising:
        if r - last < gap:
            continue
        s = max(0, r - ns(0.01))
        nxt = [q for q in rising if q > r + gap]
        e = min(len(x), s + ns(win), (nxt[0] - ns(0.015)) if nxt else len(x))
        seg = x[s:e]
        if len(seg) > ns(0.08):
            steps.append(seg)
        last = r
    return steps


def pick(steps: list[np.ndarray], n: int) -> list[np.ndarray]:
    """Keep the n steps with the most typical peak level (drop scuffs and outliers), spread through the take."""
    if len(steps) <= n:
        return steps
    pk = np.array([20 * np.log10(np.max(np.abs(s)) + 1e-9) for s in steps])
    med = np.median(pk)
    order = np.argsort(np.abs(pk - med))[: max(n, int(len(steps) * 0.7))]
    order = sorted(order)
    idx = np.linspace(0, len(order) - 1, n).round().astype(int)
    return [steps[order[i]] for i in idx]


def main() -> dict:
    out: dict = {}

    def emit(surface: str, clips: list[np.ndarray], sources: list[str], eq=None):
        files = []
        for i, c in enumerate(clips):
            c = tidy(c)
            if eq:
                c = eq(c)
            info = export_wav(DST / f"foot_{surface}_{i + 1:02d}.wav", c, peak_db=-1.0, channels=1)
            files.append(info)
        out[f"foot_{surface}"] = {"files": files, "sources": sources}
        print(f"foot_{surface:12} {len(files)} variations")

    for surface in ("concrete", "wood", "carpet", "grass"):
        paths = [K / f"footstep_{surface}_{i:03d}.ogg" for i in range(5)]
        emit(surface, [read_audio(p, mono=True) for p in paths], [str(p.relative_to(RAW.parent)) for p in paths])

    # metal grate: IENBA metal stairs + Oilcan03 grate run
    metal = []
    for sid in (698697, 834029, 786977):
        x = read_audio(F / f"{sid}.ogg", mono=True)
        st = slice_steps(x, min_gap=0.2 if sid == 786977 else 0.25)
        metal += pick(st, 3 if sid != 786977 else 2)
    emit("metal_grate", metal[:8], [f"raw/freesound/{s}.ogg" for s in (698697, 834029, 786977)],
         eq=lambda c: dsp.eq(c, "peak", 3500, 2.0, q=1.0))

    # neon tile: hard glossy tile (IENBA tile + SpliceSound sneakers on tile), a touch brighter
    tile = []
    for sid, k in ((834027, 4), (170506, 4)):
        x = read_audio(F / f"{sid}.ogg", mono=True)
        tile += pick(slice_steps(x, min_gap=0.3), k)
    emit("neon_tile", tile, [f"raw/freesound/{s}.ogg" for s in (834027, 170506)],
         eq=lambda c: dsp.eq(dsp.eq(c, "highshelf", 5000, 2.5), "hp", 90))
    Path(__file__).with_name("build_footsteps.json").write_text(json.dumps(out, indent=2))
    return out


if __name__ == "__main__":
    main()
