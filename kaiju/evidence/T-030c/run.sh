#!/usr/bin/env bash
# T-030c two-process co-op run: host and client are two OS processes talking over real UDP on 127.0.0.1.
# Usage: evidence/T-030c/run.sh  (from anywhere); writes evidence/T-030c/coop-run/*
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"; proj="$here/../../DotnetTests/Net.CoopRun"
out="$here/coop-run"; mkdir -p "$out"
dotnet build "$proj" -c Release -v q > /dev/null
bin="$proj/bin/Release/net11.0/Net.CoopRun.dll"
port=$((47000 + RANDOM % 1000))
dotnet "$bin" host --port $port --seconds 34 --out "$out" > "$out/host.stdout" 2>&1 &
hp=$!
sleep 1
dotnet "$bin" client --port $port --seconds 14 --gap 2 --seconds2 14 --out "$out" > "$out/client.stdout" 2>&1 &
cp=$!
wait $cp; wait $hp
/opt/miniconda3/envs/kaiju/bin/python "$here/analyse.py" "$out" | tee "$out/analysis.txt"
