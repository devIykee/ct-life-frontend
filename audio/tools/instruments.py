"""Synth voices shared by the SFX and music generators. All mono unless noted."""
from __future__ import annotations

import numpy as np
from scipy import signal

import dsp
from dsp import SR, ns, mtof, osc, noise, env_adsr, env_exp, filt, sat


# ================================================================ tonal

def fm_bell(freq: float, dur: float, ratio: float = 3.5, index: float = 3.0, decay: float | None = None,
            idx_decay: float | None = None, bright: float = 1.0) -> np.ndarray:
    """Two-operator FM bell / chime."""
    n = ns(dur)
    t = np.arange(n) / SR
    decay = decay or dur
    idx_decay = idx_decay or decay * 0.35
    idx = index * bright * np.exp(-6.9 * t / idx_decay)
    mod = np.sin(2 * np.pi * freq * ratio * t) * idx
    car = np.sin(2 * np.pi * freq * t + mod)
    return car * env_exp(n, decay, attack=0.001)


def chime(freq: float, dur: float = 0.6, decay: float | None = None, shimmer: float = 0.35) -> np.ndarray:
    """Clean glassy chime: FM bell + octave partial + a touch of 2.76x inharmonic sparkle."""
    decay = decay or dur
    x = fm_bell(freq, dur, ratio=2.0, index=1.2, decay=decay, idx_decay=decay * 0.2)
    x += 0.35 * fm_bell(freq * 2, dur, ratio=1.0, index=0.5, decay=decay * 0.6)
    x += shimmer * 0.25 * fm_bell(freq * 2.756, dur, ratio=1.0, index=0.2, decay=decay * 0.25)
    return x / 1.6


def marimba(freq: float, dur: float = 0.5) -> np.ndarray:
    n = ns(dur)
    t = np.arange(n) / SR
    x = np.sin(2 * np.pi * freq * t) * np.exp(-6.9 * t / (dur * 0.8))
    x += 0.4 * np.sin(2 * np.pi * freq * 4.0 * t) * np.exp(-6.9 * t / (dur * 0.12))
    x += 0.15 * np.sin(2 * np.pi * freq * 9.2 * t) * np.exp(-6.9 * t / 0.03)
    a = ns(0.0015)
    x[:a] *= np.linspace(0, 1, a)
    return x / 1.3


def epiano(freq: float, dur: float, vel: float = 0.8, gate: float | None = None) -> np.ndarray:
    """Rhodes-ish FM electric piano (carrier 1:1, tine at 14x)."""
    n = ns(dur)
    t = np.arange(n) / SR
    idx = (1.6 * vel) * np.exp(-t / 0.35) + 0.25
    mod = np.sin(2 * np.pi * freq * t) * idx
    body = np.sin(2 * np.pi * freq * t + mod)
    tine = np.sin(2 * np.pi * freq * 14.0 * t) * np.exp(-t / 0.018) * 0.22 * vel
    g = gate if gate is not None else dur * 0.85
    e = env_adsr(n, 0.002, 1.2, 0.35, max(0.08, dur - g), gate=g)
    e *= np.exp(-t / 2.5)
    x = (body + tine) * e
    # gentle tremolo
    x *= 1 + 0.08 * np.sin(2 * np.pi * 4.5 * t)
    return x * (0.6 + 0.4 * vel)


def piano_ish(freq: float, dur: float, vel: float = 0.8) -> np.ndarray:
    """Bright house piano: stacked partials with detune and hammer click."""
    n = ns(dur)
    t = np.arange(n) / SR
    x = np.zeros(n)
    for k, (a, d) in enumerate([(1, 1.2), (0.55, 0.8), (0.35, 0.5), (0.22, 0.35), (0.14, 0.25), (0.09, 0.18)], start=1):
        inh = 1 + 0.0004 * k * k
        x += a * np.sin(2 * np.pi * freq * k * inh * t) * np.exp(-t / d)
        x += a * 0.5 * np.sin(2 * np.pi * freq * k * inh * 1.0015 * t) * np.exp(-t / d)
    click = filt(noise(n=ns(0.006)), "bp", 3000, q=1.0) * 0.4
    x[: len(click)] += click
    r = ns(0.05)
    x[-r:] *= np.linspace(1, 0, r)
    a = ns(0.002)
    x[:a] *= np.linspace(0, 1, a)
    return x / 2.2 * vel


