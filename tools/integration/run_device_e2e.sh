#!/usr/bin/env bash
# Device end-to-end on the Seeker: dev build, local backend (tools/backend-local/up.sh), observer bot as a 2nd client.
#   A. UI onboarding: a fresh player taps through the real screens (lottery → character → tribe → traits → dream →
#      alias → player card → welcome). The backend must then hold that player: GET /v1/me for the same wallet
#      (dev auth) has the typed alias, a tribe, two traits, a dream, a look and the rolled lottery.
#   B. Realtime: ENTER THE CITY, then the observer bot (another client in Timeline Square) must see this player by
#      alias, and the device must log the venue join.
#   C. Feature probe on the device (integration=1, peer=<bot>): every lane's API through the client's own code.
#      "blocked" rows are backend gaps already reported (docs/progress/backend-dev-feedback.md); they do not fail.
#   D. Health: no Unity exceptions or CTLIFE errors in logcat, and the app process is still alive.
# Usage: tools/integration/run_device_e2e.sh [--out <dir>] [--skip-ui] [--wallet <base58>]
# Exit 0 = every check passes. Receipt: <out>/receipt.json; screenshots: <out>/*.png; summary on stdout.
# Tap coordinates are for the Seeker's 2670x1200 landscape screen and the onboarding layout of 2026-10-10.
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
BACKEND="${CTLIFE_BACKEND_DIR:-$REPO/../ct-life-backend}"
ADB="${ADB:-/Applications/Unity/Hub/Editor/6000.3.25f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb}"
S="${SEEKER:-SM02G40619141343}"
PKG="${CTLIFE_PACKAGE:-com.ctlife.app}"
API="${CTLIFE_API:-http://localhost:4010}"
OUT="" SKIP_UI=0 W=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --out) OUT="$2"; shift 2 ;;
    --skip-ui) SKIP_UI=1; shift ;;
    --wallet) W="$2"; shift 2 ;;
    *) echo "unknown arg $1"; exit 2 ;;
  esac
done
OUT="${OUT:-$REPO/../ct-life-evidence/integration/device-$(date +%Y%m%d-%H%M%S)}"
mkdir -p "$OUT"
CHECKS="$OUT/checks.jsonl"; : > "$CHECKS"

check() { # name pass(0/1) detail
  local name="$1" pass="$2" detail="$3"
  python3 - "$CHECKS" "$name" "$pass" "$detail" <<'EOF'
import json, sys
path, name, ok, detail = sys.argv[1], sys.argv[2], sys.argv[3] == "1", sys.argv[4]
open(path, "a").write(json.dumps({"name": name, "pass": ok, "detail": detail[:600]}) + "\n")
EOF
  echo "  $([[ "$pass" == 1 ]] && echo PASS || echo FAIL) $name: ${detail:0:200}"
}
adb_() { "$ADB" -s "$S" "$@"; }
shot() { adb_ exec-out screencap -p > "$OUT/$1.png"; }
tap() { adb_ shell input tap "$1" "$2"; }
LOGCAT_FILE=""
unity_log() { if [[ -n "$LOGCAT_FILE" ]]; then cat "$LOGCAT_FILE"; else adb_ logcat -d -s Unity 2>/dev/null; fi; }
start_logcat() { # name: clear the device log and stream Unity lines into <out>/logcat-<name>.txt
  [[ -n "${LOGCAT_PID:-}" ]] && kill "$LOGCAT_PID" 2>/dev/null
  adb_ logcat -c; LOGCAT_FILE="$OUT/logcat-$1.txt"; : > "$LOGCAT_FILE"
  adb_ logcat -v time -s Unity > "$LOGCAT_FILE" 2>/dev/null &
  LOGCAT_PID=$!; disown "$LOGCAT_PID" 2>/dev/null || true
}
wait_log() { # pattern seconds -> 0 when the Unity log matches
  local pat="$1" secs="$2" i
  for ((i = 0; i < secs; i += 2)); do unity_log | grep -q -E "$pat" && return 0; sleep 2; done
  return 1
}

# ---------------------------------------------------------------- preflight
echo "device e2e → $OUT"
adb_ get-state >/dev/null 2>&1 || { echo "Seeker $S not connected"; exit 2; }
curl -fsS "$API/healthz" >/dev/null || { echo "backend not running (tools/backend-local/up.sh)"; exit 2; }
curl -fsS http://localhost:2567/healthz >/dev/null 2>&1 || echo "  (realtime /healthz not answering; continuing)"
adb_ shell pm path "$PKG" >/dev/null 2>&1 || { echo "$PKG not installed"; exit 2; }
adb_ shell input keyevent KEYCODE_WAKEUP
adb_ shell settings put system screen_off_timeout 600000 >/dev/null 2>&1 || true

# The observer bot: a second client in Timeline Square that records every player it sees.
node "$HERE/observer-bot.mjs" --out "$OUT/bot.json" --seconds 1500 --name e2ebot > "$OUT/bot.log" 2>&1 &
BOT=$!
trap 'kill $BOT ${LOGCAT_PID:-} 2>/dev/null' EXIT
BOT_USER=""
for _ in $(seq 1 30); do
  BOT_USER="$(python3 -c "import json;print(json.load(open('$OUT/bot.json')).get('userId') or '')" 2>/dev/null)"
  [[ -n "$BOT_USER" ]] && break; sleep 1
