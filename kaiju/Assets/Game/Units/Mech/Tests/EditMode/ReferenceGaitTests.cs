using System;
using Game.Physics.Body.Contracts;
using Game.Units.Mech;
using Game.Units.Mech.Contracts;
using NUnit.Framework;

namespace Game.Units.Mech.Tests
{
    /// <summary>The reference gait is checked on its own, before any physics (T-005 fix cycle 3). Speeds: the walk that is stable today (step 0.25 l) and Froude 0.10, 0.20, 0.30.</summary>
    public class ReferenceGaitTests
    {
        const double G = 9.81, YawBudget = 1.65e8;   // N m: stance-foot friction moment, research/R-007 section 2.2
        const int N = 2400;
        static readonly double[] Froudes = { 0.0213, 0.05, 0.10, 0.20, 0.30 };
        /// <summary>Speeds at which the reference meets every joint range and the friction budget (the continuation range of this cycle). Above them the values are reported in the test output and in EVIDENCE.md.</summary>
        static readonly double[] ValidFroudes = { 0.0213, 0.05, 0.10 };

        static ReferenceGait Make(double froude, out GaitGeometry geo, out WalkTuning t)
        {
            t = Repo.Walk(); geo = GaitGeometry.From(Repo.Load<MechFrameData>("Mech/mech_frame_balanced.json"));
            var gait = new ReferenceGait(geo, t, G);
            double v = Math.Sqrt(froude * G * geo.HipHeight);
            gait.Configure(v, froude < 0.05 ? 0.25 * geo.HipHeight : 0);
            return gait;
        }

        [Test]
        public void StepLengthAndCadence_MatchTheTarget([ValueSource(nameof(Froudes))] double fr)
        {
            var gait = Make(fr, out var geo, out var t);
            double v = Math.Sqrt(fr * G * geo.HipHeight);
            Assert.That(gait.StepLength / gait.StepTime, Is.EqualTo(v).Within(1e-9), "speed = step length x cadence");
            if (fr >= 0.05)
            {
                double alexander = 2.3 * Math.Pow(fr, -0.2) * Math.Sqrt(geo.HipHeight / G);
                Assert.That(2 * gait.StepTime / alexander, Is.InRange(0.85, 1.15), "stride period against R-001 M-001.2");
                Assert.That(gait.Duty, Is.InRange(0.60, 0.72), "R-001 M-001.3");
            }
            Assert.That(gait.Rollover, fr < 0.05 ? Is.LessThan(0.35) : Is.GreaterThan(0.5), "nearly flat-footed at today's step, rolling over as the step grows");
        }

        [Test]
        public void Feet_NeverPenetrateTheGround_AndTheSupportPointIsStationaryInTheWorld([ValueSource(nameof(Froudes))] double fr)
        {
            var gait = Make(fr, out var geo, out _);
            double T = 2 * gait.StepTime; double? pivot = null; FootSupport last = FootSupport.Swing; double flatAnkle = double.NaN;
            for (int i = 0; i < N; i++)
            {
                double phase = (double)i / N; var s = gait.Sample(phase); var leg = s.L;
                double c = Math.Cos(leg.FootPitch), sn = Math.Sin(leg.FootPitch);
                double heelY = leg.Ankle.Y + (-geo.HeelZ * sn - geo.AnkleHeight * c), toeY = leg.Ankle.Y + (-geo.ToeZ * sn - geo.AnkleHeight * c);
                double heelZ = leg.Ankle.Z + (geo.HeelZ * c - geo.AnkleHeight * sn), toeZ = leg.Ankle.Z + (geo.ToeZ * c - geo.AnkleHeight * sn);
                Assert.That(heelY, Is.GreaterThan(-2e-3), "heel under the ground at phase " + phase); Assert.That(toeY, Is.GreaterThan(-2e-3), "toe under the ground at phase " + phase);
                if (leg.Support == FootSupport.Swing) { pivot = null; last = leg.Support; continue; }
                double worldTravel = gait.Speed * phase * T;
                double point = (leg.Support == FootSupport.Heel ? heelZ : leg.Support == FootSupport.Toe ? toeZ : (double)leg.Ankle.Z) + worldTravel;
                if (leg.Support != last) { pivot = point; last = leg.Support; }
                Assert.That(point, Is.EqualTo(pivot.Value).Within(2e-3), leg.Support + " support point slides in the world at phase " + phase);
                double flat = point - (leg.Support == FootSupport.Heel ? geo.HeelZ : leg.Support == FootSupport.Toe ? geo.ToeZ : 0);
                if (double.IsNaN(flatAnkle)) flatAnkle = flat;
                Assert.That(flat, Is.EqualTo(flatAnkle).Within(3e-3), "the foot is in one place for the whole stance (heel, flat and toe phases agree)");
            }
        }

