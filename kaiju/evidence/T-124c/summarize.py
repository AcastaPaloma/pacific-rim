import json,sys,glob,re
def pct(a,p):
    a=sorted(a); k=(len(a)-1)*p/100.0; i=int(k); j=min(i+1,len(a)-1); return a[i]+(a[j]-a[i])*(k-i)
for d in sys.argv[1:]:
    print("==",d)
    for f in sorted(glob.glob(d+'/m008_bay_seed*.json')):
        j=json.load(open(f)); fr=j['frames']; t=fr['total_ms']; r=fr.get('render_ms')
        s=j['streaming']; ip=s['item_profile']
        h=ip['hitches'].split(' || ') if ip['hitches'] else []
        top=sorted(t,reverse=True)[:3]
        print("seed %d cost=%s n=%d p99=%.2f p999=%.2f max=%.1f >33.3:%d >16.7:%d >12:%d late=%d holds=%d maxitem=%.1f %s"%(j['seed'],j.get('cost_version','v1?'),len(t),pct(t,99),pct(t,99.9),max(t),sum(x>33.3 for x in t),sum(x>16.7 for x in t),sum(x>12 for x in t),s['late_tile_frames'],s['physics_holds'],s['max_work_item_ms'],
             ("render p99=%.2f max=%.1f >33.3:%d"%(pct(r,99),max(r),sum(x>33.3 for x in r)) if r else "")))
        x=j.get('t124c')
        if x: print("   t124c",{k:x[k] for k in x})
        print("   destr spawn_ms_per_piece",j['destruction']['spawn_ms_per_piece'],"frag peak",j['destruction']['fragments_peak'])
        # attribute >12 ms frames
        cat={'brain':0,'strm':0,'destr':0,'phys':0}
        for e in h:
            m=dict(re.findall(r'(\w+)=([\d.]+)',e))
            if not m: continue
            tot=float(m['tot']); 
            c=max(('brain','strm','destr','phys'),key=lambda k:float(m[k])); cat[c]+=1
        print("   hitch(>12, first %d logged) dominant:"%len(h),cat)
