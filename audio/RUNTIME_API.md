# CT Life audio — runtime API (CTLife.Audio)

Stable contract for every lane that makes sound. Owner: audio integration lane. Changes are **additive only**
(no renames, no removed members); anything new is listed under "Changelog" at the bottom.
Code: `client/Assets/Scripts/Audio/`. Rules behind the numbers: `docs/spec/build-spec.md` "Sound design",
`audio/README.md`, `audio/BABBLE.md`.

## 1. Two ways to make a sound

| Your assembly | Do this | Needs a reference to CTLife.Audio |
| --- | --- | --- |
| Any (Money, Social, Careers, CityLife, UI, Net…) | `Bus.Publish(new SfxSignal("sfx_buy"));` | No (CTLife.Core only) |
| World, UI, App, WorldLife (already reference it) | `Sfx.Play(SfxIds.Buy);` and the facades below | Yes |

Both end in the same `AudioManager` call. Every API is safe before the audio runtime exists, after it is destroyed and
with unknown ids: it does nothing (an unknown id plays a quiet blip once and logs once, contract §2 rule). Nothing
throws into your code.

- `SfxSignal.volume` multiplies the clip's mapped level (0..2). `0` (the struct default) means `1`.
- Same id twice inside its cooldown plays once (see §3). Publish freely from both a card and a controller; you will
  not get a double hit.
- A loop id (`sfx_call_ring`) sent as a signal is a **keep-alive**: it plays one more cycle (clip length + 0.5 s) after
  the last signal. Re-publish it every cycle while ringing (CallController does, every 3 s); `sfx_call_connect` or
  `sfx_call_end` stops it at once.

## 2. Facades (namespace `CTLife.Audio`)

```csharp
// One-shots and loops
Sfx.Play(string id, float volume = 1f);
Sfx.PlayAt(string id, Vector3 position, float volume = 1f);        // 3D, linear roll-off 1.5–12 m
Sfx.PlayPitched(string id, float semitones, float volume = 1f);    // e.g. draw hop tick +1 st per hop (±12)
Sfx.StartLoop(string key, string id, float maxSeconds = 45f);       // explicit loop with a hard timeout
Sfx.StopLoop(string key);
bool Sfx.Has(string id);
Sfx.SetLocalHandle(string handle);   // optional: @handle for mention cues (default: PlayerProfile.Alias)

// Music (adaptive) and ambience
Music.PlayTitle();                   // CT Life main theme `mus_title`: title + onboarding (ignores market mood)
Music.SetVenue(string venueId, string districtId, bool isClub = false); // ambience + music + footstep floor
Music.SetDistrict(string key);       // "title", "lobby", district id, venue id, "clubs" or an id ("mus_title"); null fades out
Music.SetMood(float mood);           // −1 red … +1 green (normally automatic from MoodSignal)
Music.SetMoodBps(int bps);           // SOL 24h change in bps; ±500 saturates
Music.Stop();

// Footsteps
Footsteps.Attach(GameObject avatar, float gain = 0.8f);  // remote players and NPCs (not the local player)
Footsteps.SetSurface(string surface);                     // force the local surface (rug zone…); null = automatic
string Footsteps.SurfaceAt(Vector3 position);

// Ducking (music bus)
Ducking.Hold(string key, float db);          // e.g. ("call", -12) during a video call
Ducking.Release(string key);
Ducking.SetVoiceActive(string source, bool talking);  // −6 dB while `source` ("voice", "spaces", "call") has speech
```

`AudioManager.Instance` stays public for existing callers: `Play`, `PlayAt`, `PlayFootstep(surface, pos)` (surface
`null` = automatic), `PlayOtherFootstep`, `SetAmbience`, `SetVenue`, `Music` (`MusicDirector`: `SetMusic`,
`SetDistrict`, `SetMood`), `BabbleBankFor`, `Map`, `Clip`, `RegisterCameraListener`, `BindPlayerListener`,
`ReleasePlayerListener`, settings (§6).

