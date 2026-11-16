#!/usr/bin/env python3
"""T-193 cycle 2, round 3: lavender_peach with ONLY dither parameters changed (ladderMinStepL back at the default 2.5, because the
   R-131 golden model tests (HazeRungModelTests) pin the lavender rung ladder). Usage (from kaiju/): python make_variants3.py <out_dir>;
   unity run . -- -executeMethod Game.Tools.Capture.CaptureCli.Run -captureEnable -captureScale 4 -captureStride 6 -captureMaxFrames 10
        -captureTuningVariants <out_dir>/variants -captureOut <out> -captureConfig <M0StyleFrame.json>,<M3HazeFarLift.json>
   then analyse.py <out> <json>."""
import copy, json, os, sys
BASE = json.load(open("Assets/Game/Config/Rendering/PaletteProfileTuning.json"))
out = sys.argv[1]; os.makedirs(out + "/variants", exist_ok=True); os.makedirs(out + "/paths", exist_ok=True)
for v in ("v4", "v5"):
    d = json.load(open(f"docs/store/paths_motion/m004_{v}_lavender_peach.json")); d["motion_set"] = {"classes": ["Y1", "Y2"]}
    json.dump(d, open(f"{out}/paths/m004_{v}_lavender_peach.json", "w"), indent=1)
LAV = ",".join([f"{out}/paths/m004_v4_lavender_peach.json", f"{out}/paths/m004_v5_lavender_peach.json", "docs/store/paths/p4_a_sunrise_v5.json", "docs/store/paths/p4_a_still_v5.json"])
def variant(name, **kw):
    t = copy.deepcopy(BASE); l = t["profiles"]["lavender_peach"]; l.pop("ladderMinStepL", None); l.pop("ditherSpread", None); l.update(kw)
    t["capturePaths"] = LAV; json.dump(t, open(f"{out}/variants/{name}.json", "w"), indent=1)
variant("D00_orig")
for s in (0.7, 0.5, 0.4, 0.3): variant(f"D{int(s*10):02d}_spread{int(s*100)}", ditherSpread=s)
variant("D10_spread50_bands32", ditherSpread=0.5, hazeBands=32)
