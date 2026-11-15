#!/usr/bin/env python
"""T-005 r6 offline go/no-go: planar stepping model with a compliant multi-point foot (python, conda kaiju env).

Model (planar, x forward, z up; all SI, mech_frame_balanced numbers): the whole body is one point mass M at the CoM carried by two massless
legs; each leg pushes the CoM with F = N u + (tau / l) n (u = unit ankle -> CoM, n = u rotated +90 deg: the ankle torque tau on the leg shows as a
lateral hip force of tau / l, which is the ankle-strategy coupling); the feet are rigid bodies (m 3e5, I 6e6, ankle 4 m above the sole, COM 2.25 m
ahead of the ankle) with 9 compliant sole points (spring-damper normal force, stick-slip Coulomb friction). Ankle torque limit 1e9 N m.
Planner = the qp_plan logic in miniature: LIPM capture-point foothold x_fp = xi_pred(T_rem) - b, fixed step time T, explicit double support (load
share w ramps rear -> front over T_ds, rear foot lifts when its share falls under 3 %), swing ankle on a min-jerk curve to the foothold with a
clearance arc; the force split is a weighted least squares (equality: sum of leg forces = CoM wrench; soft: each leg's tau for its CoP target).
Modes (what differs): 'base' = CoP target bounds are the assumed full sole, reference-style landing pitch (heel strike, -12 deg), no pitch task;
'meas' = CoP bounds from the measured contact points of each foot (heel / flat / toe phase) with a margin; 'flat' = meas + the sole is driven to
level in late swing (pre-landing orientation task) and a flat-foot pitch task while the stance foot has partial contact.
Prints per run: survival time, steps, speed, per-step landing pitch, CoP error (measured - commanded), ankle torque saturation share.
"""
import sys, math, numpy as np

M = 1.0e7; G = 9.81; TLIM = 1.0e9; MF = 3.0e5; IF = 6.0e6; AH = 4.0; CX = 2.25; CZ = 2.0   # foot COM 2.25 ahead / 2 above the sole -> ankle at (-CX, +AH-CZ) from the COM
HEEL = -5.25; TOE = 9.75; NP = 9
PX = np.linspace(HEEL, TOE, NP)                  # sole points, x relative to the ankle, 4 m below it
KN = 3.7e8; CN = 5.0e6; KT = 2e8; CT = 4e6; MU = 0.8
ZC = 42.0; OM = math.sqrt(G / ZC); TS = 6.27; DL = 18.09; TDS = 0.3; TSW = TS - TDS
B = DL / (math.exp(OM * TS) - 1.0)
D2 = math.radians


def minjerk(s):
    s = min(1.0, max(0.0, s)); return s ** 3 * (10 + s * (-15 + 6 * s))


class Foot:
    def __init__(self, xa):
        self.xa = xa; self.za = AH; self.va = np.zeros(2); self.phi = 0.0; self.w = 0.0   # kinematic until touchdown
        self.dyn = True; self.anch = np.full(NP, np.nan); self.F = np.zeros(2); self.mom = 0.0; self.fn = np.zeros(NP); self.load = 0.0; self.sw = None

    def sole(self):   # world points: ankle + R(phi) (px, -AH); phi > 0 = toe down (clockwise in x-z)
        c, s = math.cos(self.phi), math.sin(self.phi)
        px = self.xa + PX * c + (-AH) * s * 1.0
        pz = self.za - PX * s * 1.0 + (-AH) * c
        return px, pz

    def contact(self, vx_pts, vz_pts, dt):
        px, pz = self.sole(); pen = -pz; fn = np.where(pen > 0, KN * pen + CN * np.maximum(-vz_pts, 0.0) - 0 * vz_pts, 0.0)
        fn = np.where(pen > 0, np.maximum(KN * pen - CN * vz_pts, 0.0), 0.0)
        ft = np.zeros(NP)
        for k in range(NP):
            if fn[k] > 0:
                if math.isnan(self.anch[k]): self.anch[k] = px[k]
                f = -KT * (px[k] - self.anch[k]) - CT * vx_pts[k]; lim = MU * fn[k]
                if abs(f) > lim: f = math.copysign(lim, f); self.anch[k] = px[k] + f / KT * 1.0 * -1.0 if False else px[k] + f / KT
                ft[k] = f
            else: self.anch[k] = np.nan
        self.fn = fn
        Fx = ft.sum(); Fz = fn.sum()
        mom = 0.0   # ccw moment about the ankle: r x F, r = point - ankle
        mom = np.sum((px - self.xa) * fn - (pz - self.za) * ft)
        self.load = Fz; return Fx, Fz, mom, ft


