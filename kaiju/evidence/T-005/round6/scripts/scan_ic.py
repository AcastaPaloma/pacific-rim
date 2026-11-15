import sys
import foot_sim as fs
mode = sys.argv[1] if len(sys.argv) > 1 else 'base'
for v0 in (4.0, 4.3, 4.6, 4.8, 4.97, 5.1, 5.3):
    fs.V0 = v0
    r = fs.run(mode, 40.0, land_pitch_deg=0.0)
    print('V0 %.2f fell %s t %.1f steps %d v %.2f td %s' % (v0, r['fell'], r['t'], r['steps'], r['v'], r['td_pitch'][:6]), flush=True)
