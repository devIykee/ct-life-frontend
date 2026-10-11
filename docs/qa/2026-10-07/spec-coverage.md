# CT Life frontend spec-coverage audit, 7 October 2026

**Scope.** This is a read-only audit of the Unity client against `docs/spec/build-spec.md` (44 features), `docs/spec/contract.md` and the later decisions in `docs/AGENTS-BRIEF.md`.

**What I looked at:**
- Source in `client/Assets/Scripts`.
- Assets in `client/Assets`, `art/`, `audio/` and `website/`.
- Progress logs in `docs/progress/**`.
- Screenshots, which I opened myself.
- A read-only look at the sibling backend clone `../ct-life-backend`. Its last local commit is `a5eafda`, 5 Oct.

**What I did not run:** Unity, adb, any network request, or the backend.

**How I judged:** status comes from source and actual images, not from what the logs claim. Anything inferred is labelled `[Inference]`, and anything I could not check is labelled `[Unverified]`.

**Status key:**

| Status | Meaning |
| --- | --- |
| **Done** | Real, and integrated with the backend. |
| **Partial** | Client code exists, but something named is missing or it has never run against a server. |
| **Fixture-only** | Works only offline, on development fixtures or locked review paths. |
| **Not started** | Nothing beyond placeholders or generated models. |

**One finding sets the ceiling for every row.** The client has never made a request to a running backend: not the mock, not the local Docker stack, not staging.

- `docs/progress/unity.md:55`: "no realtime server exists any more".
- `ct_direction.md:759`: "No chat was sent".
- Every Seeker build since v1 runs with `review=1`, which swaps in `App/Review/ReviewTransport.cs`. That transport returns a fake `/me` and fails every POST. The v7 city screenshot is watermarked "VISUAL REVIEW · SAMPLE DATA".

As a result, **no feature qualifies as Done**.

---

## 1. Feature table (44 features)

### Phase 1 · City and identity

