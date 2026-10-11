#!/usr/bin/env python3
"""Human-made recordings and compositions for the onboarding cues, the title theme and the red-market layer.

User direction (2026-10-09): "please don't synthesize. that would most likely sound fake, let's use real stuff".
This script does NO synthesis. It downloads each source from its public page (no account needed), checks the
SHA-256 against the value recorded at selection time, then only edits: trim, fade, crossfade a loop point, constant
gain, resample to 48 kHz, join two takes of one recording, filter (club through a wall). Every source's licence was
read on its page on CHECKED (date below); the rows feed audio/LICENSES.md and the in-game Music-Credits.txt.

Output paths are the existing cue paths, so no client wiring changes:
  music/title/mus_title.ogg              Glitter Blast - Kevin MacLeod (CC BY 4.0), 32-bar seamless loop
  sfx/sfx_origin_insider.wav             "Won!" orchestral winning jingle - spuispuin (CC BY 4.0)
  sfx/sfx_origin_exit_liquidity.wav      Kenney Music Jingles, Sax 01 (falling "wah") - Kenney (CC0)
  sfx/sfx_creator_complete.wav           Win Jingle, steel drums - Fupi (CC0)
  sfx/sfx_ui_next.wav                    Pop 5 - cogitollc (CC0, recorded on a phone)
  sfx/sfx_card_shuffle.wav               Shuffle and bridge - BMacZero (CC0)
  music/mood/mus_mood_tension_bed.ogg    Thunder ambience field recording - Gregor Quendel (CC BY 4.0), 24 s loop
  music/licensed/mus_clubs_disco_con_tutti.ogg   Disco con Tutti - Kevin MacLeod (CC BY 4.0), restored for clubs
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
        title="Pop Sounds (pop1-pop7)", author="cogitollc", licence=CC0,
        page="https://opengameart.org/content/pop-sounds", licence_page="https://opengameart.org/content/pop-sounds",
        url="https://opengameart.org/sites/default/files/pop5.ogg", file="opengameart/pop5.ogg", sha256="0433b2f016b2521df36a7e0dde006c0839af4fb97df7ca626f5fb7a4c4eb561e",
        note="page: 'Hardware: iPhone SE (2016) Software: Voice Memos, Ocenaudio' - a recording"),
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

# Pass 2 (2026-10-09): big CC0 packs instead of one page per cue. Each pack's licence read once: Kenney packs via the
# License.txt inside each downloaded zip ("Creative Commons Zero, CC0") and the kenney.nl pack pages; the freesound
# layers were already in the build (CC0 deed checked on each sound page by tools/fetch_sources.py on 2026-10-05; public
# preview files, no login). Recordings only; Kenney Music Jingles are composed and played on sampled instruments.
KENNEY = {
    "kenney_rpg": ("RPG Audio", "https://kenney.nl/assets/rpg-audio"),
    "kenney_casino": ("Casino Audio", "https://kenney.nl/assets/casino-audio"),
    "kenney_impact": ("Impact Sounds", "https://kenney.nl/assets/impact-sounds"),
    "kenney_jingles_all": ("Music Jingles (Sax, Steel, Pizzicato, Hit sets)", "https://kenney.nl/assets/music-jingles"),
}
for _k, (_t, _u) in KENNEY.items():
    SOURCES[_k] = dict(title=_t, author="Kenney (Kenney Vleugels)", licence=CC0, page=_u, licence_page=_u, url=None,
                       file=None, note="License.txt in the pack zip: Creative Commons Zero (CC0)")
FREESOUND = {"181949": ("Fog Horn", "qubodup"), "221528": ("Glass Break", "unfa"), "555161": ("zipper-01-01", "pbimal"),
             "131930": ("Industrial Air Horn", "mcpable"), "447941": ("Blast + Flamethrower cooldown", "Breviceps"),
             "459884": ("Flamethrower", "LilMati")}
for _id, (_t, _a) in FREESOUND.items():
    SOURCES["fs" + _id] = dict(title=_t, author=_a, licence=CC0, page=f"https://freesound.org/s/{_id}/", licence_page=f"https://freesound.org/s/{_id}/",
                               url=None, file=None, checked_on="2026-10-05", note="CC0 deed checked on the sound page 2026-10-05 (tools/fetch_sources.py); public HQ preview, no login")


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
    print(f"{name:28s} {len(x) / dsp.SR:5.2f}s  from {SOURCES[key]['title']} - {SOURCES[key]['author']}")


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


# ------------------------------------------------------------------ pass 2: every remaining synthesized SFX
J = {"sax": "Sax jingles/jingles_SAX", "steel": "Steel jingles/jingles_STEEL", "pizzi": "Pizzicato jingles/jingles_PIZZI", "hit": "Hit jingles/jingles_HIT"}


def k_(rel: str, mono: bool = False) -> np.ndarray:
    return read_audio((RAW / "kenney" / rel).resolve(), mono=mono)


def jingle(fam: str, n: int) -> np.ndarray:
    return k_(f"music-jingles/Audio/{J[fam]}{n:02d}.ogg")


def fs(i: str, mono: bool = False) -> np.ndarray:
    return read_audio(RAW / "freesound" / f"{i}.ogg", mono=mono)


def pop(n: int) -> np.ndarray:
    p = DL / "opengameart" / f"pop{n}.ogg"
    if not p.exists():
        req = urllib.request.Request(f"https://opengameart.org/sites/default/files/pop{n}.ogg", headers={"User-Agent": "Mozilla/5.0"})
        with urllib.request.urlopen(req, timeout=120) as r, open(p, "wb") as f:
            shutil.copyfileobj(r, f)
    return read_audio(p)


def lay(*parts, ch: int = 2) -> np.ndarray:
    """Place recordings on a timeline: parts = (audio, start_s, gain_db). Mixing only."""
    parts = [(trim(a, thresh_db=-55, tail=0.3), t, g) for a, t, g in parts]
    n = max(ns(t) + len(a) for a, t, g in parts)
    out = np.zeros((n, 2))
    for a, t, g in parts:
        a = a if a.ndim == 2 else np.stack([a, a], axis=1)
        out[ns(t): ns(t) + len(a)] += a * dsp.db(g)
    return out if ch == 2 else out.mean(axis=1)


def air_horn_blasts() -> np.ndarray:
    """Three blasts cut from one industrial air-horn recording: short, short, long."""
    x = fs("131930")
    x = x[np.argmax(np.abs(x).max(axis=1) > np.abs(x).max() * 0.05):]
    blast = lambda d: dsp.fade(x[: ns(d)], 0.004, 0.05)
    return lay((blast(0.3), 0.0, 0), (blast(0.15), 0.42, 0), (blast(1.1), 0.62, 0))


def ring_loop() -> np.ndarray:
    """Seamless 3.0 s ring: two steel-drum rings, then silence until the loop point."""
    r = trim(jingle("steel", 10), thresh_db=-55, tail=0.3)
    out = np.zeros((ns(3.0), 2))
    for t in (0.0, 1.05):
        a = r if r.ndim == 2 else np.stack([r, r], axis=1)
        out[ns(t): ns(t) + len(a)] += a[: ns(3.0) - ns(t)]
    return out.mean(axis=1)


PASS2 = {  # id: (channels, builder, source keys, edit note)
    "sfx_buy": (1, lambda: lay((jingle("pizzi", 4), 0, 0), (k_("rpg-audio/Audio/handleCoins2.ogg"), 0.02, -10), ch=1),
                ["kenney_jingles_all", "kenney_rpg"], "Pizzicato 04 (rising) + coins in hand"),
    "sfx_sell": (1, lambda: lay((jingle("pizzi", 5), 0, 0), (k_("casino-audio/Audio/card-slide-1.ogg"), 0, -8), ch=1),
                 ["kenney_jingles_all", "kenney_casino"], "Pizzicato 05 (falling) + a paper slide"),
    "sfx_take_profit": (1, lambda: lay((jingle("sax", 2), 0, 0), (k_("casino-audio/Audio/chips-stack-1.ogg"), 0.05, -6), ch=1),
                        ["kenney_jingles_all", "kenney_casino"], "Sax 02 (rising) + a chip stack"),
    "sfx_cash_in": (1, lambda: lay((k_("rpg-audio/Audio/bookFlip1.ogg"), 0, -2), (k_("rpg-audio/Audio/handleCoins.ogg"), 0.12, 0), ch=1),
                    ["kenney_rpg"], "a paper riffle (notes) + coins in hand"),
    "sfx_new_coin": (1, lambda: lay((pop(1), 0, 0), (jingle("pizzi", 16), 0.05, -2), ch=1),
                     ["pop", "kenney_jingles_all"], "a recorded pop + Pizzicato 16 (rising)"),
    "sfx_dm_ping": (1, lambda: lay((jingle("steel", 9), 0, 0), ch=1), ["kenney_jingles_all"], "Steel 09 (two-note ding)"),
    "sfx_mention": (1, lambda: lay((pop(2), 0, 0), (pop(3), 0.07, 0), (pop(7), 0.14, 0), ch=1), ["pop"], "three recorded pops"),
    "sfx_gm": (1, lambda: lay((jingle("steel", 13), 0, 0), ch=1), ["kenney_jingles_all"], "Steel 13 (gentle)"),
    "sfx_call_ring": (1, ring_loop, ["kenney_jingles_all"], "Steel 10 twice in an exact 3.0 s loop"),
    "sfx_call_connect": (1, lambda: lay((jingle("steel", 15), 0, 0), ch=1), ["kenney_jingles_all"], "Steel 15 (rising)"),
    "sfx_call_end": (1, lambda: lay((jingle("steel", 11), 0, 0), ch=1), ["kenney_jingles_all"], "Steel 11 (falling)"),
    "sfx_rank_up": (2, lambda: trim(read_audio(fetch_member("win_jingle", "WinBrass.wav")), max_s=2.6, fade_out=0.35),
                    ["win_jingle"], "Win Jingle, brass version, faded by 2.6 s"),
    "sfx_graduation": (2, lambda: lay((jingle("hit", 10), 0, -1), (jingle("hit", 11), 0.62, 0)),
                       ["kenney_jingles_all"], "Hit 10 into Hit 11 (rising orchestral hits)"),
    "sfx_news_flash": (1, lambda: lay((k_("impact-sounds/Audio/impactBell_heavy_000.ogg"), 0, 0),
                                      (k_("impact-sounds/Audio/impactBell_heavy_002.ogg"), 0.16, -1), ch=1),
                       ["kenney_impact"], "two bell strikes"),
    "sfx_liquidation": (2, lambda: lay((k_("impact-sounds/Audio/impactBell_heavy_001.ogg"), 0, 0),
                                       (k_("impact-sounds/Audio/impactBell_heavy_003.ogg"), 0.22, 0),
                                       (k_("impact-sounds/Audio/impactBell_heavy_004.ogg"), 0.44, 0),
                                       (fs("221528"), 0.8, -2), (k_("impact-sounds/Audio/impactGlass_heavy_002.ogg"), 0.8, -4),
                                       (k_("impact-sounds/Audio/impactPunch_heavy_000.ogg"), 1.5, 0)),
                        ["kenney_impact", "fs221528"], "alarm bells, glass break, heavy thud"),
    "sfx_rug": (2, lambda: lay((fs("555161"), 0, 0), (k_("rpg-audio/Audio/cloth2.ogg"), 0.1, -2), (jingle("hit", 15), 0.45, 0)),
                ["fs555161", "kenney_rpg", "kenney_jingles_all"], "zipper, cloth yank, Hit 15 (falling orchestral hit)"),
    "sfx_moon_horn": (2, air_horn_blasts, ["fs131930"], "three blasts cut from one air-horn recording"),
    "sfx_space_started": (1, lambda: lay((jingle("pizzi", 12), 0, 0), ch=1), ["kenney_jingles_all"], "Pizzicato 12"),
    "sfx_sunday_unlock": (2, lambda: lay((k_("impact-sounds/Audio/impactBell_heavy_000.ogg"), 0, 0),
                                         (k_("rpg-audio/Audio/metalLatch.ogg"), 1.0, 2),
                                         (k_("impact-sounds/Audio/impactBell_heavy_003.ogg"), 1.3, 0)),
                          ["kenney_impact", "kenney_rpg"], "bell strike, a latch unlocking, bell strike"),
    "sfx_vr_join": (1, lambda: lay((jingle("pizzi", 8), 0, 0), ch=1), ["kenney_jingles_all"], "Pizzicato 08 (up)"),
    "sfx_vr_leave": (1, lambda: lay((jingle("pizzi", 9), 0, 0), ch=1), ["kenney_jingles_all"], "Pizzicato 09 (down)"),
    "sfx_vr_raise_hand": (1, lambda: lay((pop(4), 0, 0), ch=1), ["pop"], "a recorded pop"),
    "sfx_vr_promoted": (1, lambda: lay((jingle("pizzi", 10), 0, 0), ch=1), ["kenney_jingles_all"], "Pizzicato 10 (rising)"),
    "sfx_ui_open": (1, lambda: lay((k_("casino-audio/Audio/card-slide-2.ogg"), 0, 0), ch=1), ["kenney_casino"], "a card slide"),
    "sfx_ui_close": (1, lambda: lay((k_("casino-audio/Audio/card-place-2.ogg"), 0, 0), ch=1), ["kenney_casino"], "a card placed down"),
    "sfx_whale_horn": (1, lambda: trim(fs("181949", mono=True), max_s=2.6, fade_out=0.6), ["fs181949"], "fog horn, faded by 2.6 s"),
    "sfx_pnl_card_flip": (1, lambda: lay((k_("casino-audio/Audio/card-fan-1.ogg"), 0, -3), (k_("casino-audio/Audio/card-place-1.ogg"), 0.13, 0), ch=1),
                          ["kenney_casino"], "a card fan swish + card snap"),
}
MULTI = {  # id: (variants, builder(v), source keys, edit note)
    "sfx_ore_clink": (5, lambda v: lay((k_(f"impact-sounds/Audio/impactMining_00{v}.ogg"), 0, 0),
                                       (k_(f"casino-audio/Audio/chips-collide-{v % 4 + 1}.ogg"), 0.03, -6), ch=1),
                      ["kenney_impact", "kenney_casino"], "pickaxe-on-ore hit + clay chips clinking"),
    "sfx_ore_furnace_whoosh": (2, lambda v: trim(fs("447941" if v == 0 else "459884", mono=True), max_s=2.2 + 0.4 * v, fade_out=0.5),
                               ["fs447941", "fs459884"], "flamethrower recordings, faded"),
}


def fetch_member(key: str, member: str) -> Path:
    fetch(key)
    p = DL / Path(SOURCES[key]["file"]).parent / Path(SOURCES[key]["file"]).stem / member
    if not p.exists():
        with zipfile.ZipFile(DL / SOURCES[key]["file"]) as z:
            z.extract(member, p.parent)
    return p


def pass2(rep: dict) -> None:
    for sid, (ch, build, keys, edit) in PASS2.items():
        x = build()
        loop = sid == "sfx_call_ring"
        info = export_wav(OUT / "sfx" / f"{sid}.wav", x, peak_db=-1.0, channels=ch, loop=loop)
        rep[sid] = {"sources": keys, "edit": edit + "; 48 kHz; peak-normalised to -1 dBTP", "files": [info]}
    for sid, (nv, build, keys, edit) in MULTI.items():
        files = [export_wav(OUT / "sfx" / f"{sid}_{v + 1:02d}.wav", build(v), peak_db=-1.0, channels=1) for v in range(nv)]
        rep[sid] = {"sources": keys, "edit": edit + "; 48 kHz; peak-normalised to -1 dBTP", "files": files}
    print(f"pass 2: {len(PASS2) + len(MULTI)} cues rebuilt from recordings")


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
    pass2(rep)
    (TOOLS / "build_real.json").write_text(json.dumps(rep, indent=2))
    prov = {k: {kk: (list(vv) if isinstance(vv, tuple) else vv) for kk, vv in v.items()} | {"checked": v.get("checked_on", CHECKED)}
            for k, v in SOURCES.items()}
    (ROOT / "sources" / "real-audio-20261009.json").write_text(json.dumps(prov, indent=2) + "\n")


if __name__ == "__main__":
    main()
