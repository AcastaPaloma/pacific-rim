#!/bin/bash
# usage: run.sh <attempt-tag> <n> [extra capture args]   (project root = this worktree's kaiju/)
set -u
cd "$(dirname "$0")/../.."
tag=$1; n=$2; shift 2
until ! ps ax -o command | grep -q "[U]nity.app/Contents/MacOS/Unity -batchmode"; do sleep 30; done
m=$(memory_pressure | tail -1 | grep -o '[0-9]*%' | tr -d %); [ "${m:-0}" -lt 30 ] && { echo "low memory ${m}%"; exit 9; }
unity run . --no-tail --log-file evidence/T-194d/${tag}_$n.log --timeout 1800 -- -executeMethod Game.Tools.Capture.CaptureCli.Run -capturePaths docs/store/paths -captureOut evidence/T-194d/caps/${tag}_$n -captureScale 4,8 -captureMaxFrames 60 -captureEnable "$@"
echo "exit $?"
