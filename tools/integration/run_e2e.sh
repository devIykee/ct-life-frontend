#!/usr/bin/env bash
# CT Life end-to-end, one command:
#   1. local backend (tools/backend-local/up.sh if it is not answering)
#   2. [--build] dev APK from the warm build clone, installed on the Seeker
#   3. editor integration: the real client in play mode against the backend, with an observer bot as a second client
#      (sign-in, lottery, character, city/market/venue rooms, presence, chat, emotes, a paid shift, block/report,
#      token refresh, venue isolation) plus every lane's API (FeatureProbe) and the phone apps
#   4. device e2e on the Seeker: real taps through onboarding, the server holds the picks, another client sees the
#      player in Timeline Square, the device feature probe, no errors in logcat (tools/integration/run_device_e2e.sh)
#   5. SUMMARY.md with every check and feature row; "blocked" rows name the backend item they wait on
# Usage: tools/integration/run_e2e.sh [--build] [--no-editor] [--no-device]
# Output: ../ct-life-evidence/e2e/<stamp>/ ; exit 0 only when every check passes.
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
U="${UNITY:-/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity}"
ADB="${ADB:-/Applications/Unity/Hub/Editor/6000.3.25f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb}"
S="${SEEKER:-SM02G40619141343}"
BUILD=0 EDITOR=1 DEVICE=1
for a in "$@"; do
  case "$a" in --build) BUILD=1 ;; --no-editor) EDITOR=0 ;; --no-device) DEVICE=0 ;; *) echo "unknown arg $a"; exit 2 ;; esac
done
STAMP="$(date +%Y%m%d-%H%M%S)"
OUT="${CTLIFE_E2E_OUT:-$REPO/../ct-life-evidence/e2e}/$STAMP"
mkdir -p "$OUT"
echo "e2e → $OUT"

# 1. backend
if ! curl -fsS http://localhost:4010/healthz >/dev/null 2>&1; then
  echo "[backend] starting"; bash "$REPO/tools/backend-local/up.sh" > "$OUT/backend-up.log" 2>&1 || { echo "backend failed to start ($OUT/backend-up.log)"; exit 2; }
fi
echo "[backend] $(cd "${CTLIFE_BACKEND_DIR:-$REPO/../ct-life-backend}" && git log -1 --format='%h %s' 2>/dev/null)"

# 2. APK
if [[ "$BUILD" == 1 ]]; then
  echo "[build] syncing the warm clone and building the dev APK"
  bash "$HERE/sync_build_clone.sh" Careers CityLife Competition Social Ventures WorldLife > "$OUT/sync.log" 2>&1
  B="${CTLIFE_BUILD_CLONE:-$REPO/../ct-life-build/client}"
  "$U" -batchmode -quit -projectPath "$B" -buildTarget Android -executeMethod CTLife.EditorTools.BuildScript.BuildAndroid \
    -ctlifeEnv local -ctlifeDev -ctlifeOut "Builds/ctlife-e2e-$STAMP.apk" -logFile "$OUT/build.log"
  grep -q 'CTLIFE \[build\] Succeeded' "$OUT/build.log" || { echo "build failed ($OUT/build.log)"; exit 1; }
  "$ADB" -s "$S" install -r "$B/Builds/ctlife-e2e-$STAMP.apk" > "$OUT/install.log" 2>&1 || { echo "install failed"; exit 1; }
  echo "[build] installed ctlife-e2e-$STAMP.apk"
fi

# 3. editor integration (the project must not be open in the Unity editor)
EDITOR_CODE=0
if [[ "$EDITOR" == 1 ]]; then
  if pgrep -f "Unity -projectPath $REPO/client" >/dev/null 2>&1; then
    echo "[editor] skipped: the Unity editor has $REPO/client open (close it, or pass --no-editor)"; EDITOR_CODE=2
  else
    rm -f "$REPO/client/Temp/UnityLockfile"
    echo "[editor] running (about 3 minutes)"
    CTLIFE_EVIDENCE="$OUT/editor" bash "$HERE/run_editor_integration.sh" > "$OUT/editor.log" 2>&1; EDITOR_CODE=$?
    echo "[editor] exit $EDITOR_CODE"
  fi
fi

# 4. device
DEVICE_CODE=0
if [[ "$DEVICE" == 1 ]]; then
  echo "[device] running (about 6 minutes)"
  bash "$HERE/run_device_e2e.sh" --out "$OUT/device" > "$OUT/device.log" 2>&1; DEVICE_CODE=$?
  echo "[device] exit $DEVICE_CODE"
fi

# 5. summary
python3 - "$OUT" <<'EOF'
import json, glob, os, sys, collections
out = sys.argv[1]
lines = ["# CT Life e2e - " + os.path.basename(out), ""]
def section(title, receipt):
    lines.append("## " + title)
    if not receipt or not os.path.exists(receipt):
        lines.append("not run"); lines.append(""); return None
    r = json.load(open(receipt))
    checks = r.get("checks", [])
    lines.append("**%s** - %d/%d checks" % ("PASS" if r.get("pass") else "FAIL", sum(1 for c in checks if c.get("pass")), len(checks)))
    lines.append("")
    lines.append("| check | result | detail |"); lines.append("|---|---|---|")
    for c in checks:
        res = ("blocked #" + c["blocked"]) if c.get("blocked") else ("pass" if c.get("pass") else "**FAIL**")
        lines.append("| %s | %s | %s |" % (c["name"], res, (c.get("detail") or "").replace("|", "/")[:140]))
    feats = r.get("features", [])
    if feats:
        cnt = collections.Counter(f["result"] for f in feats)
        lines.append(""); lines.append("Features: " + ", ".join("%s %d" % (k, v) for k, v in sorted(cnt.items())))
        lines.append(""); lines.append("| feature | result | backend item | detail |"); lines.append("|---|---|---|---|")
        for f in sorted(feats, key=lambda f: ({"fail": 0, "blocked": 1, "n/a": 2, "pass": 3}.get(f["result"], 4), f["feature"])):
            lines.append("| %s | %s | %s | %s |" % (f["feature"], f["result"] if f["result"] != "fail" else "**FAIL**", ("#" + f["item"]) if f.get("item") else "", (f.get("detail") or "").replace("|", "/")[:120]))
    lines.append("")
    return r
ed = sorted(glob.glob(os.path.join(out, "editor", "editor-*", "receipt.json")))
e = section("Editor integration (client in play mode + observer bot)", ed[-1] if ed else None)
d = section("Device (Seeker): onboarding UI, server state, presence, feature probe", os.path.join(out, "device", "receipt.json"))
ok = all(x is None or x.get("pass") for x in (e, d)) and any(x is not None for x in (e, d))
lines.insert(1, "**Overall: %s**" % ("PASS" if ok else "FAIL"))
open(os.path.join(out, "SUMMARY.md"), "w").write("\n".join(lines) + "\n")
print("\n".join(lines[:4]))
print("summary " + os.path.join(out, "SUMMARY.md"))
sys.exit(0 if ok else 1)
EOF
