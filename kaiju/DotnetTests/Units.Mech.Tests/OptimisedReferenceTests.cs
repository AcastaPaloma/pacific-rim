using System;
using Game.Physics.Body.Contracts;
using Game.Units.Mech;
using Game.Units.Mech.Contracts;
using NUnit.Framework;

namespace Game.Units.Mech.Tests
{
    /// <summary>Round 3 step 8.4a: the optimised stride as data (gait_reference_opt@v1) and ReferenceGait at imp = 2. Engine-free.</summary>
    public class OptimisedReferenceTests
    {
        const double G = 9.81, Rad = Math.PI / 180;
        static readonly double[] Frs = { 0.10, 0.15, 0.20 };

        static GaitReferenceData Data() { var d = Repo.Load<GaitReferenceData>("Mech/gait_reference_opt.json"); d.Validate(); return d; }
        static GaitGeometry Geo(double hipHeight = 0)
        {
            var geo = GaitGeometry.From(Repo.Load<MechFrameData>("Mech/mech_frame_balanced.json"));
            if (hipHeight > 0) geo.HipHeight = hipHeight;
            return geo;
        }
        static ReferenceGait Gait(double imp, double fr, GaitGeometry geo, GaitReferenceData data)
        {
            var t = Repo.Walk(); t.imp = imp;
            var g = new ReferenceGait(geo, t, G, data);
            g.Configure(Math.Sqrt(fr * G * geo.HipHeight), 0);
            return g;
        }

        [Test]
        public void File_LoadsAndDescribesItself()
        {
            var d = Data();
            Assert.That(d.rows.Length, Is.EqualTo(3));
            for (int i = 0; i < 3; i++) { Assert.That(d.rows[i].fr, Is.EqualTo(Frs[i]).Within(1e-12)); Assert.That(d.rows[i].nodes.Length, Is.EqualTo(40)); Assert.That(d.rows[i].verdict, Is.EqualTo("FEASIBLE")); }
            Assert.That(d.source.solver_commit, Does.StartWith("7d79db8"));
            Assert.That(d.conventions.mapping_to_reference_gait, Does.Contain("NOT verified"));
            Assert.That(d.conventions.angles, Is.Not.Empty);
        }

        [Test]
        public void Rows_AreSortedByFroude_AndPhasesAreUniform()
        {
            var d = Data();
            for (int i = 1; i < d.rows.Length; i++) Assert.That(d.rows[i].fr, Is.GreaterThan(d.rows[i - 1].fr));
            foreach (var r in d.rows)
                for (int k = 0; k < r.nodes.Length; k++) Assert.That(r.nodes[k].phase, Is.EqualTo((double)k / r.nodes.Length).Within(1e-12));
        }

        [Test]
        public void Rows_ArePeriodicMirrored_NodeNIsNodeZeroWithLegsSwapped()
        {
            // the step after the last node is node 0 with stance and swing swapped: it must follow the last node as smoothly as consecutive nodes follow each other
            foreach (var r in Data().rows)
            {
                int n = r.nodes.Length; var a = r.nodes[0]; var z = r.nodes[n - 1];
                void Check(string what, Func<GaitRefNode, double> f, double mirrored)
                {
                    double maxStep = 0; for (int k = 0; k + 1 < n; k++) maxStep = Math.Max(maxStep, Math.Abs(f(r.nodes[k + 1]) - f(r.nodes[k])));
                    Assert.That(Math.Abs(mirrored - f(z)), Is.LessThanOrEqualTo(2 * maxStep + 1e-9), what + " at the wrap, Fr " + r.fr);
                }
                Check("stance hip", x => x.stance.hip_deg, a.swing.hip_deg); Check("stance knee", x => x.stance.knee_deg, a.swing.knee_deg); Check("stance ankle", x => x.stance.ankle_deg, a.swing.ankle_deg);
                Check("swing hip", x => x.swing.hip_deg, a.stance.hip_deg); Check("swing knee", x => x.swing.knee_deg, a.stance.knee_deg); Check("swing ankle", x => x.swing.ankle_deg, a.stance.ankle_deg);
                Check("pelvis z", x => x.pelvis_z_m, a.pelvis_z_m); Check("pitch", x => x.pelvis_pitch_deg, a.pelvis_pitch_deg); Check("pelvis x (moving frame)", x => x.pelvis_x_rel_m, a.pelvis_x_rel_m);
                Check("stance ankle x", x => x.stance.ankle_x_m, a.swing.ankle_x_m); Check("swing ankle x", x => x.swing.ankle_x_m, a.stance.ankle_x_m);
                // the step is v T
                Assert.That(r.v * r.step_time_s, Is.EqualTo(r.step_length_m).Within(1e-6 * r.step_length_m));
            }
        }