        [Test]
        public void EveryJoint_StaysInsideItsRange_UpToFroudeTenth([ValueSource(nameof(ValidFroudes))] double fr) => JointRanges(fr, 0);

        /// <summary>Not met by this reference: ankle dorsiflexion has 0.4 to 2.2 degrees to spare up to Froude 0.10 and exceeds the range by 4.5 degrees at 0.20 and 10 degrees at 0.30 (values in the test output). Kept red on purpose.</summary>
        [Test, Category("KnownGap")]
        public void EveryJoint_HasFiveDegreesToSpare([ValueSource(nameof(Froudes))] double fr) => JointRanges(fr, 5);

        void JointRanges(double fr, double margin)
        {
            var gait = Make(fr, out var geo, out _);
            const double deg = 180 / Math.PI;
            double hipMin = 1e9, hipMax = -1e9, kneeMax = -1e9, kneeMin = 1e9, ankleMin = 1e9, ankleMax = -1e9;
            for (int i = 0; i < N; i++)
            {
                var s = gait.Sample((double)i / N);
                foreach (var leg in new[] { s.L, s.R })
                {
                    Assert.That(leg.Reachable, Is.True, "leg cannot reach its foot at phase " + s.Phase);
                    hipMin = Math.Min(hipMin, leg.HipFlexion * deg); hipMax = Math.Max(hipMax, leg.HipFlexion * deg);
                    kneeMin = Math.Min(kneeMin, leg.Knee * deg); kneeMax = Math.Max(kneeMax, leg.Knee * deg);
                    ankleMin = Math.Min(ankleMin, leg.AnklePlantar * deg); ankleMax = Math.Max(ankleMax, leg.AnklePlantar * deg);
                }
            }
            TestContext.WriteLine($"Fr {fr}: step {gait.StepLength:F1} m, hip flexion [{hipMin:F1}, {hipMax:F1}], knee [{kneeMin:F1}, {kneeMax:F1}], ankle plantarflexion [{ankleMin:F1}, {ankleMax:F1}] deg");
            Assert.That(hipMin, Is.GreaterThanOrEqualTo(geo.HipFlexRom[0] + margin)); Assert.That(hipMax, Is.LessThanOrEqualTo(geo.HipFlexRom[1] - margin));
            Assert.That(kneeMin, Is.GreaterThanOrEqualTo(geo.KneeRom[0])); Assert.That(kneeMax, Is.LessThanOrEqualTo(geo.KneeRom[1] - margin));
            Assert.That(ankleMin, Is.GreaterThanOrEqualTo(geo.AnkleRom[0] + margin), "dorsiflexion"); Assert.That(ankleMax, Is.LessThanOrEqualTo(geo.AnkleRom[1] - margin), "plantarflexion");
        }

        [Test]
        public void PositionsAndVelocities_AreContinuous_AcrossEveryPhaseBoundary([ValueSource(nameof(Froudes))] double fr)
        {
            var gait = Make(fr, out _, out _);
            double T = 2 * gait.StepTime, dt = T / N;
            double[] prev = null, prevVel = null; double maxJump = 0, maxAccel = 0; string where = "";
            for (int i = 0; i <= N + 2; i++)
            {
                var s = gait.Sample((double)i / N);
                double[] q = { s.HipHeight, s.L.Ankle.Y, s.L.Ankle.Z, s.L.HipFlexion * 10, s.L.Knee * 10, s.L.AnklePlantar * 10, s.L.FootPitch * 10, s.PelvisYaw * 10, s.PelvisSway };
                if (prev != null)
                {
                    var vel = new double[q.Length];
                    for (int k = 0; k < q.Length; k++)
                    {
                        double d = q[k] - prev[k];
                        if (k == 2 && d > gait.StepLength) d -= 2 * gait.StepLength;   // not a jump: the stride frame wraps
                        vel[k] = d / dt; maxJump = Math.Max(maxJump, Math.Abs(d));
                        if (prevVel != null && Math.Abs(vel[k] - prevVel[k]) / dt > maxAccel) { maxAccel = Math.Abs(vel[k] - prevVel[k]) / dt; where = "coordinate " + k + " at phase " + ((double)i / N).ToString("F4"); }
                    }
                    prevVel = vel;
                }
                prev = q;
            }
            TestContext.WriteLine($"Fr {fr}: largest sample-to-sample change {maxJump:F4} (m or 0.1 rad) at dt {dt * 1000:F2} ms, largest second difference {maxAccel:F1} ({where})");
            Assert.That(maxJump, Is.LessThan(0.12), "position continuity (no step in any coordinate)");
            Assert.That(maxAccel, Is.LessThan(450), "velocity continuity (no jump in any rate)");
        }

