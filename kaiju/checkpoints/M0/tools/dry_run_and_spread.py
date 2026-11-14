import json,os,csv,random,subprocess,shutil,gzip,statistics,copy
import numpy as np
from scipy.stats import spearmanr
W='/Users/kuanw/Me/code/pacific-rim/.claude/worktrees/fable-orchestrator-prompt-83a0a8/.wt/'
OUT=W+'T-006/kaiju/checkpoints/M0/'; REPO=W+'T-006/kaiju'; T4=W+'T-004/kaiju/evidence/T-004/'
PY='/opt/miniconda3/envs/kaiju/bin/python'
key=json.load(open(OUT+'answer_key.json'))
def rj(p): return json.load(gzip.open(p)) if p.endswith('.gz') else json.load(open(p))
sets={'M-003':[k for k in key if k.startswith('M003-') and k[5:].isdigit() and int(k[5:])<=10],'M-004':[k for k in key if k.startswith('M004-')],
      'M-005':[k for k in key if k.startswith('M005-')],'M-001':[k for k in key if k.startswith('WALK-')]}
dr=OUT+'dryrun/'; shutil.rmtree(dr,ignore_errors=True); os.makedirs(dr)
# dummy filled sheets
rng=random.Random(11)
def score(k):
    rk=key[k].get('intended_rank')
    if k.startswith('M003-A'):
        return 10 if key[k]['anchor']=='positive' else 2
    if rk is None: return rng.randint(3,6)
    return rk
for m,ids in sets.items():
    extra=[k for k in key if k.startswith('M003-A')] if m=='M-003' else []
    mx=max((key[k].get('intended_rank') or 0) for k in ids) or 1
    with open(dr+f'{m}_dummy_filled.csv','w',newline='') as f:
        w=csv.writer(f); w.writerow(['capture','founder_a','founder_b','notes'])
        for k in sorted(ids+extra):
            if k in extra: a=b=score(k)
            else:
                rk=key[k].get('intended_rank'); base=(10-9*(rk-1)/max(mx-1,1)) if rk else rng.uniform(3,6)
                a=min(10,max(1,round(base+rng.uniform(-0.8,0.8),1))); b=min(10,max(1,round(base+rng.uniform(-0.8,0.8),1)))
            w.writerow([k,a,b,'DUMMY'])
# reports per capture
ct=rj('/private/tmp/claude-501/ct_m003/report.json')
P='origin/task/T-005:kaiju/evidence/T-005/metrics/'
for m,ids in sets.items():
    for k in ids:
        if m in ('M-003','M-004'):
            rp=T4+key[k]['report_path']
            r=rj(rp) if os.path.exists(rp) else ct
        elif m=='M-001':
            r=json.loads(subprocess.run(['git','-C',REPO,'show',P+'M-001/report.json'],capture_output=True).stdout)
        else: continue
        r=copy.deepcopy(r); r['label']=k
        os.makedirs(OUT+f'bulk/dryrun_reports/{m}/{k}',exist_ok=True); json.dump(r,open(OUT+f'bulk/dryrun_reports/{m}/{k}/report.json','w'))
log=[]
for m in ('M-003','M-004','M-001','M-005'):
    rf=dr+f'{m}_dummy_ratings.json'
    subprocess.run([PY,OUT+'make_ratings.py',dr+f'{m}_dummy_filled.csv','--metric',m,'--out',rf,'--key',OUT+'answer_key.json'],check=True,capture_output=True)
    cmd=[PY,'Tools/Metrics/metrics.py','calibrate','--ratings',rf,'--reports',OUT+f'bulk/dryrun_reports/{m}','--out',dr+f'calibrate_{m}']
    p=subprocess.run(cmd,cwd=REPO,capture_output=True,text=True)
    open(dr+f'calibrate_{m}.stdout.txt','w').write(f"$ {' '.join(c.replace(W,'.wt/') for c in cmd)}\nexit {p.returncode}\n--- stdout\n{p.stdout}\n--- stderr\n{p.stderr}")
    log.append((m,p.returncode)); print(m,p.returncode,p.stdout[:400].replace('\n',' | '),p.stderr[:300].replace(W,''))
# ---- spread
def sev4(k):
    v=key[k]['metric_values_sequence']; best=0
    for sid,x in v.items():
        if x['status']=='NOT_RUN' or x['value'] is None: continue
        t=x['threshold'] or 0.001; best=max(best,x['value']/t)
    return best
def sev3(k):
    g=key[k]['set_level_gating_fail']
    return len(g) if isinstance(g,list) else 4
out=['# M0 packet: metric spread and intended order per set\n',
 'Intended order = the pipeline control or mode ranking from the build script (1 = clearly good; ties allowed). Spearman is intended rank versus the severity number named per set (positive = the metric tracks the intended order). Values come from existing reports; nothing was re-captured. Calibration against founder ratings happens after rating, not here.\n']
