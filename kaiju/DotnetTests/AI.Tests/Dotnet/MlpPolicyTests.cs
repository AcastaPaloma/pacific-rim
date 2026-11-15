using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Game.AI.Policy;
using NUnit.Framework;

namespace Game.AI.Tests
{
    /// <summary>T-041b step 2: C# MLP evaluator parity with PyTorch (fixture from tests/make_parity_fixture.py), determinism, masking, M-058 probe.</summary>
    public class MlpPolicyTests
    {
        private static string Fx(string n) => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", n);

        private static (float[][] obs, float[][] logits) LoadJson()
        {
            using (var d = JsonDocument.Parse(File.ReadAllText(Fx("mlp_parity.json"))))
            {
                float[][] Read(string k)
                {
                    var rows = d.RootElement.GetProperty(k); var o = new float[rows.GetArrayLength()][]; int i = 0;
                    foreach (var r in rows.EnumerateArray()) { o[i] = new float[r.GetArrayLength()]; int j = 0; foreach (var v in r.EnumerateArray()) o[i][j++] = v.GetSingle(); i++; }
                    return o;
                }
                return (Read("obs"), Read("logits"));
            }
        }

        private static byte[] FullMask() { var m = new byte[PolicyLayout.MaskDim]; for (int i = 0; i < m.Length; i++) m[i] = 1; return m; }

        [Test]
        public void LogitsMatchPyTorchWithin1e4()
        {
            var net = MlpPolicyNetwork.Load(Fx("mlp_parity.kpw"));
            Assert.AreEqual("parity@1", net.NetworkId);
            Assert.AreEqual(58880, net.Macs);
            var (obs, logits) = LoadJson();
            var o = new float[PolicyLayout.MaskDim]; float worst = 0f;
            for (int r = 0; r < obs.Length; r++)
            {
                net.Forward(obs[r], o);
                for (int k = 0; k < o.Length; k++) worst = Math.Max(worst, Math.Abs(o[k] - logits[r][k]));
            }
            Assert.LessOrEqual(worst, 1e-4f, "max |dlogit|");
        }

        [Test]
        public void GreedyIsMaskedArgmaxAndRespectsConditionalMask()
        {
            var net = MlpPolicyNetwork.Load(Fx("mlp_parity.kpw"), greedy: true);
            net.Reset(1);
            var (obs, _) = LoadJson();
            var mask = FullMask();
            for (int i = 0; i < PolicyLayout.NTargetChoices - 1; i++) mask[PolicyLayout.MaskTargetOffset + 1 + i] = 0; // only "none" allowed
            var a = new PolicyAction();
            for (int r = 0; r < obs.Length; r++)
            {
                net.Act(obs[r], mask, ref a);
                Assert.AreEqual(0, a.Target);
                Assert.IsFalse(PolicyLayout.NeedsTarget((Maneuver)a.Maneuver), "target-needing maneuver with no target");
                var lg = net.LastLogits;
                int best = -1; float bv = float.NegativeInfinity;
                for (int m = 0; m < PolicyLayout.NManeuvers; m++)
                    if (PolicyLayout.ManeuverAllowed(0, (Maneuver)m, mask) && lg[PolicyLayout.MaskManeuverOffset + m] > bv) { bv = lg[PolicyLayout.MaskManeuverOffset + m]; best = m; }
                Assert.AreEqual(best, a.Maneuver);
            }
        }

        [Test]
        public void SeededSamplingIsDeterministicAndVaries()
        {
            var (obs, _) = LoadJson();
            var mask = FullMask();
            int[] Run(ulong seed)
            {
                var net = MlpPolicyNetwork.Load(Fx("mlp_parity.kpw")); net.Reset(seed);
                var res = new int[obs.Length * 3 * 8]; var a = new PolicyAction(); int n = 0;
                for (int rep = 0; rep < 8; rep++)
                    for (int r = 0; r < obs.Length; r++) { net.Act(obs[r], mask, ref a); res[n++] = a.Target; res[n++] = a.Maneuver; res[n++] = a.Speed; }
                return res;
            }
            CollectionAssert.AreEqual(Run(5), Run(5));
            CollectionAssert.AreNotEqual(Run(5), Run(6));
        }

        [Test]
        public void ActAllocatesNothingAndMeetsM058Probe()
        {
            var net = MlpPolicyNetwork.Load(Fx("mlp_parity.kpw")); net.Reset(3);
            var (obs, _) = LoadJson(); var mask = FullMask(); var a = new PolicyAction();
            for (int i = 0; i < 30000; i++) net.Act(obs[i % obs.Length], mask, ref a); // warm-up (JIT)
            long z0 = GC.GetAllocatedBytesForCurrentThread(); long z1 = GC.GetAllocatedBytesForCurrentThread(); long b0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 5000; i++) net.Act(obs[i % obs.Length], mask, ref a);
            long used = GC.GetAllocatedBytesForCurrentThread() - b0 - (z1 - z0);
            Assert.LessOrEqual(used, 0L, "allocation in Act (5000 calls)");
            const int N = 20000; var t = new double[N]; double f = 1e6 / Stopwatch.Frequency;
            // Best of 5 rounds: the machine runs Unity batches and other agents; scheduler pre-emption inflates single-round tails.
            double p99 = double.MaxValue, p50 = 0;
            for (int round = 0; round < 5; round++)
            {
                for (int i = 0; i < N; i++) { long s = Stopwatch.GetTimestamp(); net.Act(obs[i % obs.Length], mask, ref a); t[i] = (Stopwatch.GetTimestamp() - s) * f; }
                Array.Sort(t);
                if (t[(int)(N * 0.99)] < p99) { p99 = t[(int)(N * 0.99)]; p50 = t[N / 2]; }
            }
            TestContext.Out.WriteLine($"M-058 probe: MlpPolicyNetwork.Act p50 {p50:F1} us, p99 {p99:F1} us (limit 50, best of 5 rounds)");
            // A Debug test build is unoptimised (Vector<T> unvectorised); the budget applies to optimised code only (run with -c Release).
            var dbg = (DebuggableAttribute)Attribute.GetCustomAttribute(typeof(MlpPolicyNetwork).Assembly, typeof(DebuggableAttribute));
            bool optimised = dbg == null || !dbg.IsJITOptimizerDisabled;
            if (optimised) Assert.LessOrEqual(p99, 50.0, "per-call p99 us");
            else TestContext.Out.WriteLine("Debug build: p99 budget not asserted");
        }

        [Test]
        public void RejectsBadFiles()
        {
            Assert.Throws<InvalidDataException>(() => MlpPolicyNetwork.Load(new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })));
        }
    }
}
