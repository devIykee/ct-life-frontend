# Contract source

This folder is a read-only copy of the backend developer's contract. Do not edit it here.

- Repo: https://github.com/devIykee/ct-life-backend (private)
- Branch: `main` at commit `9f3e5ef` (2026-10-06 05:59 +0100, "Finish: port ORE stake instruction builders from Rust")
- Contract version: still `1.0.0-rc1` plus **additive** changes listed under "Unreleased" in `CHANGELOG.md`
  (FE registries anims/sfx/spots 1.1.0 from PR #1; M4 realtime states `SpaceState`, `SpeakerState`,
  `VideoCallState`; `idl/ct_escrow.json`).
- Synced: 2026-10-07 (integration lead)

Note: the remote history was rewritten after the rc1 tag. The tag `contract-v1.0.0-rc1` still points
at `160d59e`, which is no longer an ancestor of `main`; its contract content is identical to `main`'s
M1 commit `3d2766f`. No newer contract tag exists yet, so we sync from a `main` commit.

To re-sync (from the frontend repo root, with the backend cloned beside it):

```sh
git -C ../ct-life-backend fetch origin && git -C ../ct-life-backend checkout --detach origin/main
rm -rf contract && git -C ../ct-life-backend archive <commit-or-tag> contract | tar -x -C .
# then restore this SOURCE.md and update the commit above, and re-run client codegen:
#   client/Tools~/codegen_realtime.sh  and  client/Tools~/codegen_rest.sh (+ patch_rest_codegen.py)
```

Frontend-owned registry additions (anims, sfx, spots) were merged upstream in PR #1, so
`docs/fe-contract-proposals/ids/` is now historical.
