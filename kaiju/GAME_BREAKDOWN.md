# GAME_BREAKDOWN.md: Bay Area Mech vs Kaiju (working title)

> **This document grows over time.** Agents add to it using the protocol in §0. Humans (the two founders) are the only ones who can mark an entry **Accepted**.
> Companion files:
> - `MODULARITY.md`: how the code is split up
> - `STACK.md`: tools and engine
> - `FABLE_ORCHESTRATOR_PROMPT.md`: how agents build the game

---

## §0. Expansion protocol (read before editing)

**Entry IDs.** Every entry has an ID that never changes:

| Prefix | Meaning |
|---|---|
| `D-###` | Design decision |
| `F-###` | Feature |
| `Q-###` | Open question |
| `R-###` | Research finding |
| `M-###` | Quality metric |

**Status values.**

| Status | Meaning | Who can set it |
|---|---|---|
| `Accepted` | Confirmed by the founders | Humans only |
| `Assumed` | Agent default chosen because a founder question went unanswered; must be confirmed | Agents (flag it in the change log) |
| `Proposed` | Agent suggestion (e.g. from the realism research loop) | Agents |
| `Rejected` / `Superseded by X` | Kept for history, never deleted | Humans (Rejected), anyone (Superseded) |

**Rules.**
1. **Append, never rewrite.** To change something, add a new entry and mark the old one `Superseded by D-###`. Nothing is deleted.
2. **No-fluff gate for Proposed entries.** Every Proposed entry must say:
   - (a) which experience pillar (§1) it serves
   - (b) what measurable change it makes, linked to a metric `M-###`
   - (c) its source or derivation, as an `R-###`
   - (d) its cost (performance, scope)

   Entries missing any of these are invalid.
3. **Log every edit.** Each edit adds one line to the §12 change log: date, agent role, IDs touched, reason.
4. **Ask instead of inventing.** When an agent needs a decision that isn't here, it adds a `Q-###` and either waits or uses an `Assumed` default.

---

## §1. Experience pillars (everything must serve at least one)

- **P1 · Towering weight.** Every movement should feel like moving thousands of tonnes: slow to start, slow to stop, the ground giving way.
- **P2 · Physical truth.** Fights are decided by physics: mass, momentum, leverage, structural failure. Nothing is faked where it can be simulated.
- **P3 · The real Bay.** The San Francisco Bay Area at 1:1 scale: real terrain, real seabed depths, real skyline, real fault lines.
- **P4 · Painted-pixel beauty.** Every frame should look like the style references: a limited palette, dithering, heavy atmospheric haze, and tiny figures that show the scale.
- **P5 · Two pilots, one war.** Co-op against kaiju that coordinate as a pack, with losses that matter.

---

## §2. Core decisions (from founder interview, 2026-11-14)

| ID | Decision | Status |
|---|---|---|
| D-001 | Genre: co-op physics brawler against AI-controlled kaiju in an open world (no longer a 1v1 fighting game) | Accepted |
| D-002 | Each player pilots one giant mech | Accepted |
| D-003 | Several pilots in one mech is a future control/UX feature. Architecture must allow it (see `MODULARITY.md` → Control) but it is not built now | Accepted |
| D-004 | Launch with 2 players; the code must support N players, nothing hard-coded to 2 | Accepted |
| D-005 | Kaiju are controlled by trained RL policies at the **tactical** level (targets, moves, positioning). Motion is carried out by animation blended with physics | Accepted |
| D-006 | Camera: third person. First person is a later swappable camera module | Accepted |
| D-007 | Scale follows the film genre's numbers: mechs 76–88 m tall, kaiju 60–180 m by mass category | Accepted |
| D-008 | Proportions are film-like heroic humanoid mechs. **Mass is derived from physics, not film canon** (§4) | Accepted |
| D-009 | Commercial release with **original IP**. Copy scale, proportions and weapon *types*; never copy names, designs, lore or iconography from any franchise | Accepted |
| D-010 | World: the **full Bay Area at 1:1 scale**, open world, with kaiju roaming | Accepted |
| D-011 | Combat is melee-first brawling (strikes, grapples, shoves, throws into buildings) plus cannons and heavy weaponry (arm cannons, extendable blades, rocket-assisted strikes; original designs) | Accepted |
| D-012 | Damage is locational and structural for mechs, kaiju and buildings alike. No abstract HP bars | Accepted |
| D-013 | Controls are intent plus physics: the player chooses the move and where it lands; IK and physics produce the motion within mass and balance limits | Accepted |
| D-014 | Kaiju emerge along real Bay Area fault lines (San Andreas, Hayward, etc.), with an earthquake before each emergence | Accepted |
| D-015 | Kaiju goals (RL rewards): hunt mechs, destroy infrastructure, act as a pack | Accepted |
| D-016 | Destruction persists across the world. Civilians are not scored | Accepted |
| D-017 | Between fights: hangar for repair, refit and upgrades. Damage carries over | Accepted |
| D-018 | Mechs can walk, turn, brace, wade the Bay, fall and get up, and use thruster bursts | Accepted |
| D-019 | Mech limits: reactor power budget, heat, limited ammo, pilot strain | Accepted |
| D-020 | A destroyed mech is **lost permanently** | Accepted |
| D-021 | Era: near future (2030s). Today's skyline is mostly intact at the start | Accepted |
| D-022 | Art: match the style references exactly (§9). The references set **style only**; nothing from their subject matter enters the game | Accepted |
| D-023 | Concept art pipeline: Midjourney driven through computer use (the founders accept the account risk), then Blender, then Unity, built up layer by layer | Accepted |
| D-024 | Economy: city defence contracts pay for mech builds; salvage from kaiju and wreckage provides rare parts | **Assumed** |
| D-025 | Mech roster: one shared modular frame system with 2 chassis classes at launch (Heavy Brawler, Balanced). Specialisation comes from swappable limbs and weapons | **Assumed** |
| D-026 | Kaiju roster at launch: 3 species across mass categories (light ≈ 60 m, mid ≈ 100 m, heavy ≈ 150–180 m). They share one policy architecture, with one trained policy per species | **Assumed** |
| D-027 | Atmosphere v1: a fixed time of day and palette per district (warm or cold, as in the references). Day/night, fog and weather come later as separate modules | **Assumed** |

---

## §3. Scale reference (real world vs. giants)

| Object | Height |
|---|---|
| Human | 1.8 m (one or two pixels at gameplay distance, matching the tiny figures in the references) |
| Golden Gate Bridge roadway | ≈ 67 m above water |
| Golden Gate Bridge towers | ≈ 227 m above water |
| Mech (D-007) | 76–88 m: chest at deck height, head well below the bridge towers |
| Transamerica Pyramid | ≈ 260 m |
| Salesforce Tower | ≈ 326 m: taller than any kaiju |
| Kaiju | 60–180 m |

**Water.** Most of SF Bay is only a few metres deep, so giants wade through it. The Golden Gate channel is around 100 m deep, enough to submerge a giant. Exact depths come from NOAA bathymetry (R-task).

---

## §4. Physics truths the game must respect

Constants and starting derivations. Research agents refine them (`R-###`).

**Mass from structure (D-008).** A humanoid scaled from human volume $V_h \approx 0.07\ \text{m}^3$ at $l_h = 1.8$ m:

$$V = V_h\left(\frac{l}{l_h}\right)^3,\qquad l = 80\ \text{m} \Rightarrow V \approx 6.1\times10^{3}\ \text{m}^3$$

With an effective density of $\bar\rho \in [1000, 2500]\ \text{kg/m}^3$ (a hollow armoured frame), $m \approx 6{,}000$–$15{,}000$ t. Film-canon masses (~2,000 t) would give $\bar\rho \approx 376\ \text{kg/m}^3$, which is lighter than water. Rejected.

**Gait timing (P1).** Dynamic similarity says

$$\mathrm{Fr} = \frac{v^2}{g\,l},\qquad t \propto \sqrt{l}$$

