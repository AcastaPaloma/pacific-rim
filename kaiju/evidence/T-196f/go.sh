#!/bin/bash
# usage: go.sh arm clip
C=/private/tmp/claude-501/-Users-kuanw-Me-code-pacific-rim--claude-worktrees-fable-orchestrator-prompt-83a0a8/783d9ab8-fde5-48e3-b851-e65ddc3ac6c1/scratchpad/cap
cd /Users/kuanw/Me/code/pacific-rim/.claude/worktrees/fable-orchestrator-prompt-83a0a8/.wt/T-196f/kaiju/Pipelines/capture
O=/private/tmp/t196f_run/out/$1/$2; mkdir -p $O
/opt/miniconda3/envs/kaiju/bin/python capture.py style --set $C/$1/$2 --out $O > $O/stdout.json 2> $O/stderr.txt; echo "exit $?" > $O/exit.txt
