#!/usr/bin/env bash
# Web demo: WebGL2 player of the slice (demo profile baked in) -> Builds/web_demo/. The desktop build (build.sh) is untouched.
# Usage: Tools/Build/build_web.sh [--out DIR]     then: Tools/Build/serve_web.sh  (http://localhost:8642)
# Post-build: drops the terrain.tif layers (not read at runtime), gzips large StreamingAssets files (served with
# Content-Encoding: gzip by serve_web.sh) and writes StreamingAssets/web_files.txt, the list SliceWebPreload downloads.
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"; PROJ="$(cd "$HERE/../.." && pwd)"
PY="${KAIJU_PY:-/opt/miniconda3/envs/kaiju/bin/python}"; UNITY="${UNITY_CMD:-unity}"
OUT="$PROJ/Builds/web_demo"
while [ $# -gt 0 ]; do case "$1" in --out) OUT="$2"; shift 2;; *) echo "build_web.sh: unknown arg $1" >&2; exit 2;; esac; done
fail() { echo "build_web.sh: $*" >&2; exit 1; }
FREE_GB=$(df -g / | awk 'NR==2{print $4}'); [ "$FREE_GB" -ge 25 ] || fail "only ${FREE_GB} GB free (need 25)"
N=$(ps ax -o command | grep -i "[U]nity.app/Contents/MacOS/Unity .*-batchmode" | grep -vc AssetImportWorker)
[ "$N" -lt 1 ] || fail "$N Unity batch processes running (RAM guard: at most 1 machine-wide)"
LOGS="$OUT.logs"; mkdir -p "$LOGS"
( cd "$PROJ" && "$UNITY" run "$PROJ" -- -executeMethod Game.Tools.Build.PlayerBuild.Run -buildOut "$OUT" -buildTarget web -logFile "$LOGS/unity_build.log" ) > "$LOGS/unity_run.out" 2>&1
RC=$?; [ $RC -eq 0 ] || fail "Unity build failed (exit $RC), see $LOGS/unity_build.log"
APP="$OUT/KaijuGame"; [ -f "$APP/index.html" ] || fail "no index.html in $APP"
"$PY" -I - "$APP/StreamingAssets" <<'PYEOF' || fail "StreamingAssets post-process failed"
import gzip, os, sys
root = sys.argv[1]; files = []; raw = gz = 0
for d, _, fs in os.walk(root):
    for f in fs:
        p = os.path.join(d, f); rel = os.path.relpath(p, root)
        if f.endswith(".tif") or f.endswith(".meta") or f == "web_files.txt" or f.endswith(".gz"): 
            if f.endswith(".tif") or f.endswith(".meta"): os.remove(p)
            continue
        files.append(rel); sz = os.path.getsize(p); raw += sz
        if sz > 32768:
            with open(p, "rb") as a, gzip.open(p + ".gz", "wb", 6) as b: b.write(a.read())
            os.remove(p); gz += os.path.getsize(p + ".gz")
        else: gz += sz
open(os.path.join(root, "web_files.txt"), "w").write("\n".join(sorted(files)) + "\n")
print("web_files: %d files, %.1f MB raw, %.1f MB on the wire" % (len(files), raw / 1e6, gz / 1e6))
PYEOF
"$PY" -I - "$APP/index.html" <<'PYEOF' || fail "index.html patch failed"
import sys
p = sys.argv[1]; s = open(p).read()
s = s.replace('width="960" height="600"', 'width="1280" height="720"')   # the pixel look expects 1280x720
hook = ('<script>window.__kaijuLog=[];window.__kaijuT0=performance.now();(function(){for(const k of ["log","warn","error"]){const o=console[k];'
        'console[k]=function(...a){try{window.__kaijuLog.push([Math.round(performance.now()),k,a.join(" ")]);if(window.__kaijuLog.length>3000)window.__kaijuLog.shift();}catch(e){}o.apply(console,a);};}})();</script>')
if "__kaijuLog" not in s: s = s.replace("<head>", "<head>" + hook, 1)
open(p, "w").write(s)
PYEOF
du -sh "$APP" "$APP/Build" "$APP/StreamingAssets"
echo "build_web.sh: OK $APP   serve: Tools/Build/serve_web.sh"
