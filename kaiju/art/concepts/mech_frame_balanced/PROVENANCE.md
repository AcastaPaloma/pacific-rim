# PROVENANCE.md · Balanced chassis concept layers (T-007 Phase A, D-023)

- **Tool:** Midjourney web app, https://www.midjourney.com/imagine, driven through the Claude in Chrome extension in the founder's already signed-in Chrome (D-023; founders accepted the account risk).
- **Model / mode:** the account defaults, unchanged. Each job page shows "profile Global V7 Profile", which is the account's default personalization profile applied automatically; it was not chosen or changed by this agent.
- **Date:** 2026-11-14 (all jobs).
- **Agent:** `asset-mech-concept` (T-007 Phase A).
- **AI-generated: yes** for every image in this folder (Steam AI disclosure: pre-generated concept art; nothing here ships in the game).
- **Licence:** Midjourney outputs under the account holder's Midjourney subscription terms; owned by the account holder (founders).
- **Exact prompts:** copied verbatim in `PROMPTS.md` (IDs P1-a … P4-g). Every job below was submitted with exactly the prompt text in that file.

## Generation count: 15 of 16 (1 unused)
Phase A used 11 (jobs 1-11). Fix cycle 2 (DEF-T007-style-3, mood frame) used 4 more (jobs 12-15), its full allowance.
Each job = one Midjourney generation of a 4-image grid. Image index `n` = `0_n` on the CDN (0 top-left, 1 top-right, 2 bottom-left, 3 bottom-right). Job link: `https://www.midjourney.com/jobs/<job ID>`.

| # | Prompt | Layer | Job ID | Parameters | Kept images | Discarded images and why |
|---|---|---|---|---|---|---|
| 1 | P1-a | 1 | 030477f2-cd72-437d-9a46-c1352f153159 | `--ar 3:2 --no …` (NEG) | 0 (chosen silhouette sheet), 2 (alternate) | 1, 3: lanky or noisy outlines, weak foot mass |
| 2 | P1-b | 1 | dfa62a7b-bb6f-429f-a5a1-64f0b21a7a69 | `--ar 3:2 --no …` | none | all 4: thin stick-figure legs, toy/lanky read (fails C12/C16 intent) |
| 3 | P1-c | 1 | 7325f4b2-8a24-4abd-812b-ad2164171ce6 | `--ar 3:2 --no …` | none | all 4: several figures have a domed head / dish-shaped chest that recalls well-known cinema robots (C3 risk) or are lanky |
| 4 | P2-a | 2 | 4d4eda80-57f4-4f41-8a02-a1c23192d6a5 | `--ar 16:9 --no …` | 1 (first turnaround; source design) | 0: V-shaped off-white chest panel (C4 risk); 2: views disagree; 3: off-white bow panel with orange line reads as a stripe (C4) and thin masts |
| 5 | P2-b | 2 | e75c854e-3642-4877-8ec3-25271497bba7 | `--ar 16:9 --no …` | none | all 4: off-white primary (wrong role), antennas on head and shoulders (C4), generic genre look |
| 6 | P2-c | 2 | 2fa84a4e-3d89-445e-84a3-bebe971a8b3c | image prompt = own job 4d4eda80…/0_1.png; `--ar 16:9 --no …` | 2 (**final turnaround**) | 0, 1: thin ankle linkages; 3: off-white head cap and masts |
| 7 | P3-a2 | 3 | 77faf417-aabb-4415-afb6-788705e1c8f4 | image prompt = own job 4d4eda80…/0_1.png; `--ar 16:9 --no …` | 2 (plate grouping per segment, front and back) | 0, 1: spiky shoulder flaps (fin-like, C4 risk); 3: vertical off-white chest plate reads as a face (C4) |
| 8 | P3-b | 3 | b1c655d7-811f-4413-85da-dbd5d55c2346 | `--ar 16:9 --no …` | 3 (knee, elbow ram and wrist study; bottom 34 px cropped to remove a strip of illegible pseudo-text the model drew in the corner) | 0, 1, 2: repetitive, less clear rams |
| 9 | P4-a2 | 4 | c3ce3ab4-7bd4-45bf-9d83-49b30fe05d97 | image prompt = own job 4d4eda80…/0_1.png; `--ar 16:9 --no …` | 2 (warm-sky variant: full figure, pier with tiny people and cars, crane, boat, dithered sky bands) | 0, 1, 3: head cropped out of frame or warm sky dominates |
| 10 | P4-b | 4 | e4ac21ac-3deb-4e18-a6da-e30f3264e8e5 | `--ar 16:9 --no …` | none | 0–2: machine is not the chosen design and the crane is taller than the machine (scale cue wrong); 3: not retrievable (CDN 403) |
| 11 | P4-c | 4 | 6ba8d2aa-8808-4122-8f68-af97d7ed2be4 | image prompt = own job 2fa84a4e…/0_2.png; `--ar 16:9 --no …` | 1 (v1 mood frame, **superseded** by job 15) | 0: machine's legs cropped by fog; 2, 3: arms posed, crane too close |
| 12 | P4-d | 4 | 3def4f05-6c0b-4cab-929f-73885306660c | image prompt = own job 2fa84a4e…/0_2.png; `--ar 16:9 --no …` (NEG + mast, reflection of feet) | none | all 4: the image reference pulled the framing close (head cropped in 2 of 4), feet stand on the water surface, people far too large; fails the water line and scale |
| 13 | P4-e | 4 | a2eedea7-29b3-44a7-8a7a-09e3ecf60629 | `--ar 16:9 --no …` (NEG + mast) | none | 0, 2: head out of frame; 1 (best): legs end on flat base plates sitting on the water surface (water at the sole) and a glowing orange square in the centre of the chest (C4); 3: crane and ship crowd the legs |
| 14 | P4-f | 4 | 6c9f1c93-58a6-4c16-857b-59515561cfe3 | `--ar 16:9 --no …` (NEG + mast, chest light, feet, base plate) | none (image 1 was saved and measured, then dropped for job 15) | 1: legs enter the water with splash rings, but by leg proportion the visible leg (crotch to water 0.49 of visible height) already equals the design's full leg, so the water sits at or within 1 m of the sole (C28 check fails); 0, 2: head cropped or merged with fog; 3: legs dissolve in fog |
| 15 | P4-g | 4 | af67b340-b21d-4861-b15d-57f351cebe0c | image prompt = own job 6c9f1c93…/0_1.png; `--ar 16:9 --no …` (NEG + mast, rods, chest light, feet, base plate) | 0 (**final mood frame v2**) | 1: legs fade into fog, water line unreadable; 2: arms and legs posed mid-stride; 3: warm sky dominates, legs taper to thin ankles |

