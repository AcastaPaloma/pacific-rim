// T-036 step 3: the metric dump exporters are deterministic, well-formed and carry what the Python tools require.
using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.World.Destruction.Tests
{
    public class DestructionDumpExporterTests
    {
        static List<KeyValuePair<BuildingId, BuildingAttributes>> Setup(DamageModel m, params int[] states)
        {
            var list = new List<KeyValuePair<BuildingId, BuildingAttributes>>();
            for (int i = 0; i < states.Length; i++)
            {
                var id = BuildingId.From(BuildingSource.OsmWay, 100UL + (ulong)i);
                var a = DestructionDumpExporter.Archetypes[i % 5];
                m.Register(id, a);
                m.TryGetThresholds(id, out var t1, out var t2, out var t3, out var t4);
                double[] t = { 0, t1, t2, t3, t4 };
                if (states[i] > 0) m.Apply(id, t[states[i]] * 1.0001, DamageCause.Impact, default, 1);
                list.Add(new KeyValuePair<BuildingId, BuildingAttributes>(id, a));
            }
            return list;
        }

        [Test]
        public void DestructionDump_HasRequiredFields_AndIsDeterministic()
        {
            var m = new DamageModel();
            var b = Setup(m, 0, 1, 3, 4, 4);
            string a = DestructionDumpExporter.BuildDestructionDump(m, b);
            Assert.AreEqual(a, DestructionDumpExporter.BuildDestructionDump(m, b));
            StringAssert.StartsWith("{\"format\":\"destruction_dump\",\"schema\":1,", a);
            foreach (string key in new[] { "\"m0_kg\"", "\"m_standing_kg\"", "\"piece_count\"", "\"piece_mass_sum_kg\"", "\"energy\"",
                                           "\"dpe_j\"", "\"w_ext_j\"", "\"e_frac_j\"", "\"e_ground_j\"", "\"ke_ej_j\"", "\"transition_checks\"" })
                StringAssert.Contains(key, a);
            // 3 buildings at state >= 3 carry an energy object; the two below do not.
            Assert.AreEqual(3, a.Split(new[] { "\"energy\":{" }, StringSplitOptions.None).Length - 1);
            Assert.AreEqual(5, a.Split(new[] { "\"archetype\":" }, StringSplitOptions.None).Length - 1);
            StringAssert.DoesNotContain("NaN", a);
        }

        [Test]
        public void DestructionDump_RejectsEmptyAndUnregistered()
        {
            var m = new DamageModel();
            Assert.Throws<ArgumentException>(() => DestructionDumpExporter.BuildDestructionDump(m, new List<KeyValuePair<BuildingId, BuildingAttributes>>()));
            var list = new List<KeyValuePair<BuildingId, BuildingAttributes>>
            { new KeyValuePair<BuildingId, BuildingAttributes>(BuildingId.From(BuildingSource.OsmWay, 1), DestructionDumpExporter.Archetypes[0]) };
            Assert.Throws<ArgumentException>(() => DestructionDumpExporter.BuildDestructionDump(m, list));
        }

        [Test]
        public void FragilityDump_SmallN_IsMonotoneAndDeterministic()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => DestructionDumpExporter.BuildFragilityDump(10));
            string a = DestructionDumpExporter.BuildFragilityDump(1000);
            Assert.AreEqual(a, DestructionDumpExporter.BuildFragilityDump(1000));
            StringAssert.Contains("\"state_decreases\":0", a);
            StringAssert.Contains("\"repeat_identical\":true", a);
            StringAssert.Contains("\"hazus_type\":\"W1\"", a);
            StringAssert.Contains("\"hazus_type\":\"C2.M\"", a);
            StringAssert.Contains("\"hazus_type\":\"C2.H\"", a);
        }

        /// <summary>When KAIJU_DUMP_DIR is set, writes real dumps (N = 10,000) there for the Python M-051/M-052 tools; otherwise a no-op.</summary>
        [Test]
        public void WriteDumpsForMetricTools_WhenRequested()
        {
            string dir = Environment.GetEnvironmentVariable("KAIJU_DUMP_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Pass("KAIJU_DUMP_DIR not set");
            System.IO.Directory.CreateDirectory(dir);
            var m = new DamageModel();
            var b = Setup(m, 0, 1, 3, 4, 4, 2, 3, 4, 1, 4);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "destruction_dump.json"), DestructionDumpExporter.BuildDestructionDump(m, b));
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "fragility_dump.json"), DestructionDumpExporter.BuildFragilityDump(10000));
        }
    }
}
