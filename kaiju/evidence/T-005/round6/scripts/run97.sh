#!/bin/bash
# T-005 r6: walk_fr010 at imp 3 (batch Unity, ScenarioCli). Usage: RUN=1 VAR=/path/variants.txt bash run96.sh
# starts only while fewer than 2 Unity batchmode processes run machine-wide (the interactive editor, no -batchmode, is not counted and never touched)
P=/Users/kuanw/Me/code/pacific-rim/.claude/worktrees/agent-ab189ecb6b375ff0e/kaiju
O=/private/tmp/claude-501/t005r6/run${RUN:-1}; mkdir -p $O; S=$O/status.txt; : > $S
nbatch(){ ps ax -o command | grep "[U]nity.app/Contents/MacOS/Unity " | grep -- "-batchmode" | grep -vc AssetImportWorker; }
wait_q(){ while [ "$(nbatch)" -ge 2 ]; do sleep 30; done; }
cd $P
echo "commit $(git rev-parse --short HEAD) start $(date +%T)" >> $S
cp ${VAR} $O/variants.txt
wait_q; unity run . --no-tail --log-file $O/scen.log --timeout 3000 -- -executeMethod Game.Units.Mech.Editor.ScenarioCli.Run -scenarios ${SCN:-walk_fr010} -out $O/out -variants $O/variants.txt -physlist none > $O/scen.out 2>&1
echo "scenarios rc=$? $(date +%T)" >> $S
cp $O/out/summary.txt $O/summary.txt
echo DONE >> $S
