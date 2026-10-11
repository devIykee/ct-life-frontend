"""Small numpy/scipy DSP kit used by every CT Life audio generator.

Everything renders at 48 kHz float64 in [-1, 1]. Stereo buffers are shape (n, 2).
No sample libraries: every sound built here is our own work (CC0).
"""
from __future__ import annotations

import numpy as np
from scipy import signal

SR = 48000
RNG = np.random.default_rng(20261005)


def seed(n: int) -> None:
    global RNG
    RNG = np.random.default_rng(n)


# ---------------------------------------------------------------- basics

def secs(n_samples: int) -> float:
    return n_samples / SR


def ns(seconds: float) -> int:
    return int(round(seconds * SR))


def tvec(dur: float) -> np.ndarray:
    return np.arange(ns(dur)) / SR


def db(x: float) -> float:
    return 10 ** (x / 20)


def mtof(m: float) -> float:
    return 440.0 * 2 ** ((m - 69) / 12)


def phase_from_freq(freq: np.ndarray | float, n: int) -> np.ndarray:
    """Integrate a (possibly time-varying) frequency in Hz into phase (cycles)."""
    f = np.broadcast_to(np.asarray(freq, dtype=float), (n,))
    return np.cumsum(f) / SR


def _polyblep(t: np.ndarray, dt: np.ndarray) -> np.ndarray:
    out = np.zeros_like(t)
    m = t < dt
    x = t[m] / dt[m]
    out[m] = x + x - x * x - 1.0
    m2 = t > 1.0 - dt
    x = (t[m2] - 1.0) / dt[m2]
    out[m2] = x * x + x + x + 1.0
    return out


def osc(kind: str, freq, dur: float | None = None, n: int | None = None, phase0: float = 0.0, pw: float = 0.5):
    """Band-limited-ish oscillator. kind: sine, saw, square, tri, pulse."""
    if n is None:
        n = ns(dur)
    f = np.broadcast_to(np.asarray(freq, dtype=float), (n,)).astype(float)
    ph = (phase_from_freq(f, n) + phase0) % 1.0
    dt = np.clip(np.abs(f) / SR, 1e-9, 0.5)
    if kind == "sine":
        return np.sin(2 * np.pi * ph)
    if kind == "saw":
        return 2.0 * ph - 1.0 - _polyblep(ph, dt)
    if kind in ("square", "pulse"):
        w = pw if kind == "pulse" else 0.5
        y = np.where(ph < w, 1.0, -1.0)
        y = y + _polyblep(ph, dt) - _polyblep((ph + (1 - w)) % 1.0, dt)
        return y
    if kind == "tri":
        # harmonics fall at 1/n^2, so the naive triangle aliases very little
        return 2.0 * np.abs(2.0 * ph - 1.0) - 1.0
    raise ValueError(kind)


def noise(dur: float | None = None, n: int | None = None, color: str = "white") -> np.ndarray:
    if n is None:
        n = ns(dur)
    w = RNG.standard_normal(n)
    if color == "white":
        return w / 3.0
    if color == "pink":
        b = [0.049922035, -0.095993537, 0.050612699, -0.004408786]
        a = [1, -2.494956002, 2.017265875, -0.522189400]
        y = signal.lfilter(b, a, w)
        return y / (np.max(np.abs(y)) + 1e-9) * 0.9
    if color == "brown":
        y = signal.lfilter([1], [1, -0.995], w)
        y = y - np.mean(y)
        return y / (np.max(np.abs(y)) + 1e-9) * 0.9
    raise ValueError(color)


# ---------------------------------------------------------------- envelopes

