import math
K=4; REF=664.874; Z=[124.2,664.874,10353.0]; RHO=[REF/z for z in Z]
SPEED=0.0902; START=0.6153; FR=180; PH=16; LIM=8
def wrap(x): return (x+2.0)%4.0-2.0
def nearest(t,p): 
    m=round((t-p)/K); s=p+K*m
    while s>LIM and s-K>=-LIM: s-=K
    while s<-LIM and s+K<=LIM: s+=K
    return s
def axis(ideal,tau,main_round):
    best=None
    for p in range(-LIM,LIM+1):
        e=[abs(nearest(ideal[i]-tau[i],p)-(ideal[i]-tau[i])) for i in range(3)]
        key=(round(max(e),9),round(sum(x*x for x in e),9),abs(p-main_round),p)
        if best is None or key<best[0]: best=(key,p)
    p=best[1]; return [nearest(ideal[i]-tau[i],p) for i in range(3)]
def run(mode,phis,c_off,win):
    xs=[[] for _ in range(3)]; cm=[[] for _ in range(3)]; tau=[0,0,0]
    for i in range(FR):
        c=SPEED*i+START+c_off; ideal=[]; base=[]
        for s in range(3):
            r=RHO[s]; S=math.ceil(c*r)/r; u=r*(0-S)+phis[s]; base.append(K*round(u)); ideal.append(K*r*(c-S))
        if mode=='indep': sh=[round(v) for v in ideal]
        else:
            if i==0 and mode=='state':
                sh=axis(ideal,[0,0,0],round(ideal[1])); tau=[ideal[s]-sh[s] for s in range(3)]
            sh=axis(ideal,tau,round(ideal[1]-tau[1]))
        for s in range(3): xs[s].append(base[s]-sh[s]); cm[s].append(K*RHO[s]*(0-c))
    out=[]
    for s in range(3):
        a,b=win[s]; x=xs[s][a:b]; c=cm[s][a:b]
        out.append(max(abs((x[i]-x[0])-(c[i]-c[0])) for i in range(len(x))))
    return out
for win,name in [([(0,180)]*3,'full'),([(54,180),(0,180),(0,180)],'near from 54'),([(0,126),(30,180),(0,180)],'near 0-126, ref from 30')]:
  for mode in ['indep','abs','state']:
    W=[[] for _ in range(3)]
    for j in range(24):
        c_off=((j+0.5)*0.6180339887)%1.0*7
        for p in range(PH):
            w=run(mode,[(p+0.37)/PH]*3,c_off,win)
            for s in range(3): W[s].append(w[s])
    print(name,mode,'worst',[round(max(w),2) for w in W],'median',[round(sorted(w)[len(w)//2],2) for w in W])
