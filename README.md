# CT Life

3D multiplayer Crypto Twitter life-sim for the Solana Seeker. Spec: docs/spec/ (exported from the
Claude Doc "CT Life — Build Spec"). Build order: six phases, each ends in a submittable APK.

| Folder | Owner |
| --- | --- |
| contract/ | read-only snapshot from devIykee/ct-life-backend; provenance in contract/SOURCE.md |
| docs/fe-contract-proposals/ | FE proposals for backend-owned IDs and registries |
| client/ | Unity client (FE) |
| art/ | 3D assets (CC0 preferred; attribution and usage in art/LICENSES.md) |
| audio/ | sound (licences in audio/LICENSES.md) |
| docs/progress/ | one log file per agent |

This checkout is the frontend workspace. The separate backend developer owns the server and
Solana programs in `devIykee/ct-life-backend`. Do not create a parallel backend here.

Art direction is Sims-style stylized realism (see `docs/AGENTS-BRIEF.md`). Existing MakeHuman
models and procedural avatars are rejected placeholders, not the approved character direction.
The six phases describe dependencies and checkpoints, not a cap on the finished game's scope.
