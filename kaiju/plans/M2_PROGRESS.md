# M2/M3 progress (lead orchestrator state; reload from here)

Orchestrator: M2/M3 lead, session 1 (2026-11-15, session `local_8f501ad8-71e5-4377-bc8c-7411583fbe4b`). Founder asleep: §15, every
decision logged in `ledger/HANDOFF.md` under "M2/M3 lead orchestrator, session 1" as "decided autonomously (founder asleep)".
Plans: `plans/M2.md`, `plans/M3.md`. IDs: `ledger/IDS.md` (M2 T-100–T-139 …, M3 T-140–T-159 …).
Integration branch `claude/fable-orchestrator-prompt-83a0a8` (git root = the worktree above `kaiju/`). Never `main`.
Worker rules: side-session notes folder `m2-track-rules.md` (worktree `.wt/<TASK>`, merge lock, Unity < 2, §13/§14, hand-over).
M1 (lead `local_979e0962…`) and T-005 (own orchestrator) are running: never touch their tasks or files.

## How tracks run
Session starts are refused on turns begun by another session, so tracks run as **Agent-tool workers** (Sonnet/medium,
background), one purpose each, launched by the lead. A successor lead re-launches the next step of each track from the
table below. Unity: M1 + T-005 hold the slots; M2/M3 steps are Python/dotnet until a slot frees (check `pgrep`).

## Tracks and next steps (in order; ✓ = merged)
| Track | Step | State |
|---|---|---|
| M2-1 Bay bake | R-100 Bay coverage/sources/storage + T-100 `bay_manifest@v1` | W1 launched |
| | T-101 bulk fetch (background, status files) → T-102 bake → T-103 heights → T-104 landmarks → T-105 infra → T-106 M-005@v2 | pending R-100 |
| M2-2 Streaming | R-101 streaming/LOD research | W1 launched |
| | T-107 `World.Streaming` core (dotnet) → T-108 `bake_lod.py` → T-109 Unity loader → T-110 persistence → T-111 flythrough + metrics | pending R-101 |
| M2-3 Species/pack | R-102 species biomechanics; R-103 pack + M-007@v2 + M-008 Bay scenario | W1 launched |
| | T-112 species specs → T-113 `Game.AI.Pack` → T-114 emergence → T-115 m007_bay; T-116/T-117 staged on T-041/T-005 | pending R-102/R-103 |
| M2-4 Economy | R-104 design spec | W1 launched |
| | T-118 Economy → T-119 Salvage → T-120 Roster → T-121 Refit → T-122 installers → T-123 campaign sim | pending R-104 |
| M2-5 Gate metrics | T-124 M-008@v1 Bay scenario, T-125 calibration plan | after tracks 1–3 |
| M3-1 Build | T-141 Platform contracts + null impl (dotnet) | W1 launched |
| | T-142 attribution + tile copy, T-143 VDF templates | W1 launched (one worker) |
| | T-140 build entry + smoke (needs a Unity slot) | pending slot |
| M3-2 Disclosure | R-120 (+ R-121 capture research, same worker) | W1 launched |
| | T-144 provenance scanner → T-145 disclosure draft | pending R-120 |
| M3-3 Capture | T-146 capture plan → T-147 driver (Unity) → T-148 checks | pending R-121 |
| M3-4 Polish/demo | staged until M2 closes | — |

## Launched workers (session 1)
(filled in as they launch; agent ids are session-local — a successor relies on branches `task/<ID>` and `tasks/<ID>.progress.md`)
W1 (2026-11-15, Sonnet/medium, background): T-100 (R-100 + bay_manifest), R-101, R-102, R-103, R-104, R-120 (+R-121), T-141 (Platform; merges only after a Unity run), T-142 (+T-143).
If the lead hands over before they report: check `git log` on the integration branch for `Merge task/<ID>` and each `.wt/<ID>` / `tasks/<ID>.progress.md`; relaunch any step with no merge and no progress file.
- T-141: task/T-141 01ed286 pushed, dotnet 15/15, NOT merged — needs one Unity run (GameCoreConfig + EditMode) when < 2 batch Unity run; then merge + evidence/regression/T-141.md. Follow-up: composition installer for IPlatformServices.
- T-142/T-143 ✓ merged c7300af (pytest 29, M-125 94/94 on district tiles). Integration push was refused for the worker (not retried by the lead); goes out with the next normal commit.sh push. Follow-ups: copy_tiles assumes bay_manifest {"tiles":[...]} (align with T-100), landmarks.json has no licence field, wire into T-140 build.sh.
- R-120/R-121 ✓ merged a93bb75 (disclosure: pre-generated AI yes, live-generated no — our reading; store sizes in R-120). M-124 99 % share unsourced → T-148 validates via R-121 §6. Launched T-144/T-145 (provenance + disclosure draft) and T-146 (capture plan).

