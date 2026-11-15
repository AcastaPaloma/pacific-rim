#!/bin/bash
# T-005 assist: scenario batch (ScenarioCli) in this worktree. Usage: RUN=1 VAR=/path/variants.txt SCN=walk_fr010 bash run.sh
# starts only while fewer than 2 Unity batchmode processes run machine-wide (the interactive editor, no -batchmode, is not counted and never touched)
P=/Users/kuanw/Me/code/pacific-rim/.claude/worktrees/agent-a40d2a2d589edf5bf/kaiju
O=/private/tmp/claude-501/t005assist/run${RUN:-1}; mkdir -p $O; S=$O/status.txt; : > $S
nbatch(){ ps ax -o command | grep "^/Applications/Unity.*MacOS/Unity " | grep -- "-batchmode" | grep -vc AssetImportWorker; }
wait_q(){ while [ "$(nbatch)" -ge 2 ]; do sleep 20; done; }
cd $P
echo "commit $(git rev-parse --short HEAD) start $(date +%T)" >> $S
cp ${VAR} $O/variants.txt
wait_q; echo "started $(date +%T)" >> $S
unity run . --no-tail --log-file $O/scen.log --timeout 3600 -- -executeMethod Game.Units.Mech.Editor.ScenarioCli.Run -scenarios ${SCN:-walk_fr010} -out $O/out -variants $O/variants.txt -physlist none > $O/scen.out 2>&1
echo "scenarios rc=$? $(date +%T)" >> $S
cp $O/out/summary.txt $O/summary.txt
echo DONE >> $S
