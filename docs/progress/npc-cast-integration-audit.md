# NPC cast integration audit — 2026-10-06

**Implementation checkpoint: opt-in NPC presentation plumbing is prepared and source-compiled; the M4 native fixture passed; explicit reviewed production import is still pending. No production bindings are enabled, and no backend/imported contract was changed.** The current two authored looks cannot supply the requested varied cast by themselves. Vincent remains an isolated source/adaptation candidate until the coordinating art/import review accepts it.

The original audit below records the pre-change behavior; the implemented route and validation instructions follow it.

## Original audit: why legacy NPCs remained

| Verified source | Actual behavior |
| --- | --- |
| `client/Assets/Scripts/World/Venue/VenueSession.cs:190` | Production NPC creation passes the server's `archetypeId`, `isNpc:true`, and a null look to AvatarFactory. The server map key `npcId` is available here but is not passed to character selection. |
| `client/Assets/Scripts/World/Avatars/AvatarFactory.cs:19` | NPCs use `CharacterRegistry.ForArchetype`; they do not resolve the Snow/Rain player-look entries. Supplying a look while retaining `isNpc:true` currently still ignores that look. |
| `client/Assets/Scripts/World/Avatars/CharacterRegistry.cs:67` | One prefab per archetype, then `defaultNpc`. No per-NPC selection or variant collection exists. |
| `client/Assets/Resources/Characters/CharacterRegistry.asset` | All 18 registered archetype prefab GUIDs resolve to `client/Assets/Art/characters/npc/*.fbx`, the existing legacy cast. `defaultNpc` and `defaultPlayer` are null. Snow and Rain are registered only under two complete seven-ID look keys. |
| `client/Assets/Scripts/Editor/ContentImport.cs:134` | Filename-based import adds a recognized role only if it does not already have a prefab. Re-running the importer preserves the existing legacy role bindings; importing Snow/Rain does not replace them. |
| `client/Assets/Scripts/World/Avatars/CharacterRegistry.cs:30` | `Entry.variant` is declared but no runtime consumer was found. Changing that number cannot currently create a visible variation. |
| `client/Assets/Scripts/World/Avatars/AvatarFactory.cs:107` | The fallback `NpcLook` fabricates item-like strings from the archetype and hashes only that archetype. These are not server appearance data and do not distinguish two NPCs of the same role. |

Contract inventory: 29 archetype records, **28 distinct IDs**; `spaces_host` occurs twice. Eighteen IDs have legacy prefab bindings, including all nine phase-1 IDs. Ten distinct IDs lack bindings: `ai_agent`, `cabal_insider`, `drainer`, `farmer`, `normie`, `prediction_shark_npc`, `rug_dev`, `sniper_bot`, `sybil_farmer`, `thread_boy`. There are no extra registry role keys outside the imported list. The duplicate record is not a second visual role.

Missing bindings currently fall through to the modular/procedural path. Registered legacy FBXs win before either fallback, so adding player looks alone cannot remove the rejected NPC appearance.

## Contract boundary

- The imported snapshot is `contract-v1.0.0-rc1`, commit `160d59e`, per `contract/SOURCE.md`.
- `contract/realtime/states.ts:35` and generated `client/Assets/Generated/Realtime/NpcState.cs` contain NPC archetype, state, spot, animation, followed user, dialogue/parameters, emote and start time. **No NPC look, skin, body, gender, visual-variant or outfit field is present.**
- PlayerState has its own seven-ID `look`; that is a separate schema field. Preserve it on player creation and remote-player updates.
- `contract/ids/archetypes.json` supplies role identity/name/venue/phase/line key, without an authored visual selection.
- A local archetype-to-authored-presentation mapping can use those existing IDs without changing the wire contract. It must not be presented as a server-selected outfit, or submitted as character ownership.
- Snow/Rain player item IDs remain proposals with live-config submission gates. NPC art reuse does not approve those item IDs for account creation or purchases, and should not invoke character-create endpoints.

## Design reviewed before implementation

