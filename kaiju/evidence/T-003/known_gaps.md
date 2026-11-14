# T-003 known gaps (L0_30_36)

No building was invented at M0 (brief, D-021 default: "no invented buildings"). The open sources (OSM snapshot
2026-11-13T21:22Z, DataSF 2010 heights) miss or under-describe some structures. This file lists what is known to be
missing or simplified, with the evidence that found it. Candidates for F-041 hand-crafting go to the founders.

## 1. Structures seen by lidar but not covered by any OSM footprint
Method (`Pipelines/geo/known_gaps.py`, output `known_gaps_candidates.json`, 54 candidates): 3DEP CA_SanFrancisco_B23
first-return DSM minus the bare surface > 15 m, connected area >= 300 m^2 with median > 15 m, more than 6 m from every
OSM footprint, not over water. Candidates include trees (Pioneer Park on Telegraph Hill), cranes, construction, the
Bay Bridge structure, and parts of towers whose OSM footprints are drawn smaller than the lidar outline. Not checked
by hand; a reviewer should look at each before anything is modelled.

| BayTM x, z | lat, lon | area m^2 | median height m | max m | likely |
|---|---|---|---|---|---|
| 63027.8, 73776.0 | 37.787302, -122.389506 | 3592 | 45 | 58 | Bay Bridge approach / anchorage structure |
| 63139.8, 73907.2 | 37.788485, -122.388236 | 3052 | 53 | 78 | Bay Bridge approach deck (not drawn at M0) |
| 62389.6, 74131.5 | 37.790499, -122.396756 | 1077 | 130 | 208 | tower edge or unmapped tower near Mission/Fremont: review |
| 62492.7, 74248.5 | 37.791554, -122.395586 | 585 | 136 | 235 | tower near Mission/Beale: review |
| 62493.8, 74026.9 | 37.789558, -122.395571 | 327 | 232 | 248 | 181 Fremont area (OSM has "181 Fremont Tower"; outline vs lidar differs): review |
| 62210.4, 74105.1 | 37.790260, -122.398790 | 798 | 94 | 176 | Market/2nd towers: review |
| 61562.9, 75447.6 | 37.802349, -122.406158 | 3137 | 20 | 27 | trees around Coit Tower (Pioneer Park) |
| 62154.7, 74156.3 | 37.790720, -122.399423 | 483 | 126 | 185 | review |
| 62725.0, 74111.5 | 37.790322, -122.392948 | 732 | 73 | 89 | review |
| 63349.1, 74132.2 | 37.790514, -122.385862 | 698 | 72 | 162 | Bay Bridge W2 cable/saddle structure around the tower |
| 61907.5, 74661.2 | 37.795267, -122.402235 | 1813 | 27 | 79 | Transamerica Redwood Park trees + spire edge |
| 62150.4, 74551.3 | 37.794279, -122.399476 | 641 | 75 | 167 | review |
| 62325.1, 74447.5 | 37.793346, -122.397492 | 456 | 100 | 174 | review |
| 61802.4, 75214.6 | 37.800252, -122.403436 | 1250 | 29 | 44 | trees on Telegraph Hill slopes |
| 62245.4, 74245.0 | 37.791520, -122.398394 | 316 | 108 | 154 | review |

These differences are also why the independent skyline check (M-005.7 vs R-ind) fails: see `m005_report.json`.

## 2. Simplified at M0 (by rule, documented)
- **Bridge decks and tunnels**: 66 OSM bridge ways (including the Bay Bridge, `osm:w8921938`) are flagged `bridge = 1`
  and not drawn: the data has no deck heights. The W2 tower stands alone in the water. F-041 hand-crafts the bridge.
- **Roof shapes**: flat roofs except pyramidal/spire/cone with `roof:height`; max(y) always equals base + `height_m`.
- **Height fallbacks**: 334 buildings (13.3 %, 10.1 % by area) use the 3-level default (12.66 m); the R-004 DataSF
  rule (IoU > 0.6) rejects row houses that OSM splits from one DataSF block. A containment rule would lower this to
  6.4 % (proposed, not applied; see `t003.C5_default_share_mitigation`).
- **Stale heights**: DataSF heights are from 2010; they are used only where OSM has no height and the footprint matches.
- **Pier edges** are stair-stepped at 1 m (heightfield); deck heights come from the documented pier rule.
- **Ferry Building clock tower**: OSM part gives 83.1 m above base (reported, not gated; R-004 calls it unsourced).
- **555 California**: OSM parts give 237 m above base (outline tag 226 m); reported only (conflict, R-004 §5).
- **NGS benchmarks**: 29 marks in the tile, 0 qualify (SCALED positions or structure settings), so M-005.2 gates on
  the pipeline check; the median |delta| over all 29 marks is reported as information only.
