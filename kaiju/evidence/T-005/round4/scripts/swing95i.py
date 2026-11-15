import gzip,json,sys,math
f=sys.argv[1]
# touchdown detection: a foot goes from fn<0.03W to >0.03W ; track per-foot liftoff and touchdown; print swing durations with ankle-limit / hip / knee peak
W=9.81e7; st=[None,None]; lift=[None,None]; stats=[None,None]
for l in gzip.open(f,'rt'):
    r=json.loads(l)
    if r.get('rec')!='step' or r['t']<9: continue
    q=r.get('qp') or {}
    for i,fo in enumerate(r['feet']):
        on=fo['fn']>0.03*W
        if st[i] is None: st[i]=on; continue
        if st[i] and not on and fo['p'][1]>0.3: # liftoff (ignore flicker by requiring height)
            lift[i]=r['t']; stats[i]=dict(amin=0,hmax=-99,kmax=0,vz0=r['com_v'][2],row=q.get('row'),trow=[])
            st[i]=False
        elif not st[i] and on:
            if lift[i] is not None and r['t']-lift[i]>0.5:
                s=stats[i]
                print('%s swing %6.2f-%6.2f dur %4.2f row %s | sw ankle min %5.1f (ticks at -20: %3d) hip max %5.1f knee max %5.1f | vz lift %4.2f td %4.2f'%('LR'[i],lift[i],r['t'],r['t']-lift[i],s['row'],s['amin'],s.get('nlim',0),s['hmax'],s['kmax'],s['vz0'],r['com_v'][2]))
            st[i]=True; lift[i]=None
        if lift[i] is not None and not on and r.get('angles') and r['ctl']['stance']!=i:
            a=r['angles']['sw_meas']; s=stats[i]
            s['amin']=min(s['amin'],a[2]); s['hmax']=max(s['hmax'],a[0]); s['kmax']=max(s['kmax'],a[1])
            if a[2]<-19.8: s['nlim']=s.get('nlim',0)+1
    if r['ctl']['phase']=='Fallen': print('FALL',r['t']); break