def supersaw(freq: float, dur: float, voices: int = 5, detune: float = 0.12, kind: str = "saw") -> np.ndarray:
    n = ns(dur)
    x = np.zeros(n)
    rng = np.random.default_rng(int(freq * 100) % 9999)
    for v in range(voices):
        d = (v - (voices - 1) / 2) / max(1, (voices - 1) / 2) * detune
        x += osc(kind, freq * 2 ** (d / 12), n=n, phase0=rng.random())
    return x / np.sqrt(voices)


def pad(freqs: list[float], dur: float, attack: float = 0.6, release: float = 0.8, cutoff: float = 2200.0,
        detune: float = 0.1, voices: int = 4, kind: str = "saw", stereo: bool = True) -> np.ndarray:
    """Warm detuned-saw pad chord, stereo, low-passed."""
    n = ns(dur)
    L = np.zeros(n)
    R = np.zeros(n)
    for i, f in enumerate(freqs):
        a = supersaw(f, dur, voices=voices, detune=detune, kind=kind)
        b = supersaw(f * 1.0008, dur, voices=voices, detune=detune * 1.1, kind=kind)
        L += a
        R += b
    e = env_adsr(n, attack, 0.3, 0.85, release)
    out = np.stack([L, R], axis=1) * e[:, None] / max(1, len(freqs)) ** 0.7
    out = filt(out, "lp", cutoff, order=2)
    out = filt(out, "hp", 90)
    return out if stereo else out.mean(axis=1)


def organ(freq: float, dur: float, drawbars=(0.8, 0.6, 0.4, 0.2, 0.1)) -> np.ndarray:
    n = ns(dur)
    t = np.arange(n) / SR
    x = np.zeros(n)
    for k, a in zip([0.5, 1, 2, 3, 4], drawbars):
        x += a * np.sin(2 * np.pi * freq * k * t)
    e = env_adsr(n, 0.01, 0.05, 0.9, 0.08)
    return x * e / 2.0


def pluck_ks(freq: float, dur: float, bright: float = 0.6, decay: float = 0.996, pick: float = 0.2) -> np.ndarray:
    """Karplus-Strong plucked string (block-vectorised). Good for banjo/guitar."""
    n = ns(dur)
    L = max(2, int(round(SR / freq)))
    rng = np.random.default_rng(int(freq * 10))
    burst = rng.uniform(-1, 1, L)
    # pick position comb + brightness filter on the excitation
    p = max(1, int(L * pick))
    burst = burst - np.r_[np.zeros(p), burst[:-p]]
    burst = signal.lfilter([bright], [1, -(1 - bright)], burst)
    out = np.zeros(n + L + 1)
    out[:L] = burst
    i = L
    while i < n:
        k = min(L, n - i)
        prev = out[i - L:i - L + k + 1]
        out[i:i + k] = decay * 0.5 * (prev[:-1] + prev[1:]) if len(prev) == k + 1 else decay * prev[:k]
        i += k
    y = out[:n]
    y -= np.mean(y)
    a = ns(0.001)
    y[:a] *= np.linspace(0, 1, a)
    r = ns(0.02)
    y[-r:] *= np.linspace(1, 0, r)
    m = np.max(np.abs(y)) or 1
    return y / m


def banjo(freq: float, dur: float = 0.6) -> np.ndarray:
    """Bright, short-sustain KS pluck with a drum-head resonance (banjo-ish)."""
    x = pluck_ks(freq, dur, bright=0.85, decay=0.990, pick=0.12)
    x = dsp.eq(x, "peak", 2500, 6, q=1.2)
    x = dsp.eq(x, "peak", 400, 3, q=2.0)
    x = filt(x, "hp", 120)
    x *= env_exp(len(x), dur * 1.6, attack=0.0005)
    return x


def brass(freq: float, dur: float, vel: float = 0.9, vib: float = 0.15) -> np.ndarray:
    """Synth brass: saw stack with a filter that opens on the attack (+ vibrato)."""
    n = ns(dur)
    t = np.arange(n) / SR
    vibr = 1 + (vib / 100) * np.sin(2 * np.pi * 5.5 * t) * np.clip((t - 0.25) / 0.3, 0, 1)
    f = freq * vibr * (1 - 0.01 * np.exp(-t / 0.03))
    x = 0.6 * osc("saw", f, n=n) + 0.4 * osc("saw", f * 1.003, n=n) + 0.2 * osc("pulse", f * 0.5, n=n, pw=0.4)
    fe = env_adsr(n, 0.04, 0.25, 0.55, 0.15)
    fc = freq * (1.2 + 7 * vel * fe)
    y = dsp.sweep_filter(x, "lp", fc, q=1.1, block=64)
    return y * env_adsr(n, 0.03, 0.2, 0.85, min(0.2, dur * 0.3))


