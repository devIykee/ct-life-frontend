#!/usr/bin/env python3
"""Human-made recordings and compositions for the onboarding cues, the title theme and the red-market layer.

User direction (2026-10-09): "please don't synthesize. that would most likely sound fake, let's use real stuff".
This script does NO synthesis. It downloads each source from its public page (no account needed), checks the
SHA-256 against the value recorded at selection time, then only edits: trim, fade, crossfade a loop point, constant
gain, resample to 48 kHz, join two takes of one recording, filter (club through a wall). Every source's licence was
read on its page on CHECKED (date below); the rows feed audio/LICENSES.md and the in-game Music-Credits.txt.

Output paths are the existing cue paths, so no client wiring changes:
  music/title/mus_title.ogg              Glitter Blast — Kevin MacLeod (CC BY 4.0), 32-bar seamless loop
  sfx/sfx_origin_insider.wav             "Won!" orchestral winning jingle — spuispuin (CC BY 4.0)
  sfx/sfx_origin_exit_liquidity.wav      Kenney Music Jingles, Sax 01 (falling "wah") — Kenney (CC0)
  sfx/sfx_creator_complete.wav           Win Jingle, steel drums — Fupi (CC0)
  sfx/sfx_ui_next.wav                    Pop 5 — cogitollc (CC0, recorded on a phone)
  sfx/sfx_card_shuffle.wav               Shuffle and bridge — BMacZero (CC0)
  music/mood/mus_mood_tension_bed.ogg    Thunder ambience field recording — Gregor Quendel (CC BY 4.0), 24 s loop
  music/licensed/mus_clubs_disco_con_tutti.ogg   Disco con Tutti — Kevin MacLeod (CC BY 4.0), restored for clubs
  ambience/amb_club_doors.wav            Disco con Tutti through a closed door + crowd (existing CC0 freesound layer)
Usage: curate_real_audio.py            (writes processed/ files, tools/build_real.json, sources/real-audio-20261009.json)
"""
from __future__ import annotations

import hashlib
import json
import shutil
import subprocess
import urllib.request
import zipfile
from pathlib import Path

import numpy as np

import dsp
from dsp import ns, filt
from master import BUILD, OUT, RAW, encode_ogg, export_wav, lufs, read_audio, true_peak_db

TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parent
DL = RAW / "real"
CHECKED = "2026-10-09"
CC_BY_4 = ("CC-BY-4.0", "https://creativecommons.org/licenses/by/4.0/")
CC0 = ("CC0-1.0", "https://creativecommons.org/publicdomain/zero/1.0/")

