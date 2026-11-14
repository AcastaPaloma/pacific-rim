"""R-008: why feed-forward from differenced targets hurts, and the waist yaw power. Run from the project root."""
import numpy as np
dt = 1 / 60; I_hip = 1.03e9                     # R-005 swing hip I_eff
for sq_deg in (0.001, 0.01):
    sq = sq_deg * np.pi / 180; sa = np.sqrt(6) * sq / dt**2   # 2nd difference of white noise: var = 6 sigma^2 / dt^4
    print(f"target noise {sq_deg} deg -> acceleration noise {sa:.3f} rad/s2 -> hip torque noise {I_hip*sa:.2e} N m (limit 1.2e9)")
Iup, amp, T = 3.78e8, 25 * np.pi / 180, 6.60; w = 2 * np.pi / T
print(f"waist yaw fundamental at Fr 0.20, 25 deg: torque {Iup*amp*w**2:.2e} N m, peak power {Iup*amp**2*w**3/1e6:.0f} MW")
