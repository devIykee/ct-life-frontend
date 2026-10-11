#!/usr/bin/env bash
# Start devIykee's backend locally for CT Life client integration.
#
#   tools/backend-local/up.sh            infra (Docker) + api + realtime + workers (host node)
#   tools/backend-local/up.sh --bots N   ...plus N headless bots in Timeline Square
#   tools/backend-local/down.sh          stop everything (keeps the Postgres volume)
#
# Infra (Postgres 5434, Redis 6381, LiveKit 7880/7881/7882udp) runs from the backend's own
# docker-compose.yml plus compose.override.yml here. The api (4010), realtime (2567) and
# workers run on the host with the backend's own entrypoints (`node --import tsx ...`), because
# the backend uses one LIVEKIT_URL for both server calls and the URL handed to clients, which
# only works when the processes see LiveKit at localhost.
#
# Prerequisites (one-time, see docs/progress/integration.md):
#   - ../ct-life-backend cloned, `pnpm install` done, gitignored .env created
#   - local throwaway keys in ~/.ctlife-local (JWT RSA key, devnet keypairs)
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
BE="${CTLIFE_BACKEND_DIR:-$HERE/../../../ct-life-backend}"
BE="$(cd "$BE" && pwd)"
RUN="${CTLIFE_RUN_DIR:-$HOME/.ctlife-local/run}"
LOGS="${CTLIFE_LOG_DIR:-$HOME/.ctlife-local/logs}"
mkdir -p "$RUN" "$LOGS"

BOTS=0
if [[ "${1:-}" == "--bots" ]]; then BOTS="${2:-5}"; fi

[[ -f "$BE/.env" ]] || { echo "missing $BE/.env (see docs/progress/integration.md)"; exit 1; }

echo "[infra] docker compose up postgres redis livekit"
docker compose --project-directory "$BE" -f "$BE/docker-compose.yml" -f "$HERE/compose.override.yml" \
  up -d postgres redis livekit >/dev/null

for i in $(seq 1 30); do
  if docker exec ctlife-postgres pg_isready -U ctlife -d ctlife >/dev/null 2>&1; then break; fi
  sleep 1
done

echo "[db] migrations"
(cd "$BE" && pnpm -s db:migrate >"$LOGS/migrate.log" 2>&1) || { echo "migrations failed, see $LOGS/migrate.log"; exit 1; }

# No tsconfig override: from backend 7243073 its own tsconfig compiles contract/realtime with legacy decorators.

# Local fixes for backend bugs that block the client (each reported in docs/progress/backend-dev-feedback.md).
# Applied to the backend working tree only when they apply cleanly; skipped once upstream has the fix.
for p in "$HERE"/patches/*.patch; do
  [[ -f "$p" ]] || continue
  if (cd "$BE" && git apply --reverse --check "$p" >/dev/null 2>&1); then echo "[patch] $(basename "$p") already applied"
  elif (cd "$BE" && git apply --check "$p" >/dev/null 2>&1); then (cd "$BE" && git apply "$p") && echo "[patch] $(basename "$p") applied"
  else echo "[patch] $(basename "$p") does not apply (fixed upstream? check and delete it)"; fi
done

start() { # name, entry, extra env...   (PRELOAD=<file> adds a --import before tsx)
  local name="$1" entry="$2"; shift 2
  local pre=()
  [[ -n "${PRELOAD:-}" ]] && pre=(--import "$PRELOAD")
  if [[ -f "$RUN/$name.pid" ]] && kill -0 "$(cat "$RUN/$name.pid")" 2>/dev/null; then
    echo "[$name] already running (pid $(cat "$RUN/$name.pid"))"; return
  fi
  (cd "$BE" || exit 1
   env "$@" nohup node "${pre[@]+"${pre[@]}"}" --import tsx "$entry" >"$LOGS/$name.log" 2>&1 </dev/null &
   echo $! >"$RUN/$name.pid")
  echo "[$name] pid $(cat "$RUN/$name.pid"), log $LOGS/$name.log"
}

# One identity value: the client's EnvConfig.AppIdentityUri (the MWA dApp identity) also sets the local server's
# SIWS_DOMAIN (its authority) and SIWS_URI, so wallet and server always agree. Process env beats the backend .env.
IDENTITY_URI="$(sed -n 's/.*const string AppIdentityUri = "\([^"]*\)".*/\1/p' "$HERE/../../client/Assets/Scripts/Core/Env/EnvConfig.cs")"
[[ -n "$IDENTITY_URI" ]] || { echo "could not read EnvConfig.AppIdentityUri"; exit 1; }
SIWS_DOMAIN="$(echo "$IDENTITY_URI" | sed -E 's#^[a-zA-Z][a-zA-Z0-9+.-]*://([^/?#]+).*#\1#')"
echo "[siws] domain $SIWS_DOMAIN, uri $IDENTITY_URI (from EnvConfig.AppIdentityUri)"

start api      server/api/main.ts      PORT_API=4010 SIWS_DOMAIN="$SIWS_DOMAIN" SIWS_URI="$IDENTITY_URI"
start realtime server/realtime/main.ts PORT_REALTIME=2567
# Workers start fine from backend 7243073 (they crashed at 9f3e5ef). Opt out with CTLIFE_WORKERS=0.
if [[ "${CTLIFE_WORKERS:-1}" == "1" ]]; then start workers server/workers/main.ts PORT_WORKERS=0; fi

for i in $(seq 1 40); do
  if curl -fsS http://localhost:4010/healthz >/dev/null 2>&1; then break; fi
  sleep 1
done
curl -fsS http://localhost:4010/healthz && echo
if [[ "$BOTS" != "0" ]]; then
  start bots tools/bots/world.ts BOT_COUNT="$BOTS" BOT_VENUE=timeline_square
fi
echo "REST http://localhost:4010/v1  realtime ws://localhost:2567  livekit ws://localhost:7880"
