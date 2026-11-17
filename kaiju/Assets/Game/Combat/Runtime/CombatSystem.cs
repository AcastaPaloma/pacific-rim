// Host-side combat orchestration: impact router (to Destruction and entity health), mounts, and the fixed-step driver.
using System;
using System.Collections.Generic;
using Game.Core;
using Game.World.Destruction;

namespace Game.Combat
{
    /// <summary>
    /// Routes resolved impacts: publishes <see cref="ImpactImpulse"/>, feeds building energy into <see cref="IDamageModel.Apply"/>
    /// (no Destruction change: its contract takes joules) and entity energy into <see cref="IEntityHealth"/>. Any sink may be null.
    /// </summary>
    public sealed class ImpactRouter : IImpactListener
    {
        private readonly IEventBus _bus;
        private readonly IDamageModel _damage;
        private readonly IEntityHealth _health;
        public int Impacts { get; private set; }
        public int BuildingImpacts { get; private set; }
        public int EntityImpacts { get; private set; }
        public double EnergyToBuildingsJ { get; private set; }
        public double HitPointsRemoved { get; private set; }
        /// <summary>When set, every impact is appended (tests, harness; allocates).</summary>
        public List<ImpactRecord> Log;

        public ImpactRouter(IEventBus bus, IDamageModel damage, IEntityHealth health) { _bus = bus; _damage = damage; _health = health; }

        public void OnImpact(in ImpactRecord r)
        {
            Impacts++;
            Log?.Add(r);
            _bus?.Publish(new ImpactImpulse
            {
                Source = r.Source, Weapon = (byte)r.Weapon, TargetKind = (byte)r.Kind, Building = r.Building, Target = r.Target, Step = r.Step,
                PointX = r.Point.X, PointY = r.Point.Y, PointZ = r.Point.Z, NormalX = r.Normal.X, NormalY = r.Normal.Y, NormalZ = r.Normal.Z,
                ImpulseNs = r.ImpulseNs, EnergyJ = r.EnergyJ,
            });
            switch (r.Kind)
            {
                case TargetKind.Building:
                    BuildingImpacts++;
                    if (_damage != null && r.EnergyJ > 0)
                    {
                        var ch = _damage.Apply(r.Building, r.EnergyJ, DamageCause.Impact, r.Source, r.Step);
                        if (ch.Known) EnergyToBuildingsJ += r.EnergyJ;
                    }
                    break;
                case TargetKind.Kaiju:
                case TargetKind.Mech:
                    EntityImpacts++;
                    double hp;
                    if (_health != null && _health.TryApply(r.Target, r, out hp)) HitPointsRemoved += hp;
                    break;
            }
        }
    }

    /// <summary>Hit points as a linear function of dissipated energy: hp = HpPerMegajoule * MJ (Assumed 0.15, R-027 section 3).</summary>
    public sealed class LinearEntityHealth : IEntityHealth, IHealthQuery
    {
        private readonly Dictionary<EntityId, double> _hp = new Dictionary<EntityId, double>();
        private readonly Dictionary<EntityId, double> _max = new Dictionary<EntityId, double>();
        public double HpPerMegajoule { get; set; } = 0.15;

        public void Register(EntityId id, double hitPoints) { _hp[id] = hitPoints; _max[id] = hitPoints; }

        public bool TryApply(EntityId entity, in ImpactRecord impact, out double hpRemoved)
        {
            hpRemoved = 0;
            double cur;
            if (!_hp.TryGetValue(entity, out cur)) return false;
            double want = impact.EnergyJ * 1e-6 * HpPerMegajoule;
            hpRemoved = want > 0 && !double.IsInfinity(want) ? Math.Min(cur, want) : 0;
            _hp[entity] = cur - hpRemoved;
            return true;
        }

        public bool TryGetHitPoints(EntityId entity, out double hitPoints) => _hp.TryGetValue(entity, out hitPoints);

        /// <summary>Read-only (T-154c): current over the value given to <see cref="Register"/>.</summary>
        public bool TryGetHealth(EntityId entity, out HealthStatus status)
        {
            if (_hp.TryGetValue(entity, out double cur) && _max.TryGetValue(entity, out double max)) { status = new HealthStatus(cur, max); return true; }
            status = default; return false;
        }
    }

