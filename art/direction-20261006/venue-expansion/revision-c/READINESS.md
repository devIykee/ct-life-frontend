# Spaces Studio: exact next-room readiness

Status: source staged outside Assets; no Studio native prefab, navigation, bake, scene capture or device evidence exists. Revision C compiles against the installed Editor references (exit 0, existing CS1701 Newtonsoft/netstandard harness warning). It does not change Café, any runtime file, the registry, contract or backend.

The first geometry blocker in B was concrete: 30 chairs had no physical colliders or authored standing approaches. Navigation checked occupied wire positions, and other furniture used tall invisible runtime boxes. C adds original LOD0 mesh collision and tags the chairs/podium under their existing anchors, separates bake-only exclusions, and walks to side approaches. It retains B's composition and original-scale sourced chairs. This is an interaction-boundary improvement, not evidence of adult chair/body fit.

There is also a current authoritative-action blocker: read-only inspection of the adjacent backend's `server/realtime/rooms/venue.ts:260` shows `player.anim = spot.kind === 'stage' ? 'act_type' : 'act_sit'`. All 34 Studio positioned anchors now exist in that checkout, matching the frontend snapshot; its `stage_host` specifies `loco_idle` and ten speaker anchors specify `act_sit`, so those 11 confirmed actions currently disagree. The frontend must not disguise that mismatch. Existing backend feedback item 13 requests honoring valid `spot.anim`. No backend mutation or message was sent.

## Frozen source and exact procedure

Import only `SpacesStudioCandidateC.cs` when root owns an idle native slot. SHA256 `c26bfaf3d4ae70e2d2ef8c22194def10a7d35891f6f0abcb801ce318f2e1ad6f`. Preserve this proposal directory, `anchor-contract.json`, original `Textures/` and `revision-b/Textures/`; the builder reads them. B remains unchanged, SHA256 `077ab3b71de0f4df5a0072ec3a4584e09225e0ebe209129f90596455e7e14176`. `B-to-C.patch` and `source-gate.json` record the narrow delta, frozen inputs and source checks.

- Stage: `CTLife.EditorTools.SpacesStudioCandidateC.BuildCandidate` in a coordinator-owned idle Unity batch process.
- Output: `Assets/Art/Candidates/SpacesStudio20261006C/SpacesStudioC.prefab` and `StageReceipt.json`.
- Stage refuses an existing receipt before entering its transaction, preserving prior evidence. Use a reviewed new version for a later revision.
- Native stage must preserve 34 exact anchors, create 31 authored approaches and 31 tagged physical claim targets, record complete paths and actual source-mesh bounds/triangle counts, and export `GeometryEvidence/audience-chair.obj` and `speaker-chair.obj` in immutable actor-anchor coordinates.
- The native high-back orientation check remains active. A wrong source axis fails staging; no assumed FBX axis or automatic pose substitution is accepted.
- Bake method exists as `CTLife.EditorTools.SpacesStudioCandidateC.BakeCandidate`, but root should schedule it after native geometry/contact/spatial acceptance. It is a separate isolated process and never registers the room.
- Internal protection covers source model dependencies, canonical Timeline, existing candidate folders, registry, packages and ProjectSettings. Root also takes pre-startup and post-exit hashes because Unity import can precede the helper. There is no global SaveAssets/SaveScene or registry promotion.

## Composition and numerical boundaries

Unchanged walkable floor: 24 × 20 m. Entrance `(0,-9)`, exit `(0,-10)`. Twenty audience chairs use five columns at X `-6,-3,0,3,6`, four rows Z `-2,-4,-6,-8`, facing +Z. Ten speaker chairs sit at X `-9,-7,...,9`, Z `6`, facing contract yaw 3.142. Host stays `(0,8)` with the podium center `(0,7.30)`. All 30 original lounge-chair models keep original scale; manifest source extents are approximately 0.905 m wide × 0.758 m deep × 0.85 m high. Source extents do not certify seat support or body clearance.

Each chair approach is global X +0.95 m from its anchor, same Z; host approach is X +1.05 m, same Z. Native NavMesh samples approaches within 0.15 m and requires PathComplete. Nonclaimable boundary anchors retain the prior 0.75 m sample allowance, and receipts must distinguish the sampled target from exact boundary arrival. Both row aisles and the entrance fork around seat_18 require real walking/collision review, not path-existence alone.

Camera remains B's landscape setup: distance 11.6 m, pitch 29°, yaw -12°, FOV 45°, aspect 16:9, feet at viewport Y 0.30, focus height 1 m. CPU projection using the exact CameraRig formula places the entrance feet at Y 0.30, the host head (1.8 m) at Y 0.894 and the identity center at Y 0.941. These are mathematical framing witnesses only: near-top identity readability and stage silhouette need native entry, center, speaker and host captures. Walnut acoustic fins, linen speaker chairs, petrol audience chairs, side conversation pockets, window recess and warm stage/cool window lighting remain the design. No live audience numbers, fake ON AIR status or fabricated host role is displayed.

## Existing evidence and remaining gates

