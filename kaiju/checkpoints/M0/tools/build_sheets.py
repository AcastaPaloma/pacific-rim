import json,os,csv,random,subprocess,shutil,gzip,statistics
from scipy.stats import spearmanr
W='/Users/kuanw/Me/code/pacific-rim/.claude/worktrees/fable-orchestrator-prompt-83a0a8/.wt/'
OUT=W+'T-006/kaiju/checkpoints/M0/'; REPO=W+'T-006/kaiju'; T4=W+'T-004/kaiju/evidence/T-004/'
PY='/opt/miniconda3/envs/kaiju/bin/python'
key=json.load(open(OUT+'answer_key.json'))
def rj(p): return json.load(gzip.open(p)) if p.endswith('.gz') else json.load(open(p))
sets={'M-003':('M003',[k for k in key if k.startswith('M003-') and k[5:].isdigit() and int(k[5:])<=10]),
      'M-004':('M004',[k for k in key if k.startswith('M004-')]),'M-005':('M005',[k for k in key if k.startswith('M005-')]),
      'M-001':('WALK',[k for k in key if k.startswith('WALK-')])}
anch=sorted(k for k in key if k.startswith('M003-A'))
# ---- sheets
os.makedirs(OUT+'sheets',exist_ok=True)
for m,(d,ids) in sets.items():
    rows=sorted(ids)+(anch if m=='M-003' else [])
    with open(OUT+f'sheets/{m}_sheet.csv','w',newline='') as f:
        w=csv.writer(f); w.writerow(['capture','founder_a','founder_b','notes'])
        for i in rows: w.writerow([i,'','',''])
    tpl={"schema":"panel_ratings@v1","metric":m,"_note":"Template: scores are null until rated; fill the CSV and run make_ratings.py (it writes the file calibrate reads). Rating = overall score 1..10, 10 = clearly good. Do not open answer_key.json before rating.",
         "raters":[{"rater_id":"founder_a","rater_type":"human","blind":True},{"rater_id":"founder_b","rater_type":"human","blind":True}],
         "ratings":[{"rater_id":r,"build":"M0-packet","capture":i,"scenario":"overall","score":None,"notes":""} for i in rows for r in ("founder_a","founder_b")]}
    json.dump(tpl,open(OUT+f'sheets/{m}.ratings.template.json','w'),indent=1)
open(OUT+'sheets/M-003_rubric.md','w').write("""# M-003 f rating rubric (what the founders score)
Source: `research/R-002.md` section 4, sub-measure f (panel rating), as read by `Tools/Metrics/kaiju_metrics/ratings.py` (`M-003 f (R-002 section 4 f)`).
**The band wording of R-002 section 4 f is NOT quoted here**: the packet builder was not permitted to read `research/R-002.md`. Paste the bands under this line before the sheet goes out (open item, T-006 C19).

Scale the tool reads: one integer or decimal score 1..10 per capture per rater, 10 = clearly good pixel-art style, 1 = clearly bad.
Anchors (M003-A1..A4: two clearly good, two clearly bad, shown blind): a rater is excluded if a negative anchor scores 6 or more, or positive anchors are not rated above negative ones (`calibrate`, `ratings.py`). Expect good anchors 9-10, bad anchors 1-3.
Pass line used by `calibrate`: median rating >= 8.0 counts as good.
""")
shutil.copy('/private/tmp/claude-501/s/make_ratings.py',OUT+'make_ratings.py') if os.path.exists('/private/tmp/claude-501/s/make_ratings.py') else None
print('sheets ok')