def lead_square(freq, dur: float, cutoff: float = 3000.0, pw: float = 0.35, vib: float = 0.2,
                attack: float = 0.008, release: float = 0.08) -> np.ndarray:
    n = ns(dur)
    t = np.arange(n) / SR
    f = np.asarray(freq, dtype=float) * (1 + (vib / 100) * np.sin(2 * np.pi * 5.8 * t) * np.clip((t - 0.15) / 0.2, 0, 1))
    x = osc("pulse", f, n=n, pw=pw) * 0.7 + osc("saw", f * 1.004, n=n) * 0.3
    x = filt(x, "lp", cutoff)
    return x * env_adsr(n, attack, 0.15, 0.7, release)


def acid(freq: float, dur: float, accent: bool = False, cutoff: float = 500.0, env_amt: float = 2500.0,
         res: float = 0.75, slide_from: float | None = None) -> np.ndarray:
    """303-style acid line voice."""
    n = ns(dur)
    t = np.arange(n) / SR
    f = np.full(n, freq)
    if slide_from:
        k = min(n, ns(0.06))
        f[:k] = np.geomspace(slide_from, freq, k)
    x = osc("saw", f, n=n)
    fe = np.exp(-t / (0.12 if accent else 0.2))
    fc = cutoff + env_amt * (1.6 if accent else 1.0) * fe
    y = dsp.sweep_filter(x, "lp", fc, q=1.0 + res * 9, block=32)
    y = sat(y * (1.6 if accent else 1.1), 1.4)
    return y * env_adsr(n, 0.003, 0.1, 0.8, 0.02)


def whistle(freq: float, dur: float, vib: float = 0.4) -> np.ndarray:
    """Breathy flute/whistle lead."""
    n = ns(dur)
    t = np.arange(n) / SR
    f = freq * (1 + (vib / 100) * np.sin(2 * np.pi * 5.2 * t))
    x = np.sin(2 * np.pi * np.cumsum(f) / SR) + 0.08 * np.sin(4 * np.pi * np.cumsum(f) / SR)
    br = filt(noise(n=n), "bp", freq * 2, q=2.0) * 0.25
    return (x + br) * env_adsr(n, 0.05, 0.1, 0.8, 0.12)


def cowbell(freq: float = 560.0, dur: float = 0.35, tone: float = 1.0) -> np.ndarray:
    """808-style cowbell: two detuned squares through a band-pass (phonk lead)."""
    n = ns(dur)
    x = osc("square", freq, n=n) + osc("square", freq * 1.48, n=n)
    x = filt(x, "bp", freq * 1.6, q=1.4)
    e = 0.7 * env_exp(n, 0.06) + 0.3 * env_exp(n, dur)
    return sat(x * e * 1.8 * tone, 1.5)


def vox_chop(freq: float, dur: float, vowel: str = "a") -> np.ndarray:
    """Formant 'vocal chop' synth for house leads."""
    F = {"a": (800, 1150, 2900), "o": (450, 800, 2830), "e": (400, 2000, 2550), "u": (325, 700, 2530), "i": (270, 2140, 2950)}[vowel]
    n = ns(dur)
    src = osc("saw", freq, n=n) + 0.3 * noise(n=n)
    y = np.zeros(n)
    for f, g in zip(F, (1.0, 0.6, 0.3)):
        y += g * filt(src, "bp", f, q=8, order=1)
    return y * env_adsr(n, 0.01, 0.08, 0.6, 0.06) * 1.6


# ================================================================ bass

def sub_bass(freq, dur: float, drive: float = 1.2, attack: float = 0.005, release: float = 0.05) -> np.ndarray:
    n = ns(dur)
    x = osc("sine", freq, n=n) + 0.15 * osc("tri", np.asarray(freq) * 2, n=n)
    return sat(x * drive, drive) * env_adsr(n, attack, 0.05, 0.9, release)