| Boundary | Verified evidence | Required next |
| --- | --- | --- |
| Source/license | Existing Kenney CC0 lounge chair; Quaternius CC0 couches/plants; project-authored stage/podium/mic/light props; recorded Poly Haven surface textures. Original static typography has local provenance. | Preserve those sources/materials in native copy; no new asset dependency. |
| Geometry/navigation | C compiles; original anchors and source hashes guarded. | Actual stage receipt, 34 target results, source chair orientation/surface witnesses, mesh-hit and real walk review including entrance fork. |
| Adult seating | Original-scale furniture; no Studio body-contact proof. Café motions/profiles are not attached. | Snow/Rain social poses against actual chair triangles at audience and rotated speaker anchors, both entry and stand/step-out, all active LODs. If furniture support differs, author a bounded Studio-specific solution; do not reuse Café profile by name. |
| Host interaction | Podium physical click target routes the unchanged stage_host spot through existing GoSit. | Correct confirmed loco_idle action; body/podium/mic reach and host access policy. Physical spot ownership does not grant Space host role or microphone permission. |
| Spaces UI | `docs/progress/screens/spaces-ui-native-20261006/` has simulated, watermarked list/listening/host/error/ended captures and callback checks. | Real Spaces endpoints, role state/promotion/revocation and return/travel ownership. Existing Phone Spaces app remains the actual functional entry, independent of physical seat. |
| Voice | `spaces-native-20261006` receipt verifies local native SDK synthetic PCM subscription and teardown. | Actual game room/token/role integration, user microphone permission, Seeker output/capture and two-client audible acceptance. Synthetic receive proof is not a hosted session. |
| Lighting/performance | Seven planned baked lights and source LOD policies; 30 chairs total 3,840 source triangles before shell/props. | Actual stage render/triangle totals, bake bound maps, full-room/crowd Seeker profile. Spec's visible 100–150k triangle/thermal stress/APK targets remain in scope; source sums are not performance proof. |
| Reachability | Current registry has Timeline only; Studio has no built-in or Addressables scene candidate registered. | After accepted art/interaction/authority and device review, root may add the exact inspected prefab to VenueContentRegistry or its agreed streamed catalog. CityMap and VenueLoader already use that catalog. No stub should be labeled open. |

## Six phases stay intact

This advances the Studio venue inside the full plan, not a reduced game. Phase 1 city/identity still needs complete world travel/occupancy and the wider cast. Phase 2 money/markets (including ORE/SKR and Perp Pit) remains separate work, not replaced by decorative terminals. Phase 3 voice/calls includes real Spaces host/listener/speaker behavior and permission/teardown; this room alone does not finish it. Phase 4 city life includes the actual Spaces Host NPC and interactions, not a fabricated crowd. Phase 5 projects/ads retains real placement/ownership and booking flows; the podium mark is decorative, not a sold ad. Phase 6 competition and Season 1 remain in scope. Each phase retains the spec's integration, signed APK/demo and Seeker stress criteria.

## Primary sources opened during this audit

Kenney explicitly lists Furniture Kit as CC0: [official Furniture Kit](https://kenney.nl/assets/furniture-kit). Quaternius source family is recorded in the project's license manifest and linked on its [official Ultimate House Interior page](https://quaternius.com/packs/ultimatehomeinterior.html). No new download occurred.

Unity 6.3's [NavMesh.CalculatePath documentation](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AI.NavMesh.CalculatePath.html) says the Boolean result can include partial paths, hence C also checks PathComplete. Its [MeshCollider API](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/MeshCollider.html) identifies sharedMesh as collision geometry; C uses exact source meshes for static furniture rather than tall placeholder bounds. LiveKit's [publishing guide](https://docs.livekit.io/transport/media/publish/) documents Unity runtime permission requests separately from publishing; a podium/seat claim cannot stand in for that permission flow.

## Actual native room capture helper (frozen)

Import the two files listed in `native-import-manifest.json`; both compile using the actual allowed Editor references. After successful C stage, launch a graphics Editor with `-openfile /Users/mac/Documents/codes/projects/ct-life/client/Assets/Scenes/Bootstrap.unity -executeMethod CTLife.EditorTools.SpacesStudioRoomReview.Launch -ctlife-studioOutput /tmp/ct-studio-c-room-native-20261006 -ctlife-studioExit 1`. Do not add `-projectPath` or `-quit`; preserve the saved-scene guard.

It uses the original C materials, actual CameraRig, Snow world body and LocalPlayerController in Play. Thirty-one mesh rays exercise WorldInput selection at every existing claimable spot; five representative actual approach walks cover seat_18 at the entrance fork, seat_1 near the stage, both speaker-row extremes and stage_host. Five viewpoint legs plus five approach legs and return make 11 walks. Each emits one exact unchanged wire claim intent with a null channel and remains standing; no server role, seated action, audio or microphone activation is fabricated. The independent route/placement proof is intentionally separate from Studio body/contact acceptance.

Expected captures: arrival, seat_18 approach, seat_1 approach, mid-walk to stage_1, stage_1 approach, stage_10 approach, stage_host approach and return. These provide actual audience/stage/host angles with an adult standing body. A host-angle image does not mean a real Space is hosted. Zero lightmaps means unbaked spatial evidence only. Require exact image hashes, actual registered NavMesh, complete arrivals, no sampled body-proxy overlaps, five exact claim intents and empty protected drift. No Studio B prefab exists for a native negative-control comparison, so this helper does not claim one.
