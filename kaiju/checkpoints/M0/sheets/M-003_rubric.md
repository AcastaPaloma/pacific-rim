# M-003 f rating rubric (what the founders score)
Source: `research/R-002.md` section 4, sub-measure f (panel rating), as read by `Tools/Metrics/kaiju_metrics/ratings.py` (`M-003 f (R-002 section 4 f)`).
**The band wording of R-002 section 4 f is NOT quoted here**: the packet builder was not permitted to read `research/R-002.md`. Paste the bands under this line before the sheet goes out (open item, T-006 C19).

Scale the tool reads: one integer or decimal score 1..10 per capture per rater, 10 = clearly good pixel-art style, 1 = clearly bad.
Anchors (M003-A1..A4: two clearly good, two clearly bad, shown blind): a rater is excluded if a negative anchor scores 6 or more, or positive anchors are not rated above negative ones (`calibrate`, `ratings.py`). Expect good anchors 9-10, bad anchors 1-3.
Pass line used by `calibrate`: median rating >= 8.0 counts as good.
