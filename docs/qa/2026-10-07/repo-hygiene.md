# Repo hygiene and engineering-process audit: 2026-10-07

Scope: `/Users/mac/Documents/codes/projects/ct-life` (Unity frontend). The audit was read-only. Apart from this file, nothing was modified, staged, committed or deleted. Unity and adb were not run.

Labels: the counts and sizes come from `git status -uall -z`, `git ls-files`, `du`, `stat` and `shasum` run on 2026-10-07. Statements marked **[Inference]** are conclusions I drew from those numbers. Statements marked **[Unverified]** could not be checked. This report contains both, so treat it as partly unverified.

---

## 0. Summary and prioritized actions

| # | Priority | Action | Why |
|---|---|---|---|
| 1 | **P0 (today)** | Copy the working tree to a second location, either an external disk or cloud storage. Exclude `client/Library` (about 19 GB without Library). | 24,074 untracked files (9.5 GB) and 108 modified files exist on this one disk only. Uncommitted items include the v7 APK, the master `.blend` files for Snow/Rain/Vincent, and every authored script added after HEAD. |
| 2 | **P0** | Add the `.gitignore` and `.gitattributes` changes in section 1.5 before anything else, then run `git lfs install`. | If you commit with the current rules, you commit 6.8 GB of `art/character-candidates`, 1.46 GB of screenshots, a 106 MB zip (over GitHub's limit), `.blend1` backups, `.utmp` native build output and `__pycache__`. |
| 3 | **P0** | Make checkpoint commits on a branch, following the plan in section 4.3, and push them. The repo is private (checked with `gh`). | HEAD `95cd4b9` is the only commit. `docs/AGENTS-BRIEF.md` tells agents "Do not `git commit` or push", so the loss risk comes from that rule. Change it so the user, or the coordinating agent, commits a checkpoint every day. |
| 4 | P1 | Commit the 120 untracked `.meta` files under `client/Assets/Generated` together with their code. | HEAD tracks 117 Generated `.cs` files but none of their `.meta` files. A clean clone of HEAD would get new GUIDs for them. |
| 5 | P1 | Untrack `art/pipeline/__pycache__` (12 `.pyc`). Remove the 9 tracked duplicates `UniversalRenderPipelineGlobalSettings 1..9.asset`, after checking which one `GraphicsSettings` references. | Committed junk. |
| 6 | P1 | Move the evidence out of the repo: `docs/progress/screens` (1.46 GB) and `art/character-candidates/motion-review-20261006` (4.5 GB). Delete `raw/`, `archive/`, `export-check/` and `*.blend1`. Keep a sha256 manifest of the archive in the repo. | 8.3 GB of the 9.7 GB that is not ignored is evidence or candidates, not product. |
| 7 | P1 | Keep the v7 clean-package APK (and v6 if you want it). Publish it as a private GitHub Release asset or put it in external storage. Delete the other 13 APKs (about 2.0 GB). | 15 APKs total 2,316.7 MB. The installed build exists only in an ignored folder. |
| 8 | P1 | Delete the 21 `client/Assets/Art/Candidates/*` folders that nothing references (241 MB), after you confirm. Move the 10 folders that the build does reference to stable, non-"Candidate" paths. | Unity import time and repo weight. The build currently depends on folders named "Candidates". |
| 9 | P2 | Licence fixes from section 5: add a Vincent runtime row and wire Vincent into `AssetCredits.cs` before Vincent ships; fix the KayKit "Outside-Assets" contradiction; name the AI image generator and its terms; review the Solana-mark-derived branding concepts for trademark issues. | Attribution and provenance gaps. |
| 10 | P2 | Add `docs/README.md` as an index. Move dated audits into `docs/archive/`. Cap the size of the progress logs. | 75 docs (1.15 MB of text) with no index. |

Secrets: the scan found **no** real credentials (section 3).

---

## 1. Git state

### 1.1 Repository facts

- HEAD is `95cd4b9` ("Initial CT Life frontend: Unity client, art, audio, spec"), dated 2026-10-05 18:41 +0100. It is the only commit. `origin/main` points to the same commit, so the one commit is pushed and nothing since then is.
- Remote: `github.com/Immadominion/ct-life`. Visibility is **PRIVATE**. GitHub reports disk usage of 206,621 KB (`gh repo view`).
- HEAD contains 1,959 tracked files, about 261 MB of blobs, stored as 2,267 loose objects (213 MiB) with no packs.
- There is no `.gitattributes` file and no LFS.
- `.git/sg-hook-once-toolu_…` is a stray marker file left by an agent hook. It is harmless and untracked.

### 1.2 Working-tree changes

`git status --porcelain` shows 902 top-level entries (22 D, 108 M, 772 ??). Expanding the untracked folders with `-uall` gives **24,204 entries: 108 modified, 22 deleted and 24,074 untracked files, totalling 9,538.5 MB.**

**Modified (108)**

| Area | Count | Notes |
|---|---:|---|
| `client/Assets` | 62 | 50 `.cs` files plus URP, Addressables, `link.xml`, `PanelSettings`, `ctlife.uss`, `config_snapshot.json` |
| `art/processed` | 13 | male body FBX, eye and skin textures, manifest |
| `art/pipeline` | 8 | **3 of these are `.pyc`** |
| `audio` + `audio/tools` | 6 | |
| `docs/progress` | 5 | `assets`, `audio`, `backend-dev-feedback`, `requests`, `unity` |
| `client/ProjectSettings` | 4 | |
| `client/Tools~` | 3 | |
| `client/Packages` | 2 | `com.unity.analytics` and `com.unity.purchasing` removed from `manifest.json` |
| others | 5 | `README.md`, `art/LICENSES.md`, `client.slnx`, `docs/AGENTS-BRIEF.md`, `fe-contract-proposals/ids/sfx.json` |

Diff stat for the tracked files: 130 files, +10,318 / −3,732 lines.

**Deleted (22)**
- 18 files `art/previews/*.png`. They were replaced by 18 untracked `.jpg` files: 11 MB instead of about 60 MB. That is fine, but the PNGs remain in history.
- 4 files `client/Assets/Resources/PerformanceTestRun{Info,Settings}.json(.meta)`. These are generated by the performance-testing package. Deleting them is correct, and they should be ignored.

**Untracked (24,074 files, 9,538.5 MB)**

| Area | Files | MB |
|---|---:|---:|
| `art/character-candidates` | 8,866 | 6,832.5 |
| `docs/progress/screens` | 2,014 | 1,458.4 |
| `client/Assets/Art` (whole folder untracked) | 7,492 | 796.1 |
| `client/Assets/Resources` | 2,064 | 136.4 |
| `art/direction-20261006` | 802 | 117.7 |
| `audio/processed` | 867 | 114.3 |
| `website` (draft-01 122, assets 32, evidence 12, root 5) | 171 | 41.1 |
| `art/previews` (jpg) | 18 | 10.7 |
| `art/gameplay-candidates` | 908 | 9.0 |
| `client/.utmp` | 110 | 6.5 |
| `client/Assets/Scripts` (253 `.cs`, 139 of them Editor) | 509 | 5.1 |
| `audio/candidates`, `art/branding` | 5 | 8.9 |
| `docs/progress` (text logs) | 55 | 0.8 |
| `client/Assets/Generated` (**120 `.meta` only**) | 120 | <0.1 |
| everything else (docs/research, handoff, design, fe-contract-proposals, audio/tools, audio/sources, Tools~, ProjectSettings, _Recovery…) | ~73 | <1 |

### 1.3 Tracked files that should not be tracked

| Path | Count | Problem |
|---|---:|---|
| `art/pipeline/__pycache__/*.pyc` (cpython-313 and 314) | 12 | Python bytecode. 3 of them already show as modified. |
| `client/Assets/UniversalRenderPipelineGlobalSettings {1..9}.asset` (+ `.meta`) | 18 | Duplicates that Unity created automatically. Only the unnumbered file should exist. [Inference] It is the one `GraphicsSettings` references; check before deleting. |
| `art/lookdev/*.blend` (11.4 + 10.8 MB), `art/previews/*.png` and `art/lookdev/renders/*` (4–5.5 MB each) | ~30 | Large binaries committed without LFS. They are now part of history. |
| `client/.vscode/*` | 3 | Editor settings. Optional, since some teams keep them. |
| **Missing:** `.meta` for `client/Assets/Generated/**` | 117 assets | HEAD has the `.cs` files but none of their `.meta` files. A fresh clone would assign new GUIDs. **Commit the 120 untracked metas together with the code.** |

No `.meta` is currently missing or orphaned anywhere in `client/Assets`. `m_SerializationMode: 2` (Force Text) is set, which is good.

### 1.4 Untracked items that should be ignored rather than committed

| Pattern | Files | MB |
|---|---:|---:|
| `client/.utmp/` (Android CMake/ninja/`.o` intermediates) | 110 | 6.5 |
| `**/__pycache__/`, `*.pyc` (e.g. `client/Tools~/__pycache__`) | 15 | 0.2 |
| `*.blend1` (Blender autosave) | 15 | 141.7 |
| `*.log` | 106 | 3.8 |
| `client/Assets/_Recovery/` (crash-recovery scene `0.unity`) + `.meta` | 3 | <0.1 |
| `art/character-candidates/raw/` (third-party source downloads, including **proprietary MetaPerson samples**) | 69 | 517.7 |
| `**/archive/` (superseded zips and blends) | 11 | 131.5 |
| `art/character-candidates/export-check/` (unoptimized GLBs, 90 MB and 78 MB) | 3 | 168.3 |
| `*.zip` (including `raw/snow/snow.zip`, **106.4 MB, which GitHub would block**) | 7 | 288.5 |

These should be archived outside the repo rather than committed. See section 4.4.

| Area | Files | MB |
|---|---:|---:|
| `docs/progress/screens/` | 2,014 | 1,458.4 |
| `art/character-candidates/motion-review-20261006/` (5,493 `.obj.gz` frame dumps) | 7,406 | 4,518.4 |
| `client/Assets/Art/characters/authored/MotionReview20261006/` | 121 | 114.3 |
| `website/draft-01/` | 122 | 28.1 |
| `art/gameplay-candidates/` (copies and patches of `.cs` staged outside Unity) | 908 | 9.0 |

### 1.5 Gaps in `.gitignore` and `.gitattributes`

The current root `.gitignore` covers the Unity Library, Temp, Obj, Build(s), Logs, UserSettings, csproj/sln, `*.apk`/`*.aab`, `node_modules`, `.env*`, `*.keypair.json`, `art/raw/`, `audio/raw/` and `.DS_Store`. Nested ignores in `audio/` and `art/lookdev/` cover their `.venv`, `__pycache__`, `src/` and `*.blend1`, but only inside those two folders.

These are not covered:

```gitignore
# Python (repo-wide; art/pipeline and client/Tools~ are not covered today)
__pycache__/
*.py[cod]
.venv/

# Unity extras
client/.utmp/
client/Assets/_Recovery/
client/Assets/_Recovery.meta
client/[Mm]emoryCaptures/
client/.vs/
client/*.userprefs
client/Assets/Resources/PerformanceTestRun*.json*
client/Builds/          # already covered; also add:
*_BurstDebugInformation_DoNotShip/
*.apks
*.symbols.zip

# Blender
*.blend1
*.blend2

# Logs / caches / raw downloads
*.log
art/character-candidates/raw/
art/character-candidates/export-check/
**/archive/

# Evidence (archive outside the repo; keep a sha256 manifest in docs/)
docs/progress/screens/
art/character-candidates/motion-review-*/
*.obj.gz

# macOS
._*
.AppleDouble
.Spotlight-V100
.Trashes
```

Proposed `.gitattributes`, to be added before the first binary commit:

```gitattributes
# Unity YAML
*.unity    merge=unityyamlmerge eol=lf
*.prefab   merge=unityyamlmerge eol=lf
*.asset    merge=unityyamlmerge eol=lf
*.meta     eol=lf
# LFS: models / textures / audio / video / fonts / archives
*.fbx  filter=lfs diff=lfs merge=lfs -text
*.glb  filter=lfs diff=lfs merge=lfs -text
*.blend filter=lfs diff=lfs merge=lfs -text
*.png  filter=lfs diff=lfs merge=lfs -text
*.jpg  filter=lfs diff=lfs merge=lfs -text
*.jpeg filter=lfs diff=lfs merge=lfs -text
*.tga  filter=lfs diff=lfs merge=lfs -text
*.psd  filter=lfs diff=lfs merge=lfs -text
*.exr  filter=lfs diff=lfs merge=lfs -text
*.hdr  filter=lfs diff=lfs merge=lfs -text
*.wav  filter=lfs diff=lfs merge=lfs -text
*.ogg  filter=lfs diff=lfs merge=lfs -text
*.mp3  filter=lfs diff=lfs merge=lfs -text
*.mp4  filter=lfs diff=lfs merge=lfs -text
*.ttf  filter=lfs diff=lfs merge=lfs -text
*.otf  filter=lfs diff=lfs merge=lfs -text
*.zip  filter=lfs diff=lfs merge=lfs -text
*.so   filter=lfs diff=lfs merge=lfs -text
*.aar  filter=lfs diff=lfs merge=lfs -text
```

[Inference] Leave `.anim`, `.asset` mesh YAML and `.json` as text. The largest `.asset` file in `client/Assets` is about 4 MB, and only 12 files exceed 1 MB.

---

## 2. Sizes

### 2.1 Disk use (`du -sh`; total working tree 32 GB)

| Folder | Size | Status |
|---|---:|---|
| `client/Library` | 13 GB | ignored (correct) |
| `art/character-candidates` | 6.7 GB | **untracked, not ignored** |
| `art/raw` | 4.8 GB | ignored |
| `client/Builds` | 2.3 GB | ignored |
| `docs/progress/screens` | 1.4 GB | **untracked, not ignored** |
| `art/lookdev/src` | 1.2 GB | ignored (nested) |
| `client/Assets` | 967 MB | of which Art 815 MB, Resources 142 MB, Scripts 7.9 MB |
| `audio/processed`, `audio/raw`, `audio/.venv` | 126, 131, 152 MB | processed is partly tracked; the other two are ignored |
| `art/processed`, `art/direction-20261006` | 140, 119 MB | processed is tracked; direction is untracked |
| `.git` | 214 MB | |
| `client/Logs` | 64 MB | ignored |
| `website` | 42 MB | untracked |

### 2.2 APKs in `client/Builds` (ignored)

There are **15 APKs totalling 2,316.7 MB**, plus `CT Life_BurstDebugInformation_DoNotShip/`. 14 of the 15 are larger than 100 MiB (139–189 MB each), so none of them could ever be pushed as normal git files. The status doc names v7 as the installed build: `ctlife-playable-landscape-v7-clean-package-20261007.apk`, 162,442,919 bytes. A stray `launcher-debug.apk` also sits under `client/Library/Bee/...`, which is ignored.

### 2.3 Volume of `docs/progress`

- 175 directories, including **139 run folders** under `screens/`. 2,075 files, of which 2,014 are under `screens/`. `screens/` totals 1,458 MB.
- By type under `screens/`: 1,086 PNG (1,356 MB, averaging 1.25 MB each), 703 JSON (57 MB), 79 txt, 62 md, 52 log, 18 obj (18 MB), 2 mp4, 4 csv.
- The largest run folders are each 69–78 MB, e.g. `quality-review-20261005k2` and `saved-venue-flow-*-20261007`.
- Top-level logs: 61 files (59 md and 2 json), 932,372 bytes in total. The largest are:

| File | Bytes |
|---|---:|
| `ct_direction.md` | 131,856 (1,040 lines) |
| `ct_research.md` | 51,936 |
| `ct_motion.md` | 48,703 |
| `ct_world_assets.md` | 48,417 |
| `locomotion-quality-20261006.md` | 45,480 |
| `characters.md` | 32,623 |
| `audio-content-metrics-20261005.json` | 32,224 |
| `ui-quality.md` | 28,181 |
| `backend-dev-feedback.md` | 22,011 |
| `unity.md` | 20,502 |

### 2.4 Duplicate large files (sha1 of every file over 1 MB, excluding Library, Builds, .venv and .git)

- **Files that are not ignored: 466 duplicate groups, 1,135 files, 1,280.7 MB of redundant bytes.** Of that, 941 MB sits under `art/character-candidates`, 296.6 MB under `client/Assets` and 43 MB under `docs/progress`.
- Including ignored caches: 561 groups and 2,357.8 MB redundant. For example, the Quaternius MegaKit zips are stored twice, in `art/raw` and in `art/lookdev/src` (224 MB + 99 MB).
- Notable examples:
  - `Knight.fbx` (KayKit), 19.7 MB × **6**. Five copies are inside `client/Assets`: `Candidates/KayKitRawRun20261007` and `MotionReview20261006/KayKitRawRunV2..V5`. That is 98.5 MB redundant.
  - `VincentAdult_AtlasHighReference.blend` is byte-identical to `VincentAdult_Unity.blend` (32.1 MB).
  - The Snow atlases `SnowAdult_{Normal,BaseColor,MetallicSmoothness}.png` exist ×3 each: `unity-snow/exports`, `calf-quality-candidate/exports` and `client/Assets`.
  - The Vincent atlases exist ×2: `exports` and `client/Assets`.
  - `game-dashboard.png` exists ×8 (website plus 7 screens folders).
  - The Rain `RestJogContact_Goals-*-phaseNNN.obj.gz` files are 8 identical copies each.

### 2.5 Raw and cache folders

These are all ignored or nested-ignored. They are safe from git, but they use disk space:

| Folder | Size |
|---|---:|
| `art/raw` | 4.8 GB |
| `art/lookdev/src` | 1.2 GB |
| `audio/raw` | 131 MB |
| `audio/.venv` | 152 MB |
| `art/lookdev/.venv` | 28 MB |

The un-ignored equivalent is `art/character-candidates/raw` (518 MB). It holds the Snow zip (106.4 MB), Rain zip (64.4 MB), the Rex, Vincent and Phil blends, and the MetaPerson samples.

### 2.6 What a clean commit would weigh (estimate)

This is classified over all tracked and untracked files that are not ignored: 26,045 files, 9,741 MB.

| Bucket | Files | MB |
|---|---:|---:|
| Junk (`.utmp`, pycache, `.blend1`, `.log`, `_Recovery`, `archive/`) | 270 | 283.7 |
| Evidence and candidates (screens, motion-review, export-check, raw, Art/Candidates, MotionReview, draft-01, gameplay-candidates, direction, lookdev, previews, branding) | 17,642 | 8,320.8 |
| Code, config and text (C#, asmdef, uss, json, md, py, YAML assets, metas) | 4,047 | 64.2 |
| Runtime binary assets in `client/Assets` (excluding Candidates and MotionReview) | 1,955 | 409.8 |
| `art/processed`, `audio/processed`, pipeline outputs | 1,798 | 262.8 |
| Character masters (`unity-snow`, `rain-adult-production`, `vincent-adult-production` minus archive and blend1) | 311 | 399.8 |

The numbers above:
- **Clean commit without character masters: about 737 MB.** Add about 142 MB for the 10 `Art/Candidates` folders the build actually references (section 4.5), giving **about 0.88 GB. With the masters it is about 1.28 GB.** HEAD already holds about 0.26 GB.
- The largest single file in the clean set is 36.5 MB (`VincentAdult_Tiers.blend`). No file exceeds 50 MiB, and only 3 exceed 20 MB. The only files that would hit the 100 MiB block are the evidence zip (`snow.zip`, 106.4 MB) and the APKs.

**Is LFS needed?** Yes, I recommend it. I checked GitHub's limits on docs.github.com on 2026-10-07: pushes warn above 50 MiB and are blocked above 100 MiB; repositories should "ideally" stay under 1 GB and are "strongly recommended" to stay under 5 GB; LFS on Free and Pro plans includes 10 GiB of storage and 10 GiB of bandwidth per month. [Inference] A clean commit of about 0.9–1.3 GB, on top of HEAD's 0.26 GB, already passes the 1 GB ideal on its first commit. Every new texture or FBX revision adds its full size again. Put the binary patterns from section 1.5 in LFS.

[Inference] With roughly 1 GB of LFS content, each fresh clone or CI checkout uses about 1 GB of the 10 GiB monthly bandwidth. That covers about ten full clones a month across you and the backend developer.

HEAD's existing 261 MB of non-LFS blobs can only move into LFS with `git lfs migrate import --everything`. That rewrites the single commit and needs a force-push. It is cheap now because there is only one commit, but it is your decision.

---

## 3. Secrets scan (paths only; no values printed)

**What was scanned:**
- Changed and untracked text files: 9,760 files.
- Tracked text files: 923 files.
- Skipped: binary extensions, files over 8 MB, and files containing NUL bytes.

**Patterns searched:** PEM private keys, AWS keys, GitHub, Slack, Google and `sk-` keys, JWTs, `Authorization: Bearer` headers, 64-integer Solana keypair arrays, 86–88-character base58 secrets, assigned `api_key`/`secret`/`password`/`token` literals, `.env`-style `*_SECRET=` lines, and the keywords mnemonic, seed phrase and recovery phrase. I also searched filenames for `.env*`, `*.pem`, `*.p12`, `*.key`, `*.keystore`, `*.jks`, `id_rsa*`, `*keypair*`, `google-services.json` and `*wallet*`.

**Result: no real credentials found.** Every hit was a false positive:
- `art/gameplay-candidates/{gameplay-generation-20261006,gameplay-generation-v2-20261006,realtime-startup-ownership-20261006}/checks/RealtimeChecks.cs` contain test-fixture literals (`local-…`, `refres…`). They are not real tokens.
- "Seed phrase" appears as anti-scam game copy in `client/Assets/Scripts/UI/Text/ShiftStrings.cs` and its 3 copies under `art/gameplay-candidates/mod-desk*`. It also appears in `contract/config/npc_lines.json`, `client/Assets/Resources/Contract/config_snapshot.json` and `docs/research/ct-playable-loop-audit-20261006.md`.
- `contract/openapi.yaml` contains a truncated example JWT (`eyJhbGciOi...`), and `client/Assets/Generated/Rest/.../AuthVerifyResponse.cs` contains generated model code.
- The filename search only matched `*wallet*` audio, an FBX and `MwaWallet.cs`. There is no `.env`, keystore, `.pem` or keypair file in the working tree.

**Not secrets, but worth knowing:**
- `client/Tools~/livekit_dev_token.py` (tracked) hard-codes LiveKit's public `--dev` key pair. That is fine for local development only.
- `docs/AGENTS-BRIEF.md` (tracked) contains the Seeker device serial.
- Logs and evidence under `docs/progress` contain more than 200 private LAN IP occurrences (10.x and 192.168.x) and absolute paths under `/Users/mac/...`. This is low risk while the repo is private. Scrub it before making anything public.
- Android signing uses the debug keystore (`androidUseCustomKeystore: 0`). When a release keystore is created, keep it outside git and back it up. Losing it means you cannot ship updates to the same app ID.

**Limits:** [Unverified] A regex scan cannot prove a negative. Secrets embedded in binaries, in PNG metadata or under `client/Library` were not checked. Seed-phrase detection was keyword-based only.

---

## 4. Process

### 4.1 Evidence and logs compared with code

| Measure | Value |
|---|---|
| `docs/progress` (all) | **1,459 MB** |
| `client/Assets/Scripts` (`.cs`) | **5.99 MB** (335 files, 82,394 lines) |
| Ratio, docs/progress bytes to Scripts bytes | **about 244 : 1** |
| `docs/progress` text logs only | 0.93 MB (61 files) |
| All docs text outside screens | 1.15 MB (75 `.md`) |
| Editor scripts | 141 files, 4.14 MB (**69 % of Scripts bytes**); 109 of the 141 are named `*Candidate*`/`*Review*` |
| Runtime review, spike and candidate code (non-Editor) | 0.74 MB |
| **Product runtime code** (App, Audio, Core, Net, Solana, UI, World, excluding review and spike) | **about 1.10 MB, 148 files** |

[Inference] The documentation text (1.15 MB) now outweighs the shipping runtime code (about 1.10 MB). Roughly three quarters of the C# by bytes is review and candidate harness code, not product code. 159 files with names containing review, candidate, spike, probe, audit or check add up to 4.35 MB, which is 73 %.

### 4.2 Number of audits and how easy the docs are to navigate

- Files with "audit" in the name: **15** at the top level of `docs/progress`, 1 in `docs/research`, **28** under `screens/`, and **211 paths repo-wide**.
- There are 59 `.md` logs in `docs/progress`. `README.md` says "one log file per agent".
- There is no `docs/README.md` or other index. The best current entry point is `docs/progress/frontend-status-20261007.md`, and nothing links to it. `docs/handoff/2026-10-06-agent-context.md` is 30 KB.
- What the backend developer needs is `docs/progress/backend-dev-feedback.md` (22 KB) plus `docs/fe-contract-proposals/` (6 files). Both are findable, but they are buried among 59 dated logs.
- [Inference] For you and the backend developer, the docs are currently hard to navigate. Recommended: a `docs/README.md` with three links (Current status, Backend handoff, Licences). Move superseded dated audits into `docs/archive/YYYY-MM-DD/`, cap each running log at about 30 KB, and roll older content into the archive.
- Code lives in more than one place. `art/gameplay-candidates/` (908 files) and `art/direction-20261006/` contain `.cs` copies and `.patch` files that duplicate `client/Assets/Scripts`; `ShiftStrings.cs` exists in 3 places. These copies are not compiled, so they drift from the real source.

### 4.3 Single point of loss and suggested commit plan

**Risk:** 24,074 untracked files (9.5 GB) and 108 modified files exist only on this Mac. This includes 253 untracked C# files, all imported Snow, Rain and Vincent assets, the master `.blend` files needed to rebuild those CC-BY derivatives, the v7 APK and two days of decisions. `docs/AGENTS-BRIEF.md` ("Do not `git commit` or push") makes this structural. [Unverified] Whether Time Machine or another backup covers this folder.

**Suggested commits, in order, on a branch such as `fe/checkpoint-2026-10-07`** (recommendations only):

0. Take an off-machine backup first (P0 #1).
1. `chore(repo)`: the `.gitignore` additions and `.gitattributes` with LFS patterns (section 1.5); `git lfs install`.
2. `chore(repo)`: untrack `art/pipeline/__pycache__`; remove the URP global settings duplicates 1–9 after checking the reference; delete the `PerformanceTestRun*` files (already deleted in the working tree).
3. `fix(client)`: add the 120 `client/Assets/Generated/**/*.meta` files.
4. `feat(client)`: runtime code. That means `Assets/Scripts/{App,Audio,Core,Net,Solana,UI,World}` excluding Review and Spike, plus the asmdefs, `Packages/manifest.json` and lock, `ProjectSettings/*`, `link.xml`, the Addressables settings and `Resources/Contract`.
5. `feat(client-tools)`: `Assets/Scripts/Editor/**`, `App/Review/**` and `Tools~/*`. Keeping these separate lets you decide later which harnesses to keep.
6. `feat(art)`: runtime characters (`authored/{Snow,SnowIdle,Rain,RainIdle}`; Vincent only if you intend to ship it), the Resources characters, and `Resources/UI/{Licenses,Fonts,Icons,Art,ClipCut}` with notices. Goes to LFS.
7. `feat(art)`: `Art/{environment,nature,props,animations,characters/male|female|npc,Generated}` and the 10 referenced Candidates folders. Goes to LFS.
8. `feat(audio)`: `audio/processed`, tools, licences, sources JSON, `Resources/Audio`. Goes to LFS.
9. `chore(art)`: `art/pipeline/*.py`, `art/processed` updates, `art/previews/*.jpg`, `art/LICENSES.md`, `art/character-candidates/LICENSES.md` and `README.md`.
10. `feat(art-masters)`: character master sources (`unity-snow`, `rain-adult-production`, `vincent-adult-production` minus `archive/`, `*.blend1` and `calf-quality-candidate/`, about 400 MB). Goes to LFS. Alternatively keep them in external storage with a hash manifest.
11. `docs`: progress logs (text only), research, handoff, design, fe-contract-proposals, AGENTS-BRIEF, plus a new `docs/README.md` index and the evidence-archive manifest.
12. `feat(website)`: `website/{index.html,app.js,styles.css,credits.html,README.md,assets/}`. Archive `draft-01/` and `evidence/` elsewhere.

Push, then open a PR into `main`.

### 4.4 Cleanup plan (recommendations only; nothing has been done)

| Action | Targets | Recovered |
|---|---|---:|
| **Delete** (regenerable or junk) | `client/.utmp`, all `__pycache__`, `*.blend1` (15), `*.log` (106), `client/Assets/_Recovery`, `art/character-candidates/export-check/`, `**/archive/` | about 0.45 GB |
| **Delete** (superseded APKs) | 13 of 15 APKs (keep v7 clean-package, plus v6 if wanted) and the Burst debug folder after symbols are archived | about 2.0 GB |
| **Delete after you confirm** | the 21 unreferenced `client/Assets/Art/Candidates/*` folders; `authored/{LocomotionCandidate,LocomotionDiagnostic,RainWeightDiagnostic}`; 66 of 71 unreferenced `MotionReview20261006` assets; the duplicate `Knight.fbx` copies | about 0.35 GB |
| **Archive outside the repo** (tarball plus sha256 manifest committed in docs) | `docs/progress/screens/` (1.46 GB), `art/character-candidates/motion-review-20261006/` (4.5 GB), `locomotion-quality-20261006/` (568 MB), other character-candidates review folders, `website/draft-01`, `website/evidence`, `art/gameplay-candidates`, `art/direction-20261006` once its outputs are in the client | about 6.8 GB |
| **Keep out of git permanently** | `art/character-candidates/raw/` (third-party originals are re-downloadable using the recorded URLs and hashes); **`raw/metaperson-evaluation-only/` (proprietary itSeez3D evaluation sample; do not commit or distribute)** | 0.52 GB |
| **LFS** | the binary patterns in section 1.5; character masters `*.blend` | n/a |
| **APK distribution** | private GitHub Release asset (per-file limit is far above 162 MB) or external storage | n/a |
| **Dedupe caches** | `art/raw` and `art/lookdev/src` hold the same Quaternius zips and textures | about 0.4 GB of disk |

### 4.5 Which candidate folders the build references

[Inference] This comes from a static GUID walk. I started from the build-settings scenes (`Bootstrap`, `Spike`) and all of `Resources/**`. There are no Addressables groups. Assets loaded only by path from code would not appear.

- **Referenced (10):** `CityDirection20261006B2Lamp` (186/238 assets), `ParentsBasement20261007A` (1/82), `ParentsBasement20261007B2` (66/90), `PlayableVenues20261007` (11/15), `SpacesStudio20261006E2` (7/132), `SpacesStudio20261007F3` (1/8), `SpacesStudio20261007F4` (60/77), `TerminalCafe20261006Final` (6/131), `TerminalCafe20261007Art3` (28/170), `TerminalCafe20261007Art5` (90/123). In `MotionReview20261006`, 5 of 71 are referenced.
- **Not referenced (21 folders, 241 MB):** every other `Art/Candidates/*`, plus `authored/Vincent` (0/11, so Vincent is not in the build), `LocomotionCandidate`, `LocomotionDiagnostic` and `RainWeightDiagnostic`.

---

## 5. Licences: spot-check of 22 files in `client/Assets`

The ledgers checked were `art/LICENSES.md` (220 lines, +109 lines uncommitted), `art/character-candidates/LICENSES.md`, `art/lookdev/LICENSES.md`, `audio/LICENSES.md`, `client/Assets/Resources/UI/{THIRD-PARTY-NOTICES.md,asset-sources.json,Licenses/}` and `art/processed/animations/clips.json`.

Systematic checks:
- All **96** `Art/animations/*.fbx` match `clips.json` (UAL1/UAL2 CC0, or hand-keyed CC0).
- All **265** `environment`, `nature` and `props` FBX names appear in `art/LICENSES.md`.
- All 8 licensed music tracks and 6 Kenney UI SFX are listed in `audio/LICENSES.md`.
- No FBX, GLB, prefab, material or asset file in `client/Assets` contains a Mixamo, Synty, ActorCore, ReadyPlayerMe or MetaPerson signature.

| # | File in `client/Assets` | Ledger entry | Verdict |
|---|---|---|---|
| 1 | `Art/characters/authored/Snow/SnowAdult_BaseColor.png` | art/LICENSES (CC-BY 4.0, Blender Studio), `Licenses/Snow.txt`, `AssetCredits` | OK |
| 2 | `authored/Snow/SnowAdult_LOD1.fbx` | same | OK |
| 3 | `authored/SnowIdle/SnowAdult_RelaxedIdle.fbx` | covered only by the general Snow derivative; no row names the idle clip | **Minor gap** |
| 4 | `authored/Rain/RainAdult_Creator.fbx` | art/LICENSES (CC-BY 4.0, SHA256 recorded), `Licenses/Rain.txt`, `AssetCredits` | OK |
| 5 | `authored/RainIdle/RainAdult_RelaxedIdle.fbx` | general Rain derivative; provenance in `docs/progress/rain-native-idle.md` | **Minor gap** |
| 6 | `authored/Vincent/VincentAdult_Creator.fbx` | `character-candidates/LICENSES.md` (CC-BY 4.0) and `Licenses/Vincent.txt`; `Strings.cs` has `credits.vincent`, **but `AssetCredits.cs` has no Vincent entry**, and top-level `art/LICENSES.md` lists Vincent only as a raw "source comparison" | **Gap; must be fixed before Vincent ships** (not referenced by the build today) |
| 7 | `authored/Vincent/Weekender.prefab` | same as #6 | **Gap** |
| 8 | `authored/MotionReview20261006/KayKitRawRunV2/Knight.fbx` (and V3–V5, plus `Candidates/KayKitRawRun20261007`) | art/LICENSES: KayKit Adventurers CC0, pinned commit, but it says "**Outside-Assets** motion candidate", while 5 copies are inside `client/Assets` | **Ledger inconsistent** (licence is fine) |
| 9 | `Art/animations/act_phone_scroll.fbx` | `clips.json` → UAL / hand-keyed CC0 | OK |
| 10 | `Art/environment/home/home_bed_bunk.fbx` | Quaternius Ultimate House Interior, CC0 | OK |
| 11 | `Art/nature/palm_2.fbx` | Quaternius Ultimate Nature, CC0 | OK |
| 12 | `Art/props/car_sedan.fbx` | Kenney Car Kit, CC0 | OK |
| 13 | `Art/props/news_ticker.fbx` | "CT Life procedural environment pieces" (own work) | OK |
| 14 | `Art/Generated/Venues/Textures/bb_froge.png` | original, made by `art/lookdev/make_textures.py`; covered by `art/lookdev/LICENSES.md` | OK (fictional "$FROGE" parody text) |
| 15 | `Art/Candidates/ParentsBasement20261007B2/Textures/HomeWeave.png` | only in `art/direction-20261006/home-20261007/revision-b/source-manifest.json` ("original project drawings"); not in `art/LICENSES.md` | **Minor gap** (referenced by the build) |
| 16 | `Resources/UI/Art/CityDiorama.png` | `THIRD-PARTY-NOTICES.md` and `art/lobby/city-diorama.provenance.json`: AI-generated, `"tool": "image_gen.imagegen built-in"`, prompt recorded | **Provenance incomplete:** the generator/model and its output terms are not named. [Unverified] |
| 17 | `Resources/UI/ClipCut/story-0.png` (to `story-4`) | art/LICENSES "Clip Cut original storyboards" and `asset-source-manifest.json`: "Built-in imagegen", derived from Snow/Rain renders and one game screenshot | **Same gap as #16**; CC-BY attribution carried through |
| 18 | `Resources/UI/Fonts/Manrope-Regular.ttf` | OFL 1.1, commit pinned in `asset-sources.json`, OFL text bundled, in `AssetCredits` | OK |
| 19 | `Resources/UI/Icons/briefcase.png` | Phosphor (MIT), commit pinned, SVG hashes, LICENSE bundled | OK |
| 20 | `Resources/Audio/music/licensed/mus_dubai_marina_paradise_found.ogg` | Kevin MacLeod CC-BY 4.0, `Music-Credits` shipped, in `AssetCredits` | OK |
| 21 | `Resources/Audio/sfx/licensed/sfx_ui_toggle_on_kenney.wav` | Kenney Interface Sounds, CC0 | OK |
| 22 | `Resources/Audio/ambience/amb_timeline_square.wav` | Freesound 640625 and 757838, CC0 (HQ previews) | OK |

**Unknown or risky provenance outside `client/Assets`:**
- `art/character-candidates/raw/metaperson-evaluation-only/` (46 MB) is a **proprietary itSeez3D evaluation sample**. The ledger correctly says "do not put in game or distribute". It must never be committed or pushed, even to a private repo. [Inference] Pushing it would count as redistribution.
- `art/branding/concepts/*.png` are AI-generated, and their prompts explicitly "use the supplied Solana mark as the precise three-bar silhouette". [Unverified] Whether this complies with Solana's brand terms needs a trademark review before any public use. They are not shipped today.
- `art/character-candidates/raw/{phil,rex}` are CC-BY 4.0 and recorded. They are used for comparison only.
- `website/assets/*` reuse game renders, screenshots and fonts. `website/assets/manifest.json` maps them back to their sources and `credits.html` covers Blender Foundation, OFL and MIT. OK, but the AI-generated `city-diorama.png` has the same gap as #16.

**Fixes needed:**
- Add a "runtime derivative" row for Vincent to `art/LICENSES.md`, and an `AssetCredits` entry for it, before Vincent is wired into `CharacterRegistry`.
- Change the KayKit row to say "inside `client/Assets` (review only, not referenced by gameplay)", or delete the copies.
- Name the image-generation tool and model, with a link to its terms, in `city-diorama.provenance.json` and in the ClipCut and branding manifests.
- Add rows for the Snow and Rain RelaxedIdle clips and for `HomeWeave.png` / `HomeDetailAtlas.png`.
