using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Game.Units.Mech.Contracts;
using NUnit.Framework;

namespace Game.Units.Mech.Tests
{
    /// <summary>T-201: the named fast walk override (walk_tuning_fast.json, an overlay of only the keys it changes) names real walk-tuning fields, turns walk_mode 2 on, and leaves the shipped defaults (walk_mode 0, the 2.67 m/s step cap of walk_mode 2) untouched.</summary>
    public class FastWalkOverlayTests
    {
        static string Path(string file) => System.IO.Path.Combine(Repo.Root(), "Assets", "Game", "Config", "Mech", file);

        [Test]
        public void EveryOverlayKeyIsAWalkTuningField()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path("walk_tuning_fast.json")));
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (p.Name == "schema" || p.Name == "notes") continue;
                var f = typeof(WalkTuning).GetField(p.Name, BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNotNull(f, "walk_tuning_fast.json key '" + p.Name + "' is not a WalkTuning field (JsonUtility would ignore it silently)");
                Assert.AreEqual(JsonValueKind.Number, p.Value.ValueKind, p.Name);
            }
        }

        [Test]
        public void OverlaySwitchesWalkMode2OnAndTheDefaultStaysOff()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path("walk_tuning_fast.json")));
            Assert.AreEqual(2.0, doc.RootElement.GetProperty("walk_mode").GetDouble());
            Assert.Greater(doc.RootElement.GetProperty("step_length_max_frac").GetDouble(), 0.25);
            var t = Repo.Walk();
            Assert.AreEqual(0.0, t.walk_mode, "the shipped walk_tuning.json keeps walk_mode 0");
            Assert.AreEqual(0.25, t.step_length_max_frac, "the default walk_mode 2 speed cap stays at 2.67 m/s");
            Assert.AreEqual(0.0, t.kin_clear_fast, "new T-201 keys are neutral in the shipped config");
        }
    }
}
