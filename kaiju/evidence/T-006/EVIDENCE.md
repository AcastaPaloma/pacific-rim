# T-006 evidence: M0 style frame

- Task: T-006 (style frame, M0 checkpoint packet). Criteria: `expectations/T-006.md` C1-C19.
- Branch: `task/T-006`. Head commit when written: `02327df` (range `f4c2371..02327df`, 21 commits).
- Hardware: Apple M5 Pro (CPU and GPU, Metal), macOS 26.6.2, Unity 6000.6.9f1. Frame cost is relative only; M-008 unverified (not the target PC).
- Metrics tool: `Tools/Metrics/metrics.py` (kaiju_metrics 1.0.0, registry v1.1), run on captures made at `d10ec34`; hero stills and motion were re-captured after the warm-up fix `6deae3d`.

## Reproduction (from `kaiju/`)

```
# scene (writes build_log.json, shot_params.json, placeholder_props.json)
unity run . --no-tail --log-file <log> --timeout 900 -- -executeMethod Game.Tools.StyleFrame.StyleFrameScene.BuildCli
# scene + all stills (hero x5 profiles x2 resolutions, alternatives, controls)
unity run . --no-tail --log-file <log> --timeout 1800 -- -executeMethod Game.Tools.StyleFrame.StyleFrameCapture.CaptureAllCli
# motion (orbit, push-in, static; three anchoring modes)
unity run . --no-tail --log-file <log> --timeout 1800 -- -executeMethod Game.Tools.StyleFrame.StyleFrameCapture.CaptureAllCli -styleFrameSet motion
# timing (Unity binary directly; `unity run` adds -quit, which kills the self-ticking job)
/Applications/Unity/Hub/Editor/6000.6.9f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath <kaiju> -logFile <log> -executeMethod Game.Tools.StyleFrame.StyleFrameTiming.TimingCli
# metrics and checks: commands in evidence/T-006/M003_TABLES.md and M004_TABLES.md;
#   check scripts in evidence/T-006/tools/ (step4_checks.py, m004_per_sequence.py)
```

Captures go to `evidence/T-006/captures_local/` (git-ignored, about 115 MB, regenerable by the commands above).

## Criteria

Status key: PASS, PARTIAL (some of the criterion measured and met, the rest not), FAIL, NOT MEASURED, N/A.

