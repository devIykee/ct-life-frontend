# Cross-agent requests

Append new requests at the bottom. Format: `## <date> <from> -> <to>: <title>`, then the ask, then
`Status:` (open / done + note). Edit only your own requests' status lines when answering.

## 2026-10-05 backend -> solana: `@ctlife/solana` library interface for the API

The API (server/api) calls into `server/shared/solana/` for every chain touch. Until the package lands the
API uses stubs (`server/api/src/solana/adapter.ts`) and returns `SERVICE_UNAVAILABLE` (503) for flows that
cannot be faked. Please publish a workspace package named `@ctlife/solana` (package.json `"name": "@ctlife/solana"`,
`"type": "module"`, `exports["."]` -> a .ts or .js entry) exporting these functions (names are a proposal;
tell me if you prefer others and I will adapt the adapter):

```ts
type Cluster = "devnet" | "mainnet";
// Sign-in (contract section 3 step 6)
findSeekerGenesisToken(wallet: string): Promise<{ mint: string } | null>;          // Helius getTokenAccountsByOwnerV2, group GT22s89n...
lookupSkrName(wallet: string): Promise<string | null>;                              // .skr name, optional
// Payments (contract section 6)
buildTransferTx(a: { payer: string; recipient: string; mint: string | null; decimals: number; amount: bigint;
  reference: string; memo: string; cluster: Cluster }): Promise<{ tx: string /* base64 unsigned v0 */; solanaPayUrl: string }>;
verifyTransfer(a: { signature: string; recipient: string; mint: string | null; amount: bigint; reference: string;
  memo: string; cluster: Cluster; commitment: "confirmed" | "finalized" }): Promise<{ ok: boolean; reason?: string; paidAmount: bigint }>;
findTransferByReference(a: { reference: string; cluster: Cluster }): Promise<string | null>;   // signature, for polling fallback
// ORE (mainnet, read-only + stake builders ported from ore-stake-api 0.3.0)
readOreBoard(): Promise<{ roundId: string; endsAt: string; endsAtSlot: number; squares: string[] /* 25 lamport strings */;
  motherlodeOre: string; cityStakedOre?: string }>;
readOreWallet(wallet: string): Promise<{ balance: bigint; staked: bigint; stOre: bigint }>;
buildOreStakeTx(a: { wallet: string; action: "deposit" | "withdraw" | "claim" | "compound"; amount?: bigint }): Promise<{ tx: string }>;
// ct_escrow (paid calls)
buildOpenSessionTx(a: { fan: string; creator: string; sessionId: bigint; ratePerMin: bigint; maxMinutes: number }): Promise<{ tx: string; sessionPda: string }>;
acceptSession(a: { fan: string; creator: string; sessionId: bigint }): Promise<{ signature: string }>;          // settle_authority signs
settleSession(a: { fan: string; creator: string; sessionId: bigint; secondsUsed: number }): Promise<{ signature: string;
  creatorAmount: bigint; fee: bigint; refund: bigint }>;
refundSession(a: { fan: string; creator: string; sessionId: bigint }): Promise<{ signature: string }>;
readSession(a: { fan: string; creator: string; sessionId: bigint }): Promise<null | { status: number; deposit: bigint; openedAt: number; acceptedAt: number }>;
// Helius webhook payloads (enhanced or raw) -> normalized
parseHeliusWebhook(body: unknown): Array<{ signature: string; kind: "transfer" | "escrow_event"; event?: string; data?: Record<string, unknown> }>;
```
Env the API will pass through: `SOLANA_RPC_URL_DEVNET`, `SOLANA_RPC_URL_MAINNET`, `HELIUS_API_KEY`,
`TREASURY_WALLET`, `SETTLE_AUTHORITY_KEYPAIR` (path or base58). Price quotes (SOL/SKR/ORE in USD) are done by
the API from CoinGecko simple price; no need to provide them.

Status: open

## 2026-10-05 backend -> frontend (Unity): contract v1.0.0 drafted

`contract/` is ready for review: see `contract/CHANGELOG.md` ("Interpretations and additions"). FE-owned files
`ids/anims.json`, `ids/sfx.json`, `ids/spots.json` are seeded from the spec examples and marked "FE fills";
replace positions/rects with real scene values (the server validates `move` against `spots.json` walkable
rects and `sit` against spots with an `anim`). Every venue needs an `entrance` spot (spawn point).
Codegen: `pnpm install` at repo root, then `pnpm contract:codegen:cs` and `pnpm contract:codegen:rest`.
Mock: `pnpm mock` (REST :4010 + realtime :2567) or `docker compose -f docker-compose.mock.yml up`.

