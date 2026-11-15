using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Game.AI.Harness;
using NUnit.Framework;

namespace Game.AI.Tests
{
    public class HarnessTests
    {
        private static string RunLog(HarnessScenario sc, out HarnessResult res)
        {
            var sw = new StringWriter(new StringBuilder()) { NewLine = "\n" };
            res = new KaijuHarness(sc, new BrainLogWriter(sw)).Run();
            return sw.ToString();
        }
        private static string Sha(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

        private static string FindRoot()
        {
            string d = TestContext.CurrentContext.TestDirectory;
            while (d != null && !(Directory.Exists(Path.Combine(d, "Assets")) && Directory.Exists(Path.Combine(d, "DotnetTests")))) d = Path.GetDirectoryName(d);
            return d ?? throw new InvalidOperationException("kaiju root not found");
        }

        [Test]
        public void Determinism_SameSeedSameSha_DifferentSeedDiffers()
        {
            string a = RunLog(new HarnessScenario { DurationSeconds = 120 }, out _);
            string b = RunLog(new HarnessScenario { DurationSeconds = 120 }, out _);
            string c = RunLog(new HarnessScenario { DurationSeconds = 120, Seed = 2 }, out _);
            Assert.AreEqual(Sha(a), Sha(b));
            Assert.AreNotEqual(Sha(a), Sha(c));
        }

        [Test]
        public void LogSchema_HasRequiredFields()
        {
            string log = RunLog(new HarnessScenario { DurationSeconds = 600, Seed = 7, MechRespawnSeconds = 20f }, out _);
            string[] lines = log.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            StringAssert.Contains("\"schema\":\"brain_log@v1\"", lines[0]);
            foreach (string f in new[] { "run_id", "seed", "step_seconds", "brain_period_steps", "brain_id", "config_hash", "size_class", "d_eng_m", "scenario" })
                StringAssert.Contains("\"" + f + "\":", lines[0]);
            var need = new Dictionary<string, string[]>
            {
                ["decision"] = new[] { "step", "t", "ent", "pack", "role", "alive", "x", "y", "heading_rad", "speed", "hp", "state", "prev_state", "reason", "target", "tgt_visible", "tgt_mem_age_s", "i_heading_rad", "i_speed_frac", "i_target", "i_action", "score_cur", "score_best" },
                ["target_sample"] = new[] { "step", "t", "ent", "tkind", "alive", "x", "y", "heading_rad", "hp" },
                ["hit"] = new[] { "step", "attacker", "victim", "damage", "victim_hp_after" },
                ["death"] = new[] { "step", "ent" },
            };
            var seen = new HashSet<string>();
            foreach (string l in lines.Skip(1))
            {
                string kind = need.Keys.FirstOrDefault(k => l.StartsWith("{\"kind\":\"" + k + "\"", StringComparison.Ordinal));
                Assert.NotNull(kind, l);
                seen.Add(kind);
                foreach (string f in need[kind]) StringAssert.Contains("\"" + f + "\":", l);
                StringAssert.DoesNotContain("\r", l);
            }
            CollectionAssert.AreEquivalent(need.Keys, seen);
        }

        [Test]
        public void ThreeVsFour_TenMinutes_NoExceptions_And_ReportsPackEvents()
        {
            var sc = new HarnessScenario { Name = "3v4", Seed = 7, MechRespawnSeconds = 20f };
            string log = RunLog(sc, out HarnessResult res);
            Assert.AreEqual(30000, res.Steps);
            Assert.Greater(res.Decisions, 5000);
            Assert.Greater(res.Hits, 0);
            Assert.Greater(res.Deaths, 0);
            PackReport rep = PackMetrics.Compute(log.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            TestContext.Out.WriteLine($"commit={rep.CommitEvents} multi={rep.CommitEventsMulti} flanked={rep.CommitEventsFlanked} FRcommit={rep.FrCommit:F3} FRtime={rep.FrTimeFraction:F3} " +
                $"FF={rep.MeanFf:F3} chance={rep.MeanFfChance:F3} FE={rep.FocusExcess:F3} ffTicks={rep.FfTicks} vol={rep.VoluntarySwitches} thrash={rep.Thrash} leaks={rep.Leaks} hits={rep.Hits} deaths={rep.Deaths}");
            foreach (string dg in rep.CommitDiag) TestContext.Out.WriteLine(dg);
            Assert.AreEqual(0, rep.Leaks);
            Assert.GreaterOrEqual(rep.CommitEventsFlanked, 1, "no flank commit event in 3v4 run (report: FSM does not produce it)");
            Assert.Greater(rep.FfTicks, 0, "no focus-fire evaluation ticks");
            Assert.Greater(rep.MeanFf, 0.5, "no focus fire observed");
        }

        [Test]
        public void M007_Scenario_FrCommit_MeetsC1Bar_AndBeatsDirectChargeNull()
        {
            int[] tot = new int[4]; // [scripted multi, flanked, null multi, null flanked]
            for (ulong seed = 1; seed <= 12; seed++)
                for (int d = 0; d < 2; d++)
                {
                    string log = RunLog(HarnessScenario.M007(seed, d == 1), out _);
                    PackReport rep = PackMetrics.Compute(log.Split('\n', StringSplitOptions.RemoveEmptyEntries));
                    tot[d * 2] += rep.CommitEventsMulti; tot[d * 2 + 1] += rep.CommitEventsFlanked;
                }
            Assert.Greater(tot[0], 8, "too few multi-kaiju commit events");
            double fr = (double)tot[1] / tot[0], nul = tot[2] > 0 ? (double)tot[3] / tot[2] : 0.0;
            TestContext.Out.WriteLine($"FR_commit {fr:F3} null {nul:F3}");
            Assert.GreaterOrEqual(fr, 0.60);
            Assert.GreaterOrEqual(fr, 2 * nul);
        }

        [Test]
        public void ThreeVsFourCombat_KaijuTakeDamage_RetreatReachable_Deterministic()
        {
            long hits = 0, deaths = 0, retreats = 0, withdrawn = 0; string first = null;
            for (ulong seed = 1; seed <= 8; seed++)
            {
                string log = RunLog(HarnessScenario.M007ThreeVsFour(seed, false), out HarnessResult r);
                if (first == null) first = Sha(log);
                hits += r.KaijuHits; deaths += r.KaijuDeaths; retreats += r.KaijuRetreats; withdrawn += r.KaijuWithdrawn;
                Assert.GreaterOrEqual(r.KaijuDeaths, 0);
            }
            TestContext.Out.WriteLine($"kaijuHits={hits} kaijuDeaths={deaths} retreats={retreats} withdrawn={withdrawn}");
            Assert.Greater(hits, 0, "mechs never hit a kaiju");
            Assert.Greater(retreats, 0, "no kaiju ever entered Retreat from its own low-HP rule");
            Assert.AreEqual(first, Sha(RunLog(HarnessScenario.M007ThreeVsFour(1, false), out _)));
        }

        [Test]
        public void ObservationAllies_AreRangeFiltered()
        {
            var sc = HarnessScenario.M007ThreeVsFour(3, false); sc.Brain.AllyAwarenessM = 1f; // nobody is within 1 m
            string log = RunLog(sc, out _);
            Assert.That(log.Contains("\"role\":\"wing\"") || log.Contains("\"role\":\"centre\""), Is.False, "allies beyond awareness range reached the observation");
        }

        [Test, Explicit("Runs M-007 scenario over N seeds for scripted and DirectCharge; prints FR_commit; writes logs when KAIJU_M007_OUT is set")]
        public void M007_Scenario_Sweep()
        {
            string outDir = Environment.GetEnvironmentVariable("KAIJU_M007_OUT");
            int n = int.Parse(Environment.GetEnvironmentVariable("KAIJU_M007_N") ?? "20");
            string scen = Environment.GetEnvironmentVariable("KAIJU_M007_SCENARIO"); bool threeVsFour = scen == "3v4" || scen == "churn";
            foreach (int mode in new[] { 0, 1, 2 }) // 0 scripted, 1 directcharge null, 2 nofocus (WeightFocus 0) control
            {
                bool direct = mode == 1; bool nofocus = mode == 2;
                if (nofocus && Environment.GetEnvironmentVariable("KAIJU_M007_NOFOCUS") != "1") continue;
                int c = 0, m = 0, f = 0;
                for (ulong seed = 1; seed <= (ulong)n; seed++)
                {
                    HarnessScenario scn = scen == "churn" ? HarnessScenario.M007Churn(seed, direct) : threeVsFour ? HarnessScenario.M007ThreeVsFour(seed, direct) : HarnessScenario.M007(seed, direct);
                    if (nofocus) scn.Brain.WeightFocus = 0f; // second control: scripted without the focus bonus (brain_id nofocus@1)
                    string log = RunLog(scn, out _);
                    if (outDir != null)
                    {
                        string d = Path.Combine(outDir, direct ? "directcharge" : (nofocus ? "nofocus" : "scripted")); Directory.CreateDirectory(d);
                        File.WriteAllText(Path.Combine(d, "brain_log_m007_" + seed + ".jsonl"), log, new UTF8Encoding(false));
                    }
                    PackReport rep = PackMetrics.Compute(log.Split('\n', StringSplitOptions.RemoveEmptyEntries));
                    c += rep.CommitEvents; m += rep.CommitEventsMulti; f += rep.CommitEventsFlanked;
                }
                TestContext.Out.WriteLine($"M007 mode={mode} n={n} commits={c} multi={m} flanked={f} FR={(m > 0 ? (double)f / m : double.NaN):F3}");
            }
        }

        [Test, Explicit("Writes kaiju/evidence/T-032/brain_log_sample.jsonl and m058_tick_cost.json")]
        public void WriteEvidence()
        {
            string dir = Path.Combine(FindRoot(), "evidence", "T-032"); Directory.CreateDirectory(dir);
            var sc0 = new HarnessScenario { Name = "3v4", Seed = 7, MechRespawnSeconds = 20f };
            string log0 = RunLog(sc0, out _);
            File.WriteAllText(Path.Combine(dir, "brain_log_sample.jsonl"),
                string.Concat(log0.Split('\n').Take(200).Select(l => l + "\n")), new UTF8Encoding(false));

            // warm-up then measured run
            RunLog(new HarnessScenario { Seed = 99, DurationSeconds = 30, MechRespawnSeconds = 20f, MeasureTiming = true }, out _);
            var sc = new HarnessScenario { Name = "3v4", Seed = 7, MechRespawnSeconds = 20f, MeasureTiming = true };
            RunLog(sc, out HarnessResult r);
            double us = 1e6 / Stopwatch.Frequency;
            double[] calls = r.DecideTicks.Select(t => t * us).OrderBy(v => v).ToArray();
            double[] ticks = r.TickTotals.Select(t => t * us).OrderBy(v => v).ToArray();
            double P(double[] a, double p) => a[Math.Min(a.Length - 1, (int)Math.Ceiling(p * a.Length) - 1)];
            double meanCall = calls.Average(), meanTick = ticks.Average(), p99Call = P(calls, 0.99), p99Tick = P(ticks, 0.99);
            const double budgetUs = 20000.0;
            var inv = CultureInfo.InvariantCulture;
            string J(double v) => v.ToString("R", inv);
            string json = "{\n" +
                "  \"metric\": \"M-058@v1 (harness variant)\",\n" +
                "  \"scenario\": \"3 kaiju vs 4 mechs, 600 s simulated, step 0.02 s, brain period 10 steps, mech respawn 20 s, seed 7\",\n" +
                "  \"host\": \"" + System.Runtime.InteropServices.RuntimeInformation.OSDescription + " / " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + " (dotnet test, not Unity/IL2CPP)\",\n" +
                "  \"host_budget_ms\": 20,\n" +
                "  \"steps\": " + r.Steps + ",\n  \"decide_calls\": " + calls.Length + ",\n" +
                "  \"per_decide_us\": { \"mean\": " + J(meanCall) + ", \"p50\": " + J(P(calls, 0.5)) + ", \"p99\": " + J(p99Call) + ", \"max\": " + J(calls[^1]) + " },\n" +
                "  \"per_tick_us\": { \"mean\": " + J(meanTick) + ", \"p99\": " + J(p99Tick) + ", \"max\": " + J(ticks[^1]) + ", \"note\": \"per 0.02 s step, sum of Decide calls in that step (all steps counted, incl. zero-call steps)\" },\n" +
                "  \"share_of_20ms_budget\": { \"mean_tick\": " + J(meanTick / budgetUs) + ", \"p99_tick\": " + J(p99Tick / budgetUs) + ", \"max_tick\": " + J(ticks[^1] / budgetUs) + " },\n" +
                "  \"alloc_bytes_in_decide_total\": " + r.AllocatedBytesInDecide + "\n}\n";
            File.WriteAllText(Path.Combine(dir, "m058_tick_cost.json"), json, new UTF8Encoding(false));
            TestContext.Out.WriteLine(json);
        }
    }
}
