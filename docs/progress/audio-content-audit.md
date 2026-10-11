# Audio content and device evidence audit — 2026-10-05

**Status:** the assets pass the sample-clipping and length checks below. Their musical taste, sound-design fit, audible seams and device mix have not been listening-approved. This agent could not actually listen. Device logs establish runtime state only.

This audit did not change audio assets, UI or project settings and did not launch Unity or operate the device. A separate, narrowly authorized correction in `MusicDirector.cs` implements Spaces Studio's explicit voice-only requirement. Root owns device operation and provided the build-G log.

## Build-G device evidence

Source: [saved device log](screens/quality-review-20261005g/device.log), `review-audio` lines 530 and 605; `review-check` lines 523, 575, 590.

| Check | Observed evidence | Scope of conclusion |
| --- | --- | --- |
| Listener count and placement | Both snapshots report `listenerActiveCount=1`, named `player_listener`. After walking: listener `(0.00,1.78,-0.07)`, player root `(0.00,0.08,-0.04)`, anchor `(0.00,1.78,-0.04)`, anchor error 0.028 m. After return: error 0.000 m. | One active listener follows the player head anchor in these two snapshots. This does not measure perceived spatial audio. |
| Local footsteps | Arrival snapshot reports `footstepsPlayed=9`; return snapshot remains 9. Both report `footstepsPlaying=0/4`. | Nine local Play calls occurred during navigation; the rapid venue replacement added no step. This is not a long idle/no-spam test or proof of audible foot contact. |
| Settled initial audio | Timeline ambience weight 1.000, other source stopped/empty; one music bank `timeline`, weight 1.000, five playing sources with clips. | Initial requested content is loaded and playing in the expected bank. |
| Rapid return / fade state | Ambience weights Timeline 0.983 and Terminal 0.017, both playing. Music has two loaded banks: Timeline 1.000 plus Trenches 0.000, with incoming scheduled start 0.157 s ahead. `musicTransitions=5`. | The snapshot shows bounded two-bank transition state. It does not establish audible smoothness or final cleanup after the fade. A snapshot at least 1.5 s later is still needed. |
| SFX live mute | Both snapshots have SFX gain 1.000 and `sfxPlaying=0/8`. | No active long effect was muted in these snapshots; the live-mute correction is not device-verified here. |
| Navigation / venue lifecycle | Root logs PASS for offline walk (remaining 0.09 m), five-map binding/unload/reload, venue round trip, camera target and returned-player navigation. | These are the recorded automated assertions; they do not validate audio content quality. |

No snapshot here tests the −6dB voice duck, Spaces Studio silence, long idle cadence, a final settled fade state or human listening. Do not report all audio acceptance checks passed from these two log lines.

## Build-H device evidence — follow-up

The coordinating agent ran the enhanced review on the Seeker. [Build-H device log](screens/quality-review-20261005h/device.log), lines 576–684, now supplies the settled-state and active-mute evidence missing from build G. This section records engine state; no listening test was performed by this agent.

- **One player listener:** every recorded H audio snapshot reports `listenerActiveCount=1`, `listener=player_listener` and `listenerAnchorError=0.000`. The post-travel position is `(0.00,1.78,-14.00)`, aligned with the head anchor above player root `(0.00,0.08,-14.00)`.
- **Movement-driven footstep calls:** after the run and two short walks, `footstepsPlayed=13` and `footstepsPlaying=0/4`. The count remains 13 across immediate return, the 2.017-second settled return and the mute test. This supports absence of additional step calls during that observed idle/venue-replacement interval. It does not establish audible foot-contact timing.
- **Transition cleanup:** at 22:45:49.378, the rapid return has two music banks and both ambience sources populated. At 22:45:51.395, Timeline ambience is playing at weight 1.000; its outgoing source has `clip=none`, weight 0.000 and `playing=False`. Music has exactly one Timeline bank at weight 1.000 with five playing clips; the outgoing bank has `key=none`, zero weight, zero playing sources and zero clips. This verifies cleanup for this exercised transition.
- **Active source gain mute:** `mute-before` at 22:45:51.399 reports `sfxVolume=0.600`, `sfxPlaying=1/8`, maximum playing source gain 0.600. `mute-active` at 22:45:51.413 still reports one playing source, with master and source gain both 0.000. The review logs PASS for `activeBefore=1, activeMuted=1`; `finally` restores the exact prior volume, 1.000, at 22:45:51.414. This verifies a source stays active while its gain reaches zero; it is not a hearing claim.

