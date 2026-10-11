#!/usr/bin/env python3
"""Render every CT Life one-shot SFX into audio/processed/sfx/.

Most sounds are synthesised here (own work, CC0). A few layer CC0 recordings from raw/
(Kenney packs, freesound previews); those are named in SOURCES below and in LICENSES.md.

All one-shots: 48 kHz, 16-bit PCM, normalised to -1.0 dBTP (true peak) so levels are consistent;
the in-game mix level lives in sfx-map.json (`volume_db`).
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

import numpy as np

import dsp
import instruments as ins
from dsp import SR, ns, mtof, filt, env_exp, env_adsr, noise, osc, sat
from master import OUT, RAW, export_wav, read_audio

SFX = OUT / "sfx"
K = RAW / "kenney"
F = RAW / "freesound"
REGISTRY: dict[str, dict] = {}
SOURCES: dict[str, list[str]] = {}


def sfx(id_: str, category: str, channels: int = 1, volume_db: float = 0.0, loop: bool = False, desc: str = ""):
    def deco(fn):
        REGISTRY[id_] = {"fn": fn, "category": category, "channels": channels, "volume_db": volume_db,
                         "loop": loop, "desc": desc}
        return fn
    return deco


def place(buf: np.ndarray, x: np.ndarray, t: float, gain: float = 1.0):
    s = ns(t)
    if s + len(x) > len(buf):
        x = x[: len(buf) - s]
    if buf.ndim == 2 and x.ndim == 1:
        x = dsp.to_stereo(x)
    if buf.ndim == 1 and x.ndim == 2:
        x = x.mean(axis=1)
    buf[s:s + len(x)] += x * gain


def canvas(dur: float, ch: int = 1) -> np.ndarray:
    return np.zeros(ns(dur)) if ch == 1 else np.zeros((ns(dur), 2))


def trim_tail(x: np.ndarray, thresh_db: float = -60.0, pad: float = 0.02) -> np.ndarray:
    a = np.abs(x if x.ndim == 1 else x.max(axis=1))
    m = a.max() or 1
    idx = np.where(a > m * dsp.db(thresh_db))[0]
    if len(idx) == 0:
        return x
    end = min(len(x), idx[-1] + ns(pad))
    return dsp.fade(x[:end], 0.0005, min(0.03, end / SR / 4))


def src(path: Path, mono: bool = True, start: float = 0.0, dur: float | None = None, used_by: str = "") -> np.ndarray:
    x = read_audio(path, mono=mono)
    s = ns(start)
    x = x[s: s + ns(dur)] if dur else x[s:]
    SOURCES.setdefault(used_by, []).append(str(path.relative_to(RAW.parent)))
    return x


def onset(x: np.ndarray, thresh_db: float = -30.0) -> int:
    a = np.abs(x if x.ndim == 1 else x.max(axis=1))
    idx = np.where(a > a.max() * dsp.db(thresh_db))[0]
    return int(idx[0]) if len(idx) else 0


def small_room(x: np.ndarray, wet: float = 0.12, decay: float = 0.5) -> np.ndarray:
    y = dsp.reverb(x, wet=wet, decay=decay, predelay=0.006, damp=5000, hp=300)
    return y.mean(axis=1) if x.ndim == 1 else y


# ====================================================================== UI

@sfx("sfx_ui_tap", "ui", volume_db=-12, desc="Soft button tap")
def ui_tap():
    n = ns(0.08)
    t = np.arange(n) / SR
    blip = np.sin(2 * np.pi * (1500 - 300 * t / t[-1]) * t) * env_exp(n, 0.035, attack=0.0005)
    clk = filt(noise(n=n), "bp", 3800, q=1.2) * env_exp(n, 0.008, attack=0.0002) * 0.8
    return trim_tail(blip * 0.7 + clk)


def _toggle(up: bool):
    x = canvas(0.2)
    f = [mtof(81), mtof(88)] if up else [mtof(88), mtof(81)]
    for i, fr in enumerate(f):
        n = ns(0.09)
        t = np.arange(n) / SR
        b = (np.sin(2 * np.pi * fr * t) + 0.25 * np.sin(4 * np.pi * fr * t)) * env_exp(n, 0.07, attack=0.001)
        place(x, b, i * 0.045, 0.8 if i == 0 else 1.0)
    clk = filt(noise(n=ns(0.006)), "hp", 3000) * 0.3
    place(x, clk, 0)
    return trim_tail(x)


@sfx("sfx_ui_toggle_on", "ui", volume_db=-12, desc="Toggle switched on (rising)")
def ui_toggle_on():
    return _toggle(True)


@sfx("sfx_ui_toggle_off", "ui", volume_db=-12, desc="Toggle switched off (falling)")
def ui_toggle_off():
    return _toggle(False)


@sfx("sfx_ui_error", "ui", volume_db=-10, desc="Error / not allowed: two low buzzy notes")
def ui_error():
    x = canvas(0.36)
    for i, m in enumerate([57, 53]):
        n = ns(0.13)
        f = mtof(m)
        b = 0.6 * osc("square", f, n=n) + 0.4 * osc("saw", f * 1.01, n=n)
        b = filt(b, "lp", 1600) * env_adsr(n, 0.004, 0.05, 0.7, 0.04)
        place(x, b, i * 0.15)
    return trim_tail(small_room(x, 0.08))


@sfx("sfx_ui_success", "ui", volume_db=-10, desc="Success: bright rising two-note chime")
def ui_success():
    x = canvas(0.7)
    place(x, ins.chime(mtof(84), 0.5), 0.0, 0.8)
    place(x, ins.chime(mtof(91), 0.6), 0.075, 1.0)
    return trim_tail(small_room(x, 0.15, 0.7))


@sfx("sfx_ui_toast", "ui", volume_db=-12, desc="Toast / banner appears: soft marimba plink")
def ui_toast():
    x = canvas(0.6)
    place(x, ins.marimba(mtof(88), 0.4), 0, 1.0)
    place(x, ins.marimba(mtof(95), 0.3), 0.085, 0.45)
    return trim_tail(small_room(x, 0.12, 0.6))


@sfx("sfx_ui_open", "ui", volume_db=-14, desc="Panel / phone opens (Kenney maximize_003 + soft swish)")
def ui_open():
    k = src(K / "interface-sounds/Audio/maximize_003.ogg", used_by="sfx_ui_open")
    sw = ins.whoosh(0.18, 600, 3000, q=1.5, shape=0.6) * 0.25
    x = canvas(max(len(k) / SR, 0.2) + 0.02)
    place(x, sw, 0)
    place(x, k, 0.02)
    return trim_tail(x)


@sfx("sfx_ui_close", "ui", volume_db=-14, desc="Panel / phone closes (Kenney minimize_003)")
def ui_close():
    return trim_tail(src(K / "interface-sounds/Audio/minimize_003.ogg", used_by="sfx_ui_close"))


# ====================================================================== trading

@sfx("sfx_buy", "trading", volume_db=-8, desc="Buy filled: rising chime arpeggio + coin")
def buy():
    x = canvas(1.0)
    for i, m in enumerate([84, 88, 91]):
        place(x, ins.chime(mtof(m), 0.5), i * 0.05, 0.6 + 0.2 * i)
    place(x, ins.coin(2900, 0.45), 0.13, 0.45)
    place(x, ins.sparkle(0.5, 5, 3000, 2.0), 0.15, 0.18)
    return trim_tail(small_room(x, 0.14, 0.8))


@sfx("sfx_sell", "trading", volume_db=-8, desc="Sell filled: paper swish + falling two-note chime")
def sell():
    x = canvas(0.9)
    place(x, ins.whoosh(0.22, 2500, 700, q=1.2, shape=0.3), 0, 0.35)
    place(x, ins.chime(mtof(91), 0.45), 0.05, 0.9)
    place(x, ins.chime(mtof(84), 0.55), 0.13, 0.8)
    return trim_tail(small_room(x, 0.12, 0.7))


@sfx("sfx_take_profit", "trading", volume_db=-6, desc="Take-profit hit: ka-ching register bell + coins + chord")
def take_profit():
    x = canvas(1.4)
    for i, f in enumerate([2700, 3300, 2500, 3900]):
        place(x, ins.coin(f, 0.4), 0.0 + i * 0.028, 0.5)
    bell = ins.fm_bell(mtof(96), 1.1, ratio=1.41, index=2.2, decay=1.0)
    place(x, bell, 0.09, 0.7)
    for m in [72, 76, 79, 84]:
        place(x, ins.chime(mtof(m), 0.9), 0.1, 0.35)
    place(x, ins.sparkle(0.8, 8, 2500, 2.5), 0.12, 0.2)
    return trim_tail(small_room(x, 0.16, 0.9))


@sfx("sfx_new_coin", "trading", volume_db=-8, desc="New coin listed: bubble pop + twinkle")
def new_coin():
    x = canvas(0.7)
    n = ns(0.06)
    t = np.arange(n) / SR
    f = np.geomspace(380, 1500, n)
    pop = np.sin(2 * np.pi * np.cumsum(f) / SR) * env_exp(n, 0.05, attack=0.0005)
    place(x, pop, 0, 1.0)
    place(x, ins.chime(mtof(96), 0.35), 0.05, 0.45)
    place(x, ins.sparkle(0.45, 6, 3500, 1.8), 0.07, 0.25)
    return trim_tail(small_room(x, 0.1, 0.6))


@sfx("sfx_whale_horn", "trading", volume_db=-6, desc="Whale alert: deep ship horn (synth + CC0 fog horn layer)")
def whale_horn():
    dur = 2.6
    n = ns(dur)
    t = np.arange(n) / SR
    f0 = mtof(38) * (1 + 0.02 * (1 - np.exp(-t / 0.15)) - 0.02) * (1 + 0.003 * np.sin(2 * np.pi * 4.5 * t))
    x = osc("saw", f0, n=n) + 0.7 * osc("saw", f0 * 1.005, n=n) + 0.5 * osc("pulse", f0 * 2, n=n, pw=0.3)
    x = filt(x, "lp", 900)
    x = dsp.eq(x, "peak", 320, 6, q=2)
    x = dsp.eq(x, "peak", 700, 4, q=3)
    x = sat(x * 1.3, 1.3) * env_adsr(n, 0.18, 0.2, 0.9, 0.6, gate=1.7)
    out = x
    try:
        r = src(F / "181949.ogg", used_by="sfx_whale_horn")
        r = r[onset(r):]
        r = r[: n]
        r = dsp.fade(r, 0.01, 0.6)
        out = out * 0.75 + np.pad(r, (0, n - len(r))) * 0.7 * (np.max(np.abs(x)) / (np.max(np.abs(r)) + 1e-9))
    except FileNotFoundError:
        pass
    y = dsp.reverb(out, wet=0.25, decay=2.2, predelay=0.03, damp=3000, hp=80)
    return trim_tail(y.mean(axis=1))


@sfx("sfx_pnl_card_flip", "trading", volume_db=-8, desc="PnL card flip: swish + card snap (Kenney card-place-1)")
def pnl_card_flip():
    x = canvas(0.7)
    place(x, ins.whoosh(0.2, 700, 4500, q=1.4, shape=0.7), 0, 0.5)
    card = src(K / "casino-audio/Audio/card-place-1.ogg", used_by="sfx_pnl_card_flip")
    place(x, card, 0.13, 1.0)
    place(x, ins.sparkle(0.4, 4, 3500, 1.6), 0.18, 0.12)
    return trim_tail(x)


# ====================================================================== stingers (stereo)

def siren(dur: float, f_lo: float, f_hi: float, rate: float) -> np.ndarray:
    n = ns(dur)
    t = np.arange(n) / SR
    sq = (np.floor(t * rate * 2) % 2)
    f = f_lo + (f_hi - f_lo) * sq
    f = filt(f, "lp", 60)  # glide between the two tones
    x = 0.6 * osc("square", f, n=n) + 0.4 * osc("saw", f * 1.002, n=n)
    x = filt(x, "lp", 3500)
    x = dsp.eq(x, "peak", 1200, 5, q=1.5)
    return sat(x, 1.5) * env_adsr(n, 0.01, 0.05, 1.0, 0.08)


@sfx("sfx_liquidation", "stinger", channels=2, volume_db=-3, desc="Liquidation: alarm, then glass break, then falling whoosh + thud")
def liquidation():
    x = canvas(3.6, 2)
    place(x, dsp.pan(siren(0.85, 760, 980, 3.0), 0.0), 0.0, 0.55)
    # glass: two CC0 recordings + synth shards
    try:
        g1 = src(F / "221528.ogg", mono=False, used_by="sfx_liquidation")
        g1 = g1[onset(g1):][: ns(1.6)]
        place(x, dsp.fade(g1, 0.001, 0.4), 0.78, 0.9 / (np.max(np.abs(g1)) + 1e-9) * 0.8)
    except FileNotFoundError:
        pass
    k = src(K / "impact-sounds/Audio/impactGlass_heavy_002.ogg", mono=False, used_by="sfx_liquidation")
    place(x, k, 0.78, 0.6)
    rng = np.random.default_rng(3)
    for i in range(18):
        f = rng.uniform(2500, 9000)
        s = ins.fm_bell(f, 0.25, ratio=1.33, index=0.8, decay=rng.uniform(0.05, 0.2))
        place(x, dsp.pan(s, rng.uniform(-0.8, 0.8)), 0.8 + rng.uniform(0, 0.45), 0.12)
    # falling whoosh: bomb-drop whistle + noise sweep down
    n = ns(1.5)
    t = np.arange(n) / SR
    f = np.geomspace(1900, 160, n)
    wh = np.sin(2 * np.pi * np.cumsum(f * (1 + 0.006 * np.sin(2 * np.pi * 7 * t))) / SR)
    wh *= np.clip(t / 0.2, 0, 1) * np.clip((1.5 - t) / 0.3, 0, 1)
    nz = ins.whoosh(1.5, 5000, 250, q=1.6, shape=0.55)
    fall = dsp.pan(wh * 0.35 + nz * 0.8, 0)
    place(x, fall, 1.25, 0.8)
    thud = ins.kick(0.9, f0=38, punch=0.6, click=0.1, decay=0.8)
    place(x, dsp.to_stereo(thud), 2.72, 0.9)
    y = dsp.reverb(x, wet=0.2, decay=1.8, predelay=0.02, damp=4000)
    return trim_tail(y)


@sfx("sfx_rug", "stinger", channels=2, volume_db=-3, desc="Rug pull: carpet zip, then 808 bass drop")
def rug():
    x = canvas(2.9, 2)
    # synth zip: accelerating clicks through a band-pass
    n = ns(0.42)
    t = np.arange(n) / SR
    rate = np.geomspace(35, 160, n)
    ph = np.cumsum(rate) / SR
    clicks = (np.diff(np.floor(ph), prepend=0) > 0).astype(float)
    z = filt(clicks, "bp", 2600, q=1.0) * 3 + filt(noise(n=n), "bp", 4000, q=0.8) * 0.4
    z *= np.clip(t / 0.05, 0, 1) * np.clip((0.42 - t) / 0.05, 0, 1)
    place(x, dsp.pan(z, -0.2), 0.0, 0.5)
    try:
        r = src(F / "555161.ogg", mono=True, used_by="sfx_rug")
        r = r[onset(r):][: ns(0.6)]
        place(x, dsp.pan(dsp.fade(r, 0.001, 0.05), 0.2), 0.02, 0.8 / (np.max(np.abs(r)) + 1e-9))
    except FileNotFoundError:
        pass
    # carpet whoosh as the rug is yanked
    place(x, dsp.pan(ins.whoosh(0.35, 400, 2500, q=1.0, shape=0.8), 0.0), 0.15, 0.45)
    # bass drop
    n = ns(2.2)
    t = np.arange(n) / SR
    f = 34 + (120 - 34) * np.exp(-t / 0.35)
    b = np.sin(2 * np.pi * np.cumsum(f) / SR)
    b = sat(b * 2.5, 2.5) * env_adsr(n, 0.005, 0.3, 0.8, 0.8, gate=1.3)
    place(x, dsp.to_stereo(b), 0.5, 0.9)
    place(x, dsp.to_stereo(ins.kick(0.6, f0=45, punch=1.0, click=0.4)), 0.5, 0.6)
    y = dsp.reverb(x, wet=0.12, decay=1.2, hp=250)
    return trim_tail(y)


@sfx("sfx_graduation", "stinger", channels=2, volume_db=-3, desc="Coin graduation fanfare: synth brass + timpani roll + crash")
def graduation():
    x = canvas(3.6, 2)
    # pickup triplet G4 G4 G4 then C5 chord
    for i in range(3):
        place(x, dsp.pan(ins.brass(mtof(67), 0.13, vel=0.8), -0.1), i * 0.12, 0.5)
    chord = [60, 64, 67, 72]
    for j, m in enumerate(chord):
        place(x, dsp.pan(ins.brass(mtof(m), 1.6, vel=1.0), (j - 1.5) * 0.25), 0.36, 0.38)
    place(x, dsp.pan(ins.brass(mtof(79), 1.6, vel=1.0), 0.1), 0.36, 0.25)
    # timpani roll
    for k in range(10):
        place(x, dsp.pan(ins.tom(mtof(36), 0.5), -0.3), k * 0.035, 0.12 + 0.03 * k)
    place(x, dsp.pan(ins.tom(mtof(36), 1.0), -0.3), 0.36, 0.6)
    place(x, dsp.to_stereo(ins.crash(2.5)) * np.array([0.9, 1.0]), 0.36, 0.25)
    place(x, dsp.pan(ins.sparkle(1.2, 12, 2500, 3), 0.3), 0.4, 0.15)
    y = dsp.reverb(x, wet=0.28, decay=2.0, predelay=0.02)
    return trim_tail(y)


def air_horn(dur: float, bend: float = 0.0) -> np.ndarray:
    n = ns(dur)
    t = np.arange(n) / SR
    benv = 1 + bend * np.clip((t - 0.1) / max(dur - 0.1, 1e-3), 0, 1) ** 2
    x = np.zeros(n)
    for f, a in [(466.0, 1.0), (587.0, 0.8), (698.0, 0.6), (233.0, 0.5)]:
        x += a * osc("saw", f * benv * (1 + 0.004 * np.sin(2 * np.pi * 6 * t)), n=n)
    x = dsp.eq(x, "peak", 1500, 6, q=1.2)
    x = filt(x, "lp", 5000)
    x = sat(x * 1.5, 2.0)
    return x * env_adsr(n, 0.01, 0.05, 1.0, 0.06)


@sfx("sfx_moon_horn", "stinger", channels=2, volume_db=-4, desc="Moon horn: three air-horn blasts, last one bends up")
def moon_horn():
    x = canvas(2.6, 2)
    place(x, dsp.widen(air_horn(0.32), 0.4), 0.0, 0.8)
    place(x, dsp.widen(air_horn(0.14), 0.4), 0.42, 0.8)
    place(x, dsp.widen(air_horn(1.1, bend=0.12), 0.4), 0.62, 0.85)
    try:
        r = src(F / "131930.ogg", mono=True, used_by="sfx_moon_horn")
        r = r[onset(r):][: ns(1.1)]
        place(x, dsp.to_stereo(dsp.fade(r, 0.005, 0.2)), 0.62, 0.35 / (np.max(np.abs(r)) + 1e-9))
    except FileNotFoundError:
        pass
    place(x, dsp.pan(ins.riser(0.9, 400, 6000), 0.0), 1.0, 0.25)
    y = dsp.reverb(x, wet=0.2, decay=1.4)
    return trim_tail(y)


@sfx("sfx_rank_up", "stinger", channels=2, volume_db=-5, desc="Rank up: fast rising chime arpeggio + shimmer chord")
def rank_up():
    x = canvas(2.2, 2)
    notes = [72, 76, 79, 84, 88, 91]
    for i, m in enumerate(notes):
        place(x, dsp.pan(ins.chime(mtof(m), 0.6), -0.5 + i * 0.2), i * 0.055, 0.55)
    for m in [84, 88, 91, 96]:
        place(x, dsp.pan(ins.chime(mtof(m), 1.3, shimmer=0.6), 0), 0.36, 0.3)
    place(x, dsp.widen(ins.sparkle(1.0, 14, 3000, 2.5), 0.8), 0.36, 0.2)
    place(x, ins.pad([mtof(60), mtof(67), mtof(72)], 1.2, attack=0.05, release=0.8, cutoff=3000), 0.33, 0.25)
    y = dsp.reverb(x, wet=0.25, decay=1.8)
    return trim_tail(y)


def church_bell(f: float, dur: float) -> np.ndarray:
    """Additive bell with church-bell partial ratios (hum, prime, tierce, quint, nominal...)."""
    n = ns(dur)
    t = np.arange(n) / SR
    parts = [(0.5, 0.6, dur), (1.0, 0.9, dur * 0.7), (1.19, 0.6, dur * 0.5), (1.5, 0.4, dur * 0.45),
             (2.0, 0.8, dur * 0.4), (2.5, 0.3, dur * 0.25), (3.0, 0.25, dur * 0.2), (4.2, 0.2, dur * 0.12),
             (5.4, 0.12, dur * 0.08)]
    x = np.zeros(n)
    for r, a, d in parts:
        beat = 1 + 0.5 * np.sin(2 * np.pi * (0.7 + r * 0.3) * t) * 0.1
        x += a * np.sin(2 * np.pi * f * r * t + r) * np.exp(-6.9 * t / d) * beat
    strike = filt(noise(n=ns(0.01)), "bp", f * 6, q=1.0) * 0.6
    x[: len(strike)] += strike
    a = ns(0.0015)
    x[:a] *= np.linspace(0, 1, a)
    return x / 3


@sfx("sfx_sunday_unlock", "stinger", channels=2, volume_db=-4, desc="Sunday Unlock bell: two deep bell strikes")
def sunday_unlock():
    x = canvas(6.0, 2)
    f = mtof(55)
    place(x, dsp.pan(church_bell(f, 4.5), -0.15), 0.0, 0.8)
    place(x, dsp.pan(church_bell(f * 0.75, 4.5), 0.15), 1.2, 0.8)
    try:
        r = src(F / "857938.ogg", mono=False, used_by="sfx_sunday_unlock")
        r = r[onset(r):][: ns(4.5)]
        r = dsp.fade(r, 0.002, 1.5)
        g = 0.45 / (np.max(np.abs(r)) + 1e-9)
        place(x, r, 0.0, g)
    except FileNotFoundError:
        pass
    y = dsp.reverb(x, wet=0.3, decay=3.0, predelay=0.03, damp=3500)
    return trim_tail(y, -55)


# ====================================================================== phone

@sfx("sfx_dm_ping", "phone", volume_db=-10, desc="DM received: two soft pings")
def dm_ping():
    x = canvas(0.7)
    place(x, ins.fm_bell(mtof(88), 0.35, ratio=2.0, index=0.8, decay=0.3), 0, 0.8)
    place(x, ins.fm_bell(mtof(95), 0.45, ratio=2.0, index=0.8, decay=0.4), 0.1, 0.9)
    return trim_tail(small_room(x, 0.1))


@sfx("sfx_mention", "phone", volume_db=-9, desc="@mention: three quick rising ticks")
def mention():
    x = canvas(0.7)
    for i, m in enumerate([84, 88, 93]):
        place(x, ins.marimba(mtof(m), 0.25), i * 0.06, 0.7 + 0.1 * i)
    place(x, ins.sparkle(0.3, 3, 4000, 1.5), 0.18, 0.15)
    return trim_tail(small_room(x, 0.1))


@sfx("sfx_call_ring", "phone", volume_db=-6, loop=True, desc="Incoming call ringtone, seamless 3.0 s loop")
def call_ring():
    L = ns(3.0)
    x = np.zeros(L + ns(1.0))
    pattern = [(0.0, 88), (0.14, 91), (0.28, 88), (0.42, 96), (0.70, 88), (0.84, 91), (0.98, 88), (1.12, 96)]
    for t0, m in pattern:
        place(x, ins.marimba(mtof(m), 0.45), t0, 0.8)
        place(x, ins.marimba(mtof(m - 12), 0.45), t0, 0.25)
    x = dsp.loop_wrap(x, L)
    return x  # no trim: exact loop length


@sfx("sfx_call_connect", "phone", volume_db=-10, desc="Call connected: rising bloop + chime")
def call_connect():
    x = canvas(0.6)
    n = ns(0.12)
    f = np.geomspace(500, 1100, n)
    b = np.sin(2 * np.pi * np.cumsum(f) / SR) * env_adsr(n, 0.005, 0.05, 0.6, 0.04)
    place(x, b, 0, 0.7)
    place(x, ins.chime(mtof(91), 0.4), 0.1, 0.8)
    return trim_tail(small_room(x, 0.08))


@sfx("sfx_call_end", "phone", volume_db=-10, desc="Call ended: three falling beeps")
def call_end():
    x = canvas(0.6)
    for i, m in enumerate([81, 77, 74]):
        n = ns(0.09)
        t = np.arange(n) / SR
        b = (np.sin(2 * np.pi * mtof(m) * t) + 0.2 * np.sin(4 * np.pi * mtof(m) * t)) * env_adsr(n, 0.004, 0.02, 0.8, 0.02)
        place(x, b, i * 0.13, 0.8)
    return trim_tail(x)


@sfx("sfx_space_started", "phone", volume_db=-8, desc="A Space went live: pad swell into an on-air chime")
def space_started():
    x = canvas(2.0)
    p = ins.pad([mtof(60), mtof(67), mtof(71), mtof(76)], 1.4, attack=0.5, release=0.6, cutoff=1800)
    place(x, p.mean(axis=1), 0, 0.5)
    place(x, ins.chime(mtof(84), 0.9), 0.48, 0.6)
    place(x, ins.chime(mtof(91), 1.0), 0.48, 0.5)
    place(x, ins.sparkle(0.8, 8, 3000, 2), 0.5, 0.12)
    return trim_tail(small_room(x, 0.2, 1.2))


# ====================================================================== voice rooms

@sfx("sfx_vr_join", "voice_room", volume_db=-12, desc="Someone joined the room: rising bloop")
def vr_join():
    x = canvas(0.5)
    n = ns(0.13)
    f = np.geomspace(300, 900, n)
    b = np.sin(2 * np.pi * np.cumsum(f) / SR) * env_adsr(n, 0.004, 0.05, 0.6, 0.05)
    place(x, b, 0, 0.8)
    place(x, ins.marimba(mtof(88), 0.3), 0.09, 0.5)
    return trim_tail(small_room(x, 0.08))


@sfx("sfx_vr_leave", "voice_room", volume_db=-12, desc="Someone left the room: falling bloop")
def vr_leave():
    x = canvas(0.5)
    place(x, ins.marimba(mtof(84), 0.25), 0, 0.45)
    n = ns(0.15)
    f = np.geomspace(900, 280, n)
    b = np.sin(2 * np.pi * np.cumsum(f) / SR) * env_adsr(n, 0.004, 0.05, 0.6, 0.06)
    place(x, b, 0.05, 0.8)
    return trim_tail(small_room(x, 0.08))


@sfx("sfx_vr_raise_hand", "voice_room", volume_db=-10, desc="Hand raised: pop + two-note ping")
def vr_raise_hand():
    x = canvas(0.6)
    n = ns(0.04)
    f = np.geomspace(500, 1300, n)
    place(x, np.sin(2 * np.pi * np.cumsum(f) / SR) * env_exp(n, 0.035), 0, 0.7)
    place(x, ins.chime(mtof(91), 0.3), 0.04, 0.6)
    place(x, ins.chime(mtof(96), 0.35), 0.11, 0.6)
    return trim_tail(small_room(x, 0.08))


@sfx("sfx_vr_promoted", "voice_room", volume_db=-8, desc="Promoted to speaker: little three-note fanfare")
def vr_promoted():
    x = canvas(1.3)
    for i, m in enumerate([79, 84, 88]):
        place(x, ins.chime(mtof(m), 0.5), i * 0.08, 0.6)
    place(x, ins.chime(mtof(91), 0.9, shimmer=0.6), 0.24, 0.8)
    place(x, ins.sparkle(0.6, 8, 3000, 2.2), 0.26, 0.15)
    return trim_tail(small_room(x, 0.15, 0.9))


# ====================================================================== ORE refinery

def _furnace(seed: int, dur: float = 2.2):
    dsp.seed(seed)
    n = ns(dur)
    t = np.arange(n) / SR
    swell = np.clip(t / 0.35, 0, 1) ** 1.5 * np.exp(-np.clip(t - 0.6, 0, None) / 0.5)
    fc = 300 + 3200 * np.clip(t / 0.35, 0, 1) * np.exp(-np.clip(t - 0.35, 0, None) / 0.6)
    roar = dsp.sweep_filter(noise(n=n, color="pink") * 2, "lp", fc, q=0.9) * swell
    rumble = filt(noise(n=n, color="brown"), "lp", 120) * swell * 1.2
    rng = np.random.default_rng(seed)
    crack = np.zeros(n)
    k = int(dur * 40)
    pos = rng.integers(0, n, k)
    crack[pos] = rng.uniform(-1, 1, k) * swell[pos]
    crack = filt(crack, "hp", 2000) * 2.0
    x = roar + rumble + crack * 0.4
    try:
        r = src(F / ("447941.ogg" if seed % 2 else "459884.ogg"), mono=True, used_by="sfx_ore_furnace_whoosh")
        r = r[onset(r):][:n]
        r = dsp.fade(r, 0.005, 0.5)
        x = x + np.pad(r, (0, n - len(r))) * 0.6 * np.max(np.abs(x)) / (np.max(np.abs(r)) + 1e-9)
    except FileNotFoundError:
        pass
    return trim_tail(x)


@sfx("sfx_ore_furnace_whoosh", "ore", volume_db=-6, desc="Refinery furnace flares: fire whoosh + rumble (2 variations)")
def ore_furnace_whoosh():
    return [_furnace(11), _furnace(12, 2.6)]


@sfx("sfx_ore_clink", "ore", volume_db=-10, desc="Ore nuggets clink (5 variations, synth + Kenney impactMining layer)")
def ore_clink():
    outs = []
    for v in range(5):
        rng = np.random.default_rng(100 + v)
        x = canvas(0.5)
        hits = rng.integers(2, 4)
        t0 = 0.0
        for h in range(hits):
            n = ns(0.25)
            tt = np.arange(n) / SR
            base = rng.uniform(2200, 3600)
            y = np.zeros(n)
            for r, a, d in [(1, 1, 0.09), (1.83, 0.6, 0.06), (2.71, 0.4, 0.04), (3.9, 0.25, 0.03)]:
                y += a * np.sin(2 * np.pi * base * r * rng.uniform(0.98, 1.02) * tt) * np.exp(-6.9 * tt / d)
            y += filt(noise(n=n), "bp", 1500, q=1.2) * np.exp(-6.9 * tt / 0.015) * 0.6
            place(x, y, t0, 0.8 / (h + 1) ** 0.5)
            t0 += rng.uniform(0.03, 0.08)
        k = src(K / f"impact-sounds/Audio/impactMining_00{v}.ogg", used_by="sfx_ore_clink")
        place(x, k[: len(x)], 0, 0.35)
        outs.append(trim_tail(x))
    return outs


# ====================================================================== main

def main(only: list[str] | None = None) -> dict:
    report = {}
    for id_, meta in REGISTRY.items():
        if only and id_ not in only:
            continue
        res = meta["fn"]()
        files = []
        variants = res if isinstance(res, list) else [res]
        for i, x in enumerate(variants):
            name = f"{id_}.wav" if len(variants) == 1 else f"{id_}_{i + 1:02d}.wav"
            info = export_wav(SFX / name, x, peak_db=-1.0, channels=meta["channels"], loop=meta["loop"])
            files.append(info)
        report[id_] = {k: v for k, v in meta.items() if k != "fn"} | {"files": files, "sources": sorted(set(SOURCES.get(id_, [])))}
        print(f"{id_:28} {len(variants)} file(s) {len(variants[0]) / SR:5.2f}s")
    BUILD_REPORT.parent.mkdir(exist_ok=True, parents=True)
    old = json.loads(BUILD_REPORT.read_text()) if BUILD_REPORT.exists() and only else {}
    old.update(report)
    BUILD_REPORT.write_text(json.dumps(old, indent=2))
    return report


BUILD_REPORT = Path(__file__).with_name("build_sfx.json")

if __name__ == "__main__":
    main(sys.argv[1:] or None)
