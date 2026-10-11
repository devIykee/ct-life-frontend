#!/usr/bin/env python3
"""CT Life main theme (`mus_title`): title screen and onboarding loop. Own work (composed and synthesised), CC0.

Brief (coordinator, 2026-10-09): upbeat, warm, playful, a sunny-town feel matching the painted title art
(Mediterranean plaza, café, fountain, string flags), loopable, 60–90 s.
Form (32 bars at 112 BPM, light swing = 68.6 s, rendered circularly so the loop point is seamless):
  bars  1–4  intro      ukulele island strum + glockenspiel hook + finger snaps
  bars  5–12 A          marimba states the hook (theme.HOOK); upright bass; light kit; shaker
  bars 13–20 A'         whistle takes the melody, marimba thirds, highlife-style guitar counter-line, tambourine
  bars 21–28 B          lyrical bridge on electric piano + whistle; warm pad; claps
  bars 29–32 turnaround hook fragments in call and response, tom fill back into the intro
Not mood-reactive (sfx-map `mood_reactive: false`): onboarding should not get muffled because SOL is red.
Mix: −16 LUFS integrated, true peak ≤ −1.5 dBTP before encoding; Vorbis q6 source (Unity re-encodes at 0.45).
Usage: gen_title.py      → processed/music/title/mus_title.ogg (+ build/music/mus_title.wav, tools/build_title.json)
"""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np

import dsp
import instruments as ins
import theme as th
from dsp import SR, mtof
from gen_music import Track, limiter_gain, voicing
from master import BUILD, OUT, encode_ogg, export_wav, lufs, true_peak_db

BARS = 32
TARGET_LUFS = -16.0
TP_CEIL = -1.5
STEM_LUFS = {"pad": -21.0, "bass": -21.5, "drums": -21.5, "lead": -19.5}
OUT_PATH = OUT / "music" / "title" / "mus_title.ogg"


def chords_for(bar: int):
    """[(beat_in_bar, root, quality, length_beats)] for bar 0..31."""
    if bar < 4:
        table, i = th.INTRO_CHORDS, bar
    elif bar < 20:
        table, i = th.A_CHORDS, (bar - 4) % 8
    elif bar < 28:
        table, i = th.B_CHORDS, bar - 20
    else:
        table, i = th.OUTRO_CHORDS, bar - 28
    c = table[i]
    if len(c) == 4:
        return [(0, c[0], c[1], 2), (2, c[2], c[3], 2)]
    return [(0, c[0], c[1], 4)]


