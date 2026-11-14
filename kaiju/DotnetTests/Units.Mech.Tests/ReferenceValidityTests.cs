using System;
using Game.Physics.Body.Contracts;
using Game.Units.Mech;
using Game.Units.Mech.Contracts;
using NUnit.Framework;

namespace Game.Units.Mech.Tests
{
    /// <summary>
    /// M-NEW-c (research/R-008): reference-gait validity, for any gait generator, over the WHOLE cycle sampled finely, on the round-2 (imp = 1) reference:
    /// joint margin >= 5 deg, hip excursion <= 0.05 l, single-support LIPM acceleration <= 0.1 g with the CoP in the active support (heel point, sole or toe line),
    /// foot flat for >= 75 % of single support, free-moment utilisation <= 0.67 against the friction budget (after the declared trunk share, i.e. the waist the reference carries).
    /// Asserted at Froude 0.10 and 0.15; 0.20 is reported only. Formula reference: research/R-008/model.py and yaw.py.
    /// </summary>
    public class ReferenceValidityTests
    {
        const double G = 9.81, MarginDeg = 5, ExcursionFrac = 0.05, LipmG = 0.1, FlatShare = 0.75, YawUtil = 0.67, KneeMinDeg = 5, ComAboveHip = 1.5;
        const int N = 4800, NY = 480;   // N samples for the kinematic checks, NY for the yaw profile (every tenth sample)
        const double Deg = 180 / Math.PI;

        sealed class Result
        {
            public double Fr, Speed, Step, Margin, MarginAt, HipExcursion, HipExcursionLimit, LipmAcc, LipmLimit, FlatFraction, YawPeak, YawBudgetAtPeak, YawUtilisation, KneeMin, LimbsPeak, TrunkShare, WaistPeakDeg, WaistTorque;
            public string MarginJoint, MarginWhere, LipmWhere, YawWhere;
        }

        static ReferenceGait Make(double fr, out GaitGeometry geo, out WalkTuning t)
        {
            t = Repo.Walk(); geo = GaitGeometry.From(Repo.Load<MechFrameData>("Mech/mech_frame_balanced.json"));
            t.imp = 1;   // the round-2 corrected reference (stance posture of R-008 section 1); the controller still ships with it off
            var gait = new ReferenceGait(geo, t, G);
            gait.Configure(Math.Sqrt(fr * G * geo.HipHeight), 0);
            return gait;
        }

        static void Support(in LegSample leg, GaitGeometry geo, out double lo, out double hi)
        {
            double c = Math.Cos(leg.FootPitch), s = Math.Sin(leg.FootPitch);
            double heel = leg.Ankle.Z + (geo.HeelZ * c - geo.AnkleHeight * s), toe = leg.Ankle.Z + (geo.ToeZ * c - geo.AnkleHeight * s);
            if (leg.Support == FootSupport.Heel) { lo = hi = heel; }
            else if (leg.Support == FootSupport.Toe) { lo = hi = toe; }
            else { lo = heel; hi = toe; }   // flat: the whole sole
        }

        static Result Evaluate(double fr)
        {
            var gait = Make(fr, out var geo, out var t);
            var r = new Result { Fr = fr, Speed = gait.Speed, Step = gait.StepLength, Margin = 1e9, LipmLimit = LipmG * G, HipExcursionLimit = ExcursionFrac * geo.HipHeight, KneeMin = 1e9 };
            double hipMin = 1e9, hipMax = -1e9; int ssN = 0, ssFlat = 0; double W = geo.TotalMass * G, l = geo.HipHeight;
            double budgetFlat = t.yaw_budget_flat_wl * W * l, budgetEdge = t.yaw_budget_edge_wl * W * l;
            var Mz = gait.YawMomentProfile(NY, 8);
            for (int i = 0; i < N; i++)
            {
                double phase = (double)i / N; var s = gait.Sample(phase);
                hipMin = Math.Min(hipMin, s.HipHeight); hipMax = Math.Max(hipMax, s.HipHeight);
                foreach (var pair in new[] { (s.L, "L"), (s.R, "R") })
                {
                    var leg = pair.Item1;
                    if (!leg.Reachable) { r.Margin = -99; r.MarginJoint = "reach"; r.MarginWhere = phase.ToString("F3"); }
                    void Check(string joint, double lo, double hi, double v) { double m = Math.Min(v - lo, hi - v); if (m < r.Margin) { r.Margin = m; r.MarginJoint = joint; r.MarginWhere = pair.Item2 + "@" + phase.ToString("F3"); } }
                    Check("hip", geo.HipFlexRom[0], geo.HipFlexRom[1], leg.HipFlexion * Deg);
                    Check("ankle", geo.AnkleRom[0], geo.AnkleRom[1], leg.AnklePlantar * Deg);
                    Check("knee", geo.KneeRom[0], geo.KneeRom[1], leg.Knee * Deg);
                    if (leg.Support != FootSupport.Swing) r.KneeMin = Math.Min(r.KneeMin, leg.Knee * Deg);
                }
                bool single = (s.L.Support == FootSupport.Swing) != (s.R.Support == FootSupport.Swing);
                if (!single) continue;
                var stance = s.L.Support != FootSupport.Swing ? s.L : s.R;
                ssN++; if (stance.Support == FootSupport.Flat) ssFlat++;
                Support(stance, geo, out double lo2, out double hi2);
                double dist = 0 < lo2 ? lo2 : 0 > hi2 ? -hi2 : 0;   // the CoM (pelvis proxy) is at z = 0 in the stride frame
                double acc = G / (s.HipHeight + ComAboveHip) * Math.Abs(dist);
                if (acc > r.LipmAcc) { r.LipmAcc = acc; r.LipmWhere = phase.ToString("F3") + " " + stance.Support; }
                double budget = stance.Support == FootSupport.Flat ? budgetFlat : budgetEdge, mz = Math.Abs(Mz[(int)Math.Round(phase * NY) % NY]);
                if (mz / budget > r.YawUtilisation) { r.YawUtilisation = mz / budget; r.YawPeak = mz; r.YawBudgetAtPeak = budget; r.YawWhere = phase.ToString("F3") + " " + stance.Support; }
            }
            r.HipExcursion = hipMax - hipMin; r.FlatFraction = ssN > 0 ? (double)ssFlat / ssN : 0;
            // declared trunk share: how much of the limbs' yaw momentum the waist (and the pelvis cancel) takes out, from the single-support peak moments with and without them
            var noWaist = Repo.Walk(); noWaist.imp = 1; noWaist.ref_waist_ff = 0; var g0 = new ReferenceGait(geo, noWaist, G); g0.Configure(gait.Speed, 0);
            var M0 = g0.YawMomentProfile(NY, 8); double peakWith = 0, peakLimbs = 0;
            for (int i = 0; i < NY; i++)
            {
                var s = gait.Sample((double)i / NY); if ((s.L.Support == FootSupport.Swing) == (s.R.Support == FootSupport.Swing)) continue;
                peakWith = Math.Max(peakWith, Math.Abs(Mz[i])); peakLimbs = Math.Max(peakLimbs, Math.Abs(M0[i]));
            }
            r.LimbsPeak = peakLimbs; r.TrunkShare = peakLimbs > 0 ? 1 - peakWith / peakLimbs : 0;
            double wp = 0, wt = 0, T = 2 * gait.StepTime, hh = 1e-3;
            for (int i = 0; i < 960; i++)
            {
                double ph = (double)i / 960; wp = Math.Max(wp, Math.Abs(gait.Sample(ph).WaistYaw) * Deg);
                wt = Math.Max(wt, Math.Abs(geo.UpperYawInertia * (gait.Sample(ph + hh).WaistYaw - 2 * gait.Sample(ph).WaistYaw + gait.Sample(ph - hh).WaistYaw) / (hh * hh * T * T)));
            }
            r.WaistPeakDeg = wp; r.WaistTorque = wt;
            return r;
        }