so motions at 80 m take $\approx 6.7\times$ as long as human ones. Walking uses close to full Froude timing. Attacks use a partial stretch:

$$t_g = t_h\left(\frac{l_g}{l_h}\right)^{\alpha},\qquad \alpha \in [0.11,\,0.24]$$

This follows the film-VFX practice: slow enough to feel huge, not "underwater".

**Ground pressure.** A 10,000 t mech on one foot of ≈ 150 m² exerts about

$$\frac{mg}{A} \approx \frac{9.8\times10^{7}\ \text{N}}{150\ \text{m}^2} \approx 650\ \text{kPa}$$

statically, and more under dynamic load. Typical soils bear 100–300 kPa. Feet sink, crush roads and leave craters.

**Thrusters (D-018).** Lifting a 10,000 t mech needs $\approx 98$ MN, nearly three Saturn V first stages (≈ 35 MN each). So thrusters only provide **bursts and partial unweighting** (lunges, recovering balance, slowing a fall), limited by power and heat (D-019).

**Falls.** Toppling from standing with the centre of mass at ~40 m releases

$$E \approx m\,g\,h_{\text{cm}} \approx 4\ \text{GJ}$$

roughly one tonne of TNT. Falls damage the mech, crater the ground and strain the pilot.

**Wading.** Drag is $F_d = \tfrac12 \rho_w C_d A v^2$, plus buoyancy on the submerged volume.

**Latency bonus.** Heavy mechs have low maximum acceleration, so network prediction errors stay small. Physical slowness hides network latency.

---

## §5. Features: mechs

| ID | Feature | Pillars | Status |
|---|---|---|---|
| F-001 | Articulated physics body (ArticulationBody chain) driven by PD controllers toward IK targets | P1, P2 | Accepted |
| F-002 | Intent control: the player picks a move and a target point; IK and physics carry it out within mass and balance limits | P1, P2 | Accepted |
| F-003 | Locomotion: walk, turn, brace stance, wading with drag, fall under physics, slow get-up sequence | P1, P2, P3 | Accepted |
| F-004 | Thruster bursts: partial unweighting, lunges, fall arrest. Uses power, produces heat | P2 | Accepted |
| F-005 | Melee: strikes, grapples, shoves, throws. Damage comes from impulse and contact energy at the joint or plate struck | P2 | Accepted |
| F-006 | Heavy weapons: arm cannon, extendable blade, rocket-assisted strike (original designs). Ammo is finite per sortie | P2 | Accepted |
| F-007 | Structural damage: armour plates, joints, actuators and limbs fail on their own. Losing a part changes mass, balance and available moves | P2 | Accepted |
| F-008 | Subsystems: reactor power budget, heat build-up and venting, ammo, pilot strain (stun or injury from large accelerations at cockpit height) | P1, P2 | Accepted |
| F-009 | Footfall coupling: each foot contact triggers ground deformation, decals, debris, camera impulse and audio at the exact contact frame | P1 | Accepted |
| F-010 | Permadeath: a destroyed mech is gone. Salvageable parts may be recovered | P5 | Accepted |
| F-011 | Several pilots in one mech (future) | P5 | Proposed (D-003) |
| F-012 | First-person cockpit camera (future) | P1 | Proposed (D-006) |

## §6. Features: kaiju

| ID | Feature | Pillars | Status |
|---|---|---|---|
| F-020 | Emergence: earthquake, then fault rupture, then kaiju rises along real fault traces | P3 | Accepted |
| F-021 | Two-layer brain: an RL tactical policy decides targets, moves and positioning; a motor layer carries them out with animation plus physics | P2, P5 | Accepted |
| F-022 | Reward terms: damage to mechs, infrastructure destroyed, pack coordination (flanking, baiting, focusing the same target); penalty for taking damage | P5 | Accepted |
| F-023 | Graviportal biomechanics: column-like limbs, wide stance, mass-appropriate gait timing | P1, P2 | Accepted |
| F-024 | Shares the structural damage system with mechs (F-007): limbs crippled, armour broken | P2 | Accepted |
| F-025 | Three species at launch (D-026), each with its own trained policy | P5 | Assumed |
| F-026 | Fallback scripted brain behind the same interface, for testing and for when no policy is trained yet | — | Accepted |

## §7. Features: world (the Bay)

| ID | Feature | Pillars | Status |
|---|---|---|---|
| F-040 | 1:1 Bay Area generated from open data: terrain (USGS elevation), seabed (NOAA bathymetry), buildings (OpenStreetMap footprints and heights), roads, faults (USGS fault database) | P3 | Accepted |
| F-041 | Hand-crafted landmarks at true scale: both bridges, downtown towers, the port of Oakland, Alcatraz, etc. | P3, P4 | Accepted |
| F-042 | World streaming with a floating origin. float32 resolution at 100 km is $2^{-7}$ m ≈ 0.8 cm, enough to make joints and IK jitter, so the origin must be shifted | P3 | Accepted |
| F-043 | Persistent destruction: each building has an ID and a damage state saved in the world save file | P2, P3 | Accepted |
| F-044 | Infrastructure targets kaiju go after: bridges, power, port cranes, airport | P5 | Accepted |
| F-045 | Water: shallow-Bay wading, deep-channel danger, splash and wake | P2, P3 | Accepted |
| F-046 | District palettes, fixed for v1 (D-027) | P4 | Assumed |
| F-047 | Fog, weather and day/night as later modules | P4 | Proposed |

## §8. Features: meta and co-op

| ID | Feature | Status |
|---|---|---|
| F-060 | Hangar at a Bay site: repair, refit, upgrade; damage carries over | Accepted |
| F-061 | Economy: contracts plus salvage (D-024) | Assumed |
| F-062 | Two chassis classes on one modular frame (D-025) | Assumed |
| F-063 | Host-authoritative online co-op over Steam; N player slots | Accepted |
| F-064 | Partner actions: brace a partner, drag a downed mech, combined grapples | Proposed |

---

## §9. Art direction (style only, D-022)

**What the style references share.** These are the measurable targets for the evaluation agents.

**Colour**
- Two or three dominant hues per scene plus **one accent**. Seen in the references: rust-red/orange with sepia; slate teal; lavender-blue with peach fire; slate with a crimson accent.
- Limited, posterised value bands.
- **Atmospheric perspective dominates**: distant layers are lighter and lower in contrast, near layers are darkest.

**Pixel treatment**
- A visible pixel grid at a low internal resolution: target 1/4 to 1/6 of the output resolution, tuned by the evaluation agents.
- **Ordered dithering** in gradients, smoke and sky.
- Clouds, smoke and dust rendered as clustered pixel masses with soft dithered edges.
- Vertical light shafts or streaks where dramatic.

**Composition**
- Readable silhouettes first.
- Giant forms fill the frame.
- **Tiny human-scale figures and objects** in the foreground for scale.
- Horizon low, sky large.

**Reference handling**
- Files live in `art/style_refs/`.
- Some references are fan art of existing games. Use them for **style only**; never reproduce their characters, armour designs or iconography (D-009, D-022).

**Rendering approach**
- URP custom renderer features:
  - low-resolution render target
  - palette quantisation via LUT, with a palette profile per district
  - Bayer dithering
  - depth-based haze bands
  - optional outlines
- Third-person camera: snap the camera position to the texel grid and compensate for sub-pixel offset to reduce shimmer during movement. Evaluate shimmer during rotation (M-004).

---

## §10. Quality metrics (seed set; research agents refine and source them)

