using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Game.Control.Contracts;
using Game.Units.Mech;
using Game.Units.Mech.Contracts;
using NUnit.Framework;

namespace Game.Units.Mech.Tests
{
    static class Repo
    {
        public static string Root()
        {
            foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
                for (string dir = start; dir != null; dir = Path.GetDirectoryName(dir))
                    if (Directory.Exists(Path.Combine(dir, "Assets", "Game", "Config"))) return dir;
            throw new DirectoryNotFoundException("project root not found");
        }
        public static T Load<T>(string relative)
        {
            string json = File.ReadAllText(Path.Combine(Root(), "Assets", "Game", "Config", relative));
#if UNITY_5_3_OR_NEWER
            return UnityEngine.JsonUtility.FromJson<T>(json);
#else
            return System.Text.Json.JsonSerializer.Deserialize<T>(json, new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
#endif
        }
        public static WalkTuning Walk() => Load<WalkTuning>("Mech/walk_tuning.json");
    }

    public class LipmTests
    {
        const double W = 0.48;

        [Test]
        public void DcmAtEnd_MatchesNumericalIntegration_WithMovingCop()
        {
            Vector2 xi = new Vector2(1.0f, 3.0f), p = new Vector2(0.5f, -2.0f), pdot = new Vector2(0, 1.5f);
            Vector2 closed = Lipm.DcmAtEnd(xi, p, pdot, W, 2.5);
            double x = xi.X, z = xi.Y, dt = 1e-4;
            for (double t = 0; t < 2.5; t += dt) { x += W * (x - (p.X + pdot.X * t)) * dt; z += W * (z - (p.Y + pdot.Y * t)) * dt; }
            Assert.That(closed.X, Is.EqualTo(x).Within(2e-3)); Assert.That(closed.Y, Is.EqualTo(z).Within(5e-3));
        }

        [Test]
        public void DeadbeatOffset_BringsTheDcmToItsTarget()
        {
            Vector2 xi = new Vector2(0.4f, 2.0f), nominal = new Vector2(0, -1), pdot = new Vector2(0, 2f), want = new Vector2(-1.5f, 14f);
            Vector2 offset = Lipm.DeadbeatOffset(xi, nominal, pdot, want, W, 2.0);
            Vector2 end = Lipm.DcmAtEnd(xi, nominal + offset, pdot, W, 2.0);
            Assert.That((end - want).Length(), Is.LessThan(1e-3f));
        }

        [Test]
        public void SteadyGaitOffsets_ArePeriodic()
        {
            double T = 3.3, step = 20, width = 5;
            double bx = Lipm.OffsetForward(step, W, T), by = Lipm.OffsetLateral(width, W, T);
            // forward: start bx ahead of the CoP, grow by e^{wT}, next CoP is one step ahead
            Assert.That(bx * Math.Exp(W * T) - step, Is.EqualTo(bx).Within(1e-9));
            // lateral: start by toward the swing side, next CoP is one width to that side, DCM must sit by on its other side
            Assert.That(by * Math.Exp(W * T) - width, Is.EqualTo(-by).Within(1e-9));
        }

        [Test]
        public void StepTime_FollowsAlexander_AndIsCappedWhenSlow()
        {
            double l = 42, g = 9.81, v = Math.Sqrt(0.20 * g * l);
            Assert.That(v, Is.EqualTo(9.08).Within(0.01));
            Assert.That(2 * Lipm.StepTime(v, l, g, 2.3, 0.3, 10), Is.EqualTo(6.57).Within(0.03), "R-001: stride period 2.3 Fr^-0.2 sqrt(l/g) at Fr 0.20");
            Assert.That(Lipm.StepTime(0, l, g, 2.3, 0.3, 4.0), Is.EqualTo(4.0));
            Assert.That(Lipm.MinJerk(0), Is.EqualTo(0)); Assert.That(Lipm.MinJerk(1), Is.EqualTo(1)); Assert.That(Lipm.MinJerk(0.5), Is.EqualTo(0.5).Within(1e-12));
        }
    }

