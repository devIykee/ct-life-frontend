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

@sfx("sfx_buy", "trading", volume_db=-8, desc="Buy filled: terminal up-ping (fifth up) + key tick + coin glint")
def buy():
    """CT terminals answer a fill instantly; keep it short (< 0.45 s) so rapid aping never fatigues.
    Research: docs/progress/audio-integration.md (trading-terminal feedback)."""
    x = canvas(0.6)
    place(x, ins.tick(0.012, 5200), 0.0, 0.35)
    place(x, ins.terminal_ping(mtof(88), 0.18), 0.0, 0.8)          # E6
    place(x, ins.terminal_ping(mtof(95), 0.32), 0.045, 0.9)        # B6, a fifth up
    place(x, ins.coin(3100, 0.3), 0.06, 0.18)
    return trim_tail(small_room(x, 0.08, 0.5))


@sfx("sfx_sell", "trading", volume_db=-8, desc="Sell filled: terminal down-ping (fourth down) + short paper swipe")
def sell():
    x = canvas(0.6)
    place(x, ins.whoosh(0.12, 2600, 900, q=1.2, shape=0.3), 0, 0.25)
    place(x, ins.terminal_ping(mtof(95), 0.16, bright=0.8), 0.02, 0.75)   # B6
    place(x, ins.terminal_ping(mtof(90), 0.3, bright=0.6), 0.07, 0.85)    # F#6, a fourth down
    return trim_tail(small_room(x, 0.08, 0.5))


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


@sfx("sfx_pnl_card_flip", "trading", volume_db=-1, desc="PnL card flip: swish + card snap (Kenney card-place-1)")
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


@sfx("sfx_graduation", "stinger", channels=2, volume_db=-3,
     desc="Coin graduation: bonding curve fills to 100% (accelerating rising ticks + riser), brass hit, migration whoosh L->R")
def graduation():
    """Launchpad coins 'graduate' when the bonding curve completes and migrate to a DEX (Wikipedia "Pump.fun":
    graduation at a market-cap threshold). The cue tells that story: fill, hit, move."""
    x = canvas(3.6, 2)
    scale = [72, 74, 76, 79, 81, 84, 86, 88, 91, 93, 96, 98]
    t0, gap = 0.0, 0.11
    for i, m in enumerate(scale):
        place(x, dsp.pan(ins.marimba(mtof(m), 0.18), -0.6 + i * 0.1), t0, 0.32 + 0.03 * i)
        t0 += gap
        gap *= 0.86
    fill_end = t0
    place(x, dsp.pan(ins.riser(fill_end + 0.05, 300, 7000), 0.0), 0.0, 0.22)
    chord = [60, 64, 67, 72]
    for j, m in enumerate(chord):
        place(x, dsp.pan(ins.brass(mtof(m), 1.4, vel=1.0), (j - 1.5) * 0.25), fill_end, 0.36)
    place(x, dsp.pan(ins.brass(mtof(79), 1.4, vel=1.0), 0.1), fill_end, 0.24)
    place(x, dsp.to_stereo(ins.kick(0.7, f0=42, punch=0.9, click=0.3)), fill_end, 0.5)
    place(x, dsp.to_stereo(ins.crash(2.2)) * np.array([0.9, 1.0]), fill_end, 0.22)
    # migration: a whoosh that travels left -> right (the coin moving to the DEX)
    w = ins.whoosh(0.7, 500, 6000, q=1.3, shape=0.6)
    pan = np.linspace(-0.9, 0.9, len(w))
    move = np.stack([w * np.sqrt((1 - pan) / 2), w * np.sqrt((1 + pan) / 2)], axis=1)
    place(x, move, fill_end + 0.25, 0.45)
    place(x, dsp.widen(ins.sparkle(1.0, 12, 2500, 3), 0.7), fill_end + 0.3, 0.15)
    y = dsp.reverb(x, wet=0.24, decay=1.8, predelay=0.02)
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