def bass_808(freq: float, dur: float, glide_from: float | None = None, drive: float = 2.2, decay: float = 1.4) -> np.ndarray:
    n = ns(dur)
    t = np.arange(n) / SR
    f = np.full(n, freq) * (1 + 0.6 * np.exp(-t / 0.012))
    if glide_from:
        g = min(n, ns(0.09))
        f[:g] = np.geomspace(glide_from, freq, g)
    x = np.sin(2 * np.pi * np.cumsum(f) / SR)
    e = env_exp(n, decay, attack=0.002)
    r = ns(0.02)
    e[-r:] *= np.linspace(1, 0, r)
    return sat(x * drive, drive) * e


def log_drum(freq: float, dur: float = 0.5, drive: float = 1.8) -> np.ndarray:
    """Amapiano log drum: woody pitched hit with a downward pitch bend and a bit of grit."""
    n = ns(dur)
    t = np.arange(n) / SR
    f = freq * (1 + 0.5 * np.exp(-t / 0.025)) * (1 - 0.06 * (1 - np.exp(-t / 0.15)))
    ph = 2 * np.pi * np.cumsum(f) / SR
    x = np.sin(ph) + 0.35 * np.sin(2 * ph + 0.3) + 0.12 * np.sin(3 * ph)
    body = sat(x * drive, drive) * env_exp(n, dur * 0.9, attack=0.001)
    knock = filt(noise(n=ns(0.012)), "bp", 1200, q=1.5) * 0.4
    body[: len(knock)] += knock
    return body


def pluck_bass(freq: float, dur: float) -> np.ndarray:
    n = ns(dur)
    t = np.arange(n) / SR
    x = osc("saw", freq, n=n) * 0.6 + osc("sine", freq, n=n)
    fc = 180 + 1400 * np.exp(-t / 0.06)
    y = dsp.sweep_filter(x, "lp", fc, q=1.2, block=64)
    return y * env_adsr(n, 0.003, 0.2, 0.6, 0.05)


def upright(freq: float, dur: float) -> np.ndarray:
    x = pluck_ks(freq, dur, bright=0.35, decay=0.9985, pick=0.3)
    x = filt(x, "lp", 900)
    x += 0.5 * osc("sine", freq, n=len(x)) * env_exp(len(x), dur * 0.8)
    return x / 1.5


# ================================================================ drums

def kick(dur: float = 0.45, f0: float = 52.0, punch: float = 1.0, click: float = 0.3, drive: float = 1.5, decay: float = 0.35) -> np.ndarray:
    n = ns(dur)
    t = np.arange(n) / SR
    f = f0 + f0 * 3.5 * punch * np.exp(-t / 0.03)
    x = np.sin(2 * np.pi * np.cumsum(f) / SR)
    x = sat(x * drive, drive) * env_exp(n, decay, attack=0.0005)
    c = filt(noise(n=ns(0.004)), "hp", 2500) * click
    x[: len(c)] += c
    return x


def snare(dur: float = 0.25, tone: float = 190.0, snappy: float = 0.7, decay: float = 0.18) -> np.ndarray:
    n = ns(dur)
    t = np.arange(n) / SR
    body = np.sin(2 * np.pi * np.cumsum(tone * (1 + 0.5 * np.exp(-t / 0.01))) / SR) * env_exp(n, 0.09)
    nz = filt(noise(n=n), "bp", 4500, q=0.6) * env_exp(n, decay) * snappy * 2.2
    return sat(body * 0.8 + nz, 1.2)


def clap(dur: float = 0.3, decay: float = 0.18) -> np.ndarray:
    n = ns(dur)
    nz = filt(noise(n=n), "bp", 1300, q=1.2) * 2.5
    e = np.zeros(n)
    for k, off in enumerate([0, 0.008, 0.016, 0.026]):
        s = ns(off)
        seg = env_exp(n - s, 0.03 if k < 3 else decay)
        e[s:] = np.maximum(e[s:], seg)
    return nz * e


def hat(dur: float = 0.08, open_: bool = False, tone: float = 8000.0) -> np.ndarray:
    n = ns(0.45 if open_ else dur)
    t = np.arange(n) / SR
    # metallic: six square partials (808 hat recipe) + noise
    base = [205.3, 304.4, 369.6, 522.7, 540.0, 800.0]
    m = sum(osc("square", f * 1.0, n=n) for f in base)
    x = filt(m * 0.5 + noise(n=n) * 1.5, "bp", tone, q=0.9)
    x = filt(x, "hp", 6000)
    return x * env_exp(n, 0.35 if open_ else dur, attack=0.0005) * 2.0


