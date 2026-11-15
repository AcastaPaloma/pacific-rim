import re,statistics as st,collections,sys
d=collections.defaultdict(list)
for l in open(sys.argv[1]):
    m=re.match(r'walk_fr010@(\w+?)s(\d)\s.*?at=([\d.]+)',l)
    if m: d[m.group(1)].append(float(m.group(3)))
for k,v in d.items(): print('%-9s %s median %.1f best %.1f'%(k,v,st.median(v),max(v)))