done
check bot_online "$([[ -n "$BOT_USER" ]] && echo 1 || echo 0)" "observer bot user=$BOT_USER"

new_wallet() { (cd "$BACKEND" && node -e "const r=require('node:module').createRequire(process.cwd()+'/package.json');console.log(r('@solana/web3.js').Keypair.generate().publicKey.toBase58())"); }
me_for_wallet() { # wallet -> GET /v1/me JSON (dev auth for the same wallet)
  local tok
  tok="$(curl -fsS -X POST "$API/v1/auth/dev" -H 'content-type: application/json' -H "Idempotency-Key: $(python3 -c 'import uuid;print(uuid.uuid4())')" \
    -d "{\"wallet\":\"$1\"}" | python3 -c 'import sys,json;d=json.load(sys.stdin);print(d.get("accessToken") or d.get("access_token") or "")')"
  [[ -n "$tok" ]] || return 1
  curl -fsS "$API/v1/me" -H "authorization: Bearer $tok"
}

ALIAS="e2e$((RANDOM % 90000 + 10000))"
if [[ "$SKIP_UI" == 0 ]]; then
  # ------------------------------------------------------------ A. UI onboarding with a fresh player
  W="${W:-$(new_wallet)}"
  echo "wallet $W alias $ALIAS" | tee "$OUT/player.txt"
  start_logcat ui
  ADB="$ADB" bash "$HERE/seeker_launch.sh" -e fresh 1 -e autologin dev -e wallet "$W" > "$OUT/launch.log" 2>&1
  wait_log 'CTLIFE \[auth\] Signed in' 40; signed=$?
  check sign_in "$([[ $signed == 0 ]] && echo 1 || echo 0)" "$(unity_log | grep -m1 -o 'CTLIFE \[auth\] Signed in.*' | cut -c1-160)"
  sleep 12; shot 01-origin
  tap 2148 1076; sleep 8; shot 02-origin-card
  tap 2208 1074; sleep 6; shot 03-pick-character
  tap 2366 1074; sleep 4; shot 04-tribe
  tap 994 520; sleep 1; tap 2366 1086; sleep 4; shot 05-traits
  tap 2296 446; sleep 1; tap 1684 734; sleep 2; shot 06-traits-picked
  tap 2366 1086; sleep 4; shot 07-dream
  tap 1754 428; sleep 1; tap 2366 1086; sleep 4
  tap 1436 424; sleep 2; adb_ shell input text "$ALIAS"; sleep 1; adb_ shell input keyevent 66; sleep 2; shot 08-alias
  tap 2366 1074; sleep 5; shot 09-player-card
  tap 200 1048; sleep 1; shot 10-player-card-18
  tap 2272 1086; sleep 10; shot 11-welcome

  ME="$(me_for_wallet "$W" 2>/dev/null)"; echo "$ME" > "$OUT/me.json"
  python3 - "$OUT/me.json" "$ALIAS" > "$OUT/me-check.txt" <<'EOF'
import json, sys
try:
    me = json.load(open(sys.argv[1]))
except Exception as e:
    print("0|GET /v1/me failed: %s" % e); sys.exit()
c = me.get("character") or {}
flags = me.get("flags") or {}
user = me.get("user") or {}
got = {"alias": c.get("alias"), "tribeId": c.get("tribeId"), "traits": c.get("traitIds"), "dreamId": c.get("dreamId"),
       "look": bool(c.get("look")), "lotteryRolled": flags.get("lotteryRolled"), "cash": me.get("cashCents", user.get("cashCents"))}
ok = (c.get("alias") == sys.argv[2] and c.get("tribeId") and len(c.get("traitIds") or []) == 2 and c.get("dreamId")
      and c.get("look") and flags.get("lotteryRolled") in (True, None))
print(("1" if ok else "0") + "|" + json.dumps(got))
EOF
  IFS='|' read -r ok detail < "$OUT/me-check.txt"
  check onboarding_saved_on_server "$ok" "$detail"

  # ------------------------------------------------------------ B. city + another client sees us
  tap 2300 1080; sleep 6; shot 12-home          # the celebration's ENTER THE CITY opens home
  tap 2200 1060; sleep 4; shot 13-entering      # home's ENTER THE CITY travels to Timeline Square
  wait_log 'CTLIFE \[flow\] Travel to' 40
  sleep 14; shot 14-city
  seen=0; detail="bot never saw $ALIAS"
  for _ in $(seq 1 20); do
    detail="$(python3 - "$OUT/bot.json" "$ALIAS" <<'EOF'
