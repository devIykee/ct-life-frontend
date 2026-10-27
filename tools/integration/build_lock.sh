#!/usr/bin/env bash
# One heavy Unity build (Android/IL2CPP player build, island rebuild) at a time across the parallel lanes, so the Mac
# doesn't run three IL2CPP compiles at once. Editor test runs and script compiles don't need it.
#   tools/integration/build_lock.sh run <owner> -- <command...>   -> waits for the lock, runs the command, releases it
#   tools/integration/build_lock.sh status                         -> who is building
# The lock is a directory (mkdir is atomic); a lock older than LOCK_TTL_MIN minutes is treated as stale.
set -euo pipefail
LOCK="$HOME/.ctlife-local/build.lock"
TTL="${LOCK_TTL_MIN:-40}"
mkdir -p "$(dirname "$LOCK")"

stale() { [ -n "$(find "$LOCK" -maxdepth 0 -mmin +"$TTL" 2>/dev/null)" ]; }

case "${1:-status}" in
  run)
    owner="${2:?owner}"; shift 2; [ "${1:-}" = "--" ] && shift
    until mkdir "$LOCK" 2>/dev/null; do
      if stale; then rm -rf "$LOCK"; continue; fi
      echo "build lock held by $(cat "$LOCK/owner" 2>/dev/null) since $(cat "$LOCK/since" 2>/dev/null); waiting..." >&2
      sleep 20
    done
    echo "$owner" > "$LOCK/owner"; date -u +%FT%TZ > "$LOCK/since"
    trap 'rm -rf "$LOCK"' EXIT
    "$@" ;;
  status)
    if [ -d "$LOCK" ]; then echo "held by $(cat "$LOCK/owner") since $(cat "$LOCK/since")$(stale && echo ' (stale)')"; else echo "free"; fi ;;
  *) echo "usage: $0 run <owner> -- <command...> | status" >&2; exit 2 ;;
esac
