# R-M3-DIV: M-003.d-iv vs C11 on layered translating frames (T-197)

Status 2026-11-15, T-197 worker (Sonnet), decided autonomously (founder asleep). Offline Python only; no Unity, no metric or threshold changed, **every metric change below is a proposal for an independent reviewer, never applied.** Inputs: `evidence/T-196/EVIDENCE.md`, `evidence/T-196/C11_SHARED_PHASE.md`, `research/R-131.md` (section 4), `research/R-002.md` (d-iv), `Assets/Game/Rendering/Pixel/MODULE.md` (shared grid phase). Code: `research/R-M3-DIV/lattice_bound.py` (+ `out_lattice_bound.json`), `research/R-M3-DIV/div_mech.py` (+ `out_div_mech.json`).

## 1. Question
T-196 measured: per-slab shifts give C11 0.96 px but M-003.d-iv 0.61-0.73 on translating layered clips (gate 0.995); a shared 4-px lattice phase gives d-iv 1.000 but C11 1.49 px (gate 1 px; T-196 says the floor is 1.5). (A) Is there a **render-side** option that satisfies both gates as currently read? (B) Separately: is the d-iv definition mis-specified for layered scenes?

## 2. Headline
1. **(A) No render-side option satisfies both as read, with one boundary exception.** Any frame in which every pixel sits on one k-pixel lattice and edge positions are integer pixels has C11 worst case **k/4 + 0.5 output px** for two slabs (k = 4: 1.5; verified by exhaustive search, section 4.1), because a continuously moving edge on a 4-px lattice is only ever within +-2 px, and a *shared* lattice phase can follow only one slab's edge. The three escapes all change something other than the renderer's technique: finer lattice (k = 2: bound exactly 1.0, zero margin, a look change), pixels excluded from d-iv (mask or output-resolution slab), or a per-region d-iv reading.
2. **(B) d-iv is under-specified for layered frames, not wrong where it applies.** It was validated only on single-lattice synthetic frames (R-002 table) and reads one sidecar offset although the T-182 sidecar already carries per-slab shifts. A per-region reading keeps what d-iv guards (resampling, undeclared sub-pixel shift) and drops only "all slabs share one lattice", for which the repo holds **no perceptual evidence either way** (static style references cannot speak to it; pixel-art practice is split, section 5.3). C11's 1 px (0.25 texel at k = 4, 0.167 at 4K) is the equally arguable half of the conflict (section 5.4).
3. Recommendation to the reviewer (proposal): keep C11 at 1 px; adopt **d-iv@v2 = per-slab-region lattice uniformity + a slab-seam register report**, and decide the shared-lattice question by a 2-alternative forced-choice viewing of the 8 existing clips (per-slab vs shared phase), because that is the unmeasured fact. If the reviewer rejects any metric change, the only both-pass path is k = 2 (section 4.2, option C), a project-level look decision, not a renderer tweak.

## 3. The conflict in one picture
- Slabs (near 124.2 m, main about 663 m, far 10.4 km) translate at different rates: ideal output shifts r_i = rho_i x remainder x k differ and their difference sweeps every residue modulo k as the camera moves.
- d-iv@v1 tests blocks aligned to **one** (ox, oy). A slab on a different lattice phase fails every block whose content has an edge (dither, shading, silhouette) inside it.
- C11 (T-182 jobs, `rigedge_layersnap`) measures the displacement error of one tracked edge per class; with a shared phase the edge of the slab that is *not* followed by the phase is off by up to the residue gap.

## 4. Findings, part A: render-side options

### 4.1 Bound (derivation, then search)
Model: slab i has real ideal shift r_i (output px) and applied shift p + k m_i (common integer phase p, integer m_i), so error_i = distance(r_i, p + kZ). Place residues r_i mod k on a circle of circumference k. For two slabs at **antipodal** residues the two distances sum to k/2 for every p, so max >= k/4, and an *integer* p can sit at most 0.5 px from the ideal mid-point: worst case k/4 + 0.5. Exhaustive search over residues (step 0.1 / 0.25, four sub-pixel phases) in `lattice_bound.py`:
| k | 2 slabs | 3 slabs | formula k/4 + 0.5 |
|---|---|---|---|
| 2 | 1.0 | 1.0 | 1.0 |
| 3 | 1.0 | 1.5 | 1.25 (3 slabs exceed it) |
| 4 (1080p) | 1.5 | 1.5 | 1.5 |
| 6 (4K) | 2.0 | 2.5 | 2.0 |
This reproduces T-196's measured 1.49 / 1.49 / 1.48 and its statement that 1.5 is the floor; it is **independent of how the slabs are rendered** because it only uses "one lattice" and "integer pixel positions". Per-slab free integer shifts have bound 0.5 (measured 0.96 including raster effects).

