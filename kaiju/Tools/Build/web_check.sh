#!/usr/bin/env bash
# Opens the served web demo in a fresh, throwaway Chrome profile (visible window, 1400x900) and runs web_check.py.
# Usage: Tools/Build/web_check.sh OUTDIR [URL]   (start Tools/Build/serve_web.sh first)
HERE="$(cd "$(dirname "$0")" && pwd)"; OUT="$1"; URL="${2:-http://localhost:8642/}"
PY="${KAIJU_PY:-/opt/miniconda3/envs/kaiju/bin/python}"; PROF="$(mktemp -d /tmp/kaiju-chrome.XXXX)"
"/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" --user-data-dir="$PROF" --remote-debugging-port=9333 --no-first-run \
  --no-default-browser-check --window-size=1400,900 --disable-background-timer-throttling --disable-renderer-backgrounding \
  --disable-backgrounding-occluded-windows about:blank > "$OUT.chrome.log" 2>&1 &
CP=$!; for i in $(seq 30); do curl -s 127.0.0.1:9333/json >/dev/null && break; sleep 1; done
"$PY" -I "$HERE/web_check.py" "$URL" "$OUT"; RC=$?
kill $CP; sleep 1; rm -rf "$PROF"; exit $RC
