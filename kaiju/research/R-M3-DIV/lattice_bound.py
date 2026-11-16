"""R-M3-DIV: worst-case C11 displacement error for N slabs forced onto ONE lattice (period k output px) with an integer common phase p.
Model of T-196 PresentShift: slab i's ideal shift r_i (real, output px); applied shift = p + k*m_i (integers). error_i = min_m |r_i - p - k m|.
Worst case over all residue tuples (r_i mod k), minimax over integer p.  Also: per-slab (free integer shift) bound = 0.5."""
import itertools, numpy as np, json
def circ(a,b,k): d=(a-b)%k; return min(d,k-d)
def bound(k,n,step=0.1):
    g=np.arange(0,k,step); worst=0; arg=None
    for rest in itertools.product(g,repeat=n-1):
        r=(0.0,)+rest   # global offset is irrelevant only up to integer p: sweep fraction of r0 too
        for f in (0.0,0.25,0.5,0.75):
            rr=[ri+f for ri in r]
            m=min(max(circ(x,p,k) for x in rr) for p in range(k))
            if m>worst: worst,arg=m,rr
    return round(worst,3),arg
out={}
for k in (2,3,4,6):
    for n in (2,3):
        w,a=bound(k,n,0.25 if n==3 else 0.1); out[f"k{k}_N{n}"]=w
        print("k",k,"slabs",n,"worst minimax",w,"formula k/4+0.5 =",k/4+0.5, "arg",a)
json.dump(out,open("research/R-M3-DIV/out_lattice_bound.json","w"),indent=1)
