# M1 W1/W2 research summary (R-020 to R-026), 2026-11-14

Sources and caveats are in each note. Thresholds marked *Assumed* in a note stay *Assumed* here and go to W7 calibration (R-026 §6).
Commits: R-020 9a1041e · R-021 d434aa9 → b2931c5 · R-022 1e7c4bc · R-023 7ae8c95 · R-024 6809066 · R-025 02abffe → 292656c · R-026 2cc3ce9.
One follow-up each was needed for R-021 (fragility values unsourced) and R-025 (site positions unsourced); both were fixed in the second commit.

## Per brief
**R-020 districts and landmarks (Track 1).** Seven landmark entries are sourced: Salesforce, Transamerica, the Ferry Building tower, the GGB S and N towers, the GGB cables and the GGB deck. Coit Tower (sources disagree, 64 m vs 55.5 m) and the Bay Bridge W2–W6 are excluded.
The `landmarks` layer reuses the `GeoBuildingData` prism path (no interface change); the bridge needs `GeoMeshData` and colliders. GGB facts come from GGBHTD. The cable sag (144 m, from a search snippet) puts the cable 16 m above the deck, which is unverified until the HAER CA-31 sheets are read.
- Recommendation: static GGB collision (4 leg envelope boxes, 40 m deck boxes, cable capsule chain). M-005@v1.2 gets a ≤2 % union default-height share and a seam rule (.10). New M-060 silhouette IoU: ≥0.97 vs the source render, and ≥max(0.85, noise floor − 0.02) vs lidar (*Assumed*).
- Plan impact: 7 of the 14 new tiles are about 60 % or more water (estimated). Tile cache is about 270–365 MB. Bake time is unknown, so T-020 step 1 must time it. T-023 step 1 needs the HAER CA-31 download. Bay Bridge: add L0_31_37 or drop it from M1.

**R-021 damage and persistence (Track 2).** Five Hazus damage states (None to Complete), monotone in M1. Capacity is a pushover-energy model by rise class (W1, C2.L, C2.M, C2.H), using Hazus 6.1 Moderate-Code drift medians (β = 0.4) and Complete collapse fractions, read from the SimCenter `fragility.csv` transcription.
`Cy`, α₂ and the debris coefficients stay *Assumed*. `geo_export@v1` has no structure type or year, so every building in a rise class is identical except for hash scatter.
- Thresholds: M-050 = 0 differing bytes after save/load/save and 0 bit mismatches across one migration. M-051 = max |empirical − analytic| ≤ 0.015 at N = 10,000. M-052 = mass residual ≤ 1e-6, energy residual within [0, 5 %] of ΔPE.
- Plan impact: proposes adding `year_built`, `stories` and `struct_class` to the T-020 bake from the SF Assessor roll (DataSF `wv5m-vpq2`, PDDL; columns not yet confirmed).

**R-022 fault emergence (Track 2).** Mapped traces lie 2.3 km from GG (Golden Gate fault) and 11.6 km from SoMa. Default rupture: offshore San Andreas SF section with the Golden Gate fault, Mw 7.2, rupture length 60 km, maximum displacement 2.4 m, average displacement 1.5 m (Wells & Coppersmith 1994). Alternate: Hayward, Mw 7.0, about 14 km from SoMa.
Shaking uses BSSA14 (coefficients via OpenQuake) plus the Worden 2012 MMI conversion. District Vs30 is unsourced.
- Thresholds: M-053 = p95 ≤ 1.0 m and max ≤ 2.0 m from the baked trace. M-054 = within 1σ of the regressions to pass, 1–2σ to warn.
- Plan impact: none. An offshore rupture leaves no visible scarp; Hayward is the option if a visible scarp is wanted.

**R-023 co-op netcode (Track 3).** Neither NGO nor Netcode for Entities has an official Steam transport, and NGO 3.0 needs Unity 6000.7. Recommendation: a custom engine-free `Net` layer over `ITransport`, with a Steam adapter on Steam Networking Sockets via Facepunch.Steamworks (MIT).
Sync uses snapshot interpolation only: 20 Hz snapshots, a 150 ms buffer, and pixel-snap after interpolation. The handshake must match exactly.
- Thresholds (*Assumed*): M-055 p95 ≤ RTT/2 + 34 ms. M-056 p95 ≤ 1 px, max ≤ 2 px. M-057 ≤ 64 kB/s down and ≤ 16 kB/s up per client at N = 4.
- Plan impact: STACK.md's networking row changes from NGO. The Steam adapter can't run in batch or CI, so it needs a manual two-account test. Two peers in one Unity process need a second Core root beside `CoreRuntime.Root`.

