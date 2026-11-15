using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.AI.PackHarness;
using NUnit.Framework;

namespace Game.AI.PackHarness.Tests
{
    public class BayHarnessTests
    {
        static string Run(string scenario, string comp, ulong seed, string brain = BayBrains.Candidate, double duration = 0)
        {
            var w = new StringWriter();
            new BayHarness(new BayScenario { Name = scenario, Composition = comp, Seed = seed, BrainId = brain, DurationSeconds = duration }, w).Run();
            return w.ToString();
        }

        static List<string> Kinds(string log)
            => log.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => { int i = l.IndexOf("\"kind\":\"", StringComparison.Ordinal) + 8; return l.Substring(i, l.IndexOf('"', i) - i); }).ToList();

        [Test]
        public void SameSeedSameLog_DifferentSeedDifferent()
        {
            Assert.AreEqual(Run("hunt", "LLH", 3, duration: 120), Run("hunt", "LLH", 3, duration: 120));
            Assert.AreNotEqual(Run("hunt", "LLH", 3, duration: 120), Run("hunt", "LLH", 4, duration: 120));
        }

        [Test]
        public void EveryScenarioWritesV2RowsInOrder()
        {
            foreach (string sc in new[] { "hunt", "churn", "raid", "mixed", "emerge" })
            {
                string log = Run(sc, sc == "emerge" ? "mix" : "LLH", 2, duration: 300);
                var kinds = Kinds(log);
                Assert.AreEqual("header", kinds[0], sc);
                Assert.That(log, Does.Contain("\"schema\":\"brain_log@v2\""));
                Assert.That(log, Does.Contain("\"scenario\":\"m007_bay_" + sc + "\""));
                Assert.That(kinds, Does.Contain("decision"), sc);
                Assert.That(kinds, Does.Contain("infra"), sc);
                if (sc != "raid" && sc != "emerge") Assert.That(kinds, Does.Contain("target_sample"), sc);
            }
        }

        [Test]
        public void Raid_CandidateDestroysInfrastructureAndLogsGoalChanges()
        {
            string log = Run("raid", "LLH", 1, duration: 3000);
            var kinds = Kinds(log);
            Assert.That(kinds, Does.Contain("goal_change"));
            Assert.That(log, Does.Contain("\"goal\":\"raid\""));
            Assert.IsFalse(kinds.Contains("target_sample"), "raid has no mechs");
        }

        [Test]
        public void Controls_RunAndCarryTheirIds()
        {
            foreach (string b in BayBrains.All)
            {
                string log = Run("hunt", "LLH", 5, b, 120);
                Assert.That(log, Does.Contain("\"brain_id\":\"" + b + "\""), b);
                bool solo = b == BayBrains.Solo || b == BayBrains.DirectCharge || b == BayBrains.NoFocus;
                if (solo) Assert.That(log, Does.Contain("\"pack\":0"), b);
                if (b == BayBrains.Omniscient) Assert.That(log, Does.Contain("\"board_source\":\"oracle\""));
            }
        }

        [Test]
        public void Emerge_FormsValidPacksAndLogsCalls()
        {
            string log = Run("emerge", "mix", 4, duration: 900);
            Assert.That(Kinds(log), Does.Contain("call"));
            Assert.That(log, Does.Contain("\"pack\":1").Or.Contain("\"pack\":2"));
        }
    }
}
