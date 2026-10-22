# Contract source

This folder is a read-only copy of the backend developer's contract. Do not edit it here.

- Repo: https://github.com/devIykee/ct-life-backend (private)
- Branch: `main` at commit `7d2f069a3f432347dee7b9fa33d5431676dfb3c0` (synced 2026-10-19).
- Contract version: still `1.0.0-rc1` plus **additive** changes listed under "Unreleased"/dated entries in `CHANGELOG.md`.
- Prior REST sync: 2026-10-18 (Feel lane). Changes since `7243073`: `openapi.yaml` (owned avatars/likeness, paid ads and uploads,
  businesses, DMs, `GET /ledger`, `POST /bank/repay`, `/me` `debtDueAt`/`debtWeeklyDueCents`, perp `markMicros`/
  `unrealizedPnlCents`), `ids/errors.json` (`NOTHING_TO_REPAY` and others), `config/businesses*`, `ids/archetypes|dreams|traits.json`.
  REST codegen re-run (143 models); `PostAdsBookings200Response` and `PostUploads200Response`
  were renamed upstream (unused by the client).
- Realtime sync: 2026-10-19 from the isolated clean backend worktree. Added street/vehicle state and messages, island player coordinates, real district IDs and empty parking registry; regenerated realtime C# with the existing pinned generator. REST/OpenAPI unchanged from `eeef49d`. Vehicle ownership/rentals, drone entitlements and weekly-debt remainder are still absent; see `docs/progress/backend-reconcile-20261019.md`.
- Known upstream spec issues (for the backend; not edited here): `paths./payments/intents/{id}.get.responses.200.content.application/json`
  has a stray `description` (fails strict validation, so codegen runs with `--skip-validate-spec`), and `Me.debtDueAt`'s
  flow-style description contains an unquoted `, Sun 20:00 UTC)` that truncates it. OpenAPI 3.1 drops `nullable: true`,
  so `patch_rest_codegen.py` makes the two `/me` instalment fields `DateTime?`/`long?`.

Note: the remote history was rewritten after the rc1 tag. The tag `contract-v1.0.0-rc1` still points
at `160d59e`, which is no longer an ancestor of `main`; its contract content is identical to `main`'s
M1 commit `3d2766f`. No newer contract tag exists yet, so we sync from a `main` commit.

To re-sync (from the frontend repo root, with the backend cloned beside it):

```sh
git -C ../ct-life-backend fetch origin
# Preserve any dirty backend checkout; export the requested commit directly.
git -C ../ct-life-backend archive <commit-or-tag> contract | tar -x -C .
# then restore this SOURCE.md and update the commit above, and re-run client codegen:
#   client/Tools~/codegen_realtime.sh  and  client/Tools~/codegen_rest.sh (+ patch_rest_codegen.py)
```

Frontend-owned registry additions (anims, sfx, spots) were merged upstream in PR #1, so
`docs/fe-contract-proposals/ids/` is now historical.
