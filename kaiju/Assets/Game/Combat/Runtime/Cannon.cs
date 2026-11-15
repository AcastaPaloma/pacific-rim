// Cannon: finite ammo, reload, recoil as an output event, projectiles flown by Ballistics and swept against the target query.
using System;
using Game.Core;

namespace Game.Combat
{
    /// <summary>Declared cannon data (SI). Presets are R-027 section 1; Assumed fields are marked there.</summary>
    public sealed class CannonSpec
    {
        public string Name = "cannon";
        public double CaliberM = 0.406;
        public double ShellMassKg = 1225;
        public double ChargeKg = 300;
        public double MuzzleSpeed = 762;
        /// <summary>Propellant gas momentum factor (Assumed 1.25).</summary>
        public double GasMomentumFactor = 1.25;
        public DragCurve Drag = DragCurve.ShellAssumed();
        /// <summary>Restitution of the shell against its target; 0 = fully plastic AP (Assumed).</summary>
        public double ShellRestitution = 0;
        public int MaxAmmo = 20;
        public int ReloadSteps = 150;
        public double MaxFlightSeconds = 180;

        public double ReferenceAreaM2 => Math.PI * 0.25 * CaliberM * CaliberM;
        /// <summary>Recoil impulse (m_shell + k m_charge) v, N s.</summary>
        public double RecoilImpulseNs => (ShellMassKg + GasMomentumFactor * ChargeKg) * MuzzleSpeed;
        public double MuzzleEnergyJ => 0.5 * ShellMassKg * MuzzleSpeed * MuzzleSpeed;

        public static CannonSpec Heavy() => new CannonSpec();
        public static CannonSpec Light() => new CannonSpec { Name = "light", CaliberM = 0.155, ShellMassKg = 46, ChargeKg = 12, MuzzleSpeed = 827, MaxAmmo = 60, ReloadSteps = 30 };
    }

    public struct Projectile
    {
        public CVec3 Pos, Vel;
        public double Age;
        public EntityId Source;
        public bool Alive;
    }

    public sealed class Cannon
    {
        private Projectile[] _live = new Projectile[8];
        private int _count;
        private int _cooldown;

        public CannonSpec Spec { get; }
        public BallisticEnvironment Environment;
        public int Ammo { get; private set; }
        public int LiveProjectiles => _count;
        public long RoundsFired { get; private set; }

        public Cannon(CannonSpec spec, BallisticEnvironment? env = null, int? ammo = null)
        {
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            Environment = env ?? BallisticEnvironment.Standard();
            Ammo = ammo ?? spec.MaxAmmo;
        }

        public bool CanFire => Ammo > 0 && _cooldown <= 0;

        /// <summary>
        /// Fires along <paramref name="dir"/> (normalised here). The shell inherits the shooter velocity. Publishes WeaponFired and
        /// RecoilImpulse when a bus is given. False (nothing happens) when empty or reloading or the inputs are not finite.
        /// </summary>
        public bool TryFire(EntityId source, CVec3 muzzle, CVec3 dir, CVec3 shooterVelocity, long step, IEventBus bus, out RecoilImpulse recoil)
        {
            recoil = default;
            if (!CanFire || !muzzle.IsFinite || !dir.IsFinite || !shooterVelocity.IsFinite) return false;
            CVec3 d = dir.Normalized(new CVec3(0, 0, 1));
            if (_count == _live.Length) Array.Resize(ref _live, _count * 2);
            _live[_count++] = new Projectile { Pos = muzzle, Vel = d * Spec.MuzzleSpeed + shooterVelocity, Age = 0, Source = source, Alive = true };
            Ammo--; RoundsFired++; _cooldown = Spec.ReloadSteps;
            CVec3 j = d * (-Spec.RecoilImpulseNs);
            recoil = new RecoilImpulse { Source = source, Step = step, ImpulseX = j.X, ImpulseY = j.Y, ImpulseZ = j.Z, PointX = muzzle.X, PointY = muzzle.Y, PointZ = muzzle.Z };
            if (bus != null)
            {
                bus.Publish(new WeaponFired { Source = source, Weapon = (byte)WeaponKind.Cannon, Step = step, MuzzleX = muzzle.X, MuzzleY = muzzle.Y, MuzzleZ = muzzle.Z, DirX = d.X, DirY = d.Y, DirZ = d.Z, ProjectileMassKg = Spec.ShellMassKg });
                bus.Publish(recoil);
            }
            return true;
        }

        public void Resupply(int rounds) { if (rounds > 0) Ammo = Math.Min(Spec.MaxAmmo, Ammo + rounds); }

        /// <summary>Advances every live shell by <paramref name="dt"/>; resolves first contact per shell, in spawn order.</summary>
        public void Step(double dt, long step, ICombatTargetQuery query, IImpactListener listener)
        {
            if (_cooldown > 0) _cooldown--;
            if (!(dt > 0)) return;
            double radius = 0.5 * Spec.CaliberM, area = Spec.ReferenceAreaM2;
            int w = 0;
            for (int i = 0; i < _count; i++)
            {
                Projectile p = _live[i];
                CVec3 p0 = p.Pos, v0 = p.Vel;
                Ballistics.Step(ref p.Pos, ref p.Vel, Spec.ShellMassKg, area, Spec.Drag, Environment, dt);
                p.Age += dt;
                bool done = !p.Pos.IsFinite || !p.Vel.IsFinite || p.Age >= Spec.MaxFlightSeconds;
                if (!done)
                {
                    SweepHit hit = default;
                    bool any = query != null && query.SweepSphere(p0, p.Pos, radius, p.Source, out hit) && hit.Hit;
                    double g = Environment.GroundY;
                    if (!double.IsNaN(g) && p.Pos.Y - radius <= g && p0.Y - radius > g)
                    {
                        double tg = (p0.Y - radius - g) / (p0.Y - p.Pos.Y);
                        if (!any || tg < hit.T)
                        {
                            hit = new SweepHit { Hit = true, T = tg, Point = new CVec3(p0.X + (p.Pos.X - p0.X) * tg, g, p0.Z + (p.Pos.Z - p0.Z) * tg), Normal = CVec3.Up, Kind = TargetKind.Ground };
                            any = true;
                        }
                    }
                    if (any)
                    {
                        CVec3 v = v0 + (p.Vel - v0) * hit.T;
                        double closing = -CVec3.Dot(v - hit.TargetVelocity, hit.Normal);
                        var res = ImpactMath.Resolve(Spec.ShellMassKg, hit.Kind == TargetKind.Ground ? 0 : hit.TargetMassKg, closing, Spec.ShellRestitution);
                        listener?.OnImpact(ImpactMath.MakeRecord(p.Source, WeaponKind.Cannon, hit, res, step));
                        done = true;
                    }
                }
                if (!done) _live[w++] = p;
            }
            for (int i = w; i < _count; i++) _live[i] = default;
            _count = w;
        }

        /// <summary>Copies live projectile states (for tests and the view).</summary>
        public Projectile GetProjectile(int index) => _live[index];
    }
}
