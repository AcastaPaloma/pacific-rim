#!/usr/bin/env python
"""T-005 r7 (step 8a): stride_opt_tc.py with the round-7 HARD limits, applied to the unchanged problem as source patches (stride_opt.py / stride_opt_tc.py are not modified):
  step time in [tmin, tmax] (default 1.5 .. 3.0 s, omega T < 1.5), hip height >= zmin (allowed CoM-height dip), flat-foot schedule (both points of a foot load/unload together:
  flat touchdown, rear foot flat through the double support of k_ds nodes, the front foot loads from node 0 and the rear one unloads by node k_ds), per foot CoP in
  [heel + cop_lo, toe - cop_hi] (linear rows on the two point normal forces), total F_z <= fzmax W at every node.
Env var R7 = JSON overrides {tmin,tmax,zmin,flat,k_ds,cop_lo,cop_hi,fzmax}. All other arguments are those of stride_opt_tc.py. Run from the project root:
  R7='{"tmax":3.0}' python Tools/GaitOpt/stride_opt_r7.py --fr 0.10 --lat --trans --frate 0.5 --vlift 0.5 --v3 --yaw --nodes 40 --maxiter 500 --warm <json> --out <stem>"""
import os, sys, json, pathlib
HERE = pathlib.Path(__file__).resolve().parent
R7 = dict(tmin=1.5, tmax=3.0, zmin=36.0, flat=1, k_ds=8, cop_lo=1.5, cop_hi=2.5, fzmax=1.5); R7.update(json.loads(os.environ.get("R7", "{}")))
T = (HERE / "stride_opt_tc.py").read_text()
def rep(s, a, b, cnt=1):
    assert s.count(a) >= 1, f"anchor not found: {a[:70]}"
    return s.replace(a, b, cnt)
# 1. the tc script exec's stride_opt.py source: patch that source in place (after read_text) and give it R7
T = rep(T, 'src = (HERE / "stride_opt.py").read_text(); cut = src.index(\'cons = [{"type": "eq"\')',
        'src = (HERE / "stride_opt.py").read_text(); src = R7PATCH(src); cut = src.index(\'cons = [{"type": "eq"\')')
T = rep(T, 'P = {"__file__": str(HERE / "stride_opt.py"), "__name__": "stride_opt_problem"}', 'P = {"__file__": str(HERE / "stride_opt.py"), "__name__": "stride_opt_problem", "R7": R7}')
def R7PATCH(src):
    src = rep(src, "bnds = [(-300, 300) if i % 9 == 0 else (15, 45) if i % 9 == 1 else (-1.2, 1.2) for i in range(N * 9)] + [(0.8 * Tstep_ref, 1.2 * Tstep_ref)]",
              "bnds = [(-300, 300) if i % 9 == 0 else (R7['zmin'], 45) if i % 9 == 1 else (-1.2, 1.2) for i in range(N * 9)] + [(R7['tmin'], R7['tmax'])]")
    src = rep(src, 'open("Assets/Game/Config/Mech/mech_frame_balanced.json")', 'open(R7.get("frame") or "Assets/Game/Config/Mech/mech_frame_balanced.json")')
    src = rep(src, 'open("Assets/Game/Config/Mech/body_tuning.json")', 'open(R7.get("body") or "Assets/Game/Config/Mech/body_tuning.json")')
    src = rep(src, "k_ds = int(round((BETA - 0.5) * 2 * N));", "k_ds = int(R7['k_ds']) if R7['flat'] else int(round((BETA - 0.5) * 2 * N));")
    src = rep(src, "    fl[k, 1] = k <= k_ds; fl[k, 2] = k < k_hr; fl[k, 3] = k >= k_ff",
              "    if R7['flat']: fl[k, 0] = fl[k, 1] = k <= k_ds; fl[k, 2] = fl[k, 3] = True\n    else: fl[k, 1] = k <= k_ds; fl[k, 2] = k < k_hr; fl[k, 3] = k >= k_ff")
    return src
# 2. CoP window and F_z cap as extra linear rows (row = Sx_z z + xc >= 0), inserted into the --trans row list before it is assembled
ANCH = "    Sx_c = np.array(xc); Sx_z = sp.csr_matrix((zv, (zr, zc_)), shape=(nr, nz)); Sx_O = sp.csr_matrix((ov, (orr, oc)), shape=(nr, N * NOc))"
BLK = '''    if R7["cop_lo"] is not None:
        Sole = P["TOEX"] - P["HEELX"]; clo, chi = R7["cop_lo"], Sole - R7["cop_hi"]; n_r7 = nr
        def zrow(terms, const, lab):
            global nr
            for c_, v_ in terms: zr.append(nr); zc_.append(c_); zv.append(v_)
            xc.append(const); lab_x.append(lab); nr += 1
        for k in range(N):
            for ft in (0, 1):
                hp, tp = 2 * ft, 2 * ft + 1
                if fl[k, hp] and fl[k, tp]:
                    zrow([(iFn(k, hp), -clo), (iFn(k, tp), Sole - clo)], 0.0, ("r7_cop_lo", k, ft))        # CoP >= heel + clo
                    zrow([(iFn(k, hp), chi), (iFn(k, tp), chi - Sole)], 0.0, ("r7_cop_hi", k, ft))         # CoP <= toe - cop_hi
            tot = [iFn(k, p) for p in range(4) if fl[k, p]]
            zrow([(c_, -1.0) for c_ in tot], R7["fzmax"], ("r7_fz", k, 0))                                 # sum Fn <= fzmax BW
        print(f"--r7: {nr - n_r7} CoP / F_z rows (CoP in [heel + {clo}, heel + {chi}] m of a {Sole} m sole, F_z <= {R7['fzmax']} W), T in [{R7['tmin']}, {R7['tmax']}] s, hip z >= {R7['zmin']} m, flat={R7['flat']} k_ds={R7['k_ds']}", flush=True)
'''
T = rep(T, ANCH, BLK + ANCH)
# flat feet on both legs: the toe stay_x row of foot A is implied as well (same argument as foot B)
T = rep(T, 'not (l[0] == "stay_x" and l[2] == 3 and fl[l[1], 2] and fl[l[1] + 1, 2])', 'not (l[0] == "stay_x" and l[2] in (1, 3) and fl[l[1], l[2] - 1] and fl[l[1] + 1, l[2] - 1])')
T = rep(T, "if chg > 0.01:", "if chg > 0.001:")
sys.argv[0] = str(HERE / "stride_opt_tc.py")
G = {"__file__": str(HERE / "stride_opt_tc.py"), "__name__": "__main__", "R7": R7, "R7PATCH": R7PATCH}
exec(compile(T, str(HERE / "stride_opt_tc.py"), "exec"), G)
