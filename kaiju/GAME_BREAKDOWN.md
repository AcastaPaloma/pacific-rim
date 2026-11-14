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

---

## §11. Open questions

| ID | Question | Blocking? |
|---|---|---|
| Q-001 | Confirm the four Assumed decisions D-024 to D-027 | No (defaults in use) |
| Q-002 | Hangar location: which real Bay site? | No |
| Q-003 | How many kaiju can be active at once in the open world? | Yes, for the performance budget |
| Q-004 | Story framing: how much narrative, if any? | No |
| Q-005 | Pilot representation: on-foot pilots ever, or always inside the cockpit? | No |

---

## §12. Change log

- 2026-11-14 · founders + Claude (interview) · D-001 to D-027, F-001 to F-064, M-001 to M-008, Q-001 to Q-005 · initial breakdown
