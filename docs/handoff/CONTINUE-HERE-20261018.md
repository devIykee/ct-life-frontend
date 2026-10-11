# CT Life: continue here (handoff from the Claude coordinator, 18 Oct 2026)

This is the single entry point for the next agent. It records where everything is, how we work, the design language
we agreed with the user over many passes, what is unfinished, and exactly how to make the hackathon demo video.
Read it fully before you touch anything. Then read `AGENTS.md`/`CLAUDE.md` (house rules), then the docs it links.

---

## 0. First 30 minutes (do these in order)

1. `cd /Users/mac/Documents/codes/projects/ct-life && git fetch && git log --oneline -5 origin/dev`. `dev` is the
   integration branch and has everything merged. `main` is held back at `1b57305b` (the last device-verified point
   before v2). Fast-forward main to dev only after one clean Seeker run of the whole flow.
2. Check worktrees: `git worktree list`. The main checkout is used by the Feel lane, and
   `/Users/mac/Documents/codes/projects/ct-life-world` by the World lane. Never switch branches inside a worktree someone
   else is using. To merge without a checkout, use the plumbing recipe in §5.4.
3. Check the pedestrians branch: `git branch -r | grep peds` (feat/feel-v2-peds or feat/feel-v2-peds-wip). It was the last
   work in flight; see §4.2.
4. Bring up the local backend: `bash tools/backend-local/up.sh` (Docker Desktop must be running; see §1.5 if it hangs).
   Check with `curl -s -o /dev/null -w "%{http_code}" http://localhost:4010/v1/config` (expect 200).
5. Seeker: `~/Library/Android/sdk/platform-tools/adb devices` should list `SM02G40619141343`. Something on this Mac
   restarts the adb server every minute or two, so re-run `adb reverse tcp:4010 tcp:4010` (and 2567, 7880, 7881) before
   each launch.
6. Free disk: keep at least 10 GB (`df -h /`). Unity Library folders and APKs fill it fast. Delete old
   `client/Builds/*.apk` you made; never delete user files.

---

## 1. Project facts

### 1.1 What CT Life is
- A 3D multiplayer Crypto Twitter life-sim on Solana for the **Solana Mobile hackathon**, aimed at a **global** Solana CT
  audience (balanced global representation; never Lagos-centric, even though the user is Nigerian).
- Unity **6000.3.25f1**, URP 17.3, UI Toolkit, IL2CPP Android. Target device: **Solana Seeker**, landscape 2670×1200.
- Sign-in is SIWS through Mobile Wallet Adapter / Seed Vault. The app identity is `EnvConfig.AppIdentityUri = https://ctlife.fun/`.
  **The domain ctlife.fun is NOT owned yet.** Register it, or change the identity everywhere (client EnvConfig plus the
  backend `SIWS_DOMAIN`/`SIWS_URI`), before any public build.
- Repo: `github.com/Immadominion/ct-life`. GitHub LFS is blocked, so binaries are committed as plain git; keep each file under 100 MB.

### 1.2 People
- **The user (Dominion, "Heisjoelskr | Trimmy" on Telegram)** owns the frontend, 3D, audio and taste. Treat their words
  as the spec.
- **Iyke 🦀 (devIykee)** owns the backend repo `devIykee/ct-life-backend`, deployed on Railway. We don't edit his
  repo. We send him exact, numbered asks (see §6). The user approves each Telegram message before it's sent.

### 1.3 Paths
| What | Where |
|---|---|
| Client repo (main checkout) | `/Users/mac/Documents/codes/projects/ct-life` |
| World lane worktree | `/Users/mac/Documents/codes/projects/ct-life-world` |
| Backend clone (has someone's **uncommitted** likeness work; don't modify) | `/Users/mac/Documents/codes/projects/ct-life-backend` |
| Evidence (screenshots, clips, receipts; not in git) | `/Users/mac/Documents/codes/projects/ct-life-evidence/` |
| Local backend runtime, logs, device backups | `~/.ctlife-local/` (`run/`, `logs/`, `device-backup/`) |
| Art sources and kits | `art/raw/{quaternius,kenney,kaykit,polypizza,polyhaven,ambientcg,sketchfab}` |

### 1.4 Tools that work on this Mac
- **adb**: `~/Library/Android/sdk/platform-tools/adb`; launch with `tools/integration/seeker_launch.sh` (it writes the
  automation key for dev launch extras).