# id → source record. `sha256` is of the downloaded file as selected on CHECKED.
SOURCES = {
    "glitter_blast": dict(
        title="Glitter Blast", author="Kevin MacLeod", licence=CC_BY_4,
        page="https://incompetech.com/music/royalty-free/index.html?isrc=USUAN1900001",
        licence_page="https://incompetech.com/music/royalty-free/licenses/",
        catalogue="https://incompetech.com/music/royalty-free/pieces.json (ISRC USUAN1900001, 100 BPM, ukulele/flutes/glockenspiel)",
        url="https://incompetech.com/music/royalty-free/mp3-royaltyfree/Glitter%20Blast.mp3", file="incompetech/Glitter Blast.mp3", sha256="6d24390bfe0dba5107b04d643c2e36e13dae8048a382d465f0b575397f01b89b"),
    "won": dict(
        title="Won! (Orchestral winning jingle)", author="spuispuin", licence=CC_BY_4,
        page="https://opengameart.org/content/won-orchestral-winning-jingle", licence_page="https://opengameart.org/content/won-orchestral-winning-jingle",
        url="https://opengameart.org/sites/default/files/Won%21.wav", file="opengameart/Won!.wav", sha256="794b06cc8cc4d1988680285c382134bf2888cee2d4a5eb6c13880c1da1b53c53"),
    "kenney_jingles": dict(
        title="Music Jingles (jingles_SAX01)", author="Kenney (Kenney Vleugels)", licence=CC0,
        page="https://kenney.nl/assets/music-jingles", licence_page="https://kenney.nl/assets/music-jingles",
        url=None, file="../kenney/music-jingles/Audio/Sax jingles/jingles_SAX01.ogg", sha256="272b8160616d0f924f447df3f8543ab83440c5e41168107bd5abe1140894122b",
        note="already downloaded with the Kenney packs (2026-10-05); License.txt in the zip says CC0"),
    "win_jingle": dict(
        title="Win Jingle (WinSteelDrums)", author="Fupi", licence=CC0,
        page="https://opengameart.org/content/win-jingle", licence_page="https://opengameart.org/content/win-jingle",
        url="https://opengameart.org/sites/default/files/winjingle.zip", file="opengameart/winjingle.zip", sha256="fef26adc010e7e7d6f2ab586f400188831152530f8f3adcd179f26435d69ce6b", member="WinSteelDrums.wav"),
    "pop": dict(
        title="Pop Sounds (pop5)", author="cogitollc", licence=CC0,
        page="https://opengameart.org/content/pop-sounds", licence_page="https://opengameart.org/content/pop-sounds",
        url="https://opengameart.org/sites/default/files/pop5.ogg", file="opengameart/pop5.ogg", sha256="0433b2f016b2521df36a7e0dde006c0839af4fb97df7ca626f5fb7a4c4eb561e",
        note="page: 'Hardware: iPhone SE (2016) Software: Voice Memos, Ocenaudio' — a recording"),
    "shuffle": dict(
        title="Playing Card Sounds (shuffleandbridge)", author="BMacZero", licence=CC0,
        page="https://opengameart.org/content/playing-card-sounds", licence_page="https://opengameart.org/content/playing-card-sounds",
        url="https://opengameart.org/sites/default/files/shuffleandbridge.wav", file="opengameart/shuffleandbridge.wav", sha256="7178fdd4b85875fc74aa0aaf534acbec8dad393006156cf7e679443dd6a4f85b",
        note="[Inference] a microphone recording: the page does not say; the file has a room-noise floor and real card transients"),
    "thunder": dict(
        title="Thunder / Lightning Ambience (field recording)", author="Gregor Quendel", licence=CC_BY_4,
        page="https://opengameart.org/content/thunder-lightning-ambience-field-recording",
        licence_page="https://opengameart.org/content/thunder-lightning-ambience-field-recording",
        url="https://opengameart.org/sites/default/files/gregor_quendel_-_free_thunder_ambience_-_mp3_0.mp3",
        file="opengameart/gregor_quendel_-_free_thunder_ambience_-_mp3_0.mp3", sha256="996d34046faa8e3e886f49eda1450c9a501bdec0e02f35804a2f1d0507d1bf78",
        note="page: 'A spring thunder / lightning ambience recording.'"),
}


