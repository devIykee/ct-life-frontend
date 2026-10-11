CHECKPOINT NOT PUSHED: committed locally on `dev` (last content commit ab203aa). GitHub rejected
the push: "This repository exceeded its LFS budget." Waiting on the user to raise the GitHub LFS
budget; see "Push" below. Do not force-push or rewrite history.

# Repo steward log

Agent: repo steward (authorized by the coordinator to stage, commit and push in this repo).
Scope: get real work into git on branch `dev`, archive large evidence to
`/Users/mac/Documents/codes/projects/ct-life-evidence/`, never delete work, never touch
`client/Library`, never run Unity or adb.

## 2026-10-07

### Setup
- Started from branch `main`, HEAD `95cd4b9` (the only commit), with no LFS and no `.gitattributes`.
- Installed Git LFS with `brew install git-lfs`: 3.8.0, which matches the latest upstream
  release `v3.8.0` (2026-08-28, checked with `gh api repos/git-lfs/git-lfs/releases/latest`).
  `git lfs install --local` was run for this repo only.
- Created branch `dev` from `95cd4b9` with `git switch -c dev`. The working tree was kept as is,
  with no reset, clean or stash.

### Ignore and attributes (repo-hygiene report §1.5, adapted)
- `.gitattributes`: the report's LFS list plus `*.FBX`. Unity YAML uses `merge=unityyamlmerge eol=lf`.
  I checked first that no `.asset`/`.unity`/`.prefab`/`.meta` file is binary or uses CRLF.
  The merge driver is not configured; each machine needs
  `git config merge.unityyamlmerge.driver "'<Unity.app>/Contents/Tools/UnityYAMLMerge' merge -p %O %B %A %A"`.
- `.gitignore`: the report's list, with three deviations:
  - `archive/` is scoped to `art/character-candidates/**/archive/`. The report's `**/archive/` would
    also hide the planned `docs/archive/`.
  - The evidence paths have no trailing slash so that they also match the symlinks described below.
  - `!client/Assets/**/*.obj.gz` keeps 48 Unity-imported GeometryEvidence files paired with their
    `.meta`. The pre-commit gate caught this.
- These are never committed: `art/character-candidates/raw/` (including
  `metaperson-evaluation-only/`), `client/.utmp/`, `client/Builds/`, `client/Library/`, `*.blend1`,
  `__pycache__/`, `*.log`, `.venv/` and `.env*`.

### Evidence archive (moved, not deleted). Manifest: `docs/evidence-archive-manifest.txt`
- 11,357 files, 8.69 GiB, each with sha256. I spot-checked one hash against `shasum`.
- Plain moves. Nothing in `client/Assets`, `client/ProjectSettings` or `client/Packages` references these:
  - `website/draft-01`, `website/evidence` and `art/gameplay-candidates`
  - in `art/character-candidates/`: `comparisons`, `evidence`, `renders`, `thumbs`,
    `user-tabs-audit`, `work`, `male-source-audit`, `snow-adult-proof`, `snow-hand-quality`,
    `export-check` and `unity-snow/calf-quality-candidate`
  - 12 superseded APKs from `client/Builds/` (1.95 GB). Kept in place:
    `ctlife-playable-landscape-v7-clean-package-20261007.apk`,
    `ctlife-playable-landscape-v6-20261007.apk` and `...-v6-clean-package-20261007.apk`.
    "The v6 APK" was ambiguous, so I kept both v6 files. The Burst debug folder is also kept.
- Moved, with a **symlink left at the old path**. The grep check found references to these paths
  in 60+ Editor review, probe and stager scripts. Those scripts read and write receipts there; examples
  are `PlayableVenueIntegration`, `SnowRunRuntimeStage` and `SnowWalkImport`. A device agent was also
  writing to the QA folder 16 minutes earlier. The symlinks keep those tools working, and new
  output now lands in the archive.
  - `docs/progress/screens` (1.46 GB)
  - `docs/qa/2026-10-07/device` (148 MB)
  - `art/character-candidates/motion-review-20261006` (4.74 GB)
  - `art/character-candidates/locomotion-quality-20261006` (595 MB)
- **Not archived; committed instead:** `art/direction-20261006`. Editor importers read it as input
  (textures, source pins, ClipCut storyboard sources), and it holds licence provenance for
  `HomeWeave.png`, which the build uses. Its 201 `*.obj.gz` frame dumps (90 MB) stay local and ignored.
  `vincent-idle` and `vincent-world-optimization` are Vincent master work (`.blend`, FBX, build
  scripts), so they are committed too.
- Junk removed (regenerable): 6 `__pycache__` dirs (excluding `.venv` and `art/raw`),
  12 `*.blend1` (172 MB) and `client/.utmp` (6.9 MB).

### Commits on `dev`
Each commit was checked with a scratchpad gate before committing. The gate checks:
- no forbidden paths
- every LFS-pattern file is stored as a pointer
- no non-LFS blob over 50 MiB
- every `client/Assets` asset is paired with its `.meta`, including ancestor folder metas
- a regex secret scan (PEM, AWS, GitHub, Slack, Google, `sk-`, Stripe, JWT, Bearer, Solana keypair
  arrays, base58 keys, assigned secrets, `.env` lines; seed and mnemonic keywords are flagged for review)

