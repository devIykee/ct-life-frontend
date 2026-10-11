# City street direction study

[Inference] This is an unapproved art-direction candidate. Source compile success is not native, device or user acceptance.

The study uses existing licensed environment assets to create two readable near-entry storefronts and café/market pockets in Timeline Square. It does not create new server-facing IDs or register a new venue. Research: [asset direction](../../docs/research/ct-asset-direction-20261006.md).

## Native procedure — coordinator only

1. Before launching Unity, preserve an external exact-byte baseline of canonical Timeline dependencies, their metadata, Resources registry/config, materials and ProjectSettings. The builder's snapshot starts after editor startup; it cannot catch earlier startup reserialization.
2. With no other Unity/device/bake process running, invoke `CTLife.EditorTools.CityDirectionCandidate.BuildCandidate` in a dedicated Unity batch process. Candidate output: `Assets/Art/Candidates/CityDirection20261006/TimelineStreet.prefab`.
3. Inspect `StageReceipt.json`: stage status must be `staged_unbaked_not_published`, all spot paths complete, source placements recorded. On failure retain the error and protection receipt rather than publishing the candidate.
4. Invoke `CTLife.EditorTools.CityDirectionCandidate.BakeCandidate` in a separate batch process, graphics enabled on Apple silicon. It calls the existing isolated venue-lighting helper and requires populated `VenueLightmaps`.
5. Verify protected bytes after **each** Unity process exits. The internal transaction records its durable backup path as `/tmp/ctlife-city-direction-<phase>-<id>/transaction.json`; copy needed receipts into persistent evidence before temporary storage disappears.
6. Use the root review hook with `-ctlife-reviewVenuePrefab Assets/Art/Candidates/CityDirection20261006/TimelineStreet.prefab`. Compare both baseline and candidate with matching `-ctlife-reviewCloseCamera 1`, resolution and player position. Test landscape and portrait.
7. Inspect entrance, both storefronts, central plaza, all existing benches, alley, screens and travel out/back. Check that newly added solids do not obstruct the saved positions or leave floating/overlapping objects. The path test is not visual acceptance.

The helper uses candidate-only `SaveAssetIfDirty` and prefab writes. It never calls global `AssetDatabase.SaveAssets`, never changes the venue registry and never modifies `contract/`. On protected-byte mismatch it restores exact captured bytes without triggering reimport; the coordinator must still verify after editor exit. Old lightmaps/contact-shadow geometry are removed only from the cloned candidate because moved objects need a new bake.

## Reference records

`source-evidence/*.json` freezes live Sketchfab model metadata inspected on 6 October 2026. The `*-reference.jpg` thumbnails are visual-research references from those pages, **not downloadable model licences, game assets or textures to bundle**. No supplied Sketchfab model was downloaded. Existing reused CC0 runtime models and original CT artwork retain their records in `art/LICENSES.md` and `art/lookdev/LICENSES.md`.
