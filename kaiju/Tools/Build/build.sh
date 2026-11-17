#!/usr/bin/env bash
# T-140: reproducible macOS player build -> attribution bundle -> tile copy -> manifest -> smoke test (-> M-121 repro check).
# Usage: Tools/Build/build.sh [--out DIR] [--tile-set FILE] [--tiles-src DIR] [--repro] [--no-smoke] [--dev] [--target mac|win64|windows] [--dry-run]
#   --out DIR       build directory (default /tmp/kaiju-build/<commit>); must be under /tmp or a gitignored Build*/ dir
#   --tile-set FILE JSON {"tiles":[...]} copied (hash-verified) into the player (default: the committed district tiles)
#   --tiles-src DIR tile root the set is copied from (default Assets/StreamingAssets/Geo/tiles/v1; never the 75 GB Bay cache)
#   --repro         build twice in ONE Unity session (<out>, <out>.repro2), write both manifests, run the M-121 diff,
#                   delete the repro build (size) when the diff passes
#   --target T      mac (default) | win64 (Q-231: Windows x64 player <out>/b1/KaijuGame.exe via the Unity Windows module; cannot be
#                   smoke-run on a Mac, so pass --no-smoke: the smoke step is refused for win64; Windows run = tracked row "untested")
#   --dry-run       print the resolved plan (target, output, player path, Unity command) and exit 0 before any Unity/Python work (Q-231)
#   --no-smoke      skip the player smoke test.  Windows run check: on a Windows box use Tools/Build/smoke.py --app <dir|exe> (see tasks/Q-231.progress.md)
# Needs: a free Unity batch slot (< 2 real batch processes machine-wide), >= 25 GB free disk, python env `kaiju`.
# Never publishes, never touches the Steamworks SDK; platform.steam must stay false (the manifest gate fails otherwise).
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"; PROJ="$(cd "$HERE/../.." && pwd)"
PY="${KAIJU_PY:-/opt/miniconda3/envs/kaiju/bin/python}"; UNITY="${UNITY_CMD:-unity}"
OUT=""; TILESET=""; TILESRC="$PROJ/Assets/StreamingAssets/Geo/tiles/v1"; REPRO=0; SMOKE=1; DEV=""; TARGET=mac; DRY=0
while [ $# -gt 0 ]; do case "$1" in
  --out) OUT="$2"; shift 2;; --tile-set) TILESET="$2"; shift 2;; --tiles-src) TILESRC="$2"; shift 2;;
  --repro) REPRO=1; shift;; --no-smoke) SMOKE=0; shift;; --dev) DEV="-buildDev"; shift;; --target) TARGET="$2"; shift 2;; --dry-run) DRY=1; shift;;
  *) echo "build.sh: unknown arg $1" >&2; exit 2;; esac; done