| ID | Quality | Metric (initial) |
|---|---|---|
| M-001 | Feels heavy | Gait Froude number within the target band. Peak acceleration and turn rate below derived limits. Time-to-stop matches mass. Panel score ≥ 8/10 on "feels like 10,000 t" |
| M-002 | No foot sliding | Planted-foot slip per contact < 0.5% of foot length |
| M-003 | Matches the style references | Share of pixels within palette ΔE tolerance ≥ 95%. Dithering present in gradients. Value banding by depth is monotonic. Blind evaluator panel style-match ≥ 8/10 |
| M-004 | Pixel stability | Measured shimmer and crawl during camera translation and rotation below threshold |
| M-005 | Bay is accurate | Landmark positions within X m of GIS data. Building height distribution matches OSM. Skyline silhouette from known viewpoints matches reference photos |
| M-006 | Physics is plausible | Energy and momentum checks on impacts. Ground-pressure response. Falls release energy consistent with §4 |
| M-007 | Kaiju look intelligent | Win rate vs scripted baseline. Measured pack behaviours (flank rate, focus fire). Human panel rates them "smart, not cheap" |
| M-008 | Performance | 60 fps on the target PC. Host simulation budget met with 2 mechs + 3 kaiju + destruction |


### §10a. Metric specs (versioned), appended 2026-11-14

Registered by research agents; the seed rows above are unchanged. All status `Assumed, pending founder calibration` (the numbers are derived where a source is cited, but thresholds marked Assumed in the source files need founder taste). Evaluators cite the version used (`M-003@v1`).

| Spec | Sub-measures | Headline thresholds | Source | Status |
|---|---|---|---|---|
| M-001@v1 | 13 (M-001.1 to .13; PASS needs all) | Walking Fr in [0.10, 0.30], cruise 0.20 ±0.03, never > 0.35; CoM acceleration ≤ 0.30 g fore-aft, ≤ 0.085 g lateral, net start ≤ 0.14 g; yaw ≤ 26.7 °/s and ≤ 4.0 °/s²; start 2-4 steps, normal stop 1.5-3 steps; panel median ≥ 8/10, n ≥ 3. Log contract `gait_log@v1` | R-001 §9 (`research/R-001.md`) | Assumed, pending founder calibration |
| M-002@v1 | 5 (M-002.1 to .5) | Max slip per contact < 0.5 % of foot length (< 0.098 m at the reference foot, L_f = 19.6 m); p95 < 0.25 % (reported) | R-001 §9 | Assumed, pending founder calibration |
| M-003@v1 | 6 gated groups (a, b, c1, c2, d, f) + (e) advisory | CIEDE2000 ≤ 2.0, ≥ 95 % of output pixels (≥ 99.5 % internal) in the declared profile; dither interleave share DIG ≥ 0.15 on ≥ 80 % of gradient frames; depth-band mean L* monotonic; ≤ 12 value levels, ≤ 3 hue families, grid integrity ≥ 0.995; blind panel mean ≥ 8.0. Five district profiles, `palette_set@v1` | R-002 §4 (`research/R-002.md`) | Assumed, pending founder calibration |
| M-004@v1 | 3 measures (S_px, C_px, F) over capture sequences S0, T1-T3, Y1-Y2 | Static change = 0; yaw S_px ≤ 0.01, C_px ≤ 0.01, F ≤ 0.001; translation S_px ≤ 0.12, C_px ≤ 0.15, F ≤ 0.005. Capture contract `style_capture@v1` | R-002 §5 | Assumed, pending founder calibration |
| M-005@v1 | 8 (M-005.1 to .8) | Landmark position ≤ 0.5 m vs source (p95 ≤ 5 m vs reference); NGS terrain median abs. error ≤ 0.5 m; Golden Gate depth -113 ± 4 m, Oakland harbour -15.2 ± 1 m, 66 ± 3 % of the Bay < 5 m; building-height KS D ≤ 0.02; area recall ≥ 0.98; scale ≤ 60 ppm; skyline IoU ≥ 0.98 same-source, ≥ 0.90 independent; attribution 100 %. Proposed to replace the seed text of M-005 (founder decision; seed row kept) | R-004 §6 (`research/R-004.md`) | Assumed, pending founder calibration |
| M-006@v1 | 8 (M-006.1 to .8; PASS needs all in one commit) | Totals within 0.1 %; energy drift passive drop ≤ Δt/t + 0.5 %; momentum error ≤ 0.5 % linear, ≤ 2 % angular; single-stance pressure 654 kPa ± 2 %; fall energy 3.6 GJ ± 10 %; mean walking power 30-120 MW, peak ≤ 450 MW; link speed ≤ 60 m/s, zero NaN; CoM divergence ≤ 1 cm at 60 s. Log contract `physics_log@v1` | R-005 §8 (`research/R-005.md`) | Assumed, pending founder calibration |
| M-005@v1.1 | 9 (M-005.1 to .9; supersedes M-005@v1 for the changed sub-measures, v1 kept) | As v1 except: shoreline seam redefined as M-005.9 (excess jump of the built surface over both sources in the blend band + 5 m: p99 ≤ 0.20 m, max ≤ 0.50 m); default-height share by footprint area as M-005.4(d) (M0 ≤ 10 % area, M1+ ≤ 2 % area); skyline reference R-ind recipe (class, vegetation, opening, change masks) and V4 reframed (heading 257.6°, hFOV 75°; IoU ≥ IoU(F_v) - 0.02 and ≥ 0.80, MAE ≤ MAE(F_v) + 1 px); draw calls by ProfilerRecorder (procedure only). v1 results stay valid for the unchanged sub-measures .1, .2, .3, .6, .8 and .4(a-c); .7 R-src only for V4; .7 R-ind, .4(d) and .9 must be recomputed | R-006 §5 (`research/R-006.md`) | Assumed, pending founder calibration |
| M-006@v1.1 (proposal, not registered) | Clarifications to M-006.6 and M-006.4, not relaxations | 6.6 "peak Σ P" = peak of Σ positive joint power averaged over 0.1 s, Σ\|P\| reported separately; 6.4 walking peak F_n filtered with M-001's 0.05 T moving average before the 1.0-2.0 BW check | R-007 §3 (`research/R-007.md`) | Proposed (for the founders; M-006@v1 stays the registered spec) |
| M-006@v1.1 | M-006.6 and M-006.4 clarified, M-006.5 (limp) reframed; not relaxations | 6.6 "peak Σ P" = peak of Σ positive joint power averaged over 0.1 s, Σ\|P\| reported separately; 6.4 walking peak F_n filtered with M-001's 0.05 T moving average before the 1.0-2.0 BW check; 6.5 passivity (<= 1 % cumulative gain, no step > 7.8e7 J) plus released energy 3.6 GJ ± 10 %, residual friction and limit work reported not gated | R-007 §3, R-008 §6 | Assumed (adopted for T-005 round 2, pending founder calibration) |

M-007 and M-008 have no versioned spec yet (M-NEW-derived sub-metrics for M-008 are proposed below as M-013, M-014 and M-018).

Added 2026-11-14 (scribe, second pass): `M-005@v1.1` registered (from R-006); `M-006@v1.1` recorded as a Proposed clarification only. Evaluators of T-003 cite the version they used; v1 sub-measure results stay valid where marked unchanged.

### §10b. Proposed new metrics (M-009 to M-017; M-018 to M-020 added later), appended 2026-11-14

Each carries the four no-fluff fields of §0 rule 2. Status `Proposed` unless marked incomplete.

