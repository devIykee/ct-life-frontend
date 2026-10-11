# Arrival intent consumption amendment

Staged only. No live Assets edit, native run, profile/catalog change or claim about the original intermittent arrival failure's cause.

## Narrow runtime change

`LocalPlayerController.ArriveAtDestination` now checks the actual endpoint and a fresh complete path **before** consuming `_hasDestination`/`_pendingSpot`. A rejected early invocation preserves the accepted request so a later valid arrival can dispatch it once. `MoveTo`'s synchronous already-at-approach branch explicitly clears the newly created fields if that immediate arrival returns false. It returns the same bool and keeps the existing navigation gates, speeds, transport, standing/seated ownership and exit choreography.

The baseline consumes these fields before validation, directly observed by the separate saved-room baseline negative probe. This is correction of that observed behavior, not evidence that it caused the previous intermittent Studio diagnostic or a cure for crashes/races.

## Review revision

The existing ordinary saved-room Flow/map/Home review is preserved in its parent directory. This revision replaces only its early-arrival experiment and identifies itself as `saved-playable-venue-flow-arrival-v2`.

Before the unchanged 16 fitted sit/stand cases, Snow's actual Café agent:

1. Requests `terminal_1`, receives a deliberate early private arrival invocation while far, and must retain exact destination/spot ownership with no sit intent.
2. Actually walks there under the same 18-second bound and sends exactly one intent. Two additional private arrival calls must emit no duplicate.
3. Requests `terminal_2`, retains it across an early invocation, then cancels through the actual public API. Further invocation must not dispatch the cancelled intent.
4. Requests `terminal_2`, replaces it with `terminal_3`, rejects an early invocation without losing the newer target, and actually arrives at only `terminal_3` once under the same 18-second gate.
5. Re-requests its now-current `terminal_3` approach. The immediate successful path dispatches once, consumes its fields and cannot be duplicated by another callback.

No agent fields are injected. No seat confirmation is injected for these controls. Each actual control arrival writes full path/endpoint/pending telemetry. The original ordinary Flow sequence, transport boundaries, 42 persisted routes, all six motions and both look bindings, 16 explicitly injected confirmations, current profile art and ownership cleanup are unchanged.

A synchronous preflight-complete path becoming invalid during the immediate fresh recheck is **not exercised**: the code records that the rejected-immediate cleanup is source-reviewed only. The fixture does not inject path failure to manufacture evidence.

## Import / run

Root owns all imports and native execution. `import-manifest.json` pins the unchanged live LocalPlayer baseline, the parent frozen review baseline, both candidates and the unchanged FrontendReview hook prerequisite. The latter must already be imported by the baseline saved-flow run. Preserve the original failed/passed baseline receipt.

Use the parent launch flags, with:

```text
-ctlife-reviewSavedVenueManifest /Users/mac/Documents/codes/projects/ct-life/art/direction-20261006/venue-integration-20261007/runtime-review/arrival-hardening-v1/review-inputs.completed-v2.json
-ctlife-reviewCapture /tmp/ct-saved-venue-flow-arrival-v2-native-20261007
```

Private compilation passed for the exact World scope (one source replacement), App using normal reference dependencies plus the privately compiled candidate World, and App using actual matching CTLife DLLs plus that same World. Every inherited output/refout flag was removed, and each compiler had one `/tmp` output. This is source validation, not native acceptance.

## Publication provenance

The existing integration StageReceipt and all generated art/controller/motion assets remain unchanged. Its frozen pre-stage motion proofs pin the old LocalPlayer source. If this correction passes the actual saved-room review and root accepts it, `PlayableVenueIntegration.PublishReviewed` needs an explicit, narrowly hashed LocalPlayer baseline→candidate amendment backed by that new receipt, analogous to its existing clock amendment. Do not silently rewrite the old runtime manifest or claim the original motion proofs ran this new source. The frozen builder, completed Stage proof and catalog have not been edited here.
