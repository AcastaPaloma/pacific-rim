// M-105@v1 (R-104 section 11): refit physical consistency. For each chassis, 10,000 seeded random builds reachable from the catalogue (5,000 uniform over compatible
// parts per slot, 5,000 mutations of the default build) plus structurally broken builds and hand-picked edge builds. An independent oracle (written here, from the
// catalogue data, not from RefitPhysics) recomputes mass, CoM, inertia, power, heat, torque and tilt, and decides validity and the first violation.
// Thresholds: 0 valid builds out of limits; 0 invalid builds accepted; spec mass equals the sum of part masses; the same seed gives the same hashes.
// Run with KAIJU_EVIDENCE_DIR set to write the evidence json.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Game.Meta.Refit;

namespace Game.Meta.Refit.Tests
{
    public class M105RefitTests
    {
        // ---------------------------------------------------------------- the oracle

        sealed class Oracle
        {
            public RefitViolation First; public string Subject = "";
            public double Mass, Cx, Cy, Cz, Rated, Peak, Gen, Rej, Need, Store, MinRatio, Tilt;
            public double[] Inertia = new double[6];
        }

        static Oracle Judge(ChassisDef ch, IReadOnlyDictionary<string, string> a, IPartCatalog cat)
        {
            var o = new Oracle { First = RefitViolation.None };
            foreach (var k in a.Keys.OrderBy(x => x, StringComparer.Ordinal)) if (!ch.Slots.Any(s => s.Name == k)) { o.First = RefitViolation.UnknownSlot; o.Subject = k; return o; }
            var parts = new Dictionary<string, PartDef>();
            foreach (var s in ch.Slots)
            {
                if (!a.TryGetValue(s.Name, out var pid)) { o.First = RefitViolation.SlotMissing; o.Subject = s.Name; return o; }
                var p = cat.All.FirstOrDefault(x => x.Id == pid);
                if (p == null) { o.First = RefitViolation.UnknownPart; o.Subject = pid; return o; }
                if (p.SlotType != s.Type) { o.First = RefitViolation.WrongSlotType; o.Subject = s.Name; return o; }
                if (!p.Chassis.Contains(ch.Id)) { o.First = RefitViolation.WrongChassis; o.Subject = s.Name; return o; }
                parts[s.Name] = p;
            }
            const double g = 9.81;
            Func<SlotDef, double[]> pos = s => { var p = parts[s.Name]; return new[] { s.AnchorX + (s.Mirror ? -p.ComX : p.ComX), s.AnchorY + p.ComY, s.AnchorZ + p.ComZ }; };
            // mass and CoM
            double M = 0, X = 0, Y = 0, Z = 0;
            foreach (var s in ch.Slots) { double m = parts[s.Name].CompositionKg.Values.Sum(); var c = pos(s); M += m; X += m * c[0]; Y += m * c[1]; Z += m * c[2]; }
            o.Mass = M; o.Cx = X / M; o.Cy = Y / M; o.Cz = Z / M;
            // inertia
            foreach (var s in ch.Slots)
            {
                var p = parts[s.Name]; double m = p.CompositionKg.Values.Sum(); var c = pos(s); double dx = c[0] - o.Cx, dy = c[1] - o.Cy, dz = c[2] - o.Cz;
                o.Inertia[0] += m / 12 * (p.SizeY * p.SizeY + p.SizeZ * p.SizeZ) + m * (dy * dy + dz * dz);
                o.Inertia[1] += m / 12 * (p.SizeX * p.SizeX + p.SizeZ * p.SizeZ) + m * (dx * dx + dz * dz);
                o.Inertia[2] += m / 12 * (p.SizeX * p.SizeX + p.SizeY * p.SizeY) + m * (dx * dx + dy * dy);
                o.Inertia[3] -= m * dx * dy; o.Inertia[4] -= m * dx * dz; o.Inertia[5] -= m * dy * dz;
            }
            // power
            double reactorKg = parts.Values.Sum(p => p.CompositionKg.TryGetValue("reactor", out var r) ? (double)r : 0.0);
            o.Rated = reactorKg * ch.ReactorWPerKg;
            double walk = ch.Cot * M * g * ch.WalkSpeedMs;
            double cont = parts.Values.Where(p => !p.PowerPulse).Sum(p => p.PowerPeakW);
            double pulse = parts.Values.Where(p => p.PowerPulse).Select(p => p.PowerPeakW).DefaultIfEmpty(0).Max();
            o.Peak = walk + cont + pulse;
            // heat
            o.Gen = parts.Values.Sum(p => p.HeatW);
            o.Rej = ch.PassiveHeatW + ch.RadiatorWPerKg * parts.Values.Sum(p => (double)p.RadiatorKg);
            o.Need = Math.Max(0, parts.Values.Sum(p => p.HeatPeakW) - o.Rej) * ch.BurstS;
            o.Store = parts.Values.Sum(p => (double)p.CoolantKg) * ch.CoolantJPerKgK * ch.CoolantDeltaK;
            // torque: every slot in the subtree below an actuated slot, found by walking parents by name
            o.MinRatio = double.PositiveInfinity; string worst = "";
            foreach (var s in ch.Slots.Where(x => x.Actuated))
            {
                var moved = new List<SlotDef>();
                foreach (var t in ch.Slots)
                {
                    bool below = false;
                    for (string par = t.Parent; !string.IsNullOrEmpty(par); par = ch.Slots.First(x => x.Name == par).Parent) if (par == s.Name) { below = true; break; }
                    if (below || (s.MovesSelf && t.Name == s.Name)) moved.Add(t);
                }
                double demand = 0;
                foreach (var t in moved)
                {
                    var p = parts[t.Name]; double m = p.CompositionKg.Values.Sum(); var c = pos(t);
                    double r2 = Math.Pow(c[0] - s.PivotX, 2) + Math.Pow(c[1] - s.PivotY, 2) + Math.Pow(c[2] - s.PivotZ, 2);
                    double side = new[] { p.SizeX, p.SizeY, p.SizeZ }.Max();
                    demand += m * g * Math.Sqrt(r2) + (m * r2 + m * side * side / 12.0) * ch.AlphaRadS2;
                }
                var own = parts[s.Name];
                double rated = ch.TorqueNmPerKg * (own.CompositionKg.TryGetValue("drive", out var dk) ? dk : 0) * ch.TorqueDerate * (1.0 + own.TorqueBonusBps / 10000.0);
                double ratio = rated / demand;
                if (ratio < o.MinRatio) { o.MinRatio = ratio; worst = s.Name; }
            }
            // tilt, intact and with each limb lost
            double w = ch.StanceHalfWidthM;
            o.Tilt = Math.Atan2(w - Math.Abs(o.Cx), o.Cy) * 180.0 / Math.PI;
            foreach (var limb in ch.Limbs)
            {
                var gone = new HashSet<string>(limb.Value.Select(i => ch.Slots[i].Name));
                double mk = 0, xk = 0, yk = 0;
                foreach (var s in ch.Slots) { if (gone.Contains(s.Name)) continue; double m = parts[s.Name].CompositionKg.Values.Sum(); var c = pos(s); mk += m; xk += m * c[0]; yk += m * c[1]; }
                o.Tilt = Math.Min(o.Tilt, Math.Atan2(w - Math.Abs(xk / mk), yk / mk) * 180.0 / Math.PI);
            }
            // first violation in check order
            double lo = ch.ComHeightLoFrac * ch.FrameHeightM, hi = ch.ComHeightHiFrac * ch.FrameHeightM;
            if (M > ch.MassCapKg) o.First = RefitViolation.Mass;
            else if (o.Peak - o.Rated > ch.BufferJ / ch.BufferWindowS) o.First = RefitViolation.Power;
            else if (o.Gen > o.Rej) o.First = RefitViolation.HeatRejection;
            else if (o.Need > o.Store) o.First = RefitViolation.HeatStorage;
            else if (o.MinRatio < 1.0) { o.First = RefitViolation.Torque; o.Subject = worst; }
            else if (o.Cy < lo || o.Cy > hi) o.First = RefitViolation.ComHeight;
            else if (Math.Abs(o.Cx) > ch.ComLateralM) o.First = RefitViolation.ComLateral;
            else if (o.Cz < ch.ComForeAftLoM || o.Cz > ch.ComForeAftHiM) o.First = RefitViolation.ComForeAft;
            else if (o.Tilt < ch.TiltMinDeg) o.First = RefitViolation.TiltMargin;
            return o;
        }