1. Add a small FE-owned `NpcCastCatalog` resource. Each entry maps one **existing imported archetype ID** to a curated ordered list of reviewed presentation keys. Each presentation key resolves to an **exact** `CharacterRegistry` look entry; do not use the partial-item/default fallback from `ForLook` for this resolution.
2. Add an explicit `AvatarFactory.CreateNpc(name, npcId, archetypeId, parent)` route. Keep the existing player route intact. Internally use the same authored-instantiation path with the resolved entry, while retaining `isNpc`, the server archetype, voice/babble identity and NPC controller ownership.
3. Pass the server map key from `VenueSession.OnNpcAdded` into that route. Where a role actually has multiple reviewed variants, the proposed rule is FNV-1a 32-bit over UTF-8 bytes of `archetypeId + "\0" + npcId` (offset 2166136261, multiply by 16777619 after each XOR, unchecked unsigned arithmetic), then modulo the serialized variant-list length. Keep that list ordering stable within the same content release. Do not use the local player's look, random per-client selection, or the archetype alone for individual variation.
4. Reuse the **whole registry entry**: world prefab/LODs, its own Humanoid avatar, height policy and `controllerOverride`. Rain currently has `RainController.overrideController`; copying only Breeze's prefab into a legacy role would make AvatarFactory assign the shared controller and lose that override. NPCs use world prefabs, not full-detail creator prefabs.
5. Keep named-role visual choices curated. A town crier, miner and host need distinct readable silhouettes/outfits/props from real authored assets. Do not fill every role from a global two-look random pool and call the cast complete. The catalog can grow without changing controller behavior or backend schemas.
6. Make validation report unresolved/missing role appearances explicitly. Existing fallback behavior may remain during migration, but release/art acceptance must list which roles still show legacy/procedural assets. Re-running the generic character importer must not silently replace catalog assignments.
7. Handle a changed `archetypeId` under an existing NPC key explicitly. Current `OnNpcChanged` only applies behavior to the existing view, leaving the previous model/voice metadata. Either get backend confirmation that archetype is immutable for an NPC ID or rebuild its visual binding when the role changes. Do not silently assume immutability from this contract.

The implementation checkpoint below identifies which parts of this design now exist and which remain pending. No imported generated model edits are needed. If backend-controlled NPC outfits/variants become a product requirement, request an explicit additive contract field and consume it authoritatively; do not smuggle appearance through `animId`, `spotId`, `lineParams` or a client-invented look array.

## Available asset inputs and remaining blockers

- **Everyday / Snow:** exact existing look entry → `authored/Snow/EverydayWorld.prefab`, with a separate creator prefab. Shared controller currently selected.
- **Breeze / Rain:** exact existing look entry → `authored/Rain/BreezeWorld.prefab`, separate creator prefab and Rain controller override. The coordinating agent owns latest native idle/import results.
- **Vincent:** `art/character-candidates/male-source-audit/README.md` records the selected candidate and inspected source. It explicitly says not game-ready/accepted. No Vincent prefab exists under the current authored Unity character directory. Do not assign a speculative registry key or use a Snow rig as a substitute.
- **Variety still needed:** more distinct reviewed faces/body builds/hair/outfits and role props. The two complete looks do not expose a working modular wardrobe. Merely changing `variant`, display names or player item IDs produces no new geometry/material behavior today.
- Snow/Rain attribution notices already exist. Any new derivative requires its own exact source/modification notice and inclusion in the existing runtime credits/asset ledger before shipping.

## Required evidence for the implementation handoff

- Editor/source validation: every catalog role is imported; presentation references are exact, nonempty, resolve to the intended prefab and preserve controller overrides; duplicate role/variant IDs fail validation; coverage reports each missing role.
- Determinism test: same NPC/archetype key selects the same presentation across independent resolver instances; different keys exercise the configured real variants. A test must not claim that hashing prevents collisions.
- A real-engine cast fixture must call **the production `CreateNpc` path** with imported role IDs. Log role, opaque NPC fixture key, selected presentation key, world prefab and controller identity. Current `NpcNavigationReview` instead creates a player look with `isNpc:false`, then marks the view as NPC; that tests navigation/animation but does not test production selection.
- Inspect a side-by-side in-game lineup and moving mixed crowd at ordinary camera distance: each intended face/outfit/role is visibly distinct; feet/hands, talk/react/role actions and LOD transitions work; Rain keeps her native idle override. NPC voice role and state IDs remain unchanged.
- Rejoin/reorder and target/state updates must retain the selected appearance. A role change must follow the documented policy above.
- Run mixed-cast device measurements with the actual final assets. Existing `AddReviewCrowd` clones one local profile look through the player path, so its measurements do not establish a varied production-NPC cast or the new model's budget.

No Unity or ADB command was run by this agent. No claim of full cast completion, native render quality or performance is made.


## Implemented opt-in plumbing

Files:

- `client/Assets/Scripts/World/Avatars/NpcCastCatalog.cs`: FE ScriptableObject mapping imported role IDs to ordered exact seven-part presentation keys. Duplicate roles, missing/ambiguous entries, invalid/duplicate keys and an incomplete variant pool fail resolution. Missing/empty catalog retains the previous appearance route. Unity resource/prefab references use the engine null check. The deterministic FNV-1a algorithm above is implemented; the same role and NPC ID with the same ordered variant list selects the same entry. It does not promise unique appearances or stable selection after changing that list.
- `client/Assets/Scripts/World/Avatars/AvatarFactory.cs`: public `CreateNpc(name, npcId, archetypeId, parent = null, catalog = null)` resolves the catalog and passes the **whole Entry** into the existing instantiation path. Player/preview `Create` API remains intact. NPC metadata comes from the server role/map key; the selected entry supplies only presentation. World prefabs, LODs and controller overrides remain intact.
- `client/Assets/Scripts/World/Venue/VenueSession.cs`: `OnNpcAdded` passes the actual server map key to `CreateNpc`. Other behavior/state callbacks are unchanged.
- `client/Assets/Scripts/Editor/NpcCastCatalogImport.cs`: explicit reviewed import; no asset-refresh callback or automatic role assignment.
- `client/Assets/Scripts/App/Review/NpcCastReview.cs`: development-only same-production-factory fixture; root wires/runs it. Its temporary Snow/Rain role assignments are test inputs, **not approved production bindings**.

