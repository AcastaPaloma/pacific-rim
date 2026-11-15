// Melee: one swinging arm (rigid rod about a shoulder axis plus a tip mass), swept against the target query each fixed step.
// Contact: effective mass m_eff = I / lambda^2 with lambda = (axis x r) . n (R-027 section 2); angular speed after = w + J lambda / I.
// Trigonometry (Sin/Cos) is used only to place the arm direction, so melee is host-authoritative and deterministic per runtime.
using System;
using Game.Core;

namespace Game.Combat
{
    /// <summary>Declared arm data (SI). Defaults are R-027 section 2 (Assumed).</summary>
    public sealed class ArmSpec
    {
        public double ArmMassKg = 60000;
        public double TipMassKg = 20000;
        public double ReachM = 40;
        public double ShoulderHeightM = 55;
        /// <summary>Radius of the striking sphere (fist / blade thickness), m.</summary>
        public double ContactRadiusM = 2;
        /// <summary>Length of the striking edge measured back from the tip, m (0 = tip only).</summary>
        public double BladeLengthM = 0;
        /// <summary>Contact samples along the blade, tip included (at least 1).</summary>
        public int BladeSamples = 1;
        public double MaxTipSpeed = 30;
        public double Restitution = 0.1;
        public int RecoverySteps = 100;

        /// <summary>Moment of inertia about the shoulder axis: (m_arm / 3 + m_tip) L^2, kg m^2.</summary>
        public double InertiaKgM2 => (ArmMassKg / 3.0 + TipMassKg) * ReachM * ReachM;
        /// <summary>Largest kinetic energy a swing can carry, J (0.5 I (v_max / L)^2).</summary>
        public double MaxSwingEnergyJ { get { double w = MaxTipSpeed / ReachM; return 0.5 * InertiaKgM2 * w * w; } }

        public static ArmSpec Default() => new ArmSpec();
        /// <summary>Froude-scaled upper bound (R-027): 61 m/s tip speed.</summary>
        public static ArmSpec Max() => new ArmSpec { MaxTipSpeed = 61 };
    }

    /// <summary>Swing request: plane direction relative to the mount heading and the elevation sweep (rad, 0 = horizontal, + = up).</summary>
    public struct SwingCommand
    {
        public double YawRelative;
        public double StartElevation;
        public double EndElevation;
        /// <summary>Requested duration, s; lengthened so that the peak tip speed never exceeds ArmSpec.MaxTipSpeed.</summary>
        public double Duration;
    }

    public sealed class MeleeArm
    {
        private bool _active;
        private double _t, _T, _e0, _e1, _psi;
        private CVec3 _prevShoulder, _axis;
        private int _cooldown;

        public ArmSpec Spec { get; }
        public bool Active => _active;
        public int Swings { get; private set; }
        /// <summary>Angular speed after the last contact, rad/s (NaN before any contact).</summary>
        public double OmegaAfterContact { get; private set; } = double.NaN;
        /// <summary>Actual duration of the current or last swing, s.</summary>
        public double SwingDuration => _T;

        public MeleeArm(ArmSpec spec) { Spec = spec ?? throw new ArgumentNullException(nameof(spec)); }

        public bool CanSwing => !_active && _cooldown <= 0;

        /// <summary>Peak angular speed of the smoothstep profile, rad/s: 1.5 |delta| / T.</summary>
        public static double PeakOmega(double delta, double duration) => 1.5 * Math.Abs(delta) / duration;

        public static CVec3 ArmDirection(double psi, double elevation)
        {
            double ce = Math.Cos(elevation);
            return new CVec3(Math.Sin(psi) * ce, Math.Sin(elevation), Math.Cos(psi) * ce);
        }

        public static CVec3 SwingAxis(double psi) => new CVec3(-Math.Cos(psi), 0, Math.Sin(psi));