def chord_tones_at(abs_beat: float) -> set[int]:
    """Pitch classes of the chord sounding at an absolute beat."""
    bar = int(abs_beat // 4) % BARS
    beat = abs_beat - (abs_beat // 4) * 4
    ch = next(c for c in reversed(chords_for(bar)) if c[0] <= beat + 1e-9)
    return {n % 12 for n in voicing(ch[1], ch[2], lo=48, hi=72)}


def harmony_below(notes, start_bar: int):
    """Second voice: for each (beat, note, len) the highest chord tone 3..9 semitones below `note`."""
    out = []
    for beat, n, d in notes:
        pcs = chord_tones_at(start_bar * 4 + beat)
        cand = [m for m in range(n - 9, n - 2) if m % 12 in pcs]
        if cand:
            out.append((beat, max(cand), d))
    return out


def section(bar: int) -> str:
    return "intro" if bar < 4 else "A" if bar < 12 else "A2" if bar < 20 else "B" if bar < 28 else "out"


def compose() -> Track:
    tr = Track("title", th.BPM, bars=BARS, swing=th.SWING)
    rng = np.random.default_rng(20261009)

    for bar in range(BARS):
        sec = section(bar)
        b0 = bar * 4
        chords = chords_for(bar)
        nxt = chords_for((bar + 1) % BARS)[0]

        # ---------------- ukulele: island strum D . D U . U D U
        if sec != "B":
            pat = [(0, False, 1.0), (1, False, 0.8), (1.5, True, 0.55), (2.5, True, 0.6), (3, False, 0.8), (3.5, True, 0.55)]
            for beat, up, g in pat:
                ch = next(c for c in reversed(chords) if c[0] <= beat)
                notes = voicing(ch[1], ch[2], lo=60, hi=74)[:4]
                gain = (0.30 if sec == "intro" else 0.24) * g
                tr.add("pad", ins.strum([mtof(n) for n in notes], tr.beats(0.9), up=up), b0 + beat, gain,
                       pan=-0.25, human=0.006)
        else:
            # B: electric piano comping (warm, sustained) instead of the strum
            for beat, ln, g in ((0, 1.4, 0.10), (1.5, 0.9, 0.07), (2.5, 1.4, 0.09)):
                ch = next(c for c in reversed(chords) if c[0] <= beat)
                for k, n in enumerate(voicing(ch[1], ch[2], lo=57, hi=74)):
                    tr.add("pad", ins.epiano(mtof(n), tr.beats(ln), vel=0.6), b0 + beat + k * 0.01, g / 2.2, pan=-0.15)
            ch = chords[0]
            tr.add("pad", ins.pad([mtof(n) for n in voicing(ch[1], ch[2], lo=60, hi=76)], tr.beats(4.1), attack=0.6,
                                  release=0.6, cutoff=1600, voices=3), b0, 0.025, human=0)

        # ---------------- bass: bouncy upright, root/fifth with an approach note into the next chord
        if sec != "intro":
            for beat0, root, q, ln in chords:
                r = root - 12 if root - 12 >= 40 else root
                fifth = r + 7
                if ln == 4:
                    hits = [(0, r, 0.9, 0.8), (1.5, fifth, 0.45, 0.55), (2, r, 0.9, 0.7), (3, fifth, 0.45, 0.6)]
                    nr = nxt[1] - 12 if nxt[1] - 12 >= 40 else nxt[1]
                    hits.append((3.5, nr - 1 if (nr - r) % 12 in (1, 2, 5, 7) else nr + 2, 0.45, 0.55))
                else:
                    hits = [(0, r, 0.9, 0.8), (1, fifth, 0.45, 0.6), (1.5, r + 12, 0.45, 0.45)]
                for beat, note, d, g in hits:
                    tr.add("bass", ins.upright(mtof(note), tr.beats(d) + 0.08), b0 + beat0 + beat, g, human=0.003)

        # ---------------- drums
        if sec == "intro":
            for beat in (1, 3):
                tr.add("drums", ins.snap(), b0 + beat, 0.35, pan=0.2)
            for s in range(8):
                tr.add("drums", ins.shaker(0.07, 0.8 if s % 2 else 0.5), b0 + s / 2, 0.10, pan=0.35)
        else:
            k = ins.kick(0.35, f0=58, punch=0.7, click=0.12, drive=1.2, decay=0.22)
            tr.kick_at(k, b0, 0.62)
            tr.kick_at(k, b0 + 2, 0.55)
            if bar % 2 == 1 and sec != "B":
                tr.kick_at(k, b0 + 2.75, 0.35)
            back = dsp.layer(ins.rim(0.07, 1700) * 0.7, ins.clap(0.22, 0.09) * 0.5)
            tr.add("drums", back, b0 + 1, 0.30, pan=0.05)
            tr.add("drums", back, b0 + 3, 0.30, pan=0.05)
            for s in range(16):
                acc = 1.0 if s % 4 == 2 else (0.7 if s % 2 else 0.45)
                tr.add("drums", ins.shaker(0.07, acc), b0 + s / 4, 0.09, pan=0.35)
            if sec in ("A2", "B"):
                tr.add("drums", ins.tambourine(0.18), b0 + 1, 0.16, pan=-0.35)
                tr.add("drums", ins.tambourine(0.18, 0.7), b0 + 3, 0.14, pan=-0.35)
            if bar % 4 == 3 and sec != "out":
                tr.add("drums", ins.hat(open_=True, tone=8500), b0 + 3.5, 0.10, pan=0.25)
            if bar == BARS - 1:  # fill back into the intro
                for i, (beat, f) in enumerate(((2.5, 196), (3.0, 165), (3.5, 131))):
                    tr.add("drums", ins.tom(f, 0.35), b0 + beat, 0.30 - 0.03 * i, pan=-0.3 + 0.3 * i)

    # ---------------- lead lines
    def play(notes, start_bar, voice, gain, pan=0.0, transpose=0, human=0.004):
        for beat, n, d in notes:
            tr.add("lead", voice(mtof(n + transpose), tr.beats(d)), start_bar * 4 + beat, gain, pan=pan, human=human)

    marimba = lambda f, d: ins.marimba(f, max(0.35, d + 0.15))
    whistle = lambda f, d: ins.whistle(f, d * 0.95, vib=0.45)
    glock = lambda f, d: ins.glock(f, 0.9)

    # intro: glockenspiel quotes the first bar of the hook twice, an octave up
    play(th.HOOK[:6], 0, glock, 0.20, pan=0.3, transpose=12)
    play(th.HOOK[:6], 2, glock, 0.16, pan=0.3, transpose=12)
    # A: marimba melody, glock doubles the long notes
    play(th.A_MELODY, 4, marimba, 0.60, pan=0.1)
    play([n for n in th.A_MELODY if n[2] >= 1], 4, glock, 0.07, pan=0.35, transpose=12)
    # A': whistle melody; marimba harmony = the nearest chord tone 3-9 semitones below each melody note (always consonant)
    play(th.A_MELODY, 12, whistle, 0.20, pan=-0.05)
    play(harmony_below(th.A_MELODY, 12), 12, marimba, 0.16, pan=0.3)
    # highlife-style guitar counter-line in A': chord tones on the off-beats
    for bar in range(12, 20):
        for beat0, root, q, ln in chords_for(bar):
            tones = voicing(root, q, lo=67, hi=83)
            for j, off in enumerate([0.5, 1.25, 1.75, 2.5, 3.25, 3.75][: 6 if ln == 4 else 3]):
                tr.add("lead", ins.pluck_ks(mtof(tones[j % len(tones)]), tr.beats(0.5), bright=0.6), bar * 4 + beat0 + off,
                       0.10, pan=-0.45)
    # B: whistle melody with electric-piano doubling an octave down
    play(th.B_MELODY, 20, whistle, 0.24, pan=0.05)
    play(th.B_MELODY, 20, lambda f, d: ins.epiano(f, d, vel=0.5), 0.05, pan=0.2, transpose=-12)
    # turnaround: hook call (marimba) and answer (glock)
    play(th.HOOK[:6], 28, marimba, 0.55, pan=0.1)
    play(th.HOOK[6:], 28, glock, 0.18, pan=0.35, transpose=12)
    play([(0, th.D5, 0.5), (0.5, th.G5, 0.5), (1, th.B5, 1), (2, th.D6, 2)], 30, marimba, 0.52, pan=0.1)
    # sparkle at phrase ends
    for bar in (11, 19, 27):
        tr.add("lead", ins.sparkle(0.8, 6, 2600, 2.0), bar * 4 + 3, 0.06, pan=0.4, human=0)

    tr.sidechain("pad", 0.15, 0.16)
    tr.fx_reverb("lead", 0.22, 1.6, hp=250)
    tr.fx_reverb("pad", 0.16, 1.3, hp=200)
    tr.fx_reverb("drums", 0.07, 0.7, hp=500)
    tr.fx_eq("bass", "peak", 110, 2.0, 0.9)
    return tr


def render() -> dict:
    tr = compose()
    stems = {}
    for s, target in STEM_LUFS.items():
        x = tr.buf[s]
        stems[s] = x * dsp.db(target - lufs(x))
    full = sum(stems.values())
    # Phone-speaker voicing: trim the boxy 300-500 Hz build-up (uke body + marimba fundamentals), lift presence.
    L = len(full)
    loop3 = np.concatenate([full, full, full], axis=0)
    loop3 = dsp.eq(loop3, "peak", 380, -2.0, q=1.0)
    loop3 = dsp.eq(loop3, "peak", 3200, 1.5, q=0.8)
    full = loop3[L:2 * L]
    full = full * dsp.db(TARGET_LUFS - lufs(full))
    for _ in range(4):
        tp = true_peak_db(full)
        if tp <= TP_CEIL:
            break
        full = full * limiter_gain(full, TP_CEIL - 0.6)[:, None]
        full = full * dsp.db(TARGET_LUFS - lufs(full))
    wav = BUILD / "music" / "mus_title.wav"
    export_wav(wav, full, peak_db=None, gain_db=0, loop=True)
    encode_ogg(wav, OUT_PATH, 6.0)
    rep = {
        "id": "mus_title", "bpm": th.BPM, "bars": BARS, "time_signature": "4/4", "key": "G major",
        "loop_samples": tr.L, "sr": SR, "loop_seconds": round(tr.L / SR, 4),
        "file": str(OUT_PATH.relative_to(OUT.parent)),
        "lufs_pre_encode": round(lufs(full), 2), "true_peak_pre_encode_dbtp": round(true_peak_db(full), 2),
    }
    Path(__file__).with_name("build_title.json").write_text(json.dumps(rep, indent=2))
    print(rep)
    return rep


if __name__ == "__main__":
    render()
