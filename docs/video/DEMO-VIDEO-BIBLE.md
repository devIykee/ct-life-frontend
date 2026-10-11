# CT Life: demo video bible (18 Oct 2026)

This is the production plan for the Solana Mobile hackathon submission video and the waitlist cut. It's written from
an actual frame-by-frame study of the user's reference, not from memory. The lead (coordinator) owns taste and
continuity; agents execute. **Taste over speed. No cut that isn't motivated by motion. No beat without an event.**

Inputs on disk:
- Reference video: `ct-life-evidence/video-reference/fomo-amirdzm.mp4` (20 s, 1920×1080, 30 fps), with frame sheets in
  `sheets/`, `big/` and `f/` beside it.
- Prompting method: X post by @twoclipping (2103273003555402193), captured in §2. "Every state is the same element
  morphing, never cut", built as a pure function of time.
- The user's brief (verbatim excerpts at the end).

---

## 1. What the reference actually does (fomo "introducing", by @amirdzm's team)

The posted file is a **2×2 grid** of four panels from the same film playing out of phase. Reading across all four
panels gives the full piece. What makes it premium:

**A. One world, one colour field.** The whole film lives in a single saturated periwinkle/indigo field (≈ #6670EE to
#8C93F5) with soft radial light and faint concentric rings. The only breaks are (1) a deep-navy "night" macro world
for the hero chart and (2) one pure-white flash scene ("easier"). Three palettes in total, and they alternate as a
rhythm.

**B. The mascot is the transition.** fomo's 3D coin (a thick white-rimmed disc with two dark "eyes") is the editor:
- it **flies in and lands on the last letter** of "introducing" (as a dot); it **rides the end of a swoosh ribbon** carrying "just got"
- it **spins in extreme close-up past the camera**, filling the frame, and the next scene is revealed behind it (an object-wipe)
- it **orbits the UI cards** in depth
- it **bounces on the beat** beside "easier"

There are almost no plain cuts. When a scene changes, something moves across the frame, scales through the camera,
or the background colour floods.

**C. Kinetic lowercase type, one word idea per beat.** "introducing" → "just got" → "easier" → "replay share cards".
It uses a clean geometric sans (Satoshi / General Sans class), lowercase, white on the field or black on white. Each
word has a **visual pun object**: a feather for "easier", sparkle stars and ripple rings for "replay share cards", a
swoosh for "just got".

**D. Product UI as physical objects in 3D.** UI cards (a tweet-style thesis card, the PnL card, "Top Traders") float
as thick glass slabs in 3D space:
- the camera dollies and tilts around them
- coins orbit them with motion blur
- there are macro close-ups at extreme angles with shallow depth of field (the "$75K Invested / Avg entry $9.42M" shot)
- the phone mockup sits centred and its screen content plays live (the chart redraws, numbers tick)

**E. Hero macro shot.** On a dark navy device edge with a strong blue rim light, a **glowing green chart line draws
itself** with a soft trail, at shallow depth of field. It's the emotional anchor, and it returns several times.

**F. Payoff stack.** Social proof as a ranked card ("1 · Unipcs @unipcs", a gold medal), then a **carousel of token
coins rolling across black** (BTC, ETH, SOL, …) as the ecosystem beat.

**G. Motion grammar** (what to copy):
- Springs with a tiny overshoot; nothing linear, nothing bouncy.
- Motion blur on everything fast (coins, whips).
- Depth of field on 3D shots; foreground objects pass close to the lens.
- Every 0.5–1.0 s something new happens. There's no dead time.
- The camera is always moving: slow dolly-ins, orbits, a rack focus from card to coin.

**H. Its length**: about 20 s of loopable highlights. Ours is longer (§4), but the density per second must match.

## 2. The @twoclipping method (how to build it as code, which suits us)

The key ideas, adopted as our build rules:
- **One element, never cut.** A shape morphs its size, radius, colour and content between UI states. Its contents
  swap with a short blur. A cursor (for us, a finger tap ripple) drives every change.
- **A pure function of time**: `seek(t)` computes every style. No CSS transitions or timers. Springs are closed-form
  step responses; a value with many targets is the sum of one spring per change. Drags are direct manipulation.
- **Beat grid first**: analyse the song (numpy/librosa) for the beat grid, start on a downbeat, and **place every SFX at
  its measured peak**.
- **Render** with Playwright: 4 sub-frames per frame blended with ffmpeg `tmix` for motion blur, at 60 fps. Render one
  frame per beat as a check first, and fix anything off-grid, cramped or hard to read.
- **Gotchas**:
  - No `will-change` on scaled elements (it makes text blurry).
  - Text that swaps inside a morphing container needs its own enter and exit timing.
  - Loops must match the first and last frame exactly.
- **Banned**: bouncy easing, particle bursts for their own sake, glows on UI chrome, mismatched icon strokes, dead time, template looks.

**Our hybrid pipeline:**
- The UI and type layer is HTML/canvas code rendered this way: the Duo apps, title, lobby UI and every morph.
- Game footage and 3D are captured from Unity (device and editor) and composited.
- After Effects (AE MCP, if available; verify with ToolSearch) is used only for 3D camera compositing, depth of
  field, grading and final assembly. If AE isn't reachable, do the composite in code too (WebGL or Three.js for 3D
  UI slabs, ffmpeg for compositing).

