#!/usr/bin/env bash
# T-040: waits until fewer than 2 Unity batch processes run machine-wide, then runs EditMode once (new .asmdefs).
WT="$(cd "$(dirname "$0")/../../.." && pwd)"
LOG="$WT/evidence/T-040/unity-editmode.log"
while [ "$(pgrep -fl 'Unity.*-batchmode' | grep -v AssetImportWorker | wc -l | tr -d ' ')" -ge 2 ]; do sleep 30; done
if grep -rl "fileFormatVersion: 2guid" "$WT/Assets" | head -1 | grep -q .; then echo "broken meta" > "$LOG"; exit 1; fi
unity test "$WT" --mode EditMode > "$LOG" 2>&1
echo "exit=$?" >> "$LOG"