### 4.2 Options (satisfy both as read?)
| Option | d-iv (one declared offset) | C11 | Verdict as read | Cost, look and risk |
|---|---|---|---|---|
| A. Shared lattice phase (T-196, flag `layerSharedPhase`) | 1.000 | 1.49 (bound 1.5) | fails C11 | built, ships false |
| B. Per-slab shifts (T-192) | 0.61-0.73 | 0.96 | fails d-iv | shipping path today |
| C. **Finer lattice k = 2** (internal 960x540 at 1080p) with the shared phase | 1.000 (s = 2) | bound **1.0**, no margin (T-196 measured within 0.01 of its bound, so a pass at equality is fragile) | passes both only in the limit | 4x internal pixels (M-008 cost), halves the pixel size so every R-002 / M-003 calibration (blocks, bands, dither) must be redone; this is a style decision (P4), not a rendering fix. k = 3: 1.25-1.5, k = 6 (4K): 2.0-2.5 (worse) |
| D. Output-resolution near slab (k = 1 for the near slab only) | near-slab blocks non-uniform wherever textured: fails unless that area is under 0.5 % or masked | near about 0.5 | fails d-iv unless the slab is masked ("UI masked" mechanism) | mixed pixel sizes inside one frame (mixels): the look is no longer one pixel size; masking pixels to pass a metric is gaming it |
| E. 1-px-texel near slab | same as D | same | same | same |
| F. Per-slab grids, shared phase only at slab boundaries | each slab region is uniform on its own lattice; with one declared offset every non-main region fails; the boundary condition affects only blocks straddling the silhouette | 0.96 | fails d-iv as read; passes under the per-region reading (section 5), where it is just option B | "phase at the boundary" has no meaning inside a region: a lattice is a property of the region interior |
| G. Coarser far slab (k_far = 8, nested) | uniform in 4-blocks | far worse (steps up to 8 px) | fails C11 | halves far detail; look change |
| H. Choose the phase to follow the tracked edge of the class under test | 1.000 | 0.66-0.96 in the test | **not honest**: the three classes are measured in separate captures; in play all three slabs are on screen | would pass the gate and hide the 1.5 px in play; rejected |
| I. Cap relative parallax (camera lateral speed) | n/a | the residue gap still sweeps | does not remove the bound | gameplay cost |
| J. Time dither of the phase | n/a | C11 is a per-frame maximum | no effect | rejected |
Conclusion for A: **no option in {A..J} passes both with margin; only C passes at the boundary, only by changing k.**

## 5. Findings, part B: is d-iv mis-specified for layered scenes?

### 5.1 What d-iv guards (R-002)
"At least 99.5 % of s x s blocks uniform (max-min <= 1 code value) at the declared block offset, UI masked." R-002's validation: nearest upscale 1.000 pass, bilinear 0.226 fail, sub-pixel-shifted output with offset undeclared 0.242 fail, declared 1.000 pass. Purpose: reject resampling and undeclared sub-pixel shifts. All four validation frames are single-lattice. References score 0.001-0.43 and are excluded.