| ID | Metric | Pillar | Measurable change / metric link | Source | Cost | Status |
|---|---|---|---|---|---|---|
| M-009 | Footfall sync (F-009): camera impulse in the same physics step as `contact_begin` in ≥ 99 % of contacts (≤ 1 step always, ≤ 1 render frame); one impulse per contact; amplitude vs vertical impact momentum log-log r ≥ 0.90, slope in [0.5, 1.0] (Assumed); footstep audio onset within +45 / -125 ms | P1 | Links to M-001 and F-009 | R-001 §6 (R-001 M-NEW-a) | Logging only | Proposed |
| M-010 | Log contract `gait_log@v1` as a versioned data spec (R-001 §8), owned by `Tools/` metric capture; mech and kaiju implementations emit it for every locomotion capture | P1, P2 | Input to M-001 and M-002 | R-001 §8 (R-001 M-NEW-b) | Not stated | Proposed (incomplete: missing cost) |
| M-011 | Output motion smoothness: with sub-pixel compensation on, per-frame displacement of `color_output` (phase correlation) during T1/Y1 matches the commanded camera motion within ±1 output px | P4 | Extends M-004 | R-002 (R-002 M-NEW-a) | One extra analysis pass | Proposed (not validated in source) |
| M-012 | Palette ramp L* uniformity: coefficient of variation of consecutive haze-ramp ΔL* ≤ 0.35 (Assumed); extracted quantile ramps score 0.33-1.18, `ramp_uniform_L` scores 0 | P4 | Supports M-003 (c2) | R-002 (R-002 M-NEW-b) | None at runtime | Proposed |
| M-013 | Render GPU budget (sub-metric of M-008): GPU time of passes named `Pixel.*` ≤ 0.5 ms p95 and whole frame ≤ 16.7 ms p95, Windows player, target PC, 1080p, 60 s flythrough with 2 mechs + 3 kaiju | P4, P1 | Sub-metric of M-008 | R-003 §9 / §11 (R-003 M-NEW-a) | One capture tool | Proposed (Assumed, pending calibration) |
| M-014 | ABA flicker rate: share of valid internal texels whose palette index goes A-B-A over three frames after reprojection ≤ 1.0 % per frame translation, ≤ 2.0 % rotation, and ≥ 50 % lower than with stabilisation off. R-002 owns the M-004 spec; reconcile | Not stated (P4 implied) | Input to M-004 | R-003 §7 (R-003 M-NEW-b) | Not stated (internal RT capture and analysis) | Proposed (incomplete: missing pillar, cost) |
| M-015 | Floating-origin precision (F-042): Unity position of every simulated body ≤ 4096 m at all times (float32 ulp ≤ 0.49 mm), runtime probe over a 10 min traversal | P2, P3 | Supports M-005 / F-042 | R-004 §2.4 (R-004 M-NEW-a) | Negligible | Proposed |
| M-016 | Power plausibility: mean positive mechanical power at Fr 0.25 walking within 30-120 MW, peak ≤ 450 MW at the 80 m / 10,000 t reference (scales with M·H^½); folded into M-006.6, split out if a Power subsystem owns it | P1, P2 | Measured from `physics_log@v1` power columns; links M-006 | R-005 §3.3 (R-005 M-NEW-a) | None beyond M-006 logging | Proposed |
| M-017 | Core bus cost: ns per publish per subscriber and bytes per publish (target 0 bytes); informational until registered | Not stated | Links to M-008 (implied) | `expectations/T-001.md` (draft A M-NEW-a); no R-### | Not stated | Proposed (incomplete: missing pillar, R-### source, cost) |
| M-018 | Tile render-budget measurement (support for M-008 and T-003 C20): ProfilerRecorder frame totals (Draw Calls, Batches, SetPass, Triangles, Shadow Casters), 30 + 120 frames per view, graphics device on, with a Frame Debugger per-pass capture on each URP change; thresholds per T-003 C20 (Assumed) | P4, P1 | Sub-metric of M-008 (replaces the renderer-count estimate, DEF-T003-perf-1) | R-006 §4 (R-006 M-NEW-a) | One capture script | Proposed (Assumed until a target PC exists, Q-019) |
| M-019 | Stance free-moment utilisation: per stance, \|M_z\| / (μ_d F_n r̄_contact) from `physics_log` contact points; ≤ 0.67 (the M-002 1.5 factor), target ≤ 0.5 | P1, P2 | Diagnoses foot twist and yaw; links M-002, M-006 | R-007 §2.2 (R-007 M-NEW-a) | Logging only | Proposed (target Assumed, pending calibration) |
| M-020 | Touchdown CoM vertical velocity: v_y of the CoM at each footfall ≤ v sin(atan((s - 15 m)/(2 h))) + 0.3 m/s (1.69 + 0.3 m/s at Fr 0.20), plus foot v_y ≤ 1.0 m/s (R-005 §5.2.5); separates controller-made from gait-made impacts | P1 | Diagnostic for M-001 and M-006.4 | R-007 (R-007 M-NEW-b) | Logging only | Proposed |

Merged duplicates among M proposals: none (nine distinct proposals).

Added 2026-11-14 (scribe, second pass): M-018 (from R-006) and M-019, M-020 (from R-007); no duplicates.

---

## §11. Open questions

