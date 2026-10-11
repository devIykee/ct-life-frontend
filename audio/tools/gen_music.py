#!/usr/bin/env python3
"""Adaptive district music for CT Life, composed and rendered in code (own work, CC0).

Each district: 16-bar loop, stems pad / bass / drums / lead / tension, all the same exact
sample length so they layer in sync, rendered circularly (reverb and delay tails wrap into
the loop start) so every stem loops seamlessly. A `full` preview (pad+bass+drums+lead) is
also written. Loudness: the full mix is set to -17 LUFS integrated (target window -18..-16),
true peak <= -1 dBTP; every stem of a district gets the same gain so they sum to that mix.

Usage: gen_music.py [district ...]
"""
from __future__ import annotations

import json
import sys
import zlib
from pathlib import Path

import numpy as np

import dsp
import instruments as ins
from dsp import SR, ns, mtof, filt, noise, osc, sat
from master import BUILD, OUT, encode_ogg, export_wav, lufs, true_peak_db

STEMS = ["pad", "bass", "drums", "lead", "tension"]
MIX_STEMS = ["pad", "bass", "drums", "lead"]
TARGET_LUFS = -17.0
TP_CEIL = -2.0        # pre-encode ceiling; Vorbis decoding can add a few tenths of a dB
OGG_Q = 4.0           # libvorbis quality 4 (~128 kbps stereo)

CHORDS = {
    "maj": [0, 4, 7], "m": [0, 3, 7], "7": [0, 4, 7, 10], "m7": [0, 3, 7, 10], "maj7": [0, 4, 7, 11],
    "m9": [0, 3, 7, 10, 14], "maj9": [0, 4, 7, 11, 14], "9": [0, 4, 7, 10, 14], "9sus": [0, 5, 7, 10, 14],
    "13": [0, 4, 10, 14, 21], "sus4": [0, 5, 7], "m11": [0, 3, 7, 10, 14, 17], "7sus": [0, 5, 7, 10],
}


def voicing(root: int, kind: str, lo: int = 52, hi: int = 76) -> list[int]:
    """Close-ish voicing: root kept low, upper tones folded into [lo, hi]."""
    notes = []
    for iv in CHORDS[kind]:
        n = root + iv
        while n < lo:
            n += 12
        while n > hi:
            n -= 12
        notes.append(n)
    return sorted(set(notes))


class Track:
    def __init__(self, name: str, bpm: float, bars: int = 16, swing: float = 0.5):
        self.name, self.bpm, self.bars, self.swing = name, bpm, bars, swing
        self.spb = 60.0 / bpm
        self.L = int(round(bars * 4 * self.spb * SR))
        self.buf = {s: np.zeros((self.L, 2)) for s in STEMS}
        self.rng = np.random.default_rng(zlib.crc32(name.encode()))
        self.kicks: list[float] = []

    def beats(self, b: float) -> float:
        """Seconds for a duration in beats."""
        return b * self.spb

    def pos(self, beat: float) -> int:
        b16 = beat * 4
        i = int(np.floor(b16 + 1e-9))
        frac = b16 - i
        if i % 2 == 1 and self.swing != 0.5:
            b16 = (i - 1) + 2 * self.swing + frac * (2 - 2 * self.swing)
        return int(round(b16 / 4 * self.spb * SR))

    def add(self, stem: str, x: np.ndarray, beat: float, gain: float = 1.0, pan: float = 0.0, human: float = 0.004):
        if x.ndim == 1:
            x = dsp.pan(x, pan)
        jitter = int(self.rng.normal(0, human) * SR) if human else 0
        dsp.mix_into(self.buf[stem], x, (self.pos(beat) + jitter) % self.L, gain, wrap=True)

    # ---- circular effects (input is a loop; tails wrap to the start)
    def fx_reverb(self, stem: str, wet: float, decay: float, **kw):
        y = dsp.reverb(self.buf[stem], wet=wet, decay=decay, **kw)
        self.buf[stem] = dsp.loop_wrap(y, self.L)

    def fx_delay(self, stem: str, beats: float, fb: float = 0.4, wet: float = 0.3, lp: float = 4500):
        y = dsp.delay(self.buf[stem], self.beats(beats), fb=fb, wet=wet, lp=lp, taps=6)
        self.buf[stem] = dsp.loop_wrap(y, self.L)

    def fx_filter(self, stem: str, kind: str, f: float, order: int = 2):
        x = self.buf[stem]
        y = filt(np.concatenate([x, x], axis=0), kind, f, order=order)
        self.buf[stem] = y[self.L:]

    def fx_eq(self, stem: str, kind: str, f: float, g: float, q: float = 0.707):
        x = self.buf[stem]
        y = dsp.eq(np.concatenate([x, x], axis=0), kind, f, g, q)
        self.buf[stem] = y[self.L:]

    def fx_sat(self, stem: str, drive: float):
        x = self.buf[stem]
        m = np.max(np.abs(x)) or 1
        self.buf[stem] = sat(x / m * drive, drive) * m / drive * 1.0

    def sidechain(self, stem: str, depth: float = 0.6, release: float = 0.18):
        """Duck `stem` on every kick (circular)."""
        n = self.L
        env = np.ones(n)
        r, a = ns(release), ns(0.004)
        # 4 ms look-ahead ramp down (no click), then exponential recovery
        shape = np.concatenate([1 - depth * np.sin(np.linspace(0, np.pi / 2, a)) ** 2,
                                1 - depth * np.exp(-np.arange(r) / (r / 4))])
        shape[a:a + ns(0.03)] = np.minimum(shape[a:a + ns(0.03)], 1 - depth)  # small hold
        for b in self.kicks:
            s = (self.pos(b) - a) % n
            idx = (np.arange(len(shape)) + s) % n
            env[idx] = np.minimum(env[idx], shape)
        # smooth the recovery edge where a hold meets the release
        env = np.convolve(np.concatenate([env[-48:], env, env[:48]]), np.ones(48) / 48, mode="same")[48:-48]
        self.buf[stem] *= env[:, None]

    def kick_at(self, x: np.ndarray, beat: float, gain: float = 1.0):
        self.kicks.append(beat)
        self.add("drums", x, beat, gain, human=0.0)


