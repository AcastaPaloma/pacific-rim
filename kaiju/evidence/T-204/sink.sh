#!/bin/bash
cd "$(dirname "$0")/../.."
for n in 1 2; do
  evidence/T-204/wait.sh unity run . --no-tail --log-file evidence/T-204/sink_$n.log --timeout 1800 -- -executeMethod Game.Tools.Capture.CaptureCli.Run -capturePaths evidence/T-204/paths -captureOut evidence/T-204/caps/sink_$n -captureScale 4 -captureMaxFrames 60 -captureEnable -captureTerrainSink 0.02
  echo "run $n exit $?"
done