def sha256(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def fetch(key: str) -> Path:
    s = SOURCES[key]
    p = (DL / s["file"]).resolve()
    if not p.exists():
        if not s.get("url"):
            raise FileNotFoundError(f"{p} (run tools/fetch_sources.py for the Kenney packs)")
        p.parent.mkdir(parents=True, exist_ok=True)
        req = urllib.request.Request(s["url"], headers={"User-Agent": "Mozilla/5.0"})
        with urllib.request.urlopen(req, timeout=180) as r, open(p, "wb") as f:
            shutil.copyfileobj(r, f)
    digest = sha256(p)
    if s.get("sha256") and s["sha256"] != digest:
        raise RuntimeError(f"{key}: source changed upstream ({digest} != {s['sha256']}); re-check its licence page")
    s["sha256"] = digest
    if s.get("member"):
        out = p.parent / Path(s["file"]).stem / s["member"]
        if not out.exists():
            with zipfile.ZipFile(p) as z:
                z.extract(s["member"], out.parent)
        return out
    return p


def trim(x: np.ndarray, thresh_db: float = -50.0, pre: float = 0.004, tail: float = 0.25, max_s: float | None = None,
         fade_out: float = 0.12) -> np.ndarray:
    """Cut leading/trailing silence (relative to peak), keep a short pre-roll and a natural tail with a fade.
    `max_s` caps the length (the reverb tail is faded out over `fade_out` seconds before the cap)."""
    a = np.abs(x).max(axis=1) if x.ndim == 2 else np.abs(x)
    idx = np.where(a > a.max() * dsp.db(thresh_db))[0]
    s = max(0, idx[0] - ns(pre))
    e = min(len(x), idx[-1] + ns(tail))
    if max_s:
        e = min(e, s + ns(max_s))
    return dsp.fade(x[s:e], 0.002, min(fade_out, (e - s) / dsp.SR / 4))


def one_shot(name: str, x: np.ndarray, channels: int, rep: dict, key: str, edit: str):
    info = export_wav(OUT / "sfx" / f"{name}.wav", x, peak_db=-1.0, channels=channels)
    rep[name] = {"source": key, "edit": edit + "; 48 kHz; peak-normalised to -1 dBTP", **info}
    print(f"{name:28s} {len(x) / dsp.SR:5.2f}s  from {SOURCES[key]['title']} — {SOURCES[key]['author']}")


def title_loop(rep: dict) -> None:
    """Glitter Blast: 100.00 BPM (onset comb), first downbeat 2.275 s; bars 31→63 (phrase-aligned, 8-bar grid) chosen by
    bar-feature similarity of the seam (chroma + band energy, bars e-1/e/e+1 vs s-1/s/s+1). One-beat equal-power crossfade
    with the real continuation after bar 63, so the wrap is the recording's own next bar fading into bar 31."""
    x = read_audio(fetch("glitter_blast"))
    bar = 4 * 60.0 / 100.0
    s_t, bars, xf_t = 2.275 + 31 * bar, 32, 0.6
    L, xf = ns(bars * bar), ns(xf_t)
    s = ns(s_t)
    loop = dsp.crossfade_loop(x[s:s + L + xf], L, xf)
    g = -16.0 - lufs(loop)
    loop = loop * dsp.db(g)
    tp = true_peak_db(loop)
    if tp > -1.5:                       # constant gain only: back off to the true-peak ceiling
        loop = loop * dsp.db(-1.5 - tp)
        g += -1.5 - tp
    wav = BUILD / "music" / "mus_title.wav"
    export_wav(wav, loop, peak_db=None, gain_db=0, loop=True)
    dst = OUT / "music" / "title" / "mus_title.ogg"
    encode_ogg(wav, dst, 6.0)
    rep["mus_title"] = {"source": "glitter_blast", "file": str(dst.relative_to(OUT.parent)), "loop_samples": L,
                        "loop_seconds": round(L / dsp.SR, 4), "bpm": 100.0, "bars": bars,
                        "source_window_s": [round(s_t, 3), round(s_t + bars * bar, 3)],
                        "edit": f"bars 31-63 of the original ({s_t:.3f}-{s_t + bars * bar:.3f} s), {xf_t} s equal-power loop crossfade, "
                                f"constant {g:+.2f} dB gain, 48 kHz stereo Vorbis q6; no tempo, pitch or arrangement change",
                        "lufs": round(lufs(loop), 2), "true_peak_dbtp": round(true_peak_db(loop), 2)}
    print(f"mus_title                    {L / dsp.SR:5.2f}s loop  {rep['mus_title']['lufs']} LUFS  TP {rep['mus_title']['true_peak_dbtp']}")


def tension_bed(rep: dict) -> None:
    """24 s of the steadiest rain/thunder stretch, 3 s crossfade loop, -26 LUFS (sits under the low-passed music)."""
    from gen_ambience import steadiest
    x = read_audio(fetch("thunder"))
    L, xf = ns(24.0), ns(3.0)
    s = steadiest(x, L + xf)
    loop = dsp.crossfade_loop(x[s:s + L + xf], L, xf)
    loop = filt(loop, "hp", 40)
    loop = loop * dsp.db(-26.0 - lufs(loop))
    tp = true_peak_db(loop)
    if tp > -3.0:
        loop = loop * dsp.db(-3.0 - tp)
    wav = BUILD / "music" / "mus_mood_tension_bed.wav"
    export_wav(wav, loop, peak_db=None, gain_db=0, loop=True)
    dst = OUT / "music" / "mood" / "mus_mood_tension_bed.ogg"
    encode_ogg(wav, dst, 4.0)
    rep["mus_mood_tension_bed"] = {"source": "thunder", "file": str(dst.relative_to(OUT.parent)), "loop_samples": L,
                                   "edit": f"{s / dsp.SR:.1f}-{(s + L) / dsp.SR:.1f} s of the recording (steadiest stretch), 3 s loop "
                                           "crossfade, 40 Hz high-pass, -26 LUFS, Vorbis q4",
                                   "lufs": round(lufs(loop), 2), "true_peak_dbtp": round(true_peak_db(loop), 2)}
    print(f"mus_mood_tension_bed         24.00s loop  from {s / dsp.SR:.1f}s of the field recording")


def clubs(rep: dict) -> None:
    """Restore the curated licensed club track (archived when the synthesized Afropiano stems replaced it), and rebuild
    the club-door ambience from it: low-passed through a wall + the existing CC0 crowd layer."""
    src = ROOT / "archive" / "processed" / "music" / "licensed" / "mus_clubs_disco_con_tutti.ogg"
    dst = OUT / "music" / "licensed" / "mus_clubs_disco_con_tutti.ogg"
    if src.exists() and not dst.exists():
        shutil.move(str(src), str(dst))
    from gen_ambience import XF, finish, layer_src
    mix = read_audio(dst)
    CL = ns(32.0)
    start = ns(60.0)
    seg = mix[start:start + CL + ns(XF)]
    thru = filt(seg, "lp", 160, order=4) + filt(filt(seg, "lp", 700, order=4), "hp", 160) * 0.12
    thru = dsp.eq(thru, "peak", 70, 4.0, q=1.2)
    wall = thru * dsp.db(-24 - 1 - lufs(thru))
    crowd = layer_src("amb_club_doors", 640630, CL + ns(XF), -7.0, lp=3500)
    info = finish("amb_club_doors", wall + crowd, CL)
    rep["amb_club_doors"] = {"source": "Disco con Tutti (Kevin MacLeod, CC BY 4.0) + freesound 640630 (CC0)",
                             "edit": "60-95 s of the track, 160 Hz low-pass + band-passed mids (a closed door), 3 s loop crossfade",
                             **info}


def main() -> None:
    rep: dict = {}
    title_loop(rep)
    x = read_audio(fetch("won"))
    one_shot("sfx_origin_insider", trim(x, max_s=2.6, fade_out=0.35), 2, rep, "won", "leading silence trimmed, reverb tail faded out by 2.6 s")
    x = read_audio(fetch("kenney_jingles"))
    one_shot("sfx_origin_exit_liquidity", trim(x), 2, rep, "kenney_jingles", "leading silence trimmed, tail faded")
    x = read_audio(fetch("win_jingle"))
    one_shot("sfx_creator_complete", trim(x, max_s=2.7, fade_out=0.3), 2, rep, "win_jingle", "leading silence trimmed, last chord faded out by 2.7 s (jingle 2.45 s)")
    x = read_audio(fetch("pop"), mono=True)
    one_shot("sfx_ui_next", trim(x, tail=0.04), 1, rep, "pop", "trimmed to the pop")
    x = read_audio(fetch("shuffle"), mono=True)
    riffle, bridge = x[: ns(0.68)], x[ns(1.15): ns(1.80)]
    j = ns(0.02)
    joined = np.concatenate([riffle[:-j], riffle[-j:] * np.linspace(1, 0, j) + bridge[:j] * np.linspace(0, 1, j), bridge[j:]])
    one_shot("sfx_card_shuffle", trim(joined, tail=0.08), 1, rep, "shuffle",
             "riffle (0-0.68 s) joined to the bridge cascade (1.15-1.80 s) with a 20 ms crossfade (the recording's silent gap removed)")
    tension_bed(rep)
    clubs(rep)
    (TOOLS / "build_real.json").write_text(json.dumps(rep, indent=2))
    prov = {k: {kk: (list(vv) if isinstance(vv, tuple) else vv) for kk, vv in v.items()} | {"checked": CHECKED}
            for k, v in SOURCES.items()}
    (ROOT / "sources" / "real-audio-20261009.json").write_text(json.dumps(prov, indent=2) + "\n")


if __name__ == "__main__":
    main()
