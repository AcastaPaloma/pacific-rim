# R-M3-DIV-review: independent review of the d-iv vs C11 conflict (R-M3-DIV, T-197)

Reviewer note, 2026-11-16. Inputs read: `research/R-M3-DIV.md`, `research/R-002.md` (d-iv, section d), `research/R-131.md` section 4 (C11 mechanism), `expectations/T-004.md` (C11 text), `evidence/T-192`, `T-193`, `T-196` (EVIDENCE.md, C11 tables, m003_moving.md). No Unity, no audio. **No gate, threshold or metric was changed.**

## 1. Verdict in brief
1. The conflict is real and geometric, not a rendering bug. Both gates cannot pass on layered translating frames while d-iv reads one lattice offset and C11 stays 1 px at k = 4.
2. The two gates guard different things and are not symmetric. C11 guards **truthful motion** (displacement within 1 output px of commanded). d-iv guards **crisp, unresampled pixels** (R-002: nearest 1.000, bilinear 0.226, undeclared sub-pixel shift 0.242). The per-slab render (T-192, flag off) violates neither purpose. The shared-phase render buys d-iv by giving up C11's purpose.
3. **Recommendation: P-1 (per-slab d-iv@v2) with three hardening conditions (section 5), plus P-2 as an advisory report; confirm with P-3 (2AFC) before P-2 becomes a gate. Keep C11 at 1 px. Do not do P-5 now. P-4 is harmless but unnecessary.**

