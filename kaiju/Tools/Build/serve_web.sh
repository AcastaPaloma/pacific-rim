#!/usr/bin/env bash
# Local-only static server for the web demo (binds 127.0.0.1; never public). Usage: Tools/Build/serve_web.sh [DIR] [PORT]
HERE="$(cd "$(dirname "$0")" && pwd)"; PROJ="$(cd "$HERE/../.." && pwd)"
PY="${KAIJU_PY:-/opt/miniconda3/envs/kaiju/bin/python}"
exec "$PY" -I "$HERE/serve_web.py" "${1:-$PROJ/Builds/web_demo/KaijuGame}" "${2:-8642}"