- **Background desktop control** (use it; never take over the user's screen): `~/.hermes/tools/cua-driver-0.21.0-darwin-arm64/cua-driver`.
  If `status` says it's not running: `cua-driver serve --socket ~/Library/Caches/cua-driver/cua-driver.sock &`.
  Accessibility and Screen Recording are granted. Telegram Desktop works with pixel `click`, `type_text`,
  `clipboard_write` plus `hotkey ["cmd","v"]`, and `hotkey ["return"]` to send. Window capture uses
  `screencapture -x -o -l <windowId> file.png`. Telegram's own AX tree is empty (it's a Qt app), so use pixels.
- **Chrome browser control** (claude-in-chrome) in the user's logged-in Chrome: Pinterest, Sketchfab (the user is
  logged in), Tripo dashboard, X.
- **Headless Chrome** for HTML→PNG mockups and icon renders: `"/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" --headless=new --screenshot=...`.
- **Blender**: `/Applications/Blender.app/Contents/MacOS/Blender -b -P script.py` (key art pipeline in `art/source/title-keyart/`).
- **Unity batch mode** and editor capture scripts in `client/Assets/Scripts/Editor/*Capture.cs`; out-of-editor compile
  check with `tools/compile_check.py`.
- **Tripo3D API**: key `TRIPO_API_KEY` in `../ct-life-backend/.env` (never print it). Balance about **225 credits**
  ($1 = 100 credits). Image-to-3D is 30 credits, +HD texture 10, +smart low-poly 10, auto-rig 25.
- **Icons8 MCP**: we use the "Windows 11 Color" (`fluent`) style in game UI, free with credit (see §8).
- **iOS Simulator** for studying real iOS: create your own device, e.g. "Duo Study"; don't use the user's iPhone or iPhone Mirroring.
- **After Effects** is installed (Adobe After Effects 2026), and the user says an AE MCP exists that can be scoped to this
  directory. Verify it with `ToolSearch` before you rely on it.

### 1.5 Known environment gotchas
- **Docker Desktop hangs** after something kills it: the GUI quits but `com.docker.backend` stays stuck. Fix it with
  `pkill -9 -f com.docker.backend; open -a Docker`, then wait for
  `curl --unix-socket ~/.docker/run/docker.sock http://localhost/_ping` to return OK.
- **Background processes die with the Claude session.** Start the backend detached:
  `(nohup bash tools/backend-local/up.sh > /tmp/up.log 2>&1 &)`.
- **Claude login can expire** mid-run. Agents then fail with `authentication_failed`; resume them with SendMessage after `/login`.
- The user's other agents (storage cleaner, Hermes, Codex) share this Mac and the Seeker. Check the foreground app
  before every adb tap; never touch other apps.
- zsh gotcha: `$VAR:a…` is a zsh path modifier, so write `"${VAR}:path"` in git commands.

---

## 2. How the user wants us to work (standing rules)

1. **Research before building.** Use primary sources, real references (Pinterest, Game UI Database, X), and the iOS
   Simulator for iOS. Label anything unverified with `[Unverified]` or `[Inference]`.
2. **Taste over speed. No slop, no "AI vibes"** (predictable grids, everything in 3s, generic stock looks).
3. **Don't make the user pick** when research gives an answer: recommend and build. For visual passes the user said
   "don't show me what it'd look like first, just do it". The exception is when they ask for options (the title
   screen and lobby got 3 quick versions because they asked).
4. **Real assets over generated stand-ins.** Use Sketchfab, Kenney, Quaternius, KayKit and Poly Pizza (CC0/CC-BY with
   credits). Use Tripo only for custom pieces. Never make AI-looking meshes. Check models for hidden provenance
   (AI text-to-3D uploads, re-uploaded Ready Player Me avatars, fan IP such as Skibidi Toilet). No third-party IP or
   brands; parody names only (the flying car is the "Roadstar", not Tesla).
5. **Real audio only.** No synthesis, no AI music. Use human recordings: CC0 first, CC-BY with credits.
6. **Real data only.** Never invent numbers, charts, odds, statuses or history. Hide an element when the backend has no data.
7. **Light testing, looks first** (for the demo). Compile, tests for new logic, and one Seeker run per phase. No giant
   capture sweeps.
8. **Push to GitHub before and after big passes.** Keep the user's uncommitted files (`docs/SUBMISSION-KIT.md`,
   `client/Assets/Plugins/Android/CTLikenessPicker.androidlib.meta`) uncommitted.
9. **Cost-conscious orchestration.** Use few right-sized agents with clear lanes. Resume them (SendMessage) instead of
   respawning. The coordinator does small jobs: merges, reviews and messages. When credits run low, stop agents
   cleanly (commit, push, report).