        [Test]
        public void Rows_RatesAreTheDerivativeOfTheAngles()
        {
            // the solver's rates are central differences of its coordinates over one node spacing, so the joint rates are those of the joint angles
            foreach (var r in Data().rows)
            {
                double h = r.step_time_s / r.nodes.Length;
                for (int k = 1; k + 1 < r.nodes.Length; k++)
                {
                    var p = r.nodes[k - 1]; var q = r.nodes[k]; var s = r.nodes[k + 1];
                    Assert.That(q.stance.hip_dps, Is.EqualTo((s.stance.hip_deg - p.stance.hip_deg) / (2 * h)).Within(1e-5 + 1e-6 * Math.Abs(q.stance.hip_dps)));
                    Assert.That(q.swing.knee_dps, Is.EqualTo((s.swing.knee_deg - p.swing.knee_deg) / (2 * h)).Within(1e-5 + 1e-6 * Math.Abs(q.swing.knee_dps)));
                    Assert.That(q.stance.ankle_dps, Is.EqualTo((s.stance.ankle_deg - p.stance.ankle_deg) / (2 * h)).Within(1e-5 + 1e-6 * Math.Abs(q.stance.ankle_dps)));
                }
            }
        }

        [Test]
        public void Rows_VerticalForceAveragesBodyWeight()
        {
            var geo = Geo();
            foreach (var r in Data().rows)
            {
                double sum = 0; foreach (var nd in r.nodes) sum += nd.stance.heel_fn_n + nd.stance.toe_fn_n + nd.swing.heel_fn_n + nd.swing.toe_fn_n;
                Assert.That(sum / r.nodes.Length, Is.EqualTo(geo.TotalMass * G).Within(0.02 * geo.TotalMass * G), "Fr " + r.fr);
            }
        }