        /// <summary>Starts a swing from the mount pose. False when already swinging, recovering, or the command is not finite.</summary>
        public bool TryBegin(EntityId source, in SwingCommand cmd, CVec3 shoulder, double heading, long step, IEventBus bus)
        {
            if (!CanSwing || !shoulder.IsFinite || double.IsNaN(heading) || double.IsNaN(cmd.YawRelative) || double.IsNaN(cmd.StartElevation) || double.IsNaN(cmd.EndElevation) || !(cmd.Duration > 0)) return false;
            double delta = cmd.EndElevation - cmd.StartElevation;
            if (delta == 0) return false;
            double tMin = 1.5 * Math.Abs(delta) * Spec.ReachM / Spec.MaxTipSpeed;
            _T = Math.Max(cmd.Duration, tMin); _t = 0; _e0 = cmd.StartElevation; _e1 = cmd.EndElevation;
            _psi = heading + cmd.YawRelative; _axis = SwingAxis(_psi); _prevShoulder = shoulder; _active = true; Swings++;
            OmegaAfterContact = double.NaN;
            if (bus != null)
            {
                CVec3 tip = shoulder + ArmDirection(_psi, _e0) * Spec.ReachM, d = ArmDirection(_psi, _e0);
                bus.Publish(new WeaponFired { Source = source, Weapon = (byte)WeaponKind.Melee, Step = step, MuzzleX = tip.X, MuzzleY = tip.Y, MuzzleZ = tip.Z, DirX = d.X, DirY = d.Y, DirZ = d.Z, ProjectileMassKg = Spec.TipMassKg });
            }
            return true;
        }

        private double Elevation(double u) { double s = u * u * (3 - 2 * u); return _e0 + (_e1 - _e0) * s; }
        private double Omega(double u) => (_e1 - _e0) * (6 * u - 6 * u * u) / _T;

        /// <summary>Advances the swing by dt. <paramref name="shoulder"/> and <paramref name="shoulderVelocity"/> come from the mount each step. At most one contact per swing; the swing ends there.</summary>
        public void Step(double dt, long step, EntityId source, CVec3 shoulder, CVec3 shoulderVelocity, ICombatTargetQuery query, IImpactListener listener)
        {
            if (_cooldown > 0 && !_active) _cooldown--;
            if (!_active || !(dt > 0)) return;
            double u0 = _t / _T;
            _t = Math.Min(_T, _t + dt);
            double u1 = _t / _T;
            double a0 = Elevation(u0), a1 = Elevation(u1);
            CVec3 d0 = ArmDirection(_psi, a0), d1 = ArmDirection(_psi, a1);
            int n = Math.Max(1, Spec.BladeSamples);
            bool found = false; SweepHit best = default; int bestK = 0;
            if (query != null)
            {
                for (int k = 0; k < n; k++)
                {
                    double r = n == 1 ? Spec.ReachM : Spec.ReachM - Spec.BladeLengthM * k / (n - 1);
                    if (!(r > 0)) continue;
                    SweepHit h;
                    if (query.SweepSphere(_prevShoulder + d0 * r, shoulder + d1 * r, Spec.ContactRadiusM, source, out h) && h.Hit && (!found || h.T < best.T)) { found = true; best = h; bestK = k; }
                }
            }
            if (found)
            {
                double rr = n == 1 ? Spec.ReachM : Spec.ReachM - Spec.BladeLengthM * bestK / (n - 1);
                double uh = u0 + (u1 - u0) * best.T;
                double omega = Omega(uh);
                CVec3 rvec = ArmDirection(_psi, Elevation(uh)) * rr;
                CVec3 tangential = CVec3.Cross(_axis, rvec);
                double lambda = CVec3.Dot(tangential, best.Normal);
                CVec3 vArm = shoulderVelocity + tangential * omega;
                double closing = -CVec3.Dot(vArm - best.TargetVelocity, best.Normal);
                double I = Spec.InertiaKgM2;
                double mEff = lambda * lambda > 1e-12 ? I / (lambda * lambda) : 0;
                var res = ImpactMath.Resolve(mEff, best.TargetMassKg, closing, Spec.Restitution);
                if (res.ImpulseNs > 0)
                {
                    OmegaAfterContact = omega + res.ImpulseNs * lambda / I;
                    listener?.OnImpact(ImpactMath.MakeRecord(source, WeaponKind.Melee, best, res, step));
                    Finish();
                    return;
                }
            }
            _prevShoulder = shoulder;
            if (_t >= _T) Finish();
        }

        private void Finish() { _active = false; _cooldown = Spec.RecoverySteps; }
    }
}