Image references (C2): only this task's own outputs were used, by pasting their CDN URL into the prompt: 4d4eda80-57f4-4f41-8a02-a1c23192d6a5 image 1 (jobs 6, 7, 9), 2fa84a4e-3d89-445e-84a3-bebe971a8b3c image 2 (jobs 11, 12) and 6c9f1c93-58a6-4c16-857b-59515561cfe3 image 1 (job 15). No file was uploaded; no style reference from `art/style_refs/` was viewed for this task, described in a prompt or sent anywhere.

## Kept files
| File | Layer | Source |
|---|---|---|
| `layer1_silhouettes-A_030477f2-cd72-437d-9a46-c1352f153159_0.jpg` | 1 | job 1 image 0 |
| `layer1_silhouettes-B_030477f2-cd72-437d-9a46-c1352f153159_2.jpg` | 1 | job 1 image 2 |
| `layer2_turnaround_4d4eda80-57f4-4f41-8a02-a1c23192d6a5_1.jpg` | 2 | job 4 image 1 |
| `layer2_turnaround-final_2fa84a4e-3d89-445e-84a3-bebe971a8b3c_2.jpg` | 2 | job 6 image 2 |
| `layer3_plate-breakup_77faf417-aabb-4415-afb6-788705e1c8f4_2.jpg` | 3 | job 7 image 2 |
| `layer3_joints_b1c655d7-811f-4413-85da-dbd5d55c2346_3.jpg` | 3 | job 8 image 3 |
| `layer4_mood-warm_c3ce3ab4-7bd4-45bf-9d83-49b30fe05d97_2.png` | 4 | job 9 image 2. **Superseded** by mood-v2 (fix cycle 2); kept as history only |
| `layer4_mood-cold_6ba8d2aa-8808-4122-8f68-af97d7ed2be4_1.png` | 4 | job 11 image 1. **Superseded** by mood-v2 (fix cycle 2: water at the sole, people and cars several times too large); kept as history only |
| `layer4_mood-v2_af67b340-b21d-4861-b15d-57f351cebe0c_0.png` | 4 | job 15 image 0. **Current mood frame.** 1387×777 PNG, saved the same way as the others (overlay capture, cropped) |

