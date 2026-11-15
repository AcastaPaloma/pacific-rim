using System;
using System.Numerics;
using Game.Cameras;
using Game.Control.Contracts;
using Game.Core;
using Game.Units.Mech.Contracts;
using NUnit.Framework;

namespace Game.Units.Placeholder.Tests
{
    /// <summary>TEMPORARY body (T-030c). Engine-free: runs under dotnet test and Unity EditMode.</summary>
    public class PlaceholderBodyTests
    {
        private const float Dt = 0.02f;
        private static PlaceholderBody NewBody(float heading = 0f) { var b = new PlaceholderBody(EntityId.FromValue(7), PlaceholderLimits.Default()); b.Place(0, 0, 0, heading); return b; }
        private static IntentFrame Walk(float x, float z, float effort = 1f) => new IntentFrame { move = MoveId.Walk, target = new Vector3(x, 0, z), effort = effort, pilots = 1 };
        private static void Run(PlaceholderBody b, IntentFrame f, int steps) { for (int i = 0; i < steps; i++) b.Step(f, Dt); }

        [Test]
        public void Walk_AcceleratesAtDeclaredLimit_NotInstantly()
        {
            var b = NewBody();
            Run(b, Walk(0, 5000), 50); // 1 s
            Assert.AreEqual(b.Limits.Accel * 1f, b.Speed, 1e-3f);
            Assert.IsTrue((b.Causes & LimitCause.Accel) != 0);
        }

        [Test]
        public void Walk_CapsAtMaxSpeedTimesEffort()
        {
            var b = NewBody();
            Run(b, Walk(0, 100000, 0.5f), 2000);
            Assert.AreEqual(0.5f * b.Limits.MaxSpeed, b.Speed, 1e-3f);
        }

        [Test]
        public void Walk_StopsAtTarget_NoOvershoot()
        {
            var b = NewBody();
            float maxZ = 0;
            var f = Walk(0, 120);
            for (int i = 0; i < 4000; i++) { b.Step(f, Dt); maxZ = Math.Max(maxZ, b.Z); }
            Assert.IsTrue(b.HasArrived);
            Assert.Less(Math.Abs(b.Z - 120), b.Limits.ArriveRadius + 0.5f);
            Assert.Less(maxZ, 120 + b.Limits.ArriveRadius, "no overshoot past the arrive radius");
            Assert.AreEqual(0f, b.Speed, 0.05f);
        }

        [Test]
        public void Walk_TurnsBeforeWalking_WhenTargetIsBehind()
        {
            var b = NewBody(0f);
            b.Step(Walk(0, -200), Dt); // behind: heading error pi
            Assert.AreEqual(0f, b.Speed, 1e-6f);
            Assert.IsTrue((b.Causes & LimitCause.TurnBeforeWalk) != 0);
            Run(b, Walk(0, -200), 6000);
            Assert.Less(Math.Abs(PlaceholderBody.Wrap(b.Heading - (float)Math.PI)), 0.05f);
            Assert.Less(b.Z, 0f, "walks toward the target once turned");
        }

        [Test]
        public void Turn_RespectsYawRateLimit()
        {
            var b = NewBody(0f);
            var f = new IntentFrame { move = MoveId.Turn, target = new Vector3(100, 0, 0), pilots = 1 };
            float maxRate = 0f;
            for (int i = 0; i < 4000; i++) { b.Step(f, Dt); maxRate = Math.Max(maxRate, Math.Abs(b.YawRate)); }
            Assert.LessOrEqual(maxRate, b.Limits.MaxYawRate + 1e-5f);
            Assert.AreEqual((float)(Math.PI / 2), b.Heading, 0.02f);
            Assert.AreEqual(0f, b.Speed, 1e-6f);
        }

        [Test]
        public void YawAcceleration_IsLimited()
        {
            var b = NewBody(0f);
            b.Step(new IntentFrame { move = MoveId.Turn, target = new Vector3(100, 0, 0), pilots = 1 }, Dt);
            Assert.LessOrEqual(Math.Abs(b.YawRate), b.Limits.YawAccel * Dt + 1e-7f);
        }

        [Test]
        public void EmergencyStop_StopsShorterThanStop()
        {
            float Dist(MoveId m)
            {
                var b = NewBody(); Run(b, Walk(0, 100000), 1500);
                float z0 = b.Z; var stop = new IntentFrame { move = m };
                for (int i = 0; i < 4000 && b.Speed > 0; i++) b.Step(stop, Dt);
                Assert.AreEqual(0f, b.Speed, 1e-6f);
                return b.Z - z0;
            }
            Assert.Less(Dist(MoveId.EmergencyStop), Dist(MoveId.Stop) * 0.6f);
        }

        [Test]
        public void DestroyedBody_StopsAndIgnoresIntent()
        {
            var b = NewBody(); Run(b, Walk(0, 100000), 1000);
            Assert.Greater(b.Speed, 1f);
            b.Damage.ApplyDamage(1e9f);
            Run(b, Walk(0, 100000), 4000);
            Assert.AreEqual(0f, b.Speed, 1e-6f);
        }

        [Test]
        public void BadEffort_IsClampedAndReported()
        {
            var b = NewBody(); b.Step(Walk(0, 1000, 5f), Dt);
            Assert.IsTrue((b.Causes & LimitCause.EffortRange) != 0);
            Assert.LessOrEqual(b.Speed, b.Limits.Accel * Dt + 1e-6f);
            b.Step(Walk(0, 1000, float.NaN), Dt);
            Assert.IsFalse(float.IsNaN(b.Speed) || float.IsNaN(b.Z));
        }