def electric_bell(f: float, dur: float, rate: float = 18.0) -> np.ndarray:
    """Trading-floor opening bell: a small brass bell struck rapidly by an electric clapper."""
    n = ns(dur + 1.2)
    out = np.zeros(n)
    k = 0
    t = 0.0
    while t < dur:
        hit = church_bell(f, 0.9) * (0.75 + 0.25 * ((k % 2) * 2 - 1) * 0.3)
        s = ns(t)
        e = min(n, s + len(hit))
        out[s:e] += hit[: e - s] * 0.5
        t += 1.0 / rate
        k += 1
    return out


@sfx("sfx_sunday_unlock", "stinger", channels=2, volume_db=-4,
     desc="Sunday Unlock: trading-floor opening bell (electric clapper), padlock unlock clack, one deep bell strike")
def sunday_unlock():
    """Spec: 'The Sunday Unlock bell'. CT reads 'unlock' as team/VC tokens becoming sellable (research dossier
    docs/research/ct-now-2026-10.md); the exchange opening-bell ritual makes it a market event, the clack an unlock."""
    x = canvas(5.5, 2)
    place(x, dsp.pan(electric_bell(1046.5, 1.3), -0.2), 0.0, 0.55)
    place(x, dsp.pan(electric_bell(1318.5, 1.3, rate=17.0), 0.2), 0.03, 0.35)
    clack = dsp.layer(ins.rim(0.05, 1200) * 0.8, ins.tick(0.02, 3000) * 0.8)
    place(x, dsp.pan(clack, 0.0), 1.45, 0.7)
    place(x, dsp.pan(ins.fm_bell(2400, 0.4, ratio=1.41, index=1.0, decay=0.3), 0.1), 1.5, 0.25)
    place(x, dsp.pan(church_bell(mtof(43), 3.8), 0.0), 1.6, 0.85)
    y = dsp.reverb(x, wet=0.28, decay=2.6, predelay=0.03, damp=3800)
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

# ====================================================================== CT-native cues (client-only ids, 2026-10-08)

@sfx("sfx_news_flash", "news", volume_db=-13,
     desc="JUST IN / BREAKING wire alert: two-tone siren chirp + teletype ticks (news-bot posts are CT's heartbeat)")
def news_flash():
    """docs/research/ct-now-2026-10.md 1b/2b: liquidation and 'JUST IN:' news-bot posts every 1-3 days; 'BREAKING:' with
    a siren emoji. A short wire alert for incident cards and the Town Crier."""
    x = canvas(1.0)
    for i, f in enumerate([1180, 880, 1180, 880]):
        n = ns(0.075)
        t = np.arange(n) / SR
        b = (0.7 * osc("square", f, n=n) + 0.3 * np.sin(2 * np.pi * f * t)) * env_adsr(n, 0.003, 0.02, 0.8, 0.02)
        place(x, filt(b, "lp", 3500), i * 0.075, 0.45)
    for k in range(7):
        place(x, ins.tick(0.012, 4800 + 300 * (k % 3)), 0.36 + k * 0.055, 0.5)
    return trim_tail(small_room(x, 0.06, 0.4))


@sfx("sfx_gm", "social", volume_db=-14, desc="gm ritual: soft sunrise pentatonic chime when a room says 'gm'")
def gm():
    """The CT/NFT 'gm' greeting ritual (Know Your Meme 'GM': NFT Twitter from 2018; gm/gn/wagmi replies). Played
    quietly, at most once per 12 s per client, when someone in the venue says gm."""
    x = canvas(1.2)
    for i, m in enumerate([79, 83, 86, 88]):          # G5 B5 D6 E6
        place(x, ins.marimba(mtof(m), 0.45), i * 0.07, 0.5 + 0.08 * i)
    p = ins.pad([mtof(67), mtof(71), mtof(74)], 0.9, attack=0.15, release=0.5, cutoff=2400)
    place(x, p.mean(axis=1), 0.05, 0.22)
    return trim_tail(small_room(x, 0.18, 0.9))


@sfx("sfx_cash_in", "trading", volume_db=-7,
     desc="Cash credited (top-up confirmed): money-spray flutter + register ka-ching + talking-drum glide")