        [Test]
        public void Interpolation_AtTheRowFroudeNumbers_ReproducesTheRowsExactly()
        {
            var d = Data();
            foreach (var r in d.rows)
            {
                var geo = Geo(r.leg_length_m); var gait = Gait(2, r.fr, geo, d);
                Assert.That(gait.IsOptimised);
                Assert.That(gait.StepTime, Is.EqualTo(r.step_time_s).Within(1e-9 * r.step_time_s)); Assert.That(gait.StepLength, Is.EqualTo(r.step_length_m).Within(1e-9 * r.step_length_m));
                Assert.That(gait.Speed, Is.EqualTo(r.v).Within(1e-9 * r.v));
                int n = r.nodes.Length;
                for (int k = 0; k < n; k++)
                {
                    var nd = r.nodes[k];
                    foreach (int half in new[] { 0, 1 })
                    {
                        double phase = 0.5 * half + (double)k / (2 * n);
                        var s = gait.Sample(phase); var st = half == 0 ? s.L : s.R; var sw = half == 0 ? s.R : s.L;
                        string at = "Fr " + r.fr + " node " + k + " half " + half;
                        Assert.That(st.HipFlexion, Is.EqualTo(nd.stance.hip_deg * Rad).Within(1e-9), at); Assert.That(st.Knee, Is.EqualTo(nd.stance.knee_deg * Rad).Within(1e-9), at); Assert.That(st.AnklePlantar, Is.EqualTo(nd.stance.ankle_deg * Rad).Within(1e-9), at);
                        Assert.That(sw.HipFlexion, Is.EqualTo(nd.swing.hip_deg * Rad).Within(1e-9), at); Assert.That(sw.Knee, Is.EqualTo(nd.swing.knee_deg * Rad).Within(1e-9), at); Assert.That(sw.AnklePlantar, Is.EqualTo(nd.swing.ankle_deg * Rad).Within(1e-9), at);
                        Assert.That(s.HipHeight, Is.EqualTo(nd.pelvis_z_m).Within(1e-9), at); Assert.That(s.PelvisTilt, Is.EqualTo(nd.pelvis_pitch_deg * Rad).Within(1e-9), at);
                        Assert.That(gait.TryOptimised(phase, out var o)); var os = half == 0 ? o.L : o.R; var ow = half == 0 ? o.R : o.L;
                        Assert.That(os.IsStance); Assert.That(!ow.IsStance);
                        Assert.That(os.HipTorque, Is.EqualTo(nd.stance.hip_tau_nm).Within(1e-6 + 1e-9 * Math.Abs(nd.stance.hip_tau_nm)), at); Assert.That(ow.AnkleTorque, Is.EqualTo(nd.swing.ankle_tau_nm).Within(1e-6 + 1e-9 * Math.Abs(nd.swing.ankle_tau_nm)), at);
                        Assert.That(os.HeelNormal, Is.EqualTo(nd.stance.heel_fn_n).Within(1e-6 + 1e-9 * Math.Abs(nd.stance.heel_fn_n)), at); Assert.That(os.ToeTangential, Is.EqualTo(nd.stance.toe_ft_n).Within(1e-6 + 1e-9 * Math.Abs(nd.stance.toe_ft_n)), at);
                        Assert.That(os.KneeRate, Is.EqualTo(nd.stance.knee_dps * Rad).Within(1e-9), at);
                        Assert.That(os.HeelContact, Is.EqualTo(nd.stance.heel_contact), at); Assert.That(os.ToeContact, Is.EqualTo(nd.stance.toe_contact), at); Assert.That(ow.ToeContact, Is.EqualTo(nd.swing.toe_contact), at);
                    }
                }
            }
        }

        [Test]
        public void Interpolation_IsLinearInFroude_AndClampedOutsideTheBand()
        {
            var d = Data(); var geo = Geo(d.rows[0].leg_length_m);
            double Hip(double fr) => Gait(2, fr, geo, d).Sample(0.1).L.HipFlexion;
            double lo = Hip(0.10), mid = Hip(0.125), hi = Hip(0.15);
            Assert.That(mid, Is.EqualTo(0.5 * (lo + hi)).Within(1e-9)); Assert.That(lo, Is.Not.EqualTo(hi).Within(1e-6));
            Assert.That(Hip(0.05), Is.EqualTo(lo).Within(1e-12)); Assert.That(Hip(0.30), Is.EqualTo(Hip(0.20)).Within(1e-12));
            var g = Gait(2, 0.175, geo, d);
            Assert.That(g.StepTime, Is.EqualTo(0.5 * (d.rows[1].step_time_s + d.rows[2].step_time_s)).Within(1e-9)); Assert.That(g.StepLength, Is.EqualTo(0.5 * (d.rows[1].step_length_m + d.rows[2].step_length_m)).Within(1e-9));
            Assert.That(Gait(2, 0.30, geo, d).StepTime, Is.EqualTo(d.rows[2].step_time_s).Within(1e-9));
        }

