import gzip,json,hashlib,os,random,shutil,subprocess,csv
from PIL import Image
W='/Users/kuanw/Me/code/pacific-rim/.claude/worktrees/fable-orchestrator-prompt-83a0a8/.wt/'
T4=W+'T-004/kaiju/evidence/T-004/'; C=T4+'captures_local/'; T3=W+'T-003/kaiju/evidence/T-003/'
OUT=W+'T-006/kaiju/checkpoints/M0/'
REPO=W+'T-006/kaiju'
if os.path.exists(OUT): shutil.rmtree(OUT)
for d in ('M003','M003/anchors','M004','M005','WALK','bulk','sheets','dryrun'): os.makedirs(OUT+d)
def rj(p): return json.load(gzip.open(p)) if p.endswith('.gz') else json.load(open(p))
def sc(d,seq,f=0): return json.load(open(f'{C}{d}/{seq}/f{f:06d}.json'))
key={}; rater_caps={}
def sub(r): return {s['id']:s for s in r['submeasures']}
GATING_FAIL=lambda r:[s['id'] for s in r['submeasures'] if s['kind']=='gating' and s['status']=='FAIL']
T4_BR='task/T-004'; INTEG='merged: integrated on task/T-006 and claude/fable-orchestrator-prompt-83a0a8 (T-004 merge d5ef3b0); not on main'
# ---------------- M-003
m3=rj(T4+'metrics/m003/report.json.gz'); PF={k:v['diagnostics'].get('per_frame') or v['diagnostics'].get('per_frame_DIG') for k,v in sub(m3).items() if v.get('diagnostics')}
def pfv(rep,seqkey):
    out={}
    for sid,v in sub(rep).items():
        pf=(v.get('diagnostics') or {}).get('per_frame') or (v.get('diagnostics') or {}).get('per_frame_DIG')
        if isinstance(pf,dict):
            for k,val in pf.items():
                if k.endswith(seqkey): out[sid]=val
    return out
F=5
items3=[]  # (intended_rank, src_set, seq, frame, kind, profile, toggle, reportpath)
for prof,view in (('slate_teal','eye70'),('lavender_peach','street'),('slate_crimson','elevated300'),('warm_amber_haze','eye70')):
    items3.append((1,'m003_stills',f'{prof}_{view}',F,'still',prof,'none (reference pipeline, defaults)',None))
ctrl={'dither_off':(2,'rendering.pixel.dither=false'),'haze_off':(3,'rendering.pixel.haze=false'),'bilinear':(4,'control.bilinearPresent=true'),
 'reversed_haze':(5,'control.reversedHaze=true'),'continuous_tone':(6,'continuous tone control (no palette quantise/dither look)'),'unquantised':(7,'rendering.pixel.quantise=false')}
for c,(rk,tg) in ctrl.items():
    items3.append((rk,f'control_{c}','slate_teal_eye70',F,'control','slate_teal',tg,f'metrics/controls/{c}/report.json.gz'))
ids=[f'M003-{i:02d}' for i in range(1,11)]; random.Random(30031).shuffle(ids)
rows=[]
for idn,(rk,st,seq,f,kind,prof,tg,rp) in zip(ids,items3):
    src=f'{C}{st}/{seq}/f{f:06d}_color_output.png'; shutil.copy(src,OUT+f'M003/{idn}.png')
    s=sc(st,seq,f); rep=m3 if rp is None else (rj(T4+rp) if os.path.exists(T4+rp) else None)
    vals=pfv(rep,f'/{seq}/f{f:06d}') if rep else {}
    cmd=(f"metrics.py m003 evidence/T-004/captures_local/{st} --out ... (registry {rep['registry']['revision']}, tool {rep['tool']['version']} @ {rep['tool']['git_commit'][:8]}); capture by Unity batch StyleCapture job at {s['build']['git_commit'][:8]}"
         if rep else "metrics.py m003 on this control set returned INVALID_INPUT (exit 2): palette_index and color_internal disagree (re-run in this packet step, 2026-11-14)")
    key[idn]=dict(set='M-003',report_path=(rp or 'metrics/m003/report.json.gz'),source=src.replace(W,'.wt/'),profile=prof,control_or_toggle=tg,kind=kind,station_frame=f,branch=T4_BR,commit=s['build']['git_commit'],
      tool_command=cmd,merged=INTEG,tag=('FAILED CONTROL (deliberately bad)' if kind=='control' else 'reference pipeline'),
      intended_rank=rk,metric_values_frame={k:vals.get(k) for k in ('M-003.a.output','M-003.b.DIG','M-003.d-i','M-003.d-ii','M-003.d-iii','M-003.d-iv','M-003.e.sky','M-003.e.horizon','M-003.e.contrast')},
      set_level_gating_fail=GATING_FAIL(rep) if rep else 'INVALID_INPUT', set_level_verdict=rep['verdict_label'] if rep else 'INVALID_INPUT (exit 2)',
      feature_flags={k:v for k,v in s['build']['feature_flags'].items() if k.startswith(('rendering.pixel','control'))})
    rater_caps[idn]=(rep,m3 if rp is None else rep)