    public class LegIkTests
    {
        [TestCase(0f, -36f, 6f, 0f)]
        [TestCase(2f, -30f, -10f, 0.3f)]
        [TestCase(-1f, -37.5f, 0f, -0.2f)]
        public void Solve_ReachesTheAnkleTarget_WithTheKneeForward(float dx, float dy, float dz, float footYaw)
        {
            Vector3 hip = new Vector3(-6.5f, 41f, 3f), ankle = hip + new Vector3(dx, dy, dz);
            Quaternion pelvis = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.1f), foot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, footYaw);
            var s = LegIk.Solve(hip, pelvis, ankle, foot, 19, 19, 0.1, 2.2);
            Vector3 knee = hip + Vector3.Transform(new Vector3(0, -19, 0), s.Thigh), reached = knee + Vector3.Transform(new Vector3(0, -19, 0), s.Shank);
            Assert.That((reached - ankle).Length(), Is.LessThan(0.02f));
            Assert.That(s.Knee, Is.GreaterThanOrEqualTo(0.1 - 1e-6));
            Vector3 mid = (hip + ankle) * 0.5f, fwd = Vector3.Transform(Vector3.UnitZ, foot);
            Assert.That(Vector3.Dot(knee - mid, fwd), Is.GreaterThan(0f), "the knee bends forward, never backward");
            // relative rotations compose back to the world rotations
            Assert.That(Math.Abs(Quaternion.Dot(pelvis * s.HipRelative, s.Thigh)), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(Math.Abs(Quaternion.Dot(s.Shank * s.AnkleRelative, foot)), Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void Solve_ClampsAnUnreachableTarget_InsteadOfStretchingTheLeg()
        {
            var s = LegIk.Solve(new Vector3(0, 40, 0), Quaternion.Identity, new Vector3(0, 0, 30), Quaternion.Identity, 19, 19, 0.14, 2.2);
            Assert.That(s.Clamped, Is.True);
            Assert.That(s.Knee, Is.EqualTo(0.14).Within(1e-3));
        }
    }

    public class IntentLimiterTests
    {
        const double Dt = 1.0 / 60.0;
        static MechLimits Limits() => new MechLimits(Repo.Walk(), 42.0, 9.81);
        static IntentFrame Walk(float z, float effort) => new IntentFrame { move = MoveId.Walk, target = new Vector3(0, 0, z), effort = effort, pilots = 1 };

        [Test]
        public void Limits_ComeFromFroudeAndR001()
        {
            var l = Limits();
            Assert.That(l.VCruise, Is.EqualTo(9.08).Within(0.01)); Assert.That(l.VMax, Is.EqualTo(11.12).Within(0.01));
            Assert.That(l.AccelStart, Is.LessThanOrEqualTo(0.14 * 9.81), "R-001 M-001.6");
            Assert.That(l.DecelEmergency, Is.LessThanOrEqualTo(0.30 * 9.81), "R-001 M-001.4");
            Assert.That(l.LateralAccel, Is.LessThanOrEqualTo(0.085 * 9.81), "R-001 M-001.5");
            Assert.That(l.YawRateMax * 180 / Math.PI, Is.LessThanOrEqualTo(26.7 * Math.Sqrt(42.4 / 42.0)), "R-001 M-001.7");
            Assert.That(l.YawAccelMax * 180 / Math.PI, Is.LessThanOrEqualTo(4.0), "R-001 M-001.8");
            Assert.That(l.EffortForFroude(0.20) * l.VMax, Is.EqualTo(l.VCruise).Within(1e-9));
        }

        [Test]
        public void OverCommandedSpeed_IsClamped_Ramped_AndReported()
        {
            var l = Limits(); var lim = new IntentLimiter(l, Dt);
            LocoCommand c = default; double prev = 0, maxAccel = 0;
            for (int i = 0; i < 60 * 30; i++)
            {
                c = lim.Update(Walk(5000, 3f * (float)l.EffortForFroude(0.20)), Vector2.Zero, 0, c.Speed);
                maxAccel = Math.Max(maxAccel, (c.Speed - prev) / Dt); prev = c.Speed;
                if (i == 0) Assert.That(c.Causes & LimitCause.EffortRange, Is.EqualTo(LimitCause.EffortRange), "3x cruise is out of range and says so");
            }
            Assert.That(c.Speed, Is.EqualTo(l.VMax).Within(1e-3), "never faster than the top of the Froude band");
            Assert.That(maxAccel, Is.LessThanOrEqualTo(l.AccelStart * 1.0001));
            Assert.That(c.RequestedSpeed, Is.GreaterThan(c.Speed), "the log keeps the intent as received");
        }

        [Test]
        public void InstantReversal_SlowsThenTurnsOnTheSpot()
        {
            var l = Limits(); var lim = new IntentLimiter(l, Dt); LocoCommand c = default;
            for (int i = 0; i < 60 * 20; i++) c = lim.Update(Walk(5000, 0.8f), Vector2.Zero, 0, c.Speed);
            double speedBefore = c.Speed, headingBefore = c.Heading, maxDecel = 0, maxYawRate = 0, prev = c.Speed; bool sawTurnFirst = false;
            for (int i = 0; i < 60 * 40; i++)
            {
                c = lim.Update(Walk(-5000, 0.8f), Vector2.Zero, 0, c.Speed);
                maxDecel = Math.Max(maxDecel, (prev - c.Speed) / Dt); prev = c.Speed; maxYawRate = Math.Max(maxYawRate, Math.Abs(c.YawRate));
                sawTurnFirst |= (c.Causes & LimitCause.TurnBeforeWalk) != 0;
                if (i == 0) Assert.That(Math.Abs(c.Heading - headingBefore), Is.LessThan(0.01), "no heading snap");
            }
            Assert.That(speedBefore, Is.GreaterThan(5));
            Assert.That(sawTurnFirst, Is.True);
            Assert.That(maxDecel, Is.LessThanOrEqualTo(l.DecelNormal * 1.0001));
            Assert.That(maxYawRate, Is.LessThanOrEqualTo(l.YawRateMax * 1.0001));
            Assert.That(Math.Abs(IntentLimiter.Wrap(c.Heading - Math.PI)), Is.LessThan(0.05), "the 180 degree turn is carried out, by steps");
        }

        [Test]
        public void StickJitterAt30Hz_NeverExceedsTheRates()
        {
            var l = Limits(); var lim = new IntentLimiter(l, Dt); LocoCommand c = default; double prevSpeed = 0, prevRate = 0, maxAccel = 0, maxYawAccel = 0;
            for (int i = 0; i < 600; i++)
            {
                c = lim.Update(i % 2 == 0 ? Walk(5000, 1f) : Walk(-5000, 0.2f), Vector2.Zero, 0, c.Speed);
                maxAccel = Math.Max(maxAccel, Math.Abs(c.Speed - prevSpeed) / Dt); maxYawAccel = Math.Max(maxYawAccel, Math.Abs(c.YawRate - prevRate) / Dt); prevSpeed = c.Speed; prevRate = c.YawRate;
            }
            Assert.That(maxAccel, Is.LessThanOrEqualTo(Math.Max(l.AccelStart, l.DecelNormal) * 1.0001));
            Assert.That(maxYawAccel, Is.LessThanOrEqualTo(l.YawAccelMax * 1.0001));
        }

        [Test]
        public void WalkToATarget_StopsByTheStoppingDistanceRule_AndEmergencyStopIsFasterButLimited()
        {
            var l = Limits(); var lim = new IntentLimiter(l, Dt); LocoCommand c = default; double z = 0;
            for (int i = 0; i < 60 * 80; i++) { c = lim.Update(Walk(300, 0.8f), new Vector2(0, (float)z), 0, c.Speed); z += c.Speed * Dt; }
            Assert.That(c.Speed, Is.EqualTo(0).Within(1e-6)); Assert.That(Math.Abs(z - 300), Is.LessThan(30), "within one step length of the target (C13)");
            Assert.That(c.Causes & LimitCause.Arrived, Is.EqualTo(LimitCause.Arrived));
            lim = new IntentLimiter(l, Dt); c = default;
            for (int i = 0; i < 60 * 20; i++) c = lim.Update(Walk(5000, 0.8165f), Vector2.Zero, 0, c.Speed);
            int steps = 0; double v0 = c.Speed;
            while (c.Speed > 0 && steps < 6000) { c = lim.Update(new IntentFrame { move = MoveId.EmergencyStop }, Vector2.Zero, 0, c.Speed); steps++; }
            Assert.That(steps * Dt, Is.GreaterThanOrEqualTo(v0 / (0.30 * 9.81)), "R-001: nothing stops faster than v / (0.30 g)");
            Assert.That(steps * Dt, Is.LessThan(v0 / l.DecelNormal), "the emergency stop is quicker than the normal one");
        }
    }

    public class ArchitectureTests
    {
        static IEnumerable<KeyValuePair<string, string[]>> Asmdefs()
        {
            string game = Path.Combine(Repo.Root(), "Assets", "Game");
            foreach (string module in new[] { "Physics/Body", "Control", "Units/Mech" })
                foreach (string file in Directory.GetFiles(Path.Combine(game, module), "*.asmdef", SearchOption.AllDirectories))
                {
                    string text = File.ReadAllText(file);
                    string block = Regex.Match(text, "\"references\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline).Groups[1].Value;
                    yield return new KeyValuePair<string, string[]>(Path.GetFileNameWithoutExtension(file), Regex.Matches(block, "\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value).ToArray());
                }
        }
        static string ModuleOf(string assembly)
        {
            foreach (string m in new[] { "Game.Physics.Body", "Game.Control", "Game.Units.Mech", "Game.Core" }) if (assembly == m || assembly.StartsWith(m + ".", StringComparison.Ordinal)) return m;
            return null;
        }
        static bool IsCompositionRoot(string assembly) => assembly.Contains("Tests") || assembly.EndsWith(".Editor", StringComparison.Ordinal) || assembly.EndsWith(".Scenarios", StringComparison.Ordinal);

        [Test]
        public void PhysicsBody_ReferencesNeitherControlNorUnits()
        {
            foreach (var a in Asmdefs().Where(a => ModuleOf(a.Key) == "Game.Physics.Body"))
                foreach (string r in a.Value) Assert.That(r.StartsWith("Game.Control", StringComparison.Ordinal) || r.StartsWith("Game.Units", StringComparison.Ordinal), Is.False, a.Key + " references " + r + " (layer L1 may not look up)");
        }

        [Test]
        public void RuntimeAssemblies_UseOtherModulesOnlyThroughContracts()
        {
            int checkedCount = 0;
            foreach (var a in Asmdefs().Where(a => !IsCompositionRoot(a.Key)))
                foreach (string r in a.Value)
                {
                    string other = ModuleOf(r);
                    if (other == null || other == ModuleOf(a.Key)) continue;
                    checkedCount++;
                    Assert.That(r.EndsWith(".Contracts", StringComparison.Ordinal), Is.True, a.Key + " references the implementation assembly " + r);
                }
            Assert.That(checkedCount, Is.GreaterThan(5));
        }

        [Test]
        public void EveryModule_HasContractsRuntimeTestsAndModuleDoc()
        {
            var names = new HashSet<string>(Asmdefs().Select(a => a.Key));
            foreach (string m in new[] { "Game.Physics.Body", "Game.Control", "Game.Units.Mech" })
            {
                Assert.That(names.Contains(m + ".Contracts"), m + ".Contracts"); Assert.That(names.Contains(m), m); Assert.That(names.Contains(m + ".Tests"), m + ".Tests"); Assert.That(names.Contains(m + ".PlayTests"), m + ".PlayTests");
            }
            foreach (string folder in new[] { "Physics/Body", "Control", "Units/Mech" }) Assert.That(File.Exists(Path.Combine(Repo.Root(), "Assets", "Game", folder, "MODULE.md")), folder + "/MODULE.md");
        }

        [Test]
        public void CompositionRootAssemblies_AreNotCompiledIntoPlayers()
        {
            // Scenarios constructs implementations of other modules on purpose (headless runs, tests): it must exist only where tests exist
            string text = File.ReadAllText(Path.Combine(Repo.Root(), "Assets", "Game", "Units", "Mech", "Scenarios", "Game.Units.Mech.Scenarios.asmdef"));
            Assert.That(text, Does.Contain("UNITY_INCLUDE_TESTS"));
            foreach (var a in Asmdefs().Where(a => !IsCompositionRoot(a.Key)))
                foreach (string r in a.Value) Assert.That(r.EndsWith(".Scenarios", StringComparison.Ordinal), Is.False, a.Key + " (a runtime assembly) references the composition root " + r);
        }

        [Test]
        public void BehaviourCode_KeepsItsTuningNumbersInConfig()
        {
            // DEF-T005-arch-1: no decimal literal in the controller or the limiter other than exact mathematical constants (0.5 midpoints, tiny epsilons)
            foreach (string file in new[] { "LocomotionController.cs", "IntentLimiter.cs" })
            {
                int line = 0;
                foreach (string raw in File.ReadAllLines(Path.Combine(Repo.Root(), "Assets", "Game", "Units", "Mech", "Runtime", file)))
                {
                    line++;
                    string code = raw.Contains("//") ? raw.Substring(0, raw.IndexOf("//", StringComparison.Ordinal)) : raw;
                    foreach (Match m in Regex.Matches(code, @"(?<![\w.])\d+\.\d+f?\b"))
                    {
                        string v = m.Value.TrimEnd('f');
                        Assert.That(v == "0.5" || v == "1.5" || v == "2.0" || v == "1.0", file + ":" + line + " literal " + m.Value);
                    }
                }
            }
        }

        [Test]
        public void EngineFreeAssemblies_DeclareNoEngineReferences()
        {
            string game = Path.Combine(Repo.Root(), "Assets", "Game");
            foreach (string rel in new[] { "Physics/Body/Contracts", "Physics/Body/Runtime", "Control/Contracts", "Control/Runtime", "Units/Mech/Contracts", "Units/Mech/Runtime" })
                Assert.That(File.ReadAllText(Directory.GetFiles(Path.Combine(game, rel), "*.asmdef")[0]), Does.Contain("\"noEngineReferences\": true"), rel);
        }

        [Test]
        public void FeatureFlags_OfTheseModules_ShipSwitchedOff()
        {
            foreach (string file in new[] { "PhysicsBody.json", "Control.json", "UnitsMech.json" })
            {
                string text = File.ReadAllText(Path.Combine(Repo.Root(), "Assets", "Game", "Config", "FeatureFlags", file));
                Assert.That(text, Does.Not.Contain("true"), file);
                Assert.That(Regex.Matches(text, ": false").Count, Is.GreaterThan(0), file);
            }
        }

        [Test]
        public void WalkTuning_Loads_WithItsSchema()
        {
            var w = Repo.Walk();
            Assert.That(w.schema, Is.EqualTo(WalkTuning.Schema));
            Assert.That(w.fr_cruise, Is.EqualTo(0.20)); Assert.That(w.duty_factor, Is.InRange(0.60, 0.72));
            Assert.That(w.touchdown_speed_tstar * 42.0 / Math.Sqrt(42.0 / 9.81), Is.LessThanOrEqualTo(1.0), "R-005: touchdown vertical speed <= 1.0 m/s");
        }
    }
}
