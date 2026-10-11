# Phase 5 client lane: Projects and ads (features #36 and #37)

Owner: phase5-ventures agent, 2026-10-08. Branch `feat/phase5-6`, worktree `ct-life-phase56`. Frontend only; no backend files touched.

**READY TO COMMIT:** `client/Assets/Scripts/Features/Ventures/` (all files, including the `.meta` files and `Tests~/`),
`docs/progress/phase5-ventures.md`, `docs/progress/requests.md` (the appended "phase5-ventures" entry) —
`feat(ventures): Phase 5 client — Ad Desk, Builder Hills projects, VC Row, tolerant realtime handling, 282 logic tests`

## What is built

Namespace `CTLife.Ventures`, assembly `CTLife.Features.Ventures` (references Core, Net, Generated, World, UI, **Money**, ColyseusSDK).
Layout follows Careers/CityLife: `Logic/` (pure, no UnityEngine except Log), `Net/`, `UI/`, `Tests/`, `Tests~/` (runner scripts).

| Piece | Files | Notes |
| --- | --- | --- |
| Ad Desk app (id `ads`, Phase 5) | `UI/AdDeskApp.cs` | Slot browser (kind filter, terms, "free from"), booking sheet (term, optional start date, https link, creative), payment, "my bookings" with live status, cancel. |
| Booking logic | `Logic/AdBookingFlow.cs`, `AdModels.cs`, `CreativeCheck.cs` | State machine Compose → Uploading → Booking → Booked; retained Idempotency-Keys for upload and booking separately; an upload that succeeded is not repeated when only the booking failed. |
| Creative validation | `Logic/CreativeCheck.cs` | PNG/JPEG by magic bytes (not file name), ≤ 1 MB (limit read from contract config `ads.json`, never widened beyond png/jpeg), declared pixel size read from the header without decoding, same decode caps as WorldLife's `AdImageCache` (4096 px a side, 8 Mpx), aspect warning against the slot's published size, bounded file read, path-root check. (Duplicated rather than referenced: a `Ventures → WorldLife` asmdef reference breaks `compile_check`'s folder order.) |
| Payment | `Logic/AdBookingFlow.cs` (`AdIntentGateway`, `AdPayments`, `PaymentPolicy`) | **Reuses the Money lane's `TopUpFlow`** (review → MWA `ITransactionSigner` → poll 1 s/30 s → pending/confirmed, persisted so an app kill during signing recovers). `POST /ads/bookings` already creates the intent, so a thin `ITopUpGateway` adapter hands that intent to `TopUpFlow` and delegates status to the shared `GET /payments/intents/{id}`. Own PlayerPrefs store key (`<user>.ads`), so a restored ad payment is never shown as a Cash top-up. Devnet only (`PaymentPolicy.DevnetOnly`). Amount shown is the server's intent amount, formatted with the currency's decimals from `economy.json`. |
| Projects / Builder Hills app (id `builder`) | `UI/BuilderApp.cs`, `Logic/ProjectModels.cs`, `ProjectRules.cs` | Tabs: my projects, hiring board (open roles / everything), role-offer inbox (accept/decline), found one (type, name, ticker). HQ screen: treasury, team (founder can remove), VC stakes, recent events (risk events highlighted from the `project_events` registry when served), fund with Cash, send hire offers, launch (quote → confirm exact cost → launch), enter HQ room. |
| Launch | `Logic/ProjectRules.cs` (`LaunchFlow`) | Launch only at a cost the player confirmed; `CONFLICT quote_changed` returns the new cost and forces a fresh confirmation. |
| VC Row app (id `vc`) | `UI/VcRowApp.cs` | Term sheets for projects you founded (accept/decline, expiry, implied valuation shown as an estimate, dump warning) and the deals board (status `dumped` highlighted). |
| Realtime | `Logic/VenturesRealtime.cs`, `Net/VenturesHub.cs` | `ad_booking_update`, `ads_changed`, `vc_offer` (+ `hire_offer`, `project_event` from the same proposal file) parsed tolerantly, malformed payloads counted and dropped; `evt_vc_dump` read from the existing `EventMessage`. Hub toasts work while the apps are closed. An unknown booking or an illegal status step triggers a re-read instead of guessing. |
| Not-available states | `VErrors` | 404 without the contract error body, 405 and 501 → `UNAVAILABLE` → "Not available yet on this server"; no fake data anywhere. |
| Venue links | `UI/VenturesSpotLinks.cs`, `VenturesSpots` | Tapping `vc_row.center`, `timeline_square.pixel_wall`, `conference_hall.center` (and the proposed `builder_hills.*` spots) opens the app. |

Rules observed: every label is `El.Text` / `El.Btn` with rich text off; names, aliases, URLs and reasons from the server are treated as untrusted
(control characters stripped, lengths capped, https-only links/images, safe ids in URL paths); nothing is priced or decided client-side (prices,
windows, sizes, statuses, costs, odds are all server values); game Cash is only ever moved *into* a project treasury and never shown as withdrawable;
real money is only the ad payment, through the Money lane's flow, devnet only; no restyling (existing `ct-*` classes only).

Content (parody only, from `docs/research/ct-now-2026-10.md` and the spec's archetype table): VC "We're excited to back you" / "Monthly unlock, sold OTC"
(research §archetype 15), "Audit passed (by a guy)" and "Funds are probably fine" (the Audit Row venue copy), "Community-owned, allegedly", L2 "the bridge is the risk".
No real brand, person or project is named. Spec rules for real advertisers are shown on the Ad Desk (no X-engagement rewards, no real airdrops, Sponsored, manual review).

## Verification (all run on 2026-10-08, no Unity, no adb)

```
python3 client/Tools~/compile_check.py                # player pass (dev): 0 errors, 0 warnings
python3 client/Tools~/compile_check.py --editor       # CTLife.Features.Ventures + .Tests: ok. (1 warning in CTLife.Editor VenueVerify.cs:58, not this lane)
python3 client/Tools~/compile_check.py --release      # CTLIFE_PROD: ok, WARNINGS: 0
client/Assets/Scripts/Features/Ventures/Tests~/run_ventures_tests.sh      # PASSED 282  FAILED 0
python3 client/Assets/Scripts/Features/Ventures/Tests~/copy_keys_check.py # every literal copy key exists (336 defined)
```
`run_ventures_tests.sh` compiles with the editor's csc (`ventures_check.py`, includes the Money unit it depends on) and runs every `[Test]`/`[TestCase]`
with the same reflection runner as Careers/CityLife. The tests are pure logic; the Unity Test Runner (`CTLife.Features.Ventures.Tests`, EditMode) remains the reference.

Test coverage (282): wire readers (ints, base-unit strings, times, lists, ids, realtime payload normalisation); error mapping (missing route vs
contract 404, conflict reasons, quote_changed, both insufficient-cash shapes, unknown-outcome rules, wallet decline); retained-key write flow; ad parsing
(rc1 array, proposal availability/booking/refund, backend variants, price scaling, unsafe urls/ids, wrapped pages and skipped rows); status lifecycle
(every legal and illegal step, cancel only before payment); my-bookings board (merge, sort, dedupe, pushed updates, resync triggers); creative validation
(magic bytes, exact 1 MB boundary, decompression bombs, JPEG segment walking, aspect warning, config limits, link rules, bounded file reads, path roots);
booking flow (happy path, key replay after unknown outcomes, no repeated upload, slot taken, start-date overlap/past/unsupported, cancellation);
payment (booking intent through Money's `TopUpFlow` to confirmed, wallet decline, expired, no tx, room confirmation, busy flow, persistence/restore,
devnet policy, money formatting); projects (parse, standing/permissions, name/ticker/cash/fund/hire validation, VC math, `LaunchFlow` incl. changed quote);
realtime (every message, every status, malformed, dispatch through the Bus); registries, copy completeness (every message formats; every status/problem/error key exists), spot mapping.

## Backend gaps against the proposal (docs/fe-contract-proposals/phase4-6), endpoint by endpoint

Read from the proposal README and `openapi-additions.yaml`; **nothing here was run against a live server** (no backend in this session) [Unverified].
"main" = the README's statement about ct-life-backend `9f3e5ef`.

| Endpoint | State per the proposal | What the client does / what it needs |
| --- | --- | --- |
| `GET /ads/slots` (rc1) | in contract; `terms[].priceUsdc` is a JSON **number** | Read as whole USDC and scaled by 1e6 [Inference]; the proposal's availability uses a base-unit string. Please make both the same (string base units). |
| `POST /uploads` (rc1) | in contract, multipart | `IRestClient` has no multipart verb, so `UnityMultipartClient` posts with the same headers. Needs a live check of the auth/idempotency behaviour. |
| `POST /ads/bookings` | main creates **no payment intent** (README obs. 8); rc1 returns `{bookingId, intent}`; proposal adds `startsAt` + 409 `slot_unavailable` | Client sends `startsAt` only when the player picks a date. If `intent` is missing the booking is kept and the screen says it cannot be paid yet (cancel offered). No currency field exists in the body, so ads are USDC-priced and paid in the intent's currency. |
| `GET /ads/bookings` | on main (`{bookings}`, `?limit=`), proposal adds `nextCursor` | Both shapes read; "Load more" appears only with a cursor. |
| `GET /ads/bookings/{id}` | new | Used after a `rejected`/`refunded` push to read refund details. 404 → ignored. |
| `POST /ads/bookings/{id}/cancel` | new | Cancel button (only for `awaiting_payment`); CONFLICT re-reads the list. Open question B8: a transfer landing after cancel. |
| `GET /ads/slots/{slotId}/availability` | new | Missing → the sheet uses the slot's own terms, says windows/creative size are unknown, and still lets the player book (server checks). |
| `GET /payments/intents/{id}` | rc1: returns `{status, signature?}` only | **No way to get the unsigned `tx` of an open intent again.** After the app restarts, an `awaiting_payment` booking can only be cancelled and re-booked. Ask: return `tx` on GET while pending, or make `POST /ads/bookings` replay (same Idempotency-Key) return the same intent. |
| `payment_confirmed` for ad intents | unspecified | The client confirms by polling the shared status route. If the server also sends `payment_confirmed {cashCents: 0}` the Bank hub will toast a Cash credit; please do not send it for non-top-up intents (or tell us). |
| `ad_booking_update`, `ads_changed` | new realtime | Parsed and handled; needs the `CityChannel` hook (requests.md #3) and a live Colyseus check that `Dictionary<string, object>` decoding works [Unverified]. |
| `GET /ads/live` | rc1 returns `imageKey` not a URL on main (README obs. 8) | WorldLife's existing parser rejects those; unchanged here. |
| `GET /projects?scope=` | new (`mine`/`hiring`/`all`) | Missing → "not available yet". |
| `POST /projects` | on main with `{kind}` only; proposal adds `name`, `ticker`, 409 `project_limit` | Client always sends name; ticker optional. **The seed cost is not exposed anywhere** (only "moves from my Cash"), so the form cannot show it up front; the server's 402/INSUFFICIENT_CASH is shown after. Ask: a cost in config/economy or a `quote` field. |
| `GET /projects/{id}` | on main (bare row); proposal `ProjectDetail` | Both read. |
| `POST /projects/{id}/fund` | new | Reply shape is not specified; the client re-reads the detail. |
| `POST /projects/{id}/offers`, `GET /me/project-offers`, `POST /project-offers/{id}/accept|decline` | new; main has direct `hire` without consent (not used) | Offers inbox + hire form built on the proposal. `hire_offer` push optional. |
| Hiring **NPCs** | no endpoint (offers take a player `userId`) | The form says NPCs cannot be hired yet. Spec item "hire NPC/players" needs a decision (NPC employees as a server concept?). |
| `DELETE /projects/{id}/members/{userId}` | new | Founder "Remove" button. |
| `GET /projects/{id}/launch-quote`, `POST /projects/{id}/launch` | main: status flip, no body; proposal adds quote + `quotedCostCents` + 409 `quote_changed` | Built on the proposal; `blockers` enum drives the copy. |
| `POST /projects/{id}/hq/visit` | new; `project_hq` room new | Ticket is read; entering needs `IProjectHqPresenter` (requests.md #6). Open question B7 (members-only HQ) is not decidable client-side. |
| `GET /projects/{id}/vc-offers`, `POST /vc-offers/{id}/accept|decline`, `GET /vc-row/deals` | new | Built on the proposal. `vc_offer` push and `evt_vc_dump` handled. Offers are fetched per founded project (max 5) because there is no `GET /me/vc-offers`; ask for one endpoint. |
| `project_event` push, `project_events` / `ad_rejection_reasons` registries | new | Toasts and labels; ids unknown to this build fall back to readable text. `/config` in the rc1 layout does not carry these registries yet (requests.md #4). |

## Open items (not blocking the logic; for the integration lead)

- Hooks 1, 2, 3 (asmdef, PhoneCatalog, CityChannel + FlowController) are needed before anything shows; 4, 5 are small; 6 and 7 are optional for a first device pass.
- No on-device check was possible (no Unity/adb by instruction): layouts were built with the existing `ct-*` classes and never rendered.
- Real device image picking, the HQ 3D room, and live backend behaviour are the three unverified areas.
- `Phases.Live` must be ≥ 5 for the apps to be usable.
