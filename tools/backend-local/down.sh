#!/usr/bin/env bash
# Stop the local CT Life backend started by up.sh. Keeps the Postgres/Redis volumes.
#   tools/backend-local/down.sh          stop node processes + containers
#   tools/backend-local/down.sh --wipe   also delete the Postgres/Redis volumes (fresh DB)
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
BE="${CTLIFE_BACKEND_DIR:-$HERE/../../../ct-life-backend}"
BE="$(cd "$BE" && pwd)"
RUN="${CTLIFE_RUN_DIR:-$HOME/.ctlife-local/run}"

for name in bots workers realtime api; do
  if [[ -f "$RUN/$name.pid" ]]; then
    pid="$(cat "$RUN/$name.pid")"
    if kill -0 "$pid" 2>/dev/null; then
      kill "$pid" 2>/dev/null; sleep 1; kill -9 "$pid" 2>/dev/null || true
      echo "[$name] stopped ($pid)"
    fi
    rm -f "$RUN/$name.pid"
  fi
done
# tsx spawns a child; make sure nothing keeps the ports.
for port in 4010 2567; do
  pids="$(lsof -t -nP -iTCP:$port -sTCP:LISTEN 2>/dev/null | xargs ps -o pid=,comm= -p 2>/dev/null | awk '$2 ~ /node/ {print $1}')"
  [[ -n "$pids" ]] && kill $pids 2>/dev/null && echo "[port $port] killed node $pids"
done

if [[ "${1:-}" == "--wipe" ]]; then
  docker compose --project-directory "$BE" -f "$BE/docker-compose.yml" -f "$HERE/compose.override.yml" down -v
else
  docker compose --project-directory "$BE" -f "$BE/docker-compose.yml" -f "$HERE/compose.override.yml" stop
fi
