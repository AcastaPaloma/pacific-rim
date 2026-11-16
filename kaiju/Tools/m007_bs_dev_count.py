import sys,os,glob,collections
sys.path.insert(0,'Tools'); import m007_bay as M
from multiprocessing import Pool
def one(f):
    r=M.load_run(f); p=M.compute_run(r,dict(M.CFG,rev=2)); return f,p
if __name__=='__main__':
    files=M.expand(sys.argv[1:])
    with Pool(8) as pool: res=pool.map(one,files)
    agg=collections.defaultdict(lambda:collections.Counter())
    for f,p in res:
        k=(p.get('brain'),p.get('comp')) if 'brain' in p else (os.path.basename(f))
        agg[k]['n']+=1
        for q in('bs_opp_any','bs_ev_any','bs_opp_strict','bs_ev_strict'): agg[k][q]+=p.get(q,0)
    for k,v in sorted(agg.items(),key=str): print(k,dict(v))
    print(list(res[0][1].keys())[:40])