The accompanying movement checks distinguish actual running from walking: run maximum 4.50 m/s, 176 facing samples, minimum horizontal face/velocity dot 0.869; sideways walk 2.20 m/s, 41 samples, minimum dot 0.935; return walk 2.20 m/s, 64 samples, minimum dot 0.610. All exceed the authored-character direction threshold of 0.3 after the turn-settling interval. Actual captures: [mid-run](screens/quality-review-20261005h/quality-review-20261005h/city-mid-run.png) and [sideways short walk](screens/quality-review-20261005h/quality-review-20261005h/city-side-walk.png). These images and direction logs are presentation/movement evidence, not crowd-performance measurements.

Voice ducking, a visit to voice-only Spaces Studio, prolonged idle behavior and an actual listening/mix review remain outside this recorded H check. The interrupted build-G crowd run has no summary and is not a valid sustained-performance result.

## Measured source material

Decoded all 42 music files (35 runtime stems plus 7 previews) and 9 ambience files to 48 kHz stereo float PCM with ffmpeg, then measured sample values with numpy. Every file's SHA-256, duration, RMS, peak, clipping count, per-channel seam rank and half-loop energy correlation are preserved in [the machine-readable measurements](audio-content-metrics-20261005.json). These are supplied source-file measurements, not Unity-imported playback.

- All 51 files have zero samples at or above full scale, and zero at or above 0.999 absolute amplitude. This does not test clipping in the combined game mix.
- All seven sets have exactly matching lengths for their five stems and match declared `loop_samples`.
- Seven previews measure −17.0/−17.1LUFS. Their directly decoded sample RMS ranges −19.854…−18.695dBFS; sample peaks −4.799…−2.157dBFS.
- Ambience comprises eight 30-second loops plus the 34.286-second club-door loop. Integrated levels are −24.0/−24.1LUFS, except Spaces Studio at −36.2.
- The existing true-peak report should not be the only peak check: the clinic file has a direct PCM/`astats` sample peak −5.284dBFS while this installed ffmpeg `ebur128` summary reports −8.7 for both sample and true peak. The cause was not diagnosed. This audit does not certify a true-peak ceiling from that summary alone.

### Music preview measurements

| File | Loop (s) | Sample RMS (dBFS) | Sample peak (dBFS) | EBU integrated (LUFS) |
| --- | ---: | ---: | ---: | ---: |
| `mus_airdrop_farm_full.ogg` | 42.667 | -19.85 | -2.42 | -17.1 |
| `mus_builder_hills_full.ogg` | 40.421 | -19.48 | -4.80 | -17.1 |
| `mus_club_full.ogg` | 34.286 | -19.08 | -2.16 | -17.0 |
| `mus_dubai_marina_full.ogg` | 34.909 | -18.99 | -4.39 | -17.0 |
| `mus_perp_pit_full.ogg` | 30.000 | -18.70 | -3.40 | -17.1 |
| `mus_timeline_square_full.ogg` | 38.400 | -19.20 | -2.99 | -17.1 |
| `mus_trenches_full.ogg` | 27.429 | -19.02 | -2.38 | -17.1 |

### Ambience measurements

| File | Loop (s) | Sample RMS (dBFS) | Sample peak (dBFS) | EBU integrated (LUFS) |
| --- | ---: | ---: | ---: | ---: |
| `amb_airdrop_farm.wav` | 30.000 | -29.12 | -13.23 | -24.0 |
| `amb_club_doors.wav` | 34.286 | -25.51 | -6.44 | -24.0 |
| `amb_dubai_marina.wav` | 30.000 | -26.23 | -7.85 | -24.0 |
| `amb_ore_refinery.wav` | 30.000 | -26.58 | -12.00 | -24.0 |
| `amb_perp_pit.wav` | 30.000 | -26.79 | -9.44 | -24.0 |
| `amb_rekt_clinic.wav` | 30.000 | -25.98 | -5.29 | -24.1 |
| `amb_spaces_studio.wav` | 30.000 | -36.85 | -22.93 | -36.2 |
| `amb_terminal_cafe.wav` | 30.000 | -28.39 | -3.64 | -24.0 |
| `amb_timeline_square.wav` | 30.000 | -26.58 | -8.63 | -24.0 |

