#!/bin/bash
# T-005 r3 step 8.5: EditMode tests, then ScenarioCli walk runs (imp 0 baseline vs imp 1)
P=/Users/kuanw/Me/code/pacific-rim/.claude/worktrees/fable-orchestrator-prompt-83a0a8/.wt/T-005/kaiju
O=/private/tmp/claude-501/t005r3/run${RUN:-1}; mkdir -p $O; S=$O/status.txt; : > $S
wait_q(){ while [ "$(ps ax -o command | grep -i "[U]nity.app/Contents/MacOS/Unity .*-batchmode" | grep -vc AssetImportWorker)" -ge 2 ]; do sleep 60; done; }
cd $P
echo "commit $(git rev-parse --short HEAD) start $(date +%T)" >> $S
if [ -z "$SKIPTEST" ]; then
wait_q; unity test . --mode EditMode -o $O/editmode.xml > $O/editmode.out 2>&1; echo "editmode rc=$? $(date +%T)" >> $S
grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' $O/editmode.xml | head -1 >> $S
fi
cat $(dirname $0)/${VAR:-variants85.txt} > $O/variants.txt
wait_q; unity run . --no-tail --log-file $O/scen.log --timeout 3000 -- -executeMethod Game.Units.Mech.Editor.ScenarioCli.Run -scenarios ${SCN:-stand,walk_fr010,walk_fr015,walk_fr020} -out $O/out -variants $O/variants.txt -physlist none > $O/scen.out 2>&1
echo "scenarios rc=$? $(date +%T)" >> $S
echo DONE >> $S
