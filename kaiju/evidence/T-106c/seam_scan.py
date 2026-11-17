import sys, json, numpy as np, os
R='cache/bake/tiles/v1/L0/'
tiles=sorted(os.listdir(R)); S={}
for t in tiles:
    if not os.path.exists(R+t+'/manifest.json'): continue
    m=json.load(open(R+t+'/manifest.json')); l=next(x for x in m['layers'] if x['layer']=='terrain_r16')
    raw=np.memmap(R+t+'/terrain.r16','<u2',mode='r',shape=(2049,2049)); sc=(l['h_max_m']-l['h_min_m'])/65535.0
    dec=lambda a:l['h_min_m']+np.asarray(a,float)*sc
    S[tuple(map(int,t.split('_')))]=dict(e=dec(raw[:,-1]),w=dec(raw[:,0]),n=dec(raw[-1,:]),s=dec(raw[0,:]))
bad=[]
for (i,j),a in S.items():
    for dx,dz in((1,0),(0,1)):
        b=S.get((i+dx,j+dz))
        if not b: continue
        E=np.abs(a['e']-b['w']) if dx else np.abs(a['n']-b['s'])
        p,m=np.percentile(E,99),E.max()
        if p>0.2 or m>0.5: bad.append((f'L0_{i:02d}_{j:02d}|L0_{i+dx:02d}_{j+dz:02d}',round(p,3),round(m,3),int((E>0.05).sum())))
print(len(S),len(bad))
for b in sorted(bad): print(b)
json.dump([b[0] for b in bad],open(sys.argv[1],'w'))