# anchors
anch=[('positive','m003_stills','warm_rust_sepia_street',8,'warm_rust_sepia',1),('positive','m003_stills','slate_teal_street',2,'slate_teal',1),
      ('negative','control_unquantised','slate_teal_eye70',2,'slate_teal',7),('negative','control_continuous_tone','slate_teal_eye70',2,'slate_teal',6)]
aid=['M003-A1','M003-A2','M003-A3','M003-A4']; random.Random(7).shuffle(aid)
anchors={}
for a,(pol,st,seq,f,prof,rk) in zip(aid,anch):
    src=f'{C}{st}/{seq}/f{f:06d}_color_output.png'; shutil.copy(src,OUT+f'M003/anchors/{a}.png'); s=sc(st,seq,f)
    anchors[a]=pol
    key[a]=dict(set='M-003 anchor',anchor=pol,source=src.replace(W,'.wt/'),profile=prof,branch=T4_BR,commit=s['build']['git_commit'],kind='anchor',merged=INTEG,
        tool_command='Unity batch StyleCapture job; no metric run on this frame',tag='anchor shown to raters as an example of clearly good (rate 9-10) / clearly bad (rate 1-3)',intended_rank=rk)
key['M003-11']=key['M003-12']=dict(set='M-003',status='TODO: reserved for the two T-006 hero frames (step later); no file yet; add with the same blind-ID scheme and append sheet rows')
# ---------------- M-004
m4=[('direction','m004ab_direction','metrics/m004ab/direction','eye70_Y2',1),('hybrid','m004ab_hybrid','metrics/m004ab/hybrid','eye70_Y2',2),
    ('direction_norotq','m004ab_direction_norotq','metrics/m004ab/direction_norotq','eye70_Y2',8),('hybrid_norotq','m004ab_hybrid_norotq','metrics/m004ab/hybrid_norotq','eye70_Y2',8),
    ('screen','m004ab_screen','metrics/m004ab/screen','eye70_Y2',8),('frame_noise','m004ab_control_frame_noise','metrics/m004ab/control_frame_noise','eye70_S0',10),
    ('direction','m004ab_direction','metrics/m004ab/direction','eye70_T2',1),('direction_nosnap','m004ab_direction_nosnap','metrics/m004ab/direction_nosnap','eye70_T2',5),
    ('screen','m004ab_screen','metrics/m004ab/screen','eye70_T2',3),('hybrid_nosnap','m004ab_hybrid_nosnap','metrics/m004ab/hybrid_nosnap','eye70_T2',4)]
