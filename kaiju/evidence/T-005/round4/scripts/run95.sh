#!/bin/bash
# T-005 r4 step 9.5: walk_fr010 at imp 3 (batch Unity, ScenarioCli). Usage: RUN=1 VAR=variants95.txt bash run95.sh
P=/Users/kuanw/Me/code/pacific-rim/.claude/worktrees/fable-orchestrator-prompt-83a0a8/.wt/T-005-94b/kaiju
O=/private/tmp/claude-501/t005r4/run${RUN:-1}; mkdir -p $O; S=$O/status.txt; : > $S
wait_q(){ while [ "$(ps ax -o command | grep -i "[U]nity.app/Contents/MacOS/Unity " | grep -vc AssetImportWorker)" -ge 2 ]; do sleep 30; done; }
cd $P
echo "commit $(git rev-parse --short HEAD) start $(date +%T)" >> $S
cp $(dirname $0)/${VAR:-variants95.txt} $O/variants.txt
wait_q; unity run . --no-tail --log-file $O/scen.log --timeout 1800 -- -executeMethod Game.Units.Mech.Editor.ScenarioCli.Run -scenarios ${SCN:-walk_fr010} -out $O/out -variants $O/variants.txt -physlist none > $O/scen.out 2>&1
echo "scenarios rc=$? $(date +%T)" >> $S
echo DONE >> $S