10. **Plain English to the user.** Short, direct, honest. Own mistakes specifically ("I let map accuracy drive the
    layout"), without grovelling.
11. **Background computer use only.** Ask before taking over the screen. Restore the clipboard if you use it.

---

## 3. The design language we built (match it exactly)

### 3.1 Brand
- **CT mark**: glossy green T over blue and purple C (`art/branding/logo/ct-mark-transparent-1254.png`). Lockup is the
  mark plus "LIFE" in **Lilita One**: white text, thick plum outline `#2a0b4a` (about 10 px at 1335 css width), rotated
  −4°, soft drop shadow.
- **Game type**: Lilita One for game labels and numbers, Inter for small UI text and caps (letter-spaced). White text
  and white cards (not cream). Plum `#2a0b4a` outlines and panels. Mint/green `#3ddc84`–`#1fcf72` for the primary
  action. Yellow `#ffd23f` for badges and the Dream. Red `#ff3b5c` for debt and notification badges.
- **No translucent text plates.** Text sits on solid panels or has a thick outline. Buttons are **squircles**, not pills
  (except iOS-style capsules inside the Duo).

### 3.2 Title screen (done; `SignInScreen`, key art B)
- Key art: Blender render "B · Close duo" (`ct-life-evidence/title-keyart-20261016/blender/keyart-B.png`, 3840×1726).
  Two real cast members in the real Timeline Square: she's on her phone (left), he's celebrating with fists up (right).
  It has a golden-hour key light, cool rim lights, filmic grade, bloom and vignette. Pipeline: `art/source/title-keyart/`.
- Layout v3: centred CT LIFE lockup; a thin white loading bar (≈720×16 device px) showing **real boot progress**;
  "TAP TO PLAY" letter-spaced above it, which pulses when ready; a legal line at the bottom ("18+ only · Cash is game
  money and cannot be withdrawn"). The background has a 1.5% Ken Burns drift and the logo scales in once. Tapping
  with no session opens a white iOS-like sheet with a "Sign in with Solana" squircle.
- Mock HTML: `ct-life-evidence/title-keyart-20261016/title-compose-mock.html`.

### 3.3 Lobby ("Arena", done; `DashboardScreen`/Home)
- Brawl Stars style. The live 3D character stands on a glowing **mint podium** on the real plaza backdrop
  (`bg-dof.png`) with a purple vignette, and **one** "Invite fren" slot on the right (a white silhouette with a yellow
  "+"; it opens Duo Calls).
- Skewed dark-plum **player plate** top-left (avatar, alias, tribe, Energy/Sanity meters). A **currency strip** of
  skewed pills: Cash with a green +, "Owe $X · $Y due <time>" in red only when in debt, and SOL (tap for USDC/SKR).
- **Sticker buttons** on the left: white-to-lilac tile, thick plum outline, 3D bottom lip, alternating ±3–4° tilt,
  Lilita label with outline, red badges. Angled **event tiles** on the right (Market, Today's clue), and the Duo sticker.
- **The Dream** is a season-pass bar bottom-left. Bottom-right has a "HOME · <name>" ribbon and a big angled mint
  **ENTER THE CITY** with an inner bottom lip and a plum drop edge.
- Icons: Icons8 `fluent` 256 px. Mock: `ct-life-evidence/lobby-redesign-20261016/mock.html` (V1).

### 3.4 Solana Duo (in-game phone, done in 4 waves)
- The hardware is one leaf of a foldable (approved). The header shows the **Solana logomark + "Duo"**. The home layout
  has two widgets, a 4-column grid, and a **vertical glass dock plus round search** on the right.
- **iOS 26 feel**: squircle icons (22.4% corners, gradient tiles, top sheen, inner rim, layered glass glyphs; renderer
  `art/source/duo-icons/build.py`, Phosphor fill geometry, MIT); Inter type scale, Large by default, XS–XXL in Settings;
  glass only on navigation and controls; content opaque.
- **No "box box box"**: full-bleed rows with inset hairlines, large titles, hero numbers, sheets for actions,
  App-Store-style pills, a glass bottom bar for the primary action.
- Apps: Messages (iMessage), Wallet (Apple Wallet card stack), Perp Pit (Robinhood-style hero price, edge-to-edge
  chart, glass Long/Short), Jobs (App Store), Timeline (X), Calls, Spaces, Settings (iOS), Rich List and League
  (podium), Home needs (Health), and the rest on the kit.
- **Notifications**: glass banners over the game for DMs (requests are badge-only, blocked senders get nothing, scam
  hints replace the text), red badges, Do Not Disturb.
- Spec: `docs/design/duo-ios-redesign-20261016.md`. Board: `ct-life-evidence/duo-redesign-20261016/index.html`.

### 3.5 City HUD and map (done)
- A minimal HUD in the lobby's language: a compact player plate, small Cash and SOL pills, a **CoD-style minimap**
  top-right (≈360 px, rotates with the player, heading cone, compass N, real markers, edge arrows), the Duo button by
  it, a floating thin-ring joystick, and round chat/emote/mic buttons. Prompts sit in the world next to their object.
- Full map: tilted painterly view, pill labels, zoom rail, recentre, venue sheet with "Go" (fast travel). The Duo Map
  app shares it. Atlas and area maps are baked from the real world (`AtlasBaker`; atlas.json v2 has `rotationDeg`).

### 3.6 World (CT Island v2, done for this pass)
- An organic island, about 1.6 × 1.2 km. North mountain range with the **MtnDAO** summit (cairn, flags) and the A-frame
  Basecamp Lodge. A river to the Trenches harbour, lakes, Dubai Marina basin with boats, west cliffs, a south crescent
  beach with a boardwalk, a lighthouse, and an islet with a causeway.
- Districts: Timeline Downtown (towers, plaza, CT screens, a grid turned 6° with the diagonal Consensus Boulevard),
  The Trenches, Dubai Marina, Builder Hills, Airdrop Farm, Residences, Breakpoint Park (convention centre "CHECKPOINT 26").
- Roads: RoadNetwork with 151 segments, 62 intersections and 368 lanes. Spline carriageways graded into the terrain,
  switchbacks at 8%, a bridge, a tunnel.
- Dressing: about 750 buildings, 231 dressed side walls, about 13k forest trees with biome mixes and baked
  billboards, 39k ground-detail clumps, street furniture and life (awnings, parasols, 277 shop signs, scooters,
  vendors, bus shelters), parked decor cars (`ParkedCar`), 19 live billboards, gulls.
- Community nods from `docs/research/community-eggs-2026-10.md`, including five Tripo statues: black bull, chef-hat
  bull, purple ghost, penguin cart, space cat.
- Look: one golden-hour sun, softer shadows, cool fill, light blue aerial haze, split toning, bloom.
- Plan: `docs/design/ct-island-v2-plan.md`. Contract between lanes: `docs/design/world-v2-contract-20261017.md`.
  Demo shots: `docs/design/demo-route.md`.

### 3.7 Characters and feel
- Multi-model cast (Quaternius UBC and Modular plus Sketchfab masked heroes; credits in `art/LICENSES.md`).
- Controls: GTA mobile style. Floating left stick; walk 1.6, jog 3.5, sprint 6 m/s; a shoulder follow camera with
  right-side drag to orbit, auto-recentre and occlusion handling; the CharacterController moves anywhere walkable.
- Falls only on real drops; kerbs and stairs are stepped. Emotes play over the game's idle; wave and shrug are CMU
  mocap; hand poses are fixed.
- Vehicles: hatchback, SUV, sports car, scooter, **Roadstar** (flying, parody), helicopter, drone. Arcade-believable
  physics, enter/exit with doors, a drift handbrake, auto-righting, and doorways blocked for vehicles.
- Traffic: up to 12 AI cars on lanes with signals, stops, roundabouts and yielding, honking (real horn), NPC drivers,
  and parked cars you can take.

### 3.8 Audio
- Title: "Glitter Blast" (Kevin MacLeod, CC BY 4.0). Real recorded stingers and SFX (Kenney CC0 and others).
  Everything is in `audio/LICENSES.md` and the in-game credits. Only the phonk sting and NPC babble are synthesized.

---

## 4. State and backlog (ordered by priority)

### 4.1 Must fix next (the user flagged these)
1. **Things passing through each other.** The user said: "the cars and buildings entering each other… the character
   entering wall etc." Do a collision audit:
   - Generator overlaps: buildings and lots overlapping each other or roads. Add an overlap rejection in the World
     generator (`tools/world/islandgen/`) using footprint polygons, and a test that fails on any overlap.
   - Instanced kit buildings and props need colliders that match their footprint (box or compound), on the right
     layers (12 Building, 13 Prop). Check that every instanced model has a collider entry.
   - CharacterController: check the skin width, the step offset and that the player's colliders include all layers
     (except 21 seat taps and 15 vehicle blockers). Venue shells must have wall colliders.
   - Vehicles vs buildings, props and parked cars (layer 13), plus traffic vs player cars. Use continuous collision
     detection at speed; the Roadstar and helicopter need colliders with buildings while flying low.
   - NPC pedestrians (F6) must use the navmesh and avoid props.
   - Write one "no interpenetration" editor sweep (overlap queries for every static collider pair in a district) and
     run it per district.
2. **Pedestrians (F6)**: finish and merge. See §4.2.
3. **First-launch shader stutter**: build a ShaderVariantCollection warm-up at boot or on the title, so filming works
   from the first launch.
4. **Editor (Metal) captures render some ground black.** The device is fine and the cause is unknown. It only affects
   editor captures; for marketing footage, capture on the device, or find the root cause first.

### 4.2 In flight when we stopped
- **Feel lane, F6 pedestrians**, branch `feat/feel-v2-peds` (or `-wip`). It uses
  `client/Assets/Resources/World/Island/pedestrian_paths.json`:
  - sidewalks: `{id, segment, side, width, points[[x,y,z]]}`
  - corners: ends meet sidewalk ends within about 1.5 m, so snap them
  - crossings: `{id, junction, segment, signal, width, a, b}`
  - areas: plaza/park/boardwalk polygons
  **Status at handoff**: `feat/feel-v2-peds` at 762059e0 is pushed and merged into dev. EditMode passed 1922/1922.
  - Editor capture: 22 people with 22 looks, zebra crossings respected, stepping aside 0.9 m, people on benches, on
    phones and at shopfronts, 0.08 ms a frame.
  - Seeker: 30 fps (thermal cap), about 8 people visible.
  - The last commit removes the "pedestrian" name tags and profile taps on pedestrians and NPC drivers, and raises the
    phone cap to 16 (12 when hot). **It's built (`client/Builds/ctlife-feel-peds-dev.apk`) but not device-tested.**

  Next: take the device lock, install that APK, walk out of the Timeline Square gate to the street, record about 40 s
  and check fps and that no tags show (`ct-life-evidence/feel-v2-20261017/peds/`).

### 4.3 Product backlog (not started)
- **App size** about 264 MiB. Move the world kits to Addressables or Play Asset Delivery (Seeker store limits [Unverified]).
- **Thermal**: the device sits at the 30 fps cap with GPU about 27 of 33 ms. Trim LODs and draw calls downtown.
- **In-game lighting/look pass** (deferred by the user until the app is ready). Fix the model flaws visible in close
  ups: a collar cut-out, a glasses-arm stick, jagged cuffs, flat braids, rigid skirts, no mouth/expressions.
- **"Make it look like me"** (Tripo likeness): built and deployed with flags off. Blocked on Tripo terms §3.2 (written
  permission for end-user generation); email business@tripo3d.ai.
- **Multiplayer for streets and vehicles** waits on Iyke's round 2 (§6). Vehicles are local-only until then.
- **Lodge demo shot**: the summit is behind the camera. Consider a second shot that looks up at the summit.

---

## 5. How we orchestrate agents

### 5.1 Lanes (the contract is binding)
- **World lane**: island content, generator, terrain, roads, buildings, props, vegetation, water, lighting, streaming,
  map bake data, pedestrian path data, demo shots.
- **Feel lane**: player controller, camera, controls, animation, vehicles, traffic AI, pedestrians AI, HUD/map UI, Duo UI.
- Shared files are append-only per section: `art/LICENSES.md`, credits text, `docs/SUBMISSION-KIT.md`.
- Each lane works in its own git worktree and pushes `feat/<lane>-*` branches. The coordinator reviews (looks at the
  screenshots/clips themself) and merges to `dev`.

### 5.2 Device lock (both lanes)
`mkdir ~/.ctlife-local/seeker.lockdir` is an atomic acquire. Write the lane and time to `.../owner`, and release with
`rm -rf ~/.ctlife-local/seeker.lockdir`. A lock older than 20 minutes goes to the coordinator; don't break it yourself.
Never uninstall or clear app data. Check the foreground app before taps.

### 5.3 Review loop
For each phase: the agent reports commits, screenshot paths, Seeker fps and the APK delta. The coordinator looks at
the images and sends specific art-direction notes before the next phase (see the notes given for W1→W2→W3→W4 in git
history). Hold the user's bar: "would this look premium in the demo video?"

### 5.4 Merging without a checkout (plumbing)
```bash
cd ct-life; git fetch -q origin; R=origin/feat/xyz
OUT=$(git merge-tree --write-tree --name-only dev $R)   # first line = tree; lists CONFLICT lines
T=$(echo "$OUT"|head -1); C=$(git commit-tree $T -p dev -p $R -m "Merge ...")
git update-ref refs/heads/dev $C && git push -q origin dev
```
For a conflict in an append-only file, resolve it with `git merge-file --union` against the true merge base, then
rebuild the tree in a temporary index (`GIT_INDEX_FILE=… git read-tree $T; git update-index --cacheinfo …; git write-tree`).

---

## 6. Backend (Iyke) status

- **Round 1** (`docs/handoff/backend-asks-for-iyke-20261016.md`) is **done** on backend main `eeef49d` (Railway):
  - live DMs
  - `POST /v1/bank/repay` with an Idempotency-Key header
  - `/me` `debtDueAt` and `debtWeeklyDueCents`
  - `GET /v1/ledger`
  - perps `markMicros` and `unrealizedPnlCents` (10,000 micros = 1 cent)

  Predictions are fixed-odds by design (no market odds). The client is wired and verified (F4).
- **Round 2** was **sent 18 Oct 12:03** (`docs/handoff/backend-asks-for-iyke-20261018.md`):
  - venue join placement
  - the move speed check (jitter-tolerant)
  - two-room overlap
  - street rooms per district
  - the vehicles map and messages, an island-wide vehicle room, vehicle hits, optional server parked cars

  Promised to Iyke: a JSON of district polygons and parking bays. Generate them from the World data and send them
  when he starts.
- **Pending, not sent yet** (listed at the bottom of that file):
  - the weekly due doesn't drop after paying
  - spec validation errors
  - unquoted YAML
  - archetypes `spaces_host` removed
- Local testing of new backend features: run a clean clone of backend main on **separate ports (4012/2569)** with a
  **separate DB** (`ctlife_e49d`) and Redis db 7, using the app's Mock profile. Never touch the dirty clone.

---

## 7. THE DEMO VIDEO (the next big job)

> **The authoritative plan is now `docs/video/DEMO-VIDEO-BIBLE.md`**: a frame-by-frame study of the reference, the @twoclipping code-render method, the CT Life film system, a beat-grid script, the capture list, sound and agent order. The section below is the older summary; where they disagree, the bible wins.

This is the user's top priority: the submission video for the Solana Mobile hackathon, plus a waitlist cut. The user's
own brief for it is quoted at the end of this section. **Taste over speed.** The coordinator is the lead
(director/editor). Agents do the capture, motion, sound and QA.

### 7.1 Study first (required, don't skip)
1. Open the user's reference in Chrome: `https://x.com/amirdzm/status/2110438849717703088` (an "introducing" product
   motion video, no audio). Study it frame by frame. Write down for every transition: what morphs into what, the camera
   move (push, orbit, whip, rack focus), easing (overshoot, ease-out), speed ramps, the timing in frames, type
   animation, depth layers and motion blur. Save this as `docs/video/reference-breakdown.md` with timestamps.
2. Study the prompting style in `https://x.com/twoclipping/status/2103273003555402193` and use it to write the agent prompts.
3. The user mentioned an attached image about an **audio skill** for sound design. It wasn't available to the
   coordinator. Ask the user to re-attach it, then find that skill and learn from it.
4. Look at 3–5 premium game reveal trailers (mobile: Brawl Stars, Squad Busters, Monopoly Go launch trailers) for the
   gameplay-cut pacing.

### 7.2 What the video must show (the whole app, in story order)
The story: "Crypto Twitter, but you live in it." It runs about 75–90 s at 16:9 4K/60, plus a 9:16 cut and a 15 s teaser.

| # | Beat (length) | Footage | Motion/transition idea |
|---|---|---|---|
| 1 | Cold open (0:00–0:04) | Black, then a macro of the **Seeker** screen lighting up | Light leak, then the CT mark assembles (C slides, T drops) with a bass hit |
| 2 | Title (0:04–0:08) | Title key art, Ken Burns | The logo locks up; the loading bar fills **to the beat**; "TAP TO PLAY" pulses, then a finger-tap ripple |
| 3 | Sign in with Solana (0:08–0:12) | The white sheet, then the Seed Vault approve | The sheet's squircle button **morphs** into the wallet sheet, then into a checkmark |
| 4 | Your origin (0:12–0:18) | Origin lottery cards and the player card | The cards fan out and one flips (3D in AE), then a match-cut to the character on the podium |
| 5 | Pick and own your look (0:18–0:24) | Roster, customizer, podium | A turntable orbit around the podium; colour swatches sweep across the model |
| 6 | Arena lobby (0:24–0:29) | Lobby with live meters, the Dream bar, wallet SOL | UI elements land with overshoot one by one (stickers bounce); push in on ENTER THE CITY |
| 7 | Enter the city (0:29–0:35) | Fly-through from the podium into the island | **Signature transition**: the podium ring **morphs into the minimap circle**, then the camera dives into the island (sky to street) |
| 8 | Walk the plaza (0:35–0:40) | Timeline Square, NPCs, billboards, emote | Shoulder-cam follow; whip pan to the CT screens |
| 9 | The Solana Duo (0:40–0:50) | The HUD Duo icon opens the phone; Messages DM banner, Wallet cards, Perp Pit PnL ticking, Jobs | The **Duo icon scales into the full phone** (shared-element morph). Then app-to-app swipes: the Wallet card stack fans, the PnL number counts up green |
| 10 | Get rich or get rekt (0:50–0:56) | Perp position, live PnL, a liquidation stinger | Split-screen green/red; a number ticker; a quick zoom on "Owe $600 · $120 due" |
| 11 | Ride (0:56–1:06) | Sports car through traffic, drift; the Roadstar takes off; helicopter over the marina; drone reveal | Speed ramps on the drift; motion-blur whip into take-off; one long aerial over the island at golden hour |
| 12 | Communities live here (1:06–1:12) | Statue and landmark montage: MtnDAO summit flags, the space cat, purple ghost, bull statue, CHECKPOINT 26, Superteam bunting | 0.5 s cuts on the beat, each with a small kinetic label |
| 13 | Social (1:12–1:18) | Spaces stage with speaking rings, DMs, calls, other players | Bubbles pop in; the voice waveform syncs to the music |
| 14 | End card (1:18–1:28) | Island aerial, then the CT LIFE lockup | "Live on Solana Seeker. Join the waitlist." plus the URL (only once the domain is real), and a final hit |

### 7.3 Capturing footage
- Use real game footage only. No AI video of the game itself; image/video generation is fine for abstract backgrounds
  or light textures.
- On the device for UI authenticity:
  - `adb shell screenrecord --size 2670x1200 --bit-rate 40000000 --time-limit 30 /sdcard/x.mp4`, then pull it.
  - Film from the second launch (shader warm-up).
  - Use `islandDemo=1` (offline island) and `islandShot=<id|all>` (authored shots, `docs/design/demo-route.md`).
  - Use `vehicleDemo=1` (scripted fleet drive).
  - Use Mock or fixture data for clean numbers, no debug overlays. Hide the "Development Build" watermark with a
    non-development build or a cropped frame.
- In the editor for cinematic 4K:
  - Build a small dev-only **cinematic camera tool**: spline camera paths with Cinemachine if it's in the project
    (check the package), otherwise a simple Catmull-Rom rig.
  - Record with Unity Recorder at 3840×2160, 60 fps, using the same URP quality and look volume as the device.
  - Get the editor black-ground capture bug fixed first (§4.1.4), or use device footage for ground-heavy shots.
- Shoot clean UI passes too: each Duo app with no HUD, plus solid-colour or greenscreen background versions of the phone UI.
- **Seeker device mockup** for the 3D phone shots: use an official Solana Mobile press-kit render if its licence
  allows [Unverified]; otherwise model a generic black phone in Blender (three lens dots, to match the community egg).

### 7.4 After Effects pipeline
- Project: `video/ae/CTLife-Demo.aep` with compositions per beat, at 3840×2160 and 60 fps, plus a master comp. The
  9:16 master reframes with AE's crop or re-layout per beat, not a centre crop.
- **Shared-element morphs** (the most important thing):
  - Morph a UI element into the next screen with matched-position keyframes and shape-path morphing (convert masks to
    shape layers; ease 80/20 with a small overshoot).
  - Keep corners, colours and the shared element continuous: icon to phone, podium ring to minimap, button to wallet
    sheet, card to character.
  - Use the AE 3D camera with depth-separated layers for push-ins, so the parallax feels like a real camera.
- **Camera language**: every shot moves (a slow push, orbit or lateral track). Add whip pans with directional blur
  (8–12 frames). Speed-ramp gameplay (100% to 30% to 100%) on key moments. Keep the motion blur shutter at 180°.
- **Type**: Lilita One for hero words, Inter for supporting words. Kinetic per-word reveals; white with a plum outline
  on busy footage.
- **Colour**: the game's warm golden grade, kept consistent across beats. Add a light film grain at the end for glue.
- Render: ProRes 422 HQ master, then H.264 and H.265 deliverables (X/YouTube, about 40 Mbps 4K). Make a 1080p review proxy.

### 7.5 Sound
- **Real music only** (user rule). Choose a licensed track with a strong beat grid (110–128 BPM) and a clear drop at the
  "Enter the city" beat. Sources: properly licensed tracks (check the licence page; credit them), or a track the user buys.
- **Cut the edit to the beat grid.** Every major transition lands on a downbeat; morphs hit on the bar.
- Use real recorded SFX for every visual event: UI taps, whooshes on whips, risers into the drop, the car engine and
  tyre squeal, the Roadstar thruster, helicopter rotors, coins and ticker clicks, the crowd in the plaza.
- Mix: music bus ducked about 4–6 dB under hero SFX (sidechain), a loudness target of −14 LUFS integrated for web,
  peaks under −1 dBTP. Don't put SFX on every frame; leave breathing room.
- Optional VO from the user (they've recorded a founder pitch before). Leave a slot.

### 7.6 Orchestration for the video
1. **Lead (you)**: write `docs/video/script.md` (the beat sheet in §7.2, refined after the reference study), the shot
   list, the music choice and the style frames.
2. **Capture agent**: builds the cinematic camera tool, then records all device and editor shots to `video/footage/`
   named `NN-beat-shot-take.mp4`.
3. **Motion agent(s)**: build AE comps beat by beat using the AE MCP. Start with ONE 10-second style test (beats 6→7→8,
   lobby to minimap morph to plaza) and compare it with the reference breakdown before doing the rest.
4. **Sound agent**: picks the track, builds the SFX map and the mix in the AE timeline or a DAW, and renders stems.
5. **QA agent**: frame-by-frame review against the reference breakdown and the user's bar. Lists every flaw (jumps,
   mismatched morph corners, off-beat cuts, UI glitches, clipping) with timestamps. Iterate until clean.
6. Show the user the 10 s style test, then the full cut.

### 7.7 The user's own words on the video (verbatim excerpts; follow them)
> "I don't want something that does not have a proper camera (I mean the motion and transitions and then the fact that the whole app demo)"
> "the transitions, the camera (camera in this case is like say in unity, how it follows the player) - in this case how the items morphs into the next screen. these are what make a great motion graphics, and the sound in sync properly, that's proper sound engineering."
> "I prefer taste to delivering fast, and then, what comes of it is slop"
> "the morphing has to work perfectly, make no mistakes."
> "we don't need to create things that have never been seen from AE (I mean it'd be nice if you could)"

---

## 8. Licences and credits checklist (before submission)
- In-game Credits (Settings → Credits & Licences) must list:
  - every CC-BY item: Kevin MacLeod tracks, spuispuin, Gregor Quendel, the Sketchfab heroes, MiniPoly scooter,
    NateGazzard drone, Blender Foundation Rain/Snow rigs (CC-BY 4.0, used in the key art)
  - the CMU mocap note
  - an Icons8 link to icons8.com
- **Google Play / dApp Store description**: credit Icons8 (their free-licence rule for mobile apps). There's a reminder in
  `docs/SUBMISSION-KIT.md`.
- **Tripo**: re-read Tripo's API terms for commercial use of generated outputs [Unverified]. The statues (task ids in
  `art/LICENSES.md`) depend on it.
- **No real brands**: the Roadstar is a parody (no Tesla), CHECKPOINT 26 isn't Breakpoint, and community nods follow the
  community-eggs rules.
- Fonts: Lilita One, Inter and the other bundled fonts are OFL. No Apple fonts or SF Symbols shipped.

---

## 9. Command cheat sheet
```bash
# backend
(nohup bash tools/backend-local/up.sh > /tmp/up.log 2>&1 &); curl -s http://localhost:4010/v1/config -o /dev/null -w "%{http_code}\n"
# device
A=~/Library/Android/sdk/platform-tools/adb; $A devices; for p in 4010 2567 7880 7881; do $A reverse tcp:$p tcp:$p; done
tools/integration/seeker_launch.sh -e islandDemo 1 -e islandShot all      # dev build extras (automation key handled)
$A shell dumpsys activity activities | grep topResumedActivity              # check the foreground app first
# merge a lane branch (see §5.4); push dev; main only after a full device run
```

## 10. Exact next steps
0. **Read `docs/handoff/DEEP-AUDIT-20261018.md` and fix its P0 list first.** The game isn't demo-ready until its hand-played route runs clean twice.
1. Collect the Feel lane's F6 report (branch `feat/feel-v2-peds*`) and merge it if it compiles.
2. Collision audit and fixes (§4.1.1): World lane for generator, colliders and shells; Feel lane for the controller,
   vehicles and pedestrians.
3. Shader warm-up (§4.1.3).
4. Run one full Seeker flow (title, sign-in, onboarding, lobby, city, Duo, vehicles), then fast-forward `main` to `dev`.
5. Demo video (§7), starting with the reference study and the 10 s style test.
6. When Iyke replies to round 2, wire street rooms and vehicle sync, and send him the district polygons and parking bays JSON.
