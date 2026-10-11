READY TO COMMIT: client/Assets/Scripts/Features/CityLife/ docs/progress/city-life.md docs/progress/requests.md docs/fe-contract-proposals/city-life-homes-needs.md docs/fe-contract-proposals/city-life-businesses.md docs/fe-contract-proposals/city-life-nft-gallery.md docs/fe-contract-proposals/city-life-shop-season-pass.md docs/fe-contract-proposals/ids/home-spots-city-life.md — Phase 4 City Life client: homes (5 types, rent/debt/eviction, moving, visits via home room joinById, visitor policy), needs with lazy decay + warnings + furniture actions, Sunday Unlock/Weekly Meta/Market Mood/daily clue/recap share card, Cash shop + wardrobe try-on/equip + season pass, businesses (stubbed gateway), NFT gallery wall; 129 EditMode test cases; 4 contract proposals + 1 FE spots proposal

(Unity generates the `.meta` files on first import; commit them with this. Nothing is reachable in the real flow until the
integration lead applies `docs/progress/requests.md` → "2026-10-08 city-life -> integration lead".)

# City Life lane (Phase 4) — progress log

Owner: city-life lane. Scope: `client/Assets/Scripts/Features/CityLife/` (assembly `CTLife.Features.CityLife`, C#
namespace `CTLife.CityLife`), this log, my request in `docs/progress/requests.md`, and `docs/fe-contract-proposals/city-life-*`.
Nothing else was edited. No Unity editor, no adb, no git writes. Backend read only (`ct-life-backend` @ 9f3e5ef).

## Status (2026-10-08)

Built and compiling in every pass (`compile_check.py` player / `--editor` / `--release`: 0 errors, 0 warnings in
`CTLife.Features.CityLife*`); logic covered by **129 passing NUnit cases** run out of the editor. **Not run in the Unity
editor, on the Seeker, or against a live backend**: the hooks are pending and the backend checkout lacks most Phase 4
routes in contract shape (list below). UI is functional and unstyled (existing ct-* classes only, no USS file).

## What is built

| Spec | Where | Contract endpoints / realtime | Proposal routes (behind gateways) |
| --- | --- | --- | --- |
| #8, #31 Homes: 5 types (basement → villa), current home, private home entry | `CityHomeApp` (id `home`, replaces `CTLife.UI.HomeApp`, same callbacks) | `GET /homes` (merged over `ids.homes` + `economy.rentCentsPerWeek`), `GET /me` | `Home.status/missedWeeks/frameSlots` (optional fields) |
| Rent due / debt / eviction (Sunday Unlock) | `Logic/Homes.cs` `RentLedger` + Home app rent card + hub toasts | `GET /homes nextDueAt`, `Me.cashCents/debtCents`, `economy.lottery.exit_liquidity` | `economy.rentPolicy {graceWeeks, evictToHomeId}` |
| Moving / renewing | `MoveHomeFlow` (confirm → sign → done / rejected / unknown-retry) | `POST /homes/{homeId}/rent` | — |
| Visits via `home` room | `HomeVisitFlow`, `HomeRoomClient` (`client.JoinById<DynamicSchema>(roomId)`), presence in the Home app; "Visit home" from profile card via hook | `POST /homes/{ownerId}/visit` → `{roomId}`; `home` room | `homeId` in the visit reply |
| Visitor permissions | Home app chips (Anyone / Followers / Nobody) | — | `GET/PUT /homes/me/settings`, `FORBIDDEN {reason}` |
| Furniture → needs actions | `CityLifeOverlays` furniture sheet on `WorldInput.SpotTapped` in the home (bed→sleep, desk→eat, sofa→nap/party, fridge→eat, shower, plant→touch grass, speaker→party) | `POST /needs/actions` | new `home.*` spots (FE-owned) |
| Needs display, lazy decay, warnings | `NeedsClock` / `NeedsProjection` / `NeedsWarningTracker`; `NeedsPanel` bars + "hits zero in"; toasts on escalation | `Me.needs`, `config.needs` decay; GET /me re-read every 10 min | `Me.needsAt`, `needs.warnings`, `needs.consequences` |
| #32 Sunday Unlock + Weekly Meta + Market Mood + recap/share card | `CityWeekApp` (id `week`) | `GET /meta/current`, `config.meta` rotation, city room `mood`, `GET /events/active` | `GET /me/week-recap` |
| Daily alpha clue | `DailyClueFlow` in the Week app (one guess/day, retained key) | `GET /daily-clue`, `POST /daily-clue/answer` | — |
| #33 Shop, inventory, cosmetics equip, look | `CityShopApp` (id `shop`): Cash shop, wardrobe with `AvatarPreview` try-on, `EquipFlow` | `GET /shop`, `POST /shop/purchase`, `GET /inventory`, `PATCH /characters/me/look` | — |
| #35 Season pass | Shop app tab; `IntentPurchaseFlow` (intent → MWA → poll 1 s × 30 s → pending / `payment_confirmed`) | `POST /payments/intents {kind: season_pass}`, `GET /payments/intents/{id}` | `GET /season-pass/me`, config `seasonPass.sku` |
| #30 Businesses and bad days | `CityBusinessApp` (id `biz`): catalog from `businesses.json`, buy / collect / sell-back | — | `GET/POST /businesses`, `/collect`, `/sell` (`IBusinessGateway`; stub reports UNAVAILABLE) |
| NFT gallery wall | `NftGalleryEditor` (Home app), `NftWallPresenter` (frames on `wall_N` spots), `NftImageCache` | — | `GET /nfts/me`, `GET /homes/{ownerId}/gallery`, `PUT /homes/me/gallery`, config `nft.imageHosts` |