# =================================================================== shared parts

def tension(tr: Track, root: int, pulse: bool = True, bright: float = 1.0):
    """Red-market layer: semitone-rub drone, heartbeat, high tremolo cluster, wrapping noise swells."""
    L = tr.L
    t = np.arange(L) / SR
    loop_s = L / SR
    f1, f2 = mtof(root - 24), mtof(root - 23)
    # whole-number cycles per loop so the drone itself is seamless
    f1 = round(f1 * loop_s) / loop_s
    f2 = round(f2 * loop_s) / loop_s
    drone = osc("saw", f1, n=L) + 0.8 * osc("saw", f2, n=L)
    lfo = 0.6 + 0.4 * np.cos(2 * np.pi * t / (tr.beats(16)))
    drone = filt(np.concatenate([drone, drone]), "lp", 420)[L:] * lfo
    tr.buf["tension"] += dsp.widen(drone * 0.35, 0.5)
    hi1 = round(mtof(root + 24) * loop_s) / loop_s
    hi2 = round(mtof(root + 25) * loop_s) / loop_s
    trem = 0.5 + 0.5 * np.sin(2 * np.pi * t / tr.beats(0.25))
    cluster = (np.sin(2 * np.pi * hi1 * t) + np.sin(2 * np.pi * hi2 * t)) * trem * 0.05 * bright
    tr.buf["tension"] += dsp.widen(cluster, 0.8)
    if pulse:
        hb = ins.kick(0.5, f0=48, punch=0.4, click=0.0, decay=0.35)
        for bar in range(tr.bars):
            tr.add("tension", hb, bar * 4, 0.45, human=0)
            tr.add("tension", hb, bar * 4 + 0.6, 0.3, human=0)
    # noise swell every 4 bars (rises, then drops on the downbeat)
    for bar in range(0, tr.bars, 4):
        dur = tr.beats(16)
        n = ns(dur)
        x = dsp.sweep_filter(noise(n=n) * 1.5, "bp", np.geomspace(300, 5000, n), q=2.0) * np.linspace(0, 1, n) ** 3
        x = dsp.fade(x, 0.0, 0.03)  # cut on the downbeat without a click
        tr.add("tension", dsp.widen(x, 0.6), bar * 4, 0.18, human=0)
    tr.fx_reverb("tension", 0.3, 2.5, hp=150)


def hats(tr: Track, bar: int, pattern: str, gain: float = 0.3, tone: float = 9000, open_gain: float = 0.35):
    """pattern: 16 chars per bar: x = closed, X = accented, o = open, . = rest."""
    for i, ch in enumerate(pattern):
        if ch == ".":
            continue
        b = bar * 4 + i / 4
        if ch == "o":
            tr.add("drums", ins.hat(open_=True, tone=tone * 0.9), b, open_gain, pan=0.25)
        else:
            v = 1.0 if ch == "X" else 0.6 + 0.2 * tr.rng.random()
            tr.add("drums", ins.hat(0.05, tone=tone), b, gain * v, pan=0.15)


def humanized_notes(tr, stem, voice, notes, beat, dur_beats, gain=1.0, strum=0.0, pan_spread=0.3):
    for k, n in enumerate(notes):
        p = (k / max(1, len(notes) - 1) - 0.5) * pan_spread if len(notes) > 1 else 0
        tr.add(stem, voice(mtof(n), tr.beats(dur_beats)), beat + k * strum, gain, pan=p)


# =================================================================== districts

