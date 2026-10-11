#!/usr/bin/env python3
"""Onboarding mix audit: are the UI cues loud enough over the title music at default settings?

Signal chain modelled (client/Assets/Scripts/Audio): clip × sfx-map `volume` × call-site volume (UI lane, Kit/Motion.cs
Cues + LotteryScreen/CharacterCreatorScreen) × SFX bus (default slider², VolumeSettings) versus mus_title × music bus
(default slider²) × music duck (−9 dB while a stinger plays). Master/device volume scales both equally, so it cancels.

Metric: masking happens per frequency band, and UI cues live where the ear is most sensitive, so the margin is
measured in the 1–6 kHz presence band: loudest 50 ms RMS window of the cue minus the music's median 50 ms RMS, both
band-passed 1–6 kHz (dB). Full-band numbers are printed too. Phone speakers barely reproduce < 200 Hz, so the band
metric is also the closer model of what the player hears.
Targets [Inference, common game-mix practice, not a published standard]: ≥ +6 dB for action feedback (tap, select,
next, error, flip, reveal, celebration), ≥ +3 dB for ambient ticks (shuffle hops, deselect); and every cue's peak after
gains ≤ −1 dBFS (Unity has no master limiter here; music peaks sit near −4.4 dBFS at the default slider).
Usage: mix_audit.py [--json]
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

import numpy as np

import dsp
from master import read_audio

ROOT = Path(__file__).resolve().parents[1]
WIN = int(0.05 * dsp.SR)

# Defaults mirrored from VolumeSettings (client/Assets/Scripts/Audio/Logic/MixModel.cs).
MUSIC_SLIDER, SFX_SLIDER = 0.85, 1.0
STINGER_DUCK_DB = -9.0

# (label, sfx id, call-site volume, target margin dB, source of the call)
CUES = [
    ("tap (buttons, El.Btn)",          "sfx_ui_tap",                 1.00, 6, "UI/El.cs Btn default"),
    ("select (card / trait)",          "sfx_ui_toggle_on",           0.80, 6, "Kit.Cues.Select"),
    ("shuffle deal tick",              "sfx_ui_toggle_on",           0.40, 3, "LotteryScreen.Shuffle"),
    ("spotlight hop tick",             "sfx_ui_toggle_on",           0.45, 3, "Kit.Cues.Hop"),
    ("deselect",                       "sfx_ui_toggle_off",          0.60, 3, "Kit.Cues.Deselect"),
    ("next / step done",               "sfx_ui_next",                0.55, 6, "proposed for Kit.Cues.SfxStep"),
    ("(today) next: ui_success",       "sfx_ui_success",             0.55, 6, "Kit.Cues.Step today"),
    ("error / disabled CTA",           "sfx_ui_error",               0.60, 6, "Kit.Cues.Nope"),
    ("shuffle (two riffles)",          "sfx_card_shuffle",           0.50, 3, "proposed for LotteryScreen.Shuffle"),
    ("(today) shuffle start: flip",    "sfx_pnl_card_flip",          0.50, 3, "LotteryScreen.Shuffle today"),
    ("card flip (reveal)",             "sfx_pnl_card_flip",          1.00, 6, "LotteryScreen.Reveal"),
    ("coin burst",                     "sfx_cash_in",                0.70, 6, "LotteryScreen.Reveal (good)"),
    ("origin: Insider / Seeker",       "sfx_origin_insider",         1.00, 6, "proposed for Kit.Cues.SfxGood"),
    ("origin: Exit Liquidity",         "sfx_origin_exit_liquidity",  1.00, 6, "proposed for Kit.Cues.SfxDebt"),
    ("creator celebration",            "sfx_creator_complete",       1.00, 6, "proposed for Kit.Cues.SfxCelebrate"),
    ("(today) origin good: moon horn", "sfx_moon_horn",              1.00, 6, "Kit.Cues.SfxGood today"),
    ("(today) origin debt: rug",       "sfx_rug",                    0.50, 6, "Kit.Cues.SfxDebt today"),
    ("(today) celebrate: graduation",  "sfx_graduation",             1.00, 6, "Kit.Cues.SfxCelebrate today"),
]


def presence(x: np.ndarray) -> np.ndarray:
    m = x.mean(axis=1) if x.ndim == 2 else x
    return dsp.filt(dsp.filt(m, "hp", 1000.0, order=4), "lp", 6000.0, order=4)


def rms_windows(x: np.ndarray) -> np.ndarray:
    m = x.mean(axis=1) if x.ndim == 2 else x
    n = len(m) // WIN
    if n == 0:
        return np.array([np.sqrt(np.mean(m ** 2)) + 1e-12])
    seg = m[: n * WIN].reshape(n, WIN)
    # 50 % overlap so a short click is not split across two windows
    seg2 = m[WIN // 2: WIN // 2 + (n - 1) * WIN].reshape(n - 1, WIN) if n > 1 else seg[:0]
    return np.sqrt(np.concatenate([np.mean(seg ** 2, axis=1), np.mean(seg2 ** 2, axis=1)])) + 1e-12


def db(v: float) -> float:
    return float(20 * np.log10(max(v, 1e-12)))


def main() -> int:
    m = json.loads((ROOT / "sfx-map.json").read_text())
    music_entry = m["music"].get("title")
    if not music_entry:
        print("no music.title in sfx-map.json")
        return 1
    music = read_audio(ROOT / "processed" / music_entry["full_mix"])
    music_gain = MUSIC_SLIDER ** 2 * music_entry.get("volume", 1.0)
    mw = rms_windows(music * music_gain)
    music_med = db(float(np.median(mw)))
    music_p90 = db(float(np.percentile(mw, 90)))
    mwp = rms_windows(presence(music * music_gain))
    music_med_p = db(float(np.median(mwp)))
    sfx_gain = SFX_SLIDER ** 2
    rows = []
    for label, sid, call, target, src in CUES:
        e = m["sfx"].get(sid)
        if not e:
            rows.append({"label": label, "id": sid, "missing": True})
            continue
        x = read_audio(ROOT / "processed" / e["files"][0])
        g = e["volume"] * call * sfx_gain
        peak = db(float(np.max(np.abs(x))) * g)
        loud = db(float(np.max(rms_windows(x))) * g)
        duck = STINGER_DUCK_DB if e.get("category") == "stinger" else 0.0
        full_margin = loud - (music_med + duck)
        loud_p = db(float(np.max(rms_windows(presence(x)))) * g)
        margin = loud_p - (music_med_p + duck)
        # the map gain that would meet the target exactly (capped at 0 dB: Unity source volume is 0..1)
        need_db = min(20 * np.log10(e["volume"]) + (target - margin), 20 * np.log10(e["volume"]) + (-1.0 - peak))
        rows.append({"label": label, "id": sid, "call": call, "map_db": round(20 * np.log10(e["volume"]), 1),
                     "peak_dbfs": round(peak, 1), "rms50_dbfs": round(loud, 1), "margin_full_db": round(full_margin, 1),
                     "margin_db": round(margin, 1), "target_db": target, "ok": margin >= target and peak <= -1.0, "map_db_for_target": round(float(need_db), 1), "source": src})
    if "--json" in sys.argv:
        print(json.dumps({"music_median_rms50_dbfs": round(music_med, 1), "music_p90_rms50_dbfs": round(music_p90, 1),
                          "music_median_presence_rms50_dbfs": round(music_med_p, 1), "rows": rows}, indent=2))
        return 0
    print(f"mus_title at music slider {MUSIC_SLIDER} (gain {20 * np.log10(music_gain):+.1f} dB): "
          f"50 ms RMS median {music_med:.1f} dBFS (p90 {music_p90:.1f}); 1-6 kHz band median {music_med_p:.1f} dBFS; "
          f"stingers duck it {STINGER_DUCK_DB:+.0f} dB")
    print(f"{'cue':32s} {'id':26s} {'call':>4s} {'map dB':>6s} {'peak':>6s} {'rms50':>6s} {'full':>6s} {'band':>6s} {'target':>6s}  ok")
    bad = 0
    for r in rows:
        if r.get("missing"):
            print(f"{r['label']:32s} {r['id']:26s}  MISSING from sfx-map.json")
            bad += 1
            continue
        flag = "ok" if r["ok"] else "LOW"
        bad += 0 if r["ok"] else 1
        print(f"{r['label']:32s} {r['id']:26s} {r['call']:4.2f} {r['map_db']:6.1f} {r['peak_dbfs']:6.1f} {r['rms50_dbfs']:6.1f} "
              f"{r['margin_full_db']:+6.1f} {r['margin_db']:+6.1f} {'+' + str(r['target_db']):>6s}  {flag}")
    print(f"{bad} below target")
    return 0


if __name__ == "__main__":
    sys.exit(main())
