# Failed native staging: material audit, 6 October 2026

**Native staging failed. Bake did not run. No candidate is accepted or published.** This audit and restoration proposal do not modify canonical materials. Root owns any restoration and the next Unity invocation.

## Failure and preservation limit

The first native run ended with:

```
InvalidDataException: Canonical dependency changed during isolated candidate work: Assets/Art/Generated/Venues/Lighting/Materials/Display_KEEP_neon.mat
```

Evidence: `client/Logs/quality-timeline-polish-stage-20261006.log:605–616`. The save/import boundary includes five canonical material files and `ProjectSettings/ProjectSettings.asset`. All five material mtimes also changed at 11:07:27 local time. The first implementation called global `AssetDatabase.SaveAssets()`. It retained only in-memory hashes and stopped at the first mismatch; it did not keep original bytes or a complete persistent mismatch receipt. Detection was not rollback.

No pre-stage copy of these generated, untracked material YAML files was found. The earlier Rain backup contains Rain assets only. Root's `/tmp/ct-post-stage-material-baseline-20261006.zip` is a **post-failure** snapshot, not the lost original state. We cannot claim exact historical byte restoration. The first failure's complete original dependency-hash map is unavailable; the five materials are the positively identified changed asset files, not proof that every other project byte was unchanged. ProjectSettings was saved/imported too, and its exact field delta is unresolved.

## Verified pre-stage reference

The coordinator identified M5 as the installed build before staging. Its existing build receipt and the APK's independently calculated SHA256 agree:

- APK: `client/Builds/ctlife-quality-dev.apk`.
- Receipt: `docs/progress/screens/quality-map-20261006m5/build.json`.
- SHA256: `ae139b0d148731e98636d719629990837d58598e66c661242cecc39394ae6ecb`.
- Size: 166,503,982 bytes. Parsed Unity version: `6000.3.25f1`.