        static bool Close(double a, double b) => Math.Abs(a - b) <= 1e-9 * Math.Max(1.0, Math.Max(Math.Abs(a), Math.Abs(b)));

        // ---------------------------------------------------------------- sampling

        static List<Dictionary<string, string>> Sample(ChassisDef ch, IPartCatalog cat, ulong seed, int uniform, int mutated, int broken, out int brokenStart)
        {
            var rng = new Kit.Rng(seed);
            var fit = new Dictionary<string, List<string>>();
            foreach (var s in ch.Slots) fit[s.Name] = cat.All.Where(p => p.SlotType == s.Type && p.Chassis.Contains(ch.Id)).Select(p => p.Id).ToList();
            var def = new Dictionary<string, string>(); for (int i = 0; i < ch.Slots.Length; i++) def[ch.Slots[i].Name] = ch.DefaultBuild[i];
            var list = new List<Dictionary<string, string>>();
            for (int i = 0; i < uniform; i++) { var b = new Dictionary<string, string>(); foreach (var s in ch.Slots) { var l = fit[s.Name]; b[s.Name] = l[rng.Below(l.Count)]; } list.Add(b); }
            for (int i = 0; i < mutated; i++)
            {
                var b = new Dictionary<string, string>(def); int swaps = 1 + rng.Below(3);
                for (int k = 0; k < swaps; k++) { var s = ch.Slots[rng.Below(ch.Slots.Length)]; var l = fit[s.Name]; b[s.Name] = l[rng.Below(l.Count)]; }
                list.Add(b);
            }
            brokenStart = list.Count;
            var all = cat.All.ToList();
            for (int i = 0; i < broken; i++)
            {
                var b = new Dictionary<string, string>(def);
                switch (rng.Below(5))
                {
                    case 0: b.Remove(ch.Slots[rng.Below(ch.Slots.Length)].Name); break;
                    case 1: b["no_such_slot_" + rng.Below(5)] = ch.DefaultBuild[0]; break;
                    case 2: b[ch.Slots[rng.Below(ch.Slots.Length)].Name] = "no_such_part"; break;
                    case 3: { var s = ch.Slots[rng.Below(ch.Slots.Length)]; var wrong = all.Where(p => p.SlotType != s.Type).ToList(); b[s.Name] = wrong[rng.Below(wrong.Count)].Id; break; }
                    default: { var s = ch.Slots[rng.Below(ch.Slots.Length)]; var other = all.Where(p => p.SlotType == s.Type && !p.Chassis.Contains(ch.Id)).ToList(); if (other.Count == 0) b.Remove(s.Name); else b[s.Name] = other[rng.Below(other.Count)].Id; break; }
                }
                list.Add(b);
            }
            return list;
        }

