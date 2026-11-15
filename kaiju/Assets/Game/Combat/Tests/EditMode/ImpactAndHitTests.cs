// Impact law bounds (M-062 draft: impact energy bounds), hit/miss resolution, cannon and melee, routing into Destruction and kaiju HP.
using System;
using System.Collections.Generic;
using Game.Core;
using Game.World.Destruction;
using NUnit.Framework;

namespace Game.Combat.Tests
{
    public class ImpactMathTests
    {
        [Test]
        public void EnergyConservation_HoldsForRandomCases()
        {
            ulong s = 12345;
            double Rnd() { s = s * 6364136223846793005UL + 1442695040888963407UL; return ((s >> 11) & 0x1FFFFFFFFFFFFFUL) / (double)(1UL << 53); }
            for (int i = 0; i < 2000; i++)
            {
                double m = 1 + Rnd() * 1e6, M = Rnd() < 0.2 ? 0 : 1 + Rnd() * 1e9, v = 0.1 + Rnd() * 800, e = Rnd();
                var r = ImpactMath.Resolve(m, M, v, e);
                Assert.GreaterOrEqual(r.DissipatedJ, 0);
                Assert.LessOrEqual(r.DissipatedJ, r.ImpactorKineticJ * (1 + 1e-12));
                // Impactor velocity after, along the normal: v - J/m (target at rest). KE before = KE after + target gain + dissipated.
                double vAfter = v - r.ImpulseNs / m;
                double before = 0.5 * m * v * v, after = 0.5 * m * vAfter * vAfter + r.TargetGainJ + r.DissipatedJ;
                Assert.AreEqual(before, after, 1e-9 * before, "case " + i);
            }
        }

        [Test]
        public void ImmovableTarget_FullyPlastic_DissipatesAllKinetic()
        {
            var r = ImpactMath.Resolve(1225, 0, 700, 0);
            Assert.AreEqual(0.5 * 1225 * 700 * 700, r.DissipatedJ, 1e-6);
            Assert.AreEqual(1225 * 700, r.ImpulseNs, 1e-9);
        }

        [Test]
        public void SpeedBelowZero_Or_NonFinite_GivesNothing()
        {
            Assert.AreEqual(0, ImpactMath.Resolve(100, 100, -3, 0).ImpulseNs);
            Assert.AreEqual(0, ImpactMath.Resolve(100, 100, double.NaN, 0).ImpulseNs);
            Assert.AreEqual(0, ImpactMath.Resolve(0, 100, 5, 0).ImpulseNs);
            Assert.AreEqual(0, ImpactMath.Resolve(double.PositiveInfinity, 100, 5, 0).ImpulseNs);
        }

        [Test]
        public void HeavyTarget_BehavesAsImmovable_LightTargetTakesHalfAtEqualMass()
        {
            var heavy = ImpactMath.Resolve(1000, 1e15, 50, 0);
            Assert.AreEqual(0.5 * 1000 * 50 * 50, heavy.DissipatedJ, 1e-6 * heavy.DissipatedJ);
            var equal = ImpactMath.Resolve(1000, 1000, 50, 0);
            Assert.AreEqual(0.5 * heavy.DissipatedJ, equal.DissipatedJ, 1e-9 * heavy.DissipatedJ);
        }
    }

    public class TargetWorldTests
    {
        static BuildingId B(ulong n) { return BuildingId.From(BuildingSource.OsmWay, n); }

        [Test]
        public void SweepHitsBoxFace_WithOutwardNormal_AndFractionAlongSegment()
        {
            var w = new SimpleTargetWorld(); w.AddBuilding(B(1), new CVec3(-10, 0, 100), new CVec3(10, 50, 120), 1e7);
            SweepHit h;
            Assert.IsTrue(w.SweepSphere(new CVec3(0, 10, 0), new CVec3(0, 10, 200), 1, default, out h));
            Assert.AreEqual(99.0 / 200.0, h.T, 1e-12);
            Assert.AreEqual(-1, h.Normal.Z, 1e-12);
            Assert.AreEqual(B(1), h.Building); Assert.AreEqual(TargetKind.Building, h.Kind);
            Assert.AreEqual(100, h.Point.Z, 1e-12);
        }

