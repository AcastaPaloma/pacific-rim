# M3 render / capture / store: combined evaluator (eval-final)
Scope: T-193, T-194..T-194d, T-195, T-203/T-205, T-206, T-231 as merged on the integration branch (HEAD 95ac2b4). Read-only, no Unity, no audio. Judged from evidence files, diffs and my own re-runs (§6, §13 light gate).

## Re-runs I did myself
- `pytest Pipelines/capture`: 75 passed. `pytest Tools/Build`: 14 passed.
- T-194d/T-194c cross-process: caps are not on disk (the .wt/T-194d worktree is gone, PNGs were never committed), so `capture.py xproc` could not run. Instead I compared the committed hash manifests: C_1 vs C_2/C_3/C_4 (T-194d, no sink flag): 1210 PNG hashes each, 0 differ (only capture_run.json / capture_log_summary.txt differ: run metadata). B_1 vs B_2..B_4 (T-194c, lift flag): 0 PNG diffs. A_1 vs A_3 (queue-tier attempt): 3 PNGs differ, matching the recorded failure. Claims in EVIDENCE.md hold.
- M-124 on the CRF 3 master: sha256 matches the notes (5886e2eb...); ffprobe 1920x1080 yuv444p bt709 30 fps 3000 frames. `fidelity.frame_fidelity` vs `/tmp/kaiju-build/t203/rec_a` PNGs on 8 frames (0, 333, 720, 1210, 1805, 2250, 2650, 2999): share dE00<=2 min 0.9970, max 1.0; p99.9 worst 2.80. Consistent with the recorded 67-frame result (min 0.9942, gate 0.99). Gate value unchanged.
- Threshold audit: `git diff 786f800^..HEAD` over `Tools/Metrics/registry`, `Pipelines/capture`, `docs/store/encode_settings.json`, `Assets/Game/Config/Rendering`: no registry file for M-003/M-004/M-122/M-123/M-124/C7/C11 touched, no gate number removed or lowered in capture_tools. The only value changes are look tuning (`PaletteProfileTuning.json`: lavender ditherSpread 0.25, amber 0.4, both added) plus regenerated profile assets. The T-205 fix lowered CRF 6 -> 3 (stricter), not the gate. The SAFE_MARGIN/ratio additions seen in the diff are new checks (T-195 safe zone, M-008) not relaxations.

## Verdicts
| Task | Verdict | Reasons |
|---|---|---|
| T-193 look fix | PASS-with-notes | Numbers in EVIDENCE.md are self-consistent and the change is dither-only, no threshold touched (M-004 Y 0.01008 -> 0.00877, amber DIG 0.54 -> 0.417). Notes: cost is real, M-004 T12/T3 margin fell to ~0.02 (limit 0.15 passes at 0.127); amber c1 span margin only 1.27 (16.27 vs 15); I could not re-run Unity captures (frames not committed), so the numbers are accepted from the evidence, not reproduced. |
| T-194/194b | PASS-with-notes | Diagnosis plus guard (`capture.py xproc`) and tests pass; both were stepping stones, not fixes. |
| T-194c/194d determinism | PASS-with-notes | Hash manifests verify 4 separate processes bit-identical (1210/1210 PNGs, 6 sequences). Note: determinism is bought with a 0.02 m building lift (flag) and now a 0.02 m terrain sink default in the capture scene, so captures differ from pre-sink ones at building bases (~1.2k of 2.07M px/frame); the sink is a workaround for an exact depth tie, not a root-cause fix. Only 4 processes on one machine; no cross-machine claim. |
| T-195 store assets | PASS-with-notes | All 12 asset files carry status TEMPORARY in manifest.json (title KAIJU, logotype, K icon, 8 screenshots from tile test scenes and HUD composites); placeholders are flagged in manifest, progress and disclosure draft. Not shippable as is. P4-b / P4-e paths are proposed only, not run. Trailer rows not built. |
| T-203/T-205 demo + M-124 | PASS-with-notes | M-124 passes at CRF 3 (re-verified above). The 100 s clip is honest in its notes: capsule kaiju stand-in, mech placeholder, no fall test beyond one seed. Pixel grid is 0.9945 min vs the 0.999 check, so not a strict integer grid (noted by the implementer, not hidden). The CRF 3 master (56.7 MB) is gitignored and exists only locally at Builds/demo; the git copy (CRF 6) fails M-124 on worst frames. `frame_checks_crf3.json` samples only 3 grid frames (notes cite 100 from the CRF 6 run). Hardware decode of 4:4:4 unverified (open founder row). |
| T-206 destruction cam | PASS-with-notes (weak) | Flagged, default off, additive. Evidence is one 26 s run, contact sheet only; the sheet shows a nearly flat brownish field with one building slab, a small amber sphere in 1 of 6 frames and little contrast: destruction is not readable. Its own notes admit framing is "wide and low-contrast", the full 100 s run, grid check on cut frames and EditMode were not done. The demo master (T-203) predates this camera, so the shipped clip still has no destruction cut. |
| T-231 Windows build | PASS-with-notes | Build, manifest and M-125 attribution (94/94) claimed; I could not re-run (no Unity). Windows run is untested and flagged TRACKED; no smoke by design. Build proven, run unverified. |

## Eval-style pass (judged by looking, honest)
Looked at: demo contact sheet (20 frames), T-206 sheet, hud_v4_street.png. Not looked at (limits): the remaining 7 store screenshots, capsules, and the mp4 itself in motion (no playback), so temporal dither shimmer and coherence in motion are NOT judged.
- Demo contact sheet: strongest. Coherent warm amber haze palette, banded sky with dither, layered skyline reads as depth, mech and kaiju are clearly placeholder blocks/capsule but sit inside the palette. Consistent pixel scale. Style coherent. The dark capsule kaiju is flat and reads as a cut-out; the ground plane is large, flat and muddy, and the mech is cropped by buildings in several frames.
- hud_v4_street.png: palette and dither are coherent (slate-blue haze, clean skyline silhouette). But it carries the debug HUD text "Kaiju feel check (TEMPORARY: capsule mech and kaiju...)" in the top-left and is mostly an empty teal ground half; as a store screenshot it is not presentable, and the name "street" does not match (waterfront skyline). Acceptable only as TEMPORARY.
- T-206 sheet: low quality as a showcase, murky single-hue, difficult to tell what is shown; coherent as pixels but not a legible shot.
Overall: style coherence of the renderer is good (one consistent pixel grid, palette-limited, dither used deliberately); content quality (art, framing, destruction legibility) is placeholder level.

## Open gates (not hidden)
1. M-003.d-iv (0.61-0.73 vs 0.995) vs C11 on layered translating frames: still open, documented in `research/R-M3-DIV.md` and T-193 EVIDENCE (p4_d d-iv 0.6729 under M3LayeredFarLift is the one failing row). Awaiting an independent reviewer; I did not resolve it. Any d-iv@v2 change is a proposal only.
2. M-122 trailer rows and real founder art: not built, store assets TEMPORARY.
3. M-124 master is only local (gitignored 56.7 MB); 4:4:4 playback compatibility unverified; yuv420p cannot pass M-124.
4. Pixel-grid 0.999 check not met in the demo (0.9945 min): about 0.4 % mixed blocks from the present shift.
5. T-206 full-run verification and a destruction cut in the demo clip; Windows runtime check; M-123 not run on the demo (no sidecars).
6. Unity-side recaptures for T-193/T-194 are not reproducible from committed files (frames gitignored); numbers rest on the evidence logs.

## Blocking defects
None found. Minor: items 3 to 6 above go to LEDGER for a later batch.
