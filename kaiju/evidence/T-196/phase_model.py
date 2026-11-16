# Model of C11 edge error per slab under per-slab shifts vs one shared output lattice phase.
import math, itertools
K=4; REF=664.874; Z=[124.2,664.874,10353.0]; RHO=[REF/z for z in Z]
SPEED=0.0902; START=0.6153; FR=180; PH=16
def rnd(x): return float(round(x))  # python round = half-even
def wrap(x): return (x+2.0)%4.0-2.0
def run(scheme, phis, c_off):
    # per slab: rendered texel edge index (exact lock: edge at the slab's reference depth)
    worst=[0,0,0]; x0=[None]*3; c0=[None]*3; tau=None
    for i in range(FR):
        c=SPEED*i+START+c_off
        ideal=[]; base=[]
        for s in range(3):
            r=RHO[s]; S=math.ceil(c*r)/r; u=r*(0-S)+phis[s]
            base.append(K*rnd(u)); ideal.append(K*r*(c-S))  # real ideal shift (<=0)
        if scheme=='independent': sh=[rnd(v) for v in ideal]
        elif scheme.startswith('common'):
            if tau is None: tau=[0,0,0]
            if scheme=='common_main': p=rnd(ideal[1])
            elif scheme=='common_near': p=rnd(ideal[0])
            else:
                if i==0:
                    p=rnd(ideal[1]); tau=[wrap(ideal[s]-p) for s in range(3)]
                else:
                    best=None
                    for p_ in range(-8,9):
                        e=max(abs(wrap(ideal[s]-p_-tau[s])) for s in range(3))
                        if best is None or e<best[0]-1e-9: best=(e,p_)
                    p=best[1]
            sh=[p+K*rnd((ideal[s]-p-(tau[s] if scheme=='common_minimax' else 0))/K) for s in range(3)]
        for s in range(3):
            x=base[s]-sh[s]; cmd=K*RHO[s]*(0-c)
            if i==0: x0[s]=x; c0[s]=cmd
            worst[s]=max(worst[s],abs((x-x0[s])-(cmd-c0[s])))
    return worst
for scheme in ['independent','common_main','common_near','common_minimax']:
    W=[[] for _ in range(3)]
    for j in range(24):
        c_off=((j+0.5)*0.6180339887)%1.0*7
        for p in range(PH):
            w=run(scheme,[(p+0.37)/PH]*3,c_off)
            for s in range(3): W[s].append(w[s])
    print(scheme, 'worst near/ref/far', [round(max(w),2) for w in W], 'median', [round(sorted(w)[len(w)//2],2) for w in W])