def cash_in():
    """Owambe 'spraying' (Wikipedia 'Owambe': guests throw notes to celebrate) plus a dundun talking-drum glide
    (Wikipedia 'Talking drum') for the Nigerian flavour the user asked for; own synthesis only."""
    x = canvas(1.4)
    rng = np.random.default_rng(42)
    for k in range(11):
        place(x, ins.paper_flutter(rng.uniform(0.06, 0.11), seed=k), 0.0 + k * 0.045 + rng.uniform(0, 0.02), 0.35)
    for i, f in enumerate([2700, 3300, 2500]):
        place(x, ins.coin(f, 0.4), 0.16 + i * 0.03, 0.4)
    place(x, ins.fm_bell(mtof(96), 0.9, ratio=1.41, index=2.0, decay=0.8), 0.2, 0.55)
    place(x, ins.talking_drum(170, 0.32, glide=1.5, squeeze_at=0.03, release_at=0.18), 0.42, 0.55)
    place(x, ins.talking_drum(205, 0.28, glide=1.35, squeeze_at=0.02), 0.62, 0.45)
    return trim_tail(small_room(x, 0.12, 0.7))


@sfx("sfx_rekt_phonk", "stinger", channels=2, volume_db=-1,
     desc="REKT replay tag: 2-bar phonk (pitched cowbell riff, sliding 808, triplet hats) for the shared REKT clip")
def rekt_phonk():
    """docs/research/ct-now-2026-10.md 4d: liquidation clips are posted as phonk / slowed edits ('+$40k -> liquidated').
    Own synthesis: no sampled phonk, no vocal chops."""
    bpm = 130.0
    b = 60.0 / bpm
    x = canvas(2 * 4 * b + 1.0, 2)
    riff = [(0, 70), (0.5, 70), (0.75, 73), (1.5, 70), (2, 68), (2.5, 66), (3, 65), (3.5, 63)]   # Bb minor-ish, falling
    for bar in range(2):
        for beat, m in riff:
            place(x, dsp.pan(ins.cowbell(mtof(m) * 1.0, 0.3, tone=1.0), 0.15), (bar * 4 + beat) * b, 0.32)
        place(x, dsp.to_stereo(ins.kick(0.45, f0=48, punch=0.9, click=0.2, drive=1.6)), (bar * 4) * b, 0.7)
        place(x, dsp.to_stereo(ins.kick(0.45, f0=48, punch=0.9, click=0.2, drive=1.6)), (bar * 4 + 2.5) * b, 0.6)
        place(x, dsp.to_stereo(ins.bass_808(mtof(34), 4 * b * 0.95, glide_from=mtof(41), drive=2.0, decay=1.6)), bar * 4 * b, 0.55)
        place(x, dsp.pan(ins.clap(0.25, 0.12), 0.0), (bar * 4 + 1) * b, 0.5)
        place(x, dsp.pan(ins.clap(0.25, 0.12), 0.0), (bar * 4 + 3) * b, 0.5)
        for k in range(12):                                  # triplet hat roll
            place(x, dsp.pan(ins.hat(0.04, tone=9000), 0.3), (bar * 4 + k / 3) * b, 0.18 if k % 3 else 0.26)
    y = dsp.reverb(x, wet=0.16, decay=1.4)
    return trim_tail(y)


# ====================================================================== onboarding (main-theme quotes, 2026-10-09)
# Key G major like mus_title (tools/theme.py), so a stinger over the ducked title loop stays in key.

def _theme_band(x, t0: float, notes, voice, gain: float, pan: float = 0.0, spb: float = 60.0 / 112.0):
    import theme as th  # noqa: F401  (documents the shared source of the motif)
    for beat, m, d in notes:
        place(x, dsp.pan(voice(mtof(m), max(0.12, d * spb)), pan), t0 + beat * spb, gain)


@sfx("sfx_origin_insider", "stinger", channels=2, volume_db=-3,
     desc="Origin reveal: Insider (also Seeker). Snare roll into a brass fanfare of the main-theme hook, G major hit, crash, glock sparkle")