NF=60
ids4=[f'M004-{i:02d}' for i in range(1,11)]; random.Random(40041).shuffle(ids4); seen={}
for idn,(mode,d,mp,seq,rk) in zip(ids4,m4):
    frames=[f'{C}{d}/{seq}/f{i:06d}_color_internal.png' for i in range(NF)]
    ims=[Image.open(p).convert('RGB') for p in frames]
    h=hashlib.md5(b''.join(i.tobytes() for i in ims[::15])).hexdigest()
    if h in seen: print('DUPLICATE',idn,seen[h])
    seen[h]=idn
    big=[i.resize((i.width*2,i.height*2),Image.NEAREST) for i in ims]
    pal=[b.quantize(colors=256,method=Image.MEDIANCUT,dither=Image.NONE) for b in big]
    ncol=len(set(c for b in ims for c in (x[1] for x in b.getcolors(70000)))) if False else None
    pal[0].save(OUT+f'M004/{idn}.gif',save_all=True,append_images=pal[1:],duration=20,loop=0,optimize=False,disposal=1)
    os.makedirs(OUT+f'bulk/{idn}',exist_ok=True)
    for p in frames: shutil.copy(p,OUT+f'bulk/{idn}/'+os.path.basename(p))
    s=sc(d,seq,0); rep=rj(T4+mp+'/report.json.gz'); ff=s['build']['feature_flags']
    vals={}
    for sid,v in sub(rep).items():
        for p in v.get('parts') or []:
            if p['name'].startswith(seq): vals[sid]=dict(value=p['value'],status=p['status'],threshold=p['threshold'])
    key[idn]=dict(set='M-004',report_path=mp+'/report.json.gz',source=f'.wt/T-004/kaiju/evidence/T-004/captures_local/{d}/{seq}/ frames 0..{NF-1} (color_internal, x2 nearest-neighbour GIF, 50 fps)',
      sequence=seq,mode=mode,motion=s['motion'],control_or_toggle=dict(snapPosition=ff['camera.snapPosition'],quantiseRotation=ff['camera.quantiseRotation'],ditherAnchor=ff.get('rendering.pixel.ditherAnchor'),frameNoise_control=ff.get('control.frameNoise'),stabilisation=ff.get('camera.snapPosition')),
      branch=T4_BR,commit=s['build']['git_commit'],tool_command=f"metrics.py m004 evidence/T-004/captures_local/{d} --profiles slate_teal --viewpoints eye70 (registry {rep['registry']['revision']}, tool {rep['tool']['git_commit'][:8]}); capture: Unity batch StyleCapture job m004ab",
      merged=INTEG,tag=('FAILED CONTROL / rejected variant' if rk>=3 else 'candidate anchoring mode that passed'),intended_rank=rk,
      metric_values_sequence=vals,set_level_verdict=rep['verdict_label'],set_level_gating_fail=GATING_FAIL(rep),bulk_dir=f'checkpoints/M0/bulk/{idn} (lossless frames, git-ignored)')
    rater_caps[idn]=(rep,rep)
# ---------------- M-005
r5=json.load(open(T3+'m005_report.json')); v11=r5['v1_1']['m005']['M-005.7']['views']; v1=r5['v1']['m005']['M-005.7']
def skv(view): 
    return dict(ind_iou=v11[view]['vs_r_ind']['value']['iou'],ind_mae_px=v11[view]['vs_r_ind']['value']['mae_px'],src_iou=v11[view]['vs_r_src']['value']['iou'],gate=v11[view]['vs_r_ind']['result'])
T3C='task/T-003'; T3COMMIT='192012b'
m5=[('captures/skyline_v11_V4.png','skyline V4 (eye 10 m) v1.1',skv('V4'),2),('captures/skyline_v11_V5.png','skyline V5 (eye 146 m) v1.1',skv('V5'),4),('captures/skyline_v11_V4m.png','skyline V4m (eye 70 m) v1.1',skv('V4m'),3),
    ('captures/skyline_V4.png','skyline V4 v1',dict(ind_iou=v1['V4']['vs_r_ind']['value']['iou'],src_iou=v1['V4']['vs_r_src']['value']['iou'],gate=v1['V4']['vs_r_ind']['result']),1),
    ('captures/oblique_270.png','oblique view 270',None,None),('captures/oblique_235.png','oblique view 235',None,None),('captures/shoreline_embarcadero.png','shoreline Embarcadero',None,None),
    ('captures/topdown.png','top-down tile view',None,None),('plates/height_relations_mech.png','height relations plate (mech vs towers)',None,None),('plates/wading_depth.png','wading depth plate',None,None)]
ids5=[f'M005-{i:02d}' for i in range(1,11)]; random.Random(50051).shuffle(ids5)
for idn,(p,what,val,rk) in zip(ids5,m5):
    shutil.copy(T3+p,OUT+f'M005/{idn}.png')
    key[idn]=dict(set='M-005',source=f'.wt/T-003/kaiju/evidence/T-003/{p}',what=what,branch=T3C,commit=T3COMMIT,
      tool_command='Unity batch GeoBatch.All (EVIDENCE.md step 4) then Pipelines/geo/m005_check.py check -> m005_report.json; plates by Pipelines/geo/plates.py',
      merged='merged: integrated on task/T-006 (ancestor 192012b); not on main',tag='M-005 overall FAIL at this commit (v1: 3 sub-measures fail incl. C5 default share, C8 skyline vs R-ind); v1.1 skyline gates pass',
      metric_values=val or 'no per-capture metric value (illustrative capture; set-level M-005 results in evidence/T-003/m005_report.json)',intended_rank=rk,
      note='no pipeline control exists that degrades M-005 on purpose; this set has no clearly-bad end (stated, not padded)')