def env_adsr(n: int, a: float, d: float, s: float, r: float, gate: float | None = None) -> np.ndarray:
    """ADSR in seconds; gate = seconds before release starts (default: n - r)."""
    e = np.zeros(n)
    na, nd, nr = ns(a), ns(d), min(ns(r), n)
    ng = n - nr if gate is None else min(n, ns(gate))
    ng = max(ng, min(n, na // 2 + 1))
    i = 0
    if na > 0:
        k = min(na, ng)
        e[:k] = np.linspace(0, 1, na, endpoint=False)[:k]
        i = k
    if nd > 0 and i < ng:
        k = min(nd, ng - i)
        e[i:i + k] = 1 - (1 - s) * (1 - np.exp(-5 * np.arange(k) / nd))
        i += k
    if i < ng:
        e[i:ng] = s if nd > 0 else 1.0
    lvl = e[ng - 1] if ng > 0 else 0
    rem = n - ng
    if rem > 0:
        e[ng:] = lvl * np.exp(-6.9 * np.arange(rem) / max(nr, 1))
    return e


def env_exp(n: int, decay: float, attack: float = 0.002) -> np.ndarray:
    """Fast attack, exponential decay; `decay` = seconds to -60 dB."""
    t = np.arange(n) / SR
    e = np.exp(-6.9 * t / max(decay, 1e-4))
    na = ns(attack)
    if na > 0:
        e[:na] *= np.linspace(0, 1, na)
    return e


def fade(x: np.ndarray, fin: float = 0.002, fout: float = 0.01) -> np.ndarray:
    x = x.copy()
    a, b = ns(fin), ns(fout)
    if a > 0:
        r = np.linspace(0, 1, a)
        x[:a] = (x[:a].T * r).T
    if b > 0:
        r = np.linspace(1, 0, b) ** 2
        x[-b:] = (x[-b:].T * r).T
    return x


# ---------------------------------------------------------------- filters

def _sos(kind: str, f, q=0.707, order=2):
    f = np.clip(f, 10, SR / 2 * 0.95)
    if kind in ("lp", "hp"):
        return signal.butter(order, f, btype="low" if kind == "lp" else "high", fs=SR, output="sos")
    if kind == "bp":
        bw = f / q
        lo, hi = max(10, f - bw / 2), min(SR / 2 * 0.95, f + bw / 2)
        return signal.butter(order, [lo, hi], btype="band", fs=SR, output="sos")
    raise ValueError(kind)


def filt(x: np.ndarray, kind: str, f: float, q: float = 0.707, order: int = 2) -> np.ndarray:
    return signal.sosfilt(_sos(kind, f, q, order), x, axis=0)


def biquad_coeffs(kind: str, f: float, q: float = 0.707, gain_db: float = 0.0):
    """RBJ cookbook biquad (b, a)."""
    f = min(max(f, 10.0), SR * 0.45)
    w0 = 2 * np.pi * f / SR
    cw, sw = np.cos(w0), np.sin(w0)
    alpha = sw / (2 * q)
    A = 10 ** (gain_db / 40)
    if kind == "lp":
        b = [(1 - cw) / 2, 1 - cw, (1 - cw) / 2]; a = [1 + alpha, -2 * cw, 1 - alpha]
    elif kind == "hp":
        b = [(1 + cw) / 2, -(1 + cw), (1 + cw) / 2]; a = [1 + alpha, -2 * cw, 1 - alpha]
    elif kind == "bp":
        b = [alpha, 0, -alpha]; a = [1 + alpha, -2 * cw, 1 - alpha]
    elif kind == "peak":
        b = [1 + alpha * A, -2 * cw, 1 - alpha * A]; a = [1 + alpha / A, -2 * cw, 1 - alpha / A]
    elif kind == "lowshelf":
        sq = 2 * np.sqrt(A) * alpha
        b = [A * ((A + 1) - (A - 1) * cw + sq), 2 * A * ((A - 1) - (A + 1) * cw), A * ((A + 1) - (A - 1) * cw - sq)]
        a = [(A + 1) + (A - 1) * cw + sq, -2 * ((A - 1) + (A + 1) * cw), (A + 1) + (A - 1) * cw - sq]
    elif kind == "highshelf":
        sq = 2 * np.sqrt(A) * alpha
        b = [A * ((A + 1) + (A - 1) * cw + sq), -2 * A * ((A - 1) + (A + 1) * cw), A * ((A + 1) + (A - 1) * cw - sq)]
        a = [(A + 1) - (A - 1) * cw + sq, 2 * ((A - 1) - (A + 1) * cw), (A + 1) - (A - 1) * cw - sq]
    else:
        raise ValueError(kind)
    b = np.array(b) / a[0]
    a = np.array(a) / a[0]
    return b, a


def eq(x: np.ndarray, kind: str, f: float, gain_db: float = 0.0, q: float = 0.707) -> np.ndarray:
    b, a = biquad_coeffs(kind, f, q, gain_db)
    return signal.lfilter(b, a, x, axis=0)


def sweep_filter(x: np.ndarray, kind: str, fc: np.ndarray, q: float = 0.707, block: int = 64) -> np.ndarray:
    """Time-varying RBJ biquad, coefficients updated every `block` samples (state kept)."""
    mono = x.ndim == 1
    xx = x[:, None] if mono else x
    y = np.zeros_like(xx)
    fc = np.broadcast_to(np.asarray(fc, dtype=float), (len(xx),))
    zi = np.zeros((2, xx.shape[1]))
    for i in range(0, len(xx), block):
        b, a = biquad_coeffs(kind, float(fc[i]), q)
        seg, zi = signal.lfilter(b, a, xx[i:i + block], axis=0, zi=zi)
        y[i:i + block] = seg
    return y[:, 0] if mono else y


def ladder_lp(x: np.ndarray, fc: np.ndarray, res: float = 0.3, block: int = 32) -> np.ndarray:
    """Resonant low-pass (two cascaded RBJ LPs with Q from `res`), time-varying."""
    q = 0.707 + res * 6.0
    y = sweep_filter(x, "lp", fc, q=q, block=block)
    return sweep_filter(y, "lp", fc, q=0.707, block=block)


# ---------------------------------------------------------------- dynamics / colour

def sat(x: np.ndarray, drive: float = 1.0) -> np.ndarray:
    return np.tanh(x * drive) / np.tanh(drive)


def normalize(x: np.ndarray, peak_db: float = -1.0) -> np.ndarray:
    m = np.max(np.abs(x))
    return x if m == 0 else x / m * db(peak_db)


def rms_db(x: np.ndarray) -> float:
    return 20 * np.log10(np.sqrt(np.mean(np.square(x))) + 1e-12)


def bitcrush(x: np.ndarray, bits: int = 10, down: int = 1) -> np.ndarray:
    q = 2 ** (bits - 1)
    y = np.round(x * q) / q
    if down > 1:
        y = np.repeat(y[::down], down, axis=0)[: len(x)]
    return y


# ---------------------------------------------------------------- stereo

def pan(x: np.ndarray, p: float = 0.0) -> np.ndarray:
    """Constant-power pan of a mono signal, p in [-1, 1]."""
    ang = (p + 1) * np.pi / 4
    return np.stack([x * np.cos(ang), x * np.sin(ang)], axis=1)


def to_stereo(x: np.ndarray) -> np.ndarray:
    return x if x.ndim == 2 else np.stack([x, x], axis=1)


def to_mono(x: np.ndarray) -> np.ndarray:
    return x if x.ndim == 1 else x.mean(axis=1)


def widen(x: np.ndarray, amt: float = 0.3, delay_ms: float = 12.0) -> np.ndarray:
    """Haas-style widening of a mono or stereo source."""
    m = to_mono(x)
    d = ns(delay_ms / 1000)
    dl = np.r_[np.zeros(d), m[:-d]] if d > 0 else m
    side = (m - dl) * 0.5 * amt
    return np.stack([m + side, m - side], axis=1)


def mix_into(buf: np.ndarray, x: np.ndarray, start: int, gain: float = 1.0, wrap: bool = False) -> None:
    """Add x into buf at sample `start`. With wrap=True, overflow wraps to the start (seamless loops)."""
    if x.ndim == 1 and buf.ndim == 2:
        x = to_stereo(x)
    n = len(x)
    L = len(buf)
    if start >= L and not wrap:
        return
    start = start % L if wrap else start
    end = start + n
    if end <= L:
        buf[start:end] += x * gain
    else:
        k = L - start
        buf[start:] += x[:k] * gain
        if wrap:
            rest = x[k:]
            while len(rest) > 0:
                m = min(len(rest), L)
                buf[:m] += rest[:m] * gain
                rest = rest[m:]


# ---------------------------------------------------------------- space

_IR_CACHE: dict = {}


def reverb_ir(decay: float = 1.6, predelay: float = 0.012, damp: float = 6000.0, seed_: int = 7) -> np.ndarray:
    key = (decay, predelay, damp, seed_)
    if key in _IR_CACHE:
        return _IR_CACHE[key]
    rng = np.random.default_rng(seed_)
    n = ns(decay * 1.2 + predelay)
    t = np.arange(n) / SR
    env = np.exp(-6.9 * t / decay)
    ir = rng.standard_normal((n, 2)) * env[:, None]
    # high frequencies die faster: blend a low-passed copy in over time
    lp = signal.sosfilt(signal.butter(2, damp, fs=SR, output="sos"), ir, axis=0)
    w = np.clip(t / (decay * 0.5), 0, 1)[:, None]
    ir = ir * (1 - w) + lp * w
    pd = ns(predelay)
    ir = np.r_[np.zeros((pd, 2)), ir]
    ir /= np.sqrt(np.sum(ir ** 2) / 2)
    _IR_CACHE[key] = ir
    return ir


def reverb(x: np.ndarray, wet: float = 0.25, decay: float = 1.6, predelay: float = 0.012, damp: float = 6000.0,
           hp: float = 200.0, keep_tail: bool = True) -> np.ndarray:
    """Convolution reverb with a synthetic stereo IR. Returns stereo, longer by the tail if keep_tail."""
    xs = to_stereo(x)
    ir = reverb_ir(decay, predelay, damp)
    src = filt(xs, "hp", hp) if hp else xs
    wl = signal.fftconvolve(src[:, 0], ir[:, 0])
    wr = signal.fftconvolve(src[:, 1], ir[:, 1])
    w = np.stack([wl, wr], axis=1) * wet
    out = np.zeros_like(w)
    out[: len(xs)] += xs * (1 - wet * 0.3)
    out += w
    return out if keep_tail else out[: len(xs)]


def delay(x: np.ndarray, time_s: float, fb: float = 0.35, wet: float = 0.3, taps: int = 6, pingpong: bool = True,
          lp: float = 5000.0) -> np.ndarray:
    xs = to_stereo(x)
    d = ns(time_s)
    n = len(xs) + d * taps
    out = np.zeros((n, 2))
    out[: len(xs)] += xs
    tap = filt(xs, "lp", lp)
    g = wet
    for k in range(1, taps + 1):
        s = d * k
        seg = tap * g
        if pingpong:
            m = seg.mean(axis=1)
            seg = pan(m, -0.7 if k % 2 else 0.7)
        out[s:s + len(xs)] += seg
        g *= fb
    return out


def loop_wrap(x: np.ndarray, length: int) -> np.ndarray:
    """Fold everything past `length` back onto the start (tails of a loop ring into its beginning)."""
    out = x[:length].copy()
    rest = x[length:]
    while len(rest) > 0:
        m = min(len(rest), length)
        out[:m] += rest[:m]
        rest = rest[m:]
    return out


def crossfade_loop(x: np.ndarray, length: int, xfade: int) -> np.ndarray:
    """Make a seamless loop of `length` from a recording at least length + xfade long (equal-power)."""
    assert len(x) >= length + xfade
    out = x[:length].copy()
    t = np.linspace(0, np.pi / 2, xfade)
    fi, fo = np.sin(t), np.cos(t)
    if x.ndim == 2:
        fi, fo = fi[:, None], fo[:, None]
    out[:xfade] = x[:xfade] * fi + x[length:length + xfade] * fo
    return out


def layer(*xs: np.ndarray) -> np.ndarray:
    """Sum signals of different lengths (zero-padded to the longest)."""
    n = max(len(x) for x in xs)
    shape = (n,) + xs[0].shape[1:]
    out = np.zeros(shape)
    for x in xs:
        out[: len(x)] += x
    return out
