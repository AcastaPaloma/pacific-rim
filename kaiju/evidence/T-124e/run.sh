#!/bin/bash
# usage: run.sh <label> <seeds> <extra args...>   (Bay M-008 batch run; project = the T-124e worktree; -nographics => audio off, silent)
P=/Users/kuanw/Me/code/pacific-rim/.claude/worktrees/fable-orchestrator-prompt-83a0a8/kaiju/.wt/T-124e/kaiju
L=$1; S=$2; shift 2; NG=${NG--nographics}   # NG="" for a rendered (graphics device) run
mkdir -p $P/evidence/T-124e/$L
/Applications/Unity/Hub/Editor/6000.6.9f1/Unity.app/Contents/MacOS/Unity -batchmode $NG -quit -projectPath $P -executeMethod Game.Perf.Bay.M008BayRun.Run -logFile $P/evidence/T-124e/$L/unity.log \
 -m008Out $P/evidence/T-124e/$L -m008Tiles /Users/kuanw/Me/code/pacific-rim/.claude/worktrees/fable-orchestrator-prompt-83a0a8/kaiju/Pipelines/geo/cache/bake/tiles/v1 -m008Scenario bay -m008Seeds $S "$@"