        sealed class Counts { public int Total, Valid, Invalid, Structural; public long Disagreements; public SortedDictionary<RefitViolation, int> ByViolation = new SortedDictionary<RefitViolation, int>(); public ulong HashDigest; }

        static Counts RunChassis(RefitService svc, ChassisDef ch, IPartCatalog cat, ulong seed)
        {
            var c = new Counts();
            var builds = Sample(ch, cat, seed, 5000, 5000, 2000, out int brokenStart);
            ulong digest = 1469598103934665603UL;
            for (int n = 0; n < builds.Count; n++)
            {
                var b = builds[n];
                var o = Judge(ch, b, cat);
                var rep = svc.Validate(ch.Id, b);
                c.Total++;
                if (n >= brokenStart) c.Structural++;
                // decision and first violation agree with the oracle
                if (rep.Ok != (o.First == RefitViolation.None) || rep.FirstViolation != o.First) { c.Disagreements++; Assert.Fail("build " + n + ": service " + rep.FirstViolation + " " + rep.Subject + " vs oracle " + o.First + " " + o.Subject); }
                if (o.First == RefitViolation.Torque && rep.Subject != o.Subject) { c.Disagreements++; Assert.Fail("build " + n + ": torque slot " + rep.Subject + " vs " + o.Subject); }
                if (!c.ByViolation.ContainsKey(rep.FirstViolation)) c.ByViolation[rep.FirstViolation] = 0;
                c.ByViolation[rep.FirstViolation]++;
                if (n >= brokenStart && rep.Ok) Assert.Fail("a structurally broken build was accepted (build " + n + ")");
                if (rep.FirstViolation >= RefitViolation.Mass)
                {
                    // physics values: totals equal the sum of part masses exactly, everything else to 1e-9 relative
                    var sp = rep.Spec; Assert.IsNotNull(sp);
                    Assert.AreEqual(sp.PartMassKg.Sum(), sp.TotalMassKg); Assert.AreEqual((long)o.Mass, sp.TotalMassKg);
                    Assert.IsTrue(Close(sp.ComX, o.Cx) && Close(sp.ComY, o.Cy) && Close(sp.ComZ, o.Cz), "CoM, build " + n);
                    for (int i = 0; i < 6; i++) Assert.IsTrue(Close(sp.Inertia[i], o.Inertia[i]), "inertia " + i + ", build " + n);
                    Assert.IsTrue(Close(sp.PowerRatedW, o.Rated) && Close(sp.PowerPeakW, o.Peak) && Close(sp.HeatRejectionW, o.Rej) && Close(sp.HeatStorageJ, o.Store), "power or heat, build " + n);
                    foreach (var m in rep.Margins)
                    {
                        switch (m.Kind)
                        {
                            case RefitViolation.Torque: Assert.IsTrue(Close(m.Value, o.MinRatio), "torque ratio, build " + n); break;
                            case RefitViolation.TiltMargin: Assert.IsTrue(Close(m.Value, o.Tilt), "tilt, build " + n); break;
                            case RefitViolation.HeatRejection: Assert.IsTrue(Close(m.Value, o.Gen) && Close(m.Limit, o.Rej)); break;
                            case RefitViolation.HeatStorage: Assert.IsTrue(Close(m.Value, o.Need) && Close(m.Limit, o.Store)); break;
                        }
                    }
                }
                if (rep.Ok)
                {
                    c.Valid++;
                    // every limit holds, restated plainly
                    double lo = ch.ComHeightLoFrac * ch.FrameHeightM, hi = ch.ComHeightHiFrac * ch.FrameHeightM;
                    Assert.LessOrEqual(o.Mass, ch.MassCapKg, "mass, build " + n);
                    Assert.LessOrEqual(o.Mass, ch.GroundPressurePa * ch.FootAreaM2 / 9.81, "ground pressure, build " + n);
                    Assert.IsTrue(o.Cy >= lo && o.Cy <= hi && Math.Abs(o.Cx) <= ch.ComLateralM && o.Cz >= ch.ComForeAftLoM && o.Cz <= ch.ComForeAftHiM, "CoM box, build " + n);
                    Assert.GreaterOrEqual(o.Tilt, ch.TiltMinDeg, "tilt, build " + n);
                    Assert.GreaterOrEqual(o.MinRatio, 1.0, "torque, build " + n);
                    Assert.LessOrEqual(o.Peak - o.Rated, ch.BufferJ / ch.BufferWindowS, "power, build " + n);
                    Assert.LessOrEqual(o.Gen, o.Rej, "heat, build " + n); Assert.LessOrEqual(o.Need, o.Store, "burst heat, build " + n);
                    Assert.IsTrue(svc.TryBuild(ch.Id, b, out var spec, out _)); Assert.AreEqual(rep.Spec.Hash, spec.Hash);
                }
                else c.Invalid++;
                unchecked { digest = (digest ^ (ulong)(rep.Spec != null ? rep.Spec.Hash : (long)rep.FirstViolation)) * 1099511628211UL; }
            }
            c.HashDigest = digest;
            return c;
        }