**How files were saved.** `curl` of the CDN URL was refused (HTTP 403). The full-size PNG was fetched inside the midjourney.com page context and shown full-window in an overlay; the saved file is a clean screenshot of that overlay, cropped to the image (about 1397×784 px, slightly downscaled from the 1456×816 original; the silhouette sheets from 1344×896). No other processing.

## Selection rationale
- **Layer 1:** job 1 image 0, top row middle figure: two upright box shoulder blocks that stand level with a small low head, a chest that narrows to a keel, heavy box forearms, column legs and flat feet. Chosen because it has the most distinctive, simple outline at small size (shoulder blocks give a "two-tower" top edge that no common genre design uses), reads heavy rather than lanky, and has no crest, horn or face. Top-left figure of the same sheet supplies the heavier leg and foot mass.
- **Layer 2:** job 6 image 2 is the final turnaround: front, side-three-quarter and back agree; wedge "ship-bow" chest, low flat sensor-cap head, off-white shoulder blocks on short posts, heavy forearm boxes. Job 4 image 1 is kept as the source it was derived from (it shows the hull seams and off-white thigh plates better).
- **Layer 3:** job 7 image 2 shows the plates grouped by segment front and back (chest shell, pelvis skirt, thigh slabs, shin plates, forearm boxes); its head and shoulder flaps drifted from the chosen design and are *not* to be followed. Job 8 image 3 shows the joint mechanics: round knee/ankle housings and paired hydraulic rams spanning knee and elbow. Its open hands and toed feet are *not* to be followed (the design uses closed fists and flat feet).
- **Layer 4 (current):** job 15 image 0 is the mood frame. It is the chosen design standing (bow-wedge chest, two raised off-white box shoulder blocks with the head sunk low between them, long box forearms hanging, column legs), in shallow harbour water with splash rings around the shins and no feet visible; cold slate-teal sky with ordered dither, one warm lamp on the near pier, off-centre; people, cars, a barge crane reaching about the knee and two boats in the foreground. C28 check (`evidence/T-007/mood_frame_check.json`, `mood_frame_histogram.png`): 2 hue families (slate-teal/grey-green 75.4 %, warm cream/brown 24.6 %) + accent 0.007 %; sky L* 72.9 > mech L* 38.0; water line estimated 7.7 m above the sole (5.0–10.5 m for the design's leg range 0.46–0.50 H), between ankle and mid-shin. Chosen over job 14 image 1, which measured the water at the sole.
- **Layer 4 (superseded, history):** job 11 image 1 (v1 cold frame) and job 9 image 2 (v1 warm frame).

## Known departures and issues (for eval-style and the modeller)
- Thin masts/rods on top of the shoulder blocks and head appear in several outputs; the design note removes them (C4: no antennas).
- v1 mood frames (jobs 9, 11): water at the sole, people and cars several times too large (DEF-T007-style-3); superseded.
- Mood frame v2 (job 15), not to be followed: thin masts on the deck and blocks (C4; the design note removes them, they survived `--no mast, rods, antenna`); the head reads as a low deck between the blocks rather than a distinct cap; the lower sky haze and the pier are warm cream/brown (24.6 % of pixels), so the cold family is dominant but not exclusive. Scale: the people (20–24 px) and cars (40–55 px long) stand on the near pier in front of the open water, at 3.4–4.1 % and 6.8–9.3 % of the mech's estimated image height; that is true scale only if the pier is about 1.6–1.9× nearer the camera than the mech, which the composition shows but cannot prove; at the pier's own depth they exceed the 2.5 % / 6.5 % bound the evaluator set for the mech's depth. The water line is an estimate from leg proportion (method in `evidence/T-007/mood_frame_check.py`), since the soles are hidden. The final in-engine frame comes from the T-004 pixel renderer.
- The final turnaround's lower legs taper to thin ankle linkages; the greybox uses full-width column shanks per C12 (shank never thinner mid-length than at the joints).
- Job 10 image 3 could not be fetched (CDN 403); it was never viewed or kept.

## What was and was not done on the account
Done: 15 image generations (jobs listed above; 11 in Phase A, 4 in fix cycle 2), each a fresh prompt typed into the Imagine box; viewing and fetching this session's own outputs; resizing the automation window of Chrome for clean captures.
Not done: no purchase, upgrade or plan change; no account, profile, personalization or generation-mode setting changed (the "V8 personalization is live" banner was left untouched); no terms accepted; no sign-in or sign-out; no CAPTCHA, login wall, payment prompt or moderation block was encountered (fix cycle 2 included); no image uploaded; nothing posted, shared, published, rated or messaged; no upscales, variations or edits run.