for m,ids in sets.items(): pass
# M-003
rk=[key[k]['intended_rank'] for k in sets['M-003']]; sv=[sev3(k) for k in sets['M-003']]
out+=['## M-003 (10 stills; 4 profiles + 6 controls; 2 hero slots TODO; 4 anchors)',
 'Metric used for the order: number of gating sub-measures failing in the report that holds the capture (continuous_tone: the tool returned INVALID_INPUT, counted as 4, worse than every measured control).',
 f'Spearman(intended rank, gating fails) = {spearmanr(rk,sv)[0]:.3f}, n=10.  Gating-fail counts: {sorted(sv)}.',
 'Per-frame values (station frame 5):','| ID | intended | gating fails | a.output | b.DIG | d-iv | e.contrast |','|---|---|---|---|---|---|---|']
for k in sorted(sets['M-003']):
    v=key[k]['metric_values_frame']; f=lambda x:'n/a' if x is None else f'{x:.4g}'
    out.append(f"| {k} | {key[k]['intended_rank']} | {sev3(k)} | {f(v.get('M-003.a.output'))} | {f(v.get('M-003.b.DIG'))} | {f(v.get('M-003.d-iv'))} | {f(v.get('M-003.e.contrast'))} |")
vals=[key[k]['metric_values_frame'].get('M-003.a.output') for k in sets['M-003'] if key[k]['metric_values_frame'].get('M-003.a.output') is not None]
out.append(f"Spread of a.output across the set: min {min(vals):.4g}, max {max(vals):.4g}. Caveat: palette share cannot rank dither_off, haze_off, reversed_haze (all 1.0); those controls fail other sub-measures (b.DIG, c1). The ordering among controls is my judgement of badness, not a measured fact. continuous_tone has no metric value (tool rejects its inputs).")
# M-004
rk=[key[k]['intended_rank'] for k in sets['M-004']]; sv=[sev4(k) for k in sets['M-004']]
out+=['\n## M-004 (10 sequences, 60 frames, eye70, slate_teal)',
 'Metric used: worst value/threshold ratio over the shown sequence\'s S, C, F sub-measures (threshold 0 on S0 counted as 0.001). Y2 = 1 px/frame yaw, T2 = camera translate, S0 = static (frame-noise control).',
 f'Spearman(intended rank, severity) = {spearmanr(rk,sv)[0]:.3f}, n=10.','| ID | mode | seq | intended | severity (x threshold) | S_px |','|---|---|---|---|---|---|']
for k in sorted(sets['M-004']):
    v=key[k]['metric_values_sequence']; s=[x['value'] for sid,x in v.items() if 'S_px' in sid or sid.endswith('.S0')]
    out.append(f"| {k} | {key[k]['mode']} | {key[k]['sequence']} | {key[k]['intended_rank']} | {sev4(k):.3g} | {s[0] if s else 'n/a'} |")
out.append(f"Severity range {min(sv):.3g} to {max(sv):.3g} x threshold. Duplicates removed: direction, direction_nosnap and the base m004 run render identical Y2 frames (snap is irrelevant to yaw); screen_nostab equals direction_norotq on Y2; hybrid_nosnap equals hybrid on Y2. Translate sequences (T2) were used to show the snap difference instead. Fewer distinct modes exist than 10 on Y2, so 4 captures are T2 sequences.")
# M-005
sk=[(k,key[k]['metric_values']) for k in sets['M-005'] if isinstance(key[k]['metric_values'],dict)]
out+=['\n## M-005 (10 captures, no deliberately bad end)','Only the four skyline captures have a per-capture number (IoU vs the independent lidar reference R-ind):']
for k,v in sorted(sk,key=lambda x:x[1]['ind_iou']): out.append(f"- {k}: ind IoU {v['ind_iou']:.4f}, src IoU {v['src_iou']:.4f}, gate {v['gate']}")
iou=[v['ind_iou'] for _,v in sk]; out.append(f"Spread of ind IoU {min(iou):.3f} to {max(iou):.3f}. Intended order vs metric: not computable, because the pipeline has no control that degrades M-005 on purpose (nothing invented). The remaining six captures (oblique x2, shoreline, top-down, two plates) carry no per-capture number. Raters therefore judge recognisability only; the set does not span clearly bad.")
out+=['\n## M-001/M-002 walk strips (10, UNMERGED, NOT AT TARGET PACE)','All ten strips come from one run at 3.07 m/s (Froude 0.023 against the 0.17-0.23 gate), M-001 FAIL, M-002 FAIL. The metrics are set-level (one value per run, not per strip), so there is no per-strip spread and no Spearman. The strips span scenarios (stand, start, walk, stop, estop, turn, curve, stomp, over-limit intent, shove-to-fall), not good-to-bad quality. walk_fr010/025/030 were left out because their strips are byte-identical in size to walk_fr020 (all capped at the same step length).']
open(OUT+'spread.md','w').write('\n'.join(out)+'\n')
print('\n'.join(out)[:2500]); print(log)
