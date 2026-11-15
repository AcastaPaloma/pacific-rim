import foot_sim as fs
print('sole pitch at landing (deg, + toe down) | pitch gain kpf | time to flat s | peak ankle tau/limit | mean |CoP meas - cmd| m | final pitch deg')
for phi0 in (-12.0, -6.0, 0.0, 6.0, 12.0, 20.0):
    for kpf in (0.0, 1e8, 3e8, 1e9):
        r = fs.contact_test(phi0, kpf)
        print('%6.1f | %.0e | %s | %.2f | %s | %.1f' % (phi0, kpf, ('%.2f' % r['tflat']) if r['tflat'] else 'never', r['peak'], ('%.2f' % r['cop_err']) if r['cop_err'] is not None else '-', r['phi_end']), flush=True)