def timeline_square() -> Track:
    """Lo-fi, 100 BPM, F major, lazy 16th swing."""
    tr = Track("timeline_square", 100, swing=0.58)
    prog = [(53, "maj9"), (52, "m7"), (50, "m9"), (57, "m7"), (58, "maj7"), (57, "m7"), (55, "m9"), (48, "9sus")]
    for bar in range(16):
        root, q = prog[bar % 8]
        v = voicing(root, q, lo=55, hi=74)
        b0 = bar * 4
        humanized_notes(tr, "pad", lambda f, d: ins.epiano(f, d, vel=0.7), v, b0, 2.4, 0.22, strum=0.03)
        humanized_notes(tr, "pad", lambda f, d: ins.epiano(f, d, vel=0.45), v, b0 + 2.5, 1.4, 0.14, strum=0.02)
        tr.add("pad", ins.pad([mtof(n) for n in v[:3]], tr.beats(4.2), attack=0.5, release=0.6, cutoff=1100, voices=3), b0, 0.10, human=0)
        # bass
        bn = root - 12 if root - 12 >= 40 else root
        nxt = prog[(bar + 1) % 8][0]
        nxt = nxt - 12 if nxt - 12 >= 40 else nxt
        tr.add("bass", ins.sub_bass(mtof(bn), tr.beats(1.6), drive=1.6), b0, 0.8)
        tr.add("bass", ins.sub_bass(mtof(bn + 7), tr.beats(0.45), drive=1.6), b0 + 2.5, 0.6)
        tr.add("bass", ins.sub_bass(mtof(nxt - 1 if bar % 2 else bn + 12), tr.beats(0.45), drive=1.6), b0 + 3.5, 0.55)
        # drums (boom bap)
        k = ins.kick(0.4, f0=55, punch=0.8, click=0.15, drive=1.3, decay=0.3)
        tr.kick_at(k, b0, 0.9)
        tr.kick_at(k, b0 + 1.75 if bar % 2 else b0 + 2.5, 0.7)
        tr.kick_at(k, b0 + 2.75, 0.55)
        sn = ins.snare(0.3, tone=180, snappy=0.6, decay=0.16)
        tr.add("drums", sn, b0 + 1, 0.55)
        tr.add("drums", sn, b0 + 3, 0.55)
        if bar % 8 == 7:
            for g in (3.25, 3.5, 3.75):
                tr.add("drums", sn, b0 + g, 0.18)
        hats(tr, bar, "X.x.X.xxX.x.X.x." if bar % 4 != 3 else "X.x.X.xxX.x.X.o.", gain=0.22, tone=7500)
    tr.buf["pad"] += dsp.widen(ins.vinyl_crackle(tr.L / SR, density=14, level=0.25), 0.6)[: tr.L]
    # lead: nylon-ish pluck melody (F major pentatonic), 4-bar phrases
    A = [(0, 72, 1), (1, 69, 0.5), (1.5, 67, 0.5), (2.5, 65, 1.5), (5, 69, 0.5), (5.5, 72, 0.5), (6, 74, 1.5),
         (8, 72, 0.5), (8.75, 69, 0.75), (9.5, 67, 1), (12, 65, 0.5), (12.5, 67, 0.5), (13, 69, 2.5)]
    B = [(0, 77, 1), (1.5, 76, 0.5), (2, 74, 1), (3.5, 72, 0.5), (4.5, 69, 1.5), (8, 70, 0.5), (8.5, 69, 0.5),
         (9, 67, 1), (10.5, 65, 0.5), (12, 64, 0.5), (12.5, 65, 0.5), (13, 67, 0.5), (13.5, 60, 2)]
    for start, phrase, oct_ in [(0, A, 0), (16, B, 0), (32, A, 12), (48, B, 0)]:
        for b, n, d in phrase:
            tr.add("lead", ins.pluck_ks(mtof(n + oct_ - 12), tr.beats(d + 0.6), bright=0.35, decay=0.997) * 0.8, start + b, 0.5, pan=-0.15)
    tr.fx_filter("lead", "lp", 4000)
    tr.fx_delay("lead", 0.75, fb=0.35, wet=0.25, lp=2500)
    tr.fx_reverb("lead", 0.25, 1.8)
    tr.fx_filter("drums", "lp", 9000)
    tr.fx_sat("drums", 1.3)
    tr.fx_reverb("drums", 0.08, 0.6, hp=400)
    tr.fx_filter("pad", "lp", 5000)
    tr.fx_reverb("pad", 0.2, 2.0)
    tension(tr, 50)
    return tr