| ID | Criterion | Status | Artifacts | Command | Commit |
|---|---|---|---|---|---|
| C1 | Giant machine, 40-60% height, named from 96 px | PARTIAL: visible mech 0.463 of frame height (0.497 incl. 5.1 m submerged), in range. Cold naming test by an evaluator still pending. | `checks/hero_thumb_test.json`, `hero_thumb_test.png`, `checks/mech_mask_internal.png` | `tools/step4_checks.py` | 0fee9bf |
| C2 | Recognisable San Francisco skyline | PARTIAL: Salesforce, Transamerica, Coit (off frame) geometry matches the data (see C9). Blind "which city" check with decoys pending for the evaluator. | `checks/skyline_overlay.png`, `boards/review_board.png` | n/a | 0fee9bf |
| C3 | Weight and scale | PARTIAL: mech stands on the real seabed, depth at mech 5.48 m (reported, not edited); 80.00 m height; 5 props at 1-3 internal px (analytic); ruler plate present. Figures 1-3 sit within 1.3 L* of their surround (needed >= 10), boat 7.0 L* (needed >= 10); only the car passes (24.0 L*). Hero warning: boat footprint outside 1-3 px. Waterline-between-ankle-and-mid-shin and the waterline close-up not checked. | `build_log.json`, `placeholder_props.json`, `checks/scale_cues.json`, `checks/scale_cues_plate.png` | `StyleFrameScene.BuildCli`; `tools/step4_checks.py` | 57f7def, 0fee9bf |
| C4 | Haze and value | PARTIAL: M-003c1 on hero slate_teal passes (drop 0.00 L*, rho 1.00, span 20.34 >= 15 L*) at 1080p and 1440p. Mech vs buildings/terrain/sky/median L* NOT MEASURABLE (mech mask empty in the sidecar, no class mask). Darkest-5% share not reported. c2 N/A for a single still. Sky share 0.364 in hero (in range). | `m003/hero_1080p/`, `m003/hero_1440p/`, `M003_TABLES.md` | `metrics.py m003` (see M003_TABLES.md) | 937ae4c, e5a69aa |
| C5 | Pixels and dither | PASS: d-iv 100% at s=4 and s=5; DIG 0.351 (hero slate_teal) on sky and lit surface; dither_off control DIG 0.016. Visual check of flat regions and contours not recorded. | `m003/hero_*/`, `m003/ctl_dither_off/`, `M003_TABLES.md` | `metrics.py m003` | 937ae4c, e5a69aa |
| C6 | Palette discipline | FAIL: A2 (1.000 int/out), value levels (6) and hue families (1) pass; no accent present, so d-iii is N/A and "one accent 0.1-15%" is unmet. Unquantised control rejected by the tool (INVALID_INPUT), so it does not fail on a metric; bilinear control fails A2 output and d-iv. | `m003/hero_1080p/`, `m003/ctl_unquantised/`, `m003/ctl_bilinear/` | `metrics.py m003` | 937ae4c |
| C7 | Original | PARTIAL: placeholder props are plain boxes listed in `placeholder_props.json`; mech is the `MechFrameBalanced` prefab. No text-in-image check and no banned-terms file-name check on file. | `placeholder_props.json`, `build_log.json` | n/a | d2bca52 |
| C8 | Nothing moved, scaled, restyled | PARTIAL: terrain, seabed, building, water mesh hashes and heightfield hash recorded; baked-file hashes equal the manifest; no non-unit scales; mech scale 1 at 80.00 m; water surface y = sea level. No module check compares mesh hashes with baked-derived expected values (module request). Rest pose and "static" not separately evidenced. | `build_log.json` (`hashes`, `non_unit_scales`) | `StyleFrameScene.BuildCli` | 57f7def |
| C9 | Geography honest | PARTIAL: skyline IoU 0.991 above the horizon (>= 0.98, passes; 0.946 with building parents kept). Landmarks: Salesforce 2.2 px (pass), Transamerica 6.2 px (> 3, FAIL; flat prisms, no pyramid roof model), Coit Tower off frame. A 3 px tolerance is below one internal pixel (4 output px). M-005 tile values not quoted. | `checks/skyline.json`, `checks/skyline_overlay.png` | `tools/step4_checks.py` | 6a4b013, 0fee9bf |
| C10 | Reproducible | PASS: two full stills runs bit-identical, 100/100 files, after the warm-up fix 6deae3d (the first run without warm-up differed). `shot_params.json` holds pose, FOV, mech position, depth, profile, flags, palette SHA; its commit field is "placeholder" by design (commit is in every sidecar). Scene built by Editor script; no other module edited. Committed captures within 120 MB (full set git-ignored). | `checks/two_run_hashes.json`, `shot_params.json`, `build_log.json` | the three `unity run` commands above | 6deae3d |
| C11 | Five-profile strip | FAIL: silhouettes identical by construction, but c1 fails in 4 of 5 profiles at both resolutions (lavender_peach, slate_crimson, warm_amber_haze, warm_rust_sepia); only slate_teal passes. | `m003/hero_1080p/`, `m003/hero_1440p/`, `boards/review_board.png` | `metrics.py m003` | e5a69aa |
| C12 | Alternative framings | FAIL: waterfront fails c1 (rho 0.80) and sky share 0.53 (> 0.50), and needed a relaxed site search. Closer and wider pass c1. C8-C9 not run on the alternatives. | `m003/alt_1080p/`, `m003/alt_1440p/`, `shot_params.json` | `metrics.py m003` | 937ae4c |
| C13 | Review board | PASS: board plus a shuffled copy and a key. Text and caption cropping not independently verified. | `boards/review_board.png`, `boards/review_board_shuffled.png`, `boards/board_key.json` | `tools/step4_checks.py` | 0fee9bf |
| C14 | Stability clips (M-004) | PARTIAL: orbit, push-in and static sets captured in direction, hybrid and screen modes; static frame exactly stable in all three after re-capture with warm-up. Per-sequence values: push-in misses crawl C_px.T12 in direction (0.164 vs 0.15); hybrid misses flicker F.T12 (0.0080 vs 0.005); the orbit misses all three yaw thresholds in every mode (0.61 px/frame, 2.4x the Y1 nominal). Full `metrics.py m004` gate INCOMPLETE (exit 3) by design: one profile, one viewpoint, wrong sequence names. | `m004/`, `M004_TABLES.md`, `tools/m004_per_sequence.py` | `... -styleFrameSet motion`; `tools/m004_per_sequence.py` | 17ce5cf, 5051a26 |
| C15 | Frame cost | PARTIAL (relative only; M-008 unverified): pipeline_full CPU render p50/p95 0.54/0.80 ms at 1080p and 0.52/0.69 ms at 1440p; 119 draw calls; 0 GC.Alloc samples and 0 collections after warm-up. "render" is CPU submission plus a 1-px readback, not GPU pass time. The "(internal)" column in timing.md is read one row late (known defect). | `timing/timing.json`, `timing/timing.md` | Unity binary command above | 8851fa3 |
| C16 | Index first | PARTIAL: `README.md` opens with a status line, hero image and 7 numbered decisions with defaults. Ten-minute founder usability not tested. | `checkpoints/M0/README.md`, `checkpoints/M0/hero.png` | n/a | d234161, 02327df |
| C17 | Coverage and honesty | PARTIAL: M-003, M-004, M-005 and WALK sets present with captions; `spread.md` notes M-003 has 2 hero slots marked TODO at build time; fewer-than-10 cases and unmerged T-005 walk strips are tagged. Per-capture branch/commit/command live in `answer_key.json`. Count per metric and the walk-strip labelling not re-audited here. | `checkpoints/M0/captions.md`, `answer_key.json`, `M003/`, `M004/`, `M005/`, `WALK/` | `checkpoints/M0/make_ratings.py` | 58e473a, d234161 |
| C18 | Blind and spread | PARTIAL: blind IDs with an answer-key file; spread per set reported (M-003 Spearman 0.929 n=10; M-004 0.972 n=10). The M-003 order partly rests on author judgement, and palette share cannot rank dither_off, haze_off or reversed_haze. | `checkpoints/M0/spread.md`, `answer_key.json` | `tools/dry_run_and_spread.py` | 58e473a |
| C19 | Rating sheets and thresholds path | PARTIAL: sheets for M-001, M-003, M-004, M-005 and `GUIDE.md` exist; R-002 bands quoted in the M-003 sheet. Dry run of `calibrate`: M-001, M-003, M-004 exit 0; M-005 exit 2 because `ratings.py` supports only M-001..M-004, so M-005 cannot be calibrated. The dry run ran in this worktree, not a fresh clone, on copied set-level reports. | `checkpoints/M0/sheets/`, `GUIDE.md`, `dryrun/` | `Tools/Metrics/metrics.py calibrate` (see `dryrun/README.md`) | 4134025, 58e473a |

