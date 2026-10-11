#!/usr/bin/env bash
# Realtime C# state classes from the backend contract (contract/realtime/*.ts, contract-v1.0.0-rc1), with the
# toolchain his repo pins: @colyseus/schema 5.0.36 (schema-codegen) + TypeScript 5.9.3, cached in
# ~/.cache/ctlife-codegen/schema-5.0.36. Command = contract/realtime/README.md.
# Message payloads are Zod schemas, which schema-codegen does not emit; they are hand-mirrored in
# client/Assets/Generated/Support/RealtimeMessages.cs (keep in sync with contract/realtime/messages.ts).
set -euo pipefail
REPO="$(cd "$(dirname "$0")/../.." && pwd)"
TOOLS="$HOME/.cache/ctlife-codegen/schema-5.0.36"
if [ ! -x "$TOOLS/node_modules/.bin/schema-codegen" ]; then
  mkdir -p "$TOOLS" && (cd "$TOOLS" && echo '{"private":true}' > package.json &&
    npm install --silent --no-audit --no-fund @colyseus/schema@5.0.36 typescript@5.9.3 zod@4)
fi
OUT="$REPO/client/Assets/Generated/Realtime"
# Generate into a temp dir and copy only the .cs files, so existing .meta GUIDs are kept.
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
cd "$REPO" && "$TOOLS/node_modules/.bin/schema-codegen" contract/realtime/*.ts --output "$TMP" --csharp --namespace CTLife.Realtime
mkdir -p "$OUT" && cp "$TMP"/*.cs "$OUT"/
