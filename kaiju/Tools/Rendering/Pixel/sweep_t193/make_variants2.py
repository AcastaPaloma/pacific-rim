#!/usr/bin/env python3
"""T-193 cycle 2, round 2: amber-only variants of the committed tuning for the static p3_a path (one frame is exact, the camera is fixed).
   Usage (from kaiju/): python make_variants2.py <out_dir> ; then
   unity run . -- -executeMethod Game.Tools.Capture.CaptureCli.Run -captureEnable -captureScale 4 -captureMaxFrames 1
        -captureTuningVariants <out_dir>/variants -captureOut <out> -captureConfig Assets/Game/Config/Rendering/Configurations/M3HazeFarLift.json
   then style_one.py <out> for DIG_max / c1 per variant."""
import copy, json, os, sys
BASE = json.load(open("Assets/Game/Config/Rendering/PaletteProfileTuning.json"))
out = sys.argv[1]; os.makedirs(out + "/variants", exist_ok=True)
ORIG = {"hazeBands": 32, "ladderMinStepL": 5.0}       # amber before T-193 (ditherSpread absent = profile default)
def variant(name, **kw):
    t = copy.deepcopy(BASE); a = t["profiles"]["warm_amber_haze"]
    a.pop("ditherSpread", None); a.update(ORIG); a.update(kw)
    t["capturePaths"] = "docs/store/paths/p3_a_golden_gate_v3.json"
    json.dump(t, open(f"{out}/variants/{name}.json", "w"), indent=1)
variant("B00_orig")
variant("B01_cand", hazeBands=16, ladderMinStepL=6.5, ditherSpread=0.6)     # the committed candidate
variant("B02_spread60", ditherSpread=0.6)
variant("B03_spread40", ditherSpread=0.4)
variant("B04_step65", ladderMinStepL=6.5)
variant("B05_step80", ladderMinStepL=8.0)
variant("B06_bands16", hazeBands=16)
variant("B07_bands24", hazeBands=24)
variant("B08_spread60_step65", ditherSpread=0.6, ladderMinStepL=6.5)
variant("B09_spread60_bands16", ditherSpread=0.6, hazeBands=16)
variant("B10_spread60_bands24", ditherSpread=0.6, hazeBands=24)
variant("B11_step65_bands16", ladderMinStepL=6.5, hazeBands=16)
variant("B12_spread40_step65", ditherSpread=0.4, ladderMinStepL=6.5)
variant("B13_spread40_bands24", ditherSpread=0.4, hazeBands=24)
