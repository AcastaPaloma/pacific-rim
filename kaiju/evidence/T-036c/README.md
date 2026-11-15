# T-036c: real-input metric runs (M-050..M-054, M-005 --pieces)

Reproduce: `evidence/T-036c/run_metrics.sh` (from `kaiju/`; dotnet + the kaiju conda python). Input: the 15 baked district tiles
(Golden Gate 3 x 3, SoMa 3 x 2; 17,858 building records, 17,670 damageable) and the baked `faults_bay.geojson`.
Scenario: Mw 7.2 on the San Andreas system (hypocentre vertex nearest the Golden Gate reference, seed 20261115, 60.0 km, vertices 447-1327)
through `QuakeDirector` -> `FaultRupture` -> `SeismicShakingBridge` -> `DamageModel` (flags on in the test only); a Hayward Mw 7.2 rupture
(59.9 km) is run on a second director without the bridge so M-053/M-054 see two systems. Damage census (states 0..4): 17019 / 577 / 72 / 2 / 0.

| Metric | Input (dumps/) | Verdict | Key numbers |
|---|---|---|---|
| M-050 save round trip | save_before.json, save_after.json (Core envelope, capture -> restore into a fresh model -> capture) | PASS (0 differing bytes, 0 ids) | 651 damaged ids |
| M-051 fragility | fragility_dump.json (N = 10,000 per class) | PASS | PGA 0.01019 <= 0.015, energy 0.00969, 0 monotone violations |
| M-052 mass/energy | destruction_dump.json (851 buildings, 2 collapsed; 5 archetype transition checks) | PASS (provisional: 1 assumed threshold) | E_frac/dPE = 0.25 at the assumed cap, mass 0 ids, pieces 64 <= 64, path 0 |
| M-053 rupture offset | fault_rupture_dump.json (173 runs, 10,060 samples at 10 m) | PASS (provisional: assumed 1 / 2 m) | p95 6e-12 m, max 1.1e-11 m, mapped share 1.0, slip rel 1.3e-6, hash ok; map-scale uncertainty 25 to 137 m reported |
| M-054 rupture scaling | rupture_dump.json | PASS | 0.0489 log10/M vs 0.56; SA and Hayward Mw 7.20, SRL 59.97 / 59.95 km, MD 2.43 m, AD 1.445 m |
| M-005@v1.2 `--pieces` | district_pieces.json (18,114 pieces) | PASS | clipped_pieces_area worst 3.5e-4 (limit 1e-3), height/base 0.0 |

M-050 and M-052 stay `registered`: their thresholds are seed values and await founder calibration; nothing here makes them gating.
The M-052 energy row sits exactly on the assumed 25 % E_frac cap (T-024's cap, not in R-021), so it is a PASS at the boundary.
Only 2 of 17,670 buildings reach state 3 under this scenario (none state 4): M-052's energy rule is exercised by the archetype transition checks
and 2 real buildings, not by a collapse population. A harder scenario (closer rupture, higher PGA) would need the founder's choice of scenario.

Since the second pass the Seismic input is the production path: `GeoFaultsLoader.LoadGlobal` -> `GeoFaultData.FaultId` -> `PiecesFromGeo` -> `FaultSystem.SectionBakedIds` (test `ProductionPath_EqualsDirectReading_AndKeepsEveryBakedFaultId` proves it equals an independent reading of the geojson). M-053 on production faults: PASS (provisional, assumed 1/2 m), 173 runs, 10,060 samples, p95 6.1e-12 m, max 1.1e-11 m, mapped share 1.0, slip rel 1.3e-6, hash ok; M-005 district stays PASS.

## Finding fixed during this run
First M-053 run FAILED: max offset 20.5 m (p95 0). Cause: the dissolve joins baked pieces within 50 m with a straight chord that lies on no piece
(the polyline sampled every 10 m left the trace there). Fix: `FaultSystem.Chord[]` flags those segments; the exporter ends a run at a chord
(bridges and section changes already did). The chord stays ruptured and shaken (gap far below the 25 to 137 m map-scale uncertainty).

## Unity
`unity/` holds the batch run of `Game.World.Destruction.Editor.DistrictPiecesBatch.Write` and the EditMode run (an `.asmdef` was added); see `unity/status.txt`.

Result: `DistrictPiecesBatch.Write` (batch, exit 0) wrote 18,114 pieces, numerically identical to the dotnet file (same bids and tiles, area diff 0.0), M-005 --pieces PASS on it (`unity/m005_pieces_unity.txt`). EditMode: 487 total, 485 passed, 0 failed, 2 skipped (`unity/editmode.xml`).
