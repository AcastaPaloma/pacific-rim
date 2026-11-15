"""T-005 r4 step 9.5g: 3D-aware additions to the planar stride optimisation (used by stride_opt_tc.py --yaw). Import has no side effects; init(P, ...) takes the exec'd stride_opt.py namespace.
Units as stride_opt.py: m, s, 1e6 kg, MN, MN m (a force in W = M g is a force in units of the body weight).
(a) Yaw (vertical-axis) moment balance. Whole-body yaw momentum about the CoM from the limbs: L_z = -sum m_i o_i (x_i' - xc'), o_i = lateral offset of the body from the CoM line (legs +-HW, arms +-13.5 m, trunk lump 0, as
    research/R-008 yaw.py / yaw_check.py), so dL_z/dt = Mz = -sum m_i o_i (x_i'' - xc'') (the lateral sway cancels). Legs from the 7 planar bodies of the stride; the arms are lifted out of the trunk lump and swing as two straight arms
    (shoulder -> hand, mass 0.70, CoM rc below the shoulder): arm on the leg-A side at flexion psi(k), arm on the leg-B side at -psi(k) (anti-phase, +psi = hand forward), psi anti-periodic over a step (psi(N) = -psi(0)).
    The arm swing also acts on the sagittal dynamics: dQ = Newton-Euler generalised force of the swinging arm minus that of the same arm rigid in the trunk lump, added to the three dyn rows (hip x, hip z, trunk lean);
    the shoulder torque is the Lagrange row of psi (|tau| <= shoulder limit x KSH).
(b) Shared friction cone, torsion included: ground free yaw moment Mt = Mz - M0, M0 = sum_p [(x_p - xc) Fy_p - o_p Ft_p] (moment about the CoM of the planned shear: sagittal Ft_p at the foot's lateral offset o_p and lateral LIPM
    shear Fy_p = Fn_p a_lat/g), and  Ft_tot^2 + Fy_tot^2 + (Mt / (KAPPA r_k))^2 <= (mu Fn_tot)^2 per node, r_k = mean radius of the loaded sole (flat: both heel and toe loaded, yaw_check.py rf; edge: 2.5 m, yaw_check.py re)."""
import numpy as np, torch
from torch.func import jacfwd, vmap

class Y: pass
C = Y()

def init(P, a_lat_over_g, kappa=0.85, ksh=0.9, hw_arm=None):
    S = P["S"]; N = P["N"]; g = P["g"]; C.N, C.g, C.W, C.MU = N, g, P["W"], P["MU"]
    C.HIPY = P["HIPY"]; C.MASS = P["MASS"]; C.comf = P["comf"]; C.ptf = P["ptf"]; C.fl = P["fl"]; C.act = P["act"]
    segs = [S["upper_arm_L"], S["forearm_L"], S["hand_L"]]; m = np.array([s["mass_kg"] / 1e6 for s in segs]); cy = np.array([s["com"][1] for s in segs])
    C.shy = segs[0]["proximal_joint"][1]; C.hs = C.shy - C.HIPY; C.ma = float(m.sum()); ycom = float((m * cy).sum() / m.sum()); C.rc = C.shy - ycom
    Ls = np.array([abs(s["proximal_joint"][1] - s["distal_end"][1]) for s in segs]); C.Ia = float((m * Ls ** 2 / 12 + m * (cy - ycom) ** 2).sum())
    C.oarm = abs(segs[0]["com"][0]); C.HW = 0.5 * P["J"]["stance"]["neutral_stance_width_m"]
    C.limsh = 3.0e8 / 1e9 * ksh                                   # shoulder torque limit in 1e9 N m (body_tuning.json joint kind shoulder)
    C.kappa = kappa
    # sole mean radii (yaw_check.py): flat = mean hypot over the sole, edge = 2.5 m
    Xg, Zg = np.meshgrid(np.linspace(-5, 5, 201), np.linspace(-7.5, 7.5, 301)); C.rf = float(np.hypot(Xg, Zg).mean()); C.re = 2.5
    fl = C.fl; rk = np.zeros(N)
    for k in range(N): rk[k] = C.rf if ((fl[k, 0] and fl[k, 1]) or (fl[k, 2] and fl[k, 3])) else C.re
    C.rk = rk; C.al = np.asarray(a_lat_over_g, float)
    C.o_leg = torch.tensor([0.0, -C.HW, -C.HW, -C.HW, C.HW, C.HW, C.HW]); C.o_pt = torch.tensor([-C.HW, -C.HW, C.HW, C.HW])
    return C