| ID | Question | Blocking? |
|---|---|---|
| Q-001 | Confirm the four Assumed decisions D-024 to D-027 | No (defaults in use) |
| Q-002 | Hangar location: which real Bay site? | No |
| Q-003 | How many kaiju can be active at once in the open world? | Yes, for the performance budget |
| Q-004 | Story framing: how much narrative, if any? | No |
| Q-005 | Pilot representation: on-foot pilots ever, or always inside the cockpit? | No |
| Q-006 | Cruise Froude number for the reference mech (R-001 Q-NEW-c, T-005 cruise pace). R-001 and T-005 use Fr 0.20 (9.1 m/s, stride 6.6 s); R-005 sizes torque and power at Fr 0.25 (10.15 m/s). Both recorded; not reconciled. Default in use (Assumed): Fr 0.20, founders calibrate with the M-001 Froude-sweep captures. | No |
| Q-007 | Foot shape and size (R-001 Q-NEW-a, T-005 foot shape; see also Q-013). Both sources give 150 m² but disagree on shape: 19.6 × 7.65 m (R-001, 0.245 H, 2.9× the human-proportioned ≈ 52 m² foot) versus 15 × 10 m (R-005). Keep the oversized heroic feet? Default in use (Assumed): yes, 150 m², 15 × 10 m (T-005); M-002 scales with L_f from the log header. | No |
| Q-008 | Reactor power for the reference mech (R-001 Q-NEW-b, R-005 Q-NEW-b; D-019, F-008). Sources disagree: R-001 200 MW mechanical (20 W/kg); R-005 300 MW continuous electrical plus 1.5 GJ peak buffer (≈ 150 MW for 10 s), scaled with M·H^½. Default in use (Assumed): not reconciled; M0 walking needs ≈ 50-100 MW mean and ≈ 400 MW peak (R-005), so neither figure blocks M0. | Yes, for F-004 and the Power subsystem (not M0 walking), per R-005 |
| Q-009 | Minimum mass versus height (R-005 Q-NEW-a, T-005 minimum mass). At 80 m / 6,000 t the hull is lighter than sea water (739 kg/m³) so the mech would float in the Golden Gate channel. Allow it, or require M ≥ 8,500 t × (H/80)³? Default in use (Assumed): lower bound M ≥ 8,500 t × (H/80)³; the 6,000 t corner loads with a warning. | No |
| Q-010 | Start, stop and turn feel ceilings (R-001 Q-NEW-d, T-005 sluggishness). 4 steps to start (7-13 s), 3 to stop, 90° turn in ≥ 6.7 s, 5.3 °/s turning at cruise, first visible response to the stick ≤ 1.5 s (unregistered) trade P1 against responsiveness. Are the derived limits fun? Default in use (Assumed): keep the derived limits; decide from the calibration captures. | No |
| Q-011 | Foot-ground friction coefficient on Bay soils and pavement (R-001 Q-NEW-e). Default in use (Assumed): μ = 0.4, to be replaced by a follow-up R-task on soil bearing and shear (also needed for the §4 ground-pressure claims). | No |
| Q-012 | Heel-strike harshness (R-005 Q-NEW-c): touchdown vertical speed target trades a heavier thud (P1) against impulse and pressure spikes (M-006.4 peak ≤ 2 BW). Default in use (Assumed): ≤ 1.0 m/s, tune with the feel evaluator. | No |
| Q-013 | Mech proportions in `mech_frame_balanced.json` (shoulder breadth 0.34 H, head 0.11 H; foot, see Q-007) are a heroic default chosen by research (R-005 Q-NEW-d). Confirm, or let the concept-art pass revise under `mech_frame@v2`. Default in use (Assumed): as published. | No |
| Q-014 | Assisted balance (T-005): if full dynamic balance cannot be reached, is a small declared stabilising assist acceptable? The two expectation drafts disagree (A: yes if flagged and reported; B: at most 1 % of m·g or ankle torque limit, never silent); this touches P2. Default in use: the gate is judged with assistance off. | No |
| Q-015 | Dither type (R-002 Q-NEW-a). The references show clustered or irregular dithering (ordered index O = -0.34…+0.15) while §9 specifies ordered Bayer (O ≈ 1.1). Bayer 4×4, blue-noise or clustered mask for v1? Default in use (Assumed): Bayer 4×4 as §9 says, O advisory, the M-003 (f) panel compares a blue-noise variant. | No |
| Q-016 | Translation stability (R-002 Q-NEW-b): direction-locked dither cannot get below ≈ 0.245 changed pixels per pixel of lateral motion under perspective. Is surface-anchored dither for near geometry in scope for v1? Default in use (Assumed): yes for terrain, buildings and giants; direction-locked for sky and fog; M-004 translation threshold stays 0.12. | No |
| Q-017 | Style profile set (R-002 Q-NEW-c): accept five v1 profiles (warm_rust_sepia, warm_amber_haze, slate_teal, lavender_peach, slate_crimson) where §9 lists four. Default in use (Assumed): yes; ref_04B and ref_06A stay excluded. | No |
| Q-018 | Internal resolution rule (R-002 Q-NEW-d, R-003 Q-NEW-b). R-002: 480×270 at 1080p (scale 4) and integer scale 8 at 4K, 320×180 as the coarse bound. R-003: constant ≈ 270 lines (k up to 8 at 4K) or the §9 range k in [4, 6]; its default k = clamp(round(H/270), 4, 6), which gives scale 6 at 4K. The two defaults differ at 4K; not reconciled. Default in use (Assumed): 480×270 at 1080p, 4K undecided. | No |
| Q-019 | Target PC and test hardware for M-008 (R-003 Q-NEW-a, Q-NEW-d). Default in use (Assumed): Windows 10/11 x64, RTX 3060 12 GB / RX 6600 class, 6-core CPU, 16 GB RAM, SSD, 1080p60 (1440p recommended tier). No Windows test hardware is listed in STACK.md (dev is an M5 Pro Mac): Mac numbers are relative regressions only and M-008 stays unverified until target-class hardware or a cloud GPU exists. | Blocking for M-008 sign-off only (not development) |
| Q-020 | Placement of `PixelStabilisationHints` / `IPixelStabilisationSource` in `Rendering.Pixel.Contracts` (L1) with the Camera rig as provider, to respect MODULARITY §3 layering (R-003 Q-NEW-c). Default in use (Assumed): yes. | No |
| Q-021 | Accept camera rotation quantisation (turns step in ≈ 0.2° increments at slow speeds) as part of the heavy-mech camera feel (R-003 Q-NEW-e)? Default in use (Assumed): on, behind `Pixel.RotationQuantise`, re-judged by the M-004 and feel panels. | No |
| Q-022 | `art/style_refs/ref_01.png` and `ref_02.png` are byte-identical (R-003 Q-NEW-f). Was a different sixth reference intended? Default in use: treat the set as 5 references. | No |
| Q-023 | Is shipped OSM-derived tile data a Produced Work or a Derivative Database under ODbL (R-004 Q-NEW-a)? Default in use (Assumed): treat cooked OSM layers as a Derivative Database, publish those layers (geometry and tags only) under ODbL, credit OpenStreetMap contributors on the splash and in the credits, get legal review before the Steam release. | Blocking for release, not M0 |
| Q-024 | Render Earth curvature (R-004 Q-NEW-b)? The flat world hides a 196 m drop at Mt Diablo seen from SF. Default in use: visual-only curvature drop in the far-terrain/LOD vertex shader beyond 2 km (R_eff = 7/6 R); physics stays flat. | No |
| Q-025 | Maximum separation between co-op players under host-authoritative PhysX (R-004 Q-NEW-c; F-063, F-042). Default in use: one `PhysicsScene` per activity region with its own tile-corner origin whenever players are more than 4 km apart. | No |
| Q-026 | OSM snapshot policy for persistent building IDs (R-004 Q-NEW-d; F-043). Default in use: pin one OSM replication timestamp per release; on update keep an ID remap table (old `bid` to new) from footprint IoU > 0.6. | No |
| Q-027 | Install-size budget (R-004 Q-NEW-e): 1 m terrain everywhere is ≈ 39 GB raw. Default in use: 1 m for urban and shoreline tiles, 2 m elsewhere, zstd-compressed. | No |
| Q-028 | Static water level or tides (R-004 Q-NEW-f)? Tidal range MLLW +0.018 to MHHW +1.798 m. Default in use: static MSL +0.969 m NAVD88 for v1; tides become a later module. | No |
| Q-029 | Add `pdal` and `python-pdal` (conda-forge) to `environment.yml` for the lidar first-return DSM used as the independent skyline reference (R-004 Q-NEW-g; M-005.7)? Default in use: add when M-005.7 is first run. | No |
| Q-030 | M1 district tile sets and whether any fictional fault splay may cross a district (R-004 Q-NEW-h). Default in use: GG = i 25-27, j 36-38; SoMa = i 29-31, j 35-36; no fictional faults; emergences on the real offshore San Andreas / Golden Gate fault and the Hayward fault. | No |
| Q-031 | Bay Bridge west-span tower height conflict: 143 m vs 158 m vs 160 m in OSM (R-004 Q-NEW-i). Default in use: exclude these towers from M-005.1(d) until Caltrans drawings are sourced; F-041 modelling uses 160 m (Assumed). | No |
| Q-032 | Save compatibility promise (`expectations/T-001.md`, draft B Q-c): how far back must saves load? Default in use: every schema from v1 loads forever via migration; pre-M3 development saves may be dropped only by founder decision. | No |
| Q-033 | Look of the Balanced chassis (`expectations/T-007.md`): Default in use: industrial-naval (hull-plate armour, exposed hydraulics at knees and elbows, shipyard-built), gunmetal/slate primary with off-white secondary and one small accent, no emblems until a faction identity exists. | No |
| Q-034 | Chassis heights within D-007 (`expectations/T-007.md`): Balanced at 80 m (both drafts). Drafts disagree on the later Heavy Brawler (A: 84-88 m and wider; B: 76-80 m but wider). Recommended: 76-80 m and wider so the two classes share sockets. Not needed for M0. | No |
| Q-035 | Do provisional passes count (`expectations/T-002.md`)? A PASS resting on Assumed thresholds before calibration. Default in use (Assumed): it counts as provisional and is labelled so until the first calibration checkpoint. | No |
| Q-036 | Incomplete metrics at the M0 gate (`expectations/T-002.md`): panel scores and the optional non-flat-terrain walk will be pending. Default in use (Assumed): the gate uses the headless verdict plus the agent panel proxy, and the pending items are listed for the founders. | No |
| Q-037 | Calibration ratings procedure (`expectations/T-002.md`). Default in use (Assumed): both founders rate the same ~10 captures per metric plus anchors, independently; the helper uses the median and reports disagreement. | No |
| Q-038 | Version bump on calibration (`expectations/T-002.md`). Default in use (Assumed): the spec stays `@v1`, each registry change creates revision `v1.N`, and reports name the revision they used. | No |
| Q-039 | Recent buildings missing from the open data: add by hand? (`expectations/T-003.md`). Default in use (Assumed): no invented buildings at M0; candidates listed in `known_gaps.md`. | No |
| Q-040 | Roads at M0 (`expectations/T-003.md`). Default in use (Assumed): ribbons on the terrain with bridge decks separate. | No |
| Q-041 | DataSF-derived heights shipped under PDDL with a City and County of San Francisco credit (`expectations/T-003.md`). Default in use (Assumed): yes. | No |
| Q-042 | Camera impulse envelope and default framing (`expectations/T-004.md`). Default in use (Assumed): impulse cap about 0.5° and 0.5 m, decay about 1.2 s; the 80 m mech fills about half the frame height, low horizon. | No |
| Q-043 | Outlines, light shafts and clustered smoke (`expectations/T-004.md`). Default in use (Assumed): outlines optional and off in M0; shafts and smoke in a later slice. | No |
| Q-044 | Use 3DEP lidar to fill missing building heights, at the cost of some independence of the lidar reference? (R-006 Q-NEW-a). Default in use (Assumed): yes, `height_source = lidar`, reported separately in every lidar comparison. | No |
| Q-045 | Default-height limit path (M-005.4(d)) (R-006 Q-NEW-b, T-003 C5). Default in use (Assumed): ≤ 10 % by area at M0, ≤ 2 % by area from M1 (Assumed). | No |
| Q-046 | V4 framing for the founder scene (R-006 Q-NEW-c). Default in use (Assumed): one view at 257.6° with 75° hFOV, rather than two 60° views. | No |
| Q-047 | Skyline margins against the noise floor (-0.02 IoU, +1 px, floor 0.80) and the vegetation (50 % multi-return) and crane (3 m) parameters (R-006 Q-NEW-d). Default in use (Assumed): as stated, calibrated at the first checkpoint. | No |
| Q-048 | Widen hip extension from -20° to -25° in `mech_frame` for a 5° drive margin at Fr 0.23 with less pre-swing knee bend? (R-007 Q-NEW-a). Default in use (Assumed): no change (case D reaches 33.6 m with the current ROM). | No |
| Q-049 | Arm counter-swing amplitude is a look decision (±25° cancels the legs' fundamental; human-like ±12-15° leaves more for the waist, about ±10° thorax yaw) (R-007 Q-NEW-b). Default in use (Assumed): ±20°, the waist absorbs the rest. | No |
| Q-050 | Nominal duty factor within the registered 0.60-0.72 (R-007 Q-NEW-c). Default in use (Assumed): β = 0.64 (swing 2.38 s); at β ≥ 0.70 hip power and yaw rise above the budget. | No |
| Q-051 | Simulation mass unit: PhysX needs masses scaled (1e5 kg per unit) for contacts on 1e7 kg articulations to work; the rescaling is physically consistent, but without a project-wide declaration a later module can silently mix units (DEF-T005-physics-10) (`evaluations/T-005/eval-physics.md` DEF-T005-physics-10). Default in use (Assumed): one documented conversion at Core level, all logs and contracts in SI. | No |
| Q-052 | Mech independence tolerance (C20): one mech's motion shifts by 2-4 cm when another mech steps at the same time, against a 1 cm criterion; cause not isolated; repeat runs are bit-identical (DEF-T005-arch-2, DEF-T005-physics-9) (`evaluations/T-005/eval-arch.md` DEF-T005-arch-2, `eval-physics.md` DEF-T005-physics-9). Default in use (Assumed): keep the test red, research the solver coupling, decide the tolerance afterwards. | No |
| Q-053 | The M0 walk gate (walk passes M-001 and M-002) is not met after three implementation cycles: the mech walks under pure physics with no assistance, but at Froude 0.023 against a 0.20 target. Research R-008 is establishing whether the body data or the controller is the limit. Options to decide between: another engineering approach (stance-leg impedance around a corrected reference; whole-body inverse dynamics; offline trajectory optimisation; a learned tracking policy), a small declared stabilising assist (ties to Q-014), a change to the body data (for example ankle range), or an interim lower band for M0. (T-005 orchestrator, cycle 3 result; related to Q-014, assisted balance, not a duplicate). Default in use (Assumed): no assist, no threshold or data change; T-005 stays unmerged; research first. | Yes, for the M0 gate |
| Q-054 | Weight read of the first mech passed at exactly 8/10 (C16, DEF-T007-style-2) with no margin; more margin needs wider legs or a higher volume cap (shank and foot volume fractions 1.25 of a 1.30 limit, total volume 9,721 of 10,000 m³) (`evaluations/T-007/eval-style.cycle3.md` ("Not lost", DEF-T007-style-2)). Default in use (Assumed): accept for M0, revisit with the founders' look review. | No |
| Q-055 | Accept a visible torso counter-rotation of about ±20-25° at cruise as part of the mech's look (needed to cancel the legs' vertical angular momentum and keep foot friction within budget)? Default in use (Assumed): Yes, accept it; tune the amplitude with the feel evaluator. | No |
| Q-056 | Margin data changes in `mech_frame`: ankle dorsiflexion to -30° and waist yaw force limit 7.5e8 N·m with ROM ±40°? Default in use (Assumed): No change now; revisit if the stance-impedance cycle shows saturation. | No |
| Q-057 | `style_capture@v1.1` contract gaps: where the haze_cal dark/light pairs live (proposed sidecar `haze_targets`), sequence `viewpoint` for M-004 grouping, whether EXR depth must be supported, and which `resolution` fields may vary per frame (R-002 §6 allows per-frame output offsets, the T-002 validator rejects them). Default in use (Assumed): Publish `style_capture@v1.1`: `haze_targets` in the sidecar, `viewpoint` in sequence.json, offsets may vary per frame and only `internal_wh`, `output_wh`, `integer_scale` must match; until then M-004 sets are captured with sub-pixel compensation off. | No |
| Q-058 | Spec readings of `M-001@v1` and `M-002@v1` fixed by the T-002 tool: limits scaled from header values (0.085 g by (W_f/2)/h, 4.0 °/s² by 42.4/l); one stated floor for M-001.11 (1.5 steps = 4.95 s is below t* 2.4 = 4.99 s, so the step floor never binds); M-001.12 uses (v - 0.3)/a_ref (orchestrator ruling); `gait_log@v1.1` header fields `c_mech_cost`, `F_act_max_N`; contiguous step index; boundary pairs on discrete statistics; per-capture minimum evidence (>= 1 contact per foot, >= 3 steady strides). Default in use (Assumed): As the tool implements them (Assumed); add the two header fields in `gait_log@v1.1`; R-001 to state the end speed of the 3.1 s floor; recalibrate at the first checkpoint. | No |
| Q-059 | Spec readings of `M-003@v1` and `M-004@v1` fixed by the T-002 tool: c2 mean-L* allowance 0.0 as written (R-002's script used -2.0); (b) window anchored at the pixel; C_px with the M-003 (b) interleaved definition (R-002's script used a 4-neighbour mask); c2 gate signed (absolute reading reported beside it, because the signed reading passes haze targets whose contrast inverts); C_px without dither is NOT_RUN and the metric INCOMPLETE, 'enough' dither = 1 % interleaved share. Default in use (Assumed): As the tool implements them (Assumed); R-002 to state which c2 reading is meant; re-judged at the first calibration. | No |
| Q-060 | Which dither anchoring mode is the M0 default? Measured on Unity captures (five profiles, two viewpoints): `direction` meets every yaw threshold but misses translation shimmer and crawl (and T3 flicker by 0.006); `hybrid` meets the translation thresholds (crawl 0.1507 against 0.15 in one sequence) but misses the three yaw thresholds; no mode meets both sets (R-002's table implies the same). Default in use (Assumed): Keep `hybrid` as the reconciled spec says until the founders have seen both in motion; recalibrate the yaw thresholds for surface-anchored dither. Related to Q-016. | No |
| Q-061 | Haze band spacing: log distance (built) or equal transmittance (R-003 §5)? Default in use (Assumed): Log distance with `hazeStartM` 60 m and `hazeFullM` 20 km per profile, judged by the style panel. | No |
| Q-062 | DataSF height match for row houses (T-003): allow a DataSF height when >= 80 % of the OSM footprint lies inside one DataSF footprint (relief > 3 m); would cut the default-height share from 13.3 % to 6.4 % in `L0_30_36`. Related to Q-041, Q-045. Default in use (Assumed): Not applied until the founders approve (Assumed). | No |
| Q-063 | Push height for the balance criteria (C15): forces assume a push at CoM height; a push at torso height is a harder, separate case. Default in use (Assumed): Specify the push height in the criterion: CoM height for the balance test, torso height as a separate, harder case. | No |
| Q-064 | Evidence size: per-step physics logs are 225 MB raw for one task; keep gzipped in the repository or keep out of git with hashes in `EVIDENCE.md`? Default in use (Assumed): gzip in the repository (in use); alternatively git-ignored with hashes in `EVIDENCE.md`, as T-004 already does for bulk captures. | No |
| Q-065 | Joint limits against geometry in `mech_frame_balanced`: R-005 hip adduction -25°, hip internal rotation ±40°, shoulder adduction -10° and external rotation -60° are not reachable from the rest stance without limb-on-limb contact (feet 3 m apart, arms 0.95 m beside the thigh at rest); waist lateral bend ±20° and hip abduction +40° also drive hand or forearm into the thigh (depths 1.6-4.6 m). Default in use (Assumed): T-005 disables ArticulationBody collision between {upper_arm, forearm, hand} and {pelvis, thigh} of the same side and the balance controller adds shoulder abduction to compensate; alternative: narrow shoulder adduction to >= 0°. Assumed. | No |
| Q-066 | Socket placement in config: `mech_frame@v1` has no sockets; the forearm hardpoint offset lives in `Pipelines/blender/mech_frame_balanced/mf_common.py` (`FOREARM_PLATE`). Default in use (Assumed): A `sockets[]` field in `mech_frame@v2` (or a `mech_sockets@v1` file) owned by the frame config and read by the build script and tests. | No |
| Q-067 | Placeholder props in the first scene (T-006): keep plain primitives until real assets exist? Default in use (Assumed): Yes, keep plain primitives until real assets exist. | No |

Q-006 to Q-034 were raised by research and expectation agents; every default listed is `Assumed` and must be confirmed. Source placeholders are mapped in `ledger/ID_MAP.md`. Seven duplicates were merged (cruise Froude, foot shape, reactor power, minimum mass, start/stop/turn feel, internal resolution, target PC/test hardware).

Added 2026-11-14 (scribe, second pass): Q-044 to Q-054. Q-044 to Q-050 are the proposals of R-006 and R-007 (mapping in `ledger/ID_MAP.md`); Q-051 to Q-054 come from evaluations and the orchestrator. One more source was added to an existing question instead of a new number: Q-009 (minimum mass versus height) gains the T-007 finding that the mech's implied density at 10,000 t is about 1,030 kg/m³ (9,721 m³ asset volume), almost exactly sea water, relevant to wading and buoyancy later; default unchanged (note it).

Added 2026-11-14 (scribe, M0 catch-up): Q-055 to Q-067 from R-008, T-002, T-003, T-004, T-005, T-006 and T-007 proposals. Overlaps mapped in `ledger/ID_MAP.md` instead of new numbers: Q-053 (R-008 Q-NEW-a, T-005 Q-NEW-b and Q-NEW-e: M0 gate at Fr >= 0.10, default yes), Q-014 (R-008 Q-NEW-c: assist limited to yaw moment, <= 3 % of ankle limit, Fr >= 0.15, default no assist), Q-051 (T-005 Q-NEW-a), Q-040 (T-003 Q-NEW-b), Q-047 (T-003 Q-NEW-c), Q-042 (T-006 default framing of the first image: mech about half the frame height, tallest tower behind).

---

## §11a. Research findings

Research output is agent output: all rows are `Proposed` until the founders accept them. Detail stays in the files.

| ID | Finding (headline numbers) | File | Status |
|---|---|---|---|
| R-001 | Weight and gait specs for M-001/M-002. Reference mech l = 42.4 m, h_com = 44.8 m, L_f = 19.6 m; walking Fr band [0.10, 0.30], nominal 0.20 (9.12 m/s, stride 60 m, T 6.6 s); CoM acceleration ≤ 0.30 g fore-aft, ≤ 0.085 g lateral; yaw ≤ 26.7 °/s; start 2-4 steps, stop 1.5-3 steps; slip < 0.098 m per contact. Note: §4 says CoM height ≈ 40 m, the anthropometric 0.56 H is 44.8 m (fall energy 4.4 GJ); R-001 proposes §4 cite it (no edit made, founders decide) | [research/R-001.md](research/R-001.md) | Proposed |
| R-002 | Style specs for M-003/M-004 and reference palettes. Tolerance CIEDE2000 ≤ 2.0; native pixel grid 3.1-4.6 px (150-500 px wide), so 480×270 at 1080p; five district profiles of 11-24 colours; references show clustered, not Bayer, dithering (DIG 0.09-0.33); yaw shimmer 0.0025 with direction-locked dither versus 0.51 screen-locked, translation needs surface-anchored dither (0.095 versus 0.245) | [research/R-002.md](research/R-002.md) | Proposed |
| R-003 | Pixel-pipeline survey for URP 17.6 / Unity 6000.6.9f1: Render Graph only, separate reorderable renderer features, dedicated low-res RT with integer k, per-band 32³ palette LUT with Bayer dithering, haze bands at equal transmittance steps; pixel chain ≤ 0.5 ms GPU, geometry is the M-008 risk | [research/R-003.md](research/R-003.md) | Proposed |
| R-004 | GIS sources, CRS, tile scheme, M-005@v1: NOAA CUDEM 1/9″ topobathy with 3DEP 1 m overlay (median seam 0.02 m), OSM NorCal PBF, USGS Qfaults, BayTM CRS (0 to +55 ppm vs UTM -400 ppm), water at MSL +0.969 m NAVD88, 2048 m tiles (`geo_tile@v1`), M0 tile `L0_30_36`; eight M-005 sub-measures | [research/R-004.md](research/R-004.md) | Proposed |
| R-005 | Articulated biped at 10,000 t: 15-link 23-DOF frame (CoM 42.5 m = 0.53 H, hull 1,232 kg/m³), force-type drives with phase-scheduled gains 1e8-1e12 N·m/rad, walking at Fr 0.25 needs ≈ 50-100 MW mean and ≈ 400 MW peak, SIMBICON + DCM control, TGS 1/60 s; §4 checks hold (654 kPa single stance, 4.17 GJ ≈ 1 t TNT); eight M-006 sub-measures | [research/R-005.md](research/R-005.md) | Proposed |
| R-006 | M-005@v1.1 after first use on the M0 tile: shoreline continuity measured on the built surface (p99 excess jump ≤ 0.20 m, from the B23 NVA), default-height share by footprint area with containment matching and parent inheritance, R-ind with class/vegetation/crane/change masks, V4 heading 257.6° at 75° hFOV, noise-floor thresholds; draw calls measured with ProfilerRecorder | [research/R-006.md](research/R-006.md) | Proposed |
| R-007 | Why the first walk misses M-001 and the ranked path to Froude 0.20: the flat-footed knee-flexed gait is ankle-limited to 0.34-0.37 l (measured 0.3 l); heel-to-toe rollover with a pre-swing knee bend reaches 33.6 m (0.80 l) with 5° margin; 4.49 BW and 2,485 MW overshoots are controller-made; Fr 0.20 is reachable with no data or threshold change; two M-006@v1.1 clarifications proposed | [research/R-007.md](research/R-007.md) | Proposed |
| R-008 | Walk gate escalation (Froude 0.023 against 0.20 after three cycles): the controller binds (a flat-footed force-loop stance is capped at Fr 0.005-0.025); with the frame as given, Fr 0.20 is kinematically feasible with 4.9° margin only with human-like rockers (heel strike 25°, toe-off 20-40°) and 10° pelvic tilt (R-007's bounds give -1.8°); stance-foot yaw demand 3.3e8 (Fr 0.10) and 4.5-6.9e8 (Fr 0.20) N·m against a 1.65e8 budget, so the trunk must cancel >= 71 % / >= 81 % of the limbs' vertical momentum (waist 21-23°; R-007's 1.2e8 superseded); recommended: stance impedance around a corrected reference for M0 at Fr >= 0.10, then trajectory optimisation with whole-body QP for 0.17-0.20, learned tracker as fallback, no assist | [research/R-008.md](research/R-008.md) | Proposed |

Disagreements between R files are recorded in the relevant Q entries, not resolved: cruise Froude 0.20 (R-001) versus 0.25 (R-005) in Q-006; foot shape in Q-007; reactor power in Q-008; internal resolution at 4K in Q-018.

R-006 and R-007 were added 2026-11-14 (scribe, second pass); their placeholders are mapped in `ledger/ID_MAP.md` (M-018 to M-020, Q-044 to Q-050).

---

## §12. Change log

- 2026-11-14 · founders + Claude (interview) · D-001 to D-027, F-001 to F-064, M-001 to M-008, Q-001 to Q-005 · initial breakdown
- 2026-11-14 · scribe · R-001 to R-005 (§11a) · registered the five research findings as Proposed, links to files
- 2026-11-14 · scribe · M-001@v1 to M-006@v1 (§10a) · registered versioned metric specs from R-001, R-002, R-004, R-005 as Assumed pending founder calibration; M-007, M-008 have none yet
- 2026-11-14 · scribe · M-009 to M-017 (§10b) · numbered the nine M-NEW proposals from research and T-001 expectations as Proposed (3 marked incomplete); no duplicates
- 2026-11-14 · scribe · Q-006 to Q-034 (§11) · numbered 36 Q-NEW proposals from R-001..R-005 and expectations T-001/T-005/T-007, 7 duplicates merged, disagreements between sources recorded in the entries; mapping in `ledger/ID_MAP.md`
- 2026-11-14 · scribe · F-009, F-043, D-003, D-004, M-008, M-017 · T-001 Core module (L0) integrated (merge 61aee09; eval-arch PASS, eval-perf PASS; dotnet 89/89, Unity EditMode 94/94): typed zero-alloc event bus with same-step FIFO nested delivery and cascade cap, explicit service registry, stable EntityId/BuildingId(source,u64)/PlayerSlot IDs, per-module feature-flag files in Config/FeatureFlags/ (all off), fixed-step clock from Config/CoreSettings.json (0.02 s Assumed), versioned crash-safe JSON save contract with section migrations. Six minor arch and two minor perf defects open for fix cycle 1; the ledger rows stay in-progress (Core part only). See Assets/Game/Core/MODULE.md.
- 2026-11-14 · scribe · R-005, T-001 (Core clock), T-005 · the recommended fixed physics step changed from the Assumed 0.02 s to 1/60 s per R-005 (TGS 1/60 s); T-001 Core still ships 0.02 s in Config/CoreSettings.json (one value) and the change will be applied by T-005
- 2026-11-14 · scribe · Q-035 to Q-043 (§11) · numbered 9 genuinely new questions from the reconciled specs expectations/T-002.md, T-003.md, T-004.md; the other questions in those specs duplicate Q-015, Q-016, Q-017, Q-018, Q-019, Q-021, Q-023, Q-030, Q-031 and were added as extra sources in `ledger/ID_MAP.md`
- 2026-11-14 · scribe · D-003, D-004, F-009, F-043, M-008 (ledger) · T-001 fix cycle 1 integrated (merge f2e71b5; eval-arch.fix1 PASS; dotnet 96/96, EditMode 101/101): DEF-T001-arch-1 to 6 and DEF-T001-perf-1, 2 closed; DEF-T001-arch-7 (nit) open; rows stay in-progress (Core part only)
- 2026-11-14 · scribe · D-007, D-008, D-009, D-022, D-023, D-025, F-001 (ledger), Q-033, Q-034 · T-007 mech frame asset integrated (merge 93ad0b5, cycle 3: eval-style, eval-physics, eval-arch all PASS; dotnet 96/96, EditMode 111/111); all DEF-T007-* closed except DEF-T007-style-11 and -12 (minor); rows stay in-progress ("mech frame asset done"); D-025 moved from todo to in-progress in the ledger because T-007 serves it
- 2026-11-14 · scribe · D-007, D-009 (ledger) · orchestrator rulings on C6, C11 and C27 recorded (appended to `expectations/T-007.md`): a rest-pose hand-to-thigh gap is not a style requirement; C11 shoulder width is measured on torso and shoulder blocks only; the 0.6 front IoU in C27 is "reported, expected", not a gate; cycle 3 was the last fix cycle
- 2026-11-14 · scribe · R-006, R-007 (§11a) · registered the two research findings as Proposed, links to files
- 2026-11-14 · scribe · M-005@v1.1 (§10a), M-006@v1.1 (§10a) · registered `M-005@v1.1` from R-006 (shoreline seam M-005.9, default-height share M-005.4(d), R-ind recipe, V4 reframed) as Assumed pending founder calibration; v1 results stay valid for unchanged sub-measures; R-007's `M-006@v1.1` clarifications recorded as Proposed, not registered
- 2026-11-14 · scribe · M-018 to M-020 (§10b), Q-044 to Q-050 (§11) · numbered 3 M-NEW and 7 Q-NEW proposals from R-006 and R-007 (no overlaps with existing questions); mapping in `ledger/ID_MAP.md`
- 2026-11-14 · scribe · Q-051 to Q-054 (§11), Q-009 (extra source), R-008 (§11a) · four new founder questions from evaluations (PhysX mass unit, mech independence tolerance, M0 walk gate after three cycles, weight read margin at exactly 8/10), all defaults Assumed, none blocking; the implied density of 1,030 kg/m³ at 10,000 t added as a source to Q-009
- 2026-11-14 · scribe · T-002, T-003, T-005 (ledger tasks and defects) · evaluation state recorded without judgement: T-002 eval-arch FAIL (1 blocker, 1 major, 5 minor), eval-style and eval-feel PASS, fix cycle 2; T-003 eval-bay FAIL (4 major, 3 minor), eval-arch and eval-perf PASS, fix cycle 2 finishing the shoreline-seam fix, 4 defects routed to research R-006 / founders; T-005 eval-arch FAIL (2 major, 6 minor), eval-physics FAIL (2 blocker, 7 major, 2 minor), escalated after 3 cycles (walk gate not met: Froude 0.023 against 0.20; research R-008 commissioned; not merged); T-004 implementing, T-006 planned
- 2026-11-14 · scribe · R-008 (§11a), T-005 (ledger) · R-008 reserved as commissioned (no file yet); T-005 escalated after its third cycle (steady Froude 0.023, 3.07 m/s; M-001 3 pass / 6 fail / 3 not run / 1 pending; M-002.1, .2 and M-006.4, .5, .6 fail), not merged, founder decision pending (Q-053)
- 2026-11-14 · scribe · R-008 (§11a) · registered R-008 as Proposed (walk gate escalation; link to `research/R-008.md`), replacing the Commissioned row
- 2026-11-14 · scribe · M-006@v1.1 (§10a) · registered as Assumed (adopted for T-005 round 2, pending founder calibration): R-007's 6.6 and 6.4 clarifications plus R-008's 6.5 passivity reading; the earlier Proposed row is kept
- 2026-11-14 · scribe · Q-055 to Q-067 (§11) · numbered 13 new founder questions from R-008, T-002, T-003, T-004, T-005, T-006 and T-007 proposals, all defaults Assumed, none blocking; overlaps mapped to Q-014, Q-040, Q-042, Q-047, Q-051, Q-053 in `ledger/ID_MAP.md`
