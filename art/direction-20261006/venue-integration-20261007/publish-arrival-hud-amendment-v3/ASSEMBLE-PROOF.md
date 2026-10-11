# Root-owned publication handoff

Do not execute publication while V4 native acceptance or post-Editor protected-file review is pending. The V4 first actual Café image now shows HUD controls; this alone is not whole-run acceptance.

1. Inspect the completed `/tmp/ct-saved-venue-flow-arrival-queue-hud-v4-native-20261007/saved-venue-flow-review.json` and root-owned post-exit audit. Require the native process exit 0, receipt `pass`, clean cleanup and pinned files, all 16 seat cases, 10 visits/11 callbacks/9 public offline attempts/zero private joins or transport mutations, 22 passing HUD captures, all five phone restoration controls, and one actual latest-target submission with movement. Preserve earlier failed receipts.
2. Verify the frozen publisher import manifest and its exact current original baseline before importing its one Editor source. This is a publication helper change, not a re-stage. Keep the existing StageReceipt, persisted prefabs and original completed proof unchanged.
3. Copy `integration-prerequisites.completed-v1.json` to a newly named proof. Copy the `arrival_amendment` object from this directory's template into it. Resolve each null review field from the actual completed artifacts below; hash the exact file bytes. Do not invent a receipt, substitute an earlier revision or change any original art/runtime proof row.

| Amendment field | Exact source |
| --- | --- |
| `review_helper_source.path` | `art/direction-20261006/venue-integration-20261007/runtime-review/hud-restoration-v4/SavedVenueFlowReview.cs` |
| `review_helper_source.sha256` | `8c7ef897cedd7e52786b719fccdf26265135081b75a744c8b373b827ca719b71` |
| `review_input_manifest.path` | `art/direction-20261006/venue-integration-20261007/runtime-review/hud-restoration-v4/review-inputs.completed-v4.json` |
| `review_input_manifest.sha256` | `343190f489338706664975e79411a770c1dc67fdac6f2e9e1df3dca66b2f6d36` |
| `review_receipt.path` | Actual completed V4 receipt path, or its byte-identical archive |
| `review_receipt.sha256` | Compute only after the actual run has completed |

The template already pins LocalPlayer queue `43a77…`, PhoneShell `0f88…`, their preserved baseline sources and current stage proof. Do not change those source pins to make publication pass. The original completed proof SHA is `cf55beb332d441f76349080397a3ab859169812fec64079c757a7934ba41e55a`; its catalog baseline SHA is `6a6e6dc2c233b59d5bc59a9a4532c6c291fd8d8438ab15aedc49a290217ca030`.

4. Only after root's actual rendered/runtime review accepts integration, create the separate explicit acceptance document and bind its exact path/hash in the new proof's `integration_acceptance`. It must name scope `actual_saved_cafe_studio_profiles_and_travel`, approved true, and these actual persisted outputs:

| Output | SHA-256 |
| --- | --- |
| StageReceipt | `c3221e4fd249e90562bcb501f6416c17ab67680bf05501376c106ff53badb896` |
| terminal_cafe | `7415226b1301b2c636d34482b486535fb5a728df2fd8a33e31c2304fefb03e52` |
| spaces_studio | `56b2358c03fe517ae8148aa2d8e8b542a01c219c8ac492e84212ced7d885723e` |

5. Root may then run `CTLife.EditorTools.PlayableVenueIntegration.PublishReviewed` with `-ctlife-integrationProofs` naming that new pinned proof. Use the established native clean-scene/launcher procedure. The helper backs up exact catalog bytes and changes only the two public room references, preserving the selected Timeline and private Home B2. Verify the printed scoped publication receipt and post-exit catalog/whole-tree audit before calling the rooms registered.

No acceptance document, completed amendment proof, catalog write or native publication was performed while preparing this handoff. Queue/HUD native proof is separate from physical-device integration; the next APK/device review remains root-owned.
