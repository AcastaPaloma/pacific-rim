# ID_MAP.md: placeholder to real ID

Written by the scribe on 2026-11-14. Translates the `M-NEW-*` / `Q-NEW-*` placeholders in research and expectation files to the real IDs now in `GAME_BREAKDOWN.md` (§10b for M, §11 for Q). Placeholders are per file: `Q-NEW-a` in R-001 is not `Q-NEW-a` in R-002. Where several placeholders map to one ID, they were merged duplicates. The R-### rows are in §11a; the M-###@v1 specs in §10a.

| Source file and placeholder | Real ID |
|---|---|
| research/R-001.md M-NEW-a | M-009 |
| research/R-001.md M-NEW-b | M-010 |
| research/R-002.md M-NEW-a | M-011 |
| research/R-002.md M-NEW-b | M-012 |
| research/R-003.md M-NEW-a | M-013 |
| research/R-003.md M-NEW-b | M-014 |
| research/R-004.md M-NEW-a | M-015 |
| research/R-005.md M-NEW-a | M-016 |
| expectations/T-001.md M-NEW (Core bus cost; draft A M-NEW-a) | M-017 |
| research/R-001.md Q-NEW-c; expectations/T-005.md Q-NEW (cruise pace) | Q-006 |
| research/R-001.md Q-NEW-a; expectations/T-005.md Q-NEW (foot shape) | Q-007 |
| research/R-001.md Q-NEW-b; research/R-005.md Q-NEW-b | Q-008 |
| research/R-005.md Q-NEW-a; expectations/T-005.md Q-NEW (minimum mass) | Q-009 |
| research/R-001.md Q-NEW-d; expectations/T-005.md Q-NEW (sluggishness versus weight) | Q-010 |
| research/R-001.md Q-NEW-e | Q-011 |
| research/R-005.md Q-NEW-c | Q-012 |
| research/R-005.md Q-NEW-d | Q-013 |
| expectations/T-005.md Q-NEW (assisted balance) | Q-014 |
| research/R-002.md Q-NEW-a | Q-015 |
| research/R-002.md Q-NEW-b | Q-016 |
| research/R-002.md Q-NEW-c | Q-017 |
| research/R-002.md Q-NEW-d; research/R-003.md Q-NEW-b | Q-018 |
| research/R-003.md Q-NEW-a; research/R-003.md Q-NEW-d | Q-019 |
| research/R-003.md Q-NEW-c | Q-020 |
| research/R-003.md Q-NEW-e | Q-021 |
| research/R-003.md Q-NEW-f | Q-022 |
| research/R-004.md Q-NEW-a | Q-023 |
| research/R-004.md Q-NEW-b | Q-024 |
| research/R-004.md Q-NEW-c | Q-025 |
| research/R-004.md Q-NEW-d | Q-026 |
| research/R-004.md Q-NEW-e | Q-027 |
| research/R-004.md Q-NEW-f | Q-028 |
| research/R-004.md Q-NEW-g | Q-029 |
| research/R-004.md Q-NEW-h | Q-030 |
| research/R-004.md Q-NEW-i | Q-031 |
| expectations/T-001.md Q-NEW (save compatibility; draft B Q-c) | Q-032 |
| expectations/T-007.md Q-NEW (look of the Balanced chassis) | Q-033 |
| expectations/T-007.md Q-NEW (chassis heights within D-007) | Q-034 |
| expectations/T-002.md Q (provisional passes) | Q-035 |
| expectations/T-002.md Q (incomplete metrics at the M0 gate) | Q-036 |
| expectations/T-002.md Q (calibration ratings) | Q-037 |
| expectations/T-002.md Q (version bump on calibration) | Q-038 |
| expectations/T-003.md Q (recent buildings missing from open data) | Q-039 |
| expectations/T-003.md Q (roads at M0) | Q-040 |
| expectations/T-003.md Q (DataSF-derived heights under PDDL) | Q-041 |
| expectations/T-004.md Q (camera impulse envelope and default framing) | Q-042 |
| expectations/T-004.md Q (outlines, light shafts, clustered smoke) | Q-043 |
| expectations/T-003.md Q (M0 tile and tile scheme) | Q-030 (extra source; tile scheme) |
| expectations/T-003.md Q (budgets for a tile) | Q-019 (extra source; target PC) |
| expectations/T-003.md Q (Bay Bridge west tower height) | Q-031 (extra source) |
| expectations/T-003.md Q (fictional fault through downtown) | Q-030 (extra source; no fictional faults) |
| expectations/T-003.md Q (OSM share-alike) | Q-023 (extra source) |
| expectations/T-004.md Q (dither look) | Q-015 (extra source) |
| expectations/T-004.md Q (translation shimmer) | Q-016 (extra source) |
| expectations/T-004.md Q (rotation quantisation) | Q-021 (extra source) |
| expectations/T-004.md Q (4K internal resolution) | Q-018 (extra source) |
| expectations/T-004.md Q (profiles: five versus four) | Q-017 (extra source) |
| expectations/T-004.md Q (target PC for M-008) | Q-019 (extra source) |

Not placeholders but also registered: `R-001` to `R-005` (the file's own ID), `M-001@v1` to `M-006@v1` (specs; the proposals named `M-003@v1`, `M-004@v1`, `M-005@v1`, `M-006@v1` in the research files), `gait_log@v1` as M-010, `physics_log@v1`, `palette_set@v1`, `style_capture@v1`, `geo_tile@v1`, `mech_frame@v1` stay file-level data contracts owned by the file that defines them.

Added 2026-11-14 (scribe): Q-035 to Q-043 from the reconciled specs `expectations/T-002.md`, `T-003.md`, `T-004.md`; the rows marked "extra source" are duplicates of an existing Q and create no new number.
