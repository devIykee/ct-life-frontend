# Contract source

This folder is a read-only copy of the backend developer's contract. Do not edit it here.

- Repo: https://github.com/devIykee/ct-life-backend (private)
- Branch: `main` at commit `eeef49d` (2026-10-17 13:25 +0100, "feat(bank): debt repayment, cash history and live perp PnL")
- Contract version: still `1.0.0-rc1` plus **additive** changes listed under "Unreleased"/dated entries in `CHANGELOG.md`.
- Synced: 2026-10-18 (Feel lane). Changes since `7243073`: `openapi.yaml` (owned avatars/likeness, paid ads and uploads,
  businesses, DMs, `GET /ledger`, `POST /bank/repay`, `/me` `debtDueAt`/`debtWeeklyDueCents`, perp `markMicros`/
  `unrealizedPnlCents`), `ids/errors.json` (`NOTHING_TO_REPAY` and others), `config/businesses*`, `ids/archetypes|dreams|traits.json`.
  `realtime/` is unchanged. REST codegen re-run (143 models); `PostAdsBookings200Response` and `PostUploads200Response`
  were renamed upstream (unused by the client).
- Known upstream spec issues (for the backend; not edited here): `paths./payments/intents/{id}.get.responses.200.content.application/json`
  has a stray `description` (fails strict validation, so codegen runs with `--skip-validate-spec`), and `Me.debtDueAt`'s
  flow-style description contains an unquoted `, Sun 20:00 UTC)` that truncates it. OpenAPI 3.1 drops `nullable: true`,
  so `patch_rest_codegen.py` makes the two `/me` instalment fields `DateTime?`/`long?`.

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
