import json,sys,math,glob,statistics as st,collections
W=9.81e7
def rot(q,v):
    x,y,z,w=q; cr=(y*v[2]-z*v[1], z*v[0]-x*v[2], x*v[1]-y*v[0]); dot=x*v[0]+y*v[1]+z*v[2]; uu=x*x+y*y+z*z
    return [2*dot*u+(w*w-uu)*vv+2*w*c for u,vv,c in zip((x,y,z),v,cr)]
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
                        fz=rot(fo['q'],[0,0,1]); c=fo['cop']; p=fo['p']; along=sum((c[j]-p[j])*fz[j] for j in range(3))
                        res.append((dur,along>0,r['t']))
                lift[i]=None
    return res
tot=collections.defaultdict(list)
for pat in sys.argv[1:]:
    for f in sorted(glob.glob(pat)):
        name=f.split('@')[1].rsplit('s',1)[0].split('.')[0]
        r=run(f); tot[name]+= [x for x in r if x[2]<20]
for k,v in tot.items():
    d=[x[0] for x in v]
    print('%-9s n %3d swing dur median %.2f mean %.2f  <1.5s: %3.0f%%  toe-first TD: %3.0f%%'%(k,len(v),st.median(d),sum(d)/len(d),100*sum(x<1.5 for x in d)/len(d),100*sum(x[1] for x in v)/len(v)))