def origin_insider():
    import theme as th
    x = canvas(3.4, 2)
    spb = 60.0 / th.BPM
    for k in range(10):                                   # snare/rim roll, crescendo
        place(x, dsp.pan(ins.snare(0.12, tone=210, snappy=0.8, decay=0.06), 0.1), k * 0.035, 0.08 + 0.025 * k)
        place(x, dsp.pan(ins.tom(98, 0.25), -0.3), k * 0.035, 0.05 + 0.02 * k)
    t0 = 0.36
    fan = [(0, th.D5, 0.35), (0.33, th.G5, 0.35), (0.66, th.B5, 0.4)]   # quick pickup of the hook
    for beat, m, d in fan:
        place(x, dsp.pan(ins.brass(mtof(m), d, vel=0.9), -0.1), t0 + beat * spb, 0.42)
        place(x, dsp.pan(ins.brass(mtof(m - 12), d, vel=0.8), 0.15), t0 + beat * spb, 0.22)
    hit = t0 + 1.0 * spb
    for j, m in enumerate([th.G4, th.B4, th.D5, th.G5, th.D6]):           # G major brass chord
        place(x, dsp.pan(ins.brass(mtof(m), 1.5, vel=1.0), (j - 2) * 0.22), hit, 0.30)
    place(x, dsp.to_stereo(ins.kick(0.7, f0=45, punch=0.9, click=0.3)), hit, 0.55)
    place(x, dsp.to_stereo(ins.crash(2.2)) * np.array([0.9, 1.0]), hit, 0.22)
    for i, m in enumerate([th.G5, th.B5, th.D6, th.G6, th.B6]):           # glock run up
        place(x, dsp.pan(ins.glock(mtof(m), 0.8), 0.35), hit + 0.08 + i * 0.06, 0.28)
    place(x, dsp.widen(ins.sparkle(1.1, 12, 2600, 3.0), 0.7), hit + 0.1, 0.14)
    y = dsp.reverb(x, wet=0.24, decay=1.8, predelay=0.02)
    return trim_tail(y)


@sfx("sfx_origin_exit_liquidity", "stinger", channels=2, volume_db=-4,
     desc="Origin reveal: Exit Liquidity. Cartoon bonk, a short slide-whistle deflate, then a hopeful ukulele + glock quote of the hook")
def origin_exit_liquidity():
    """Comic, never punishing: the bonk lands the joke, the rising tag says 'you can still make it'."""
    import theme as th
    x = canvas(3.0, 2)
    place(x, dsp.pan(ins.bonk(0.5), 0.0), 0.0, 0.85)
    place(x, dsp.pan(ins.slide_whistle(1250, 420, 0.42), 0.15), 0.26, 0.22)
    t0 = 1.0
    spb = 60.0 / th.BPM
    # hopeful tag: G major strum, then the hook's opening rising to D6
    place(x, dsp.pan(ins.strum([mtof(n) for n in (67, 71, 74, 79)], 1.2), -0.25), t0, 0.55)
    tag = [(0, th.D5, 0.5), (0.5, th.G5, 0.5), (1, th.A5, 0.5), (1.5, th.B5, 0.5), (2, th.D6, 1.5)]
    for beat, m, d in tag:
        place(x, dsp.pan(ins.marimba(mtof(m), max(0.3, d * spb + 0.2)), 0.1), t0 + 0.1 + beat * spb, 0.5)
    place(x, dsp.pan(ins.glock(mtof(th.D6 + 12), 1.0), 0.35), t0 + 0.1 + 2 * spb, 0.3)
    place(x, dsp.pan(ins.strum([mtof(n) for n in (67, 71, 74, 79)], 1.0, up=True), -0.25), t0 + 0.1 + 2 * spb, 0.4)
    y = dsp.reverb(x, wet=0.18, decay=1.4, predelay=0.015)
    return trim_tail(y)


@sfx("sfx_creator_complete", "stinger", channels=2, volume_db=-3,
     desc="Character creation done: the full main-theme hook on marimba + brass, ukulele strums, two claps, ba-dum-tss, G major finish with a glock run")
