# Reachable Café and Studio — persistent integration stage

Source is staged outside Assets. No native stage, profile assignment, catalog edit or publication has been performed by this helper. Proof-template receipt hashes are null and visual acceptance fields are false; these are missing observations, not user-permission prompts.

The selected art remains Café Art5 and Studio F4. This stage copies their prefabs, retains shared art/nav/lightmap dependencies, and adds explicit profiles. It does not move furniture, anchors, colliders, camera or lights, or rebake the rooms.

## Concrete output

`CTLife.EditorTools.PlayableVenueIntegration.StageCandidates` writes only a previously absent `Assets/Art/Candidates/PlayableVenues20261007/`:

- `terminal_cafe.prefab`: 8 work and 4 social routes ending at existing authored standing approaches.
- `spaces_studio.prefab`: 20 audience and 10 speaker routes. Standing `stage_host` is excluded.
- Two persisted controllers: the exact accepted 48 states plus the two inspected exit states. Rain uses the separately replayed contact walking clip, matching the actual room helper. Public character controllers/registry remain unchanged.
- Six data assets: Snow/Rain × work/social-right/social-left. Work pairs are `(.45,0,-.05)` and `(.30,0,-.02)`. Social pairs are signed `(.375,0,-.05)` and signed `(.375,0,0)`. Studio speaker yaw≈π selects left, matching the global +X approach direction.
- One independent source stand-prefix clip copy with loop disabled. No curve authoring occurs. Saved curves, Humanoid flag and loop settings are compared after synchronous import.

Root profiles receive exact baseline-controller/own-Avatar/derived-controller bindings and relevant motion references. All 42 rows preserve existing spot IDs, action types, anchors and approaches. Non-profile rendered/collided content, nav, map texture/binding references and camera settings are compared before/after save. This does not turn representative family tests into a claim that every neighboring furniture arrangement has been animated and measured.

Motion data assembly mirrors frozen `SocialSeatExitRoomReview.MakeMotion`. Persistent `AnimatorStateMachine.AddState` already creates a state subasset, so it is not added twice. [Unity's official reference source](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Editor/Mono/Animation/StateMachine.cs) shows that implementation. Actual installed-Editor persistence still requires the native stage.

## Required evidence and stage order

Copy `integration-prerequisites.template.json` to a completed manifest outside Assets. Supply `-ctlife-integrationProofs /absolute/path/to/completed.json` to either method.

Staging requires each exact family (`work`, `cafe`, `studio-audience`, `studio-speaker`) to have pinned no-image timing/contact and diagnostic-image receipts, with each receipt matched to its exact preserved helper source through `timing_helper_source` / `visual_helper_source` path/hash, both looks/local+remote complete, no contact/cleanup failures, and cadence pass for timing. Root sets `visual_and_furniture_accepted=true` only after actual image/crossing review. Diagnostic PNG work is not timing evidence. Trace-only helper edits do not invalidate earlier receipts: their preserved source must still hash to `receipt.helper_sha256`; current runtime/data/art pins remain mandatory. The template also pins selected prefabs, Home B2, contract snapshot, runtime/clock manifests and exact controller/pose/hand/gait data. Missing or changed inputs stop before output creation.

After staging, exercise the actual **saved** prefabs/controllers through ordinary Flow/map travel and current review resolver. This closes transient-to-persisted integration; it is not further pose authoring. Record actual room hashes and StageReceipt hash. Review profile routing, return to Timeline/Home and owned cleanup without transient profile injection. Existing connected/device performance boundaries remain explicit.

## Publication

`CTLife.EditorTools.PlayableVenueIntegration.PublishReviewed` is separate. It requires all source/runtime prerequisites, unchanged staged outputs, and pinned `integration_acceptance` JSON:

```
{
  "approved": true,
  "scope": "actual_saved_cafe_studio_profiles_and_travel",
  "stage_receipt_sha256": "exact reviewed StageReceipt hash",
  "prefabs": {"terminal_cafe": "reviewed prefab hash", "spaces_studio": "reviewed prefab hash"}
}
```

It also requires exact current `catalog_baseline_sha256` and rejects a dirty catalog. It backs up catalog bytes to a unique OS temporary folder, updates only the two public venue entries, compares every other public and private Home entry, then narrowly saves that catalog. Accepted Timeline and `parents_basement` must already exist. No substitute Home or new venue/wire ID is created. Existing MapApp/contract owns public room discovery.

## Protection

The helper rejects dirty scenes, writes only isolated output while staging, and records physical-file hashes plus path sets for Assets/ProjectSettings/Packages. It reports changed/missing and newly added protected files. It never loads scene assets to clear dirty flags, globally saves, deletes failed candidates, restores user bytes or recreates virtual package aliases. Method-end results still need root's post-Editor audit. Private compilation is separate from native/profile/room/device acceptance.
