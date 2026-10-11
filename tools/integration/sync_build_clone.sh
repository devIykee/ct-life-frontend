#!/usr/bin/env bash
# Sync the main checkout's client into an APFS clone used for APK builds, leaving out feature lanes that are not yet
# wired into the app (their in-progress code must not block a build). Usage: sync_build_clone.sh [lane ...to include]
# First time: cp -c -R client ../ct-life-build/client   (copy-on-write clone incl. Library; no extra disk at first)
set -euo pipefail
REPO="$(cd "$(dirname "$0")/../.." && pwd)"
B="${CTLIFE_BUILD_CLONE:-$REPO/../ct-life-build/client}"
INCLUDE=" Money ${*:-} "
EXCL=()
for d in "$REPO"/client/Assets/Scripts/Features/*/; do
  n="$(basename "$d")"
  [[ "$INCLUDE" == *" $n "* ]] || EXCL+=(--exclude "Scripts/Features/$n" --exclude "Scripts/Features/$n.meta")
done
rsync -a --delete --delete-excluded ${EXCL[@]+"${EXCL[@]}"} --exclude "Scripts/Audio" "$REPO/client/Assets/" "$B/Assets/"
# Lane-owned assemblies edited in place (CTLife.Audio): build the last committed version unless AUDIO_WORKTREE=1.
if [[ "${AUDIO_WORKTREE:-0}" == "1" ]]; then
  rsync -a --delete "$REPO/client/Assets/Scripts/Audio/" "$B/Assets/Scripts/Audio/"
else
  rm -rf "$B/Assets/Scripts/Audio" && mkdir -p "$B/Assets/Scripts"
  git -C "$REPO" archive HEAD client/Assets/Scripts/Audio | tar -x -C "$B/.." --strip-components=0
fi
rsync -a --delete "$REPO/client/ProjectSettings/" "$B/ProjectSettings/"
rsync -a --delete "$REPO/client/Packages/" "$B/Packages/"
echo "synced to $B (lanes excluded: ${EXCL[*]+${EXCL[*]}})"
