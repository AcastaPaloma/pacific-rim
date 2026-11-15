#!/bin/bash
# T-108f: re-bake the 109 flagged L1 tiles at bake_lod.py@8 into the integration cache.
cd "$(dirname "$0")/../.."
export KAIJU_GEO_CACHE=/Users/kuanw/Me/code/pacific-rim/.claude/worktrees/fable-orchestrator-prompt-83a0a8/kaiju/Pipelines/geo/cache
ARGS=""; for t in $(cat evidence/T-108f/tiles.txt); do ARGS="$ARGS --tile $t"; done
/opt/miniconda3/envs/kaiju/bin/python -I Pipelines/geo/bake_bay.py --stage lod --levels 2 --workers 5 --min-free-gb 20 --force $ARGS > evidence/T-108f/bake.log 2>&1
echo "exit $?" >> evidence/T-108f/bake.log