def shaker(dur: float = 0.09, accent: float = 1.0) -> np.ndarray:
    n = ns(dur)
    x = filt(noise(n=n), "bp", 7000, q=1.0)
    e = env_adsr(n, 0.012, 0.03, 0.3, 0.04)
    return x * e * 2.2 * accent


def rim(dur: float = 0.06, f: float = 1700.0) -> np.ndarray:
    n = ns(dur)
    t = np.arange(n) / SR
    x = np.sin(2 * np.pi * f * t) + 0.5 * np.sin(2 * np.pi * f * 2.3 * t)
    x = x * env_exp(n, 0.04) + filt(noise(n=n), "hp", 3000) * env_exp(n, 0.01)
    return x * 0.6


def tom(freq: float = 110.0, dur: float = 0.4) -> np.ndarray:
    n = ns(dur)
    t = np.arange(n) / SR
    f = freq * (1 + 0.4 * np.exp(-t / 0.04))
    x = np.sin(2 * np.pi * np.cumsum(f) / SR) * env_exp(n, dur * 0.9)
    x += filt(noise(n=n), "lp", 2000) * env_exp(n, 0.04) * 0.3
    return x


def crash(dur: float = 2.0) -> np.ndarray:
    n = ns(dur)
    x = filt(noise(n=n), "hp", 3500) * env_exp(n, dur, attack=0.001) * 0.8
    x += filt(noise(n=n), "bp", 9000, q=0.5) * env_exp(n, dur * 0.4) * 0.5
    return x


def vinyl_crackle(dur: float, density: float = 18.0, level: float = 0.06) -> np.ndarray:
    n = ns(dur)
    rng = np.random.default_rng(99)
    x = np.zeros(n)
    k = int(dur * density)
    pos = rng.integers(0, n, k)
    x[pos] = rng.choice([-1, 1], k) * rng.uniform(0.15, 1.0, k) ** 3
    x = filt(x, "bp", 2500, q=0.5) * 6
    hiss = filt(noise(n=n), "bp", 6000, q=0.4) * 0.08
    return (x + hiss) * level


# ================================================================ fx

def whoosh(dur: float = 0.8, f_start: float = 300.0, f_end: float = 3000.0, q: float = 2.0, shape: float = 0.5) -> np.ndarray:
    """Band-passed noise sweep with a bell-shaped amplitude (peak at `shape` of the duration)."""
    n = ns(dur)
    t = np.linspace(0, 1, n)
    fc = np.geomspace(f_start, f_end, n)
    x = dsp.sweep_filter(noise(n=n) * 3, "bp", fc, q=q, block=64)
    e = np.where(t < shape, (t / shape) ** 2, ((1 - t) / (1 - shape)) ** 1.5)
    return x * e


def riser(dur: float = 1.5, f0: float = 200.0, f1: float = 4000.0) -> np.ndarray:
    n = ns(dur)
    t = np.linspace(0, 1, n)
    x = dsp.sweep_filter(noise(n=n) * 2, "bp", np.geomspace(f0, f1, n), q=3.0)
    x += 0.3 * osc("saw", np.geomspace(f0 / 2, f1 / 4, n), n=n) * t
    return x * t ** 2


def coin(freq: float = 2600.0, dur: float = 0.5) -> np.ndarray:
    """Small metal coin: inharmonic plate partials with fast decays."""
    n = ns(dur)
    t = np.arange(n) / SR
    x = np.zeros(n)
    for r, a, d in [(1.0, 1.0, 0.35), (1.59, 0.6, 0.25), (2.14, 0.5, 0.18), (2.65, 0.35, 0.12), (3.16, 0.25, 0.08)]:
        x += a * np.sin(2 * np.pi * freq * r * t + r) * np.exp(-6.9 * t / (d * dur / 0.5))
    c = filt(noise(n=ns(0.004)), "hp", 5000) * 0.5
    x[: len(c)] += c
    a = ns(0.0008)
    x[:a] *= np.linspace(0, 1, a)
    return x / 2.2


def sparkle(dur: float = 0.6, n_notes: int = 7, base: float = 2000.0, spread: float = 2.5) -> np.ndarray:
    rng = np.random.default_rng(int(base) + n_notes)
    n = ns(dur)
    out = np.zeros(n)
    for i in range(n_notes):
        s = int(rng.uniform(0, 0.6) * n)
        f = base * spread ** rng.uniform(0, 1)
        c = fm_bell(f, 0.25, ratio=1.0, index=0.3, decay=0.2) * rng.uniform(0.3, 0.8)
        e = min(n, s + len(c))
        out[s:e] += c[: e - s]
    return out


