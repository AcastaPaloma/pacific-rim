#!/bin/bash
# T-201b: scenario batch (ScenarioCli, Unity batch mode, PhysX) in this worktree.
# Extra Unity CLI args: XARGS="-walkoverlay walk_tuning_fast.json".
# Usage: RUN=a1 VAR=/path/variants.txt SCN=walk_v3,walk_v4 bash run.sh   (out: $T201_OUT/run$RUN/out, summary.txt)
# Starts only while fewer than 2 Unity batchmode processes run machine-wide (AGENT_PROTOCOL 9).
P=$(cd "$(dirname "$0")/../../.." && pwd)
BASE=${T201_OUT:-/private/tmp/claude-501/t201b}
O=$BASE/run${RUN:-1}; mkdir -p $O; S=$O/status.txt; : > $S
nbatch(){ ps ax -o command | grep -i "[U]nity.app/Contents/MacOS/Unity .*-batchmode" | grep -vc AssetImportWorker; }
wait_q(){ while [ "$(nbatch)" -ge 2 ]; do sleep 20; done; }
cd $P
echo "commit $(git rev-parse --short HEAD) start $(date +%T)" >> $S
cp ${VAR} $O/variants.txt
wait_q; echo "started $(date +%T)" >> $S
unity run . --no-tail --log-file $O/scen.log --timeout 3600 -- -executeMethod Game.Units.Mech.Editor.ScenarioCli.Run -scenarios ${SCN:-walk_fr010} -out $O/out -variants $O/variants.txt -physlist none ${XARGS} > $O/scen.out 2>&1
echo "scenarios rc=$? $(date +%T)" >> $S
cp $O/out/summary.txt $O/summary.txt
echo DONE >> $S
