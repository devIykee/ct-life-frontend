# Contract source

This folder is a read-only copy of the backend developer's contract. Do not edit it here.

- Repo: https://github.com/devIykee/ct-life-backend (private)
- Branch: `main` at commit `dbfefc1` (2026-10-10 23:39 +0100, "fix(5): run the market sim in the workers so coins launch and tick")
- Contract version: still `1.0.0-rc1` plus **additive** changes listed under "Unreleased" in `CHANGELOG.md`.
- Synced: 2026-10-11. Changes since `7243073` (all additive, no ID removals): `openapi.yaml` adds `GET/POST /businesses`,
  `GET /businesses/{id}`, `POST /businesses/{id}/collect|sell` (`Business`, `BusinessDay`) and the `GET /dms` inbox
  object `{threads}`; `config/businesses.json` adds `payoutPeriodHours`, `sellBackBps`, `maxOwned`, `badDays`;
  `ids/archetypes.json` adds `barista`, `patron`; `ids/dreams.json` and `ids/traits.json` gain fields only.
  REST models regenerated (9 new model files); the client's hand-written businesses and DM parsers already read these shapes.

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