**No `Resources/Characters/NpcCastCatalog.asset` has been created by this work.** Thus the shipping/default behavior still uses the legacy/modular/procedural route. This is deliberate staged integration, not acceptance of those rejected visuals or completion of the varied-cast requirement. A changed archetype under an existing NPC key still needs the documented backend immutability confirmation or visual-rebind behavior; this creation-only change does not settle that contract question.

### Reviewed import hook

The coordinating agent owns Unity execution. Entry points:

- Read-only validation: `-executeMethod CTLife.EditorTools.NpcCastCatalogImport.ValidateReviewed -ctlife-npc-cast-manifest /absolute/path/to/reviewed.json`
- Explicit asset import: `-executeMethod CTLife.EditorTools.NpcCastCatalogImport.ImportReviewed -ctlife-npc-cast-manifest /absolute/path/to/reviewed.json`

Manifest shape (illustrative placeholders, deliberately not an approved import):

```json
{
  "version": 1,
  "approvedForRuntime": false,
  "bindings": [
    {
      "archetypeId": "<existing imported role ID>",
      "presentationKeys": ["<exact seven-part CharacterRegistry key>"],
      "reviewEvidence": ["<existing repository-relative review file>"]
    }
  ]
}
```

Validation requires an explicit `approvedForRuntime:true`, known roles from **the imported `contract/ids/archetypes.json`**, unique assignments/keys, existing local review evidence, an exact registry entry, an authored `.prefab` under `Assets/Art/characters/authored/`, a separate preview/world reference when a preview is supplied, a valid Humanoid Avatar, at least two complete world LOD tiers, and an entry/shared controller. It validates all rows before changing the asset. It reports every retained fallback role. It does not judge evidence contents or magically approve visual quality; root must inspect and approve the real assets/role assignment before supplying a manifest. An explicit approved empty binding list restores legacy fallback; absence of the resource already has that behavior.

### Encoded fixture checks — native run pending

`await NpcCastReview.Run()` uses temporary ScriptableObjects and the actual `CreateNpc` factory, without server writes or resource changes:

- Snow/Rain fixture roles instantiate exact authored **world** meshes, LOD counts/transitions, own Humanoid Avatars and the entry controller override. Role/NPC identity metadata survives animation requests.
- Thirty-two fixture IDs resolve consistently across separately created catalogs with role rows reordered (the variant-list order stays fixed). Both configured real presentations are instantiated and recreated.
- Every current ConfigStore archetype takes the empty-catalog path and is compared with the old factory route: current inventory is 28 distinct roles, with 18 legacy registry prefabs and 10 procedural fallbacks. Native counts are logged rather than fabricated.
- An invalid member invalidates its complete variant pool and takes legacy fallback. Duplicate roles and partial cosmetic keys are rejected.
- Every created GameObject/catalog is disposed; no production catalog or global role override is installed.

This is factory/asset-plumbing coverage, not proof of a varied rendered city, network-driven role changes, retargeted action quality, a mixed-cast 60fps budget, or final art approval. A temporary fixture assignment does not grant server player-look ownership. Root must inspect a real lineup and moving mixed crowd after approving actual role assets, then run the relevant device checks.

### Source verification

`python3 client/Tools~/compile_check.py --editor` passed all CT Life assemblies after the cast fixture/importer and leave diagnostic were saved. Log: `/tmp/ctlife-cast-compile.log`. Scoped `git diff --check` passed. This checks source/API compatibility against pinned Unity references; it does not replace native Unity import or Seeker execution. Root owns those next checks.

## M4 native cast receipt — 2026-10-06

[Device log](screens/quality-review-20261006m4/device.log), 10:01:50–10:01:51, records every encoded cast check passing:

- The same production `CreateNpc` route instantiated fixture Town Crier with `EverydayWorld` / `AvatarController` and fixture Spaces Host with `BreezeWorld` / **Rain Native Idle**. Both retained three world LOD tiers and exact authored mesh/Avatar checks. These role assignments remain temporary fixture inputs.
- Thirty-two IDs remained stable across independently created/reordered role catalogs; both authored entries instantiated and retained on recreation with fixed variant-list order.
- All **28** imported role IDs matched the previous route with an empty catalog: **18** registered prefabs, **10** modular/procedural fallbacks.
- Missing-member pool fell back; duplicate role and partial key checks rejected their invalid inputs.

No production catalog was enabled by these checks. This verifies selection/instantiation/fallback plumbing on the real engine, not final varied-cast appearance, user approval, crowd performance, online role mutation or paid-player cosmetic authorization. Explicit reviewed import remains root-owned.