        [Test]
        public void SweepMisses_WhenPassingBesideOrStoppingShort()
        {
            var w = new SimpleTargetWorld(); w.AddBuilding(B(1), new CVec3(-10, 0, 100), new CVec3(10, 50, 120), 1e7);
            SweepHit h;
            Assert.IsFalse(w.SweepSphere(new CVec3(30, 10, 0), new CVec3(30, 10, 200), 1, default, out h));
            Assert.IsFalse(w.SweepSphere(new CVec3(0, 10, 0), new CVec3(0, 10, 90), 1, default, out h));
            Assert.IsFalse(w.SweepSphere(new CVec3(0, 80, 0), new CVec3(0, 80, 200), 1, default, out h));
        }

        [Test]
        public void SweepStartingInside_HitsAtZero()
        {
            var w = new SimpleTargetWorld(); w.AddBuilding(B(1), new CVec3(-10, 0, 100), new CVec3(10, 50, 120), 1e7);
            SweepHit h;
            Assert.IsTrue(w.SweepSphere(new CVec3(0, 10, 110), new CVec3(0, 10, 130), 1, default, out h));
            Assert.AreEqual(0, h.T);
        }

        [Test]
        public void Capsule_SideAndCap_AndIgnore()
        {
            var w = new SimpleTargetWorld(); var k = EntityId.FromValue(7);
            w.AddCapsule(k, TargetKind.Kaiju, new CVec3(0, 0, 100), 10, 100, 1e8);
            SweepHit h;
            Assert.IsTrue(w.SweepSphere(new CVec3(0, 50, 0), new CVec3(0, 50, 200), 1, default, out h));
            Assert.AreEqual(89.0 / 200.0, h.T, 1e-12); Assert.AreEqual(-1, h.Normal.Z, 1e-12); Assert.AreEqual(k, h.Entity);
            Assert.IsTrue(w.SweepSphere(new CVec3(0, 300, 100), new CVec3(0, -50, 100), 1, default, out h));
            Assert.AreEqual(1, h.Normal.Y, 1e-9, "top cap");
            Assert.IsFalse(w.SweepSphere(new CVec3(0, 50, 0), new CVec3(0, 50, 200), 1, k, out h), "ignored shooter");
        }

        [Test]
        public void EarliestOfTwoTargetsWins()
        {
            var w = new SimpleTargetWorld();
            w.AddBuilding(B(2), new CVec3(-5, 0, 300), new CVec3(5, 40, 320), 1e7);
            w.AddBuilding(B(1), new CVec3(-5, 0, 100), new CVec3(5, 40, 120), 1e7);
            SweepHit h; Assert.IsTrue(w.SweepSphere(new CVec3(0, 10, 0), new CVec3(0, 10, 400), 1, default, out h));
            Assert.AreEqual(B(1), h.Building);
        }
    }

    public class CannonTests
    {
        static BuildingId B(ulong n) { return BuildingId.From(BuildingSource.OsmWay, n); }
        static readonly BuildingAttributes MidRise = new BuildingAttributes { HeightM = 25, Levels = 6, FootprintM2 = 600 };

        static (Cannon c, SimpleTargetWorld w, RecordingListener l) Rig(CannonSpec spec = null)
        {
            var w = new SimpleTargetWorld(); w.AddBuilding(B(1), new CVec3(-30, 0, 500), new CVec3(30, 80, 520), 5e7);
            return (new Cannon(spec ?? CannonSpec.Heavy()), w, new RecordingListener());
        }

        static void Fly(Cannon c, SimpleTargetWorld w, IImpactListener l, int steps, double dt = 0.02)
        {
            for (int i = 0; i < steps; i++) c.Step(dt, i, w, l);
        }

        [Test]
        public void FlatShot_HitsBuildingFace_EnergyWithinMuzzleBound()
        {
            var (c, w, l) = Rig(); var bus = new RecordingBus();
            Assert.IsTrue(c.TryFire(EntityId.FromValue(1), new CVec3(0, 10, 0), new CVec3(0, 0, 1), CVec3.Zero, 0, bus, out var recoil));
            Fly(c, w, l, 200);
            Assert.AreEqual(1, l.Records.Count);
            var r = l.Records[0];
            Assert.AreEqual(TargetKind.Building, r.Kind); Assert.AreEqual(B(1), r.Building);
            Assert.AreEqual(-1, r.Normal.Z, 1e-9);
            Assert.Greater(r.EnergyJ, 0);
            Assert.LessOrEqual(r.EnergyJ, c.Spec.MuzzleEnergyJ, "cannot exceed muzzle energy (drag only removes)");
            Assert.Greater(r.EnergyJ, 0.8 * c.Spec.MuzzleEnergyJ, "500 m flat shot keeps most energy");
            Assert.LessOrEqual(r.EnergyJ, r.ImpactorKineticJ * (1 + 1e-12));
            Assert.AreEqual(0, c.LiveProjectiles);
            Assert.AreEqual(1, bus.Count<WeaponFired>()); Assert.AreEqual(1, bus.Count<RecoilImpulse>());
        }