        [Test]
        public void SameInputs_SameState()
        {
            PlaceholderBody Make() { var b = NewBody(); for (int i = 0; i < 700; i++) b.Step(i % 300 < 200 ? Walk(40, 60) : Walk(-30, 10, 0.7f), Dt); return b; }
            var a = Make(); var c = Make();
            Assert.AreEqual(a.X, c.X); Assert.AreEqual(a.Z, c.Z); Assert.AreEqual(a.Heading, c.Heading); Assert.AreEqual(a.Speed, c.Speed);
        }

        [Test]
        public void ImplementsCameraTarget_ReportsGroundPositionAndHeight()
        {
            var b = NewBody(); b.Place(3, 1, -4, 0);
            ICameraTarget t = b;
            Assert.IsTrue(t.TryGetTarget(out var p, out float h));
            Assert.AreEqual(3f, p.X); Assert.AreEqual(1f, p.Y); Assert.AreEqual(-4f, p.Z); Assert.AreEqual(b.Limits.HeightM, h);
        }

        [Test]
        public void FromWalkTuning_ConvertsGravityAndTstarUnits()
        {
            var t = new WalkTuning { reference_leg_length_m = 40, fr_max = 0.3, accel_start_g = 0.01, decel_normal_g = 0.02, decel_emergency_g = 0.05, yaw_rate_max_tstar_deg = 10, yaw_accel_max_tstar_deg = 5, turn_first_deg = 45 };
            var l = PlaceholderLimits.FromWalkTuning(t);
            double g = 9.80665, tstar = Math.Sqrt(40 / g);
            Assert.AreEqual(0.3 * Math.Sqrt(g * 40), l.MaxSpeed, 1e-3);
            Assert.AreEqual(0.01 * g, l.Accel, 1e-5);
            Assert.AreEqual(0.02 * g, l.Decel, 1e-5);
            Assert.AreEqual(0.05 * g, l.EmergencyDecel, 1e-5);
            Assert.AreEqual(10 * Math.PI / 180 / tstar, l.MaxYawRate, 1e-5);
            Assert.AreEqual(5 * Math.PI / 180 / (tstar * tstar), l.YawAccel, 1e-5);
            Assert.AreEqual(Math.PI / 4, l.TurnFirstRad, 1e-5);
        }

        [Test]
        public void FromWalkTuning_MissingFields_KeepDefaults()
        {
            var d = PlaceholderLimits.Default();
            var l = PlaceholderLimits.FromWalkTuning(new WalkTuning());
            Assert.AreEqual(d.MaxSpeed, l.MaxSpeed); Assert.AreEqual(d.Accel, l.Accel);
            Assert.AreEqual(d.Accel, PlaceholderLimits.FromWalkTuning(null).Accel);
        }
    }

    public class PlaceholderDamageRecordTests
    {
        private static PlaceholderDamageRecord Rec() => new PlaceholderDamageRecord(EntityId.FromValue(1), 900f);

        [Test]
        public void States_FollowThresholds()
        {
            var r = Rec();
            Assert.AreEqual(PlaceholderDamageState.Intact, r.State);
            r.ApplyDamage(301); Assert.AreEqual(PlaceholderDamageState.Damaged, r.State);
            r.ApplyDamage(300); Assert.AreEqual(PlaceholderDamageState.Critical, r.State);
            r.ApplyDamage(10000); Assert.AreEqual(PlaceholderDamageState.Destroyed, r.State);
            Assert.AreEqual(0f, r.HitPoints);
        }

        [Test]
        public void ApplyDamage_ReturnsActualAndIgnoresJunk()
        {
            var r = Rec();
            Assert.AreEqual(100f, r.ApplyDamage(100f));
            Assert.AreEqual(0f, r.ApplyDamage(-5f)); Assert.AreEqual(0f, r.ApplyDamage(float.NaN)); Assert.AreEqual(0f, r.ApplyDamage(float.PositiveInfinity));
            Assert.AreEqual(1, r.HitsTaken);
            Assert.AreEqual(800f, r.HitPoints);
        }

        [Test]
        public void Repair_ClampsToMax()
        {
            var r = Rec(); r.ApplyDamage(500);
            Assert.AreEqual(500f, r.Repair(9999f)); Assert.AreEqual(900f, r.HitPoints);
            r.ApplyDamage(900); Assert.AreEqual(PlaceholderDamageState.Destroyed, r.State);
            r.RepairFully(); Assert.AreEqual(PlaceholderDamageState.Intact, r.State);
        }

        [Test]
        public void ExportImport_RoundTripsExactly_AndClamps()
        {
            var r = Rec(); r.ApplyDamage(123.5f); r.ApplyDamage(10f);
            var d = r.Export();
            var r2 = Rec(); r2.Import(d);
            Assert.AreEqual(r.HitPoints, r2.HitPoints); Assert.AreEqual(r.HitsTaken, r2.HitsTaken); Assert.AreEqual(r.State, r2.State);
            r2.Import(new PlaceholderDamageData { MaxHitPoints = 100, HitPoints = 5000, HitsTaken = -3 });
            Assert.AreEqual(100f, r2.HitPoints); Assert.AreEqual(0, r2.HitsTaken);
        }

        [Test]
        public void Ctor_RejectsBadMax()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlaceholderDamageRecord(EntityId.FromValue(1), 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlaceholderDamageRecord(EntityId.FromValue(1), float.NaN));
        }
    }
}
