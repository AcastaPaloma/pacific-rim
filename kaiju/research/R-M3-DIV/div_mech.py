"""R-M3-DIV: mechanism demo. Synthetic layered frame (1920x1080, k=4): three slab regions, each an exact k-lattice at its own offset.
Single declared offset (main) vs per-region offsets, M-003.d-iv uniformity rule (max-min <= 1 per channel). Content statistics are ASSUMED
(texel is a continuation of the left neighbour with probability flat, else a new palette colour), so absolute numbers are a mechanism
illustration, not a measurement; T-196's measured 0.61-0.73 is the reference."""
import numpy as np, json
rng=np.random.default_rng(7); k=4; H,W=1080,1920
pal=np.array([[40,50,70],[90,100,130],[170,120,100],[230,190,140],[250,245,200]],np.int16)
def lattice_region(offx,offy,h,w,flat):
    ty=(h+2*k)//k+2; tx=(w+2*k)//k+2; idx=rng.integers(0,len(pal),(ty,tx))
    for x in range(1,tx):
        keep=rng.random(ty)<flat; idx[:,x]=np.where(keep,idx[:,x-1],idx[:,x])
    img=np.repeat(np.repeat(pal[idx],k,0),k,1)
    return img[(k-offy)%k+k:(k-offy)%k+k+h,(k-offx)%k+k:(k-offx)%k+k+w]
def blocks_uniform(img,offx,offy,mask):
    h,w,_=img.shape; ys=range(offy-k if offy else 0,h-k+1,k); n=u=0
    for y0 in range(offy%k - (k if offy%k else 0)+k,h-k+1,k):
        for x0 in range(offx%k - (k if offx%k else 0)+k,w-k+1,k):
            if not mask[y0:y0+k,x0:x0+k].all(): continue
            b=img[y0:y0+k,x0:x0+k].reshape(-1,3); n+=1; u+= int((b.max(0)-b.min(0)).max()<=1)
    return u/max(n,1),n
def run(flat,offs=((0,0),(2,1),(1,3)),shares=(0.3,0.4,0.3)):
    frame=np.zeros((H,W,3),np.int16); masks=[]; r=0
    rows=[int(H*s) for s in np.cumsum(shares)]; rows[-1]=H; prev=0
    for (ox,oy),e in zip(offs,rows):
        m=np.zeros((H,W),bool); m[prev:e]=True; reg=lattice_region(ox,oy,H,W,flat); frame[m]=reg[m]; masks.append(m); prev=e
    full=np.ones((H,W),bool)
    single=blocks_uniform(frame,*offs[1],full)[0]       # declared offset = main slab (offs[1])
    per=[blocks_uniform(frame,*o,m)[0] for o,m in zip(offs,masks)]
    return round(single,3),[round(p,3) for p in per]
out={f"flat{f}":run(f) for f in (0.5,0.8,0.9)}
print(json.dumps(out)); json.dump(out,open("research/R-M3-DIV/out_div_mech.json","w"))
