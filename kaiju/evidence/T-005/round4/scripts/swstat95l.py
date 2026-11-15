import json,sys,glob,statistics as st,collections
W=9.81e7; LIM=[1.176e9,0.784e9,0.98e9]
def run(f):
    S=[json.loads(l) for l in open(f) if l.startswith('{"rec":"step"')]
    on=[None,None]; lift=[None,None]; res=[]
    for k,r in enumerate(S):
        if r['t']<9: continue
        for i,fo in enumerate(r['feet']):
            o=fo['fn']>0.03*W
            if on[i] is None: on[i]=o; continue
            if on[i] and not o and fo['p'][1]>0.3: lift[i]=k; on[i]=False
            elif not on[i] and o:
                on[i]=True
                if lift[i] is not None:
                    dur=r['t']-S[lift[i]]['t']
                    if dur>0.4:
                        seg=S[lift[i]:k+1]; n=h=kn=0
                        for q in seg:
                            qq=q.get('qp') or {}; kk=qq.get('k'); tau=qq.get('tau')
                            if not kk or not tau: continue
                            sw=int(kk[6]); n+=1; h+=abs(tau[3*sw])>0.97*LIM[0]; kn+=abs(tau[3*sw+1])>0.97*LIM[1]
                        res.append((dur,n,h,kn))
                lift[i]=None
    return res
tot=collections.defaultdict(list)
for pat in sys.argv[1:]:
    for f in sorted(glob.glob(pat)):
        import re; name=re.sub(r's\d\.gait.*','',f.split('@')[1]); tot[name]+=run(f)
for k,v in tot.items():
    n=sum(x[1] for x in v)
    print('%-9s swings %3d dur median %.2f | hip sat %4.1f%% knee sat %4.1f%% of swing ticks'%(k,len(v),st.median([x[0] for x in v]) if v else 0,100*sum(x[2] for x in v)/max(n,1),100*sum(x[3] for x in v)/max(n,1)))