[ "$TARGET" = windows ] && TARGET=win64   # profile alias (Q-231)
case "$TARGET" in mac|win64) ;; *) echo "build.sh: --target must be mac, win64 or windows" >&2; exit 2;; esac
fail() { echo "build.sh: $*" >&2; exit 1; }
COMMIT="$(git -C "$PROJ" rev-parse --short=10 HEAD)"; [ -n "$OUT" ] || OUT="/tmp/kaiju-build/$COMMIT"
case "$OUT" in /tmp/*|/private/tmp/*|*/Build/*|*/Builds/*) ;; *) fail "--out must be under /tmp or a Build(s)/ dir (gitignored)";; esac
case "$TILESRC" in *Pipelines/geo/cache*) fail "refusing the Bay cache as tile source: use a small tile set under a committed tile root";; esac

if [ "$DRY" = 1 ]; then
  PL=KaijuGame.app; [ "$TARGET" = win64 ] && PL=KaijuGame.exe
  echo "build.sh DRY-RUN: target=$TARGET (Unity -buildTarget $TARGET => $([ "$TARGET" = win64 ] && echo StandaloneWindows64 || echo StandaloneOSX)) scripting=project default (Mono; no IL2CPP override)"
  echo "build.sh DRY-RUN: player=$OUT/b1/$PL manifest=$OUT/b1.manifest.json smoke=$([ "$SMOKE" = 1 ] && [ "$TARGET" != win64 ] && echo run || echo skipped) repro=$REPRO"
  echo "build.sh DRY-RUN: $UNITY run $PROJ -- -executeMethod Game.Tools.Build.PlayerBuild.Run -buildOut $OUT/b1 -buildRepeat $([ "$REPRO" = 1 ] && echo 2 || echo 1) -buildTarget $TARGET $DEV"
  exit 0
fi

# preflight
FREE_GB=$(df -g / | awk 'NR==2{print $4}'); [ "$FREE_GB" -ge 25 ] || fail "only ${FREE_GB} GB free (need 25)"
[ -z "$(grep -rl 'fileFormatVersion: 2guid' "$PROJ/Assets" 2>/dev/null)" ] || fail "broken hand-written .meta (fileFormatVersion: 2guid)"
N=$(ps ax -o command | grep -i "[U]nity.app/Contents/MacOS/Unity .*-batchmode" | grep -vc AssetImportWorker)   # AGENT_PROTOCOL s9 form (pgrep -fl counted other sessions' multi-line shell commands)
[ "$N" -lt 2 ] || fail "$N Unity batch processes running (limit 2)"
mkdir -p "$OUT/logs"; rm -rf "$OUT/app.manifest.json"

# tile set
if [ -z "$TILESET" ]; then
  TILESET="$OUT/district_tiles.json"
  "$PY" -I - "$TILESRC" "$TILESET" <<'PYEOF' || fail "tile set generation failed"
import json, os, sys
root, out = sys.argv[1:3]
tiles = sorted("L%s_%s" % (lv[1:], d) for lv in os.listdir(root) if lv.startswith("L") and os.path.isdir(os.path.join(root, lv))
               for d in os.listdir(os.path.join(root, lv)) if os.path.isdir(os.path.join(root, lv, d)))
json.dump({"tiles": tiles}, open(out, "w"), indent=1)
print("tile set: %d tiles" % len(tiles))
PYEOF
fi

# 1. attribution bundle (M-125; exit 1 fails the build) -- before the Unity run so a licence gap costs no build
"$PY" -I "$PROJ/Pipelines/build/attribution.py" --geo-root "$PROJ/Assets/StreamingAssets/Geo" --out-dir "$OUT/attribution" || fail "attribution (M-125) failed"

# 2. Unity player build
REP=1; [ "$REPRO" = 1 ] && REP=2
( cd "$PROJ" && "$UNITY" run "$PROJ" -- -executeMethod Game.Tools.Build.PlayerBuild.Run -buildOut "$OUT/b1" -buildRepeat "$REP" -buildTarget "$TARGET" $DEV -logFile "$OUT/logs/unity_build.log" ) > "$OUT/logs/unity_run.out" 2>&1
RC=$?; [ $RC -eq 0 ] || fail "Unity build failed (exit $RC), see $OUT/logs/unity_build.log"

# 3. per build: tile copy (hash-verified), manifest
DIRS=("$OUT/b1"); [ "$REPRO" = 1 ] && DIRS+=("$OUT/b1.repro2")
for D in "${DIRS[@]}"; do
  if [ "$TARGET" = win64 ]; then
    [ -f "$D/KaijuGame.exe" ] || fail "no player at $D/KaijuGame.exe"; APP="$D"; SA="$D/KaijuGame_Data/StreamingAssets/Geo/tiles/v1"
  else
    APP="$D/KaijuGame.app"; [ -d "$APP" ] || fail "no player at $APP"; SA="$APP/Contents/Resources/Data/StreamingAssets/Geo/tiles/v1"
  fi
  mkdir -p "$SA"
  "$PY" -I "$PROJ/Pipelines/build/copy_tiles.py" --src-root "$TILESRC" --tile-set "$TILESET" --dest "$SA" > "$OUT/logs/copy_tiles.$(basename "$D").log" 2>&1 || fail "tile copy failed for $D"
  "$PY" -I "$HERE/build_manifest.py" write --repo-root "$PROJ" --app-dir "$APP" --unity-json "$D.unity_build.json" --tile-set "$TILESET" \
      --attribution-dir "$OUT/attribution" --out "$D.manifest.json" || fail "manifest gate failed for $D"
done

# 4. smoke (the first build)
if [ "$SMOKE" = 1 ] && [ "$TARGET" = win64 ]; then echo "build.sh: smoke skipped: a Windows player cannot run on this Mac (tracked: Windows run untested)"
elif [ "$SMOKE" = 1 ]; then
  "$PY" -I "$HERE/smoke.py" --app "$OUT/b1/KaijuGame.app" --out-dir "$OUT/smoke" || fail "smoke test failed (see $OUT/smoke)"
fi

# 5. M-121 diff, then free the first build
if [ "$REPRO" = 1 ]; then
  "$PY" -I "$HERE/build_manifest.py" diff "$OUT/b1.manifest.json" "$OUT/b1.repro2.manifest.json" | tee "$OUT/logs/m121_diff.txt"
  [ "${PIPESTATUS[0]}" -eq 0 ] || fail "M-121 FAILED (manifests differ), builds kept in $OUT"
  rm -rf "$OUT/b1.repro2"; echo "build.sh: repro build deleted (manifest kept)"
fi
PL=KaijuGame.app; [ "$TARGET" = win64 ] && PL=KaijuGame.exe
echo "build.sh: OK $OUT/b1/$PL  manifest $OUT/b1.manifest.json"
