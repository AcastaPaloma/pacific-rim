# Regression: M1 merge 3 (T-021 step 3, T-036 step 2, T-020b), 2026-11-15

Merges: 957d203 (T-021 step 3), d13f93c (T-036 step 2), d283806 (T-020b re-bake); fix e387d5a (M-005@v1.2.yaml quoting, registry list test).

| Suite | Result |
|---|---|
| dotnet WorldGeo | 59/59 |
| pytest Pipelines/geo | 99/99 |
| pytest Tools/Metrics | 415/415 |
| Unity EditMode (integration, after `CoreConfigAsset.CreateOrUpdate`, asset unchanged) | 436 passed, 0 failed, 2 skipped of 438 |
| M-005@v1.2 district (T-035 tool on the re-baked tiles) | FAIL only on default-height share: union 0.0218 (limit 0.02; was 0.0992); per tile fails L0_25_38 0.84, L0_26_38 0.75, L0_27_37 0.17, L0_26_36 0.06; L0_27_38 0.81 reported. Seams, edge gap, ownership, clipped area, collider overlap PASS. `evidence/T-020b/m005_district_after_fill.txt` |
