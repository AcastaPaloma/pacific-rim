import json,numpy as np,sys
f=sys.argv[1]; t0=float(sys.argv[2]); t1=float(sys.argv[3])
R=[json.loads(l) for l in open(f)]
R=[r for r in R if r.get('rec')=='step' and r.get('qp') and t0<r['t']<t1]
yd=np.array([r['qp']['yd'] for r in R]); 
print(f.split('/')[-1][:30], 'n',len(R),'rms Llegs %.2e Larms %.2e Lrest %.2e LT %.2e  tgt %.2e'%tuple(np.sqrt((yd[:,i]**2).mean()) for i in (7,8,4,0,3)), 'ratio rest/legs %.2f'%(np.sqrt((yd[:,4]**2).mean())/np.sqrt((yd[:,7]**2).mean())))
