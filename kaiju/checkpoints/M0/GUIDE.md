# M0 checkpoint rating guide (one page)

**What you have.** `M003/` ten stills + `M003/anchors/` four anchor stills (style); `M004/` ten 60-frame GIFs (shimmer while the camera moves; lossless frames are in the git-ignored `bulk/`); `M005/` ten views and plates of the San Francisco tile; `WALK/` ten mech contact sheets, **UNMERGED, NOT AT TARGET PACE** (branch task/T-005, 3 m/s against the 9 m/s gate). `captions.md` says what each ID is. Two M-003 slots (M003-11, M003-12) are reserved for the hero frames and not yet added.

**How to rate.** Open only the images, `captions.md` and your sheet. Do not open `answer_key.json`, `spread.md` or `bulk/` until every sheet is done. Judge each capture alone, at 1:1 or integer zoom, no resizing. Score 1 to 10 (10 = clearly good, 1 = clearly bad; decimals allowed) in your column of `sheets/<metric>_sheet.csv`; use the notes column for what you saw. M-003: score the pixel-art style (the R-002 section 4 f bands are in `sheets/M-003_rubric.md`; the anchors M003-A1..A4 are scored like any other frame). M-004: watch the GIF at full size for 10 s; score how still dither and edges stay (shimmer, crawling, boiling counts against). M-005: score how recognisably and correctly this is San Francisco at 1:1 (towers taller than the mech, skyline, shore). Walk strips: score how a 10,000 t mech should look and move; rate what you see, not the tag. No capture may be skipped: a blank is read as not rated.

**Run calibrate** (from the project root, kaiju env; after all sheets are filled):
```
PY=/opt/miniconda3/envs/kaiju/bin/python
$PY checkpoints/M0/make_ratings.py checkpoints/M0/sheets/M-004_sheet.csv --metric M-004 --out r4.json --key checkpoints/M0/answer_key.json
$PY Tools/Metrics/metrics.py m004 <capture sets> --out out/m004      # one report per capture, report label = capture ID
$PY Tools/Metrics/metrics.py calibrate --ratings r4.json --reports out/m004 --out out/cal4
```
`calibrate` needs at least 8 rated captures with a report each, and refuses if a negative anchor scores 6 or more. It prints a proposed registry diff and never writes `registry/`. The M0 dry run on dummy sheets is in `dryrun/` (M-003, M-004, M-001 succeed, exit 0). **M-005 cannot be calibrated by this tool**: `ratings.py` accepts only M-001 to M-004, so the M-005 sheet is rated and kept for the record but gives no threshold proposal until the tool is extended (dry run `dryrun/calibrate_M-005.stdout.txt`, exit 2).

**Rules that bind.** (1) A threshold changes only on the founders' sign-off, recorded as a calibration entry that names the ratings file, the proposed diff and the date; `calibrate` output alone changes nothing, and a new registry revision is a new file, never an edit. (2) A metric whose ratings do not track its number (weak or negative rank correlation between median rating and the metric value, or founders disagreeing with each other) is flagged for review and its threshold is left as it is. (3) Ratings are not edited after the key is opened.