        static string Row(Result r) =>
            $"Fr {r.Fr:F3}: v {r.Speed:F2} m/s, step {r.Step:F1} m | joint margin {r.Margin:F2} deg ({r.MarginJoint} {r.MarginWhere}) (>= {MarginDeg}) | hip excursion {r.HipExcursion:F2} m (<= {r.HipExcursionLimit:F2}) | " +
            $"LIPM acc {r.LipmAcc:F3} m/s2 = {r.LipmAcc / G:F3} g at {r.LipmWhere} (<= {LipmG}) | foot flat {r.FlatFraction * 100:F1} % of single support (>= {FlatShare * 100:F0}) | " +
            $"yaw utilisation {r.YawUtilisation:F3} ({r.YawPeak:E2} of {r.YawBudgetAtPeak:E2} at {r.YawWhere}) (<= {YawUtil}) | trunk share {r.TrunkShare:F2}, waist peak {r.WaistPeakDeg:F1} deg, torque {r.WaistTorque:E2} N m | min stance knee {r.KneeMin:F1} deg (>= {KneeMinDeg})";

        static readonly double[] Asserted = { 0.10, 0.15 };

        [Test] public void JointMargin_IsAtLeastFiveDegrees_OverTheWholeCycle([ValueSource(nameof(Asserted))] double fr) { var r = Evaluate(fr); TestContext.WriteLine(Row(r)); Assert.That(r.Margin, Is.GreaterThanOrEqualTo(MarginDeg), r.MarginJoint + " at " + r.MarginWhere); }
        [Test] public void StanceKnee_IsNeverLocked([ValueSource(nameof(Asserted))] double fr) { var r = Evaluate(fr); Assert.That(r.KneeMin, Is.GreaterThanOrEqualTo(KneeMinDeg)); }
        [Test] public void HipExcursion_IsAtMostFiveHundredthsOfLegLength([ValueSource(nameof(Asserted))] double fr) { var r = Evaluate(fr); Assert.That(r.HipExcursion, Is.LessThanOrEqualTo(r.HipExcursionLimit)); }
        [Test] public void SingleSupportLipmAcceleration_IsAtMostATenthG_WithTheCopInTheActiveSupport([ValueSource(nameof(Asserted))] double fr) { var r = Evaluate(fr); Assert.That(r.LipmAcc, Is.LessThanOrEqualTo(r.LipmLimit), r.LipmWhere); }
        [Test] public void FootIsFlat_ForAtLeastThreeQuartersOfSingleSupport([ValueSource(nameof(Asserted))] double fr) { var r = Evaluate(fr); Assert.That(r.FlatFraction, Is.GreaterThanOrEqualTo(FlatShare)); }
        [Test] public void FreeMomentUtilisation_IsAtMostTwoThirds_AfterTheDeclaredTrunkShare([ValueSource(nameof(Asserted))] double fr) { var r = Evaluate(fr); Assert.That(r.YawUtilisation, Is.LessThanOrEqualTo(YawUtil), r.YawWhere); }

        /// <summary>Froude 0.20 (the registered cruise): reported, not asserted (R-008 expects this approach alone not to reach it).</summary>
        [Test] public void ReportAllChecks_AtEveryFroude()
        {
            foreach (double fr in new[] { 0.05, 0.075, 0.10, 0.125, 0.15, 0.175, 0.20 }) TestContext.WriteLine(Row(Evaluate(fr)));
            Assert.Pass();
        }
    }
}