## 3. CT Life's visual system for the film (derived from the game; don't invent a new brand)
- **Field colours (three worlds, alternating like the reference)**:
  1. **Golden-hour world**: the game's own warm look (key art B, plaza). Gameplay lives here.
  2. **Plum field**: `#2a0b4a` to `#4a1d7a` radial, with faint concentric rings. Type and UI slabs live here (our
     version of fomo's periwinkle).
  3. **White flash**: pure `#ffffff` with black type, used 2–3 times at most, for punchlines.
  - Accent: mint `#3ddc84` (gains, ENTER THE CITY), red `#ff3b5c` (rekt / debt), yellow `#ffd23f` (the Dream, medals).
- **Type**: **Lilita One** for hero words (our game voice, chunky, with the plum outline on footage). **Inter** for
  secondary. Lowercase for supporting words ("you just got…"), Lilita caps for punch words ("REKT", "WAGMI", "ENTER").
- **Mascot / transition object** (our "coin"): the **CT mark** as a thick 3D coin (glossy green T over the blue/purple
  C, white rim). Build it in Blender from `art/branding/logo/ct-mark-transparent-1254.png` (extruded, bevelled, 2 mm
  rim, glossy). It does everything fomo's coin does: lands as a dot, rides swooshes, wipes scenes, orbits cards.
  **The secondary object** is the Solana Duo phone (the hardware leaf from the game) as a 3D prop.
- **Hero macro**: a **glowing mint PnL chart line** drawing itself across a dark Seeker-like device edge with a rim
  light, our version of beat E. Use real numbers from a fixture account.
- **UI slabs**: real Duo screens captured clean (no HUD), extruded as glass slabs (12 px thick, rounded, soft shadow,
  rim light) in 3D.

## 4. Script on a beat grid (master: 16:9, 3840×2160, 60 fps; about 64 bars at 124 BPM, about 2:04, trimmed to the ~85 s "core cut" below)

Pick the track first (§6). The bars below assume **124 BPM** (1 bar = 1.935 s). Shift everything to the real grid
after beat analysis. ★ marks a hit on the downbeat.

| Bars | Time | Shot | What happens (motion) | Transition out |
|---|---|---|---|---|
| 1–2 | 0:00–0:04 | **Night macro** | A dark Seeker edge with a rim light. A mint chart line draws left to right, dips red (a liquidation tick), then rips green. The shot has shallow DoF and a slow dolly-in. | ★ The CT coin flies through the lens (blur), revealing the plum field |
| 3 | 0:04–0:06 | Plum field | "introducing" types in (Inter, lowercase); the CT coin lands as the dot on the i | The coin flips and its face fills the frame (a coin-wipe) |
| 4–5 | 0:06–0:10 | **Title key art** | Key art B pushes in slowly; the CT LIFE lockup assembles (C slides, T drops ★); the loading bar fills **in four beats**; "TAP TO PLAY" pulses | A finger-tap ripple, then the ripple becomes the white sign-in sheet rising |
| 6 | 0:10–0:12 | Sign in | The white sheet's "Sign in with Solana" squircle **morphs** into the Seed Vault approval sheet, then into a ✓ | The ✓ shrinks into a coin, which rolls right |
| 7–8 | 0:12–0:16 | **Origin lottery** | Tarot-style origin cards fan out in 3D; one flips (★) to "INSIDER"; confetti is out (banned), so a gold light sweep crosses the card instead | The card face zooms toward camera, and its portrait becomes the 3D character |
| 9–10 | 0:16–0:20 | Your look | A turntable orbit of the character on the mint podium; skin, hair and outfit swatches change on each beat (four looks across 4 beats, the same pose) | The podium ring scales up and becomes the lobby podium |
| 11–12 | 0:20–0:24 | **Arena lobby** | Lobby UI pieces land one per beat with a spring: currency pills, then the sticker rail (each tilts in), then event tiles, then the Dream bar fills to 3%. The camera has a slow push. | Push in on ENTER THE CITY; a press on ★ and the button's mint floods the frame |
| 13–14 | 0:24–0:28 | **Into the world** | The signature move: the mint flood resolves into the **minimap circle**. The camera dives *through* the minimap into the real island (aerial, golden hour) and down to street level behind the player. | Continuous (no cut) |
| 15–16 | 0:28–0:32 | Timeline Square | A shoulder-cam walk-and-turn; pedestrians, the CT screens and billboards; an emote (a wave) on ★ | A whip pan right (12 frames, directional blur) to the Duo button |
| 17–18 | 0:32–0:36 | **The Duo opens** | The HUD Duo icon scales (shared element) into the full phone; the phone floats out of the screen into 3D on the plum field | The phone tilts and the camera enters its screen |
| 19–22 | 0:36–0:44 | **Duo apps** (one per bar) | Messages: a DM banner drops ★ and a bubble types. Wallet: the card stack fans and the Solana card lifts. Perp Pit: the chart draws and PnL ticks +$6.86 → +$10.78. Jobs: the "Work" pill presses and a shift-done banner appears. | Each app-to-app change is a horizontal slab slide with a depth parallax |
| 23–24 | 0:44–0:48 | Punchline 1 | White flash: "get rich" (black Lilita) ★, then a hard spring to "or get rekt" in red; the PnL slab cracks into red | A red liquidation flash cuts to black |
| 25–28 | 0:48–0:56 | **Ride** | Sports car through traffic; a drift (a speed ramp 100→35→100%); the Roadstar wheels fold ★ and it takes off; a drone reveal over the marina | Motion-blur whip up into the sky |
| 29–30 | 0:56–1:00 | **Aerial** | One long golden-hour aerial over CT Island toward the MtnDAO summit (flags) | Continuous; the camera tilts down onto the plaza crowd |
| 31–34 | 1:00–1:08 | **Communities live here** | Two shots per bar (0.97 s each), each with a kinetic label: MtnDAO summit, space-cat statue, purple ghost, bull statue, CHECKPOINT 26 arch, Superteam bunting, Jupiter dome, Seeker phone shop | A coin carousel: community-nod coins roll across black (our version of beat F) |
| 35–36 | 1:08–1:12 | Social | The Spaces stage with speaking rings pulsing **to the music**; DM banners stack; a call screen | Rings expand to fill the frame (plum) |
| 37–38 | 1:12–1:16 | Social proof | The Rich List podium slab: 1 · 2 · 3 cards land; "you" pinned | The podium cards collapse into the CT coin |
| 39–40 | 1:16–1:20 | **End card** | The CT coin flips into the CT LIFE lockup on key art B; "live on Solana Seeker" (Inter), "join the waitlist" plus the URL (only if the domain is live) | Hold 1 bar; the last frame matches the first frame of the teaser loop |

**Also cut:** a **15 s teaser** (bars 1–3, 13–14, 25–27, 39) and a **9:16** (re-laid per shot; UI slabs centred,
type re-flowed, no centre crops).

## 5. Shot capture list (real footage only; AI image/video only for abstract textures)

| ID | Source | How |
|---|---|---|
| S-title | Device | Second launch (shader warm-up); `adb shell screenrecord --size 2670x1200 --bit-rate 40000000` |
| S-signin, S-origin, S-look, S-lobby | Device | Fresh dev account with fixture data; record each screen; also export each screen's UI clean (no watermark: build without the Development flag) |
| S-dive | Editor 4K | Cinematic camera path from 300 m aerial to the shoulder cam (needs the camera tool, below) |
| S-plaza, S-ride, S-aerial, S-community | Editor 4K and device | `islandDemo=1`, `islandShot=<id>`, `vehicleDemo=1`; editor via Unity Recorder 3840×2160@60 (the black-ground Metal bug must be fixed first, audit §5.10; otherwise use device footage) |
| S-duo apps | Device and code | Device recordings for authenticity, plus **rebuilt as HTML** at 4K for the slab shots (pixel-matched to the Duo kit; real fixture numbers) |
| S-macro | Blender | A Seeker-like dark device edge plus a rim light; the chart line is an animated curve with glow; 4K, DoF |
| CT coin | Blender | Extruded CT mark coin, turntable plus flight paths; render with alpha, motion blur on |

**Cinematic camera tool (Unity, dev only):** Catmull-Rom spline paths in the editor (or Cinemachine if the package is
in the project; check the manifest). Keyframed FOV and DoF focus distance. A "player autopilot" that walks a recorded
input track, so the shoulder-cam shots are repeatable. Record with Unity Recorder (check it's installed).

## 6. Sound (the user rule: real music only, engineered in sync)
- **Track**: a real, licensed 120–128 BPM track with a clear build to a drop at bar 13 (Into the world) and a second
  lift at bar 25 (Ride). Upbeat, playful, a modern electronic/pop instrumental. Options: properly licensed (read the
  licence page and keep the URL and date in `audio/LICENSES.md`), or the user buys a track. Never AI music.
- **Beat analysis**: `librosa.beat.beat_track` plus a downbeat check. Write `video/audio/beatgrid.json` (times of every
  beat and bar). All edits and morphs are keyed to it.
- **SFX map** (real recordings, CC0 first; reuse the game's own real cues where they fit):
  - coin whoosh and fly-bys, coin land "tink", tap ripple pop, sheet slide, ✓ chime
  - card flip, the light-sweep shimmer, swatch clicks
  - UI spring "plip" (soft, varied pitch per element), ENTER press thump
  - the minimap dive "air rush", footsteps and plaza crowd bed
  - DM ping (the game's), cash register on PnL, a red "rekt" sub-drop
  - engine, tyre squeal and gear shift, Roadstar thruster ignition, helicopter rotor, drone buzz, wind on the aerial
  - Spaces voice murmur, final logo hit
- **Place every SFX at its visual event's peak frame** (not its start).
- **Mix**:
  - music bus sidechain-ducked 4–6 dB under hero SFX
  - dialogue-free
  - **−14 LUFS integrated, ≤ −1 dBTP**
  - leave air: no SFX layering more than 3 at once
  - render stems (music, SFX, ambience)
- **The audio skill the user mentioned**: ask the user to re-attach that image. Find that skill and apply it here.

## 7. Agents and order of work
1. **Lead (coordinator)**: lock the track, then `beatgrid.json`, then adjust §4 to the grid, then write
   `video/style-frames/` (one still per bar, rendered from the code layer and footage stills) for the user's sign-off.
2. **Capture agent**: the Unity camera tool, then all S-* shots, to `video/footage/NN-shot-take.mov` (ProRes or
   lossless H.264).
3. **3D agent (Blender)**: the CT coin, the Duo phone prop, the macro chart shot, UI slab templates (texture swap from 4K HTML renders).
4. **Motion agent (code-first, per §2)**: one HTML project `video/motion/` with `seek(t)` scenes per bar. Springs
   closed-form; morphs between Duo states, title and lobby; type; coin paths from the Blender exports (alpha
   sequences); Playwright render with 4 sub-frames and `tmix` motion blur at 60 fps.
5. **Comp and grade (AE if available, else ffmpeg/WebGL)**: depth layers, DoF, light wrap on footage, a unified warm
   grade on the golden world, plum field rings, film grain at the end.
6. **Sound agent**: the SFX map on the beat grid, the mix, stems.
7. **QA agent**: frame-by-frame against this bible and the reference. Check:
   - every bar has an event
   - every transition is object-led or a match-move (no unmotivated cuts)
   - morph corners and colours stay continuous
   - text is readable for at least 12 frames
   - no UI glitches, no debug overlays, no "Development Build" mark
   - SFX on peaks, loudness on target
   Output: a timestamped defect list. Iterate until clean.
8. **Order**: first a **10-second style test** (bars 11–16: lobby UI landing, ENTER, minimap dive into the plaza),
   reviewed by the user. Then the full cut. Then the 15 s teaser and the 9:16.

## 8. Quality bar (reject the cut if any are true)
- A plain cut with no motion carrying across it (outside the deliberate white flash and red flash beats).
- A bar with nothing happening, or more than 3 things competing.
- Easing that's linear, or bounces more than one small overshoot.
- Gameplay footage with jitter, the HUD clipping, pop-in, or black ground patches.
- Type that's on screen under 12 frames or set below 48 px at 4K.
- Any brand or IP we don't own (Tesla, Breakpoint name, real logos). Community nods use only our own designs.
- Sound not on the grid, or music that's synthesized or AI-made.

## 9. The user's own words (follow literally)
> "I don't want something that does not have a proper camera (I mean the motion and transitions and then the fact that the whole app demo)"
> "the transitions, the camera… how the items morphs into the next screen. these are what make a great motion graphics, and the sound in sync properly, that's proper sound engineering."
> "I prefer taste to delivering fast, and then, what comes of it is slop"
> "the morphing has to work perfectly, make no mistakes."
> "we don't need to create things that have never been seen from AE (I mean it'd be nice if you could)"

## 10. Prerequisites from the game (fix before capture; from `docs/handoff/DEEP-AUDIT-20261018.md`)
- Shader warm-up (no first-launch stutter); the black-ground Metal bug (for editor 4K); no pedestrian nameplates;
  the drone camera not inside the body; offline demo mode with no network.
- The shots rely on **real working features**. The P0 audit fixes (safe exit, flight, collisions) don't block filming
  scripted shots, but should land before the waitlist cut so the video doesn't promise what the build can't do.