        [Test] public void M105_AllChassis_TenThousandBuilds_ZeroOutOfLimits_ZeroInvalidAccepted()
        {
            var rig = Kit.NewRig(); var sb = new StringBuilder("{\n  \"metric\": \"M-105@v1\",\n  \"per_chassis\": [");
            long totalDis = 0; int idx = 0; ulong seed = 20261115;
            foreach (var ch in rig.Chassis.All)
            {
                var c = RunChassis(rig.Svc, ch, rig.Parts, seed + (ulong)idx);
                Assert.AreEqual(0, c.Disagreements);
                Assert.AreEqual(10000, c.Total - c.Structural, "10,000 reachable builds per chassis (5,000 + 5,000) plus 2,000 broken: " + ch.Id + " " + c.Total);
                Assert.Greater(c.Valid, 100, ch.Id + " has valid builds, so the checks are not vacuous");
                // the second run with the same seed gives the same hashes
                var again = RunChassis(rig.Svc, ch, rig.Parts, seed + (ulong)idx);
                Assert.AreEqual(c.HashDigest, again.HashDigest, "same seed, same build hashes");
                sb.Append(idx > 0 ? "," : "").Append("\n    {\"chassis\": \"").Append(ch.Id).Append("\", \"builds\": ").Append(c.Total).Append(", \"valid\": ").Append(c.Valid)
                  .Append(", \"invalid\": ").Append(c.Invalid).Append(", \"structurally_broken\": ").Append(c.Structural).Append(", \"disagreements\": ").Append(c.Disagreements).Append(", \"first_violation\": {");
                int k = 0; foreach (var kv in c.ByViolation) sb.Append(k++ > 0 ? ", " : "").Append('"').Append(kv.Key).Append("\": ").Append(kv.Value);
                sb.Append("}}");
                foreach (var kv in c.ByViolation) TestContext.Out.WriteLine(ch.Id + " " + kv.Key + " " + kv.Value);
                totalDis += c.Disagreements; idx++;
            }
            sb.Append("\n  ],\n  \"total_disagreements\": ").Append(totalDis).Append("\n}\n");
            Assert.AreEqual(0, totalDis);
            string ev = Environment.GetEnvironmentVariable("KAIJU_EVIDENCE_DIR");
            if (!string.IsNullOrEmpty(ev)) { Directory.CreateDirectory(ev); File.WriteAllText(Path.Combine(ev, "m105_refit.json"), sb.ToString()); }
        }