        [Test]
        public void Shot_FeedsRealDamageModel_AndBusReportsStateChange()
        {
            var (c, w, l) = Rig(); var bus = new RecordingBus();
            var dm = new DamageModel(bus); dm.Register(B(1), MidRise);
            var router = new ImpactRouter(bus, dm, null);
            c.TryFire(EntityId.FromValue(1), new CVec3(0, 10, 0), new CVec3(0, 0, 1), CVec3.Zero, 3, bus, out _);
            Fly(c, w, router, 200);
            Assert.AreEqual(1, router.BuildingImpacts);
            Assert.IsTrue(dm.TryGetState(B(1), out var st));
            Assert.Greater(st.State, (byte)DamageState.None);
            Assert.AreEqual(router.EnergyToBuildingsJ, st.CumulativeEnergyJ, 1e-3);
            var dmg = bus.Of<BuildingDamaged>();
            Assert.GreaterOrEqual(dmg.Count, 1);
            Assert.AreEqual((byte)DamageCause.Impact, dmg[0].Cause);
            Assert.AreEqual(EntityId.FromValue(1), dmg[0].Source);
            var imp = bus.Of<ImpactImpulse>();
            Assert.AreEqual(1, imp.Count); Assert.Greater(imp[0].ImpulseNs, 0); Assert.Greater(imp[0].Step, 0);
        }

        [Test]
        public void ShotOverTheBuilding_Misses_AndLandsOnGround_WithNoDamage()
        {
            var (c, w, l) = Rig(); var bus = new RecordingBus();
            var dm = new DamageModel(bus); dm.Register(B(1), MidRise);
            var router = new ImpactRouter(bus, dm, null) { Log = new List<ImpactRecord>() };
            c.TryFire(EntityId.FromValue(1), new CVec3(0, 10, 0), new CVec3(0, 0.4, 1), CVec3.Zero, 0, bus, out _);
            Fly(c, w, router, 3000);
            Assert.AreEqual(1, router.Impacts); Assert.AreEqual(TargetKind.Ground, router.Log[0].Kind);
            Assert.Greater(router.Log[0].Point.Z, 520);
            Assert.IsTrue(dm.TryGetState(B(1), out var st)); Assert.AreEqual((byte)DamageState.None, st.State);
            Assert.AreEqual(0, bus.Count<BuildingDamaged>());
        }

        [Test]
        public void FastShell_DoesNotTunnelThroughAThinWall()
        {
            var w = new SimpleTargetWorld(); w.AddBuilding(B(9), new CVec3(-50, 0, 200), new CVec3(50, 60, 201), 1e6);
            var c = new Cannon(CannonSpec.Heavy()); var l = new RecordingListener();
            c.TryFire(EntityId.FromValue(1), new CVec3(0, 10, 0), new CVec3(0, 0, 1), CVec3.Zero, 0, null, out _);
            Fly(c, w, l, 50);
            Assert.AreEqual(1, l.Records.Count, "shell moves ~15 m per 0.02 s step, wall is 1 m");
        }

        [Test]
        public void KaijuHit_RemovesHitPoints_FromTheHealthContract()
        {
            var w = new SimpleTargetWorld(); var kaiju = EntityId.FromValue(500);
            w.AddCapsule(kaiju, TargetKind.Kaiju, new CVec3(0, 0, 400), 15, 120, 5e7);
            var hp = new LinearEntityHealth(); hp.Register(kaiju, 1000);
            var router = new ImpactRouter(null, null, hp);
            var c = new Cannon(CannonSpec.Heavy());
            c.TryFire(EntityId.FromValue(1), new CVec3(0, 60, 0), new CVec3(0, 0, 1), CVec3.Zero, 0, null, out _);
            Fly(c, w, router, 200);
            Assert.AreEqual(1, router.EntityImpacts);
            Assert.IsTrue(hp.TryGetHitPoints(kaiju, out var left));
            Assert.Less(left, 1000); Assert.Greater(left, 0);
            Assert.AreEqual(1000 - router.HitPointsRemoved, left, 1e-9);
            Assert.AreEqual(router.HitPointsRemoved, router.Log == null ? router.HitPointsRemoved : 0, 1e-9);
        }

