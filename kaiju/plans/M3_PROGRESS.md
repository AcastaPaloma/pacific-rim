# M3 / render / audio lead progress (lead session 1, 2026-11-15)

Scope: pixel look (T-180..T-185), audio wiring, M1 slice polish, M3 groundwork. Rules: ledger/AGENT_PROTOCOL.md §13-§18; worker rules `m2-track-rules.md` in the side-session notes folder.
IDs used: T-187..T-190 (inside the T-160..T-199 gap block, Rendering/Audio gaps), T-140/T-147/T-148 from plans/M3.md.

## Already merged before this lead
T-141 platform null impl, T-142/T-143 attribution + SteamPipe templates, T-144/T-145 provenance + AI disclosure draft, T-146 capture plan, T-180/T-181a-c/T-182/T-184 pixel layer slabs (C11 exact 0.96/0.96/0.95 px; near edge 124.2 vs 123.49 m still 4.58 px).

## Queue
| ID | Task | Unity | State |
|---|---|---|---|
| T-187 | Pixel: near-edge depth (C11 4.58 px) + T-185 C7 contrast at 1 km, full thresholds | yes | dispatched W1 |
| T-188 | Audio: wire Game.Audio into slice scene behind flag (footfall, impact, structural failure, quake, weapon) + metric | yes (1 run) | dispatched W2 |
| T-148 | Capture frame checks + encode + sheets (Python) | no | queued (W2 after T-188 or next free) |
| T-140 | Build entry + manifest + build.sh | yes | queued |
| T-147 | capture_path@v1 + Unity driver | yes | queued (after T-140) |
| T-189 | Slice scene polish on placeholder mech (combat, destruction, seismic, kaiju brain, hangar flags) | yes | queued |

## Log
- T-187 ✓ merged 01cf79c (C11 0.96 px near/ref/far via rig-supplied near edge; C7 1 km passes 5 profiles, layers on/off identical; Q-260 decided). Open: Units.Mech near-subject bound, seam report on moving layered sequences.
- T-148 ✓ merged 845723a (Pipelines/capture, pytest 25). Open: matrix/lint need T-147 masters.
- T-140 BLOCKED (task/T-140 460e7a3 pushed, Python 43 pass, PlayerBuild.cs never compiled/run): disk 13 GB free (<25 GB rule). /tmp/kaiju_t115e_logs is 34 GB, owned by another worker; not deleted. Resume: free disk, APFS-clone Library, `Tools/Build/build.sh --repro --out /tmp/kaiju-build/t140`, fix, merge.
- Integration branch pushes of T-187/T-148 merges denied by classifier; not routed around.
- T-188 audio running. Unity-needing items (T-147, T-189, T-140) held until disk >= 25 GB.
- T-188 ✓ d5a7239 (audio.slice adapter; capture metrics evidence/T-188). Open: combat events unfed (T-189), no listening test, T-005 foot contacts.
- T-140 ✓ 4eaeb16 (M-121 PASS, smoke PASS, M-125 94/94). Open: two-process repro, Windows (Q-231), signing.
- T-147 ✓ 8bd3ae3 (capture_path@v1 driver + lint + matrix; 26 frames; M-123 scale 26/26). Findings: (1) M-003.c1 depth-band lightness monotonic and M-003.e sky/horizon contrast FAIL on 4/5 real Bay frames -> T-190 (fix the look, no relaxing). (2) M-124: yuv420p fails the 99% gate even at CRF 0 (86%); yuv444p passes from CRF 6. Decided autonomously (founder asleep): master and trailer encode = yuv444p H.264 High 4:4:4; M-124 threshold unchanged; if Steam's player rejects 4:4:4, that is a founder-action row, not a threshold change. Open: shots.json, HUD-on pass, docs/store/encode_settings.json, sheets, M-004 clip sets, 4 blocked paths (kaiju/walk, V1/V2 tiles).
- Queue: T-189 slice polish (running), T-190 real-Bay M-003.c1/e look fix (dispatched), then encode_settings.json + shots.json + HUD pass (T-191).
- T-189 ✓ 28f5b13 (feel-check slice: `slice_feelcheck.flags.json` default, SliceCombatWorld, SliceFeelRig cannon/arm/hangar cycle, docs/FEEL_CHECK.md; EditMode 791/0, PlayMode smoke 1/1). Open: no Mac player smoke, no hardware listening, kaiju kill/respawn not in smoke, ai.pack/meta/net off, recoil/foot contacts await T-005. Assumed feel values: kaiju hp 400, masses.
- T-191 dispatched: encode_settings.json, shots.json, HUD-on pass, contact sheets, M-004 clip sets.
- T-190 ✓ 883d8fe (ID collides with earlier Audio T-190 files; files sit side by side). M-003.c1 and e PASS on all 5 T-147 sets via flag `rendering.pixel.hazeFarLift` (default OFF; per-profile hazeFarStartM/FullM/Power); C7 flag-on all 5 profiles PASS (amber 4.3 vs 4 L*, binding hazeFarStartM=400); C11 0.958 px; Pixel EditMode 107/107. Paths reframed (pitch up 2.2-3.0 deg). Metric defects proposed as "M-003 v1.2" (not applied: that would change a gate; research item, only by an independent reviewer).
  Decided autonomously (founder asleep): far lift is ON in the feel-check and capture configs (it is what makes the gates pass; shipping config follows once seam report T-192 clean); no gate change.
- T-192 dispatched: enable far lift in feelcheck/capture configs, C1/C2/A2 seam report on moving layered sequences, amber C7 margin, Unity regression after merges.