Count: PASS 3 (C5, C10, C13), FAIL 3 (C6, C11, C12), PARTIAL 13. No criterion is NOT MEASURED or N/A in full; parts that are (mech L*, c2, M-005) are noted in the rows.

## Known gaps and module requests

- Pending human steps: cold naming of the mech from the 96 px thumb (C1), blind "which city" check (C2), the founder ratings (C19), M-003.f panel.
- Mech-vs-surround L* needs a mech or class mask in the capture sidecar (mask_animated is empty). Accent (C6) is absent from the frame; no accent was added.
- Scale cues: figures and boat do not reach 10 L* from their surround; boat footprint falls outside 1-3 px in the hero.
- Transamerica landmark at 6.2 px: flat prisms only, pyramid and skillion roofs not modelled; a 3 px tolerance is below one internal pixel.
- Four of five profiles need haze tuning in this scene; the waterfront framing needs a different site and lower sky share.
- M-004: the full gate is INCOMPLETE (T-004 holds it); orbit runs at 2.4x the nominal yaw rate, outside the class tolerance; no mode meets the yaw thresholds (Q-060).
- Module requests: World.Geo (seabed or seabed-hash API, an "unmodified tile" check, a public depth-at-point helper); StyleCapture (public `AddCameras`, `OnGrid`; a scene path and pose argument on `RunPassTiming` so a `timing` set can be offered); Metrics (`ratings.py` accept M-005; a relaxed single-frame mode for m003; sequence naming for m004).
- Open questions: whether `StyleFrame.unity` (tens of MB, embeds tile data) stays git-ignored (it is, and is rebuilt by the CLI); mech yaw 270 means the hero sees its back.
- Volumes: committed evidence is small (`m003` 484 KB, `m004` 8.7 MB). `captures_local/` (about 115 MB) and the generated scene are git-ignored and regenerable.
- Uncommitted at time of writing: untracked `StyleFrameTiming.cs.meta` (not part of this commit). Unity test results are not included in this file.