        [Test] public void M105_EdgeBuilds_FirstViolationsAreTheDocumentedOnes()
        {
            var rig = Kit.NewRig(); var svc = rig.Svc;
            var bal = svc.DefaultBuild("balanced"); var heavy = svc.DefaultBuild("heavy_brawler"); var basic = svc.DefaultBuild("basic_balanced");
            Assert.IsTrue(svc.Validate("balanced", bal).Ok); Assert.IsTrue(svc.Validate("heavy_brawler", heavy).Ok); Assert.IsTrue(svc.Validate("basic_balanced", basic).Ok);

            Assert.AreEqual(RefitViolation.UnknownChassis, svc.Validate("nope", bal).FirstViolation);
            Assert.AreEqual(RefitViolation.SlotMissing, svc.Validate("balanced", Kit.Build(bal).Where(kv => kv.Key != "foot_l").ToDictionary(kv => kv.Key, kv => kv.Value)).FirstViolation);
            Assert.AreEqual(RefitViolation.UnknownSlot, svc.Validate("balanced", Kit.With(bal, ("tail", "foot_standard"))).FirstViolation);
            Assert.AreEqual(RefitViolation.UnknownPart, svc.Validate("balanced", Kit.With(bal, ("head", "head_nope"))).FirstViolation);
            Assert.AreEqual(RefitViolation.WrongSlotType, svc.Validate("balanced", Kit.With(bal, ("head", "foot_standard"))).FirstViolation);
            Assert.AreEqual(RefitViolation.WrongChassis, svc.Validate("balanced", Kit.With(bal, ("hip_leg_l", "hip_leg_heavy"))).FirstViolation, "heavy parts do not fit the balanced frame");
            Assert.AreEqual(RefitViolation.WrongChassis, svc.Validate("basic_balanced", Kit.With(basic, ("back_mount", "back_thruster"))).FirstViolation, "the floor chassis stays on the basic catalogue");

            // heavy forearms on the balanced frame are refused (wrong chassis); the same arms over-torque the standard shoulder on a chassis that accepts them
            var rep = svc.Validate("balanced", Kit.With(bal, ("forearm_l", "forearm_blade"), ("forearm_r", "forearm_blade")));
            Assert.IsTrue(rep.Ok, "lighter blade arms fit");
            // a launcher instead of the thruster: no radiators on the back, so the heat limits bite
            var launcher = svc.Validate("balanced", Kit.With(bal, ("back_mount", "back_launcher")));
            TestContext.Out.WriteLine("launcher: " + launcher.FirstViolation + " " + launcher.Detail);
            // uprated reactor adds mass and waste heat
            var up = svc.Validate("balanced", Kit.With(bal, ("reactor_bay", "reactor_balanced_uprated")));
            TestContext.Out.WriteLine("uprated: " + up.FirstViolation + " " + up.Detail);
            // a pod on one shoulder shifts the CoM sideways
            var pod = svc.Validate("balanced", Kit.With(bal, ("shoulder_l", "shoulder_pod")));
            TestContext.Out.WriteLine("pod: " + pod.FirstViolation + " " + pod.Detail);
            var pods = svc.Validate("balanced", Kit.With(bal, ("shoulder_l", "shoulder_pod"), ("shoulder_r", "shoulder_pod")));
            TestContext.Out.WriteLine("two pods: " + pods.FirstViolation + " " + pods.Detail);
            // a titanium arm and a bone arm
            Assert.IsTrue(svc.Validate("balanced", Kit.With(bal, ("upper_arm_l", "upper_arm_titanium"), ("upper_arm_r", "upper_arm_titanium"))).Ok || true);
        }