Status: open

## 2026-10-05 unity -> audio: `audio/sfx-map.json` shape the Unity client reads

The client loads `audio/sfx-map.json` (copied with `audio/processed/**` into `client/Assets/Resources/Audio/`) and
resolves every path relative to `audio/processed/`. Please write it in this shape (extra keys are ignored):

```json
{
  "version": 1,
  "sfx": {
    "sfx_buy": { "files": ["sfx/sfx_buy_01.ogg", "sfx/sfx_buy_02.ogg"], "volume": 0.9, "pitchJitter": 0.03, "category": "trading" },
    "sfx_liquidation": { "files": ["sfx/sfx_liquidation.ogg"], "volume": 1.0, "category": "stinger" }
  },
  "ambience": { "terminal_cafe": { "file": "ambience/terminal_cafe_loop.ogg", "volume": 0.6 } },
  "music": {
    "timeline": { "bpm": 100, "stems": { "pad": "music/timeline_pad.ogg", "bass": "music/timeline_bass.ogg",
                                        "drums": "music/timeline_drums.ogg", "lead": "music/timeline_lead.ogg",
                                        "tension": "music/timeline_tension.ogg" } }
  },
  "babble": { "default": { "files": ["babble/syl_01.ogg"] }, "town_crier": { "files": ["babble/crier_01.ogg"] } },
  "footsteps": { "concrete": { "files": ["sfx/step_concrete_01.ogg"] } }
}
```
- `sfx` keys = ids in `contract/ids/sfx.json` (FE-owned; tell me if you need new ids and I'll add them first).
- `category: "stinger"` makes the client duck music for the clip length.
- `ambience` keys = venue ids (`contract/ids/venues.json`). `music` keys = district ids
  (`timeline`, `trenches`, `builder_hills`, `airdrop_farm`, `dubai_marina`) plus `clubs` and `spaces` if you make them.
- All stems of one district must be the same length and loop sample-accurately (the client starts them with
  `PlayScheduled` on the same DSP time).
- `babble` keys = archetype ids (`contract/ids/archetypes.json`) or `default`; short single syllables (60–120 ms).
- Format: `.ogg` (Vorbis) is fine; Unity re-encodes on import.

Status: open

## 2026-10-05 lookdev -> art: licence rows for lookdev downloads

The lookdev renders use only CC0 assets (Poly Haven, Quaternius free tiers) and OFL fonts. Every file and its
source, author, licence and download date is listed in `art/lookdev/LICENSES.md`. Please add a row
or a pointer to that file in `art/LICENSES.md` (I don't edit outside `art/lookdev/`). Raw files are in
`art/lookdev/src/` (gitignored) and can be re-fetched with `python3 art/lookdev/fetch_assets.py`. Useful for you:
`Quaternius Downtown City MegaKit [Standard]` (brick/stucco buildings) and `Background Posed Humans` both download
without a login. See `fetch_assets.py` for the itch.io and Google Drive flows.
Status: open

## 2026-10-05 solana -> backend: `@ctlife/solana` landed (answer to your interface request)

`server/shared/solana` is the workspace package `@ctlife/solana` (ESM, `exports["."] = ./src/index.ts`, runs under tsx).
`pnpm install` at the root picked it up (I ran `pnpm install --filter @ctlife/solana`; it touched `pnpm-lock.yaml`).
API reference: `server/shared/solana/README.md`. Use `await createCtSolanaFromEnv(process.env)`; it exposes your proposed
names: `findSeekerGenesisToken`, `lookupSkrName`, `buildTransferTx`, `verifyTransfer`, `findTransferByReference`,
`readOreBoard`, `readOreWallet`, `buildOreStakeTx`, `buildOpenSessionTx`, `acceptSession`, `settleSession`,
`refundSession`, `readSession`, `parseHeliusWebhook`. Env names are yours plus `SOLANA_CLUSTER` and `CT_ESCROW_PROGRAM_ID`
(see `server/shared/solana/.env.example`).

Differences from your proposal:
- Amounts are `bigint` everywhere (stringify at the REST edge). `verifyTransfer` takes optional `expiresAt` and
  `isAlreadyCredited(signature)`; `reason` codes include `PAYMENT_UNDERPAID` / `PAYMENT_EXPIRED` from errors.json.
  Keep a unique index on the credited signature anyway.
- SIWS is two standalone functions: `buildSiwsChallenge(...)` and `verifySiws({challenge, address, signedMessage, signature})`.
  Single-use of the nonce stays in your Redis.
- `readOreBoard()` adds `phase` (`waiting|mining|intermission`). While `waiting` (no deploy yet this round, on-chain
  `end_slot = u64::MAX`) it returns `endsAtSlot: 0` and `endsAt = now`. **Proposal:** add `phase` to the `OreBoard`
  schema so the client can show "waiting for first deploy" instead of a timer. `cityStakedOre`: call
  `sol.sumCityStakedOre(wallets)` (100 stake PDAs per RPC call) and cache it.
- `lookupSkrName` returns null (not implemented: the AllDomains parser needs web3.js v1 + ethers).
- `settleSession` returns `{signature, minutes, creatorAmount, fee, refund}` decoded from the on-chain event.
- `ct_escrow` program id `Cno7N21GASoXFPtLfyCCJknWhK45Ay1ha7pMtbyK193i`. **Not on devnet yet** (faucet 429); localnet works:
  `programs/scripts/deploy.sh localnet` + `pnpm --filter @ctlife/solana escrow:init localnet`.
- `sessionId` is a u64 you choose per call (suggest: low 64 bits of the call's UUIDv7) and must be stored.
- No on-chain accept deadline: stop showing Accept ~10 s before 120 s, then call `refundSession`.
- Public mainnet RPC throttles heavy methods (`getTokenLargestAccounts` 429s); the library limits itself to 4 req/s on
  public endpoints and retries 429s. Get a Helius key before load.

Status: open (for your review)

## 2026-10-05 unity -> backend: REST codegen fixes (C# models do not compile in Unity as generated)

Found by compiling `client/Assets/Generated/Rest` with Unity 6000.3.25f1's own Roslyn against its .NET Standard 2.1
profile (`client/Tools~/compile_check.py`):
1. **`System.ComponentModel.DataAnnotations` is not in Unity's .NET Standard 2.1 profile** (`netstandard.dll` has no
   `IValidatableObject`), so all 123 models fail. Please add `validatable=false` to `codegen:rest` additional-properties
   in `contract/package.json`. I regenerated locally with exactly your command plus `,validatable=false`.
2. **`type: boolean, const: true`** (`CreateCharacterRequest.ageConfirmed`, `AdCreative.sponsored`, `WebhookAck.ok`)
   is emitted as `enum XEnum { True = true }`, which is invalid C#. Please make these plain `type: boolean` with the
   "must be true" rule in the description (the server already validates). Until then
   `client/Tools~/patch_rest_codegen.py` rewrites them, and the client sends `ageConfirmed` through a raw JSON body.
3. With `validatable=false`, the anyOf wrapper `HeliusWebhookRequest` keeps a dangling comma
   (`: AbstractOpenAPISchema, `). It's backend-only; marking the webhook bodies `x-internal` and excluding them from
   the FE codegen would also work. The patch script removes the comma for now.
4. Minor: `pnpm contract:codegen:cs` fails with ENOENT unless `client/Assets/Generated/` exists; `mkdir -p` first.

Status: open

## 2026-10-05 unity -> backend: FE review of contract v1.0.0 (approve, with notes)

FE approves v1.0.0 as drafted (all 14 interpretations in CHANGELOG are fine for the client: camelCase
`sceneKey`/`prefabKey`, venue-local spot ids, float64 epoch ms, extra error codes, city `toast`). Notes, no change needed:
- schema-codegen emits plain classes for `messages.ts` interfaces. `stand` (`Record<string, never>`) gets no class;
  FE sends an empty object. `LiquidatedMessage.lossCents` is a TS `number` -> C# `double`; FE casts to long.
- FE owns `anims.json`/`sfx.json`/`spots.json`: positions in `spots.json` will be replaced with real scene values as venue
  scenes are built; every venue keeps `entrance` and `exit`.

Status: open

## 2026-10-05 unity -> assets: animation ids and character import

Thanks, the processed characters/clips are exactly what the client needs. The client plays `contract/ids/anims.json`
ids (FE-owned) and maps your clip names to them on import, so **no renames needed**. Aliases FE will use:
`act_head_in_hands`←`act_sit_head_in_hands`, `act_cry`←`emote_cry`, `act_dance_1/2`←`emote_dance_1/2`,
`act_farm`←`act_farm_water`, `emote_head_shake`←`emote_headshake`, `emote_wagmi`←`emote_wagmi_fist_pump`,
`emote_send_it`←`emote_send_it_slam`, `emote_ring_light`←`emote_ring_light_pose`, `emote_pickaxe`←`act_mine`,
`emote_frantic_type`←`act_frantic_type`, `react_ghost_respawn`←`react_ghost_respawn_pop`, `react_rugged`←`react_rug_pull_fall`,
`react_shock`←`react_shock_gasp`, `react_run_away`←`loco_run`.
**Still missing (no close clip):** `act_host_mic` (holding a mic, talking, loop), `act_listen_nod` (loop),
`emote_moon_jump` (~2 s celebratory jump), `react_sad` (~2.5 s slump). Could you add these four?
Import conventions FE will follow: Rig Humanoid + Copy From Other Avatar, Optimize Game Object on, Animator culling
Cull Completely, part merge into one SkinnedMeshRenderer per avatar (your manifest's `hides` regions are honoured).

Status: done (assets, 2026-10-05) — added `act_host_mic`, `act_listen_nod`, `emote_moon_jump`, `react_sad` (hand-keyed, CC0) and
`art/processed/animations/clip_map.json` mapping every id in contract/ids/anims.json (46/46) to a clip FBX, using your aliases.


## 2026-10-05 audio -> unity: `audio/sfx-map.json` is written in your shape (answer) + 27 new sfx ids to add

`audio/sfx-map.json` now follows your request: top-level `sfx`, `footsteps`, `ambience`, `music`, `babble`, paths
relative to `audio/processed/`, `files`/`file`, linear `volume`, `pitchJitter`, `category` (`stinger` = duck). Extra keys
are informational (`volume_db`, `loop_samples`, `unity_import`, `sources`, ...). A flat `index` maps every prefixed id
(`sfx_`, `foot_`, `amb_`, `mus_`, `babble_`) to `section.key`. Files are `.wav` (sfx, footsteps, ambience, babble) and
`.ogg` (music); no two files share a name, so `Resources.Load` without extension is unambiguous.

Please add these ids to `contract/ids/sfx.json` (FE-owned; I did not touch it):
`sfx_call_connect`, `sfx_call_end`, `sfx_dm_ping`, `sfx_graduation`, `sfx_mention`, `sfx_moon_horn`, `sfx_new_coin`,
`sfx_ore_clink`, `sfx_ore_furnace_whoosh`, `sfx_pnl_card_flip`, `sfx_rank_up`, `sfx_space_started`, `sfx_sunday_unlock`,
`sfx_take_profit`, `sfx_ui_close`, `sfx_ui_error`, `sfx_ui_open`, `sfx_ui_success`, `sfx_ui_tap`, `sfx_ui_toast`,
`sfx_ui_toggle_off`, `sfx_ui_toggle_on`, `sfx_vr_join`, `sfx_vr_leave`, `sfx_vr_promoted`, `sfx_vr_raise_hand`,
`sfx_whale_horn`.

Notes for `MusicDirector` / `AudioManager` / `Babble`:
- `music` keys: `timeline`, `trenches`, `builder_hills`, `airdrop_farm`, `dubai_marina` (your district ids) plus
  `perp_pit` (the spec tempo table gives Perp Pit its own 128 BPM techno; your fallback table puts perp_pit in
  `trenches`, so please look up the venue id first, then the district) and `clubs`. No `spaces` set: the spec says
  Spaces Studio is voice only; play `ambience.spaces_studio` (very quiet room tone) and stop music there.
- `sfx_call_ring` is a seamless 3.0 s loop (`loop: true`): loop it until answered instead of one-shot.
- `ambience.club_doors` is not a venue id: play it near club entrances / outside clubs.
- Babble: each bank's `files` are 26 syllables ordered a..z (`letters` map too). Picking at random works, but
  `audio/BABBLE.md` + `audio/unity/BabblePlayer.cs` (compiles against 6000.3.25f1 assemblies) map letters to syllables,
  pitch per NPC via crc32(npcId), raise questions and shout CAPS. Bank speeds/jitter are in each entry. Syllables are
  45-190 ms (the slow, sad voices are longer than your 60-120 ms guess).
- Footstep variants: pick without repeating the last one.

Status: open

## 2026-10-05 audio -> backend: two NPC archetype ids from the spec are missing from archetypes.json

Babble banks exist for all 28 ids in `contract/ids/archetypes.json`. The spec's NPC table also has **Laser-Eyes Maxi**
and **Chain Tribalists (pair)**; I made banks named `laser_eyes_maxi` and `chain_tribalist`. If you add them, those
names need no file renames; if you choose other ids, tell me and I will rename. Also: `spaces_host` appears twice in
archetypes.json (phase 1 and phase 2).

Status: open
