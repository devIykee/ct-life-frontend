#!/usr/bin/env python3
"""Render an NPC line as babble, using exactly the rules in audio/BABBLE.md (same as BabblePlayer.cs).

Usage: babble_preview.py <archetype> "<line>" [npc_id] [out.wav]
Writes to audio/build/babble_preview/ by default (gitignored scratch).
"""
from __future__ import annotations

import json
import sys
import zlib
from pathlib import Path

import numpy as np

import dsp
from master import BUILD, OUT, export_wav, read_audio

BANKS = json.loads((OUT / "babble" / "babble-banks.json").read_text())["banks"]
DIGITS = {"0": "o", "1": "w", "2": "t", "3": "r", "4": "f", "5": "f", "6": "s", "7": "s", "8": "e", "9": "n"}
SYMBOLS = {"$": "s", "@": "a", "#": "h", "%": "p", "&": "n", "+": "p"}
PAUSE = {" ": 1.0, ",": 2.0, ";": 2.0, ":": 2.0, ".": 3.0, "!": 3.0, "?": 3.0, "\n": 3.0, "-": 1.0, "/": 1.0}
MAX_SECONDS = 3.0


def npc_offset(npc_id: str, lo: float, hi: float) -> float:
    """Stable per-NPC pitch offset in semitones (CRC32 of the id -> [lo, hi])."""
    return lo + (zlib.crc32(npc_id.encode()) % 1000) / 999 * (hi - lo)


def plan(text: str, bank: dict, npc_id: str) -> list[tuple[float, str, float, float]]:
    """-> [(time_s, letter, semitones, gain)] following the BABBLE.md rules."""
    rng = np.random.default_rng(zlib.crc32((npc_id + text).encode()))
    step = 1.0 / bank["chars_per_second"]
    off = npc_offset(npc_id, *bank["npc_pitch_range_semitones"])
    jit = bank["pitch_jitter_semitones"]
    letters_idx = [i for i, c in enumerate(text) if c.isalnum() or c in SYMBOLS]
    end_q = text.rstrip().endswith("?")
    end_x = text.rstrip().endswith("!")
    events, t = [], 0.0
    for i, ch in enumerate(text):
        if t > MAX_SECONDS:
            break
        c = ch.lower()
        letter = c if "a" <= c <= "z" else DIGITS.get(c) or SYMBOLS.get(c)
        if letter:
            semis = off + rng.uniform(-jit, jit)
            gain = 1.0
            if ch.isupper():
                semis += 1.5
                gain = 1.12
            rank_from_end = len(letters_idx) - 1 - letters_idx.index(i) if i in letters_idx else 99
            if end_q and rank_from_end < 4:
                semis += (4 - rank_from_end) * 1.0          # question: rise over the last 4 syllables
            if end_x and rank_from_end < 3:
                semis += 1.0
                gain *= 1.12
            events.append((t, letter, semis, gain))
            t += step
        else:
            t += step * PAUSE.get(c, 0.5)
    return events


def render(arch: str, text: str, npc_id: str = "npc_0001") -> np.ndarray:
    bank = BANKS[arch]
    clips = {k: read_audio(OUT.parent / v, mono=True) for k, v in bank["files"].items()}
    ev = plan(text, bank, npc_id)
    total = (ev[-1][0] if ev else 0) + 0.5
    out = np.zeros(dsp.ns(total))
    for t, letter, semis, gain in ev:
        x = clips[letter]
        ratio = 2 ** (semis / 12)                      # Unity AudioSource.pitch: resample (speed + pitch)
        n = int(len(x) / ratio)
        y = np.interp(np.arange(n) * ratio, np.arange(len(x)), x)
        s = dsp.ns(t)
        e = min(len(out), s + n)
        out[s:e] += y[: e - s] * gain
    return out * dsp.db(bank["volume_db"])


if __name__ == "__main__":
    arch, text = sys.argv[1], sys.argv[2]
    npc = sys.argv[3] if len(sys.argv) > 3 else "npc_0001"
    dst = Path(sys.argv[4]) if len(sys.argv) > 4 else BUILD / "babble_preview" / f"{arch}.wav"
    y = render(arch, text, npc)
    export_wav(dst, y, peak_db=None, gain_db=0, channels=1)
    print(dst, f"{len(y) / dsp.SR:.2f}s")
