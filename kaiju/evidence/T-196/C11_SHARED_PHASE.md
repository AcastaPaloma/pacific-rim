# C11 slab / depthShift captures (T-182)

Measured through the shipped present stage (output-resolution edge positions). Figures are max |displacement error| in output px over the frames of T1 where the edge is tracked (180; near about 130), per start offset; worst = max over start offsets, median = median over start offsets, plus how many start offsets meet 1 px. A start offset that puts the edge phase exactly on a texel rounding boundary flips by one texel (4 px) even when the lock is exact, so worst is a property of the number of starts tried; the median and the share are the stable figures. Model = R-131 `c11_sim.py` logic re-evaluated at the snapped depth of the configuration (16 edge phases). Gate: 1 px, unchanged.

## near class (R-131 depth 124.2 m)

| configuration | starts | frames (min) | measured worst | measured median | starts within 1 px | R-131 model worst / median | model at geometric depth worst / median | abs worst / median | gate 1 px (worst) |
|---|---|---|---|---|---|---|---|---|---|
| rigedge_layersnap | 24 | 126 | 1.49 | 1.49 | 0 / 24 | 4.04 / 2.58 | 0.75 / 0.75 | 3.39 / 2.48 | fail |

## reference class (R-131 depth 663.0 m)

| configuration | starts | frames (min) | measured worst | measured median | starts within 1 px | R-131 model worst / median | model at geometric depth worst / median | abs worst / median | gate 1 px (worst) |
|---|---|---|---|---|---|---|---|---|---|
| rigedge_layersnap | 72 | 180 | 1.49 | 1.49 | 0 / 72 | 4.84 / 0.93 | 0.96 / 0.96 | 3.84 / 2.47 | fail |

## far class (R-131 depth 10353.0 m)

| configuration | starts | frames (min) | measured worst | measured median | starts within 1 px | R-131 model worst / median | model at geometric depth worst / median | abs worst / median | gate 1 px (worst) |
|---|---|---|---|---|---|---|---|---|---|
| rigedge_layersnap | 48 | 180 | 1.48 | 1.48 | 0 / 48 | 0.66 / 0.66 | 0.66 / 0.66 | 3.94 / 2.43 | fail |

## Any depth (R-131 model, per-layer snap with N log-spaced layers, worst over all surface depths)

- N=3: worst 13.1 px, median 3.6 px
- N=6: worst 7.4 px, median 3.4 px
- N=12: worst 5.4 px, median 3.3 px
- N=20: worst 4.9 px, median 3.2 px
- N=28: worst 4.8 px, median 2.5 px

Layered frames (min over start offsets): rigedge_layersnap 180/180

