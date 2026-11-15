using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Control;
using Game.Control.Contracts;
using Game.Units.Mech.Scenarios;
using Game.Units.Mech.Unity;
using NUnit.Framework;
using UnityEngine;

namespace Game.Units.Mech.UnityTests
{
    /// <summary>
    /// T-005 r4 step 9.4d: the imp 0 / 1 / 2 code paths must be byte-identical to the base commit f7284be (before the 9.4b QP port).
    /// The scripted walk of ScenarioLibrary.Walk (stand 3 s, then walk at Fr 0.10) runs for <see cref="Ticks"/> ticks at 60 Hz; after every tick the body state (every link pose and velocity, every DOF position, velocity and applied torque,
    /// every foot force) goes into an FNV-1a 64 hash. The baseline file holds the final hash and a checkpoint every <see cref="Every"/> ticks per imp, produced by the same test run on f7284be (KAIJU_HASH_OUT=path writes it instead of comparing).
    /// Each imp is run <see cref="Repeats"/> times in one session: the engine does not reproduce itself indefinitely (imp 1 diverged after about 900 ticks in the first measurement), so identity is asserted over the checkpoint prefix on which the repeats agree with each other, in both the baseline session and this one.
    /// </summary>
    public class ImpByteIdentityTests
    {
        const double Dt = 1.0 / 60.0; const int Ticks = 1200, Every = 50, Repeats = 5, MinStable = 8;
        static readonly double[] Imps = { 0, 1, 2 };
        const string Baseline = "Game/Units/Mech/Tests/UnityOnly/imp012_baseline.txt";

        static ulong Mix(ulong h, long v) { unchecked { for (int i = 0; i < 8; i++) { h ^= (ulong)((v >> (8 * i)) & 0xFF); h *= 1099511628211UL; } } return h; }
        static ulong MixF(ulong h, float v) => Mix(h, BitConverter.SingleToInt32Bits(v));
        static ulong MixD(ulong h, double v) => Mix(h, BitConverter.DoubleToInt64Bits(v));
        static ulong MixV(ulong h, System.Numerics.Vector3 v) => MixF(MixF(MixF(h, v.X), v.Y), v.Z);

        static ulong Hash(Game.Physics.Body.Contracts.IArticulatedBody b, ulong h)
        {
            for (int l = 0; l < b.LinkCount; l++)
            {
                b.GetLink(l, out var s); h = MixV(h, s.Position); h = MixF(MixF(MixF(MixF(h, s.Rotation.X), s.Rotation.Y), s.Rotation.Z), s.Rotation.W); h = MixV(h, s.Com); h = MixV(h, s.Velocity); h = MixV(h, s.AngularVelocity);
            }
            for (int d = 0; d < b.DofCount; d++) { h = MixD(h, b.DofPosition(d)); h = MixD(h, b.DofVelocity(d)); h = MixD(h, b.DofAppliedTorque(d)); }
            for (int f = 0; f < b.FootCount; f++) { b.GetFoot(f, out var fs); h = MixD(h, fs.NormalForceN); h = MixV(h, fs.TangentialForceN); h = Mix(h, fs.Contact ? 1 : 0); }
            return h;
        }

        static void Run(double imp, out ulong final, out List<ulong> checkpoints)
        {
            var cfg = MechConfig.LoadDefault().WithWalk(w => w.imp = imp); checkpoints = new List<ulong>(); ulong h = 14695981039346656037UL;
            using (var sim = new MechSimulation(cfg.Body, Dt))
            {
                var pilot = new ScriptedPilotSource(sim.Core.Clock);
                var unit = sim.Spawn(cfg, new PilotSeat(new SinglePilotArbiter(), pilot), Vector3.zero, 0, false);
                ScenarioLibrary.Walk("walk_fr010", 0.10).Script(pilot, unit.Controller.Limits);
                for (int k = 1; k <= Ticks; k++) { sim.StepOnce(); h = Hash(unit.Body, h); if (k % Every == 0) checkpoints.Add(h); }
            }
            final = h;
        }

        static string Line(double imp, int stable, List<ulong> cp)
        {
            var sb = new StringBuilder("imp" + imp.ToString("0", CultureInfo.InvariantCulture) + " ticks=" + Ticks + " every=" + Every + " stable=" + stable + " cp=");
            for (int i = 0; i < cp.Count; i++) { if (i > 0) sb.Append(','); sb.Append(cp[i].ToString("x16")); }
            return sb.ToString();
        }

        [Test]
        public void Imp012_BodyStateIsByteIdenticalToTheBaseCommit()
        {
            var lines = new List<string>(); string outPath = Environment.GetEnvironmentVariable("KAIJU_HASH_OUT");
            string baselinePath = Path.Combine(Application.dataPath, Baseline);
            var expected = new Dictionary<string, string>();
            if (outPath == null)
            {
                Assert.That(File.Exists(baselinePath), Is.True, "baseline hashes " + baselinePath);
                foreach (var l in File.ReadAllLines(baselinePath)) if (l.Trim().Length > 0) expected[l.Substring(0, l.IndexOf(' '))] = l.Trim();
            }
            foreach (double imp in Imps)
            {
                // the engine does not reproduce itself indefinitely (step 9.4d measurement: imp 1 repeated in one session diverges after about 900 ticks), so the comparison is over the prefix on which Repeats runs agree with each other
                var runs = new List<List<ulong>>(); for (int k = 0; k < Repeats; k++) { Run(imp, out _, out var c); runs.Add(c); }
                int stable = runs[0].Count; foreach (var c in runs) { int i = 0; while (i < stable && c[i] == runs[0][i]) i++; stable = i; }
                string key = "imp" + imp.ToString("0", CultureInfo.InvariantCulture), line = Line(imp, stable, runs[0]); lines.Add(line); TestContext.WriteLine(line + "  (" + Repeats + " in-process repeats)");
                if (outPath != null) continue;
                Assert.That(stable, Is.GreaterThanOrEqualTo(MinStable), key + ": the engine reproduces itself over only " + stable + " checkpoints of " + Every + " ticks, too few to test identity");
                Assert.That(expected.ContainsKey(key), Is.True, key + " in the baseline file");
                var parts = expected[key].Split(' '); int baseStable = int.Parse(parts[3].Substring("stable=".Length)); var bc = parts[4].Substring("cp=".Length).Split(',');
                int common = Math.Min(stable, baseStable); Assert.That(common, Is.GreaterThanOrEqualTo(MinStable), key + ": common reproducible prefix (checkpoints)");
                for (int i = 0; i < common; i++)
                    Assert.That(runs[0][i].ToString("x16"), Is.EqualTo(bc[i]), key + ": body state differs from the baseline commit at checkpoint " + i + " (tick " + (i + 1) * Every + "); reproducible prefix now " + stable + ", baseline " + baseStable);
                TestContext.WriteLine(key + ": identical to the baseline over " + common + " checkpoints (" + common * Every + " ticks)");
            }
            if (outPath != null) File.WriteAllLines(outPath, lines);
        }
    }
}