Core signals the audio runtime already listens to: `SfxSignal`, `MoodSignal` (city room mood → music),
`VoiceActivitySignal` (→ −6 dB duck), `VenueChangedSignal` (null → stop venue audio). Realtime messages it voices on
its own (assembly `CTLife.Audio.Cues`): `dm` → `sfx_dm_ping`; `call_invite` → ring; market `tape` print ≥ 10×
`tapeThresholdCents` → `sfx_whale_horn`; venue `said` "gm" → `sfx_gm`, `@you` → `sfx_mention`; `payment_confirmed` →
`sfx_cash_in`.

## 3. Sound ids, cooldowns and who fires them

Constants: `SfxIds.*`. Registry aliases resolve automatically (`sfx_ui_toggle`→`sfx_ui_toggle_on`,
`sfx_pnl_flip`→`sfx_pnl_card_flip`, `sfx_sunday_bell`→`sfx_sunday_unlock`, `sfx_room_join/leave`→`sfx_vr_join/leave`,
`sfx_raise_hand`→`sfx_vr_raise_hand`, `sfx_promoted`→`sfx_vr_promoted`).

Cooldown = minimum spacing of the same id: ui 0.06 s, trading 0.05 s, phone 0.25 s, voice room 0.15 s, stinger 1.2 s;
own: whale horn 20 s, new coin 1.5 s, gm 12 s, DM 0.6 s, mention 1 s, cash-in 1 s, Space started 5 s, room join/leave 0.4 s.
A trading/stinger/phone/voice-room/ORE/news cue masks a `sfx_ui_toast` tick started within 0.2 s (one event, one sound).

| id | when | trigger path today |
| --- | --- | --- |
| `sfx_ui_tap` `sfx_ui_open` `sfx_ui_close` `sfx_ui_success` `sfx_ui_error` `sfx_ui_toast` `sfx_ui_toggle_on` | UI feedback | UI (`El.Btn` default), PhoneShell, ToastLayer, apps |
| `sfx_ui_toggle_off` | a toggle switched off | **requested** (UI) |
| `sfx_buy` `sfx_sell` | trade filled | Money (PulseApp, PerpPitApp, PredictionPitApp) |
| `sfx_take_profit` | a sell realises profit | **requested** (Money) |
| `sfx_pnl_card_flip` | PnL / REKT card opens | **requested** (Money) |
| `sfx_new_coin` | coin launched | UI incident card (`evt_coin_launch`); market add **requested** (Money) |
| `sfx_whale_horn` | tape print ≥ $10k Cash [Inference threshold] | audio bus subscription (`tape`) |
| `sfx_cash_in` (client-only) | top-up credited | audio bus subscription (`payment_confirmed`) |
| `sfx_liquidation` (stinger) | you were liquidated | RektCard / MoneyRektCard / incident card (deduped) |
| `sfx_rug` (stinger) | rug / exploit event | incident card, lottery |
| `sfx_graduation` (stinger) | coin you hold/watch graduates | **requested** (Money, MarketFeed stage change) |
| `sfx_moon_horn` (stinger) | moon / ORE motherlode | incident card, lottery |
| `sfx_rank_up` (stinger) | promotion | JobsApp, Careers ShiftRunner |
| `sfx_sunday_unlock` (stinger) | Sunday Unlock reached | **requested** (CityLife week clock) |
| `sfx_dm_ping` | DM received | audio bus subscription + Social MessagesController (deduped) |
| `sfx_mention` | someone @mentions you | audio bus subscription (`said`) |
| `sfx_call_ring` (loop) | incoming call / ringback | audio (invite) + Social CallController keep-alive |
| `sfx_call_connect` `sfx_call_end` | call joined / ended | Social CallController |
| `sfx_space_started` | a followed Space goes live | **requested** (Social) |
| `sfx_vr_join` `sfx_vr_leave` `sfx_vr_promoted` | Spaces room changes | Social SpacesController |
| `sfx_vr_raise_hand` | a hand goes up | **requested** (Social) |
| `sfx_ore_clink` | ORE stake sent | Money OreRefineryApp |
| `sfx_ore_furnace_whoosh` | ORE round rolls over / furnace flash | **requested** (Money, WorldLife) |
| `sfx_news_flash` (client-only) | "JUST IN" news card / Town Crier | Town Crier babble signature; incident card default **requested** (UI) |
| `sfx_rekt_phonk` (client-only stinger) | REKT clip replay / share (phonk-edit culture) | **requested** (Money REKT card share) |
| `sfx_origin_insider` (stinger; `sfx_origin_seeker` is an alias) | origin reveal: Insider / Seeker — brass fanfare of the theme hook | **requested** (UI: `"sfx_origin_" + outcomeId`) |
| `sfx_origin_exit_liquidity` (stinger) | origin reveal: Exit Liquidity — cartoon bonk, deflate, hopeful hook tag | **requested** (UI) |
| `sfx_creator_complete` (stinger) | end of character creation — hook fanfare (~1.2 s) + ring-out | **requested** (UI) |
| `sfx_ui_next` | onboarding step complete — rising two-note chime (D6→G6) | **requested** (UI `Kit.Cues.SfxStep`) |
| `sfx_card_shuffle` | origin draw — two card riffles (Kenney CC0) | **requested** (UI `LotteryScreen.Shuffle`) |
| `sfx_gm` (client-only) | someone says "gm" in the venue | audio bus subscription (`said`) |

