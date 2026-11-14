#!/usr/bin/env bash
# Reproduces every T-001 evidence artifact in evidence/T-001/. Run from anywhere; uses the project that contains this file.
# Needs: dotnet SDK (11.x), Unity 6000.6.9f1 via the `unity` CLI, the kaiju conda env. Starts one Unity batch process at a time.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"; cd "$ROOT"
EV=evidence/T-001; RAW=$EV/raw; PY=/opt/miniconda3/envs/kaiju/bin/python
rm -rf "$RAW"; mkdir -p "$RAW"
echo "commit $(git rev-parse HEAD)" > $RAW/commit.txt
{ echo "dotnet $(dotnet --version)"; echo "unity $(cat ProjectSettings/ProjectVersion.txt | head -1)"; sw_vers 2>/dev/null | tr '\n' ' '; echo; sysctl -n machdep.cpu.brand_string 2>/dev/null; } > $EV/versions.txt

echo "== C1: dotnet build (no Unity DLLs)"
dotnet build DotnetTests/Core.Tests -c Debug > $RAW/dotnet-build.log 2>&1
grep -E "Warn|Error|Build succeeded" $RAW/dotnet-build.log | tail -3
grep -h -o '<[A-Za-z]*Reference Include="[^"]*"' DotnetTests/*/*.csproj | sort -u > $EV/dotnet-references.txt

echo "== C2/C8/C19/C28: dotnet test in two separate processes"
for run in 1 2; do
  KAIJU_EVIDENCE_DIR="$ROOT/$RAW/dotnet-run$run" dotnet test DotnetTests/Core.Tests --no-build \
    --logger "trx;LogFileName=dotnet-test-run$run.trx" --results-directory "$ROOT/$RAW" > $RAW/dotnet-test-run$run.log 2>&1 || true
  tail -1 $RAW/dotnet-test-run$run.log
done
cp $RAW/dotnet-test-run1.trx $EV/dotnet-test.trx
cp $RAW/dotnet-test-run1.log $EV/dotnet-test.log

echo "== C3: forced recompile of the Core assemblies, compile log"
rm -rf Library/ScriptAssemblies/Game.Core* Library/Bee/artifacts/*/Game.Core*
unity run "$ROOT" --no-tail --log-file "$ROOT/$RAW/unity-compile.log" --timeout 1500 -- -executeMethod Game.Core.Unity.CoreConfigAsset.CreateOrUpdate > /dev/null 2>&1 || true
{
  echo "# Unity batch compile of $(git rev-parse --short HEAD) (Library/ScriptAssemblies/Game.Core* and Bee artifacts deleted first to force recompilation)"
  echo "# command: unity run <project> --no-tail --log-file raw/unity-compile.log --timeout 1500 -- -executeMethod Game.Core.Unity.CoreConfigAsset.CreateOrUpdate"
  echo "Csc lines for Core assemblies:"; grep -E "Csc .*Game\.Core" $RAW/unity-compile.log || true
  echo "error CS lines (all assemblies): $(grep -c 'error CS' $RAW/unity-compile.log || true)"
  echo "warning CS lines (all assemblies): $(grep -c 'warning CS' $RAW/unity-compile.log || true)"
  echo "warning/error CS lines in Assets/Game/Core: $(grep -E '(error|warning) CS' $RAW/unity-compile.log | grep -c 'Assets/Game/Core' || true)"
  grep -E "(error|warning) CS" $RAW/unity-compile.log || true
  grep -E "\[Core\]|Scripts have compiler errors|compilation failed" $RAW/unity-compile.log || true
} > $EV/compile.log
cat $EV/compile.log | tail -6

echo "== C2/C12/C16: Unity EditMode (shared + Unity-only tests)"
KAIJU_EVIDENCE_DIR="$ROOT/$RAW/unity" unity test "$ROOT" --mode EditMode -o "$ROOT/$EV/unity-editmode.xml" > $RAW/unity-test.log 2>&1 || true
head -2 $EV/unity-editmode.xml | tail -1 | grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"'

echo "== C2: test-name diff"
$PY $EV/tools/test_names.py $EV/dotnet-test.trx $EV/unity-editmode.xml $EV/test-name-diff.txt || true

echo "== merge measurement lines"
for f in alloc bench trace-hashes save-baseline test-hygiene; do
  { echo "# $f: lines written by the tests (Evidence.Line) per runner/process; commit $(git rev-parse --short HEAD)"
    for r in dotnet-run1 dotnet-run2 unity; do
      src=$RAW/$r; file=$(ls $src/*-$f.txt 2>/dev/null | head -1 || true)
      [ -n "$file" ] && { echo "## $r"; cat "$file"; }
    done; } > $EV/$f.txt
done
cp $RAW/unity/unity-sample-save.json $EV/sample-save.json

echo "== grep counts (C16, C22, C27, C1, C33)"
$EV/tools/grep.sh > $EV/grep-counts.txt
for r in dotnet-run1 unity; do echo "## test-produced checks ($r)"; cat $RAW/$r/*-grep-counts.txt; done >> $EV/grep-counts.txt

echo "== C6: gitignore exception"
{ echo '$ git check-ignore -v DotnetTests/Core.Tests/Core.Tests.csproj DotnetTests/Core/Core.csproj DotnetTests/Core.Contracts/Core.Contracts.csproj Assembly-CSharp.csproj Game.Core.csproj DotnetTests/Core/obj/x DotnetTests/Core.Tests/bin/x'
  for p in DotnetTests/Core.Tests/Core.Tests.csproj DotnetTests/Core/Core.csproj DotnetTests/Core.Contracts/Core.Contracts.csproj Assembly-CSharp.csproj Game.Core.csproj DotnetTests/Core/obj/x DotnetTests/Core.Tests/bin/x; do
    git check-ignore -v --no-index "$p" || echo "(not ignored: tracked-able)  $p"; done
  echo; echo '$ git ls-files DotnetTests'; git ls-files DotnetTests
  echo; echo "# every file/folder under Assets/Game/Core and Assets/Game/Config has a .meta:"
  missing=0; while IFS= read -r p; do [ -e "$p.meta" ] || { echo "MISSING META: $p"; missing=$((missing+1)); }; done < <(find Assets/Game/Core Assets/Game/Config -mindepth 1 ! -name '*.meta' ! -name '.gitkeep' ! -name '.DS_Store')
  echo "missing metas: $missing"; } > $EV/check-ignore.txt
tail -1 $EV/check-ignore.txt
echo done