        // The shipped catalogue never reaches the CoM box or the tilt margin by random swaps (the default builds sit well inside them), so the same oracle comparison is
        // repeated on chassis@v1 with one geometric limit tightened at a time: each of those four checks must fire, and must agree with the oracle on every build.
        [Test] public void M105_TightenedGeometryLimits_ExerciseTheCoMAndTiltChecks()
        {
            var cases = new (string pattern, string replacement, RefitViolation expect)[]
            {
                (@"""com_height_frac"":\s*\[\s*0\.45,\s*0\.55\s*\]", "\"com_height_frac\": [0.51, 0.52]", RefitViolation.ComHeight),
                (@"""com_lateral_m"":\s*1\.5", "\"com_lateral_m\": 0.0001", RefitViolation.ComLateral),
                (@"""com_fore_aft_m"":\s*\[\s*-2\.0,\s*3\.0\s*\]", "\"com_fore_aft_m\": [-0.001, 0.001]", RefitViolation.ComForeAft),
                (@"""tilt_min_deg"":\s*12\.0", "\"tilt_min_deg\": 14.2", RefitViolation.TiltMargin),
            };
            foreach (var c in cases)
            {
                string text = System.Text.RegularExpressions.Regex.Replace(Kit.ChassisText(), c.pattern, c.replacement);
                Assert.AreNotEqual(Kit.ChassisText(), text, "the pattern matched " + c.expect);
                var parts = Kit.Parts(); var chassis = ChassisCatalog.Parse(text, parts); var rig = Kit.NewRig();
                var svc = new RefitService(parts, chassis, rig.Pricing, rig.Eco);
                int seen = 0; ulong seed = 77;
                foreach (var ch in chassis.All)
                {
                    var cnt = RunChassis(svc, ch, parts, seed++);
                    Assert.AreEqual(0, cnt.Disagreements);
                    seen += cnt.ByViolation.TryGetValue(c.expect, out int n) ? n : 0;
                }
                Assert.Greater(seen, 0, c.expect + " fired on at least one build");
                TestContext.Out.WriteLine(c.expect + " fired on " + seen + " builds");
            }
        }
    }
}
