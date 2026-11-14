#!/usr/bin/env bash
# Source greps over Core (Contracts + Runtime) and the adapter. Run from the project root. Comments are included in the search
# (conservative); each section prints the command, the hit count and the hits.
cd "$(cd "$(dirname "$0")/../../.." && pwd)"
C="Assets/Game/Core/Contracts Assets/Game/Core/Runtime"
run() { echo "\$ grep -rnE '$1' $2"; out=$(grep -rnE "$1" $2 --include='*.cs' || true); n=$( [ -z "$out" ] && echo 0 || echo "$out" | wc -l | tr -d ' '); echo "hits: $n"; [ -n "$out" ] && echo "$out"; echo; }
echo "# grep counts, commit $(git rev-parse --short HEAD)"; echo
echo "## C1: Unity references outside the adapter"; run 'UnityEngine|UnityEditor|using Unity' "$C"
echo "## C1: Unity references in the adapter (expected > 0)"; echo "hits: $(grep -rlE 'UnityEngine' Assets/Game/Core/Unity --include='*.cs' | wc -l | tr -d ' ') files"; echo
mutable_statics() { grep -rnE '\bstatic\b[^(]*;' $1 --include='*.cs' | grep -vE '\bconst\b|\breadonly\b|\{ get; \}|=>' || true; }
echo "## C16: static fields that are not const, readonly or get-only (Core)"
echo "\$ grep -rnE '\\bstatic\\b[^(]*;' <Core> | grep -vE 'const|readonly|{ get; }|=>'"; out=$(mutable_statics "$C"); echo "hits: $( [ -z "$out" ] && echo 0 || echo "$out" | wc -l | tr -d ' ')"; echo "$out"; echo
echo "## C16: same scan over the adapter (expected: exactly the one composition root s_root)"
out=$(mutable_statics Assets/Game/Core/Unity); echo "hits: $( [ -z "$out" ] && echo 0 || echo "$out" | wc -l | tr -d ' ')"; echo "$out"; echo
echo "## C22: hard-coded player/mech/kaiju/pilot counts"; run '[Pp]layer_?[0-9]+\b|[Mm]ax_?([Pp]layers?|[Mm]echs?|[Kk]aiju|[Pp]ilots?|[Ss]lots?)\b|new [A-Za-z]+\[2\]' "$C Assets/Game/Core/Unity"
echo "## C27/C19: wall clock, randomness, GUIDs, instance IDs, string hashing"; run 'DateTime\.(Utc)?Now|Stopwatch|TickCount|\bRandom\b|NewGuid|GetInstanceID|Time\.(time|deltaTime)' "$C"
echo "## C19: GetHashCode calls in Ids.cs"; run '\.GetHashCode\(\)' "Assets/Game/Core/Contracts/Ids.cs"
echo "## C13: string topics (Subscribe/Publish taking a string)"; run '(Subscribe|Publish)\(string' "$C"
echo "## A2/B: LINQ in Core"; run 'using System\.Linq|\.Select\(|\.Where\(' "$C"
echo "## C33: building knowledge in the save code"; run 'Building' "Assets/Game/Core/Contracts/Save.cs Assets/Game/Core/Runtime/Save.cs"
echo "## size guideline: lines in Contracts + Runtime (all lines / non-blank non-comment)"
cat Assets/Game/Core/Contracts/*.cs Assets/Game/Core/Runtime/*.cs | wc -l | tr -d ' '
cat Assets/Game/Core/Contracts/*.cs Assets/Game/Core/Runtime/*.cs | grep -vE '^\s*$|^\s*//' | wc -l | tr -d ' '