## Hand-over (M2/M3 lead session 1 → session 2, 2026-11-15, §16 80k cap)
- Applied §16 (1a50cb3): rule 17 added to `m2-track-rules.md`; hygiene sweep done (no duplicate/stale M2 sessions; the M1 family is the M1 lead's to sweep).
- Still running as this session's Agent workers (they merge themselves; check `git log --oneline --since=4.hours | grep 'Merge task/'`): T-100 (R-100 + bay_manifest), R-101, R-102, R-103, R-104, T-144/T-145, T-146. A step with no merge and no `tasks/<ID>.progress.md` after ~2 h is relaunched.
- Waiting on a Unity slot (< 2 batch): T-141 — run GameCoreConfig + EditMode on `task/T-141`, then merge.
- Next to launch, in order, as each input merges:
  1. after T-100: T-101 bulk fetch driver (background, status files; check `df -h /` first) and align `Pipelines/build/copy_tiles.py` with the real `bay_manifest@v1` shape;
  2. after R-101: T-107 `World.Streaming` contracts + engine-free scheduler (dotnet) and T-108 `bake_lod.py`;
  3. after R-102: T-112 species specs + `Units.Kaiju` contracts; after R-103: T-113 `Game.AI.Pack` (new assembly) and T-115 M-007@v2 tool;
  4. after R-104: T-118 `Meta.Economy` → T-119 Salvage → T-120 Roster → T-121 Refit (one worker each, dotnet; new asmdefs need one batched Unity run);
  5. after T-144/T-146: T-147 capture driver when a Unity slot frees; T-148 checks.
- Batch Unity: T-141 + the Meta asmdefs in one run when a slot frees (§13).
- Register new metrics/R in GAME_BREAKDOWN via the scribe once R-100–R-104 land (one Sonnet/low scribe pass).
- Update at hand-over: merged R-101 (e6d4ddb), R-103 (cd0e928), R-104 (9fd8399). Launched T-118 (Meta.Economy) and T-107 (World.Streaming core); both may stop at "awaiting Unity slot". Still running: T-100, R-102, T-144/T-145, T-146.
- R-104 salvage headline (0.60 / 0.30) disagrees with its own computed 0.67 / 0.34: T-119 aligns it. R-103 species speeds are assumptions until R-102; T-113 starts after R-102 merges. Q-NEW placeholders in R-103/R-104 are numbered by the scribe pass (Q-208+).
- Successor session start was refused by the permission classifier; left as a proposal card "M2/M3 lead orchestrator, session 2".
- T-146 ✓ merged abf89ce (capture plan: 22 shots, 5 capturable now). Docs only, no regression note needed. T-147 converts landmark lat/lon → BayTM; confirm 480×270 internal size with the style track.
- R-102 NOT merged: task/R-102 300e56e pushed (3 species: STRIDER 60 m/4,190 t bait-flank, BREACHER 100 m/35,000 t flank-breaker, BASTION 160 m/163,900 t anchor). Worker's HANDOFF.md conflict resolution was denied by the permission classifier, merge aborted, integration clean. Lead did not redo it on the worker's behalf; needs the founder's OK (or a fresh merge whose conflict resolution is permitted). T-112/T-113 wait on it.
- T-144/T-145 ✓ merged 081b27a (+6b270a8 marker fix). pytest 37. **M-120 FAIL 92.31 %** (12 unknown: SampleScene, landmarks.json, 4 TutorialInfo template files, 6 art/style_refs) and **M-126 FAIL 1 hit** (an OSM venue name in L0_29_35 buildings.geojson). Thresholds unchanged. Next step T-144b: style_refs are not shipped (D-022) → exclude via the T-140 build manifest, not an allow-list; TutorialInfo + SampleScene excluded from the player build; landmarks.json gets a data-derived record (licence LicenseRef-facts-cited, also closes the T-142 gap); OSM `name` tags are not player-visible → bake drops them from shipped tiles (kept in cache) so shipped text has zero hits. Disclosure draft at docs/store/ai_disclosure_draft.md (DRAFT, founder action).
- T-100 ✓ merged 1b36ee7: 4311 tiles (urban 1048, rural 2006, water_only 149, ocean_only 1108, outside 313); ~40 GB static + 40–100 GB lidar, ~86 GB baked. Gaps: 1160 tiles without CUDEM (702 land → 3DEP alone; 458 ocean → bathymetry source open, R-100 Q-NEW-b), 82 building tiles outside EPT (T-103 tries TNM point clouds). Launched T-101 (CUDEM version fix + fetch_bay.py + background osm/water/cudem/3dep fetches into the integration cache; lidar waits for T-103). Align Pipelines/build/copy_tiles.py with bay_manifest@v1 in T-102.

## Session 2 (M2/M3 lead, 2026-11-15 ~02:00, Agent workers; session starts refused on this session-begun turn)
- Checked session-1 workers: T-101 (uncommitted fetch_bay.py, active 01:56), T-107 (e880e0b, Unity run in progress 01:57), T-118 (c1ca4b8, EditMode 610/612 passed 0 failed) — all still alive, they merge themselves; not relaunched. Disk 436 GiB free. T-144–T-146 already merged.
- Launched W2 (Sonnet/medium, background): **T-112** species specs + `Units.Kaiju` + M-103 check; **T-113** `Game.AI.Pack` (Contracts only, own minimal species-role input; adapter to Units.Kaiju is a follow-up); **T-108** `bake_lod.py` (new files only, not bake.py/fetch.py while T-101 edits them); **T-115 step 1** M-007@v2 tool on synthetic traces (m007_bay runs wait for T-113).
- Held: T-144b (touches bake: OSM name drop) until T-101 merges; T-119 until T-118 merges; T-141 Unity run still waiting on a slot (Unity batch count was at/above 2).
- Next on each wake: T-118 merged → T-119 Salvage (aligns R-104 0.60/0.30 vs 0.67/0.34); T-101 merged → T-102 bake (+copy_tiles alignment) and T-144b; T-107 merged → T-109 loader (Unity slot); T-113 merged → T-115 runs + T-114 emergence; T-112+T-113 merged → Units.Kaiju→Pack adapter.
- T-118 ✓ merged b915ddf (dotnet 31/31, Core 105/105, EditMode 610/0/2, GameCoreConfig regenerated). Open: Composition wiring (price file load + SaveSystem registration — composition step after T-121, T-122), IAssetValueProvider adapter, airport/rail rows; fee/advance multipliers Assumed (T-123 tunes). Launched T-119 Salvage (also corrects R-104 salvage fractions). Next: T-120 Roster after T-119, then T-121 Refit.
- Update (from session-1 starter): T-118 ✓ merged b915ddf; session 1 launched T-119 Salvage itself. Session 1 launches nothing more; its T-101, T-107, T-119 workers merge themselves — track, relaunch only if their output shows they died.
- T-107 ✓ merged 9197ea2 (dotnet 36/36, 75 km traversal peak 3563 m < 4096, EditMode 646/0). Its GameCoreConfig.asset conflict was hand-resolved → regenerated in batch run M2-batch-1 (with T-141 merge). Launched: M2-batch-1 (T-141 + CoreConfig regen + EditMode) and T-109 Unity loader. T-110 → T-111 follow T-109.
- T-141 ✓ merged 14efaea (EditMode 652/0/2 skipped; Platform 15/15, Core 105/105; GameCoreConfig regenerated: +1 Platform flag guid, 0 duplicates — T-107 hand-edit cleaned). batch-1 logs are summaries (duplicate chain overwrote raw output). Follow-up: composition installer for IPlatformServices.
- T-113 committed ffed28a (dotnet 13/13), not merged → Unity + merge worker launched. Follow-ups (track 3): Units.Kaiju→SpeciesRole adapter, IInfrastructureSource adapter, CommsModel (R-103 4.1), Bay route planner, brain_log@v2 PackLog, PackBrainFactory in Composition. Role-fit/mech-value weights are R-103 assumptions (Q-NEW-d).
- T-115 step 1 ✓ merged eb01dfa (pytest 29/29; m007_bay runs wait for T-113 harness; C5 needs T-125 panel, C6 pending T-041). T-112 e90fac9 not merged: pytest 17/17, dotnet 19/19, M-103 PASS ×3; needs Unity (EditMode + CoreConfig) — Unity contention 5–24 batch procs.
- **T-119 blocker** (b832dfa, Salvage 25/25): `meta.salvage` in Meta.json breaks `Meta.Economy.Tests` `Flag_FileDefaultsOff` (asserts one "meta." key). Worker's test edit was refused by the permission check → NOT applied by the lead (no routing around). Next lead: move the flag to its own `Config/FeatureFlags/Meta.Salvage.json` (the per-module convention: AI.Pack.json, Units.Kaiju.json, Platform.json), which needs no test change; same for T-120/T-121. Then Unity run + merge.
- Still running at hand-over (Agent workers of session 2; die with this session if it ends): T-108, T-109, T-113 Unity+merge, T-112 (polling for a slot). Session 1: T-101. Relaunch any with no merge/progress after ~2 h.
- T-113 ✓ merged 2bd8ad7 (EditMode 652/0/2 skipped, AI.Pack 13, AI 42, Core 105; GameCoreConfig has the Game.AI.Pack guid once, 0 duplicate guids). Two lead workers overlapped in .wt/T-113 (lead's fault: double dispatch); results consistent, confirm in the next batched EditMode on integration head. T-101 progress note merged ad09e4a (background fetches running; see tasks/T-101.progress.md for PIDs).
- T-109 ✓ merged 80c8630 (EditMode 663/0/2 skipped, Streaming.Host 7/7, WorldStreaming 36/36). Gaps: geo_lod@v1 reader for L1–L4 waits on T-108; one render origin only (per-cluster PhysicsScene → T-111); StreamingHost not wired in Composition (lead's call, later installer task). Next: T-110 journal flush on TileEvicted, then T-111.
- T-108 ✓ merged 1ad13e0 (pytest geo 165 passed; 13 district pyramid tiles, byte-identical reruns). **Budget miss, not relaxed:** terrain 150k-tri cap met on 1/8 L1, 1/3 L2, 0/1 L3 urban tiles at nominal RTIN threshold; B1 ~100k target 7/8 L1. Lead decision: T-108b pre-filters 1 m lidar noise (edge-preserving, error-bounded vs M-102) before RTIN, rather than raising thresholds; re-measure against the same caps. Also next: T-109 geo_lod@v1 reader for L1–L4 + C# LodSpec validator mirror; cliff/pier max error ~20 m stays a tracked item for M-102.
- T-119 ✓ merged eae5dea (Meta.Salvage 25/25, Meta.Economy 31/31). Blocker fixed without touching EconomyTests: `meta.salvage` moved to `Config/FeatureFlags/Meta.Salvage.json`, Meta.json economy-only, Salvage test reads the new file; GameCoreConfig regenerated (also picks up AI.Pack).
- T-112 ✓ merged eae5dea (Units.Kaiju 19/19, EditMode 663/0/2 skipped, `evidence/T-112/`). Also fixed a Unity compile break from T-041b: `Vector.Sum` is not in Unity's profile, replaced by a lane loop in MlpPolicyNetwork (AI 48, AI.Pack 13 green). Note: EditMode with `-nographics` fails 10 PixelPipeline tests (RenderTexture); run without it.
- T-108b ✓ lidar pre-filter (bilateral, clamped to 0.5 x RTIN thr) in geo_lod bake: terrain tris -1..22 %, 150k cap met 4/13 (was 3/13), error rms +0.002..0.14 m; caps/thresholds unchanged. Cap still fails on 9 tiles (L1_13_18 757k -> 679k): structural, next = ground/canopy split or T-111 M-102 measurement; see evidence/T-108b/ and HANDOFF.
- T-110 ✓ merged a29babc (dotnet Host 20/20, Streaming 36/36, Geo 73/73; Unity EditMode awaiting Unity slot, asmdef refs added). `StreamingJournal` + `streaming@v1` + flush on TileEvicted + crater/removal replay in `GeoL0TileReader`; LOD-form replay waits for the geo_lod@v1 reader.
- T-108c ✓ ground-only L1+ far field (footprint harmonic fill, one-sided, clamped to height_m+1 m): terrain 150k cap met 6/13 (was 4/13); urban tiles fixed, seabed tiles (5) are bathymetry-dominated, 2 hill tiles close (285k, 160k); no cap/threshold changed; evidence/T-108c/.
- T-108c next: T-111 M-102 pixel error at rtin_threshold_for_cap_m + lead budget; seabed depth rule undecided; geo pytest 183 pass / 6 skip (rebake tests skip without fetch cache, T-108b failures fixed).
