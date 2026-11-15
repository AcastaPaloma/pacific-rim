import json,sys,math
f=sys.argv[1]; W=9.81e7; LIM=[1.176e9,0.784e9,0.98e9]
S=[]
for l in open(f):
    if l.startswith('{"rec":"step"') or l.startswith('{"rec": "step"'): S.append(json.loads(l))
on=[None,None]; lift=[None,None]; out=[]
for k,r in enumerate(S):
    if r['t']<9: continue
    for i,fo in enumerate(r['feet']):
        o=fo['fn']>0.03*W
        if on[i] is None: on[i]=o; continue
        if on[i] and not o and fo['p'][1]>0.3: lift[i]=k; on[i]=False
        elif not on[i] and o:
            on[i]=True
            if lift[i] is not None and r['t']-S[lift[i]]['t']>0.4: out.append((i,lift[i],k))
            lift[i]=None
    if r['ctl']['phase']=='Fallen': print('FALL',r['t']); break
for i,a,b in out:
    seg=S[a:b+1]; T=seg[-1]['t']-seg[0]['t']
    n=0; pd=[0,0,0]; qt=[0,0,0]; dom=[0,0,0]; ex=[]; ez=[]; ad=[]; lt=0; late=0; fps=0; tref0=None
    for r in seg:
        q=r.get('qp') or {}; kk=q.get('k'); tau=q.get('tau')
        if not kk or not tau: continue
        sw=int(kk[6]); n+=1
        for j in range(3):
            p=kk[3*sw+j]; t=tau[3*sw+j]; pd[j]+=abs(p); qt[j]+=abs(t)
            if abs(p)>abs(t): dom[j]+=1
        if kk[10]>0.5 or True:
            ex.append(kk[12]); ez.append(kk[13]); ad.append(math.hypot(kk[14],kk[15]))
        lt+= kk[16]>0.5; late=max(late,kk[9]); fps+=kk[19]
        if tref0 is None: tref0=kk[7]
    if n==0: continue
    print('%s swing %6.2f-%6.2f dur %4.2f tref0 %.1f | mean|PD| / mean|QP tau| (1e6): hip %4.0f/%4.0f knee %4.0f/%4.0f ank %4.0f/%4.0f | PD>QP ticks %%: %3.0f %3.0f %3.0f | heel err x mean %5.1f max %5.1f z mean %5.1f | |a_cmd| max %5.1f m/s2 | landQ %3.0f%% late max %.2f fps %.2f'%('LR'[i],seg[0]['t'],seg[-1]['t'],T,tref0,pd[0]/n/1e6,qt[0]/n/1e6,pd[1]/n/1e6,qt[1]/n/1e6,pd[2]/n/1e6,qt[2]/n/1e6,*[100*d/n for d in dom],sum(ex)/n,max(ex,key=abs),sum(ez)/n,max(ad),100*lt/n,late,fps/n))
