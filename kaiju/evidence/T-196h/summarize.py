"""T-196h: reduce the every-frame slab-id evaluations (slab_id_eval.py results, both arms, 8 clips) to summary.json + summary.txt.
usage: summarize.py <out dir> arm=results.json [arm=results.json ...]"""
import json, sys, os
out = sys.argv[1]; summ = {"schema": "T-196h-gate-confirmation@v1", "arms": {}, "all_clip_arms_clean": True, "clip_arms": 0, "frames": 0}
lines = []
for spec in sys.argv[2:]:
    arm, path = spec.split("=", 1); rows = json.load(open(path)); summ["arms"][arm] = {}
    for clip in sorted({r["clip"] for r in rows}):
        R = [r for r in rows if r["clip"] == clip]
        e = {"frames": len(R), "all_ok": all(r["ok"] for r in R), "min_frame_v2": min(r["frame_v2"] for r in R),
             "min_slab_v2": min([min(r["slab_scores"].values()) for r in R if r["slab_scores"]] or [1.0]),
             "min_a2": min([r["seam"] for r in R if r["seam"] is not None] or [1.0]), "min_eval_area": min(r["eval_area"] for r in R),
             "min_m123": min(r["m123"] for r in R), "max_undecidable": max(r["undec"] for r in R), "max_unlabelled_px": max(r["unlabelled_px"] for r in R),
             "min_v1_beside": min(r["v1"] for r in R)}
        e["clean"] = bool(e["all_ok"] and e["min_a2"] == 1.0 and e["min_m123"] == 1.0 and e["min_frame_v2"] >= 0.995 and e["min_slab_v2"] >= 0.995
                          and e["min_eval_area"] >= 0.95 and e["max_undecidable"] <= 0.05 and e["max_unlabelled_px"] == 0)
        summ["arms"][arm][clip] = e; summ["clip_arms"] += 1; summ["frames"] += len(R); summ["all_clip_arms_clean"] &= e["clean"]
        lines.append(f"{arm:8s} {clip:24s} frames {e['frames']} clean {e['clean']} A2 {e['min_a2']:.5f} M-123 {e['min_m123']:.6f} v2 {e['min_frame_v2']:.4f} area {e['min_eval_area']:.4f} undec {e['max_undecidable']:.4f} v1(beside) {e['min_v1_beside']:.4f}")
json.dump(summ, open(os.path.join(out, "summary.json"), "w"), indent=1); open(os.path.join(out, "summary.txt"), "w").write("\n".join(lines) + "\nALL CLEAN: %s (%d clip-arms, %d frames)\n" % (summ["all_clip_arms_clean"], summ["clip_arms"], summ["frames"]))
print(open(os.path.join(out, "summary.txt")).read())
