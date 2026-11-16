# T-194c evidence (capture draw-order / depth-tie fix)
Command per run (`run.sh <tag> <n> -captureStableOrder`): `unity run . ... -executeMethod Game.Tools.Capture.CaptureCli.Run -capturePaths docs/store/paths -captureScale 4,8 -captureMaxFrames 60 -captureEnable -captureStableOrder`; one batch Unity process at a time, 4 separate processes per attempt. Compare: `capture.py xproc --a <run1> --b <runN>`.
- Attempt 2 (queue 2001 for buildings + name-derived rendererPriority), runs A_1..A_4: A_1=A_2=A_4, A_3 differs (3 files, p4_d_dolly_v4 f33). FAIL (2 outcomes again; a parallel session saw the same, 7 flagged processes). `xproc/A_*`.
- Attempt 3 (same + buildings/landmarks lifted 0.02 m, `-captureLiftM`, default 0.02 under the flag; URP Lit has no polygon Offset): runs B_1..B_4 bit-identical (0 differing files of 1210 in all three comparisons). PASS. `xproc/B_*`, `hashes/B_*.hashes.txt.gz`.
- Capture PNGs are not committed (local `caps/`, ignored); hashes.sha256 of every run are in `hashes/`.
- Cost: lifted runs differ from unlifted ones in ~1.2k of 2.07M output pixels per frame on average (building bases), so the flag changes captures; the default is unchanged (flag off).