# ================================================================ CT-native additions (2026-10-08, audio integration lane)
# Research notes and sources: docs/progress/audio-integration.md "Sound culture research".

def talking_drum(f0: float, dur: float = 0.35, glide: float = 1.45, squeeze_at: float = 0.04, release_at: float | None = None,
                 drive: float = 1.3) -> np.ndarray:
    """Yoruba dùndún-style talking drum: a membrane hit whose pitch glides as the player squeezes the tension cords
    (Wikipedia "Talking drum": pitch changes within a single beat, "a warbling note", "like a human humming").
    `glide` is the squeezed pitch ratio; `release_at` (s) lets the pitch fall back for the "gùn-gun" speech contour."""
    n = ns(dur)
    t = np.arange(n) / SR
    up = 1 / (1 + np.exp(-(t - squeeze_at) / 0.012))
    ratio = 1 + (glide - 1) * up
    if release_at is not None:
        down = 1 / (1 + np.exp(-(t - release_at) / 0.02))
        ratio = ratio - (glide - 1) * 0.8 * down
    f = f0 * ratio * (1 + 0.25 * np.exp(-t / 0.008))
    ph = 2 * np.pi * np.cumsum(f) / SR
    body = np.sin(ph) + 0.35 * np.sin(1.52 * ph + 0.4) + 0.12 * np.sin(2.3 * ph)
    body = sat(body * drive, drive) * env_exp(n, dur * 0.85, attack=0.0015)
    slap = filt(noise(n=ns(0.015)), "bp", 1800, q=1.2) * 0.5
    body[: len(slap)] += slap
    return body * 0.8


def paper_flutter(dur: float = 0.09, centre: float = 3800.0, flap_hz: float = 45.0, seed: int = 0) -> np.ndarray:
    """One banknote fluttering past (owambe "spraying"): band-passed noise with a fast irregular flap."""
    rng = np.random.default_rng(seed)
    n = ns(dur)
    t = np.arange(n) / SR
    x = filt(noise(n=n), "bp", centre * rng.uniform(0.8, 1.25), q=1.4)
    flap = 0.5 + 0.5 * np.sign(np.sin(2 * np.pi * flap_hz * rng.uniform(0.8, 1.3) * t + rng.uniform(0, 6)))
    flap = filt(flap, "lp", 400)
    return x * flap * env_adsr(n, 0.004, 0.02, 0.6, dur * 0.4) * 1.8


def air_horn(dur: float, bend: float = 0.0) -> np.ndarray:
    """Synth DJ air horn (stacked saws, honky mid boost). Own synthesis; no sampled horn."""
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


def terminal_ping(freq: float, dur: float = 0.22, bright: float = 1.0) -> np.ndarray:
    """Clean trading-terminal ping: sine + light FM, very fast attack, short exponential tail."""
    n = ns(dur)
    t = np.arange(n) / SR
    idx = 0.9 * bright * np.exp(-t / 0.03)
    x = np.sin(2 * np.pi * freq * t + idx * np.sin(2 * np.pi * freq * 2 * t))
    x += 0.18 * np.sin(2 * np.pi * freq * 3 * t) * np.exp(-t / 0.02)
    return x * env_exp(n, dur, attack=0.0008)


def tick(dur: float = 0.012, f: float = 5200.0) -> np.ndarray:
    """Teletype / key tick."""
    n = ns(dur)
    x = filt(noise(n=n), "bp", f, q=2.0) * env_exp(n, dur * 0.6, attack=0.0003) * 2.0
    return x


# ================================================================ main-theme voices (2026-10-09, audio integration lane)

def uke(freq: float, dur: float = 0.6, bright: float = 0.7) -> np.ndarray:
    """Ukulele / nylon-string pluck: Karplus-Strong with a small wooden body (peaks ~450 Hz and ~2.4 kHz)."""
    x = pluck_ks(freq, dur, bright=bright, decay=0.9955, pick=0.22)
    x = dsp.eq(x, "peak", 450, 4.0, q=1.4)
    x = dsp.eq(x, "peak", 2400, 3.0, q=1.2)
    x = filt(x, "hp", 160)
    return x * env_exp(len(x), dur * 1.4, attack=0.0008)


