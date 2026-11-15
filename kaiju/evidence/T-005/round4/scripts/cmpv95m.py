import json,math,sys,glob,re,collections
d=sys.argv[1]
at={}
for l in open(d+'/out/summary.txt'):
    m=re.match(r'walk_fr010@(\w+?)s(\d)\s.*?at=([\d.]+)',l)
    if m: at[(m.group(1),m.group(2))]=float(m.group(3))
for f in sorted(glob.glob(d+'/out/gait/*.gait.jsonl')):
    m=re.search(r'@(\w+?)s(\d)\.',f); k=(m.group(1),m.group(2))
    rows=[]
    for l in open(f):
        if l.startswith('{"rec":"step"'):
            try: rows.append(json.loads(l))
            except: pass
    rt={}; sp=[]; mx=0
    for r in rows:
        if r['t']>at[k]: break
        q=r.get('qp')
        if q and q.get('row') is not None:
            rt.setdefault(q['row'],round(r['t'],1)); mx=max(mx,q['row'])
            if r['t']>=12: sp.append(math.hypot(r['com_v'][0],r['com_v'][2]))
    print('%-7s s%s at %5.1f maxrow %d v(12s-fall) mean %.2f max %.1f rows %s'%(k[0],k[1],at[k],mx,sum(sp)/max(1,len(sp)),max(sp) if sp else 0,rt))
