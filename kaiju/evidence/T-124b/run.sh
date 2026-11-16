#!/bin/bash
# usage: run.sh <label> <seeds> <extra args...>   (kaiju project root of the T-124b worktree)
P=/Users/kuanw/Me/code/pacific-rim/.claude/worktrees/fable-orchestrator-prompt-83a0a8/kaiju/.wt/T-124b/kaiju
L=$1; S=$2; shift 2
mkdir -p $P/evidence/T-124b/$L
/Applications/Unity/Hub/Editor/6000.6.9f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -quit -projectPath $P -executeMethod Game.Perf.Bay.M008BayRun.Run -logFile $P/evidence/T-124b/$L/unity.log \
 -m008Out $P/evidence/T-124b/$L -m008Tiles /Users/kuanw/Me/code/pacific-rim/.claude/worktrees/fable-orchestrator-prompt-83a0a8/kaiju/Pipelines/geo/cache/bake/tiles/v1 -m008Scenario bay -m008Seeds $S -m008Raster $P/evidence/T-115/bay_cost_256m_v1.bin "$@"