## 2. Is d-iv, as read on layered translating clips, measuring something the player sees?
- d-iv@v1 asks "does every s x s block, aligned to one declared (ox, oy), hold one colour". On a layered frame the near, main and far slabs legitimately carry different integer output shifts (T-182: up to 3-4 px apart). A slab on another phase fails every textured block, so 0.61-0.73 says "slabs are on different lattices", not "pixels are resampled".
- What the player sees in each slab is still a sharp nearest-neighbour grid of identical 4x4 pixels (every slab is rasterised at internal resolution then integer-upscaled). What differs is **register** at silhouette edges between slabs: a near silhouette over a far wall can be 0-3 px off the far texel grid. That is a visible property only at depth seams, and it is exactly what real parallax between independently moving layers produces. Under a shared lattice the slabs instead move in whole 4-px steps relative to each other, which is a visible stepped parallax of up to k/4 + 0.5 = 1.5 px error against the commanded motion (T-196 measured 1.49, 1.49, 1.48).
- Quantification: I could not reproduce 0.61-0.73 or measure per-slab uniformity on the layered clips. `evidence/T-192` and `T-196` keep only tables and summaries (no frames). The only frames on disk (a sibling session's scratch `cap_A0..A3/p4_d_dolly_v4`, commit 03983bc) have `layerSnap: false`, `layerSharedPhase: false`: a single lattice. On them I recomputed d-iv with the sidecar offset ((0,0) at f10, (0,3) at f40): **1.000**, and the best offset in each of three row bands equals the sidecar offset (all 1.000). That confirms the single-lattice path and the per-frame offset behaviour, and nothing about layered frames. R-M3-DIV 5.2 (synthetic 0.44-0.50 single vs 1.000 per region) is mechanism only, and its content model is assumed.
- Judgement from definitions: reading d-iv on layered translating frames with one offset does **not** measure a defect the player would see. It measures a lattice-sharing assumption that nobody has shown to matter perceptually (R-M3-DIV 5.3 says the same; the repo has no moving-image evidence for either side).

## 3. Is C11's floor k/4 + 0.5 inherent?
- Inherent **to the shared-lattice premise**, yes. Argument (checked by hand, matches `research/R-M3-DIV/lattice_bound.py` as described): with one lattice every slab edge sits at p + k m (common integer phase p, integer m). Two slabs whose ideal output shifts differ by k/2 (mod k) have errors summing to k/2 for every p, so max >= k/4, and integer p adds up to 0.5. k = 4 gives 1.5, which the three measured values (1.48-1.49) hit within raster noise. Rendering technique cannot change it: the bound uses only "one lattice" and "integer pixel positions".
- It is **not inherent to C11 itself**: per-slab integer shifts measure 0.96, 0.96, 0.95 (T-187, T-192, T-193 lines in `plans/M3_PROGRESS.md`) against the 0.5 quantisation bound (0.96 includes raster effects, which R-131 4.3 explains).
- Caveat I did not resolve: the 3-slab bound for k = 4 is stated as 1.5 in R-M3-DIV's table but its search covers only two or three slabs of its own model; real depth is continuous (R-131: about 5 px for non-slab depths) and C11 is only claimed at the three class depths (Q-260). So "C11 passes per-slab" is a statement about slab reference depths, not about all depths.

## 4. Options judged (never merely lowering a threshold)
| Option | Keeps d-iv intent | Keeps C11 intent | Verdict |
|---|---|---|---|
| Shared phase (flag on) | yes | no (1.49 > 1, motion wrong by design) | reject as default; keep flag off |
| Per-slab shifts + d-iv as read | no (false failure) | yes | fails a gate that mis-measures; needs P-1 |
| P-1 per-slab d-iv@v2 | yes, within each slab (resampling, undeclared shift) | yes | **adopt, with hardening** |
| P-2 register report | adds the property v1 protected by accident | n/a | adopt as advisory first |
| P-3 forced choice | settles the open perceptual fact | n/a | do it before P-2 gates |
| P-4 C11 in texels | n/a | cosmetic at k = 4 | optional, no effect |
| P-5 k = 2 | yes | bound exactly 1.0, zero margin (fragile; measured at the bound within 0.01) | not now; 4x pixel cost and recalibration of R-002/M-003 |
| Mask slabs, follow tracked edge (R-M3-DIV D, H) | no (gaming) | no (hides error) | reject, agree |
| Another option: k = 3 | n/a | bound 1.25 for two slabs, fails | no |
Why not "lower a threshold": P-1 changes the **unit of measurement** (per slab region, same 0.995, same 1-code-value uniformity) for a frame type whose structure v1 never modelled (R-002 validated only single-lattice synthetic frames). Every v1 failure mode still fails under P-1 (section 5 checks it).

## 5. Required hardening for P-1 (otherwise it is weaker than v1)
1. **Do not trust the declared per-slab offsets.** R-002's own table shows an undeclared sub-pixel shift fails but a *declared* one passes, so a declared-offset test is only as strong as the declaration. Add an independent estimate: per slab region, the best (ox, oy) in 0..k-1 by uniformity must equal the declared offset (mod k), with the best-versus-second-best margin reported. This catches bilinear or fractional-shift output inside a slab (best uniformity then collapses for every offset, as in the 0.226/0.242 rows).
2. **Seam exclusion must be capped.** Blocks straddling slab boundaries are excluded; require the evaluated area to be >= 95 % of non-UI pixels per frame (number to be set by measurement, not chosen to pass). Otherwise a renderer could add seams to dodge the test.
3. **Slab labelling comes from the capture, not from the renderer under test**: use the `depth_linear` plus the sidecar slab bounds; if the label boundaries disagree with the sidecar by more than one block, fail the frame.
4. Gate per slab AND overall (>= 0.995 each), min over frames, so one bad slab cannot hide in a large frame.
5. Unlayered frames keep v1 unchanged, so existing stills and yaw-only results (1.000) are unaffected.
Validation required before adoption (R-002 style, per slab): per-slab nearest 1.000, bilinear inside one slab fail, fractional-shifted slab with declared integer offset fail, two slabs on different declared offsets pass.

## 6. Evidence that would confirm or refute this recommendation
- A: Rerun the d-iv@v2 prototype on the existing 8 T-196 clips (needs frames; they are not in git). Prediction: per-slab 1.000 on slabs and the estimate matches the declaration; the seam-excluded area < 5 %. If a slab scores < 0.995 under its own offset, there is real resampling and P-1 must not paper over it.
- B: P-3 2AFC of per-slab vs shared-phase on p1_d_strafe_t192, p3_a_strafe_t192, p4_d_dolly_v4 (the translating ones). Refutes the recommendation only if viewers reliably prefer shared phase or call per-slab "swimming" at seams; then C11, not d-iv, should be the gate to renegotiate (and P-5 reconsidered).
- C: T-192's seam report on the shared arm (R-M3-DIV Q-NEW-h): needs one Unity cycle.
- D: Negative control for P-1: render a clip with deliberate per-slab bilinear and confirm failure.

## 7. What I could NOT verify
- Any d-iv number on layered translating frames (no frames kept; sibling scratch is non-layered). The 0.61-0.73 and 1.000 are taken from T-192/T-196 evidence tables.
- The slab-seam register visibility to a human; all arguments here are from definitions and forum-grade practice (R-M3-DIV sources 2-4, search summaries only, plus Texel Splatting arXiv snippet). The seam report of T-192 covers per-slab only.
- The exhaustive bound for 3+ slabs beyond R-M3-DIV's table; the derivation I rely on is the two-slab antipodal case.
- Whether the measured C11 failure on the shared arm depends on which slab the phase follows (T-196 states 1.49 for all three classes; I accept it).
- 4K or other k: only k = 4 reasoned about.