def arm_pos(qq, phi):                                              # CoM of the straight arm (+ orientation angle alpha, ccw), phi = shoulder flexion relative to the trunk (+ hand forward)
    th = qq[2]; sh = qq[:2] + C.hs * torch.stack([torch.sin(th), torch.cos(th)]); al = phi - th
    return sh + C.rc * torch.stack([torch.sin(al), -torch.cos(al)]), al

def arm_force(qs, phis, h):                                        # qs (3, 9), phis (3,): Newton-Euler generalised force on (hip x, hip z, trunk lean), shoulder torque, CoM x and its second difference
    rs, als = zip(*[arm_pos(qs[i], phis[i]) for i in range(3)]); a = (rs[2] - 2 * rs[1] + rs[0]) / h ** 2; add = (als[2] - 2 * als[1] + als[0]) / h ** 2
    Jr = jacfwd(lambda qq: arm_pos(qq, phis[1])[0])(qs[1])[:, :3]
    Q = C.ma * Jr.T @ (a + torch.tensor([0.0, C.g])) + C.Ia * add * torch.tensor([0.0, 0.0, -1.0])
    ga = torch.stack([torch.cos(als[1]), torch.sin(als[1])]) * C.rc
    tau = C.ma * ga @ (a + torch.tensor([0.0, C.g])) + C.Ia * add
    return Q, tau, a[0]

def ynode(u):
    qm, q, qp = u[0:9], u[9:18], u[18:27]; ts = u[27]; F = u[28:36].reshape(4, 2); ps = u[36:39]; al, rk = u[39], u[40]; h = ts / C.N
    qs = torch.stack([qm, q, qp]); Q0 = torch.zeros(3); dQ = torch.zeros(3); tau = []; Xa = []; X0a = []
    for sgn in (1.0, -1.0):
        Qs, ts_, xs = arm_force(qs, sgn * ps, h); Qr, _, xr = arm_force(qs, 0 * ps, h); dQ = dQ + Qs - Qr; tau.append(ts_); Xa.append(xs); X0a.append(xr)
    Xl = [C.comf(qq)[:14].reshape(7, 2)[:, 0] for qq in (qm, q, qp)]; Xdd = (Xl[2] - 2 * Xl[1] + Xl[0]) / h ** 2
    xcdd = ((C.MASS * Xdd).sum() + C.ma * ((Xa[0] - X0a[0]) + (Xa[1] - X0a[1]))) / C.MASS.sum()
    Mz = -(C.MASS * C.o_leg * (Xdd - xcdd)).sum() - C.ma * (-C.oarm * (Xa[0] - xcdd) + C.oarm * (Xa[1] - xcdd))
    xc = ((C.MASS * Xl[1]).sum() + C.ma * (arm_pos(q, ps[1])[0][0] + arm_pos(q, -ps[1])[0][0] - 2 * arm_pos(q, 0 * ps[1])[0][0])) / C.MASS.sum()   # CoM x (the lump carries the rigid arms)
    xp = C.ptf(q)[:, 0]; M0 = (( (xp - xc) * F[:, 1] * al - C.o_pt * F[:, 0]).sum()) * C.W
    Mt = (Mz - M0) / C.W; Fnt = F[:, 1].sum(); Ftt = F[:, 0].sum(); Fy = al * Fnt
    cone = (C.MU * Fnt) ** 2 - Ftt ** 2 - Fy ** 2 - (Mt / (C.kappa * rk)) ** 2
    use = torch.sqrt(Ftt ** 2 + Fy ** 2 + (Mt / rk) ** 2) / (C.MU * Fnt + 1e-9)
    return torch.cat([dQ[0:1] / C.W, dQ[1:2] / C.W, dQ[2:3] / 1000, torch.stack([tau[0] / 1000, tau[1] / 1000, Mz, M0, Mt * C.W, cone, use])])