        [Test]
        public void ShooterCapsule_IsIgnored()
        {
            var w = new SimpleTargetWorld(); var me = EntityId.FromValue(1);
            w.AddCapsule(me, TargetKind.Mech, new CVec3(0, 0, 0), 15, 80, 2e6);
            var c = new Cannon(CannonSpec.Heavy()); var l = new RecordingListener();
            c.TryFire(me, new CVec3(0, 40, 0), new CVec3(0, 0, 1), CVec3.Zero, 0, null, out _);
            Fly(c, w, l, 20);
            Assert.AreEqual(0, l.Records.Count);
        }

        [Test]
        public void Ammo_IsFinite_AndReloadGatesFire()
        {
            var spec = CannonSpec.Heavy(); spec.MaxAmmo = 2; spec.ReloadSteps = 5;
            var c = new Cannon(spec); var w = new SimpleTargetWorld(); var l = new RecordingListener();
            Assert.IsTrue(c.TryFire(default, CVec3.Zero, new CVec3(0, 0, 1), CVec3.Zero, 0, null, out _));
            Assert.IsFalse(c.TryFire(default, CVec3.Zero, new CVec3(0, 0, 1), CVec3.Zero, 0, null, out _), "reloading");
            for (int i = 0; i < 5; i++) c.Step(0.02, i, w, l);
            Assert.IsTrue(c.TryFire(default, CVec3.Zero, new CVec3(0, 0, 1), CVec3.Zero, 5, null, out _));
            for (int i = 0; i < 5; i++) c.Step(0.02, i, w, l);
            Assert.AreEqual(0, c.Ammo);
            Assert.IsFalse(c.TryFire(default, CVec3.Zero, new CVec3(0, 0, 1), CVec3.Zero, 9, null, out _), "empty");
            c.Resupply(10); Assert.AreEqual(2, c.Ammo);
        }

        [Test]
        public void Recoil_IsOppositeToTheShot_AndAtTheMuzzle()
        {
            var c = new Cannon(CannonSpec.Heavy());
            c.TryFire(EntityId.FromValue(1), new CVec3(1, 2, 3), new CVec3(0, 1, 1), CVec3.Zero, 7, null, out var r);
            double s = Math.Sqrt(0.5), J = c.Spec.RecoilImpulseNs;
            Assert.AreEqual(0, r.ImpulseX, 1e-6); Assert.AreEqual(-J * s, r.ImpulseY, 1e-3); Assert.AreEqual(-J * s, r.ImpulseZ, 1e-3);
            Assert.AreEqual(2, r.PointY); Assert.AreEqual(7, r.Step);
        }

        [Test]
        public void ShooterVelocity_IsInherited()
        {
            var c = new Cannon(CannonSpec.Light(), BallisticEnvironment.Standard());
            c.TryFire(default, new CVec3(0, 100, 0), new CVec3(0, 0, 1), new CVec3(3, 0, 0), 0, null, out _);
            Assert.AreEqual(3, c.GetProjectile(0).Vel.X, 1e-12);
            Assert.AreEqual(827, c.GetProjectile(0).Vel.Z, 1e-9);
        }

        [Test]
        public void NonFiniteInputs_AreRefused()
        {
            var c = new Cannon(CannonSpec.Heavy());
            Assert.IsFalse(c.TryFire(default, new CVec3(double.NaN, 0, 0), new CVec3(0, 0, 1), CVec3.Zero, 0, null, out _));
            Assert.IsFalse(c.TryFire(default, CVec3.Zero, new CVec3(0, double.PositiveInfinity, 1), CVec3.Zero, 0, null, out _));
            Assert.AreEqual(20, c.Ammo);
        }

