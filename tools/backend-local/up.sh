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

export TSX_TSCONFIG_PATH="$HERE/tsconfig.backend.json"   # see tsconfig.backend.json

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

start api      server/api/main.ts      PORT_API=4010
start realtime server/realtime/main.ts PORT_REALTIME=2567
# Workers are not started: at backend 9f3e5ef server/workers/main.ts crashes at startup
# (new URL(<bare redis host>) throws, and BullMQ 6 rejects the ':' in its queue names).
# See docs/progress/backend-dev-feedback.md. Opt in with CTLIFE_WORKERS=1 once fixed.
if [[ "${CTLIFE_WORKERS:-0}" == "1" ]]; then start workers server/workers/main.ts PORT_WORKERS=0; fi

for i in $(seq 1 40); do
  if curl -fsS http://localhost:4010/healthz >/dev/null 2>&1; then break; fi
  sleep 1
done
curl -fsS http://localhost:4010/healthz && echo
if [[ "$BOTS" != "0" ]]; then
  start bots tools/bots/world.ts BOT_COUNT="$BOTS" BOT_VENUE=timeline_square
fi
echo "REST http://localhost:4010/v1  realtime ws://localhost:2567  livekit ws://localhost:7880"