    public sealed class CannonMount
    {
        public Cannon Cannon { get; }
        public IWeaponMount Mount { get; }
        public double MuzzleForwardM, MuzzleHeightM;
        internal CannonMount(Cannon c, IWeaponMount m, double fwd, double h) { Cannon = c; Mount = m; MuzzleForwardM = fwd; MuzzleHeightM = h; }
    }

    public sealed class MeleeMount
    {
        public MeleeArm Arm { get; }
        public IWeaponMount Mount { get; }
        internal MeleeMount(MeleeArm a, IWeaponMount m) { Arm = a; Mount = m; }
    }

    /// <summary>Owns the mounted weapons and advances them in a fixed order (cannons in mount order, then arms). The host steps it; clients never do.</summary>
    public sealed class CombatSystem : IAmmoQuery
    {
        private readonly IEventBus _bus;
        private readonly ICombatTargetQuery _query;
        private readonly ImpactRouter _router;
        private readonly List<CannonMount> _cannons = new List<CannonMount>();
        private readonly List<MeleeMount> _arms = new List<MeleeMount>();

        public long StepIndex { get; private set; }
        public ImpactRouter Router => _router;
        public IReadOnlyList<CannonMount> Cannons => _cannons;
        public IReadOnlyList<MeleeMount> Arms => _arms;
        /// <summary>Recoil impulses produced by Fire; the consumer (the real body, later) drains and clears it.</summary>
        public readonly List<RecoilImpulse> RecoilOutbox = new List<RecoilImpulse>();

        public CombatSystem(IEventBus bus, ICombatTargetQuery query, ImpactRouter router)
        {
            _bus = bus; _query = query; _router = router ?? throw new ArgumentNullException(nameof(router));
        }

        /// <summary>First mounted cannon whose mount id is <paramref name="owner"/>; read-only, no behaviour change (T-152b).</summary>
        public bool TryGetAmmo(EntityId owner, out AmmoStatus status)
        {
            for (int i = 0; i < _cannons.Count; i++)
                if (_cannons[i].Mount.Id.Equals(owner)) { status = _cannons[i].Cannon.GetAmmoStatus(); return true; }
            status = default; return false;
        }

        public CannonMount AddCannon(IWeaponMount mount, CannonSpec spec, double muzzleForwardM = 0, double muzzleHeightM = 0, BallisticEnvironment? env = null)
        {
            var m = new CannonMount(new Cannon(spec, env), mount, muzzleForwardM, muzzleHeightM); _cannons.Add(m); return m;
        }

        public MeleeMount AddMelee(IWeaponMount mount, ArmSpec spec) { var m = new MeleeMount(new MeleeArm(spec), mount); _arms.Add(m); return m; }

        /// <summary>Fires a mounted cannon at <paramref name="yawRelative"/> to the mount heading and <paramref name="pitch"/> above horizontal.</summary>
        public bool Fire(CannonMount m, double yawRelative, double pitch)
        {
            m.Mount.GetPose(out var pos, out var heading, out var vel);
            double psi = heading + yawRelative;
            CVec3 dir = MeleeArm.ArmDirection(psi, pitch);
            CVec3 muzzle = pos + new CVec3(Math.Sin(heading) * m.MuzzleForwardM, m.MuzzleHeightM, Math.Cos(heading) * m.MuzzleForwardM);
            RecoilImpulse r;
            if (!m.Cannon.TryFire(m.Mount.Id, muzzle, dir, vel, StepIndex, _bus, out r)) return false;
            RecoilOutbox.Add(r);
            return true;
        }

        public bool Swing(MeleeMount m, in SwingCommand cmd)
        {
            m.Mount.GetPose(out var pos, out var heading, out _);
            return m.Arm.TryBegin(m.Mount.Id, cmd, pos + new CVec3(0, m.Arm.Spec.ShoulderHeightM, 0), heading, StepIndex, _bus);
        }

        public void Step(double dt)
        {
            for (int i = 0; i < _cannons.Count; i++) _cannons[i].Cannon.Step(dt, StepIndex, _query, _router);
            for (int i = 0; i < _arms.Count; i++)
            {
                var a = _arms[i];
                a.Mount.GetPose(out var pos, out _, out var vel);
                a.Arm.Step(dt, StepIndex, a.Mount.Id, pos + new CVec3(0, a.Arm.Spec.ShoulderHeightM, 0), vel, _query, _router);
            }
            StepIndex++;
        }
    }
}