        [Test]
        public void Determinism_SameShotSameRecord()
        {
            ImpactRecord Run()
            {
                var (c, w, l) = Rig();
                c.TryFire(EntityId.FromValue(1), new CVec3(0, 10, 0), new CVec3(0.01, 0.002, 1), new CVec3(1, 0, 2), 0, null, out _);
                Fly(c, w, l, 300);
                return l.Records[0];
            }
            var a = Run(); var b = Run();
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(a.EnergyJ), BitConverter.DoubleToInt64Bits(b.EnergyJ));
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(a.Point.Y), BitConverter.DoubleToInt64Bits(b.Point.Y));
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(a.ImpulseNs), BitConverter.DoubleToInt64Bits(b.ImpulseNs));
        }

        [Test]
        public void MetricDraft_M061_BallisticAccuracyVsAnalytic_PrintsMaxRelError()
        {
            // Vacuum shot, 20 s: simulated landing-free position vs closed form, over a grid of angles. Draft threshold 1e-9 relative.
            double worst = 0; var env = BallisticEnvironment.Standard();
            for (int deg = 5; deg <= 85; deg += 10)
            {
                double th = deg * Math.PI / 180, v = 762;
                CVec3 pos = new CVec3(0, 0, 0), vel = new CVec3(v * Math.Cos(th), v * Math.Sin(th), 0);
                for (int i = 0; i < 1000; i++) Ballistics.Step(ref pos, ref vel, 1225, 0.13, DragCurve.Constant(0), env, 0.02);
                double x = v * Math.Cos(th) * 20, y = v * Math.Sin(th) * 20 - 0.5 * env.Gravity * 400;
                worst = Math.Max(worst, Math.Max(Math.Abs(pos.X - x) / x, Math.Abs(pos.Y - y) / Math.Abs(y)));
            }
            TestContext.WriteLine("M-061 draft max relative error: " + worst);
            Assert.Less(worst, 1e-9);
        }
    }

    public class MeleeTests
    {
        static BuildingId B(ulong n) { return BuildingId.From(BuildingSource.OsmWay, n); }

        // Shoulder at the origin, plane toward +z. Elevation sweeps down from 50 deg to -30 deg.
        static SwingCommand Down(double dur = 2) { return new SwingCommand { YawRelative = 0, StartElevation = 50 * Math.PI / 180, EndElevation = -30 * Math.PI / 180, Duration = dur }; }

        static void Run(MeleeArm arm, ICombatTargetQuery q, IImpactListener l, int steps, double dt = 0.02)
        {
            for (int i = 0; i < steps; i++) arm.Step(dt, i, EntityId.FromValue(1), CVec3.Zero, CVec3.Zero, q, l);
        }

        [Test]
        public void ShortDuration_IsStretched_SoPeakTipSpeedNeverExceedsTheLimit()
        {
            var arm = new MeleeArm(ArmSpec.Default());
            Assert.IsTrue(arm.TryBegin(default, Down(0.05), CVec3.Zero, 0, 0, null));
            double v = MeleeArm.PeakOmega(80 * Math.PI / 180, arm.SwingDuration) * arm.Spec.ReachM;
            Assert.LessOrEqual(v, arm.Spec.MaxTipSpeed * (1 + 1e-12));
            Assert.AreEqual(arm.Spec.MaxTipSpeed, v, 1e-9, "request was faster than allowed: clamped exactly to the limit");
        }

        [Test]
        public void Inertia_And_MaxEnergy_FollowTheDeclaredMasses()
        {
            var s = ArmSpec.Default();
            Assert.AreEqual((60000 / 3.0 + 20000) * 1600, s.InertiaKgM2, 1);
            Assert.AreEqual(18e6, s.MaxSwingEnergyJ, 1e3);
            Assert.AreEqual(74.4e6, ArmSpec.Max().MaxSwingEnergyJ, 0.2e6);
        }

        [Test]
        public void SwingIntoFlatRoof_Immovable_ConservesEnergyAndReboundsByRestitution()
        {
            // Roof at y = 15 m: the tip (reach 40 m, shoulder at origin) meets it where the arm descends through ~22 deg.
            var spec = ArmSpec.Default(); spec.Restitution = 0.25; spec.ContactRadiusM = 1;
            var arm = new MeleeArm(spec); var w = new SimpleTargetWorld();
            w.AddBuilding(B(1), new CVec3(-100, 0, -100), new CVec3(100, 15, 100), 0); // mass 0 = immovable
            var l = new RecordingListener();
            Assert.IsTrue(arm.TryBegin(EntityId.FromValue(1), Down(3), CVec3.Zero, 0, 0, null));
            Run(arm, w, l, 400);
            Assert.AreEqual(1, l.Records.Count);
            var r = l.Records[0];
            Assert.AreEqual(1, r.Normal.Y, 1e-9);
            double I = spec.InertiaKgM2, omegaAfter = arm.OmegaAfterContact;
            // lambda = L cos(elevation) > 0 for the roof normal; omega before = -omegaAfter / e (rebound reverses by e).
            double omega = -omegaAfter / spec.Restitution;
            double keBefore = 0.5 * I * omega * omega, keAfter = 0.5 * I * omegaAfter * omegaAfter;
            Assert.AreEqual(keBefore, keAfter + r.EnergyJ, 1e-6 * keBefore);
            Assert.LessOrEqual(r.EnergyJ, spec.MaxSwingEnergyJ * (1 + 1e-9));
            Assert.AreEqual(r.ImpactorKineticJ, keBefore, 1e-6 * keBefore);
            Assert.IsFalse(arm.Active);
        }

        [Test]
        public void SwingIntoMovableTarget_GivesLessEnergyThanImmovable_AndBoundedByKinetic()
        {
            double Energy(double mass)
            {
                var spec = ArmSpec.Default(); spec.ContactRadiusM = 1; var arm = new MeleeArm(spec); var w = new SimpleTargetWorld();
                w.AddBuilding(B(1), new CVec3(-100, 0, -100), new CVec3(100, 15, 100), mass);
                var l = new RecordingListener(); arm.TryBegin(EntityId.FromValue(1), Down(3), CVec3.Zero, 0, 0, null);
                Run(arm, w, l, 400); Assert.AreEqual(1, l.Records.Count);
                Assert.LessOrEqual(l.Records[0].EnergyJ, l.Records[0].ImpactorKineticJ);
                return l.Records[0].EnergyJ;
            }
            double fixedE = Energy(0), light = Energy(2e4), heavy = Energy(1e9);
            Assert.Less(light, fixedE); Assert.AreEqual(fixedE, heavy, 1e-3 * fixedE);
        }

        [Test]
        public void HeavierTip_AtTheSameSpeed_DeliversMoreEnergy()
        {
            double Energy(double tip)
            {
                var spec = ArmSpec.Default(); spec.TipMassKg = tip; spec.ContactRadiusM = 1; var arm = new MeleeArm(spec); var w = new SimpleTargetWorld();
                w.AddBuilding(B(1), new CVec3(-100, 0, -100), new CVec3(100, 15, 100), 0);
                var l = new RecordingListener(); arm.TryBegin(EntityId.FromValue(1), Down(3), CVec3.Zero, 0, 0, null);
                Run(arm, w, l, 400); return l.Records[0].EnergyJ;
            }
            Assert.Greater(Energy(40000), Energy(20000)); Assert.Greater(Energy(20000), Energy(5000));
        }

        [Test]
        public void SwingThroughEmptySpace_MissesAndRecovers()
        {
            var arm = new MeleeArm(ArmSpec.Default()); var l = new RecordingListener(); var w = new SimpleTargetWorld();
            Assert.IsTrue(arm.TryBegin(default, Down(2), CVec3.Zero, 0, 0, null));
            Assert.IsFalse(arm.TryBegin(default, Down(2), CVec3.Zero, 0, 0, null), "already swinging");
            Run(arm, w, l, 150);
            Assert.IsFalse(arm.Active); Assert.AreEqual(0, l.Records.Count);
            Assert.IsFalse(arm.CanSwing, "recovery");
            for (int i = 0; i < arm.Spec.RecoverySteps; i++) arm.Step(0.02, i, default, CVec3.Zero, CVec3.Zero, w, l);
            Assert.IsTrue(arm.CanSwing);
        }

        [Test]
        public void TargetOutsideReach_OrOutsideThePlane_IsMissed()
        {
            var arm = new MeleeArm(ArmSpec.Default()); var w = new SimpleTargetWorld(); var l = new RecordingListener();
            w.AddBuilding(B(1), new CVec3(-100, 0, 80), new CVec3(100, 15, 200), 0);     // beyond 40 m reach
            w.AddBuilding(B(2), new CVec3(200, 0, -100), new CVec3(260, 15, 100), 0);    // off to the side of the swing plane
            arm.TryBegin(default, Down(3), CVec3.Zero, 0, 0, null);
            Run(arm, w, l, 400);
            Assert.AreEqual(0, l.Records.Count);
        }

        [Test]
        public void BladeSamples_ContactEarlierPointOnTheEdge_WithLowerLever()
        {
            var spec = ArmSpec.Default(); spec.BladeLengthM = 20; spec.BladeSamples = 5; spec.ContactRadiusM = 1;
            var arm = new MeleeArm(spec); var w = new SimpleTargetWorld(); var l = new RecordingListener();
            w.AddBuilding(B(1), new CVec3(-100, 0, -100), new CVec3(100, 15, 100), 0);
            arm.TryBegin(default, Down(3), CVec3.Zero, 0, 0, null);
            Run(arm, w, l, 400);
            Assert.AreEqual(1, l.Records.Count);
            Assert.LessOrEqual(l.Records[0].EnergyJ, spec.MaxSwingEnergyJ * (1 + 1e-9));
        }

        [Test]
        public void MoreSamplesThanOne_OnlyOneContactPerSwing()
        {
            var spec = ArmSpec.Default(); spec.BladeLengthM = 30; spec.BladeSamples = 7;
            var arm = new MeleeArm(spec); var w = new SimpleTargetWorld(); var l = new RecordingListener();
            w.AddBuilding(B(1), new CVec3(-100, 0, -100), new CVec3(100, 15, 100), 1e8);
            arm.TryBegin(default, Down(3), CVec3.Zero, 0, 0, null);
            Run(arm, w, l, 400);
            Assert.AreEqual(1, l.Records.Count);
        }

        [Test]
        public void Determinism_SameSwingSameRecord()
        {
            ImpactRecord One()
            {
                var arm = new MeleeArm(ArmSpec.Default()); var w = new SimpleTargetWorld(); var l = new RecordingListener();
                w.AddBuilding(B(1), new CVec3(-100, 0, -100), new CVec3(100, 15, 100), 3e6);
                arm.TryBegin(EntityId.FromValue(1), Down(2.5), new CVec3(0, 0, 0), 0.3, 0, null);
                Run(arm, w, l, 400); return l.Records[0];
            }
            var a = One(); var b = One();
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(a.EnergyJ), BitConverter.DoubleToInt64Bits(b.EnergyJ));
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(a.ImpulseNs), BitConverter.DoubleToInt64Bits(b.ImpulseNs));
        }

        [Test]
        public void NonFiniteSwing_IsRefused()
        {
            var arm = new MeleeArm(ArmSpec.Default());
            Assert.IsFalse(arm.TryBegin(default, new SwingCommand { StartElevation = double.NaN, EndElevation = 0, Duration = 1 }, CVec3.Zero, 0, 0, null));
            Assert.IsFalse(arm.TryBegin(default, new SwingCommand { StartElevation = 0, EndElevation = 0, Duration = 1 }, CVec3.Zero, 0, 0, null));
            Assert.IsFalse(arm.TryBegin(default, Down(0), CVec3.Zero, 0, 0, null));
            Assert.AreEqual(0, arm.Swings);
        }

        [Test]
        public void MetricDraft_M062_EveryImpactEnergyIsBoundedBySwingKinetic_AcrossAGrid()
        {
            int n = 0; double worst = 0;
            foreach (double mass in new[] { 0.0, 1e5, 1e7, 1e10 })
                foreach (double dur in new[] { 0.5, 1.0, 2.0, 4.0 })
                {
                    var spec = ArmSpec.Max(); spec.ContactRadiusM = 1; var arm = new MeleeArm(spec); var w = new SimpleTargetWorld(); var l = new RecordingListener();
                    w.AddBuilding(B(1), new CVec3(-100, 0, -100), new CVec3(100, 15, 100), mass);
                    arm.TryBegin(default, Down(dur), CVec3.Zero, 0, 0, null);
                    Run(arm, w, l, 600);
                    foreach (var r in l.Records) { n++; worst = Math.Max(worst, r.EnergyJ / spec.MaxSwingEnergyJ); Assert.LessOrEqual(r.EnergyJ, spec.MaxSwingEnergyJ * (1 + 1e-9)); }
                }
            TestContext.WriteLine("M-062 draft: " + n + " impacts, worst E / E_swing_max = " + worst);
            Assert.Greater(n, 0);
        }
    }

    public class CombatSystemTests
    {
        static BuildingId B(ulong n) { return BuildingId.From(BuildingSource.OsmWay, n); }

        [Test]
        public void MountedCannonAndArm_DriveDestructionAndKaijuThroughTheSystem()
        {
            var bus = new RecordingBus();
            var dm = new DamageModel(bus); dm.Register(B(1), new BuildingAttributes { HeightM = 25, Levels = 6, FootprintM2 = 600 });
            var kaiju = EntityId.FromValue(900); var hp = new LinearEntityHealth(); hp.Register(kaiju, 1000);
            var w = new SimpleTargetWorld();
            w.AddBuilding(B(1), new CVec3(-30, 0, 500), new CVec3(30, 80, 520), 5e7);
            w.AddCapsule(kaiju, TargetKind.Kaiju, new CVec3(200, 0, 0), 20, 120, 5e7);
            var router = new ImpactRouter(bus, dm, hp);
            var sys = new CombatSystem(bus, w, router);
            var mount = new FakeMount { Position = new CVec3(0, 0, 0), Heading = 0 };
            var cannon = sys.AddCannon(mount, CannonSpec.Heavy(), 5, 60);
            var arm = sys.AddMelee(mount, new ArmSpec { ShoulderHeightM = 55, ReachM = 40, ContactRadiusM = 2 });
            Assert.IsTrue(sys.Fire(cannon, 0, 0));
            Assert.AreEqual(1, sys.RecoilOutbox.Count);
            // Swing toward +x (yaw 90 deg) at the kaiju capsule 200 m away: out of reach, so only the shell hits.
            Assert.IsTrue(sys.Swing(arm, new SwingCommand { YawRelative = Math.PI / 2, StartElevation = 0.8, EndElevation = -0.5, Duration = 2 }));
            for (int i = 0; i < 400; i++) sys.Step(0.02);
            Assert.AreEqual(1, router.BuildingImpacts);
            Assert.AreEqual(0, router.EntityImpacts);
            Assert.IsTrue(dm.TryGetState(B(1), out var st)); Assert.Greater(st.State, (byte)0);
            Assert.AreEqual(2, bus.Count<WeaponFired>());
            // Move the kaiju into reach and swing again.
            var w2 = new SimpleTargetWorld(); w2.AddCapsule(kaiju, TargetKind.Kaiju, new CVec3(45, 0, 0), 12, 120, 5e7);
            var sys2 = new CombatSystem(bus, w2, router); var m2 = sys2.AddMelee(mount, new ArmSpec { ShoulderHeightM = 55, ReachM = 40, ContactRadiusM = 2 });
            Assert.IsTrue(sys2.Swing(m2, new SwingCommand { YawRelative = Math.PI / 2, StartElevation = 0.3, EndElevation = -0.3, Duration = 2 }));
            for (int i = 0; i < 400; i++) sys2.Step(0.02);
            Assert.AreEqual(1, router.EntityImpacts);
            hp.TryGetHitPoints(kaiju, out var left); Assert.Less(left, 1000);
        }

        [Test]
        public void Router_WithNoSinks_StillPublishesImpulseAndNeverThrows()
        {
            var bus = new RecordingBus(); var router = new ImpactRouter(bus, null, null);
            router.OnImpact(new ImpactRecord { Kind = TargetKind.Building, Building = B(5), EnergyJ = 10, ImpulseNs = 5 });
            router.OnImpact(new ImpactRecord { Kind = TargetKind.Kaiju, Target = EntityId.FromValue(3), EnergyJ = 10 });
            Assert.AreEqual(2, bus.Count<ImpactImpulse>());
        }

        [Test]
        public void UnknownBuilding_IsNotCountedAsDeliveredEnergy()
        {
            var dm = new DamageModel(null); var router = new ImpactRouter(null, dm, null);
            router.OnImpact(new ImpactRecord { Kind = TargetKind.Building, Building = B(77), EnergyJ = 1e9 });
            Assert.AreEqual(0, router.EnergyToBuildingsJ);
        }

        [Test]
        public void Flags_AreTheDeclaredNames() { Assert.AreEqual("combat.melee", CombatFlags.Melee); Assert.AreEqual("combat.cannon", CombatFlags.Cannon); }
    }
}
