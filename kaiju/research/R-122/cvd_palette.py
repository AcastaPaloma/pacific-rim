"""R-122: colour-blind and contrast audit of the 5 pixel palette profiles (read only).
Usage: python cvd_palette.py <repo kaiju root>.  Machado 2009 severity-1.0 matrices (linear RGB), CIE76/CIE2000 via skimage-free code."""
import re, glob, sys, itertools, json, math
import numpy as np
M = {"protan": [[0.152286,1.052583,-0.204868],[0.114503,0.786281,0.099216],[-0.003882,-0.048116,1.051998]],
     "deutan": [[0.367322,0.860646,-0.227968],[0.280085,0.672501,0.047413],[-0.011820,0.042940,0.968881]],
     "tritan": [[1.255528,-0.076749,-0.178779],[-0.078411,0.930809,0.147602],[0.004733,0.691367,0.303900]]}
def lin(c): c=np.asarray(c)/255.; return np.where(c<=0.04045,c/12.92,((c+0.055)/1.055)**2.4)
def enc(l): l=np.clip(l,0,1); return np.where(l<=0.0031308,l*12.92,1.055*l**(1/2.4)-0.055)*255
def lab(rgb):
    l=lin(rgb); m=np.array([[.4124,.3576,.1805],[.2126,.7152,.0722],[.0193,.1192,.9505]]); xyz=m@l/np.array([.95047,1,1.08883])
    f=np.where(xyz>216/24389,np.cbrt(xyz),(24389/27*xyz+16)/116); return np.array([116*f[1]-16,500*(f[0]-f[1]),200*(f[1]-f[2])])
def sim(rgb,k): return enc(np.array(M[k])@lin(rgb))
def de(a,b): return float(np.linalg.norm(lab(a)-lab(b)))
def y(rgb): return float(np.array([.2126,.7152,.0722])@lin(rgb))
def cr(a,b): ya,yb=sorted([y(a),y(b)],reverse=True); return (ya+.05)/(yb+.05)
def load(root):
    out={}
    for f in sorted(glob.glob(root+'/Assets/Game/Config/Rendering/PaletteProfiles/*.asset')):
        v=[int(x) for x in re.findall(r'rgba: (\d+)',open(f).read())]
        cols=[((c&255),((c>>8)&255),((c>>16)&255)) for c in v]
        out[f.split('/')[-1][:-6]]=sorted(set(cols))
    return out
if __name__=="__main__":
    P=load(sys.argv[1]); res={}
    for n,cols in P.items():
        cols=[np.array(c,float) for c in cols]; L=[lab(c)[0] for c in cols]
        dark=cols[int(np.argmin(L))]; light=cols[int(np.argmax(L))]
        # accent candidates: colours with chroma >= 25, sorted by hue family
        ch=[math.hypot(*lab(c)[1:]) for c in cols]
        # HUD plan: text = lightest, panel = darkest; 4 gauge hues = 4 most mutually distinct chromatic colours (greedy, max-min dE under worst CVD)
        cand=[i for i in range(len(cols)) if L[i]>35 and ch[i]>=12]
        def worst(a,b): return min([de(cols[a],cols[b])]+[de(sim(cols[a],k),sim(cols[b],k)) for k in M])
        best=None
        for comb in itertools.combinations(cand,min(4,len(cand))) if len(cand)<=22 else []:
            m=min(worst(a,b) for a,b in itertools.combinations(comb,2))
            if best is None or m>best[0]: best=(m,comb)
        # pairwise dL* between 4 gauge fills (CVD-safe because lightness survives CVD)
        d={'n_colours':len(cols),'text_on_panel_contrast':round(cr(light,dark),2),'L_range':[round(min(L),1),round(max(L),1)],
           'chromatic_candidates':len(cand)}
        if best:
            m,comb=best; d['best4_min_dE_worst_cvd']=round(m,1)
            d['best4_min_dL']=round(min(abs(L[a]-L[b]) for a,b in itertools.combinations(comb,2)),1)
            d['best4_hex']=['%02x%02x%02x'%tuple(int(v) for v in cols[i]) for i in comb]
            d['best4_vs_panel_contrast_min']=round(min(cr(cols[i],dark) for i in comb),2)
        res[n]=d
    print(json.dumps(res,indent=1))
