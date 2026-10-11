#!/usr/bin/env bash
# End-to-end: the Unity editor client (real flow, env=local, dev auth) against the local backend,
# with tools/integration/observer-bot.mjs as the second client.
#   tools/backend-local/up.sh            # first
#   tools/integration/run_editor_integration.sh [--keep-open]
# Receipt + logs: $CTLIFE_EVIDENCE (default ../ct-life-evidence/integration/<timestamp>/). Exit 0 = all checks pass.
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
U="${UNITY:-/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity}"
STAMP="$(date +%Y%m%d-%H%M%S)"
OUT="${CTLIFE_EVIDENCE:-$REPO/../ct-life-evidence/integration}/editor-$STAMP"
mkdir -p "$OUT"
curl -fsS http://localhost:4010/healthz >/dev/null || { echo "backend not running (tools/backend-local/up.sh)"; exit 2; }

node "$HERE/observer-bot.mjs" --out "$OUT/bot.json" --seconds 900 > "$OUT/bot.log" 2>&1 &
BOT=$!
trap 'kill $BOT 2>/dev/null' EXIT
sleep 3

# A fresh wallet per run so onboarding (lottery + creator) is exercised every time; WALLET=<base58> reuses one.
WALLET="${WALLET:-$(python3 -c 'import os;a="123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";n=int.from_bytes(os.urandom(32),"big");s="";
while n: n,r=divmod(n,58); s=a[r]+s
print(s)')}"
echo "wallet $WALLET"
EXTRA=()
[[ "${1:-}" == "--keep-open" ]] && EXTRA+=(-ctlife-integrationKeepOpen 1)
"$U" -batchmode -projectPath "$REPO/client" -buildTarget Android \
  -executeMethod CTLife.Tests.Integration.IntegrationRun.Launch \
  -ctlife-env local -ctlife-autologin dev -ctlife-wallet "$WALLET" -ctlife-autocreate 1 -ctlife-autoenter 1 -ctlife-fresh 1 \
  -ctlife-botStatus "$OUT/bot.json" -ctlife-integrationOut "$OUT/receipt.json" ${EXTRA[@]+"${EXTRA[@]}"} \
  -logFile "$OUT/unity.log"
CODE=$?
grep "CTLIFE \[integration\]" "$OUT/unity.log" | sed 's/^/  /'
echo "exit $CODE; receipt $OUT/receipt.json"
exit $CODE