import json, sys
try: b = json.load(open(sys.argv[1]))
except Exception: print("0|bot.json unreadable"); sys.exit()
hit = [p for p in (b.get("players") or {}).values() if p.get("name") == sys.argv[2]]
print(("1|" if hit else "0|") + json.dumps({"botVenue": b.get("roomVenueId"), "players": [p.get("name") for p in (b.get("players") or {}).values()], "me": hit[0] if hit else None})[:500])
EOF
)"
    [[ "${detail%%|*}" == 1 ]] && { seen=1; break; }
    sleep 3
  done
  check seen_by_other_client "$seen" "${detail#*|}"
  join="$(unity_log | grep -E 'CTLIFE \[(flow|venue|rt)\]' | grep -i -E 'travel to|joined|rejoined' | tail -3 | cut -c1-140 | tr '\n' ' ')"
  check device_joined_venue "$([[ -n "$join" ]] && echo 1 || echo 0)" "$join"
else
  [[ -n "$W" ]] || { echo "--skip-ui needs --wallet of an onboarded player"; exit 2; }
fi

# Health after the UI part, before the probe adds its own (expected) warnings.
errs="$(unity_log | grep -E ' E Unity|Exception' | grep -v -E 'Tethering|NOT_FOUND|404' | head -20)"
echo "$errs" > "$OUT/logcat-errors-ui.txt"
n=$(grep -c . "$OUT/logcat-errors-ui.txt" 2>/dev/null || true); n=${n:-0}
check no_errors_during_play "$([[ "$n" == 0 ]] && echo 1 || echo 0)" "$n error lines$( [[ $n -gt 0 ]] && echo ": $(head -3 "$OUT/logcat-errors-ui.txt" | cut -c1-160 | tr '\n' ' ')")"

# ---------------------------------------------------------------- C. device feature probe (same account)
start_logcat probe
adb_ shell rm -f "/sdcard/Android/data/$PKG/files/integration-features.json" >/dev/null 2>&1
ADB="$ADB" bash "$HERE/seeker_launch.sh" -e autologin dev -e wallet "$W" -e autoenter 1 -e integration 1 -e peer "$BOT_USER" > "$OUT/launch-probe.log" 2>&1
if wait_log 'CTLIFE \[probe\] wrote' 420; then
  adb_ pull "/sdcard/Android/data/$PKG/files/integration-features.json" "$OUT/features.json" >/dev/null 2>&1 \
    || adb_ exec-out run-as "$PKG" cat files/integration-features.json > "$OUT/features.json" 2>/dev/null
  unity_log | grep 'CTLIFE \[probe\]' > "$OUT/probe.txt"
  summary="$(python3 - "$OUT/features.json" "$OUT/probe.txt" <<'EOF'
import json, sys, collections, re
rows = []
try: rows = json.load(open(sys.argv[1]))
except Exception:
    for line in open(sys.argv[2]):
        m = re.search(r"CTLIFE \[probe\] (\w+) ([\w.]+): (.*)", line)
        if m and m.group(2) != "wrote": rows.append({"feature": m.group(2), "result": m.group(1).lower(), "detail": m.group(3)})
c = collections.Counter(r["result"] for r in rows)
fails = [r["feature"] for r in rows if r["result"] == "fail"]
print(("1" if rows and not fails else "0") + "|" + json.dumps(dict(c)) + (" fails=" + ",".join(fails) if fails else ""))
EOF
)"
  check device_feature_probe "${summary%%|*}" "${summary#*|}"
else
  check device_feature_probe 0 "no 'CTLIFE [probe] wrote' within 7 min"
fi
shot 20-after-probe
alive="$(adb_ shell pidof "$PKG" 2>/dev/null)"
check app_alive "$([[ -n "$alive" ]] && echo 1 || echo 0)" "pid=$alive"
exc="$(grep -E 'Exception' "$OUT/logcat-probe.txt" | grep -v -E 'ApiException|NOT_FOUND' | head -5)"
check no_exceptions_during_probe "$([[ -z "$exc" ]] && echo 1 || echo 0)" "$(echo "$exc" | cut -c1-160 | tr '\n' ' ')"
# Leave the shared rooms empty for the next run (an idle phone player would stand in Timeline Square).
[[ "${CTLIFE_E2E_KEEP_APP:-0}" == 1 ]] || adb_ shell am force-stop "$PKG"

# ---------------------------------------------------------------- receipt
python3 - "$CHECKS" "$OUT/features.json" "$OUT/receipt.json" "$W" "$ALIAS" <<'EOF'
import json, sys, datetime
checks = [json.loads(l) for l in open(sys.argv[1]) if l.strip()]
try: features = json.load(open(sys.argv[2]))
except Exception: features = []
ok = bool(checks) and all(c["pass"] for c in checks)
json.dump({"at": datetime.datetime.now(datetime.timezone.utc).isoformat(), "pass": ok, "wallet": sys.argv[4], "alias": sys.argv[5],
           "checks": checks, "features": features}, open(sys.argv[3], "w"), indent=1)
print("RESULT %s (%d/%d checks)" % ("PASS" if ok else "FAIL", sum(c["pass"] for c in checks), len(checks)))
EOF
python3 -c "import json,sys;sys.exit(0 if json.load(open('$OUT/receipt.json'))['pass'] else 1)"