## The content-quality gap

[The music generator](../../audio/tools/gen_music.py) authors one 16-bar loop per district, entirely through synthesis. Its Timeline chord progression repeats after 8 bars (19.2 seconds). The decoded full mix's first/second-half RMS-envelope correlation is 0.9703; the drums are 0.9946. Those correlations measure repeated energy shape, not whether the music is good. Over five minutes, Timeline's file cycles 7.81 times and its ambience cycles 10 times. [The original delivery log](audio.md) explicitly records no human audition, no real musical performance, and no producer's-ear review for the named genres.

[The ambience generator](../../audio/tools/gen_ambience.py) selects automatically steady stretches of field recordings and collapses them into one 30-second stereo mix. Its selection was based on metadata and level stability, not listening. The original Timeline exterior-crowd source lasts 47.70 seconds and the second busy-square recording 120.26 seconds, so longer material exists locally. Intelligible conversation, source fit and distracting recurrent details have not been auditioned. Changing listeners or adding crossfades does not add independent localized sound design to a single mixed ambience bed.

The −17LUFS previews are not what neutral gameplay plays. Reconstructing Timeline's actual settled neutral weights (`pad1 + bass1 + drums0.15625`) at default master 0.7 gives −22.4LUFS. Green full stems at that master give −20.1LUFS. Red before Unity's low-pass gives −21.3LUFS; the low-pass was not modelled. This excludes ambience, UI, NPCs and LiveKit, so it does not determine the runtime master loudness.

## Loop-seam finding

[The original QA script](../../audio/tools/check_audio.py) downmixes to mono before ranking the wrap step. A channel-by-channel measurement flags `mus_club_tension.ogg`: L/R boundary jumps −29.245/−33.443dBFS, at the 99.9959/99.9991 percentile among each channel's sample steps. The original mono check reports 96.103 percentile. Perp Pit tension's right channel is 99.9949 percentile at −35.497dBFS. These are review flags, not proof of an audible click; the red-market low-pass may attenuate them. Ambience boundary jumps rank at most 92.6562 percentile per channel. The Unity-compressed loops still require device listening.

## Recommended next content improvement

Make Timeline's music the first actual content replacement: an auditioned 100 BPM lo-fi cue with recorded/performed timbres and a 64-bar (153.6s) arrangement—sparse A, richer B, breakdown, varied return. Preserve the current pad/bass/drums/lead plus tension stems and IDs ; 64 bars at 100 BPM means 7,372,800 samples per stem at 48 kHz. This is a production brief, not a claim that a suitable licensed replacement is already sourced or sounds better. Follow the existing licence rules; no new synthetic batch or runtime framework is needed.

Compare neutral/green/red versions with crowd/UI/voice on phone and headphones. Select on timbre, groove, repetition and speech clarity, then inspect two complete loop boundaries. A shorter reviewed cue can be preferable to a longer poor one ; 64 bars is a proposed variation target, not a quality guarantee. Extend/re-edit ambience from the existing longer recording only after listening confirms that it fits the plaza.

## Corrected explicit spec mismatch

[Build Spec, Sound design](../spec/build-spec.md), line 705: `Spaces Studio | None | Voice only`. Registries places Spaces Studio in Timeline; the previous district fallback therefore selected Timeline music. The authorized narrow correction makes `MusicDirector.SetMusic` select no music for Spaces Studio before venue/district/club fallbacks, using the existing fade-out.

Audio and World compile. The mocked regression passed 657 assertions including 13 new checks: ordinary-venue district fallback, studio rejection of district/club music, every source stopped/cleared after fade, and district music restored when leaving. Scratch test artifact: `/var/folders/zm/v0w94xb95p11_5df6rxm0js80000gp/T/ctlife-studio-audio-check-bl0qp9xs`. The build-G audio snapshots above do not themselves exercise Spaces Studio.

## Build-G crowd measurement

The requested summary at `screens/seeker-authored-crowd-20261005g/summary.json` was not present during the first audit read. No waiting loop or device operation was started. Its results must be reviewed separately from the two audio snapshots and from the earlier build-D placeholder-crowd measurements.