Contract operations called: 15 (`CityLifeApi.ContractCovered`; `Tests~/contract_coverage.py` checks each exists in
`contract/openapi.yaml` and that no proposal route collides with one: RESULT ok).

### How it behaves
- **Server-authoritative.** Cash, needs, home, debt, rent, prizes and inventory only change from server replies; after
  writes the hub re-reads `GET /me` (coalesced ≤ 1/2 s). Needs between reads are a display projection
  (`value − decayPerHour × hours`, clamped) from the last server reading; the client re-reads `/me` every 10 min.
- **Idempotency.** Every write keeps one UUIDv4 key per exact request (`RetainedKey`/`RetainedWriteFlow`): unknown outcome
  (network, timeout, 5xx, unreadable reply) → "Try again" resends the same key; a definitive answer retires it; an
  unknown outcome blocks a *different* request (e.g. a different clue answer or look) until settled.
- **Fail closed.** Unknown moderation → pending; visitors never see non-`ok` art; NSFW/blocked art is never fetched; no
  configured image hosts → no fetch; unknown visitor policy → closed; no eviction countdown without a server policy; no
  rent shown when no source gives a price; no sku → season pass purchase disabled.
- **Images.** HTTPS + allow-listed hosts, no redirects, byte cap on declared and streamed size, PNG/JPEG only, pixel cap,
  2 concurrent downloads, 48 MB LRU of textures (destroyed on eviction/sign-out).
- **Tolerant parsing** of contract and backend variants (string bigints, `{items|homes|owned}` wrappers, backend uuid
  shop ids vs item keys, key/value meta rows, visit-log replies), each named below. Unsafe path ids are refused.
- **Phase gate.** Home (Phase 1) shows needs + home entry; rent/moving/visits/gallery appear at `Phases.IsOpen(4)`;
  `GET /homes` is not called before Phase 4. Shop/Week/Business are Phase 4 apps.

## Files

- `Logic/` (pure, tested): `CityWire`, `CityErrors` (+`RetainedKey`), `WeekClock`, `NeedsModel`, `Homes` (catalog,
  furniture map, lease receipt, `RentLedger`, `MoveHomeFlow`), `HomeVisit`, `Weekly` (meta, rotation, Market Mood, clue
  flow, recap/share card), `Shop` (shop/inventory parse, `LookDraft`, `EquipFlow`, season pass, `IntentPurchaseFlow`),
  `Businesses` (+ `RetainedWriteFlow`, stub gateway), `NftGallery` (items, visibility, layout, `ImageFetchPolicy`,
  `ByteLruCache`, stub gateway), `CityLifeStrings` (all copy).
- `Net/`: `CityLifeApi` (+ REST business/NFT gateways), `CityLifeHub` (+ `NeedsClock`), `HomeRoomClient`
  (+ `IHomeVisitPresenter`, `RoomOnlyHomeVisitPresenter`), `NftImageCache`.