**Result:** no real secrets. The only keyword hits are anti-scam game copy in
`config_snapshot.json` and `ShiftStrings.cs`.
```
582fce7 chore(repo): add Git LFS attributes and extend ignore rules
e530dda chore(repo): untrack pycache, drop perf-test output, add evidence manifest
b45444e fix(client): add missing .meta files for generated contract code
09c118d feat(client): checkpoint runtime code, project settings and packages
b23bb72 feat(client-tools): checkpoint editor, review and probe tooling
3feb460 feat(art): checkpoint runtime characters and UI art (LFS)
3c11a0a feat(art): checkpoint world art, animations and generated venues (LFS)
56546d1 feat(art): checkpoint venue/motion candidates and venue registry (LFS)
6153e13 feat(audio): checkpoint processed audio, curation tools and licences (LFS)
b820bc7 chore(art): update pipeline, processed male set, previews and ledger
e43af51 feat(art-masters): checkpoint Snow/Rain/Vincent master sources (LFS)
e1b33f6 feat(art): checkpoint art direction sources and branding concepts
d516fc8 chore(contract): sync contract snapshot from backend main 9f3e5ef
```
These follow, in order: docs, website, tools/backend-local.

### Notes for the integration agent
- Duplicate URP global settings are **not** deleted: `client/Assets/UniversalRenderPipelineGlobalSettings {1..9}.asset`
  (+ `.meta`) are still tracked. Check which one `GraphicsSettings` references before removing any.
- 944 binaries committed in `95cd4b9` match the new LFS patterns but are still plain git blobs
  (202 MB). I did not renormalize them: that would add 202 MB of LFS while the old blobs stay in
  history. Each one moves to LFS the next time it changes. Git LFS may warn "should have been
  pointers" for these. A full fix is `git lfs migrate import --everything`, which rewrites
  history and needs a force-push. That is the user's decision.
- Review harnesses are in their own commit (`b23bb72`). They depend on the symlinked evidence paths.
- Runtime folders still contain probe code: `UI/Phone/Apps/PerpsReviewProbe.cs` and
  `Net/Voice/SpacesCaptureProbe.cs`.
In addition to the 13 commits above:
```
ebaf25b docs: checkpoint progress logs, research, handoff, QA and proposals
fb130fb feat(website): checkpoint landing page, assets and credits
ab203aa chore(tools): checkpoint local backend run scripts (work in progress)
```

### Push: BLOCKED (2026-10-07 23:05 WAT)
- Ran `git push -u origin dev`. Git LFS's pre-push upload was refused 23 times with:
  `batch response: This repository exceeded its LFS budget. The account responsible for the budget should increase it to restore access.`
  The run ended with `error: failed to push some refs to 'https://github.com/Immadominion/ct-life.git'`.
- Nothing reached GitHub: `Uploading LFS objects: 0% (0/100)`, there is no remote `dev` branch,
  and `origin/main` is still `95cd4b9`. I did not retry, force-push, or push without LFS.
  `--no-verify` would leave dangling pointers.
- The push would have sent about 24 MB of compressed git objects (11,068 objects) plus
  2,240 unique LFS objects (1,019 MB).
- [Inference] The repo had no LFS objects before this push, so the refusal comes from the
  account's LFS budget or billing setting, not from this repo's size. I could not read billing
  usage: `gh api /users/Immadominion/settings/billing/usage` needs the `user` scope, and I did not
  change auth.
- **To unblock (user action):** on GitHub, go to Settings → Billing and licensing → Budgets and
  alerts, and raise or remove the Git LFS budget, or check the LFS quota for the account. Then
  re-run `git push -u origin dev` from this repo. Nothing else needs to change.
- Local sizes: `.git` is 1.2 GB (242 MB objects, 976 MB LFS store). HEAD has 3,375 LFS files.
  The archive at `../ct-life-evidence` is 8.7 GB.

### Left uncommitted, on purpose
- `client/Assets/Scripts/Features/Money/**` (17 files, still being written by another agent at
  22:53–23:0x, and no `.meta` files yet). Committing them now would break `.meta` pairing.
- `art/character-candidates/raw/`, `**/archive/`, `*.obj.gz` frame dumps (direction, vincent-idle),
  `*.log`, `client/Assets/_Recovery`: ignored by design, still on local disk only.

### Licence and ledger gaps (re-checked 23:05, none fixed by me)
- `AssetCredits.cs` has no Vincent entry, and `art/LICENSES.md` has no Vincent runtime-derivative row.
- `art/LICENSES.md` still says the KayKit rows are "Outside-Assets" (4 rows), but 5 `Knight.fbx` copies are in `client/Assets`.
- There are no rows for the Snow/Rain RelaxedIdle clips, `HomeWeave.png` or `HomeDetailAtlas.png`. HomeWeave is used by the build.
- The AI image generator is recorded only as `"image_gen.imagegen built-in"` in `art/lobby/city-diorama.provenance.json`,
  and the ClipCut and branding manifests have the same gap. No model is named and no terms are linked.
- `art/branding/concepts` uses a Solana-mark silhouette and needs a trademark review before any public use.
