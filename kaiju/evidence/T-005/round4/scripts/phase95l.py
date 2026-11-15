import json,sys,glob,collections
TS=4.41; LIM=[1.176e9,0.784e9,0.98e9]
for pat in sys.argv[1:]:
    b=collections.defaultdict(lambda:[0,0,0.0,0,0]); 
    for f in glob.glob(pat):
        for l in open(f):
            if not l.startswith('{"rec":"step"'): continue
            r=json.loads(l)
            if r['t']<9: continue
            q=r.get('qp') or {}; kk=q.get('k'); tau=q.get('tau')
            if not kk or not tau or kk[5]>0 : pass
            if not kk or not tau: continue
            sw=int(kk[6]); inc=int(kk[11]) if len(kk)>11 else 0
            if (inc>>(2*sw))&3: continue   # swing leg has contact
            i=min(9,max(0,int(kk[8]/0.5)))
            x=b[i]; x[0]+=1; x[1]+=abs(tau[3*sw])>0.97*LIM[0]; x[2]+=abs(tau[3*sw])/LIM[0]; x[3]+=abs(tau[3*sw+1])>0.97*LIM[1]; x[4]+=1 if kk[9]>0 else 0
    print(pat.split('/')[-1]); 
    for i in sorted(b): x=b[i]; print('  tau %.1fs n %5d hip sat %4.0f%% mean|tau|/LIM %.2f knee sat %3.0f%% late %3.0f%%'%(i*0.5,x[0],100*x[1]/x[0],x[2]/x[0],100*x[3]/x[0],100*x[4]/x[0]))