X0 = 9.4; V0 = 3.8


def run(mode, tmax=40.0, dt=2.5e-4, seed=0, verbose=False, land_pitch_deg=-12.0, kpf=3e8):
    rng = np.random.default_rng(seed)
    com = np.array([CX - X0, ZC]); v = np.array([V0, 0.0])
    ft = [Foot(-DL), Foot(0.0)]; ft[0].w = 1.0; ft[1].w = 0.0
    st = 0; sw = 1; phase = 'DS'; t = 0.0; tl = 0.0; steps = 0; x_fp = DL; tds0 = 0.0
    sw_x0 = ft[1].xa; sw_phi0 = 0.0; log = []; sat = 0; ticks = 0; tdlog = []; copErr = []; fell = None
    tds0 = 0.0   # start: swing foot behind, just lifted
    sw_x0 = ft[1].xa; sw_t0 = 0.0; sw_za0 = ft[1].za
    while t < tmax:
        t += dt; ticks += 1
        S, W = ft[st], ft[sw]
        # ---------- planner
        Sf = ft[sw] if phase == 'DS' else S
        xcop_nom = Sf.xa + CX
        trem = max(TS - (t - tl), 0.05); xi = com[0] + v[0] / OM
        xip = xcop_nom + (xi - xcop_nom) * math.exp(OM * trem)
        xfp_new = xip - B - CX                                     # ankle target of the swing foot (mid-sole at xi_pred - b)
        xfp_new = min(max(xfp_new, Sf.xa + 0.2 * DL), Sf.xa + 1.6 * DL)
        x_fp += min(1.0, dt / 0.5) * (xfp_new - x_fp)
        # ---------- swing foot kinematics + pitch dynamics
        if phase == 'SS':
            s = (t - sw_t0) / TSW
            W.xa = sw_x0 + (x_fp - sw_x0) * minjerk(s)
            W.za = AH + (sw_za0 - AH) * (1 - minjerk(s * 3)) + 5.0 * math.sin(math.pi * min(1.0, s)) ** 2 - 0.5 * max(0.0, s - 0.85) * 3
            # pitch target
            if mode == 'flat':
                u = minjerk((s - 0.5) / 0.45); ptar = (1 - u) * D2(-5.0) + u * D2(-2.0)      # level the sole before touchdown (2 deg heel-first)
            else:
                u = minjerk((s - 0.3) / 0.6); ptar = (1 - u) * D2(20.0) + u * D2(land_pitch_deg)   # reference-like: toe down after lift-off, heel strike at touchdown
            px, pz = W.sole(); phid = getattr(W, 'phid', 0.0)
            tq = np.clip(3e7 * (ptar - W.phi) - 3e7 * phid, -0.98 * TLIM, 0.98 * TLIM)
            Ia = IF + MF * (CX ** 2 + CZ ** 2)
            mom = 0.0
            vxp = np.zeros(NP); vzp = np.zeros(NP)
            if W.za < AH + 1.0:
                _, _, mom, _ = W.contact(vxp, vzp, dt)
            acc = (-tq + mom * 0 - mom) / Ia   # clockwise-positive pitch: ankle torque tq (toe down) acts +, contact ccw moment acts -
            # pitch is clockwise positive: contact ccw moment 'mom' decreases phi; the ankle torque tq increases phi
            acc = (tq - mom) / Ia
            W.phid = phid + acc * dt; W.phi += W.phid * dt
            W.va[:] = 0
            # touchdown: first sole point under the ground after 80 % of the swing
            px, pz = W.sole()
            if s > 0.8 and pz.min() < -0.02:
                W.dyn = True; W.va[0] = (x_fp - sw_x0) * 30 * s * s * (1 - s) ** 2 / TSW if s < 1 else 0.0; W.va[1] = 0.0
                tdlog.append((t, math.degrees(W.phi), x_fp - W.xa)); phase = 'DS'; tds0 = t; tl = t; W.w = 0.0
                if verbose: print('td t=%.2f pitch %.1f deg x_a %.2f' % (t, math.degrees(W.phi), W.xa))
        # ---------- desired wrench on the CoM
        fzd = M * (G + 20.0 * 0.0 + 0.05 * (ZC - com[1]) - 0.5 * v[1] * 0 + (0.05 * (ZC - com[1]) * 0) - 0.4 * v[1] * 0.0)
        fzd = M * (G + 0.02 * (ZC - com[1]) - 0.35 * v[1])
        w_f = 0.0
        if phase == 'DS':
            u = (t - tds0) / TDS; w_f = u * u * (3 - 2 * u) if u < 1 else 1.0
            ft[sw].w = w_f; ft[st].w = 1.0 - w_f
        else:
            ft[st].w = 1.0
        # total CoP target
        legs = [f for f in (ft[st], ft[sw]) if f.dyn and f.w > 1e-3]
        # foot CoP targets (mid-sole nominal), clipped to the foot's usable polygon
        def bounds(f):
            if mode == 'base': return f.xa + HEEL, f.xa + TOE
            idx = np.where(f.fn > 0.02 * M * G * 0.1)[0]
            if len(idx) == 0: return f.xa + CX, f.xa + CX
            lo, hi = f.sole()[0][idx].min(), f.sole()[0][idx].max()
            if mode == 'flat' and len(idx) >= NP - 2: lo += 1.0; hi -= 2.0
            return lo, hi
        # CoM wrench from the DCM: x_cop_tot = sum w_i cop_i
        cops = []; ws = []
        for f in legs:
            lo, hi = bounds(f); c = min(max(f.xa + CX, lo), hi)
            if phase == 'DS' and f is ft[st]: c = min(max(f.xa + (HEEL + TOE) * 0.5 + 3.0, lo), hi)    # rear foot pushes off: CoP toward its toe
            cops.append(c); ws.append(f.w)
        wsum = sum(ws); xcop_tot = sum(c * w for c, w in zip(cops, ws)) / max(wsum, 1e-9)
        fxd = M * OM * OM * (com[0] - xcop_tot)
        # per-leg (N, tau) by weighted LS
        n = len(legs); A = []; bvec = []
        for i, f in enumerate(legs):
            uvec = np.array([com[0] - f.xa, com[1] - f.za]); L = np.hypot(*uvec); uvec /= L; nvec = np.array([-uvec[1], uvec[0]])
            f._u, f._n, f._L = uvec, nvec, L
        # unknowns [N_1, tau_1, ...]; equalities weight 1e3 (scaled by W), softs
        sc = M * G
        rows = []; rhs = []; wt = []
        for k in range(2):
            r = np.zeros(2 * n)
            for i, f in enumerate(legs): r[2 * i] = f._u[k]; r[2 * i + 1] = f._n[k] / f._L * TLIM / sc
            rows.append(r); rhs.append((fxd if k == 0 else fzd) / sc); wt.append(1e3)
        for i, f in enumerate(legs):
            fzi = f.w * fzd
            tau_t = fzi * (cops[i] - f.xa)
            if mode == 'flat' and f.load > 0.02 * sc:   # flat-foot task: sole pitch -> level while the contact is partial
                px, pz = f.sole(); partial = (f.fn[0] < 1e-3 * sc) != (f.fn[-1] < 1e-3 * sc)
                if partial: tau_t += kpf * (0.0 - f.phi) - 0.3 * kpf * getattr(f, 'phid', 0.0)
            r = np.zeros(2 * n); r[2 * i + 1] = 1.0; rows.append(r); rhs.append(tau_t / TLIM); wt.append(1.0)
            r = np.zeros(2 * n); r[2 * i] = 1.0; rows.append(r); rhs.append(f.w * (fzd / max(f._u[1], 0.2)) / sc); wt.append(0.3)
        Am = np.array(rows) * np.sqrt(np.array(wt))[:, None]; bm = np.array(rhs) * np.sqrt(np.array(wt))
        x = np.linalg.lstsq(Am, bm, rcond=None)[0]
        Fcom = np.zeros(2)
        for i, f in enumerate(legs):
            N_ = max(x[2 * i] * sc, 0.0); tau = x[2 * i + 1] * TLIM
            if abs(tau) > 0.97 * TLIM: sat += 1
            tau = float(np.clip(tau, -0.98 * TLIM, 0.98 * TLIM)); f._tau = tau
            Fc = N_ * f._u + tau / f._L * f._n; f._Fc = Fc; Fcom += Fc
        ticks_leg = len(legs)
        if verbose and ticks % 8000 == 0: print('dbg t=%.2f ph=%s x=%.2f v=%.2f xcop_tot=%.2f fxd/W=%.3f Fx/W=%.3f Fz/W=%.3f nlegs=%d xfp=%.1f' % (t, phase, com[0], v[0], xcop_tot, fxd / sc, Fcom[0] / sc, Fcom[1] / sc, n, x_fp))
        # ---------- integrate CoM
        a = Fcom / M - np.array([0.0, G]); v += a * dt; com += v * dt
        # ---------- integrate dynamic feet
        for f in legs + [g for g in (S, W) if g.dyn and g not in legs]:
            if not f.dyn: continue
            px, pz = f.sole(); c, s_ = math.cos(f.phi), math.sin(f.phi)
            # point velocities: ankle va + omega x r, phid clockwise positive
            phid = getattr(f, 'phid', 0.0); rx = px - f.xa; rz = pz - f.za
            vxp = f.va[0] + phid * rz * 1.0 * 1.0; vzp = f.va[1] - phid * rx * 1.0
            Fx, Fz, mom, ftp = f.contact(vxp, vzp, dt)
            Fc = getattr(f, '_Fc', np.zeros(2)); tau = getattr(f, '_tau', 0.0)
            if f not in legs: Fc = np.zeros(2); tau = 0.0
            # foot COM: ankle + (CX, CZ-AH) rotated; treat the foot as a body of mass MF at the ankle (CoM offset in the pitch inertia only)
            fx = Fx - Fc[0]; fz = Fz - Fc[1] - MF * G
            f.va[0] += fx / MF * dt; f.va[1] += fz / MF * dt; f.xa += f.va[0] * dt; f.za += f.va[1] * dt
            Ia = IF + MF * (CX ** 2 + CZ ** 2)
            f.phid = phid + (tau - mom) / Ia * dt; f.phi += f.phid * dt
            f.phi = float(np.clip(f.phi, -D2(60), D2(60)))
        # ---------- support switch: rear foot lifts when its share is gone
        if phase == 'DS' and ft[st].w < 0.03:
            phase = 'SS'; steps += 1; old = st; st, sw = sw, st
            W2 = ft[sw]; W2.dyn = False; W2.va[:] = 0; sw_x0 = W2.xa; sw_za0 = W2.za; sw_t0 = t; W2.w = 0.0; x_fp = max(x_fp, W2.xa)
            ft[st].w = 1.0
            tdlog.append((t, 'lift', math.degrees(W2.phi)))
        # CoP error (measured total CoP vs commanded), sampled
        if ticks % 200 == 0:
            tot = 0.0; mx = 0.0
            for f in ft:
                if f.dyn and f.load > 0: px, _ = f.sole(); tot += f.load; mx += (px * f.fn).sum()
            if tot > 0.3 * M * G: copErr.append((mx / tot) - xcop_tot)
            log.append((t, com[0], v[0], com[1], math.degrees(ft[st].phi), math.degrees(ft[sw].phi)))
        if com[1] < 0.8 * ZC or abs(math.degrees(ft[st].phi)) > 55 or com[0] < ft[st].xa - 14 or com[0] > ft[st].xa + 30:
            fell = t
            if verbose: print('FELL t=%.3f z=%.2f phi=%.1f x=%.2f xa=%.2f' % (t, com[1], math.degrees(ft[st].phi), com[0], ft[st].xa))
            break
    tds = [x for x in tdlog if x[1] != 'lift']
    return dict(mode=mode, fell=fell, t=t, steps=steps, v=v[0], x=com[0], td_pitch=[round(x[1], 1) for x in tds], cop_err_m=(float(np.mean(np.abs(copErr))) if copErr else None), sat_share=sat / max(ticks, 1), log=log)


