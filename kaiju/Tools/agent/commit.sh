#!/usr/bin/env bash
# Agent commit helper: commit ONLY the given paths and push the current branch.
# Usage (from anywhere inside the worktree):  Tools/agent/commit.sh "<message>" <path> [<path>...]
# Safe for several agents sharing one worktree: serialised by a lock, never uses `git add -A`.
set -uo pipefail
if [ "$#" -lt 2 ]; then echo "usage: commit.sh \"<message>\" <path>..." >&2; exit 2; fi
msg="$1"; shift
common="$(cd "$(git rev-parse --git-common-dir)" && pwd)"
lock="$common/agent-commit.lock"
i=0
while ! mkdir "$lock" 2>/dev/null; do
  i=$((i+1))
  # break a stale lock (holder died) after 3 minutes
  if [ -d "$lock" ] && [ -n "$(find "$lock" -maxdepth 0 -mmin +3 2>/dev/null)" ]; then rmdir "$lock" 2>/dev/null; continue; fi
  if [ "$i" -gt 240 ]; then echo "commit.sh: lock timeout" >&2; exit 1; fi
  sleep 1
done
trap 'rmdir "$lock" 2>/dev/null' EXIT
git add -- "$@" || { echo "commit.sh: git add failed" >&2; exit 1; }
if git diff --cached --quiet -- "$@"; then echo "commit.sh: nothing to commit for given paths"; exit 0; fi
git commit -q -m "$msg" -m "Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>" -- "$@" || { echo "commit.sh: commit failed" >&2; exit 1; }
branch="$(git rev-parse --abbrev-ref HEAD)"
case "$branch" in main|master) echo "commit.sh: refusing to push $branch" >&2; exit 1;; esac
for n in 1 2 3 4; do
  if git push -q -u origin "$branch"; then echo "commit.sh: pushed $(git rev-parse --short HEAD) to origin/$branch"; exit 0; fi
  git pull -q --rebase origin "$branch" 2>/dev/null || true
  sleep 3
done
echo "commit.sh: committed $(git rev-parse --short HEAD) locally but PUSH FAILED" >&2; exit 1