        /// <summary>Design aid, not a check: scans reference parameters for the smallest joint-range violation at Froude 0.20. Run with --filter Scan.</summary>
        [Test, Category("DesignAid")]
        public void ScanParameters()
        {
            var t = Repo.Walk(); var geo = GaitGeometry.From(Repo.Load<MechFrameData>("Mech/mech_frame_balanced.json")); const double deg = 180 / Math.PI;
            double best = 1e9; string bestText = "";
            foreach (double bias in new[] { -0.12, -0.05, 0.0, 0.05, 0.10 })
            foreach (double pre in new[] { 20.0, 30.0, 40.0 })
            foreach (double rise in new[] { 0.45, 0.55, 0.70 })
            foreach (double toe in new[] { 20.0, 30.0 })
            foreach (double yaw in new[] { 6.0, 10.0 })
            foreach (double duty in new[] { 0.60, 0.63 })
            {
                t.ref_front_bias_frac = bias; t.ref_preswing_knee_deg = pre; t.ref_heel_rise_start = rise; t.ref_toe_off_deg = toe; t.ref_pelvis_yaw_deg = yaw; t.duty_factor = duty;
                double worst = 0;
                foreach (double fr in new[] { 0.10, 0.20, 0.30 })
                {
                    var gait = new ReferenceGait(geo, t, G); gait.Configure(Math.Sqrt(fr * G * geo.HipHeight), 0);
                    for (int i = 0; i < 400; i++)
                    {
                        var s = gait.Sample(i / 400.0);
                        foreach (var leg in new[] { s.L, s.R })
                        {
                            worst = Math.Max(worst, geo.HipFlexRom[0] + 5 - leg.HipFlexion * deg); worst = Math.Max(worst, geo.AnkleRom[0] + 5 - leg.AnklePlantar * deg);
                            worst = Math.Max(worst, leg.AnklePlantar * deg - (geo.AnkleRom[1] - 5)); worst = Math.Max(worst, leg.Knee * deg - (geo.KneeRom[1] - 5)); if (!leg.Reachable) worst = Math.Max(worst, 50);
                        }
                    }
                }
                if (worst < best) { best = worst; bestText = $"bias {bias} preswing {pre} rise {rise} toe {toe} yaw {yaw} duty {duty}: worst violation {worst:F1} deg"; }
            }
            TestContext.WriteLine("SCAN " + bestText);
        }

        [Test]
        public void Touchdown_IsControlled_AndTheFootMeetsTheGroundAtRest([ValueSource(nameof(Froudes))] double fr)
        {
            var gait = Make(fr, out _, out _);
            double T = 2 * gait.StepTime, h = 1e-4, worstVy = 0;
            for (double before = 0.5; before > 0.004; before -= 0.01)
            {
                double phase = 1 - before / T;   // the left foot lands at phase 1 (= 0)
                var a = gait.Sample(phase - h); var b = gait.Sample(phase + h);
                worstVy = Math.Max(worstVy, -(b.L.Ankle.Y - a.L.Ankle.Y) / (2 * h * T));
            }
            var e0 = gait.Sample(1 - 2e-4); var e1 = gait.Sample(1 - 1e-4);
            double vzWorld = (e1.L.Ankle.Z - e0.L.Ankle.Z) / (1e-4 * T) + gait.Speed;
            TestContext.WriteLine($"Fr {fr}: fastest descent in the last 0.5 s {worstVy:F2} m/s, forward speed of the foot in the world at touchdown {vzWorld:F3} m/s");
            Assert.That(worstVy, Is.LessThanOrEqualTo(1.0), "R-005 / R-007: foot vertical speed <= 1 m/s over the last 0.5 s");
            Assert.That(Math.Abs(vzWorld), Is.LessThan(0.05), "swing-leg retraction: the foot is at rest in the world when it lands");
        }

        [Test]
        public void YawMomentAskedOfTheStanceFoot_StaysInsideTheFrictionBudget_UpToFroudeTenth([ValueSource(nameof(ValidFroudes))] double fr) => YawBudgetCheck(fr);

        /// <summary>Not met by this reference above Froude 0.10 (3.5e8 N m at 0.20 against 1.65e8): the swing still starts at toe-off; R-007 change 3 (swing started in pre-swing) is what halves it. Kept red on purpose.</summary>
        [Test, Category("KnownGap")]
        public void YawMomentAskedOfTheStanceFoot_StaysInsideTheFrictionBudget_AtCruiseAndAbove([Values(0.20, 0.30)] double fr) => YawBudgetCheck(fr);

        void YawBudgetCheck(double fr)
        {
            var gait = Make(fr, out _, out _);
            double worst = 0;
            for (int i = 0; i < 400; i++) worst = Math.Max(worst, Math.Abs(gait.YawMoment((double)i / 400)));
            TestContext.WriteLine($"Fr {fr}: peak yaw moment of the reference {worst:E2} N m against the budget {YawBudget:E2}");
            Assert.That(worst, Is.LessThanOrEqualTo(YawBudget));
        }
    }
}
