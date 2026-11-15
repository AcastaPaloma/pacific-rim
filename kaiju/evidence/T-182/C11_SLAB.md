# C11 slab / depthShift captures (T-182)

Measured through the shipped present stage (output-resolution edge positions). Figures are max |displacement error| in output px over the frames of T1 where the edge is tracked (180; near about 130), per start offset; worst = max over start offsets, median = median over start offsets, plus how many start offsets meet 1 px. A start offset that puts the edge phase exactly on a texel rounding boundary flips by one texel (4 px) even when the lock is exact, so worst is a property of the number of starts tried; the median and the share are the stable figures. Model = R-131 `c11_sim.py` logic re-evaluated at the snapped depth of the configuration (16 edge phases). Gate: 1 px, unchanged.

## near class (R-131 depth 124.2 m)

| configuration | starts | frames (min) | measured worst | measured median | starts within 1 px | R-131 model worst / median | model at geometric depth worst / median | abs worst / median | gate 1 px (worst) |
|---|---|---|---|---|---|---|---|---|---|
| exact_layersnap | 24 | 133 | 4.81 | 0.96 | 23 / 24 | 4.04 / 2.58 | 0.75 / 0.75 | 2.91 / 1.51 | fail |
| ref663_base | 24 | 134 | 12.58 | 11.39 | 0 / 24 | 13.39 / 12.14 | 14.61 / 12.13 | 11.46 / 10.89 | fail |
| ref663_depthshift | 24 | 134 | 11.94 | 10.45 | 0 / 24 | 3.97 / 2.89 | 4.29 / 3.21 | 10.77 / 10.20 | fail |
| ref663_layersnap | 24 | 134 | 4.58 | 1.48 | 0 / 24 | 0.64 / 0.64 | 4.52 / 2.47 | 2.84 / 2.27 | fail |
| ref663_layersnap_and_depthshift | 0 | 0 | n/a | n/a | 0 / 0 | - | - | n/a / n/a | fail |
| ref664.874_base | 24 | 134 | 12.58 | 11.30 | 0 / 24 | 14.37 / 11.72 | 14.71 / 12.77 | 11.40 / 10.72 | fail |
| ref664.874_depthshift | 24 | 134 | 10.96 | 10.45 | 0 / 24 | 4.51 / 3.40 | 4.18 / 3.38 | 10.67 / 9.97 | fail |
| ref664.874_layersnap | 24 | 134 | 4.58 | 1.48 | 0 / 24 | 0.67 / 0.67 | 4.52 / 2.47 | 2.84 / 2.27 | fail |

## reference class (R-131 depth 663.0 m)

| configuration | starts | frames (min) | measured worst | measured median | starts within 1 px | R-131 model worst / median | model at geometric depth worst / median | abs worst / median | gate 1 px (worst) |
|---|---|---|---|---|---|---|---|---|---|
| exact_layersnap | 72 | 180 | 0.96 | 0.96 | 72 / 72 | 4.84 / 0.93 | 0.96 / 0.96 | 2.50 / 1.57 | pass |
| ref663_base | 72 | 180 | 3.99 | 1.11 | 0 / 72 | 0.96 / 0.96 | 3.87 / 1.14 | 2.52 / 1.63 | fail |
| ref663_depthshift | 72 | 180 | 3.99 | 1.11 | 0 / 72 | 0.96 / 0.96 | 3.87 / 1.14 | 2.52 / 1.63 | fail |
| ref663_layersnap | 72 | 180 | 3.99 | 1.11 | 0 / 72 | 0.96 / 0.96 | 3.87 / 1.14 | 2.52 / 1.63 | fail |
| ref663_layersnap_and_depthshift | 1 | 180 | 1.11 | 1.11 | 0 / 1 | - | - | 0.70 / 0.70 | fail |
| ref664.874_base | 72 | 180 | 0.96 | 0.96 | 72 / 72 | 4.84 / 0.93 | 0.96 / 0.96 | 2.50 / 1.57 | pass |
| ref664.874_depthshift | 72 | 180 | 0.96 | 0.95 | 72 / 72 | 4.84 / 0.93 | 0.96 / 0.96 | 2.50 / 1.58 | pass |
| ref664.874_layersnap | 72 | 180 | 0.96 | 0.96 | 72 / 72 | 4.84 / 0.93 | 0.96 / 0.96 | 2.50 / 1.57 | pass |

## far class (R-131 depth 10353.0 m)

| configuration | starts | frames (min) | measured worst | measured median | starts within 1 px | R-131 model worst / median | model at geometric depth worst / median | abs worst / median | gate 1 px (worst) |
|---|---|---|---|---|---|---|---|---|---|
| exact_layersnap | 48 | 180 | 4.04 | 0.95 | 47 / 48 | 0.65 / 0.65 | 0.65 / 0.65 | 3.52 / 1.49 | fail |
| ref663_base | 48 | 180 | 5.68 | 4.43 | 0 / 48 | 5.68 / 4.79 | 5.70 / 4.79 | 4.33 / 3.39 | fail |
| ref663_depthshift | 48 | 180 | 4.02 | 2.94 | 0 / 48 | 4.04 / 3.01 | 4.04 / 3.01 | 3.66 / 3.51 | fail |
| ref663_layersnap | 48 | 180 | 4.04 | 0.96 | 47 / 48 | 0.66 / 0.66 | 0.66 / 0.66 | 3.52 / 1.49 | fail |
| ref663_layersnap_and_depthshift | 1 | 180 | 0.96 | 0.96 | 1 / 1 | - | - | 1.33 / 1.33 | pass |
| ref664.874_base | 48 | 180 | 5.68 | 4.32 | 0 / 48 | 5.68 / 4.79 | 5.68 / 4.79 | 4.26 / 3.38 | fail |
| ref664.874_depthshift | 48 | 180 | 4.02 | 2.94 | 0 / 48 | 4.04 / 3.02 | 4.04 / 3.02 | 3.66 / 3.51 | fail |
| ref664.874_layersnap | 48 | 180 | 4.04 | 0.96 | 47 / 48 | 0.66 / 0.66 | 0.66 / 0.66 | 3.52 / 1.49 | fail |

## Any depth (R-131 model, per-layer snap with N log-spaced layers, worst over all surface depths)

- N=3: worst 13.1 px, median 3.6 px
- N=6: worst 7.4 px, median 3.4 px
- N=12: worst 5.4 px, median 3.3 px
- N=20: worst 4.9 px, median 3.2 px
- N=28: worst 4.8 px, median 2.5 px

depthShift ignored under layerSnap: edge tracks identical = True

Layered frames (min over start offsets): exact_layersnap 180/180, ref663_layersnap 180/180, ref663_layersnap_and_depthshift 180/180, ref664.874_layersnap 180/180

