#!/bin/bash
# T-004: runs every metric and check on the committed and local capture sets and stores the outputs in
# evidence/T-004/metrics. Usage (from the project root):
#   Tools/Rendering/Pixel/run_evidence.sh <path to T-002 Tools/Metrics> [rerun_dir [rerun_dir_2]]
# rerun_dir holds a second run of haze_cal, cut_check, c11_subpixel_on and stages_ab; rerun_dir_2 one of m003_stills and the controls.
# The T-002 tools are used read-only from their own checkout; their commit is recorded in metrics/tool_versions.txt.
set -u
T2="${1:?path to the T-002 Tools/Metrics directory}"; RERUN="${2:-}"; RERUN2="${3:-}"
PY=/opt/miniconda3/envs/kaiju/bin/python
E=evidence/T-004; M=$E/metrics;
# The full capture output lives in the git-ignored folder captures_local (written by the capture tool); the committed
# subset in captures/ is a copy of part of it. Metrics run on the full output when it is there.
C=${CAPTURES:-$E/captures_local}; [ -d "$C/m003_stills" ] || C=$E/captures
 PAL=research/R-002/palettes.json; CH=Tools/Rendering/Pixel/pixel_checks.py; SH=Tools/Rendering/Pixel/pixel_sheets.py
rm -rf $M/validate $M/m003* $M/m004* $M/controls
mkdir -p $M/validate $M/m004ab $M/controls $M/checks $E/sheets
{ echo "T-002 metric tools: $(git -C "$T2" rev-parse HEAD) ($(git -C "$T2" log -1 --format=%s | cut -c1-80))"; $PY "$T2/metrics.py" --version; echo "T-004 checks: $(git rev-parse HEAD)"; $PY --version; } > $M/tool_versions.txt 2>&1
profiles="warm_rust_sepia warm_amber_haze slate_teal lavender_peach slate_crimson"