def trenches() -> Track:
    """Trap / phonk, 140 BPM half-time, C# minor. 808 slides, hat rolls, cowbell riff."""
    tr = Track("trenches", 140)
    prog = [(49, "m"), (45, "maj"), (42, "m"), (44, "maj")]
    for bar in range(16):
        root, q = prog[(bar // 2) % 4]
        b0 = bar * 4
        if bar % 2 == 0:
            v = voicing(root, q, lo=56, hi=72)
            tr.add("pad", ins.pad([mtof(n) for n in v], tr.beats(8.2), attack=0.4, release=0.8, cutoff=1300, detune=0.15), b0, 0.35, human=0)
            tr.add("pad", ins.fm_bell(mtof(v[-1] + 12), tr.beats(4), ratio=3.5, index=1.5, decay=tr.beats(4)), b0, 0.10, pan=0.3)
        bn = root - 12
        while bn > 44:
            bn -= 12
        while bn < 33:
            bn += 12
        pat = [(0, 0, 1.5), (1.5, 0, 0.5), (2.75, 0, 0.75), (3.5, 12, 0.5)] if bar % 2 == 0 else \
              [(0, 0, 0.75), (0.75, 0, 1.25), (2.5, 7, 0.5), (3.0, 0, 1.0)]
        prev = None
        for b, iv, d in pat:
            f = mtof(bn + iv)
            tr.add("bass", ins.bass_808(f, tr.beats(d) + 0.02, glide_from=prev if iv == 0 and prev and prev != f else None, drive=2.4), b0 + b, 0.75, human=0)
            prev = f
        k = ins.kick(0.35, f0=60, punch=1.2, click=0.4, drive=2.0, decay=0.22)
        tr.kick_at(k, b0, 0.8)
        tr.kick_at(k, b0 + (2.75 if bar % 2 == 0 else 0.75), 0.7)
        cl = dsp.layer(ins.clap(0.35) * 0.8, ins.snare(0.3, tone=210, snappy=0.9, decay=0.14) * 0.6)
        tr.add("drums", cl, b0 + 2, 0.75)
        if bar % 4 == 3:
            # hat roll: 32nd-note triplets on beat 3
            for i in range(12):
                tr.add("drums", ins.hat(0.03, tone=9500), b0 + 3 + i / 12, 0.18 + 0.02 * (i % 3), pan=0.2, human=0)
            hats(tr, bar, "x.x.x.x.x.x.....", gain=0.25, tone=9500)
        elif bar % 4 == 1:
            hats(tr, bar, "x.x.x.xxx.x.xxxo", gain=0.25, tone=9500)
        else:
            hats(tr, bar, "x.x.x.x.x.x.x.xx", gain=0.25, tone=9500)
    # cowbell riff (2 bars), phonk style
    riff = [(0, 73), (0.5, 73), (0.75, 76), (1.25, 73), (1.75, 68), (2.5, 71), (2.75, 73), (3.25, 76), (3.5, 78),
            (4.0, 80), (4.5, 78), (4.75, 76), (5.25, 73), (6, 71), (6.5, 73), (7, 68), (7.5, 71)]
    for rep in range(8):
        if rep in (2, 6):
            continue  # breathing space
        for b, n in riff:
            tr.add("lead", ins.cowbell(mtof(n), 0.3), rep * 8 + b, 0.35, pan=0.1)
    tr.fx_reverb("lead", 0.18, 1.2)
    tr.fx_sat("drums", 1.6)
    tr.fx_reverb("drums", 0.06, 0.8, hp=600)
    tr.fx_reverb("pad", 0.3, 2.5)
    tension(tr, 49)
    return tr


def perp_pit() -> Track:
    """Techno, 128 BPM, A minor. Rolling bass, acid line, dub stabs."""
    tr = Track("perp_pit", 128)
    stab_prog = [(57, "m7"), (57, "m7"), (53, "maj7"), (55, "7sus")]
    for bar in range(16):
        b0 = bar * 4
        root, q = stab_prog[bar % 4]
        k = ins.kick(0.4, f0=50, punch=1.3, click=0.35, drive=1.8, decay=0.3)
        for beat in range(4):
            tr.kick_at(k, b0 + beat, 0.9)
        cl = ins.clap(0.3, 0.15)
        tr.add("drums", cl, b0 + 1, 0.5, pan=-0.05)
        tr.add("drums", cl, b0 + 3, 0.5, pan=-0.05)
        hats(tr, bar, "x.xXx.xXx.xXx.xX".replace("X", "x") if bar < 8 else "xxoxxxoxxxoxxxox", gain=0.18, tone=10000, open_gain=0.22)
        for beat in range(4):
            tr.add("drums", ins.hat(open_=True, tone=8000), b0 + beat + 0.5, 0.2, pan=-0.2)
        if bar >= 8:
            for e in range(8):
                tr.add("drums", ins.fm_bell(4200, 0.25, ratio=1.41, index=1.0, decay=0.2), b0 + e / 2, 0.05, pan=0.35)
        if bar % 8 == 7:
            for i in range(8):
                tr.add("drums", ins.snare(0.2, tone=220, snappy=0.8, decay=0.1), b0 + 2 + i / 4, 0.15 + 0.04 * i)
        # rolling bass on 16ths 2,3,4 of each beat
        bn = 33 if root == 57 else (29 if root == 53 else 31)
        for beat in range(4):
            for s in (1, 2, 3):
                tr.add("bass", ins.pluck_bass(mtof(bn + (12 if s == 3 and beat == 3 else 0)), tr.beats(0.22)), b0 + beat + s / 4, 0.55 if s != 1 else 0.7, human=0.002)
        # dub stabs
        v = voicing(root, q, lo=57, hi=72)
        for sb in ((0.75, 0.5), (2.5, 0.35)) if bar % 2 == 0 else ((1.5, 0.45),):
            tr.add("pad", ins.pad([mtof(n) for n in v], tr.beats(0.35), attack=0.005, release=0.15, cutoff=1500, voices=3), b0 + sb[0], sb[1], human=0)
    # atmospheric bed under the stabs
    bed = ins.pad([mtof(45), mtof(52), mtof(60)], tr.L / SR, attack=2.0, release=0.1, cutoff=700, voices=3)
    tr.buf["pad"] += dsp.crossfade_loop(np.concatenate([bed, bed[: ns(1.0)]]), tr.L, ns(1.0)) * 0.12
    tr.fx_delay("pad", 0.75, fb=0.55, wet=0.45, lp=2500)
    tr.fx_reverb("pad", 0.3, 3.0)
    # acid line: 1-bar pattern, cutoff swept over the loop
    pat = [(0, 45, True, False), (0.25, 45, False, False), (0.5, 57, False, True), (0.75, 45, False, False),
           (1.0, 48, True, False), (1.25, 45, False, False), (1.5, 55, False, True), (1.75, 57, False, True),
           (2.0, 45, True, False), (2.25, 60, False, False), (2.5, 45, False, False), (2.75, 52, True, False),
           (3.0, 45, False, False), (3.25, 57, False, True), (3.5, 55, True, False), (3.75, 48, False, False)]
    for bar in range(16):
        sweep = 0.5 - 0.5 * np.cos(2 * np.pi * bar / 16)
        prev = None
        for b, n, acc, slide in pat:
            f = mtof(n)
            x = ins.acid(f, tr.beats(0.26), accent=acc, cutoff=250 + 900 * sweep, env_amt=1200 + 2200 * sweep,
                         res=0.7, slide_from=prev if slide else None)
            tr.add("lead", x, bar * 4 + b, 0.35, pan=0.05, human=0.001)
            prev = f
    tr.fx_delay("lead", 0.75, fb=0.3, wet=0.2, lp=3000)
    tr.fx_reverb("lead", 0.12, 1.5)
    tr.sidechain("bass", 0.7, 0.16)
    tr.sidechain("pad", 0.5, 0.25)
    tr.fx_sat("drums", 1.4)
    tension(tr, 57, bright=1.2)
    return tr


def airdrop_farm() -> Track:
    """Banjo lo-fi, 90 BPM, G major. Forward-roll banjo, upright bass, brushes, organ bed."""
    tr = Track("airdrop_farm", 90, swing=0.55)
    prog = [(55, "maj"), (48, "maj"), (55, "maj"), (50, "maj"), (52, "m"), (48, "maj"), (50, "maj"), (55, "maj"),
            (55, "maj"), (48, "maj"), (55, "maj"), (50, "maj"), (52, "m"), (48, "maj"), (50, "7"), (50, "7")]
    for bar in range(16):
        root, q = prog[bar]
        b0 = bar * 4
        v = voicing(root, q, lo=55, hi=71)
        tr.add("pad", ins.pad([mtof(n) for n in v], tr.beats(4.1), attack=0.3, release=0.5, cutoff=1400, voices=2, kind="tri"), b0, 0.5, human=0)
        for n in v:
            tr.add("pad", ins.organ(mtof(n), tr.beats(3.9)), b0, 0.10, pan=(n - 63) / 20)
        bn = root - 12 if root - 12 >= 40 else root
        tr.add("bass", ins.upright(mtof(bn), tr.beats(1.8)), b0, 0.9)
        tr.add("bass", ins.upright(mtof(bn + 7 if bn + 7 <= 52 else bn - 5), tr.beats(1.8)), b0 + 2, 0.75)
        if bar % 4 == 3:
            nxt = prog[(bar + 1) % 16][0]
            nxt = nxt - 12 if nxt - 12 >= 40 else nxt
            tr.add("bass", ins.upright(mtof(nxt - 2), tr.beats(0.5)), b0 + 3.5, 0.6)
        k = ins.kick(0.35, f0=58, punch=0.6, click=0.1, drive=1.2, decay=0.25)
        tr.kick_at(k, b0, 0.7)
        tr.kick_at(k, b0 + 2, 0.55)
        br = dsp.layer(ins.snare(0.25, tone=200, snappy=1.0, decay=0.2) * 0.5, ins.shaker(0.2, 1.2) * 0.5)
        tr.add("drums", br, b0 + 1, 0.4)
        tr.add("drums", br, b0 + 3, 0.4)
        for e in range(8):
            tr.add("drums", ins.shaker(0.09, 1.0 if e % 2 else 0.6), b0 + e / 2, 0.2, pan=0.3)
    # banjo forward roll on 16ths over chord tones + G4 drone (open-G banjo)
    roll = [0, 1, 2, 0, 1, 2, 0, 3]
    for bar in range(16):
        root, q = prog[bar]
        tones = voicing(root, q, lo=59, hi=76)[:3]
        tones = (tones + [tones[-1] + 12])[:3] + [67]
        for i in range(16):
            if bar >= 8 and i >= 8 and bar % 2 == 1:
                continue  # leave room for the whistle answer
            n = tones[roll[i % 8]]
            acc = 1.0 if i % 4 == 0 else 0.7
            tr.add("lead", ins.banjo(mtof(n), 0.5), bar * 4 + i / 4, 0.32 * acc, pan=-0.2, human=0.003)
    melody = [(1, 74, 1), (2, 71, 0.5), (2.5, 74, 1.5), (5, 76, 1), (6, 74, 1.5), (9, 79, 1), (10, 76, 0.5),
              (10.5, 74, 1.5), (13, 72, 1), (14, 71, 1.8)]
    for b, n, d in melody:
        tr.add("lead", ins.whistle(mtof(n), tr.beats(d)), 32 + b, 0.18, pan=0.2)
    tr.fx_reverb("lead", 0.2, 1.4)
    tr.fx_filter("drums", "lp", 7000)
    tr.fx_reverb("drums", 0.1, 0.7)
    tr.buf["pad"] += dsp.widen(ins.vinyl_crackle(tr.L / SR, density=10, level=0.18), 0.6)[: tr.L]
    tr.fx_reverb("pad", 0.2, 1.8)
    tension(tr, 55, pulse=False)
    return tr


def builder_hills() -> Track:
    """Synthwave, 95 BPM, E minor. Supersaw pad, driving 8th bass, gated snare, arp + lead."""
    tr = Track("builder_hills", 95)
    prog = [(52, "m"), (48, "maj"), (55, "maj"), (50, "maj")]
    for bar in range(16):
        root, q = prog[(bar // 2) % 4]
        b0 = bar * 4
        v = voicing(root, q, lo=55, hi=72)
        if bar % 2 == 0:
            tr.add("pad", ins.pad([mtof(n) for n in v] + [mtof(v[0] + 12)], tr.beats(8.3), attack=0.5, release=0.9, cutoff=2600, detune=0.18, voices=5), b0, 0.4, human=0)
        bn = root - 12 if root - 12 >= 40 else root
        for e in range(8):
            n = bn + (12 if e % 2 else 0)
            tr.add("bass", ins.pluck_bass(mtof(n), tr.beats(0.45)), b0 + e / 2, 0.55 if e % 2 else 0.7, human=0.002)
        k = ins.kick(0.45, f0=50, punch=1.0, click=0.3, drive=1.6, decay=0.35)
        tr.kick_at(k, b0, 0.9)
        tr.kick_at(k, b0 + 2, 0.9)
        if bar % 2:
            tr.kick_at(k, b0 + 2.5, 0.6)
        sn = ins.snare(0.35, tone=190, snappy=1.0, decay=0.2)
        gated = dsp.reverb(sn, wet=0.9, decay=1.2, predelay=0.005, keep_tail=True)[: ns(0.32)]
        gated = dsp.fade(gated, 0.0, 0.03)
        tr.add("drums", gated, b0 + 1, 0.55)
        tr.add("drums", gated, b0 + 3, 0.55)
        hats(tr, bar, "xxXxxxXxxxXxxxXx", gain=0.14, tone=9000)
        if bar % 8 == 7:
            for i, f in enumerate([180, 150, 120, 95]):
                tr.add("drums", ins.tom(f, 0.4), b0 + 2 + i * 0.5, 0.5, pan=0.4 - i * 0.25)
    tr.sidechain("bass", 0.35, 0.15)
    tr.sidechain("pad", 0.35, 0.25)
    # arp (16ths, up pattern) for the whole loop, melody joins in bars 9-16
    for bar in range(16):
        root, q = prog[(bar // 2) % 4]
        tones = voicing(root, q, lo=64, hi=84)
        seq = tones + [tones[0] + 12]
        for i in range(16):
            n = seq[i % len(seq)]
            tr.add("lead", ins.lead_square(mtof(n), tr.beats(0.22), cutoff=2500, pw=0.3, vib=0, release=0.03), bar * 4 + i / 4, 0.12, pan=0.3 * np.sin(i), human=0)
    mel = [(0, 76, 2), (2, 74, 1), (3, 71, 1), (4, 72, 3), (7, 71, 0.5), (7.5, 72, 0.5), (8, 74, 2), (10, 79, 1.5),
           (11.5, 78, 0.5), (12, 74, 3.5), (16, 76, 2), (18, 78, 1), (19, 79, 1), (20, 76, 3), (24, 74, 2), (26, 71, 1),
           (27, 72, 1), (28, 71, 3.5)]
    for b, n, d in mel:
        tr.add("lead", ins.lead_square(mtof(n), tr.beats(d), cutoff=3500, pw=0.45, vib=0.35, attack=0.02, release=0.15), 32 + b, 0.3, pan=-0.05)
    tr.fx_delay("lead", 0.75, fb=0.4, wet=0.3, lp=4000)
    tr.fx_reverb("lead", 0.25, 2.2)
    tr.fx_reverb("pad", 0.3, 2.8)
    tr.fx_sat("drums", 1.3)
    tension(tr, 52)
    return tr


def dubai_marina() -> Track:
    """Deep house, 110 BPM, F minor. Off-beat chords, syncopated sub, shuffled hats, vocal chops."""
    tr = Track("dubai_marina", 110, swing=0.56)
    prog = [(53, "m9"), (49, "maj9"), (58, "m9"), (48, "7sus")]
    for bar in range(16):
        root, q = prog[(bar // 2) % 4]
        b0 = bar * 4
        v = voicing(root, q, lo=56, hi=75)
        for cb, d, g in ((0, 1.2, 0.26), (1.75, 0.5, 0.18), (2.5, 1.0, 0.22)):
            humanized_notes(tr, "pad", lambda f, dd: ins.epiano(f, dd, vel=0.65), v, b0 + cb, d, g, strum=0.01)
        if bar % 2 == 0:
            tr.add("pad", ins.pad([mtof(n) for n in v[:4]], tr.beats(8.1), attack=0.8, release=0.8, cutoff=1500, voices=3), b0, 0.12, human=0)
        bn = root - 12 if root - 12 >= 40 else root
        for b, iv, d in ((0.5, 0, 0.4), (1.5, 0, 0.3), (1.75, 12, 0.2), (2.5, 0, 0.4), (3.5, 7 if bar % 2 else 10, 0.4)):
            tr.add("bass", ins.sub_bass(mtof(bn + iv), tr.beats(d), drive=1.8), b0 + b, 0.75)
        k = ins.kick(0.45, f0=48, punch=0.9, click=0.2, drive=1.4, decay=0.35)
        for beat in range(4):
            tr.kick_at(k, b0 + beat, 0.85)
        cl = ins.clap(0.4, 0.2)
        tr.add("drums", cl, b0 + 1, 0.4)
        tr.add("drums", cl, b0 + 3, 0.4)
        hats(tr, bar, "x.xxx.xxx.xxx.xx", gain=0.14, tone=9500)
        for beat in range(4):
            tr.add("drums", ins.hat(open_=True, tone=8500), b0 + beat + 0.5, 0.16, pan=-0.2)
        for s in range(16):
            tr.add("drums", ins.shaker(0.07, 1.0 if s % 4 == 2 else 0.5), b0 + s / 4, 0.08, pan=0.35)
        if bar % 4 == 1:
            tr.add("drums", ins.rim(), b0 + 3.75, 0.25, pan=0.3)
    tr.sidechain("bass", 0.5, 0.15)
    tr.sidechain("pad", 0.45, 0.25)
    chops = [(0.5, 72, "a"), (1.25, 75, "o"), (2.5, 77, "a"), (6.5, 75, "e"), (7.0, 72, "a"),
             (8.5, 80, "o"), (9.25, 77, "a"), (10.5, 75, "a"), (14.5, 72, "o"), (15.0, 70, "a")]
    for rep in range(4):
        for b, n, vw in chops:
            if rep == 0 and b > 8:
                continue
            tr.add("lead", ins.vox_chop(mtof(n), tr.beats(0.45), vw), rep * 16 + b, 0.3, pan=0.15)
    for bar in range(8, 16, 2):
        tr.add("lead", ins.fm_bell(mtof(84), 1.0, ratio=3.0, index=1.2, decay=0.9), bar * 4 + 3.5, 0.12, pan=-0.3)
    tr.fx_delay("lead", 0.75, fb=0.35, wet=0.3, lp=3500)
    tr.fx_reverb("lead", 0.3, 2.4)
    tr.fx_reverb("pad", 0.22, 2.2)
    tr.fx_reverb("drums", 0.07, 1.0, hp=500)
    tension(tr, 53)
    return tr


def club() -> Track:
    """Clubs and parties (spec: Amapiano / Afrobeats, 112 BPM). Afropiano groove with Nigerian flavour, own synthesis:
    - log-drum bassline (Wikipedia "Amapiano": "a wide percussive bassline"), shakers, jazzy piano (pad stem);
    - Afrobeats 3-2 clave on a woodblock (Wikipedia "Afrobeats": 3-2 / 2-3 clave), kick on every beat with rim/clap
      on 2 and 4 (Native Instruments amapiano guide, 4 Sep 2025: kick every beat, shakers, ~110s BPM);
    - dundun talking-drum glide fills every 4 bars (Wikipedia "Talking drum": pitch glides within a beat);
    - lead stem: interlocking highlife-style guitar plucks + a flute/whistle answer + one DJ air-horn drop per loop
      (lead only enters when the market is green: "we're so back").
    C major ii-V-I-vi. Docs: docs/progress/audio-integration.md "Sound culture research"."""
    tr = Track("club", 112, swing=0.54)
    prog = [(50, "m9"), (55, "13"), (48, "maj9"), (57, "m9")]
    log_pats = [
        [(0, 0), (0.75, 0), (1.5, 7), (2.25, 0), (2.75, 12), (3.5, 10)],
        [(0, 0), (0.5, 0), (1.25, 0), (2.0, 7), (2.75, 0), (3.25, 5), (3.75, 7)],
    ]
    clave = [[0, 6, 12], [4, 8]]                       # 3-2 son clave over two bars (16th positions)
    for bar in range(16):
        root, q = prog[bar % 4]
        b0 = bar * 4
        v = voicing(root, q, lo=57, hi=77)
        for cb, d, g in ((0, 0.6, 0.22), (0.75, 0.4, 0.15), (1.5, 0.6, 0.2), (2.5, 0.4, 0.16), (3.25, 0.6, 0.18)):
            humanized_notes(tr, "pad", lambda f, dd: ins.piano_ish(f, dd, vel=0.7), v, b0 + cb, d, g, strum=0.008)
        if bar % 4 == 0:
            tr.add("pad", ins.pad([mtof(n) for n in voicing(root, q, lo=60, hi=79)], tr.beats(16.2), attack=1.0, release=1.0, cutoff=1800, voices=3), b0, 0.08, human=0)
        bn = root - 12 if root - 12 >= 38 else root
        pat = log_pats[1 if bar % 4 == 3 else 0]
        for b, iv in pat:
            tr.add("bass", ins.log_drum(mtof(bn + iv), 0.45), b0 + b, 0.6, human=0.002)
        k = ins.kick(0.4, f0=52, punch=0.8, click=0.15, drive=1.3, decay=0.3)
        for beat in range(4):
            tr.kick_at(k, b0 + beat, 0.75 if beat % 2 == 0 else 0.6)
        sn = dsp.layer(ins.rim(0.08, 1500) * 0.6, ins.clap(0.25, 0.1) * 0.6)
        tr.add("drums", sn, b0 + 1, 0.42)
        tr.add("drums", sn, b0 + 3, 0.42)
        for s16 in range(16):
            acc = 1.0 if s16 % 4 == 2 else (0.75 if s16 % 2 else 0.45)
            tr.add("drums", ins.shaker(0.08, acc), b0 + s16 / 4, 0.16, pan=0.3)
        for s16 in clave[bar % 2]:
            tr.add("drums", ins.rim(0.05, 2200), b0 + s16 / 4, 0.17, pan=-0.4)
        tr.add("drums", ins.hat(open_=True, tone=8500), b0 + 1.5, 0.12, pan=0.2)
        tr.add("drums", ins.hat(open_=True, tone=8500), b0 + 3.5, 0.12, pan=0.2)
        if bar % 4 == 3:
            # dundun "speech" fill: low-high-glide answer on beat 4
            tr.add("drums", ins.talking_drum(150, 0.22, glide=1.3), b0 + 3.0, 0.32, pan=-0.2)
            tr.add("drums", ins.talking_drum(190, 0.2, glide=1.25), b0 + 3.25, 0.28, pan=-0.2)
            tr.add("drums", ins.talking_drum(160, 0.4, glide=1.55, squeeze_at=0.05, release_at=0.22), b0 + 3.5, 0.36, pan=-0.2)
        elif bar % 2 == 1:
            tr.add("drums", ins.talking_drum(175, 0.3, glide=1.4, squeeze_at=0.06), b0 + 2.75, 0.22, pan=-0.25)
    tr.sidechain("pad", 0.25, 0.2)
    # Lead: highlife-style interlocking guitar (two plucked lines) on bars 0-7 and 8-15, flute answer, one air horn.
    # Two interlocking guitar lines drawn from each bar's chord tones (always consonant with the ii-V-I-vi).
    gtr_a = [(0, 0), (0.5, 1), (1, 2), (1.75, 1), (2.5, 3), (3, 2)]
    gtr_b = [(0.25, 0), (0.75, 1), (1.5, 2), (2.25, 1), (3.25, 0), (3.75, 1)]
    for bar in range(16):
        root, q = prog[bar % 4]
        hi = voicing(root, q, lo=72, hi=88)
        lo_ = voicing(root, q, lo=64, hi=76)
        for b, k_ in gtr_a:
            tr.add("lead", ins.pluck_ks(mtof(hi[k_ % len(hi)]), tr.beats(0.5), bright=0.7), bar * 4 + b, 0.16, pan=0.35)
        for b, k_ in gtr_b:
            tr.add("lead", ins.pluck_ks(mtof(lo_[k_ % len(lo_)]), tr.beats(0.4), bright=0.5), bar * 4 + b, 0.11, pan=-0.35)
    mel = [(0.5, 76, 0.5), (1, 79, 1), (2.5, 77, 0.5), (3, 76, 0.75), (4, 74, 1.5), (6, 72, 0.5), (6.5, 74, 1)]
    for rep in (1, 3):
        for b, n, d in mel:
            tr.add("lead", ins.whistle(mtof(n), tr.beats(d), vib=0.5), rep * 16 + 8 + b, 0.18, pan=-0.1)
    horn = dsp.widen(ins.air_horn(0.28), 0.4)
    horn2 = dsp.widen(ins.air_horn(0.6, bend=0.08), 0.4)
    tr.add("lead", horn, 8 * 4, 0.10, human=0)
    tr.add("lead", horn2, 8 * 4 + 0.5, 0.10, human=0)
    tr.fx_delay("lead", 0.75, fb=0.3, wet=0.22)
    tr.fx_reverb("lead", 0.22, 1.8)
    tr.fx_reverb("pad", 0.2, 1.8)
    tr.fx_reverb("drums", 0.06, 0.8, hp=500)
    tension(tr, 57)
    return tr

DISTRICTS = {
    "timeline_square": timeline_square, "trenches": trenches, "perp_pit": perp_pit, "airdrop_farm": airdrop_farm,
    "builder_hills": builder_hills, "dubai_marina": dubai_marina, "club": club,
}

# Stem balance: each stem is first set to this loudness (LUFS) on its own, then the summed mix of
# pad+bass+drums+lead is normalised to TARGET_LUFS (every stem gets that same final gain).
STEM_LUFS_DEFAULT = {"drums": -20.5, "bass": -21.0, "pad": -22.0, "lead": -23.0, "tension": -23.0}
STEM_LUFS = {
    "timeline_square": {"pad": -20.5, "drums": -21.0, "bass": -22.0, "lead": -23.0},
    "trenches": {"drums": -20.0, "bass": -20.0, "pad": -24.0, "lead": -22.5},
    "perp_pit": {"drums": -19.5, "bass": -22.0, "pad": -24.0, "lead": -22.0},
    "airdrop_farm": {"lead": -21.0, "pad": -22.5, "bass": -22.5, "drums": -23.0},
    "builder_hills": {"pad": -21.0, "drums": -20.5, "bass": -21.5, "lead": -22.5},
    "dubai_marina": {"drums": -20.5, "bass": -21.0, "pad": -21.5, "lead": -23.5},
    "club": {"bass": -20.5, "drums": -20.5, "pad": -22.0, "lead": -23.5},
}


def soft_clip_peaks(x: np.ndarray, thr: float) -> np.ndarray:
    a = np.abs(x)
    over = a > thr
    y = x.copy()
    y[over] = np.sign(x[over]) * (thr + (a[over] - thr) / (1 + (a[over] - thr) / (thr * 0.25 + 1e-9)))
    return y


def limiter_gain(x: np.ndarray, ceil_db: float, block: int = 240) -> np.ndarray:
    """Circular look-ahead peak limiter gain (5 ms blocks, linear between block centres).
    Guarantees |x * g| <= ceiling on every sample; inter-sample peaks get the extra margin above."""
    n = len(x)
    a = np.abs(x).max(axis=1) if x.ndim == 2 else np.abs(x)
    ceil = dsp.db(ceil_db)
    graw = np.minimum(1.0, ceil / (a + 1e-12))
    nb = int(np.ceil(n / block))
    pad = np.ones(nb * block)
    pad[:n] = graw
    bmin = pad.reshape(nb, block).min(axis=1)
    tgt = np.minimum(np.minimum(np.roll(bmin, 1), bmin), np.roll(bmin, -1))
    centres = np.arange(nb) * block + block / 2
    xp = np.concatenate([[centres[-1] - nb * block], centres, [centres[0] + nb * block]])
    fp = np.concatenate([[tgt[-1]], tgt, [tgt[0]]])
    return np.interp(np.arange(n), xp, fp)


def render(name: str) -> dict:
    tr = DISTRICTS[name]()
    targets = STEM_LUFS_DEFAULT | STEM_LUFS.get(name, {})
    stems = {}
    for s in STEMS:
        x = tr.buf[s]
        stems[s] = x * dsp.db(targets[s] - lufs(x))
    full = sum(stems[s] for s in MIX_STEMS)
    g_lin = dsp.db(TARGET_LUFS - lufs(full))
    # Mix-bus peak limiter, applied as one shared gain envelope to every stem so stems still sum to the mix.
    for _ in range(4):
        worst = full + stems["tension"]           # mood transitions can briefly play everything at once
        tp = max(true_peak_db(full * g_lin), true_peak_db(worst * g_lin))
        if tp <= TP_CEIL:
            break
        env = np.minimum(limiter_gain(full * g_lin, TP_CEIL - 0.6), limiter_gain(worst * g_lin, TP_CEIL - 0.6))
        for s in STEMS:
            stems[s] = stems[s] * env[:, None]
        full = sum(stems[s] for s in MIX_STEMS)
        g_lin = dsp.db(TARGET_LUFS - lufs(full))
    tmp = BUILD / "music"
    tmp.mkdir(parents=True, exist_ok=True)
    out = {"district": name, "bpm": tr.bpm, "bars": tr.bars, "loop_samples": tr.L, "sr": SR,
           "loop_seconds": round(tr.L / SR, 4), "stem_lufs_pre_gain": targets, "mix_gain_db": round(20 * np.log10(g_lin), 2),
           "files": {}}
    for s, x in list(stems.items()) + [("full", full)]:
        wav = tmp / f"mus_{name}_{s}.wav"
        export_wav(wav, x * g_lin, peak_db=None, gain_db=0, loop=True)
        # Stems ship (music/stems/<set>/ → imported Compressed In Memory); the full preview is a reference only and
        # stays in build/ so it never reaches the APK.
        ogg = (OUT / "music" / "stems" / name / f"mus_{name}_{s}.ogg") if s != "full" else (BUILD / "music" / f"mus_{name}_full_preview.ogg")
        ogg.parent.mkdir(parents=True, exist_ok=True)
        encode_ogg(wav, ogg, OGG_Q)
        out["files"][s] = str(ogg.relative_to(OUT.parent))
    print(f"{name:16} {tr.bpm:5.0f} BPM  loop {tr.L / SR:6.2f}s  mix gain {20 * np.log10(g_lin):+.1f} dB  TP {true_peak_db(full * g_lin):.1f}")
    return out


def main(names: list[str]) -> None:
    rep_path = Path(__file__).with_name("build_music.json")
    rep = json.loads(rep_path.read_text()) if rep_path.exists() else {}
    for n in names or list(DISTRICTS):
        rep[n] = render(n)
        rep_path.write_text(json.dumps(rep, indent=2))


if __name__ == "__main__":
    main(sys.argv[1:])