**R-024 scripted brain (Track 4).** Six states (Idle, Approach, Flank, Charge, Retreat, Withdrawn), all expressible as heading, speed, target and action intents, so the brain needs no locomotion. The pack design is based on the wolf-pack and lion role literature; flanking is measured geometrically.
- Thresholds (*Assumed*): M-007@v1 C1 flank commit ≥ 0.60 and ≥ 2× the control. C2 focus ≥ 0.50. C3 thrash ≤ 0.10 and ≤ 3 switches per kaiju-minute. C4 0 information leaks. C5 panel median "smart" ≥ 5 and "cheap" ≤ 3 (of 7). C6 win rate deferred. M-058 brain cost p99 ≤ 2 % of the host budget with 0 B allocation.
- Plan impact: T-032 needs the real seat intent shape (Q-100), and M-008 needs a host-budget figure in ms.

**R-025 hangar (Track 5).** No Bay structure clears 80 m: Hangar One is 198 ft overall. Recommendation: the Crissy Field former airfield, 28 acres of open ground in tile L0_27_36 or L0_27_37 (inside GG, no extra bake), used as an open-air pad; the pad's exact centre is *Assumed*.
- Threshold: M-059 = 0 bitwise differences over 3 fight → hangar cycles × 3 seeds, with a negative control.
- Plan impact: none if an open-air pad counts as a "hangar". The roofed alternative is Treasure Island Building 2, prone pose only, tile L0_31_37.

**R-026 metric registry (Track 6).** 15 registry-ready blocks, with no conflicting metric definitions. M-006@v2 is a proposal derived from R-005 §8 and R-021 (no wave-1 draft existed).
Not headless: M-060, the M-005 game views (they need graphics), M-058 and M-008 absolute timing (Windows, Q-019), and M-007 C5 and C6. Needing no calibration: M-050, M-051, M-054, M-059.
- It lists three contract conflicts without resolving them:
  - C-1: landmark id `landmark:<slug>` (R-020) vs `bld:…:<u64>` (R-021).
  - C-2: step length 1/60 s (R-005) vs Core's 0.02 s.
  - C-3: M-051 compares a PGA grid against drift medians.
- Plan impact: M-060 has no task slot (about +3 steps), so add T-039 or a fourth T-035 step. T-035 to T-038 total 13 steps, as planned.

## Proposed answers to Q-100 to Q-106 (proposals for the founders, not decisions)
- **Q-100 proposed: Yes, option A, integration branch only.** R-023 §4 and R-024 Q-NEW-a both depend on the seat intent shape. Fix C-2 (step length) in the same change, since M-055 and M-006 assume 1/60 s.
- **Q-101 proposed: a custom `Net` layer plus Facepunch.Steamworks over Steam Networking Sockets (R-023), not the NGO prior.** This needs a STACK.md change and a manual Steam two-account test.
- **Q-102 proposed: Yes, static only.** Use conservative envelope colliders until the HAER CA-31 sheets give leg spacing and cable-to-deck distance (R-020 §3).
- **Q-103 proposed: Yes.** Five Hazus states, monotone in M1, with pre-baked meshes per class (R-021 §1).
- **Q-104 proposed: Crissy Field airfield (Presidio), L0_27_36 or L0_27_37, open-air pad (R-025).**
- **Q-105 proposed: Yes.** SoMa fill liquefaction is the largest omission (R-022 §5); keep it as M2 research.
- **Q-106 proposed: Yes, 80 m and 10,000 t.** R-024 and R-025 worked with it; kaiju speed reuses R-001's 9.12 m/s (*Assumed*).

## Founders must decide before W2 implementation (T-020 bake, T-024 damage model, T-029 Net core)
1. Q-101 stack: blocks T-029.
2. C-1 landmark id format and C-2 step length: block T-020, T-024 and T-025.
3. Whether the T-020 bake adds the Assessor fields (`year_built`, `stories`, `struct_class`): this has to be settled before the bake runs.
4. Sign-off on the M-006@v2 proposal (R-026 §2), and C-3 for the M-051 tool.
5. Q-100: needed for W3 T-032 and W6, not for W2 itself.
Wave-1 workers raised Q-NEW questions in each note (R-020 a–e, R-021 a–f, R-022 a–e, R-023 a–g, R-024 a–e, R-025 a–b, R-026 a–h). They are unnumbered; numbering them into Q-107 and later is left to the ledger pass.