Exact diffs for every **requested** row: `docs/progress/requests.md` → "2026-10-08 audio-integration -> …".

## 4. Music

- `title` (`mus_title`, CT Life main theme): 32 bars, G major, 112 BPM, 68.6 s seamless loop, own work. Title screen and
  onboarding. `mood_reactive: false`: plays the plain track whatever the market does; the remembered mood fades in over
  two bars once a reactive set (lobby, venues) takes over. The origin stingers and the creator jingle quote its hook in
  the same key, so they sit over the ducked loop.

- A set is either five sample-locked stems (`pad`, `bass`, `drums`, `lead`, `tension`; today: `clubs`) or a complete
  licensed composition plus `stems.tension` (a key/tempo-neutral red-market bed; today: every other set).
- Selection: venue set → `clubs` for club venues → district set; `spaces_studio` = voice only (no music).
- `market_mood` (−1..+1) → `MoodMix` (unit-tested): pad and bass always on; drums fade in 0 → +0.5; lead fades in
  +0.35 → +0.5; tension fades in 0 → −0.5; low-pass open at ≥ 0, 20 kHz → 2.5 kHz (0 → −0.5) → 900 Hz (−1).
  Full mixes stay intact (green = neutral = the track); red low-passes them and adds the tension bed.
- New moods switch on the next bar line and fade over two bars (2–8 s). A new set starts in the current mood.
- Stems start on one DSP tick after every clip has loaded (3 s timeout); a drift guard re-locks any stem > 1024
  samples off. Venue changes crossfade 1.2 s; lobby → Timeline keeps the shared track's phase.
- Music bus = Music setting × duck. Ducks: voice / Space speakers / call −6 dB; stingers −9 dB for their length;
  deepest wins (no stacking); attack ≈ 75 dB/s, release ≈ 12 dB/s.

## 5. Footsteps and babble

- **Surface** (priority): `FootstepSurface` component on the ground collider or a parent → physics material name →
  renderer material name → object name ("Floor_WoodPlanks", "pm_metal_grate", "Terrain") → venue floor default
  (`sfx-map.json` `surfaces.venues`) → concrete. Values: `concrete wood metal_grate carpet neon_tile grass`.
  World/venue builders: add `FootstepSurface` to floor patches, or name materials with those words.