### 5.2 Mechanism check (synthetic, content assumed)
`div_mech.py`: a 1080p frame in three horizontal regions (30 / 40 / 30 %), each an exact 4-px lattice at offsets (0,0), (2,1), (1,3), random palette content with a flatness probability. Result (flat 0.5 / 0.8 / 0.9): **single declared offset 0.44 / 0.49 / 0.50; per-region offsets 1.000 / 1.000 / 1.000**. Magnitude depends on content (T-196's measured 0.61-0.73 is higher because real frames have more flat area); the point is that the failure is the metric reading one offset, not a loss of pixel crispness: every region is an exact lattice. Bilinear or undeclared shifts would still fail the per-region test (they break uniformity inside a region).

### 5.3 What per-region d-iv no longer guarantees
Lattice **register** across slabs: adjacent depth layers may sit on different pixel phases (a near silhouette over a far wall is then misregistered by 0-3 px relative to the far texel grid). Whether that is a visible defect is the open fact:
- For per-slab-independent layers: a pixel renderer devlog scales each parallax layer to an integer size and then scrolls layers independently and smoothly "to keep pixels crisp" [3]; the Godot and Monogame advice is to snap everything to a common render-grid unit and accept stepped parallax [2], [4] (forum-grade evidence, not studies).
- The style references are static; the repo has **no moving-image evidence** of either alternative. T-192 (per-slab) reported no tear or band seams in the A2/c1/c2 report; T-196 did not rerun the seam report for the shared arm.
So the d-iv@v1 requirement "one lattice per frame" encodes an untested perceptual assumption.

### 5.4 The other half: C11's 1 px
1 px is 0.25 texel at k = 4 but 0.167 texel at 4K (k = 6) and 0.5 texel at k = 2; the bound k/4 + 0.5 grows with k, so the same gate is stricter in texel terms as resolution rises, and standard pixel-art parallax (layers snapped to whole art pixels, [2]) has errors up to k/2 px by construction. C11 thus asks layered pixel art for sub-texel parallax fidelity that the usual practice does not offer; the gate's own origin (T-003 C-criterion on motion lock to the reference plane, R-131) was set for the single-reference-plane design. This does not prove the gate wrong; it shows the two gates were set independently and are jointly infeasible at k = 4 for depth-separated moving edges.

### 5.5 Assessment (honest)
- d-iv@v1 is **correctly defined for single-lattice frames and incorrectly applied to layered translating frames**: its single-offset input is a limitation of the sidecar reading, not of the idea. Per-region reading is a faithful generalisation, **provided** a register check is added; otherwise the metric loses the thing it was partly written to protect (one consistent pixel grid).
- It would be wrong to call the failures at 0.61-0.73 a visual defect on that evidence alone, and equally wrong to wave the conflict away: the constraint is geometric (4.1), so one of the two gates must be reinterpreted, or k changed.
- Which to reinterpret is a **founder / independent-reviewer call** (a perceptual judgement), so this note gives the evidence and the proposals, nothing applied.

## 6. Proposals for an independent reviewer (not applied)
| Proposal | Definition | Procedure | Effect | Source |
|---|---|---|---|---|
| P-1 M-003.d-iv@v2 (layered) | For frames whose sidecar has `layers`, label each output pixel by slab (from the sidecar slab depth bounds and the `depth_linear` capture), take each slab's own offset (the per-slab shifts in sidecar `layers`, mod k), count uniform blocks fully inside one slab region; gate >= 0.995 **per slab**; unlayered frames keep v1 | Python in `Tools/Rendering/Pixel` (T-182 `c11_slab.py` neighbours); re-run the 8 T-196 clip captures once (one Unity cycle, later; raw frames from T-196 are gone) | per-slab path expected ~1.000 (mechanism 5.2; **unmeasured on real frames**) | 5.1, 5.2 |
| P-2 Slab register report (advisory then gate) | Per frame: number of distinct lattice phases (<= number of slabs) and, per slab boundary, the fraction of boundary-straddling blocks; plus the mean absolute phase difference between adjacent slabs over the clip | same captures | makes the dropped property visible; gate value to be set from the viewing study | 5.3 |
| P-3 Forced-choice viewing (2AFC) | per-slab vs shared-phase renders of the 8 clips, founders or reviewers judge "pixel swim at depth edges" and "parallax feels wrong" | 16 short clips; no tooling beyond the existing capture driver | decides which of d-iv(one lattice) or C11 yields; sets P-2 threshold | 5.3 |
| P-4 C11 in texel units (cosmetic) | express the C11 gate as <= 0.25 texel (same at k = 4) so it follows k | threshold text only | removes the 4K inconsistency without loosening 1080p | 5.4 |
| P-5 Look decision k = 2 | internal 960x540 | a design decision for the founders, with M-008 cost | the only technical route that passes both as read | 4.2 C |
Not proposed: lowering either threshold, masking pixels, or choosing the phase from the tracked edge (H).

## 7. Open questions (decided autonomously)
| Q | Question | Default |
|---|---|---|
| Q-NEW-g | Which reading governs layered translating frames? | Keep the per-slab path shipping (C11 0.96, T-192 clean seams) and flag `layerSharedPhase` off; ask the reviewer to rule on P-1/P-2/P-3 before any gate text changes. |
| Q-NEW-h | Does the seam report for the shared arm matter? | Yes if P-3 favours shared phase: rerun `evidence/T-192` seam report on the T-196 arm (needs one Unity cycle, not now: demo build has priority). |
| Q-NEW-i | k = 2 as a look change | Not pursued; note for the founders' look review (cost 4x pixels). |

## 8. Sources (accessed 2026-11-15)
1. Repo evidence: `evidence/T-196/EVIDENCE.md`, `evidence/T-196/C11_SHARED_PHASE.md`, `research/R-131.md` section 4 (including its cited Texel Splatting preprint, arXiv 2603.14587, https://arxiv.org/abs/2603.14587, search snippet only), `research/R-002.md` (d-iv text and validation), `Assets/Game/Rendering/Pixel/MODULE.md`.
2. Godot Docs, 2D Parallax, https://docs.godotengine.org/en/4.6/tutorials/2d/2d_parallax.html (listed in the search; page not read), and forum advice, https://community.monogame.net/t/smooth-parallax-scrolling-for-low-res-2d-game/10741 (summarised from the search result only: snap camera and layer offsets to the same render grid).
3. "Smooth Parallax - Pixel Renderer Devlog #2" transcript summary, https://sozai.app/transcript/smooth-parallax-pixel-renderer-devlog-2/index.md (upscale each layer to an integer size, then scroll independently; search summary only).
4. Debugging guide, https://bugnet.io/blog/how-to-debug-pixel-perfect-2d-rendering-issues (fractional parallax-layer offsets cause shimmer; search summary only).
Sources 2-4 are forum and blog grade, read as search summaries; they support "practice is split", not a numeric claim. The bound in 4.1 and the mechanism in 5.2 are this note's own computations (code above).