- `UI/`: `CityLifeUi` (builders, `CityView`, `CityApp`), `NeedsPanel`, `CityHomeApp`, `NftGalleryEditor`, `CityWeekApp`,
  `CityShopApp`, `CityBusinessApp`, `CityLifeOverlays` (+ `CityLifeLinks`, `NftWallPresenter`).
- `Tests/` (EditMode, asmdef `CTLife.Features.CityLife.Tests`): `HomesTests`, `NeedsAndWeekTests`, `ShopBizNftTests`.
- `Tests~/` (ignored by Unity): `citylife_check.py`, `run_citylife_tests.sh`, `Runner.cs` (same as Money's), `contract_coverage.py`.

## Verify

```sh
python3 client/Tools~/compile_check.py --editor                       # all CT Life assemblies incl. CityLife + tests: ok, 0 warnings
python3 client/Tools~/compile_check.py --release                      # ok
client/Assets/Scripts/Features/CityLife/Tests~/run_citylife_tests.sh  # → PASSED 129  FAILED 0
#   add --head-unit CTLife.Audio (or --head-deps) if another lane's mid-edit breaks a dependency
python3 client/Assets/Scripts/Features/CityLife/Tests~/contract_coverage.py   # 15 contract ops present, 10 proposal routes distinct
```
Editor reference runner: Test Runner → EditMode → `CTLife.Features.CityLife.Tests`. Last run 2026-10-08: all of the above green.

## Hooks requested (`docs/progress/requests.md`, 2026-10-08 city-life -> integration lead)

1. `CTLife.App.asmdef` reference. 2. `PhoneCatalog`: `CityHomeApp` (param type `PhoneApp`), `CityShopApp` for the Shop
tile, plus `CityWeekApp`, `CityBusinessApp`. 3. `FlowController`: `CityHomeApp` in `BuildHudAndPhone`, `CityLifeOverlays`
in `Start`. 4. `ConfigStore.Normalize`: also copy `businesses`, `seasonPass`, `nft`. 5. `ProfileCard.VisitHome` static +
button. 6. 3D home visits: `RoomChannel.Join(roomId)` → `JoinById`, `IRealtimeService.JoinHome`, `VenueSession.EnterHomeVisit`,
App `WorldHomeVisitPresenter`. 7. Forward the backend list below and the proposals.

## Proposals written

- `docs/fe-contract-proposals/city-life-homes-needs.md` — lease state, rent policy, visitor settings, visit reply `homeId`,
  `Me.needsAt`, needs warnings/consequences, weekly recap, meta shape, clue field name, Weekly Meta theme ideas.
- `docs/fe-contract-proposals/city-life-businesses.md` — routes, config fields, CT-native bad-day table.
- `docs/fe-contract-proposals/city-life-nft-gallery.md` — routes, DAS fields (verified), moderation, image proxy, privacy.
- `docs/fe-contract-proposals/city-life-shop-season-pass.md` — season pass status + sku, shop/inventory shapes, cosmetic ideas.
- `docs/fe-contract-proposals/ids/home-spots-city-life.md` — FE-owned `home.*` furniture and `wall_1…12` frame spots.

## Backend vs contract (ct-life-backend 9f3e5ef, read-only) — for backend-dev-feedback.md

1. Homes: no `GET /homes`; `POST /homes/{id}/rent` takes a home *instance* id and only pays rent (contract: type id, signs
   a lease); `POST /homes/{id}/visit` returns a visit-log row (201) with no `roomId`; there is no `home` room
   (`server/realtime/rooms/`: city, market, space, venue, video-call). Rent worker runs Mon 00:00 UTC, not Sunday Unlock.
2. Short Cash → `VALIDATION_FAILED {reason: "insufficient cash", have, need}` (homes, cosmetics, season pass) instead of `INSUFFICIENT_CASH`.
3. Shop: `{items: [...]}` with uuid `itemId` + `itemKey`; purchase takes the uuid and returns one row (201); inventory rows differ.
4. Daily clue: answer body `{attempt}` (contract `{answer}`); one attempt/day → `FORBIDDEN already_attempted`. The client sends both keys.
5. Meta: `GET /meta/current` → `{key, value, updatedAt}`; the Sunday Unlock worker writes `{weekIso, winnersCount, losersCount, perkCents}` — no `bonus`.
6. Season pass: bought with $5 **Cash** (`POST /season-pass/buy`); spec says 5 USDC real money via payment intent.
7. Businesses: kinds `lemonade_stand…arcade` vs contract config `kols_agency…lounge`; `run` instead of accrue/collect; no list.
8. Needs: party is not venue-checked by the REST route (comment: "the realtime layer enforces the venue").

## Research and sources (all opened 2026-10-07/08; WebSearch quota was exhausted, so primary pages were fetched directly)

- `docs/research/ct-x-observations-2026-10-04.md` (read first, as instructed) and `docs/research/ct-now-2026-10.md/.json`
  v0.1 partial (read 2026-10-08 00:40). Used for: Market Mood copy ("it's so over / we're so back", cooked, shorts
  squeezed, "a memecoin will flip Bitcoin"), Conference week copy (parody **COIN2050 Singapura**, **Checkpoint London**),
  bad-day reasons (faked PnLs, KOL wallet DB 3 Oct, bundled launches, fake-death rug 3 Oct, MEV "Yoinker" 15 Sep, oil-shock
  liquidations 6–7 Oct, launchpad rewards change → **Pomp.fun**, exchange freeze → **Bitgot**), cosmetic ideas.
- Conference life: [TOKEN2049 Singapore 7–8 Oct 2026, Marina Bay Sands, After2049 party 9 Oct](https://token2049.com/singapore/);
  [Solana Breakpoint 15–17 Nov 2026, Olympia London, "The Token Supercycle"](https://solana.com/breakpoint).
- Relocation flex: [UAE: "The UAE does not levy income tax on individuals"](https://u.ae/en/information-and-services/finance-and-investment/taxation) → Marina Villa line.
- Touch grass: [Know Your Meme](https://knowyourmeme.com/memes/touch-grass) (insult telling someone to log off; Twitter use since 2015) → plant/balcony actions, sanity copy.
- Airdrop/weekly rituals: [Seeker Summer, 12 Aug 2026](https://solanamobile.com/blog/suns-out-your-seeker-has-been-busy-this-summer)
  (rounds of four apps, eight quests/badges, tiered SKR claims with 30-day windows) → `airdrop_season` meta idea.
- Solana PFP culture (gallery wall): Magic Eden stats API floors on 2026-10-08 — SMB 12.65 SOL, Mad Lads 8.50,
  Claynosaurz 7.20, Okay Bears 1.28, Retardio Cousins 0.72. Players' own NFTs only; no real names in authored content.
- [Helius DAS getAssetsByOwner](https://www.helius.dev/docs/api-reference/das/getassetsbyowner): image fields and **no moderation flag**.
- Backend zod 4.6.5 strips unknown keys (ran `z.object({attempt}).safeParse({attempt, answer})` in the backend checkout) → the
  clue answer body carries both names safely.
- Unity APIs used (`UnityWebRequest` with `redirectLimit`/`downloadedBytes`/`Abort`, `Texture2D.LoadImage`,
  `Awaitable.NextFrameAsync`, `Client.JoinById<T>` in vendored Colyseus 0.18.8) were checked only by compiling against
  6000.3.25f1 reference assemblies and the vendored SDK source. `[Unverified]` on device.

## Gaps / not done

- `[Unverified]` everything at runtime: no editor, device or live backend run. The backend checkout cannot serve the
  contract homes/visit/home-room flow yet (list above), so visits end in "this server logs visits but has no home room".
- 3D visited interior needs hook 6; only `parents_basement` has playable home content (`VenueContent.HasPlayableHome`).
- Furniture beyond bed/desk/sofa and the frame wall need authored anchors in the home prefab (proposal spots).
- HUD needs bars still show the last server snapshot (the Home app and toasts use the projection; `/me` is re-read every 10 min).
- Season pass perks/claim and on-chain cosmetic NFTs (#34) are not built; gallery "house prints" for empty walls need art.
- Weekly recap is device-local ("since you last checked") until `GET /me/week-recap` exists.

## Next

- After the hooks: run the real flow in the editor against the local backend (dev auth) with `phase=4`, check every
  loading/empty/error state, then on the Seeker; PlayMode tests for the views; re-check `docs/research/ct-now-2026-10.md`
  v1 (archetypes, fashion, Nigerian CT sections were `[pending]`) and fold new copy into `CityLifeStrings`.