Read-only extraction used [UnityPy 1.25.4](https://pypi.org/project/UnityPy/), released 2026-10-03, MIT; its [primary repository](https://github.com/K0lb3/UnityPy) documents loading APKs, object iteration and type-tree reading. It was installed only in `/tmp/ct-material-audit-venv`, with no Unity runtime dependency or global Python installation. The first binary-only install could not supply `tpk_ar`; the normal pinned install built that small Python dependency successfully.

The APK stores `globalgamemanagers.assets` as seven numbered ZIP members. UnityPy's `load_assets` split-file path used its filesystem loader for those APK members, leaving this dependency unparsed. The audit explicitly joined `.split0` through `.split6` **in memory**, checked consecutive indexes, and passed the bytes through `Environment.load_file`. Shader pointer resolution then succeeded. No APK or project asset was rewritten. `PlayerSettings.parse_as_dict()` subsequently failed with `EOFError: read_str out of bounds`; that data was not guessed or used for restoration.

## Complete runtime-material comparison

YAML decimal numbers were converted to float32 before comparison to serialized APK floats. Property collections were compared as maps, including presence/absence; serialization order was not treated as a runtime change.

| Material | Confirmed M5 → current delta |
|---|---|
| `Display_KEEP_neon` | Legacy `_Color`: white `(1,1,1,1)` → `(0.25999993085861206,0.8999999761581421,0.7400000095367432,1)` |
| `Baked_M_stylized_nature_megakit__Leaves_NormalTree_C` | `m_DoubleSidedGI`: false → true; `_AlphaToMask`: 0 → 1 |
| `Baked_M_ultimate_stylized_nature__Bush_Leaves` | Same two changes |
| `Baked_M_ultimate_stylized_nature__BlossomTree_Leaves` | Same two changes |
| `Baked_M_ultimate_stylized_nature__Flowers` | Same two changes |

That is **nine property-level changes**. The neon's `_BaseColor` did not change. Its legacy `_Color` delta is not merely small rounding; it was white in M5.

The following match for all five materials:

- Exact shader names: `Universal Render Pipeline/Unlit` for neon and `Universal Render Pipeline/Baked Lit` for foliage. The foliage APK shader-file GUID also equals the current YAML GUID `0ca6dca7396eb48e5849247ffd444914`; neon's shader is in `globalgamemanagers.assets`, so identity there was checked by exact shader name.
- Every texture property, null assignment, scale and offset. All four non-null `_BaseMap` APK serialized-file GUIDs **exactly equal** the current material's texture GUIDs, and resolved names match their actual source PNG paths. This checks reference identity, not equivalence of compressed APK texture bytes to source PNG bytes.
- All valid/invalid keyword lists, RenderType tags, disabled passes, lightmap flags, instancing flag, render queue, property-name sets, integer/float/color values other than the table, and empty build-texture stacks.

Durable data: `m5-materials.json` contains the extracted five material type trees; `m5-resolved.json` contains shader/texture pointer resolutions; `semantic-comparison.json` contains current hashes, normalized runtime values and complete deltas. These files contain material data only. Scratch extraction/comparison scripts remain under `/tmp/ct-material-audit/`.

[Inference] These changes are consistent with installed URP validation. `BaseShaderGUI.cs:924–940` updates surface options, derives double-sided GI from culling, and synchronizes legacy `_Color` with `_BaseColor`. `MaterialPostprocessor.cs:106–114` can issue a global SaveAssets and persist its upgrade version. The initial run did not instrument the exact dirtying call stack, so this is not proof of one exclusive trigger. These properties affect material behavior/baking; no claim of harmlessness or visual equivalence is made.

## Narrow restoration proposal — not applied

`proposed-semantic-restoration.patch` changes exactly the nine verified properties above to their M5 values. `proposed-semantic-restoration.json` pins the current and proposed SHA256 for every file.

Coordinator sequence:

1. With Unity stopped, verify **all five** expected current hashes before any mutation; stop if any differ.
2. Preserve current bytes in a separate durable backup. The existing post-failure ZIP is also retained.
3. Apply only this reviewed patch; verify every proposed hash and rerun the runtime-field comparison.
4. Label the result **semantic restoration to M5 material values**. Do not label it restoration of historical YAML bytes, ProjectSettings, or all project state.
5. Do not alter ProjectSettings based on this audit. The M5 PlayerSettings parser failed, and the APK cannot reconstruct the complete editor settings file. Parsed BuildSettings confirms Bootstrap/Spike scenes, Unity version and the build's graphics API values; it is not a replacement for ProjectSettings.

No historical `.meta`, editor AssetVersion or YAML ordering is proposed for reconstruction. A later URP validation may normalize restored legacy fields again; that must be observed with protected byte snapshots rather than assumed absent.

## Revised importer — source compiled, native retry pending

`TimelinePolishImport.cs` now requires a dedicated batch process and captures byte copies of the canonical prefab's dependency closure, registry/snapshot, metadata and **all ProjectSettings files** before candidate work. Its external scratch transaction receipt records every original hash and backup path. It clones every inherited material into the candidate directory and uses scoped candidate saves instead of global SaveAssets. Root separately changed the bake helper to save only explicit bake outputs.

On failure it records all mismatches, preserves failed bytes, verifies/copies the original backups, invalidates the candidate BuildReceipt, and writes a durable failure receipt. It does not reimport after rollback, because that could repeat validation. **Root must verify protected hashes again after the Unity process exits.** A startup import before this transaction remains outside its protection; root's external snapshot is still relevant. Failure recovery itself requires native verification.

Latest offline command: `python3 client/Tools~/compile_check.py --editor`; all assemblies passed, including 23 Editor files. Log: `/tmp/ct-timeline-transaction-final-compile.log`. No Unity or ADB command was run by this agent. No bake, native visual acceptance or performance result is claimed.


## Coordinator restoration followed by independent read-only verification

Root applied only the proposed nine properties after checking all expected/proposed hashes, retaining `/tmp/ct-before-material-semantic-restoration-20261006.zip` and `semantic-restoration-applied.json`. This agent reran the read-only comparison: **zero runtime material-field deltas across all five files**. Receipt: `semantic-comparison-after-restoration.json`. This confirms the stated semantic restoration; it does not recover historical YAML bytes or resolve ProjectSettings. The original comparison and unapplied-proposal history remain preserved.
