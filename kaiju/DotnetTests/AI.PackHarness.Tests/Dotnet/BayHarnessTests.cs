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

        // T-115e: positions are simulated in a local frame, so translating the whole scenario (integer metres, large BayTM-scale offsets) must not change anything but the logged x/y.
        static string RunShifted(string scenario, string comp, ulong seed, string brain, double duration, double dx, double dy)
        {
            var w = new StringWriter();
            new BayHarness(new BayScenario { Name = scenario, Composition = comp, Seed = seed, BrainId = brain, DurationSeconds = duration, Layout = BayLayout.Synthetic.Translated(dx, dy) }, w).Run();
            return w.ToString();
        }

        static string StripXY(string log) => System.Text.RegularExpressions.Regex.Replace(log, "\"(x|y)\":[-0-9.eE+]+", "\"$1\":0");

        [Test]
        public void Translation_NoFocusControlAndCandidateAreInvariant()
        {
            foreach (string b in new[] { BayBrains.NoFocus, BayBrains.Candidate })
                foreach (ulong seed in new ulong[] { 1, 2, 7 })
                    foreach (var d in new[] { (60000.0, 0.0), (0.0, 100000.0), (-5000.0, 90000.0) })
                    {
                        string a = Run("hunt", "LLMH", seed, b, 300), s = RunShifted("hunt", "LLMH", seed, b, 300, d.Item1, d.Item2);
                        Assert.AreEqual(StripXY(a), StripXY(s), b + " seed " + seed + " shift " + d);
                        // logged positions agree to float32 resolution of the absolute coordinate
                        var ra = System.Text.RegularExpressions.Regex.Matches(a, "\"x\":([-0-9.eE+]+)"); var rs = System.Text.RegularExpressions.Regex.Matches(s, "\"x\":([-0-9.eE+]+)");
                        Assert.AreEqual(ra.Count, rs.Count);
                        for (int i = 0; i < ra.Count; i += 37)
                            Assert.AreEqual(double.Parse(ra[i].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) + d.Item1, double.Parse(rs[i].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), 0.02);
                    }
        }

        [Test]
        public void Translation_EmergeRaidMixedAreInvariant()
        {
            foreach (string sc in new[] { "raid", "mixed", "emerge" })
                Assert.AreEqual(StripXY(Run(sc, sc == "emerge" ? "mix" : "LLH", 3, BayBrains.Candidate, 400)), StripXY(RunShifted(sc, sc == "emerge" ? "mix" : "LLH", 3, BayBrains.Candidate, 400, 70000, 20000)), sc);
        }

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

        // T-115c: the water map (synthetic depth / soft-ground field)
        static BayResult RunMap(string scenario, string comp, ulong seed, bool plan, double duration, out string log)
        {
            var w = new StringWriter();
            var r = new BayHarness(new BayScenario { Name = scenario, Composition = comp, Seed = seed, DurationSeconds = duration, Map = "water", PlanRoutes = plan }, w).Run();
            log = w.ToString(); return r;
        }

        [Test]
        public void WaterMap_IsDeterministic_AndTheHeaderNamesTheMap()
        {
            RunMap("mixed", "LLH", 2, true, 300, out string a); RunMap("mixed", "LLH", 2, true, 300, out string b);
            Assert.AreEqual(a, b);
            Assert.That(a, Does.Contain("\"map\":\"m007_bay_synth@v1+water\""));
            Run("hunt", "LLH", 2, duration: 30);
        }

        [Test]
        public void CrossScenario_PlannedRoutesReachTheFarShore_StraightLinesStopAtTheChannel()
        {
            foreach (string comp in new[] { "LL", "MH" })
            {
                var planned = RunMap("cross", comp, 1, true, 6000, out string logP);
                var straight = RunMap("cross", comp, 1, false, 6000, out string logS);
                Assert.AreEqual(0, planned.BlockedSteps, comp);
                Assert.AreEqual(1, planned.InfraDestroyed, comp + ": the pack walks round the channel and razes the bridge");
                Assert.Greater(straight.BlockedSteps, 100000, comp + ": control stands at the shore");
                Assert.AreEqual(0, straight.InfraDestroyed, comp);
            }
        }

        [Test]
        public void FlatMap_IsUnchangedByTheRoutePlanningFlag()
        {
            var cfg = Game.AI.Pack.PackConfig.Bay(); cfg.RoutePlanning = false;
            var w1 = new StringWriter(); var w2 = new StringWriter();
            new BayHarness(new BayScenario { Name = "hunt", Composition = "LLH", Seed = 3, DurationSeconds = 200, Pack = cfg }, w1).Run();
            new BayHarness(new BayScenario { Name = "hunt", Composition = "LLH", Seed = 3, DurationSeconds = 200 }, w2).Run();
            // the header hash covers the config fields it lists (RoutePlanning is not among them), so the logs are byte-equal
            Assert.AreEqual(w1.ToString(), w2.ToString());
        }
    }
}
