"""T-196g: every-frame d-iv@v2 (A2 seam adherence included) and M-123 layered relation with the slab-id channel, plus a check that the
channel did not move any output pixel. usage: slab_id_eval.py <new capture dir> <old capture dir or -> <out.json> [stride]"""
import json, os, sys
from multiprocessing import Pool
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "Pipelines", "capture"))
from capture_tools import divv2, divv2_adapter as A

NEW, OLD, OUT = sys.argv[1], sys.argv[2], sys.argv[3]
LEGACY_SKY = os.environ.get('SLAB_ID_LEGACY_SKY') == '1'   # capture made before the sky-is-not-a-gap shader fix: slab-2 gap codes (7) are sky, read as plain slab 2 (3)
STRIDE = int(sys.argv[4]) if len(sys.argv) > 4 else 1

def one(args):
    clip, name = args
    d = os.path.join(NEW, clip, "x4")
    sc, dep, co = A.load_frame(d, name)
    sid = A.load_slab_id(d, sc)
    if LEGACY_SKY: sid = np.where(sid == 7, 3, sid).astype(np.uint8)
    ci = np.array(Image.open(os.path.join(d, sc["files"]["color_internal"])))[..., :3]
    r = A.evaluate_frame(sc, dep, co, slab_id=sid)
    m, own, undec = A.layered_nn_match(sc, dep, ci, co, slab_id=sid)
    v1 = divv2.d_iv_v2(co, sc["resolution"]["integer_scale"], offset=tuple(sc["resolution"]["output_block_offset_px"]))["frame"]
    same = None
    if OLD != "-":
        od = os.path.join(OLD, clip, "x4", sc["files"]["color_output"].split("/")[-1])
        same = bool(os.path.exists(od) and (np.array(Image.open(od))[..., :3] == co).all())
    lab, gap, cl = A.slab_id_labels(sid)
    return dict(clip=clip, frame=name, frame_v2=r["frame"], slab_scores={int(k): v["score"] for k, v in r["slabs"].items()}, ok=r["ok"],
                fails=r["failures"], seam=r.get("seam_adherence"), eval_area=r["eval_area"], v1=v1, m123=m, m123_own=own, undec=undec,
                gap_px=int(gap.sum()), clamped_px=int(cl.sum()), unlabelled_px=int((lab < 0).sum()), unchanged_vs_old=same)

if __name__ == "__main__":
    jobs = []
    for clip in sorted(os.listdir(NEW)):
        d = os.path.join(NEW, clip, "x4")
        if not os.path.isdir(d): continue
        names = sorted(n for n in os.listdir(d) if n.startswith("f") and n.endswith(".json"))
        jobs += [(clip, n) for n in names[::STRIDE]]
    rows = []
    with Pool(3, maxtasksperchild=40) as p:
        for n, r in enumerate(p.imap_unordered(one, jobs, chunksize=2)):
            rows.append(r)
            if n % 60 == 0: print('progress', n, len(jobs), flush=True)
    json.dump(rows, open(OUT, "w"))
    for clip in sorted({r["clip"] for r in rows}):
        R = [r for r in rows if r["clip"] == clip]
        print(clip, "frames", len(R), "all ok", all(r["ok"] for r in R), "min frame_v2 %.4f" % min(r["frame_v2"] for r in R),
              "min slab %.4f" % min(min(r["slab_scores"].values()) for r in R if r["slab_scores"]), "min A2 %.5f" % min(r["seam"] for r in R if r["seam"] is not None),
              "min eval_area %.4f" % min(r["eval_area"] for r in R), "min m123 %.6f" % min(r["m123"] for r in R), "max undec %.5f" % max(r["undec"] for r in R),
              "gap px max %d" % max(r["gap_px"] for r in R), "unlabelled max %d" % max(r["unlabelled_px"] for r in R),
              "out unchanged", sorted({str(r["unchanged_vs_old"]) for r in R}), "v1 min %.4f" % min(r["v1"] for r in R))