def strum(freqs: list[float], dur: float, up: bool = False, spread: float = 0.011, bright: float = 0.7) -> np.ndarray:
    """A strum across `freqs` (low→high, or high→low for an up-strum), each string `spread` s apart."""
    order = list(reversed(freqs)) if up else list(freqs)
    n = ns(dur + spread * len(order))
    out = np.zeros(n)
    for i, f in enumerate(order):
        s = ns(i * spread)
        v = uke(f, dur, bright=bright) * (0.8 + 0.2 * (i / max(1, len(order) - 1)))
        out[s:s + len(v)] += v[: n - s]
    return out / max(1.0, len(order) ** 0.6)


def glock(freq: float, dur: float = 0.9) -> np.ndarray:
    """Glockenspiel: bar partials ~1, 2.76, 5.4 with fast upper decays and a mallet tick."""
    n = ns(dur)
    t = np.arange(n) / SR
    x = np.sin(2 * np.pi * freq * t) * np.exp(-t / (dur * 0.45))
    x += 0.35 * np.sin(2 * np.pi * freq * 2.76 * t) * np.exp(-t / 0.12)
    x += 0.15 * np.sin(2 * np.pi * freq * 5.40 * t) * np.exp(-t / 0.04)
    tick = filt(noise(n=ns(0.003)), "hp", 6000) * 0.25
    x[: len(tick)] += tick
    a = ns(0.0007)
    x[:a] *= np.linspace(0, 1, a)
    return x / 1.4


def tambourine(dur: float = 0.18, accent: float = 1.0) -> np.ndarray:
    """Tambourine: jingles (several inharmonic metallic partials) + skin tap."""
    n = ns(dur)
    t = np.arange(n) / SR
    x = np.zeros(n)
    for f in (5200, 6100, 7300, 8800, 10400):
        x += np.sin(2 * np.pi * f * t + f) * np.exp(-t / 0.06)
    x = x * 0.15 + filt(noise(n=n), "bp", 8000, q=1.2) * env_exp(n, 0.07) * 1.2
    skin = filt(noise(n=ns(0.01)), "bp", 1200, q=1.0) * 0.3
    x[: len(skin)] += skin
    return x * env_adsr(n, 0.002, 0.05, 0.4, dur * 0.6) * accent


def snap(dur: float = 0.12) -> np.ndarray:
    """Finger snap: short bright noise burst with a little body."""
    n = ns(dur)
    x = filt(noise(n=n), "bp", 2600, q=1.5) * env_exp(n, 0.025, attack=0.0004) * 2.0
    x += filt(noise(n=n), "hp", 5000) * env_exp(n, 0.012) * 0.6
    return x


def bonk(dur: float = 0.45) -> np.ndarray:
    """Cartoon bonk: hollow wooden knock with a fast downward pitch drop, then a short springy wobble."""
    n = ns(dur)
    t = np.arange(n) / SR
    f = 160 + 620 * np.exp(-t / 0.018)
    knock = np.sin(2 * np.pi * np.cumsum(f) / SR) * env_exp(n, 0.12, attack=0.0005)
    wood = filt(noise(n=ns(0.02)), "bp", 1500, q=3.0) * 0.8
    knock[: len(wood)] += wood
    wob_f = 330 * (1 + 0.08 * np.sin(2 * np.pi * 17 * t) * np.exp(-t / 0.2)) * (1 - 0.25 * t / dur)
    wobble = np.sin(2 * np.pi * np.cumsum(wob_f) / SR) * env_adsr(n, 0.02, 0.1, 0.5, dur * 0.5) * 0.35
    s = ns(0.05)
    out = knock.copy()
    out[s:] += wobble[: n - s]
    return sat(out * 1.4, 1.3)


def slide_whistle(f0: float, f1: float, dur: float) -> np.ndarray:
    """Slide whistle glide f0 → f1 (comic deflate when f1 < f0)."""
    n = ns(dur)
    t = np.linspace(0, 1, n)
    f = f0 * (f1 / f0) ** (t ** 1.3)
    f = f * (1 + 0.012 * np.sin(2 * np.pi * 6 * t * dur))
    x = np.sin(2 * np.pi * np.cumsum(f) / SR) + 0.05 * np.sin(4 * np.pi * np.cumsum(f) / SR)
    br = filt(noise(n=n), "bp", 2000, q=1.0) * 0.06
    return (x + br) * env_adsr(n, 0.03, 0.05, 0.85, 0.08)
