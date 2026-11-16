#!/usr/bin/env python3
"""T-193: writes tuning variants (complete PaletteProfileTuning.json copies) and the reduced M-004 path files for the in-process sweep
   (CaptureCli -captureTuningVariants). Usage (from kaiju/): python make_variants.py <out_dir> ; then
   unity run . -- -executeMethod Game.Tools.Capture.CaptureCli.Run -captureEnable -captureScale 4 -captureStride 6 -captureMaxFrames 10
        -captureTuningVariants <out_dir>/variants -captureOut <out> -captureConfig <M0StyleFrame.json>,<M3HazeFarLift.json>"""
import copy, json, os, sys

BASE = json.load(open("Assets/Game/Config/Rendering/PaletteProfileTuning.json"))
out = sys.argv[1]
os.makedirs(out + "/variants", exist_ok=True); os.makedirs(out + "/paths", exist_ok=True)
for pid in ("lavender_peach", "warm_amber_haze"):
    for v in ("v4", "v5"):
        src = f"docs/store/paths_motion/m004_{v}_{pid}.json"
        d = json.load(open(src)); d["motion_set"] = {"classes": ["Y1", "Y2"]}
        json.dump(d, open(f"{out}/paths/m004_{v}_{pid}.json", "w"), indent=1)
P = "Pipelines"
def paths(*names): return ",".join(f"{out}/paths/{n}.json" for n in names)
AMB = paths("m004_v4_warm_amber_haze", "m004_v5_warm_amber_haze") + ",docs/store/paths/p3_a_golden_gate_v3.json"
LAV = paths("m004_v4_lavender_peach", "m004_v5_lavender_peach") + ",docs/store/paths/p4_a_sunrise_v5.json,docs/store/paths/p4_a_still_v5.json"
def variant(name, profile, paths_, **kw):
    t = copy.deepcopy(BASE); t["profiles"][profile].update(kw); t["capturePaths"] = paths_
    json.dump(t, open(f"{out}/variants/{name}.json", "w"), indent=1)
variant("A00_base", "warm_amber_haze", AMB)
variant("A01_spread075", "warm_amber_haze", AMB, ditherSpread=0.75)
variant("A02_spread060", "warm_amber_haze", AMB, ditherSpread=0.6)
variant("A03_step65", "warm_amber_haze", AMB, ladderMinStepL=6.5)
variant("A04_bands16", "warm_amber_haze", AMB, hazeBands=16)
variant("A05_bands64", "warm_amber_haze", AMB, hazeBands=64)
variant("L00_base", "lavender_peach", LAV)
variant("L01_spread120", "lavender_peach", LAV, ditherSpread=1.2)
variant("L02_spread150", "lavender_peach", LAV, ditherSpread=1.5)
variant("L03_step35", "lavender_peach", LAV, ladderMinStepL=3.5)
variant("L04_step50", "lavender_peach", LAV, ladderMinStepL=5.0)
variant("L05_bands24", "lavender_peach", LAV, hazeBands=24)
variant("L06_bands32", "lavender_peach", LAV, hazeBands=32)
variant("L07_bands12", "lavender_peach", LAV, hazeBands=12)
