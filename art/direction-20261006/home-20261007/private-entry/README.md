# Private own-Home entry — staged implementation, not native acceptance

This patch makes the accepted basement scene reachable from the ordinary Home phone app when the signed-in player's **actual `Me.homeId` is `parents_basement`**. It leaves public venue registration, home visits, needs/economy behavior and the room art untouched. Root owns import and native/device validation; nothing in `client/Assets` has been changed by this staging task.

## Contract and present gap

`docs/spec/contract.md:183` explicitly specifies a local own-home scene selected by `Me.homeId` before Phase 4; the `home` realtime room arrives with visits in Phase 4. `contract/openapi.yaml` lists `/homes/{ownerId}/visit` as Phase 4. No visit endpoint or public `JoinVenue("home")` is appropriate here. Needs actions remain authoritative HTTP actions usable anywhere (contract section 4); walking around a bed must not fabricate sleep effects.

The current Home app only exposes those needs actions. Public `FlowController.Travel` attempts a venue join before its offline fallback, so routing Home through it would use the wrong contract. `VenueSession` already serializes visits, cancels the previous owner, drains scene loading, releases its captured channel/voice, and owns avatars, input subscription, additive scene, navigation, camera and lightmaps. The patch reuses that owner instead of adding a second room lifecycle.

## Exact patch

`private-home.patch` is relative to `client`. `import-manifest.json` pins all 9 replacements, their live baselines, and the selected B2 prefab. `baseline/` holds the exact old bytes; `runtime/` holds the proposed bytes.

| File | Bounded change |
| --- | --- |
| `World/Venue/VenueContentRegistry.cs` | Separate `homes` list. `FindHome` resolves only the explicitly supported `parents_basement` and requires logical scene ID `home`. Public venue lookup unchanged. |
| `World/Venue/VenueLoader.cs` | `LoadPrivateHome` requires the selected shipped prefab/navigation/lightmaps, then uses the same additive scene/load/unload path. No Addressables/greybox fallback for unsupported homes. |
| `World/Venue/VenueSession.cs` | `EnterOwnHome` reads current signed-in session + `PlayerProfile.Me.HomeId`, validates identity before/after previous visit release and after load, then enters locally with no room or voice join. Home furniture taps remain inactive pending furniture contact approval. |
| `App/FlowController.cs` | One existing gameplay owner and travel gate. Inject Home callbacks, retain the previous valid public venue ID, close the phone after taking the gate, enter the local Home, return through normal public travel. Dashboard “Enter city” returns from Home. Failed entry after teardown reopens the existing dashboard, not an obsolete room. |
| `App/PhoneCatalog.cs` | Optional Home app injection; still exactly one `home` registration. |
| `UI/Phone/Apps/HomeApp.cs` | `Go home` / `Return to city` card through existing UI tokens. View/session identity and outstanding needs request gate are checked before callback. Transition is owned by gameplay, so closing the phone does not cancel it. Needs requests retain existing ownership/receipt behavior. |
| `UI/Phone/Apps/HomeStrings.cs` | Localized entry/return/unavailable/error copy. |
| `UI/Hud/Hud.cs` | Show the actual home display name; disable proximity microphone in own Home. Dashboard/lobby button retains its existing purpose. |
| `Resources/World/VenueContentRegistry.asset` | Add a separate private `homes` entry referencing the exact selected B2 prefab, without adding a public venue. |

Selected art: `Assets/Art/Candidates/ParentsBasement20261007B2/ParentsBasementB2.prefab`, SHA-256 `010c1c9ccf99d8cf6521afb8681eb68fa52ac90187cdead76f05ac89bcbc4de6`. All 6 authored `home` anchors, floor, navigation, lighting, camera, colliders and furniture are unchanged.

## Entry/return state policy

1. Home phone page checks the current session/profile and shipped scene. Null, unknown, `hacker_house`, `studio`, or any other unimplemented home remains unavailable for physical entry. Needs actions remain available. There is **no substitute basement**.
2. `Go home` takes the existing Flow travel gate synchronously, records the current valid public venue ID (or existing `StartVenue` if there is no public visit), then closes the phone. It uses the gameplay-generation token, not the detached phone-view token.
3. The existing serialized venue transition cancels/releases the public visit, loads B2 as logical `home`, installs nav/maps/camera, and creates only the local avatar. No public join, RTC, NPC population, occupancy claim or home-visit API is called.
4. `Return to city` enters the remembered public venue through ordinary travel. It restores the normal venue entry/server-selected position; it does not promise the former exact coordinate or room instance. If no remembered public ID remains valid, use the existing city start venue. This is a return destination default, never a home ownership fallback.
5. Map travel also leaves Home normally. Dashboard remains a dashboard; its Enter city action returns from Home. Gameplay cancellation/destruction invalidates all Home continuations and uses the existing scoped cleanup.
6. If loading fails after the previous visit has drained, present the dashboard with the localized failure, allowing normal city travel. Do not reattach an old channel or make a new implicit Home selection.

No new backend contract is required for this Phase 1 local path. A general Home scene mapping for other owned IDs, furnishing/contact actions, visits, rent, and exact position return are deliberately outside this bounded patch. The public city connection can remain owned by the current gameplay generation; Home creates no venue presence.

## Validation and remaining gates

Private-output CSC passed current Editor and development World/UI/App. Release validation initially exposed stale cached release inputs: absent current World source files and old Net API references. Those failed receipts are retained as `compile-first-attempt.json` and `compile-second-attempt.json`. The final script preserves release defines/external references, adds only source paths present in the current native development assembly input, and recompiles all current CTLife release dependencies into `/tmp/ct-home-private-entry-compile/player`. No Bee output/reference assembly is overwritten; all `out`, `refout`, PDB, documentation/error/generated output flags are removed before one private output is added. See `compile-receipt.json` for actual result; compilation is not runtime proof.

Root review/import should verify every baseline hash first. If another room is promoted into the public catalog meanwhile, semantically add only the `homes` entry instead of replacing newer public entries. No broad project setup/save or prefab rebake is needed.

Native acceptance must exercise the **actual Home card callbacks and Flow owner**, not only direct loader calls: city → Home → phone close/open → return; return via dashboard and map; 6 exact anchors and actual B2 lightmaps; one local avatar/input subscription/nav owner; no Home realtime/HTTP mutation/RTC calls; unsupported and changed HomeId leave the current public visit intact; repeated tap takes one transition; needs request blocks entry until completion/cancellation; owner cancellation/destruction drains the scene and does not mount late UI; repeated round trips release previous scenes/maps/avatars and maintain valid walking. Preserve diagnostic receipts/screenshots and inspect Home/phone labels at the actual landscape camera. Actual Android entry/walk/return remains pending.

Home furniture pose/contact and Home physical needs interactions are **not accepted** by this patch. An active local scene is not a server room, multiplayer proof or home-visits implementation.
