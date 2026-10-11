# Phase 6 · Competition (client lane) — 2026-10-08

Branch `feat/phase5-6` worktree `ct-life-phase56`. Spec rows #38-#44. Contract source: the PROPOSAL in
`docs/fe-contract-proposals/phase4-6/` (not merged; the backend serves only part of it). No commits were made.

## READY TO COMMIT (exact paths)

```
client/Assets/Scripts/Features/Competition.meta
client/Assets/Scripts/Features/Competition/            (whole folder, every .cs/.asmdef and its .meta; Tests~/ has no metas by design)
docs/progress/phase6-competition.md
docs/progress/requests.md                              (appended entry "2026-10-08 phase6-competition", nothing else changed)
```
Do not include `client/Assets/Scripts/Features/Ventures/` (the Phase 5 lane). Nothing outside these paths was edited; the main
checkout and the Phase 5 folder were not touched.

## What is built (`CTLife.Competition`, asmdef `CTLife.Features.Competition`, refs Core, Net, Generated, UI)

Layout follows Careers/CityLife: `Logic/` (pure, tested), `Net/` (API + hub), `UI/`, `Tests/` (NUnit EditMode), `Tests~/` (out-of-editor runner).

| Spec # | Piece | Where |
| --- | --- | --- |
| 38 tribes + district control | Tribes tab (view, pick with confirm), Districts tab (holder, since, standings, rollover time, my score), `DistrictBoard` data API for the city map (flags, `ForVenue`, `Changed`, live `district_flipped`) | `UI/LeagueApp.cs`, `Logic/Boards.cs` |
| 39 leagues + seasons | My league (rank, net worth, league board via rc1 `leaderboards?scope=league`), all leagues, last season results with rewards, rollover / league_result toasts | `UI/LeagueApp.cs`, `Logic/Realtime.cs` |
| 40 CT Beef | Challenge form (fixed topics, stake, invite or open), accept / decline / cancel, list (mine / arena), detail with countdown and server-driven voting, Elo tab + board, incoming-challenge cards, profile-card "Call out" | `UI/BeefApp.cs`, `Logic/Rules.cs` (`BeefRules`) |
| 40 arena presentation | `ArenaBoard`: featured match + plain lines for `Hook_RatioMeter`; never shows tallies before `beef_result` | `Logic/Boards.cs` |
| 41 Airdrop Farm + Sybil Barn | Daily claim loop (server numbers only: base, SGT and tribe bonus bps, today, cap, streak, countdown, history), jail state (reason, release countdown, bail with confirm when `bailable`), barn roster | `UI/FarmApp.cs`, `Logic/Rules.cs` (`FarmRules`) |
| 42 Cabal Lounge + Dubai Marina | Door (membership, invites left, host gate), venue access messaging per gated venue (`GET /venues/{id}/access`), invites for me (accept by id), invites I sent (revoke), invite a player from the profile card, pre-join gate check `CompetitionLinks.BlockMessageBeforeJoin` | `UI/CabalApp.cs`, `Logic/Rules.cs` (`GateRules`), `Net/CompetitionHub.cs` |
| 43 LLM chat (AI Agent, Sleuth) | Message list, input capped at 500, one in flight, rate-limit cooldown, quota line, moderation states (filtered, refused), disabled / not-available states, always labelled AI-generated, report reply | `UI/ChatApp.cs`, `Logic/Chat.cs` |
| 44 verified X link | Link (system browser at the server's https URL), polling + `x_link_update`, linked @handle + avatar, X attribution line, unlink with confirm; embeddable `XLinkPanel.Build()` for Settings | `UI/XLinkApp.cs`, `Logic/Rules.cs` (`XLinkRules`) |
| realtime | Tolerant parsing of the 9 Phase 6 messages, `CompetitionFeed` (state + events + toasts), duplicate-delivery safe | `Logic/Realtime.cs`, `Net/CompetitionHub.cs` (`CompetitionRuntime`) |

Phone apps (all `Phase => 6`): `league`, `beef`, `farm`, `cabal`, `npcchat`, `xlink`. All text goes through `El.Text` / `El.Btn` with rich text off
(`CompUi.Text` also sets `enableRichText = false`); copy is in `Logic/CompStrings.cs` (parody names only; topics, jail reasons, gate lines grounded in
`docs/research/ct-now-2026-10.md`: ratio fights, exposés vs denial, bundles, sybil points farms; no new web research was done for this lane).
Nothing is restyled: only the existing `ct-*` classes are used.

### Server authority (what the client refuses to decide)
- Beef: settlement, vote counting, voter eligibility, draws and Elo are the server's. The client sends intents (`open/accept/decline/cancel/votes`) and
  shows what comes back. Buttons are hidden only when the answer is certainly "no" (participants never vote). Tallies show only when the server sends
  `votes`; the "payout" line is labelled an estimate from the server's `feeBps`.
- Cabal: the model has no field for an invite code, the API has no code parameter, and tests assert that a `code` in a response is never stored or shown.
  Invites are named for one guest and accepted by id. Net-worth gates and membership are shown from `GET /venues/{id}/access`; when that endpoint is
  missing the pre-join check lets the room's own `onAuth` decide (it never blocks on a guess).
- Sybil: jail, release and bail are the server's; the farm claim button is disabled only while the server says jailed.
- X link: the OAuth round trip is entirely backend + system browser. The client opens only an https URL on x.com / twitter.com (or a subdomain); anything
  else is refused. Avatar fetch: https only, content type png/jpeg, 512 KB and 1024 px caps, no redirects. Display-only; no X action is rewarded.
- LLM: filtering, moderation and prompt-injection checks are server-side; the client only caps the input (500, the proposal's `maxLength`) and shows the
  server's `filtered` / `refused` states.
- Idempotency: every write holds a `RetainedKey` per (method, path, body), reused while an outcome is unknown, so a retry replays instead of paying twice.

### Missing-endpoint behaviour
A 404 without the contract's `{error}` body, a 405 or a 501 becomes `UNAVAILABLE` and every screen shows "Not available yet" with "Check again" (no fake
lists, no invented numbers). `LLM_DISABLED` and `X_LINK_DISABLED` (503) are separate "switched off" states. The leagues/tribes/districts tabs fail
independently, so one missing route does not blank the others.

## Backend gaps against the proposal (endpoint by endpoint)

"On main" / "new" is from the proposal README (main @ `9f3e5ef`); nothing was verified against a running server. [Unverified] for every row here.

| Endpoint | State | Client behaviour / gap |
| --- | --- | --- |
| `GET /tribes`, `GET /districts` | on main | Read tolerantly (wrapped or bare). Main seeds one district per tribe turf (README B11), not the 5 map districts; flags follow whatever the server returns. |
| `GET /districts/{id}/standings` | **new** | Missing → "Not available yet" in the standings box only. `myContribution` null for anon is handled. |
| `GET /leagues`, `GET /me/league` | on main | `membership: null` before the first rollover is handled. League board uses rc1 `GET /leaderboards/net_worth?scope=league` (rc1 route; main support not confirmed). |
| `GET /me/season-results` | **new** | Missing → block in the results box. No `seasonId` is sent (latest finished season); needs the server to default it. |
| `PATCH /characters/me/tribe` (rc1) | in rc1, once per season | Server refusal text is shown. README B5: main enforces once per ISO week, not per season. |
| `GET /beef` | **new** (header-only on main) | Tabs Mine (`scope=mine`) and Arena (`scope=all`) need it. Without it the beef list shows "Not available yet"; challenge, detail by id need `GET /beef/{id}` (**new**). |
| `POST /beef`, `/accept`, `/decline`, `GET /beef/elo` | on main | Body now sends `topicId` (+ `invitedUserId`), **not on main**: main takes `{stakeCents}` only. `decline` semantics differ on main (challenger withdraw); client uses `/cancel` for that (**new**). |
| `POST /beef/{id}/votes`, `/cancel` | **new** | Voting UI is dormant until served. Main still settles via `POST /beef/:id/settle` with no authz (handoff B1; the client never calls it). |
| `GET /leaderboards/beef_elo` | proposal adds `beef_elo` | Missing → block under the Elo tab only. |
| `GET /airdrop/farm` | **new** | Farm card shows "Not available yet"; history (`GET /airdrop/claims`) and claim (`POST /airdrop/claim`) are on main and work independently. Main applies SGT +50% only; the proposal's +15% tribe bonus and daily cap come from this endpoint. |
| `GET /me/sybil-status`, `POST /me/sybil-status/bail`, `GET /sybil-barn` | **new** | Jail card / bail / roster show "Not available yet". Main exposes `isSybilJailed` on `/me` only (not read by this lane). |
| `GET /venues/{venueId}/access` | **new** | Pre-join check returns "go ahead" and the room's `onAuth` decides. Join rejection wording for `NET_WORTH_TOO_LOW` / `INVITE_REQUIRED` / `SYBIL_JAILED` is ready (`CompErrors.JoinRejection`). [Unverified] how the Colyseus SDK surfaces an `onAuth` rejection reason (README). |
| `GET /me/cabal`, `POST /cabal/invites/{id}/revoke` | **new** | Door and revoke show "Not available yet". |
| `GET/POST /cabal/invites`, `POST /cabal/invites/{id}/accept` | on main, **insecure** | Main lists every invite with its bearer `code` and lets anyone accept by id (handoff B2). The client calls them as the proposal defines (own invites only, guest-named, accept by id) and never reads or shows a `code`. **If B2 is fixed as "accept takes `{code}` in the body" instead, this client has no code field and the accept flow needs a decision** (a code is a bearer secret; showing it only to its host is the handoff's rule). |
| `POST /llm/chat` | on main (behind `LLM_NPCS_ENABLED`) | Main's rate limit is a 200 refusal string, not 429 (README B15); until fixed the client shows that text as an NPC reply. Max message on main is 1000, the client sends ≤ 500. `quota` in the reply is optional for the client. |
| `GET /llm/quota` | **new** | Missing → the chat input is blocked with "Not available yet". |
| `POST /reports` `targetType: npc_reply` | proposal adds the enum value | Report fails with the server's error until added. |
| `GET /x/link`, `GET /x/link/start` | on main | The start URL must be https on x.com / twitter.com. [Unverified] X also serves authorize pages on other hosts; if so, widen `XLinkRules` deliberately. Callback is the server's. README: `x_user_id = '0'` unique collision for pending links on main (500). |
| `DELETE /x/link` | **new** | Missing → error text on the unlink button. |
| Realtime `district_flipped`, `season_rollover`, `league_result`, `beef_challenge`, `beef_update`, `beef_result`, `sybil_status`, `cabal_invite`, `x_link_update` | proposal (B16: main's city room sends almost nothing yet) | Parsed, tolerant, optional fields; none required for the screens to work (they fall back to REST). Also need: `beef_update` / `beef_result` broadcast on `ratio_arena` venue rooms; `kicked {reasonId: 'sybil_jailed'}`. |
| Registries | proposal | `districts.json`, `beef_topics.json`, `sybil_reasons.json` are not fetched: the 6 topic ids and 3 jail reasons are compiled into `CompStrings` (unknown ids fall back to a readable id). Topic ids sent to the server are only those 6. |

Backend questions this lane depends on (from README B5, B11, B12, B13, B15): one season clock, the district score inputs, vote eligibility and phase lengths
(the client shows no durations; it only counts down `phaseEndsAt`), which jails are bailable, and the 429 for LLM limits.

## Tests

`Tests/` (4 files, EditMode NUnit, pure logic): wire readers and model parsing (wrapped/bare, bigint strings, nulls, unknown enums), error mapping
(missing route vs contract 404, 429 retry-after, insufficient cash variants, sybil reason, gate codes), retained idempotency keys, beef rules (every
state x role, stake parsing, outcome, Elo from server values, ordering), gate / farm / league / X-link rules (URL allow-list, handle and avatar
sanitising), the 9 realtime parsers (missing fields drop the message) and the feed (toast tone by my tribe / my beef, duplicate delivery from city
and venue rooms, reset), district and arena boards, chat session (limits, in-flight, cooldown, quota window, moderation states), the REST wrapper
against a fake `IRestClient` (paths, bodies, keys, unsafe ids, no code ever sent, unsafe X URL refused), and copy checks (every key formats, no markup,
no real brand names). `Tests~/competition_check.py` also checks that every literal string key used in code exists and that all 31 routes in
`CompetitionApi.ProposalRoutes` exist in `openapi-additions.yaml`.

**224 tests, all passing** in all three modes.

## Not verified (honest limits)
- Nothing was run in Unity or on a device (instructed). UI Toolkit layout, tap targets, scroll behaviour and the avatar download are compile-checked only.
- Nothing was run against a real backend; request shapes follow the proposal, response readers are tolerant but untested against live JSON.
- The rate/quota and phase numbers shown are the server's; the only client constants are: input cap 500 (proposal), 60 s chat minute window (from
  `perMinuteLimit`), X poll every 3 s up to 40 times, chat memory 60 lines, arena memory 8 matches, avatar caps. None is a game rule.

## Verification commands (repo root)

```
python3 client/Tools~/compile_check.py             # 0 errors; 0 warnings in CTLife.Features.Competition (a Ventures lane warning exists, not mine)
python3 client/Tools~/compile_check.py --editor    # same; one warning in CTLife.Editor, not mine
python3 client/Tools~/compile_check.py --release
client/Assets/Scripts/Features/Competition/Tests~/run_competition_tests.sh [--editor|--release]   # PASSED 224 FAILED 0
python3 client/Assets/Scripts/Features/Competition/Tests~/competition_check.py                     # string keys + proposal route coverage
```
