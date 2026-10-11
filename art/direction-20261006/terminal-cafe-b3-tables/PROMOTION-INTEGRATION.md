# Café integration audit after source staging

Current source inspection: 2026-10-06. No runtime or registry changes are made by this document.

## Concrete final artifact

Build one new combined candidate from the B3Hits/B3Tables lineage. It must contain the accepted shared social chairs, the 20 cm social table/decor move if native composition is accepted, the independently accepted office chair/desk/centered keyboard, truthful source-mesh hit geometry and existing SpotAnchor ancestry. Preserve all exact contract IDs/positions/yaws; rebuild all nav exclusions from that final geometry. The current B3Social, B3Hits and B3Tables workstations remain old B2 furniture and are not suitable for final registration.

Do not modify/re-bake an old B2 prefab in place. A fresh output assembled from the latest builder prevents accidental carry-over of old `Direction solid` BoxColliders or old baked navigation/lightmaps. Retain tall 2.4 m modifier bounds only in the navigation build inputs. Actual runtime MeshColliders must reference LOD0 source triangles; block hit boxes must match authored block dimensions. After bake, inspect the serialized final hierarchy: no old `Direction solid` objects, twelve explicit tagged chair/table pairs, and no duplicated or changed SpotAnchor IDs. MeshCollider source meshes can remain original while the bake clones renderer meshes for UV2; verify they describe the same world geometry.

A fresh full-room navigation gate must exercise all 12 real approaches and verify both complete paths and direct movement around chair/table silhouettes. The existing stage gate samples an approach within 0.35 m then tests PathComplete; it does not establish that the entire drawn path or agent capsule clears physical furniture. The native camera-ray fixture establishes selection, not walking/seat authority.

## Clip integration is not part of prefab registration

`AvatarFactory` chooses `CharacterRegistry.Entry.controllerOverride` (or the global controller) at avatar creation. There is currently no per-venue seat-motion profile. `AnimationLibrary` only plays the requested state ID; `LocalPlayerController.OnServerSeat` uses the authoritative animation string. Therefore changing the venue prefab alone leaves the previous seated clips active.

Typing's persistent FK clip must pass post-save replay and the combined furniture preview with both foot IK and hand IK pass OFF before a per-look controller change. SocialBodyFitV1 was accepted only with a transient FootIK-enabled state; production act_sit is currently OFF. That discrepancy needs an explicit accepted motion integration, not a prefab assumption. Enabling/changing act_sit globally also affects Timeline benches and future lounge furniture; review those support profiles or deliberately scope a frontend profile to the appropriate seats. Both local and remote avatars must use the same visible profile, while synced IDs and authoritative actor roots remain unchanged. No new profile mechanism is implemented here.

Test the actual AvatarView crossfade into/out of each accepted seated state, repeated seat updates, local direct-input stand, empty server seat updates, late echoes, rejection and venue teardown. Earlier null-channel controller tests passed, but do not substitute for these visual transitions or connected behavior. The imported terminal animation/server mapping issue remains in frontend feedback; do not force a false server confirmation to make a typing clip appear.

## Registry and loading route

`Assets/Resources/World/VenueContentRegistry.asset` currently references only Timeline Square. `VenueContentRegistry.Find` returns the first matching non-null entry. After final gates, add exactly one `terminal_cafe` entry pointing to the final baked prefab's VenueSceneRoot; preserve the existing Timeline entry. Do not append duplicate venue IDs. No Addressables group, remote catalog or built-in scene entry is required: `VenueLoader.Load` prefers this shipped registry before scene/Addressables fallbacks.

Bake the final geometry with valid `VenueLightmaps` colors and renderer bindings for all rendered LODs. `VenueLightingBuilder` removes old bindings from its temporary root, produces fresh maps and saves that exact prefab; validate post-exit source/protected hashes. `VenueLoader` installs the prefab navigation and lighting and restores both on unload. Verify Timeline→Café→Timeline round-trip, camera bounds/arrival composition, map/district/music consistency, and no lingering lightmaps/nav/physics objects. Use an exact registered-device build afterward; native review overrides alone do not prove shipped registry resolution.

## Offline phone boundary

The ordinary review controller enters venues with `joinRoom:false`. WorldInput can select a real chair and LocalPlayerController can walk to its approach, but `ArriveAtDestination` only sends a seat request when a joined channel exists; it does not fabricate a confirmed local seat. Registering Café art will not make offline taps seat the character. Connected acceptance or a separately authorized, clearly labelled local review seating flow is needed for that phone interaction. Current ray/component fixtures deliberately do not fake server authority.

Finally measure the actual final static MeshCollider cooking, venue load time, RAM, draw cost and sustained device frame/thermal behavior with the intended cast. Old Timeline or source-triangle counts do not accept the new room. Root owns publication of the tested registry/art/controller changes; no backend or economic behavior is altered here.