# ---------------- WALK
P='origin/task/T-005:kaiju/evidence/T-005/'
walk=['stand','start','walk_fr020','stop','estop','turn90_inplace','curve','stomp','intent_over_limit','shove_060']
idsw=[f'WALK-{i:02d}' for i in range(1,11)]; random.Random(60061).shuffle(idsw)
def git(*a): return subprocess.run(['git','-C',REPO]+list(a),capture_output=True).stdout
r1=json.loads(git('show',P+'metrics/M-001/report.json')); r2=json.loads(git('show',P+'metrics/M-002/report.json'))
for idn,w in zip(idsw,walk):
    open(OUT+f'WALK/{idn}.png','wb').write(git('show',P+f'strips/{w}.quarter.png'))
    times=git('show',P+f'strips/{w}.times.txt').decode().splitlines()[0]
    key[idn]=dict(set='M-001/M-002 walk strip',scenario=w,source=f'git:origin/task/T-005:kaiju/evidence/T-005/strips/{w}.quarter.png',branch='task/T-005',commit='bedecf4',
      tool_command='unity run . -- -executeMethod Game.Units.Mech.Editor.ScenarioCli.Run -scenarios all -out evidence/T-005 -strips (EVIDENCE.md 0.2); metrics: T-002 tools metrics.py m001/m002 at ef3b834',
      merged='UNMERGED: branch task/T-005 is not on main or on the integration branch',tag='UNMERGED, NOT AT TARGET PACE (3.07 m/s, Froude 0.023 vs gate 0.17-0.23; M-001 FAIL, M-002 FAIL)',
      frame_times=times,metric_values='set-level only (no per-strip values): M-001.1 0.02258 Fr, M-001.3 0.3292, M-001.5 2.477, M-001.8 85.66; M-002.1 0.2461 L_f, M-002.2 0.034 L_f',intended_rank=None)
    rater_caps[idn]=(None,None)
json.dump(key,open(OUT+'answer_key.json','w'),indent=1)
json.dump(dict(m003=r5 and None),open('/dev/null','w'))
# captions (rater-facing, non-revealing)
cap=['# M0 packet captions (rater-facing)\n','Captions name only what the image is. Branch, commit, tool, command, control/toggle and merged status for every ID are in `answer_key.json`, opened after rating. Tags below mark unmerged or reconstructed work only.\n']
cap.append('\n## M-003 stills (PNG, 1920x1080 output, same viewing size)')
for i in sorted(i for i in key if i.startswith('M003-') and i[5:].isdigit() and int(i[5:])<=10): cap.append(f'- {i}: palette-quantised render, one frame, rate overall style quality.')
cap.append('- M003-11, M003-12: reserved for the T-006 hero frames (TODO, not yet added).')
cap.append('- M003-A1..A4 (anchors/): example frames for scale calibration; two are clearly good and two clearly bad (the key says which after rating).')
cap.append('\n## M-004 sequences (GIF, 60 frames at 50 fps, x2 integer scale; lossless frames in the git-ignored bulk/ folder)')
for i in ids4: cap.append(f'- {i}: moving-camera sequence, 60 frames, rate how still the dither and edges are while the camera moves.')
cap.append('\n## M-005 city views (PNG)')
for i in ids5: cap.append(f'- {i}: view or plate of the San Francisco tile; rate recognisability and correctness of the city.')
cap.append('\n## M-001/M-002 walk strips (PNG contact sheets)  [UNMERGED, NOT AT TARGET PACE: branch task/T-005, 3 m/s versus the 9 m/s gate]')
for i in idsw: cap.append(f'- {i}: contact sheet of one mech scenario (24 tiles left to right, top to bottom); rate how a 10,000 t mech should look and move.  [UNMERGED, NOT AT TARGET PACE]')
open(OUT+'captions.md','w').write('\n'.join(cap)+'\n')
open(OUT+'.gitignore','w').write('bulk/\n')
# sheets: csv + template json
import itertools
SH={'M-003':('M003',ids+[],'M-003'),'M-004':('M004',ids4,'M-004'),'M-005':('M005',ids5,'M-005'),'M-001':('WALK',idsw,'M-001')}
meta=dict(ids3=ids,ids4=ids4,ids5=ids5,idsw=idsw,anchors=anchors)
json.dump(meta,open('/private/tmp/claude-501/s/meta.json','w'))
# per-capture derived reports for dry run
for idn,(rep,_) in rater_caps.items():
    pass
print('ok'); print(json.dumps({k:(v.get('intended_rank'),v.get('metric_values_frame') or v.get('metric_values_sequence')) for k,v in key.items() if k.startswith(('M003-0','M003-10','M004'))},indent=0)[:3000])