| # | Feature | Status | Evidence / what is missing |
| --- | --- | --- | --- |
| 1 | Sign In With Solana and Seeker badge | **Partial** | **Real:** MWA 2.0 SIWS via Solana.Unity-SDK `main@bd0d30f` (`Solana/MwaWallet.cs:71-106`); challenge → sign → verify, refresh and logout (`Net/Auth/AuthService.cs:31-65`).<br>**Never run:** this path has not executed on a device against any server (`unity.md:57`). `identityUri` is still the placeholder `https://ctlife.invalid/` (`MwaWallet.cs:27`).<br>**Badge:** a text "✓" after the alias (`WorldLabels.cs:120`, `ProfileCard.cs:141`, `CoreApps.cs:132`). Whether a player is a Seeker holder is decided by the server's Seeker Genesis Token check. |
| 2 | Character creator | **Partial** | **Present:** 3D turntable preview, tribe, traits, dream, alias and 18+ toggle (`UI/Screens/CharacterCreatorScreen.cs`).<br>**Looks:** only 2 whole-body looks, Snow and Rain (`Resources/UI/CreatorLookCatalog.asset`). No modular wardrobe.<br>**Cannot be saved against the contract:** the 7 look IDs (`head_snow`, `hair_rain_ponytail`, …) are not in `contract/ids/items.json`, which has 12 items. `ConfigStore.IsLookConfirmed` (`Core/Data/ConfigStore.cs:49`) therefore returns false and the UI shows "Preview look · not available to save yet". The fix is proposed in `docs/fe-contract-proposals/{snow,rain}-starter-look.md` but not merged. |
| 3 | Birth lottery | **Partial** | The server rolls; the client plays a highlight spin over 2–3 cards and shows a Seeker Baby card for holders (`UI/Screens/LotteryScreen.cs`). No share card. Never run against `/lottery/roll`. |
| 4 | City map and travel | **Partial** | **Map:** a phone list grouped by district (`UI/Screens/CityMapScreen.cs`), not a low-detail map scene. Build Settings contain only Bootstrap and Spike.<br>**Venues:** 3 of 21 are reachable.<br>**Travel:** works, and falls back to "Realtime is offline. You're alone here" (`App/FlowController.cs:450-457`). |
| 5 | Timeline Square | **Partial** | **Venue:** an authored, baked asset-kit street (`Art/Generated/Venues/TimelineStreet20261006.prefab`) with static billboard and screen props.<br>**Missing:** the Pixel Wall and live billboard slots. The square has never been populated by real players or NPCs.<br>**Look:** flat and overcast (`screens/seeker-playable-v7-20261007/touch-city-confirmed.png`). |
| 6 | Movement, emotes, chat bubbles | **Partial** | **Movement:** analog is the default, with a persisted arrows option (`Core/Services/ControlSettings.cs`, `CoreApps.cs:160-174`); NavMesh.<br>**Chat:** chat bar, bubbles and `say`/`said` rate limits are in code (`Hud.cs:211`, `Channels.cs:119-137`, `WorldLabels.cs`), but a chat has never reached a server.<br>**Emotes:** 23 of 27 animate. The tribe emotes `send_it_slam`, `max_long`, `ring_light_pose` and `frantic_typing` do nothing.<br>**Running:** not accepted. V2 transitions penetrate the floor, and V3 is unassigned (`frontend-status-20261007.md:29`). |
| 7 | Nine Phase-1 NPC archetypes | **Fixture-only** | **IDs:** all 9 present.<br>**Meshes:** kit models with one prop each, in the mannequin style the user rejected (`Art/characters/npc`, `art/previews/characters_npc_phase1.jpg`).<br>**Controller:** a generic one that follows server `NpcState`, with babble and bubbles wired (`World/Npcs/NpcController.cs`).<br>**Seen only offline:** NPCs have appeared only in Editor fixtures (`App/Review/NpcCastReview.cs`, `NpcNavigationReview.cs`). The brains are server-side in `ct-life-backend/packages/npc-brain`. |
| 8 | Needs and own home | **Partial** | **Needs:** the HUD shows them; Home app actions call `POST /needs/actions` (`UI/Phone/Apps/HomeApp.cs:130`).<br>**Homes:** 1 of 5 (Parent's Basement B2, `Resources/World/VenueContentRegistry.asset`). Home entry and return passed in the native Editor only (`screens/home-private-entry-native-20261007`). |
| 9 | First 3 careers with 9 mini-games | **Partial** | **Wired to the live shift flow:** 3 contract games, Mod Desk, Earnings Desk and Thread (`JobsApp.cs:306-308`).<br>**Practice-only:** Raid Router, Role Lock, Receipt Match, Live Cues, Runway Plan and Clip Cut use *proposed* IDs and are reached only through the dev-only `App/Review/PracticeJobsApp.cs`. The proposal is `fe-contract-proposals/phase1-nine-games-20261007.md`.<br>**Backend:** assigns one game per career. No shift has ever been settled. |
| 10 | Town Crier and server events | **Partial** | **Wired:** an incident card and history feed with dedupe and TTL, plus stingers, all on city `event` (`UI/Overlays/IncidentPresentation.cs`).<br>**Missing:** the Town Crier is only the generic NPC controller. No live event has ever been received. |
| 11 | Net Worth and Weekly PnL boards | **Partial** | The rich-list tabs call `/leaderboards/{id}` (`CoreApps.cs:68-117`). Complete on the client, never run against a server. |
| 12 | Report and block | **Partial** | **Present:** report and block from the profile card (`UI/Overlays/ProfileCard.cs:129-210`).<br>**Missing:** an Unblock button (`Api.Unblock` has no caller, and the integration checklist needs it) and reporting a chat message from its bubble. |

### Phase 2 · Money and markets

| # | Feature | Status | Evidence / what is missing |
| --- | --- | --- | --- |
| 13 | Cash top-ups in USDC, SOL, SKR and ORE | **Not started** | `ComingApp("topup")` (`App/PhoneCatalog.cs:23`). The Bank button just toasts "coming soon" (`CoreApps.cs:63`). `Api.cs` has no `/payments/intents` wrapper. The generated models exist, and `MwaWallet.SignAndSendBase64` exists but has never run. |
| 14 | Terminal Café trading | **Not started** | The café room exists as art. Pulse is a `ComingApp`. `Api.Trade` has no caller. |
| 15 | Launchpad Tower | **Not started** | No venue and no UI. |
| 16 | Perp Pit and liquidation | **Fixture-only** | `PerpsApp` reads and prepares orders, but orders are disabled (`ordersEnabled=false`) and the app is phase-locked (`PhoneShell.CurrentPhase=1`). REKT card, `react_liquidated` and the stinger are wired to `liquidated` (`RektCard.cs`). No Perp Pit venue. |
| 17 | Rekt Clinic and tombstones | **Not started** | `Api.Tombstones` has no caller. No venue. |
| 18 | ORE Refinery, ORE payments, Miner badge | **Not started** | ORE is a `ComingApp`. No `/ore/*` wrapper, no Refinery venue, no hard hat. The miner tier is shown only as ⛏ glyphs in nameplates (`WorldLabels.cs:121-123`). The audit `docs/progress/ore-skr-client-audit.md` is planning only. |
| 19 | Prediction Pit | **Not started** | `ComingApp("predict")`. |
| 20 | Alpha Calls and Trust Score | **Not started** | — |
| 21 | Smart Money copy trading | **Not started** | Only a degraded-feature flag. |

### Phase 3 · Voice and calls

| # | Feature | Status | Evidence / what is missing |
| --- | --- | --- | --- |
| 22 | Proximity voice | **Partial** | **Built:** `Net/Voice/VoiceService.cs` with an 8 m radius, the 6 nearest speakers and music ducking, using LiveKit 2.1.0.<br>**Tested:** desktop only, against a local LiveKit dev server with synthetic audio (`screens/voice-native-20261007`).<br>**Not tested:** Android microphone, a real `/rtc/token`, lip-sync (uLipSync is not installed). The local backend clone has no `/rtc/token` route `[Unverified for the remote backend]`. |
| 23 | Spaces Studio | **Fixture-only** | `SpacesApp.cs` (502 lines) and the Studio art room exist, but the app is phase-locked. The only receive test used local LiveKit and synthetic audio. The local backend has no `/spaces` routes. |
| 24 | Free video calls | **Not started** | `ComingApp("calls")`. |
| 25 | Paid video calls | **Not started** | No `ct_escrow` client and no UI. |
| 26 | DMs | **Not started** | `ComingApp("messages")`. The backend has `/dms`. |
| 27 | In-game Timeline feed and follows | **Not started** | `ComingApp("timeline")`. The backend has `/feed` and `/follows`. |
| 28 | Tips | **Not started** | — |

### Phases 4–6

| # | Feature | Status | Note |
| --- | --- | --- | --- |
| 29 | All 14 careers and gigs | **Not started** | Phase-4 careers would fall through to a generic tap game (`JobsApp.cs:303-372`). |
| 30–35 | Businesses; home visits and rent; Sunday Unlock, Meta and clue; shop; NFTs; season pass | **Not started** | Shop is a `ComingApp`. Only private home *entry* exists, counted under #8. |
| 36 | Ads | **Not started** | Billboards are static house art. |
| 37 | Player projects, Builder Hills, VC Row | **Not started** | — |
| 38–44 | Tribe control, leagues, Ratio Arena, Airdrop Farm, Dubai/Cabal, LLM NPCs, X link | **Not started** | League is a `ComingApp`. |

### Counts by phase

| Phase | Features | Done | Partial | Fixture-only | Not started |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1 City and identity | 12 | 0 | 11 | 1 | 0 |
| 2 Money and markets | 9 | 0 | 0 | 1 | 8 |
| 3 Voice and calls | 7 | 0 | 1 | 1 | 5 |
| 4 City life | 7 | 0 | 0 | 0 | 7 |
| 5 Projects and ads | 2 | 0 | 0 | 0 | 2 |
| 6 Competition | 7 | 0 | 0 | 0 | 7 |
| **Total** | **44** | **0** | **12** | **3** | **29** |

### The 21 venues

The contract's `venues.json` lists 16 IDs. The spec's 21 are those 16 minus the district IDs `builder_hills` and `airdrop_farm`, plus 7 IDs that exist only as spot sets. `docs/progress/ct-world-coverage-20261006.md` already flags this mismatch.

| State | Count | Venues |
| --- | ---: | --- |
| Playable art room | 3 | `timeline_square`, `terminal_cafe`, `spaces_studio` |
| In the contract, locked, no art (generic greybox in dev only) | 11 | `perp_pit`, `rekt_clinic`, `ore_refinery`, `launchpad_tower`, `prediction_pit`, `audit_row`, `vc_row`, `ratio_arena`, `dubai_marina`, `cabal_lounge`, `discord_heights` |
| Spot positions only, not in the venue catalog | 7 | `the_trenches`, `hackathon_house`, `airdrop_fields`, `sybil_barn`, `club_liquidity`, `conference_hall`, `ct_jail` |

Homes: 1 of 5 (Parent's Basement). There is no city-map scene. The rooms render sparse and flat-lit (`seeker-playable-v6-20261007/cafe-after.png`, `studio-arrival-device.png`).

### The 28 NPC archetypes

| Dimension | Coverage |
| --- | --- |
| Contract IDs | 28 distinct IDs in 29 records: `spaces_host` is listed twice. Laser-Eyes Maxi and Chain Tribalists have no ID. |
| Lines | Only the Phase-1 nine have them, about 30 each (`contract/config/npc_lines.json`). |
| Babble banks | 31 banks covering all 28 (`Resources/Audio/babble`). |
| Distinct visual | 18 of 28: old kit mesh plus one prop, in the rejected mannequin style. 10 of 28 use the procedural placeholder. 0 of 28 match the approved Snow/Rain style. Farmer and normie art exists but is not linked because the file names differ (`airdrop_farmer.fbx`, `normie_tourist.fbx`). |
| Behaviour | Brains run on the server (`ct-life-backend/packages/npc-brain`, applied in `server/realtime/rooms/venue.ts`). The client follows state generically. On top of that it adds only an idle shrug and a talk point. No archetype has its own client behaviour. |

---

## 2. Phase verdict

The spec (`build-spec.md:797-802`) says a phase is done when:
1. every feature in it is Done;
2. the integration checklist steps for that phase pass on staging;
3. the stress test passes on a Seeker;
4. a signed APK and a demo video exist.

### Phase 1 against that definition

| Requirement | State |
| --- | --- |
| Features Done | **0 of 12.** 11 are Partial and 1 is Fixture-only. |
| Signed APK | **No.** v7 is a *development* build (`client/Builds/ctlife-playable-landscape-v7-clean-package-20261007.apk`, 162.4 MB). No release keystore is set (`ProjectSettings.asset:291 androidUseCustomKeystore: 0`). |
| Size | 162.4 MB, over the 150 MB budget. |
| Demo video | None found. |
| Stress test (50 players + 12 NPCs, voice on, 60 fps for 10 min) | **Not run.** See below. |

**Stress test detail:**
- The 5 Oct engine spike held 60 fps with 62 *procedural placeholder* avatars for 150 s (`unity.md:54`).
- v7 holds 30 fps under the thermal cap, with one avatar registered.
- v7 draw calls are 194–226, against a budget under 150.
- Snow and Rain are 43k and 77k LOD0 triangles, against a budget of 4–6k.
- `[Inference]` A 50-avatar crowd with the current hero meshes cannot meet the triangle budget without new LODs or lighter crowd bodies.

### Integration checklist, Phase-1 steps

| Step | Can it run today? | Blocker |
| --- | --- | --- |
| 1. Switch EnvConfig to staging and build | **No** | Staging realtime URL is `wss://staging-realtime.invalid` (`Resources/Env/EnvConfig_Staging.asset`, `EnvConfig.cs:75-80`). Whether the staging REST host exists is `[Unverified]`. No release signing. |
| 2. SIWS on A; profile shows the Seeker badge | Code exists, **never executed** | Needs a reachable backend and a fingerprint on the Seeker. The SIWS identity URI is a placeholder. |
| 3. Create a character and roll the lottery | **No** | The look IDs are not in the backend's item registry, so the character cannot be submitted. |
| 4. Both phones in Timeline Square; move, emote, chat; block and unblock | Code exists, **never executed with 2 clients** | No Unblock UI. |
| 5. Work one shift; Cash rises | Code exists, **never executed** | Only 3 of the 9 games can earn pay. |

**Verdict:** Phase 1 is roughly a client-side vertical slice with no proven integration. `[Inference]` The fastest way to an APK that "uses MWA and runs on a device", which is CLOCK IN's entry rule:
1. Run `ct-life-backend` with `docker compose`.
2. Build with the `local` env and `adb reverse tcp:3000 tcp:3000 tcp:2567 tcp:2567`.
3. Merge the starter-look items.
4. Complete SIWS on the Seeker.
5. Run checklist steps 2–5.

The spec gives the CLOCK IN deadline as **8 Oct 2026 23:59** (`build-spec.md:15`, `:806`), which is tomorrow. `[Unverified]` whether that deadline still applies: AGENTS-BRIEF says the user imposed no build deadline.

### Other phases

| Phase | Verdict |
| --- | --- |
| 2 | Only Perps exists, as a fixture. It cannot qualify for the ORE match or the SKR prize, because the integration must be "live in the submitted product". The local backend already serves `/payments/intents`, `/ore/board`, `/ore/me`, `/ore/stake/tx`, `/market/*`, `/predictions` and `/alpha-calls`. The client is now the bottleneck for Phase 2. |
| 3 | Voice and Spaces client code works on desktop with a local LiveKit. The local backend clone has no `/rtc/token` or `/spaces` routes `[Unverified for the remote]`. |
| 4–6 | Not started. |

---

## 3. Specific checks

| Check | Finding |
| --- | --- |
| **Wallet sign-in (MWA/SIWS)** | The code is real and correct in shape: SDK `main@bd0d30f` (the #289 MWA v2 merge), the exact challenge strings are signed, verify, refresh, logout and deauthorize. It has **never been exercised end to end on the Seeker.** The dev bypass is compile-guarded (`FlowController.cs:167-219`). `SignAndSendBase64` has an `[Unverified]` VersionedTransaction round-trip comment (`MwaWallet.cs:116`). |
| **Seeker Genesis Token badge** | The server decides `isSeeker`. The client shows a "✓" after the name and the Seeker Baby lottery card. There is no designed badge asset. |
| **ORE Refinery and SKR** | **Nothing real on the client.** No ORE app, Refinery venue, board, staking, payments or hard hat. SKR appears only as an `skrName` text tag in the profile. The planning is in `docs/progress/ore-skr-client-audit.md`. |
| **Top-ups** | Not started (see #13). |
| **LiveKit voice and Spaces** | **Proximity:** `VoiceService` passed a native desktop test with the installed SDK and synthetic audio: subscribe and unsubscribe by distance, room replacement, late-connection drain. **Spaces:** a receive test passed with 9,560 non-zero PCM samples. **Not done:** Android microphone, a real token endpoint, the 6-peer cap on a device. Spaces is phase-locked in the shipping catalog. |
| **Multiplayer presence** | `VenueSession` and `RemotePlayer` render players from Colyseus `VenueState`. This has **never run with a server.** The offline fallback shows a lone player. The Studio "populated" captures used 12 synthetic actors (`ct_direction.md:751`). |
| **NPC brains** | Server-side, as the spec intends (`ct-life-backend/packages/npc-brain`). The client only follows state. Fine architecturally; unproven together. |
| **Market and perps** | The market room joins on sign-in, but no coin UI is shown (Pulse is a placeholder). Perps reads and drafts tickets with orders disabled, and is phase-locked. |
| **Audio integration** | See below. |
| **Characters** | **Players:** 2 whole-body looks (Snow, Rain); a Vincent prefab exists but is unregistered. **NPCs:** 22 NPC FBX, 18 registered, all in the old mannequin kit. The 91-part modular kit (93 FBX) is dead at runtime (`Resources/Characters/Parts` is empty). **Against the 28-archetype cast:** 18 have kit visuals, 10 are placeholders, 0 are in the approved style. |
| **Animations in use** | 96 clip FBX in `Art/animations`. The controller has 46 states using 44 distinct clips. No locomotion blend tree; one full-body layer. The contract has 10 animation IDs and the FE snapshot 50; the spec lists about 70. The jog, turns, jumps, phone set, talk gestures, most dances and the stagger/get-up set are not wired. |
| **Website** | **Draft, never deployed.** `website/index.html` (6 Oct) is a six-section landing page that mentions ORE, SKR and the Seeker; it is shelved. The current `website/draft-01/` (opening 09) is a hero plus one section, with "Get the game" disabled and no ORE, SKR or dApp Store mention. There have been about 9 hero iterations, 28 screenshots and 8 logs. |

**Audio detail:**
- **SFX map:** `Resources/Audio/sfx-map.json` is loaded (`AudioManager.cs:294-300`). The root `audio/sfx-map.json` is an unused byte-identical copy.
- **SFX that never fire:** 20 of the 32 mapped IDs, including every trading, phone and voice-room sound.
- **Music:** chosen by venue, then club, then district (`MusicDirector.cs:118-128`). It uses 8 CC-BY Kevin MacLeod full mixes. There are no market-mood stems, so `market_mood` has no effect. There is no FMOD.
- **Wasted size:** 42 unreferenced old stems (about 20 MB) still ship.
- **Babble:** wired for all archetypes (`NpcController.cs:285-301`).
- **Footsteps:** always "concrete", timed by distance, local player only.
- **Stingers:** 4 of 6 fire; graduation and the Sunday bell never do.
- **Voice ducking:** implemented in code.

---

## 4. Where the effort went

Measured on 7 Oct. Percentage shares are `[Inference]`, using line and file counts as a stand-in for effort. There is a single git commit (5 Oct), so there is no timeline.

### Code: 82,394 C# lines in total

| Bucket | Lines | Share |
| --- | ---: | ---: |
| Editor: motion, foot, seat, run and exit candidates and probes (67 files) | 22,916 | 27.8% |
| Editor: venue art candidates, bakes and room reviews (54 files) | 23,104 | 28.0% |
| App/World/Net review harnesses (41 files) | 9,103 | 11.0% |
| Runtime game and infrastructure | 22,078 | 26.8% |

About **73%** of the C# does not ship in the player.

### Docs, screenshots and art

| Measure | Finding |
| --- | --- |
| Screenshot folders | 139 folders, 2,014 files, 1.4 GB. Motion, seat, run and foot account for 851 files (42%), minigames and UI for 26%, device and stability for 21%, venue art for 10%. 18 folders are named "failed". |
| `art/` folder | 13 GB. About 5.0 GB is motion-review receipts; `art/processed` (the shipped output) is 140 MB. |
| `ct_direction.md` keywords | About 131–159 lines hit motion terms (foot, mm, contact, seat, sprint, transition). Only 17 hit ORE, SKR, wallet, MWA, top-up, LiveKit, multiplayer, trade or perp. |

### Estimated share of effort `[Inference]`

| Area | Share |
| --- | ---: |
| Micro-quality validation and evidence | ~45–50% |
| …of which motion, seat and foot alone | ~30–35% |
| Venue art iteration | ~20–25% |
| Feature breadth | ~25% |
| Infrastructure, audio, website | ~5–8% |

### Is this the right balance? No, given the stated goals

The goals are a quality game, integration with the backend in under an hour, and phases that are each ready to submit.

**The millimetre-level work was rigorous but went to the wrong layer.** Sub-millimetre sole clearance, 2 mm ankle drift and seat-exit choreography were polished on 2 characters in 2 rooms. Meanwhile the problems a player or judge sees on the first screen are untouched:
- empty, flat-lit rooms;
- mannequin NPCs;
- 2 looks;
- a phone of "coming soon" apps.

The quality the user asked for (Sims-style, lush, golden hour) is a look-and-content problem more than a contact-tolerance problem.

**Integration risk compounds.** Everything verified so far is verified against fixtures. The "under an hour" target assumes both sides have already met once; they have not. The local backend is now ahead on breadth: it has M3 markets, ORE, payments, feed and DMs.

**Worth keeping.** The ownership, lifetime and failure-path hardening, and the honest failure records, are real assets for crash-free behaviour. The issue is proportion, not discipline.

`[Inference]` A healthier split for the next stretch:
- about 40% integration and the Phase-2 client (ORE, SKR, top-ups, trading);
- about 30% content breadth (cast, venues, lighting);
- about 20% device performance;
- about 10% motion polish, held to a visual threshold (no visible float or sink at the gameplay camera), not mm gates.

---

## 5. Gaps, ranked by importance to the user

| # | Gap | Why it matters | Evidence |
| --- | --- | --- | --- |
| 1 | **No end-to-end run against any backend** | This directly blocks "integration in under an hour". The first real run will surface contract drift in bulk. | `ReviewTransport.cs`; `unity.md:55`; `ct_direction.md:759` |
| 2 | **SIWS/MWA never executed on the Seeker**; the installed APK bypasses the wallet | CLOCK IN's entry rule is a native APK that uses MWA on a device. The Seeker badge depends on it. | `MwaWallet.cs`; `seeker-playable-v7` README |
| 3 | **Character creation cannot be saved against the contract** | The creator's look IDs are missing from `items.json`, so checklist step 3 fails. | `ConfigStore.cs:49`; `fe-contract-proposals/*-starter-look.md` |
| 4 | **No staging or release build path** | The staging realtime URL is a placeholder, there is no release keystore, and the APK is 162 MB (over 150). No demo video. | `EnvConfig_Staging.asset`; `ProjectSettings.asset:291` |
| 5 | **Phase-2 money layer missing on the client**: top-ups (USDC, SOL, SKR, ORE), ORE Refinery, board, staking, trading, launchpad, predictions | ORE and SKR must stay in scope and are prize-relevant. The backend endpoints already exist. | `PhoneCatalog.cs:19-24`; backend `server/api/routes/{ore,payments,markets}.ts` |
| 6 | **Cast and wardrobe**: 2 player looks; NPCs are the rejected mannequin kit or placeholders; no wardrobe | An explicit user requirement; the most visible quality gap. | `CreatorLookCatalog.asset`; `Art/characters/npc`; empty `Resources/Characters/Parts` |
| 7 | **Performance budget** | Hero meshes are 43k/77k triangles against 4–6k; 194–226 draw calls; 30 fps under the thermal cap; the stress test was never run. | `frontend-status-20261007.md:11`; v7 README |
| 8 | **World breadth**: 3 of 21 venues, no city-map scene, no art for Perp Pit, Rekt Clinic, ORE Refinery or Launchpad | Phases 1–2 need those venues. | `VenueContentRegistry.asset` |
| 9 | **Visual direction not met** | Flat overcast light, sparse rooms and web-style panels, while the lobby illustration oversells the 3D world. | `touch-city-confirmed.png`; `dashboard.png` |
| 10 | **Built features are locked and the contract is misaligned** | Perps and Spaces are hidden behind `CurrentPhase=1`. The local backend lacks RTC and Spaces routes. The contract has 16 venues against 21, a duplicate `spaces_host`, and 2 missing archetypes. | `PhoneShell.cs:45`; `contract/ids/*` |
| 11 | **6 of 9 Phase-1 mini-games are practice-only** | Their IDs are not agreed, and the backend assigns one game per career. | `JobsApp.cs:306-308`; `phase1-nine-games-20261007.md` |
| 12 | **Checklist UI gaps** | No Unblock button, no reporting from chat bubbles, the badge is a text tick, and the miner tier is ⛏ glyphs with no hard hat. | `ProfileCard.cs`; `WorldLabels.cs:120-123` |
| 13 | **Thin animation set** | 44 of 96 clips wired, no blend tree, 4 of 7 tribe signature emotes don't play, and running is still unaccepted. | `AvatarController.controller`; `frontend-status-20261007.md:29` |
| 14 | **Audio gaps** | 20 of 32 SFX are silent (trading, phone, voice rooms), no mood stems, footsteps always concrete, 20 MB of dead stems ship. | `sfx-map.json`; `MusicDirector.cs`; `LocalPlayerController.cs:453` |
| 15 | **Process risk** | 1 git commit and 903 uncommitted paths. Agents are told not to commit, so this is for the user to decide. 5 GB of receipts. The website's current draft is a hero plus one section, undeployed, with no ORE or SKR. | `git status`; `website/draft-01/` |

---

*Method notes.*
- Counts are from `wc`, `grep`, `find` and `du` on 7 Oct, and from parallel read-only sub-audits of audio, characters, website/visuals and effort, which I spot-checked: 46 controller states, 96 animation FBX, 22 NPC FBX, an empty `Parts` folder.
- Backend observations come from the local clone only. `[Unverified]` whether the remote `ct-life-backend` has moved past `a5eafda`.
