"""Export, measurement and ffmpeg helpers (decode, loudness, Vorbis encode)."""
from __future__ import annotations

import json
import re
import subprocess
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy import signal

import dsp

ROOT = Path(__file__).resolve().parents[1]          # audio/
RAW = ROOT / "raw"
OUT = ROOT / "processed"
BUILD = ROOT / "build"                              # gitignored scratch renders
FFMPEG = "ffmpeg"


def true_peak_db(x: np.ndarray) -> float:
    """4x oversampled peak (same idea as BS.1770 true peak)."""
    y = signal.resample_poly(x, 4, 1, axis=0)
    return 20 * np.log10(np.max(np.abs(y)) + 1e-12)


def tpdf_16(x: np.ndarray) -> np.ndarray:
    lsb = 1.0 / 32768
    rng = np.random.default_rng(1)
    d = (rng.random(x.shape) - rng.random(x.shape)) * lsb
    return np.clip(x + d, -1.0, 1.0 - lsb)


def export_wav(path: Path, x: np.ndarray, peak_db: float | None = -1.0, channels: int | None = None,
               gain_db: float | None = None, sr: int = dsp.SR, loop: bool = False) -> dict:
    """Write 16-bit PCM WAV. Either normalise to a true-peak target or apply a fixed gain."""
    path.parent.mkdir(parents=True, exist_ok=True)
    x = np.asarray(x, dtype=float)
    if channels == 1 and x.ndim == 2:
        x = x.mean(axis=1)
    if channels == 2 and x.ndim == 1:
        x = np.stack([x, x], axis=1)
    # remove DC and sub-sonic rumble (nothing useful below 25 Hz on a phone speaker or earbuds)
    sos = signal.butter(2, 25, btype="high", fs=sr, output="sos")
    if loop:  # circular filtering keeps the loop seam intact
        n = len(x)
        x = signal.sosfiltfilt(sos, np.concatenate([x, x, x], axis=0), axis=0)[n:2 * n]
    else:
        x = signal.sosfiltfilt(sos, x, axis=0)
    if gain_db is not None:
        x = x * dsp.db(gain_db)
    elif peak_db is not None:
        tp = true_peak_db(x)
        x = x * dsp.db(peak_db - tp)
    sf.write(str(path), tpdf_16(x), sr, subtype="PCM_16")
    return {"path": str(path.relative_to(ROOT)), "samples": len(x), "sr": sr, "channels": 1 if x.ndim == 1 else 2}


def read_audio(path: Path, sr: int = dsp.SR, mono: bool = False) -> np.ndarray:
    """Decode anything ffmpeg reads to float at `sr` (soxr resampler)."""
    cmd = [FFMPEG, "-v", "error", "-i", str(path), "-af", f"aresample={sr}:resampler=soxr:precision=28",
           "-f", "f32le", "-acodec", "pcm_f32le"]
    if mono:
        cmd += ["-ac", "1"]
    else:
        cmd += ["-ac", "2"]
    cmd += ["-"]
    raw = subprocess.run(cmd, check=True, capture_output=True).stdout
    x = np.frombuffer(raw, dtype=np.float32).astype(float)
    return x if mono else x.reshape(-1, 2)


def ebur128(path: Path) -> dict:
    """Integrated loudness, LRA and true peak via ffmpeg's ebur128 filter."""
    r = subprocess.run([FFMPEG, "-nostats", "-hide_banner", "-i", str(path), "-af", "ebur128=peak=true:framelog=quiet",
                        "-f", "null", "-"], capture_output=True, text=True)
    txt = r.stderr
    summ = txt[txt.rfind("Summary:"):]
    def grab(pat):
        m = re.search(pat, summ)
        return float(m.group(1)) if m else None
    return {"I": grab(r"I:\s+(-?[\d.]+|-inf) LUFS"), "LRA": grab(r"LRA:\s+(-?[\d.]+) LU"),
            "TP": grab(r"Peak:\s+(-?[\d.]+|-inf) dBFS")}


def encode_ogg(src: Path, dst: Path, q: float = 4.0) -> None:
    dst.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run([FFMPEG, "-v", "error", "-y", "-i", str(src), "-c:a", "libvorbis", "-q:a", str(q), str(dst)], check=True)


def probe(path: Path) -> dict:
    r = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "stream=codec_name,sample_rate,channels,duration_ts,duration",
                        "-of", "json", str(path)], capture_output=True, text=True, check=True)
    return json.loads(r.stdout)["streams"][0]


def lufs(x: np.ndarray, sr: int = dsp.SR) -> float:
    """BS.1770-4 integrated loudness (K-weighting, 400 ms blocks, -70 LUFS abs / -10 LU rel gates).
    Used for fast balancing; final numbers in reports always come from ffmpeg's ebur128."""
    xs = x if x.ndim == 2 else x[:, None]
    # K-weighting stage 1 (high shelf) and stage 2 (RLB high-pass), coefficients for 48 kHz
    b1, a1 = [1.53512485958697, -2.69169618940638, 1.19839281085285], [1.0, -1.69065929318241, 0.73248077421585]
    b2, a2 = [1.0, -2.0, 1.0], [1.0, -1.99004745483398, 0.99007225036621]
    if sr != 48000:
        raise ValueError("lufs() expects 48 kHz")
    y = signal.lfilter(b2, a2, signal.lfilter(b1, a1, xs, axis=0), axis=0)
    blk, hop = int(0.4 * sr), int(0.1 * sr)
    if len(y) < blk:
        return -70.0
    p = np.square(y)
    cs = np.concatenate([np.zeros((1, p.shape[1])), np.cumsum(p, axis=0)], axis=0)
    starts = np.arange(0, len(y) - blk + 1, hop)
    ms = (cs[starts + blk] - cs[starts]) / blk           # mean square per channel per block
    z = ms.sum(axis=1)                                     # channel weights = 1 (L, R)
    lk = -0.691 + 10 * np.log10(z + 1e-15)
    g = z[lk > -70]
    if len(g) == 0:
        return -70.0
    rel = -0.691 + 10 * np.log10(np.mean(g)) - 10
    g2 = z[(lk > -70) & (lk > rel)]
    return float(-0.691 + 10 * np.log10(np.mean(g2)))
