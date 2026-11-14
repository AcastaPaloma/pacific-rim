# M-003 f rating rubric (what the founders score)
Source: `research/R-002.md` section 4, sub-measure f (panel rating), as read by `Tools/Metrics/kaiju_metrics/ratings.py` (`M-003 f (R-002 section 4 f)`).
**R-002 §4 f, quoted verbatim** (pasted by the orchestrator):

> ### (f) Blind panel rubric, style match 1–10
> - **Protocol** (ITU-T P.910 ACR-style single-stimulus [11], adapted):
>   - Each evaluator (≥ 8: founders plus blinded agent evaluators; P.910 lab tests use more, so the result is labelled "small panel") sees one capture beside a fixed reference board of the 11 panels.
>   - Order is random; build and source are hidden.
>   - Each set includes 2 hidden positive anchors (crops of reference panels; expected ≥ 9) and 2 negative anchors (unquantised render of the same view; expected ≤ 3). A rater who scores the negative anchors ≥ 6 is excluded.
> - **Pass:** panel mean ≥ 8.0 over ≥ 12 captures, and no profile mean < 7.
> - **Score bands:**
>   - **1–2:** Reads as a standard 3D render. Smooth gradients, unlimited colours, no pixel grid.
>   - **3–4:** Pixelated or posterised, but hues are wrong for the profile, there is no haze ramp, and near and far have equal contrast.
>   - **5–6:** Correct palette family and visible grid, but dithering is missing or mechanical everywhere, flat banding in sky and smoke, weak depth layering, silhouettes muddy.
>   - **7:** Palette, grid and haze correct. One clear miss: dither texture, cloud or smoke treatment, accent overuse, or composition (high horizon, no scale figures).
>   - **8:** Could sit on the board as a lesser panel: 2–3 hues plus accent, posterised value bands, dithered or clustered transitions, near-dark to far-light layering, readable silhouette.
>   - **9:** Indistinguishable in style from the stronger panels. Clustered pixel clouds, light shafts or haze where fitting, tiny scale cues.
>   - **10:** Would be picked as a reference panel itself.

Scale the tool reads: one integer or decimal score 1..10 per capture per rater, 10 = clearly good pixel-art style, 1 = clearly bad.
Anchors (M003-A1..A4: two clearly good, two clearly bad, shown blind): a rater is excluded if a negative anchor scores 6 or more, or positive anchors are not rated above negative ones (`calibrate`, `ratings.py`). Expect good anchors 9-10, bad anchors 1-3.
Pass line used by `calibrate`: median rating >= 8.0 counts as good.
