# Reachable Café and Studio: final integration handoff

Current-source review:2026-10-07. This is a concrete implementation handoff, not another art audit. No Assets/registry/runtime change is made here.

## Current chosen art

| Contract venue | Selected source | Exact prefab SHA256 | Actual lighting |
| --- | --- | --- | --- |
| `terminal_cafe` | `Assets/Art/Candidates/TerminalCafe20261007Art5/TerminalCafeArt5.prefab` | `728bc17e662a48f95cd0c1a6f6ad1d4d6bfcb968a2b0501607c544586e296673` |3 maps,240 bound renderers |
| `spaces_studio` | `Assets/Art/Candidates/SpacesStudio20261007F4/SpacesStudioF4.prefab` | `5ab05e7d8af66c51aae22341effe5d5fd1ff12ec9b7604616f05a25166ce46d1` |3 maps,95 bound renderers |

Café's initial bake wrapper failed after maps were saved; its separate successful `BakeFinalizationReceipt.json` is authoritative for saved map/renderer revalidation, while the failed `BakeReceipt.json` stays preserved. The actual room graphics/rays/walks subsequently passed. Five generated foliage-material files changed after the earlier finalizer capture; current bytes match exact source Material objects and are frozen in `terminal-cafe-visual-20261007/revision-5/finalization/generated-foliage-current-state-audit.json`. Never label that earlier finalizer transaction post-exit clean. Current rendered Art5 state is the selected state.

Studio F4 has a normal successful `BakeReceipt.json`, complete standing sweeps and actual synthetic fitted social population/contact evidence. No further static mesh/art audit is needed absent a concrete changed surface or visible failure. Its30 chairs,34 anchors,31 approaches and floorY≈0 are held.

## Venue definitions already exist

Both IDs are already present in the imported public `venues` list (50 clients,10Hz patch rate; Café kindtrade, Studio kindstage). `Registries` supplies frontend district fallback `terminal_cafe → trenches` and `spaces_studio → timeline`; the selected prefab root fields match those parsed definitions. Do not add a new VenueDef, alter backend registry IDs or hardcode a replacement map district.

`VenueLoader.Load` prefers `Resources/World/VenueContentRegistry` before built-in/Addressables/greybox fallbacks. That registry currently has Timeline Square only. Exactly one entry for each accepted Café/Studio selection makes normal existing travel resolve authored content. No Addressables build/group, new built-in scene or remote catalog is required for those local prefab entries.

## One actual remaining motion boundary

Motion's latest source handoff confirms **no current-clock aggregate runtime proof is yet accepted**. Current live profile is Café/work-only:

- Binding: baselineController, ownAvatar, cafeController, workExitMotion.
- ExitRoute: exact normalized spotId plus venue-local ground points ending at the unchanged authored approach.
- It still requires venue`terminal_cafe`, matches exact actor Avatar/controller identity and only resolves12 known Café spots.
- Current actual work-room V3 contact/control evidence preceded a discovered first-callback clock mismatch. V4 passed at10Hz, V5 stopped before cases. Reusing old success as final acceptance would be false.
- Shared social right cases passed; left cases still need the staged prefix correction and actual Local/Remote room proof. Final social binding/route fields will come from that single accepted source freeze.

Café's four social seats and eight work seats need the combined accepted current runtime. Studio's20 audience plus10 rotated speaker chairs need the explicit social-right/social-left schema, its exact action/spot whitelist and actual room traversal. `stage_host` stays excluded; the existing backend feedback item13 for server stage action mismatch remains the single report. Directly broadening the Café-only venue guard or claiming the synthetic preview as runtime acceptance is not a shortcut.

Root chose instant seat entry as the current game interaction. Continuous entry choreography is not an additional requirement. Actual physically coherent exit/control is the remaining requirement already observed to fail in earlier builds.

## Exact promotion implementation once motion supplies that proof

1. Pin the single accepted runtime receipt, its final runtime source hashes, motion data/controllers/avatars, and the selected art/material hashes above. Do not resurrect `CafeFinalProfileAttachment.cs` against the obsolete October6Final prefab; it lacks the current work/social route/data fields.
2. Make one explicit integration copy of each selected prefab, preserving the selected source and its evidence. Add only the accepted profile/data/route references. No render geometry, material, collider, anchor, approach, nav data, camera or lightmap change is needed. Re-baking unchanged art is not needed.
3. Verify saved component bindings and exact serialized non-profile fields on the reloaded copy. Apply its existing VenueLightmaps and confirm all indices,scaleOffsets and color texture references remain valid. This is a saved-reference check, not another full static-body experiment.
4. Back up registry bytes and replace/insert exactly one matching entry per venue; reject duplicate IDs. Preserve Timeline and any other root-owned entries. Narrow-save only the registry and the two new integration prefabs. Record exact before/after hashes for reversible review.
5. Use the normal phone map/travel callbacks in the actual local review build: Timeline→Café→Studio→return, room identity, correct music/district, authored camera, visible maps, local/remote seat/exit control for the accepted scope, and resource cleanup. A direct scene override is insufficient for this integration step.
6. Root builds the APK with the actual registry and measures the final rooms on the physical Seeker. Assets under Candidates are still bundled when referenced; the folder name does not make their mobile memory/free shader cost disappear. No connected server occupancy/voice or paid behavior is inferred from a null-channel review.

This closes the route from accepted source art to reachable rooms. Home stays a separate private local selector keyed by Me.homeId and must never be inserted as an invented public venue.
