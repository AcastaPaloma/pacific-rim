#!/bin/bash
# T-005 r2 step 6.7: regenerate evidence at head for the best stable configuration
P=/Users/kuanw/Me/code/pacific-rim/.claude/worktrees/fable-orchestrator-prompt-83a0a8/.wt/T-005/kaiju
E=evidence/T-005/round2/final; L=/private/tmp/claude-501/t005r2/final; mkdir -p $L; S=$L/status.txt; : > $S
wait_q(){ while [ "$(ps ax -o command | grep -i "[U]nity.app/Contents/MacOS/Unity .*-batchmode" | grep -vc AssetImportWorker)" -ge 2 ]; do sleep 60; done; }
cd $P; mkdir -p $E/tests $E/imp1
echo "commit $(git rev-parse --short HEAD) start $(date +%T)" >> $S
wait_q; unity test . --mode EditMode -o $E/tests/unity_editmode.xml > $L/editmode.out 2>&1; echo "editmode rc=$? $(grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' $E/tests/unity_editmode.xml | head -1) $(date +%T)" >> $S
dotnet test DotnetTests/Units.Mech.Tests --logger "trx;LogFileName=$P/$E/tests/units_mech.trx" > $L/dotnet.out 2>&1; echo "dotnet rc=$? $(grep -E 'Passed!|Failed!' $L/dotnet.out | tail -1) $(date +%T)" >> $S
wait_q; unity run . --no-tail --log-file $L/all.log --timeout 1500 -- -executeMethod Game.Units.Mech.Editor.ScenarioCli.Run -scenarios all -out $E -strips -physlist stand,single_stance,walk_fr020,walk_fr025,shove_060,intent_over_limit,curve,stomp > $L/all.out 2>&1; echo "all rc=$? $(date +%T)" >> $S
wait_q; unity run . --no-tail --log-file $L/rep.log --timeout 900 -- -executeMethod Game.Units.Mech.Editor.ScenarioCli.Run -scenarios repeat60 -repeat 3 -nophys -out $L/rep > $L/rep.out 2>&1; mkdir -p $E/gait; cp $L/rep/gait/* $E/gait/ 2>/dev/null; cp $L/rep/summary.txt $E/summary_repeat60.txt; echo "repeat rc=$? $(date +%T)" >> $S
printf 'imp1cap025 {"imp":1,"imp_step_length_max_frac":0.25}\n' > $L/variants.txt
wait_q; unity run . --no-tail --log-file $L/imp1.log --timeout 900 -- -executeMethod Game.Units.Mech.Editor.ScenarioCli.Run -scenarios stand,start,stop,walk_fr010,walk_fr015,walk_fr020,repeat60 -physlist walk_fr010 -variants $L/variants.txt -out $E/imp1 > $L/imp1.out 2>&1; echo "imp1 rc=$? $(date +%T)" >> $S
find $E -name "*.jsonl" -exec gzip -f {} \;
du -sh $E >> $S; echo DONE >> $S
