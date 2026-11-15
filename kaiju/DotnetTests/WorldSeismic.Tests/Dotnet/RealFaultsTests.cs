using System;
using System.Linq;
using NUnit.Framework;

namespace Game.World.Seismic.Tests
{
    public class RealFaultsTests
    {
        static FaultTraceSet _set;
        static FaultTraceSet Set { get { return _set ?? (_set = FaultTraceSet.Build(RealFaults.Load())); } }

        [Test]
        public void PrintSystemLengths()
        {
            foreach (FaultSystem s in Set.Systems.OrderByDescending(x => x.LengthM).Take(25))
                Console.WriteLine($"{s.Name} | sections={s.Sections.Length} length={s.LengthM/1000:F1} km mapped={s.MappedLengthM/1000:F1} bridges={s.BridgeCount} bridged={s.BridgedLengthM/1000:F1} unchained={s.UnchainedChains} ({s.UnchainedLengthM/1000:F1} km)");
            foreach (FaultSystem s in Set.Systems.Where(x => x.Name.Contains("San Andreas") || x.Name.Contains("Golden") || x.Name.Contains("Hayward")))
                Console.WriteLine($"* {s.Name} id={s.FaultId} length={s.LengthM/1000:F1} km sections=[{string.Join("; ", s.Sections)}]");
        }

        FaultSystem SanAndreas()
        {
            FaultSystem sa = Set.Systems.Where(x => x.Name.StartsWith("San Andreas")).OrderByDescending(x => x.LengthM).First();
            return sa;
        }

        [Test]
        public void SanAndreasSystemFitsDefaultMw()
        {
            FaultSystem sa = SanAndreas();
            double need = RuptureScaling.SrlKmFromMw(SeismicAssumptions.DefaultMw);
            Assert.GreaterOrEqual(sa.LengthM / 1000.0, 60.0, sa.Name);
            Assert.GreaterOrEqual(sa.LengthM / 1000.0, need);
        }

        [Test]
        public void SystemsAreConsistent()
        {
            foreach (FaultSystem s in Set.Systems)
            {
                Assert.AreEqual(s.VertexCount - 1, s.Bridge.Length, s.Name);
                Assert.AreEqual(s.VertexCount, s.SectionOfVertex.Length);
                for (int i = 0; i < s.Bridge.Length; i++)
                    if (s.Bridge[i]) Assert.LessOrEqual(s.ArcM[i + 1] - s.ArcM[i], FaultTraceSet.MaxBridgeGapM + 1e-6, s.Name);
                Assert.AreSame(s, Set.FindSystem(s.FaultId));
            }
        }

        [Test]
        public void GoldenGateIsItsOwnSystem()
        {
            FaultSystem gg = Set.Systems.FirstOrDefault(x => x.Name.StartsWith("Golden Gate"));
            Assert.IsNotNull(gg);
            Assert.AreNotEqual(SanAndreas().FaultId, gg.FaultId);
            Assert.AreSame(gg, Set.FindSystem(gg.Name));
        }
    
        [Test]
        public void SanAndreasRuptureAtDefaultMwOnMappedVertices()
        {
            FaultSystem sa = SanAndreas();
            int mid = sa.VertexCount / 2;
            var sc = new QuakeScenario { EventId = 1, Mw = SeismicAssumptions.DefaultMw, FaultId = sa.FaultId, HypoX = (float)sa.X[mid], HypoZ = (float)sa.Z[mid], Seed = 3 };
            RuptureResult r = RuptureSolver.Solve(sa, sc);
            Assert.IsFalse(r.ShortenedToSystem);
            Assert.AreEqual(RuptureScaling.SrlKmFromMw(SeismicAssumptions.DefaultMw), r.SrlKm, 0.1 * r.SrlKm);
            Assert.AreEqual(RuptureScaling.MaxDisplacementMFromMw(r.Mw), r.PeakSlipM, 1e-4);
            Assert.AreEqual(r.AdM, r.MeanSlipM, 0.01 * r.AdM);
            Console.WriteLine($"SA rupture: vertices {r.VertexStart}-{r.VertexEnd} srl={r.SrlKm:F1} km section={sa.Sections[r.SectionIdx]} endS={r.EndS:F1}");
        }

        [Test]
        public void GoldenGateSystemShortensMw()
        {
            FaultSystem gg = Set.Systems.First(x => x.Name.StartsWith("Golden Gate"));
            int mid = gg.VertexCount / 2;
            var sc = new QuakeScenario { EventId = 2, Mw = SeismicAssumptions.DefaultMw, FaultId = gg.FaultId, HypoX = (float)gg.X[mid], HypoZ = (float)gg.Z[mid], Seed = 3 };
            RuptureResult r = RuptureSolver.Solve(gg, sc);
            Assert.IsTrue(r.ShortenedToSystem);
            Assert.AreEqual(RuptureScaling.MwFromSrlKm(gg.LengthM / 1000.0), r.Mw, 1e-9);
            Assert.AreEqual(0, r.VertexStart);
            Assert.AreEqual(gg.VertexCount - 1, r.VertexEnd);
        }
    }
}