def contact_test(phi0_deg, kpf, dt=2.5e-4, tmax=8.0, cop_cmd_off=2.25):
    """Single stance foot landing with sole pitch phi0 (deg, + toe down): the leg carries the weight, CoP command = ankle + cop_cmd_off (flat-sole mid), the
    ankle torque = F_z x (cop_cmd - ankle) + kpf (0 - phi) - 0.3 kpf phid. Returns time to flat (|phi| < 1 deg), peak |tau| / TLIM, mean |measured CoP - commanded|
    over the first 4 s and the final pitch."""
    f = Foot(0.0); f.phi = D2(phi0_deg); f.phid = 0.0; px, pz = f.sole(); f.za = AH - pz.min()   # lowest sole point just touching
    com = np.array([f.xa - 4.0, ZC + (f.za - AH)]); v = np.array([2.0, 0.0]); t = 0.0; ticks = 0
    tflat = None; peak = 0.0; ce = []
    while t < tmax:
        t += dt; ticks += 1
        uvec = np.array([com[0] - f.xa, com[1] - f.za]); L = np.hypot(*uvec); uvec /= L; nvec = np.array([-uvec[1], uvec[0]])
        fzd = M * (G + 0.02 * (ZC - com[1]) - 0.35 * v[1]); xcop = f.xa + cop_cmd_off
        tau_t = fzd * cop_cmd_off + kpf * (0.0 - f.phi) - 0.3 * kpf * f.phid
        tau = float(np.clip(tau_t, -0.98 * TLIM, 0.98 * TLIM)); Nn = max((fzd - tau / L * nvec[1]) / uvec[1], 0.0)
        Fc = Nn * uvec + tau / L * nvec; peak = max(peak, abs(tau_t) / TLIM)
        a = Fc / M - np.array([0.0, G]); v += a * dt; com += v * dt
        px, pz = f.sole(); rx, rz = px - f.xa, pz - f.za
        vxp = f.va[0] + f.phid * rz; vzp = f.va[1] - f.phid * rx
        Fx, Fz, mom, ftp = f.contact(vxp, vzp, dt)
        f.va[0] += (Fx - Fc[0]) / MF * dt; f.va[1] += (Fz - Fc[1] - MF * G) / MF * dt; f.xa += f.va[0] * dt; f.za += f.va[1] * dt
        Ia = IF + MF * (CX ** 2 + CZ ** 2); f.phid += (tau - mom) / Ia * dt; f.phi += f.phid * dt
        if tflat is None and abs(math.degrees(f.phi)) < 1.0 and t > 0.2: tflat = t
        if ticks % 200 == 0 and t < 4.0 and f.load > 0.3 * M * G:
            ce.append(abs((f.sole()[0] * f.fn).sum() / f.load - xcop))
    return dict(tflat=tflat, peak=peak, cop_err=float(np.mean(ce)) if ce else None, phi_end=math.degrees(f.phi), z=com[1])


if __name__ == '__main__':
    modes = sys.argv[1].split(',') if len(sys.argv) > 1 else ['base', 'meas', 'flat']
    tmax = float(sys.argv[2]) if len(sys.argv) > 2 else 40.0
    for m in modes:
        for lp in ([-12.0, 8.0] if m != 'flat' else [-2.0]):
            r = run(m, tmax, land_pitch_deg=lp, verbose=len(sys.argv) > 3)
            print('%-5s land %5.1f: fell %s t %.1f steps %d v %.2f xcom %.1f | td pitch %s | cop err %.2f m | ankle sat %.3f' % (m, lp, r['fell'], r['t'], r['steps'], r['v'], r['x'], r['td_pitch'][:8], r['cop_err_m'] or -1, r['sat_share']))
            for l in r['log'][::max(1, len(r['log']) // 12)]: print('    t %.1f x %.1f v %.2f z %.1f stance pitch %.1f swing pitch %.1f' % l)
