#!/bin/bash
cd /Users/kuanw/Me/code/pacific-rim/.claude/worktrees/fable-orchestrator-prompt-83a0a8/kaiju
for n in 1 2 3 4; do
  waited=0
  while true; do
    c=$(ps ax -o command | grep -i "[U]nity.app/Contents/MacOS/Unity .*-batchmode" | grep -vc AssetImportWorker)
    m=$(memory_pressure | tail -1 | grep -o '[0-9]*%' | tr -d %)
    [ "$c" = 0 ] && [ "${m:-0}" -ge 40 ] && break
    waited=$((waited+1)); [ $waited -gt 30 ] && { echo "slot timeout run $n"; exit 9; }; sleep 60
  done
  unity run . --no-tail --log-file /tmp/kaiju-t196h/sink0_$n.log --timeout 1500 -- -executeMethod Game.Tools.Capture.CaptureCli.Run -capturePaths docs/store/paths/p4_d_dolly_v4.json -captureOut /tmp/kaiju-t196h/sink0_$n -captureScale 4,8 -captureMaxFrames 60 -captureEnable -captureTerrainSink 0 -nosound
  echo "run $n exit $?"
done
echo done
