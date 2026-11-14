VERDICT: FAIL

Scope: C3, C8, C9, C10 judged from the listed evidence only. No Unity, no re-measurement.
FAIL rests on one item: the tile-unmodified hashes in build_log.json do not match the committed T-003 manifest (DEF-1).

| ID | Severity | Criterion | Text |
|---|---|---|---|
| DEF-T006-bay-1 | major | C8 | build_log `baked_files` reports terrain 2d3f79eb..., buildings 7cbb4072..., water 4f4d8695..., each labelled equal to "manifest_sha256". The committed evidence/T-003/tiles/L0_30_36_manifest.json lists terrain.tif e2f546a1..., buildings.geojson 4ce3c0b4..., water.png 945546a5.... None of the three build_log values occurs in that manifest. The check compares the file to a manifest that the code reads at run time (ctx.ManifestLayer), so it is internally consistent but does not tie to the committed T-003 manifest. Either the tile on disk was rebaked after the manifest was committed, or the build used a different tile. "Tile unmodified" is therefore unproven against the cited baseline. Reconcile before accepting. |
| DEF-T006-bay-2 | minor | C8 | non_unit_scales is empty (pass), but the mesh hashes (terrain, seabed, buildings, water) are only recorded. They are not compared with baked-derived expected values, so mesh-level "unmodified" rests on the file hash alone, which DEF-1 undermines. |
| DEF-T006-bay-3 | minor | C3 | Feet are not both on the seabed. Sole y -4.146 sits on the higher foot's seabed; the other foot's seabed is -4.428, so it floats 0.28 m. The sole is 0.37 m above the data at the foot centre (-4.511). Within plausibility, but "feet on the seabed" holds for one foot only. |
| DEF-T006-bay-4 | minor | C3 | Depth is reported as 5.48 m at the foot centre (water 0.969 minus data -4.511). The site search chose 5.54 m. The two differ by 0.06 m, within the 0.25 m tolerance. The waterline-between-ankle-and-mid-shin check was not done (admitted in EVIDENCE). |
| DEF-T006-bay-5 | minor | C9 | Transamerica error 6.2 px against the 3 px limit. The build's own note says flat prisms with no pyramid roof model. The error is partly modelling, not geography. It remains an unmet spec and is correctly shown as a miss. Not a wrong geography claim. |
| DEF-T006-bay-6 | minor | C9 | The IoU 0.991 depends on an assumed height convention (base_navd88 + height_m) and on dropping parent outlines of building:part groups (0.946 with parents kept). The Salesforce top 330.892 = 4.892 + 326 matches shot_params, which supports the convention. The parent choice is picked post hoc to clear 0.98, and the evidence says "appears to use the parts". Sound but not verified against the renderer's own mesh list. M-005 tile values are not quoted (stated "not quoted"). |
| DEF-T006-bay-7 | minor | C10 | shot_params.json commit field is "placeholder" by design. The spec requires the commit recorded there. The commit lives only in per-capture sidecars. |

Per-criterion
- C3 PARTIAL: mech 80.00 m, scale 1, depth 5.48 m reported from the heightfield (data sample, not edited), but cue contrast (figures under 10 L*) fails, boat is outside 1-3 px, and the waterline check is missing.
- C8 FAIL: mech scale 1 and height pass and non_unit_scales is empty, but the baked hashes do not match the committed manifest (DEF-1).
- C9 PARTIAL: IoU 0.991 passes (>= 0.98) with the assumptions above; Salesforce 2.2 px passes; Transamerica 6.2 px fails; Coit Tower is off frame.
- C10 PASS (with minor DEF-7): 100/100 files bit-identical across runs A and B after the warm-up fix; the first run without warm-up differed (disclosed). The CLI commands are listed.

Judgements asked for
- 5.48 m off the Embarcadero: plausible. The site is 151 m offshore, in the CUDEM topobathy merged tile, and the real bay shelf there is a few metres deep. It is a data-derived number, so it is reported, not endorsed beyond the tile.
- Ferry Building position "estimated from the tallest-building anchor plus an offset": acceptable, not a defect. It is used only to place a search window. The site is chosen by depth and offshore distance. The estimate is disclosed in build_log. The mech's claim "off the downtown waterfront" holds by its BayTM position, not by the estimate, but the window was relaxed to search pass C (a disclosed fallback).
- Height convention: sound (see DEF-6), with the caveat that the evidence documents it as "assumed".

## Orchestrator ruling (2026-11-14, after cycle 1)
The major defect (baked hashes match no entry in `evidence/T-003/tiles/L0_30_36_manifest.json`) is a stale evidence copy, not a modified tile: that file was last written in 9c129a9, before T-003's fix-cycle-2 rebake (e58a282, 20c8353). The shipped manifest `Assets/StreamingAssets/Geo/tiles/v1/L0/30_36/manifest.json` (20c8353) lists exactly the hashes in `build_log.json` (terrain 2d3f79eb…, water 4f4d8695…, buildings 7cbb4072…), `shasum -a 256 terrain.tif` = 2d3f79eb…, the build runs with `VerifyHashes = true`, and `git diff f4c2371..HEAD -- Assets/StreamingAssets` is empty. Ruled: C8 tile-unmodified PASS; the stale copy is logged as a minor T-003 evidence defect (DEF-T003-evidence-stale-manifest). The six minor defects stand.