- **Cadence** follows speed: stride 1.463 m up to 2.2 m/s, → 1.508 m at 4.5 m/s (steps/s = speed / stride).
- **Budget** for other avatars (`FootstepBudget`): silent beyond 12 m; every second step beyond 6 m; ≤ 6 voices;
  ≤ 10 step starts per 0.25 s; a nearer step steals the farthest voice. The local player has its own 4 voices.
- **Babble** (`BabblePlayer.Say(text)` with `BabbleBankFor(archetype)`): audio/BABBLE.md rules in
  `BabbleScheduler`; per-NPC pitch from crc32(npcId); ≤ 3 NPCs babbling near the listener (nearest win, a nearer line
  cuts the farthest); one line per NPC per 1.5 s; silent beyond 15 m (linear roll-off from 2 m). `Say` always returns
  the line length for bubbles, even when the budget keeps it silent. Babble is on the SFX bus. Optional archetype
  signature (`babble.<id>.signature` in sfx-map.json: Town Crier → `sfx_news_flash`, Giveaway Guy → `sfx_take_profit`,
  Engagement Farmer → `sfx_new_coin`) plays quietly at the NPC as a line starts, at most once per 30 s per NPC.

## 6. Settings

`AudioManager.Instance.masterVolume / musicVolume / sfxVolume / voiceVolume / ambienceVolume` (0..1, persisted as
`ctlife.audio.<key>`; defaults 1, 0.85, 1, 1, 0.75 — music raised from 0.7 on 2026-10-09). Gain = slider² (50 % ≈ −12 dB). Master is `AudioListener.volume`
(covers LiveKit too). `voiceVolume` must be applied by the voice owners to their LiveKit playback sources:
`AudioManager.Instance.BusGain(AudioBus.Voice)`, refreshed on `AudioManager.SettingsChanged` (requested: Net/Social).

## 7. Shipping and import

- Only files referenced by `audio/sfx-map.json` ship. After any generator run:
  `audio/tools/ship_audio.py --archive --sync` (moves unreferenced files to `audio/archive/`, mirrors the shipping set
  into `client/Assets/Resources/Audio/`, prints the estimated Android footprint). `--check` fails when out of sync.
- Android import settings come from `ImportPolicy` (unit-tested) via `CTLife.Audio.Editor/AudioImportPostprocessor`
  (order 100, after the general content postprocessor). Menu: CT Life → Audio → Reimport Audio With Policy.

UI cue levels are audited against these defaults: `audio/tools/mix_audit.py` prints, for each onboarding cue at its
call-site volume, the margin over `mus_title` in the 1–6 kHz band (targets +6 dB actions, +3 dB ticks; peaks ≤ −1 dBFS).

## 8. Tests

`client/Assets/Scripts/Audio/Tests~/run_audio_tests.sh` (no editor) or Unity Test Runner → EditMode →
`CTLife.Audio.Tests`. Compile every audio assembly out of the editor:
`python3 client/Assets/Scripts/Audio/Tests~/compile_audio.py [--editor] [--release]`.

## Changelog

- 2026-10-09 — `Music.PlayTitle()`, `Music.Title/Lobby`, `SetDistrict` accepts ids, `mood_reactive: false` sets;
  `Sfx.PlayPitched`; ids `sfx_origin_insider` (+ alias `sfx_origin_seeker`), `sfx_origin_exit_liquidity`,
  `sfx_creator_complete`, `sfx_ui_next`, `sfx_card_shuffle`; UI cue levels re-mastered; music default 0.85.
- 2026-10-08 — v1: facades `Sfx`, `Music`, `Footsteps`, `Ducking`; `SfxIds`; settings master/voice; loops; budgets;
  `CTLife.Audio.Cues` realtime subscriptions; client-only ids `sfx_news_flash`, `sfx_gm`, `sfx_cash_in`, `sfx_rekt_phonk`.