def creator_complete():
    import theme as th
    x = canvas(3.4, 2)
    spb = 60.0 / th.BPM * 0.5                           # the hook at double speed: the fanfare lands at ~1.2 s
    for beat, m, d in th.HOOK[:6] + [(4, th.B5, 0.5), (4.5, th.D6, 1.5)]:
        place(x, dsp.pan(ins.marimba(mtof(m), max(0.3, d * spb + 0.2)), 0.1), beat * spb, 0.5)
        place(x, dsp.pan(ins.brass(mtof(m), max(0.12, d * spb), vel=0.7), -0.15), beat * spb, 0.16)
    for beat, up in ((0, False), (1, False), (1.5, True), (2.5, True), (3, False), (3.5, True)):
        place(x, dsp.pan(ins.strum([mtof(n) for n in (67, 71, 74, 79)], 0.6, up=up), -0.3), beat * spb, 0.32 if not up else 0.22)
    end = 4.5 * spb
    for k, t in enumerate((2 * spb, 3 * spb)):
        place(x, dsp.pan(ins.clap(0.25, 0.1), 0.0), t, 0.42)
    place(x, dsp.pan(ins.snare(0.15, tone=200, decay=0.08), 0.0), end - 0.2, 0.35)
    place(x, dsp.to_stereo(ins.kick(0.6, f0=48, punch=0.9, click=0.3)), end - 0.07, 0.5)
    place(x, dsp.to_stereo(ins.crash(2.4)) * np.array([1.0, 0.9]), end, 0.24)
    for j, m in enumerate([th.G4, th.B4, th.D5, th.G5]):
        place(x, dsp.pan(ins.brass(mtof(m), 1.1, vel=0.95), (j - 1.5) * 0.25), end, 0.26)
        place(x, dsp.pan(ins.epiano(mtof(m), 1.3, vel=0.7), 0.2), end, 0.12)
    for i, m in enumerate([th.G5, th.B5, th.D6, th.G6, th.B6]):
        place(x, dsp.pan(ins.glock(mtof(m), 0.9), 0.35), end + 0.1 + i * 0.065, 0.26)
    place(x, dsp.widen(ins.sparkle(1.2, 14, 2600, 3.0), 0.8), end + 0.1, 0.14)
    y = dsp.reverb(x, wet=0.22, decay=1.8, predelay=0.02)
    return trim_tail(y)


@sfx("sfx_ui_next", "ui", volume_db=-5,
     desc="Onboarding step complete: rising two-note chime (D6 to G6, the main theme's key), glock + marimba")
def ui_next():
    """Onboarding spec (docs/research/duolingo-design-teardown.md C4): 'Step complete: rising 2-note chime'.
    Bright enough to sit in the 1-6 kHz band over mus_title (tools/mix_audit.py)."""
    import theme as th
    x = canvas(0.7)
    for i, m in enumerate((th.D6, th.G6)):
        place(x, ins.glock(mtof(m), 0.45), i * 0.085, 0.7)
        place(x, ins.marimba(mtof(m - 12), 0.35), i * 0.085, 0.45)
    return trim_tail(small_room(x, 0.08, 0.4))


@sfx("sfx_card_shuffle", "card", volume_db=-1,
     desc="Origin draw: two quick card riffles (Kenney casino-audio card-shuffle, first 1.15 s)")
def card_shuffle():
    """Onboarding spec C 'Screen 1/2': the cards gather and deal twice (~1 s). Kenney casino-audio, CC0."""
    r = src(K / "casino-audio/Audio/card-shuffle.ogg", used_by="sfx_card_shuffle")
    r = r[onset(r, -40):][: ns(1.15)]
    # The riffle is many tiny paper clicks with a 30 dB crest factor: tame the spikes (5 ms look-ahead limiter, 14 dB)
    # so it reads as a riffle over the title music instead of a few faint ticks.
    from gen_music import limiter_gain
    peak = 20 * np.log10(np.max(np.abs(r)) + 1e-12)
    r = r * limiter_gain(r, peak - 16.0)
    r = dsp.eq(r, "peak", 3500, 3.0, q=0.9)
    return dsp.fade(r, 0.002, 0.12)


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