        [Test]
        public void Interpolation_IsLinearInPhase_AndWrapsToTheMirroredNode()
        {
            var d = Data(); var r = d.rows[1]; var geo = Geo(r.leg_length_m); var gait = Gait(2, r.fr, geo, d); int n = r.nodes.Length;
            double a = gait.Sample(3.0 / (2 * n)).L.Knee, b = gait.Sample(4.0 / (2 * n)).L.Knee;
            Assert.That(gait.Sample(3.5 / (2 * n)).L.Knee, Is.EqualTo(0.5 * (a + b)).Within(1e-12));
            // after the last node of the first step comes node 0 of the second: left is then the swing leg, so it carries node 0's swing values
            double last = gait.Sample((n - 1.0) / (2 * n)).L.Knee, wrapped = gait.Sample(0.5).L.Knee;
            Assert.That(wrapped, Is.EqualTo(r.nodes[0].swing.knee_deg * Rad).Within(1e-9));
            Assert.That(gait.Sample((n - 0.5) / (2 * n)).L.Knee, Is.EqualTo(0.5 * (last + wrapped)).Within(1e-12));
            Assert.That(gait.Sample(1.25).L.Knee, Is.EqualTo(gait.Sample(0.25).L.Knee).Within(1e-12));
        }

        [Test]
        public void Imp0AndImp1_DoNotLookAtTheData_AndImp2LeavesNoSharedState()
        {
            var d = Data(); var geo = Geo(); double fr = 0.10;
            double[] Probe(double imp, GaitReferenceData data)
            {
                var g = Gait(imp, fr, geo, data); var s = g.Sample(0.23);
                return new[] { g.StepTime, g.StepLength, g.Duty, g.FrontReach, g.Tilt, g.HipTarget, s.HipHeight, s.PelvisTilt, s.PelvisYaw, s.WaistYaw, s.ArmFlexionL, s.L.HipFlexion, s.L.Knee, s.L.AnklePlantar, s.R.HipFlexion, s.R.Knee, s.R.AnklePlantar, s.L.Ankle.Z, s.R.Ankle.Z };
            }
            var imp0 = Probe(0, null); var imp1 = Probe(1, null);
            Assert.That(Gait(0, fr, geo, d).IsOptimised, Is.False); Assert.That(Gait(1, fr, geo, d).IsOptimised, Is.False);
            Assert.That(Probe(0, d), Is.EqualTo(imp0)); Assert.That(Probe(1, d), Is.EqualTo(imp1));
            Assert.That(Gait(1, fr, geo, d).TryOptimised(0.2, out _), Is.False);
            // imp = 2 without data is the imp = 1 pattern
            Assert.That(Probe(2, null), Is.EqualTo(imp1));
            // using imp = 2 (also at other speeds, and mutating nothing it was given) changes nothing for imp 0 and 1 afterwards
            var tuning = Repo.Walk(); string before = System.Text.Json.JsonSerializer.Serialize(tuning, new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
            tuning.imp = 2; var opt = new ReferenceGait(geo, tuning, G, d);
            foreach (double f in new[] { 0.05, 0.1, 0.13, 0.2, 0.3 }) { opt.Configure(Math.Sqrt(f * G * geo.HipHeight), 0); for (int k = 0; k < 50; k++) opt.Sample(k / 50.0); }
            opt.Configure(0, 0); Assert.That(opt.IsOptimised, Is.False);
            tuning.imp = 0; Assert.That(System.Text.Json.JsonSerializer.Serialize(tuning, new System.Text.Json.JsonSerializerOptions { IncludeFields = true }), Is.EqualTo(before));
            Assert.That(Probe(0, null), Is.EqualTo(imp0)); Assert.That(Probe(1, null), Is.EqualTo(imp1));
        }

        [Test]
        public void Scaling_FollowsFroudeSimilarity_ForOtherLegLengths()
        {
            var d = Data(); var r = d.rows[1]; var full = Gait(2, r.fr, Geo(r.leg_length_m), d); var half = Gait(2, r.fr, Geo(0.5 * r.leg_length_m), d);
            Assert.That(half.StepLength, Is.EqualTo(0.5 * full.StepLength).Within(1e-9)); Assert.That(half.StepTime, Is.EqualTo(full.StepTime * Math.Sqrt(0.5)).Within(1e-9));
            Assert.That(half.Sample(0.1).L.Knee, Is.EqualTo(full.Sample(0.1).L.Knee).Within(1e-12));
        }
    }
}