# 1. Contract validation of every capture set (style_capture@v1, T-002 validator).
echo "capture root: $C" >> $M/tool_versions.txt
for d in $C/*/; do n=$(basename $d); $PY "$T2/metrics.py" validate style_capture $d --palettes $PAL > $M/validate/$n.txt 2>&1; echo "$n: $(head -1 $M/validate/$n.txt | cut -c1-90)"; done > $M/validate/summary.txt

# 2. M-003@v1 on the stills (gate), the detail views, 1440p and 4K, and on each negative control.
$PY "$T2/metrics.py" m003 $C/m003_stills $C/haze_cal --palettes $PAL --out $M/m003 > $M/m003.txt 2>&1
$PY "$T2/metrics.py" m003 $C/m003_details $C/haze_cal --palettes $PAL --out $M/m003_details --no-images > $M/m003_details.txt 2>&1
$PY "$T2/metrics.py" m003 $C/m003_stills_1440p $C/haze_cal --palettes $PAL --out $M/m003_1440p --no-images > $M/m003_1440p.txt 2>&1
$PY "$T2/metrics.py" m003 $C/m003_stills_2160p $C/haze_cal --palettes $PAL --profiles slate_teal --out $M/m003_2160p --no-images > $M/m003_2160p.txt 2>&1
for c in unquantised bilinear reversed_haze dither_off haze_off subpixel_offset; do
  $PY "$T2/metrics.py" m003 $C/control_$c $C/haze_cal --palettes $PAL --profiles slate_teal --out $M/controls/$c --no-images > $M/controls/$c.txt 2>&1
done

# 3. M-004@v1: the five profiles x two viewpoints (hybrid anchoring, the configured default), then every
#    anchoring mode and stabiliser variant on slate_teal / eye70, and the per-frame-noise control.
$PY "$T2/metrics.py" m004 $C/m004_warm_rust_sepia $C/m004_warm_amber_haze $C/m004_slate_teal $C/m004_lavender_peach $C/m004_slate_crimson --palettes $PAL --out $M/m004 --jobs 6 > $M/m004.txt 2>&1
for d in $C/m004ab_*/; do n=$(basename $d); n=${n#m004ab_}
  $PY "$T2/metrics.py" m004 $d --palettes $PAL --profiles slate_teal --viewpoints eye70 --out $M/m004ab/$n --no-images --jobs 6 > $M/m004ab/$n.txt 2>&1
done
$PY "$T2/metrics.py" m004 $C/dolly_slate_teal --palettes $PAL --profiles slate_teal --viewpoints eye70 --out $M/m004_dolly --no-images > $M/m004_dolly.txt 2>&1

# 4. Checks the metric tools do not cover.
$PY $CH cut $C/cut_check --out $M/checks/c13_cut.json
$PY $CH smooth $C/c11_subpixel_on/eye70_tower_T1 $C/c11_subpixel_off/eye70_tower_T1 --out $M/checks/c11_smooth_T1.json
$PY $CH smooth $C/c11_subpixel_on/eye70_tower_Y1 $C/c11_subpixel_off/eye70_tower_Y1 --out $M/checks/c11_smooth_Y1.json
$PY $CH grid $C/control_subpixel_offset/slate_teal_eye70 --out $M/checks/c2_grid_declared_offset.json
$PY $CH grid $C/control_bilinear/slate_teal_eye70 --out $M/checks/c2_grid_bilinear_control.json
$PY $CH depth $C/m003_stills/slate_teal_eye70 --out $M/checks/c19_depth.json
for p in $profiles; do
  $PY $CH scale $C/m003_details/${p}_detail_scale --out $M/checks/c7_scale_$p.json
  $PY $CH layers $C/m003_stills/${p}_eye70 --out $M/checks/c6_layers_$p.json
  $PY $CH blocks $C/m003_stills/${p}_eye70 --out $M/checks/c7_blocks_$p.json
done
[ -n "$RERUN" ] && for s in haze_cal cut_check c11_subpixel_on stages_ab; do $PY $CH identical $C/$s $RERUN/$s --out $M/checks/c19_identical_$s.json; done
[ -n "$RERUN2" ] && for s in m003_stills control_unquantised control_reversed_haze; do $PY $CH identical $C/$s $RERUN2/$s --out $M/checks/c19_identical_$s.json; done

# 5. Sheets: stage A/B strip, toggle combinations, swatches and depth ramps, sequence contact sheets, panel board.
S=$C/stages_ab
$PY $SH strip $E/sheets/stage_ab_strip.png "M0: all stages on=$S/m0_all_on" "low-res OFF (native, shown reduced)=$S/off_lowres" "dither OFF=$S/off_dither" "haze OFF=$S/off_haze" "quantise OFF=$S/off_quantise" "supersample ON=$S/on_supersample" "outline ON=$S/on_outline" --cols 4
args=(); for d in $S/combo_*; do n=$(basename $d); args+=("${n#combo_}=$d"); done
$PY $SH strip $E/sheets/stage_combinations.png "${args[@]}" --cols 4
$PY $SH swatches $E/sheets/palette_swatches_depth_ramps.png $PAL $C/m003_stills
for q in S0 T1 T2 T3 Y1 Y2; do $PY $SH contact $E/sheets/m004_slate_teal_eye70_$q.png $C/m004_slate_teal/eye70_$q --every 15 --cols 6; done
$PY $SH contact $E/sheets/orbit_slate_teal.png $C/orbit_slate_teal/orbit_20s --every 40 --cols 6
args=(); for p in $profiles; do args+=("$p eye70=$C/m003_stills/${p}_eye70/f000005_color_output.png" "$p street=$C/m003_stills/${p}_street/f000004_color_output.png"); done
$PY $SH board $E/sheets/panel_board.png art/style_refs "${args[@]}" "NEGATIVE anchor: unquantised=$C/control_unquantised/slate_teal_eye70/f000005_color_output.png" "NEGATIVE anchor: bilinear upscale=$C/control_bilinear/slate_teal_eye70/f000005_color_output.png" --cols 4
echo done
