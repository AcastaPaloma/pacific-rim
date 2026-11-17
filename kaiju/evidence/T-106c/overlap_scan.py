import sys, json, os, time, multiprocessing as mp
import numpy as np, shapely
from shapely.geometry import shape
from shapely import wkb
sys.path.insert(0,'.')
from kaiju_geo.common import parse_tile
R='cache/bake/tiles/v1/'
BAND=300.0
def one(tid):
    t=parse_tile(tid); x0,z0,x1,z1=t.bounds
    out=[]
    try: feats=json.load(open(R+t.folder+'/buildings.geojson'))['features']
    except Exception: return out
    for f in feats:
        p=f['properties']
        if p.get('excluded'): continue
        geom=p.get('remainder_geometry') if p.get('has_parts') else f['geometry']
        if not geom: continue
        g=shape(geom); b=g.bounds
        cross = b[0]<x0 or b[1]<z0 or b[2]>x1 or b[3]>z1
        if cross or b[0]<x0+BAND or b[1]<z0+BAND or b[2]>x1-BAND or b[3]>z1-BAND:
            out.append((tid,p.get('part_of') or f['id'],wkb.dumps(g),cross,p.get('height_source'),p.get('height_m')))
    return out
if __name__=='__main__':
    tiles=sorted('L0_'+d for d in os.listdir(R+'L0') if os.path.exists(R+'L0/'+d+'/manifest.json'))
    if len(sys.argv)>1: tiles=[t for t in tiles if t in set(sys.argv[1].split(','))]
    t0=time.time(); items=[]
    with mp.Pool(2) as P:
        for k,r in enumerate(P.imap(one,tiles,chunksize=8)):
            items+=r
            if k%500==0: print(k,len(items),time.time()-t0,flush=True)
    geoms=[wkb.loads(i[2]) for i in items]
    tree=shapely.STRtree(geoms); res=[]
    for k,g in enumerate(geoms):
        for m in tree.query(g):
            m=int(m)
            if m<=k: continue
            a_,b_=items[k],items[m]
            if a_[0]==b_[0] or a_[1]==b_[1] or not (a_[3] or b_[3]): continue
            a=g.intersection(geoms[m]).area
            if a>0.05: res.append((round(a,1),a_[0],a_[1],a_[4],a_[5],b_[0],b_[1],b_[4],b_[5]))
    res.sort(reverse=True)
    print(len(res))
    for r in res: print(r)
    json.dump(res,open('/private/tmp/claude-501/scratch/ovl_before.json','w'))
