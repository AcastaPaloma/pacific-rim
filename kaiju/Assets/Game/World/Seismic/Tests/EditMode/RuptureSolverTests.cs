using System;
using System.Collections.Generic;
using NUnit.Framework;
using Game.Core;

namespace Game.World.Seismic.Tests
{
    public class RuptureSolverTests
    {
        sealed class Sink : ISeismicEventSink
        {
            public readonly List<object> Events = new List<object>();
            public void Publish<T>(T e) where T : unmanaged, IEvent { Events.Add(e); }
        }

        // A straight east-west fault, vertices every 500 m. Section A: x 0..40 km, section B: x 43..100 km (3 km gap, bridged),
        // plus a far piece at x 110..120 km (10 km gap, not bridged).
        static FaultPiece Piece(string name, string section, double x0, double x1, double z)
        {
            var l = new List<double>();
            for (double x = x0; x <= x1 + 1e-9; x += 500) { l.Add(x); l.Add(z); }
            return new FaultPiece { FaultName = name, Section = section, SlipSense = "right lateral", Xz = l.ToArray() };
        }

        static FaultTraceSet Synthetic(double endKm = 100)
        {
            var pieces = new List<FaultPiece> { Piece("Test fault zone", "A section", 0, 40000, 0), Piece("Test fault zone", "B section", 43000, endKm * 1000, 0) };
            if (endKm >= 100) pieces.Add(Piece("Test fault zone", "C section", 110000, 120000, 0));
            return FaultTraceSet.Build(pieces);
        }

        static QuakeScenario Scenario(FaultSystem s, double hx, uint id = 1, uint seed = 7)
        {
            return new QuakeScenario { EventId = id, Mw = SeismicAssumptions.DefaultMw, FaultId = s.FaultId, HypoX = (float)hx, HypoZ = 500f, HypoDepthM = 8000f, RuptureSpeedMs = 2800f, Seed = seed };
        }

        static DistrictRefs Districts() { return new DistrictRefs { GGX = 20000, GGZ = 5000, SoMaX = 80000, SoMaZ = 9000, TargetGG = true }; }

        [Test]
        public void SystemChainsSectionsAndFlagsBridge()
        {
            FaultSystem s = Synthetic().Systems[0];
            Assert.AreEqual(1, Synthetic().Systems.Count);
            Assert.AreEqual(100000.0, s.LengthM, 1.0);           // 0..100 km, the 3 km gap included
            Assert.AreEqual(1, s.BridgeCount);
            Assert.AreEqual(3000.0, s.BridgedLengthM, 1.0);
            Assert.AreEqual(1, s.UnchainedChains);                // C section is 10 km away
            Assert.AreEqual(3, s.Sections.Length);
            Assert.AreEqual(FaultTraceSet.HashName("Test fault zone"), s.FaultId);
            Assert.AreEqual(SlipSense.RightLateral, s.SlipSense);
        }

        [Test]
        public void RuptureHasDefaultSizeTimingAndSitsOnVertices()
        {
            FaultSystem s = Synthetic().Systems[0];
            RuptureResult r = RuptureSolver.Solve(s, Scenario(s, 50000));
            double want = RuptureScaling.SrlKmFromMw(SeismicAssumptions.DefaultMw);
            Assert.AreEqual(want, r.SrlKm, 0.6);                 // vertex spacing 0.5 km
            Assert.IsFalse(r.ShortenedToSystem);
            Assert.AreEqual(SeismicAssumptions.DefaultMw, r.Mw, 1e-9);
            Assert.LessOrEqual(r.VertexStart, r.HypoVertex);
            Assert.GreaterOrEqual(r.VertexEnd, r.HypoVertex);
            Assert.AreEqual(want * 500.0 / 2800.0, r.EndS, 0.5);  // (SRL/2)/speed ~ 10.7 s
            Assert.AreEqual(0.0, r.VertexTimeS(r.HypoVertex), 0);
            Assert.AreEqual(r.EndS, Math.Max(r.VertexTimeS(r.VertexStart), r.VertexTimeS(r.VertexEnd)), 1e-9);
            Assert.AreEqual(s.SectionOfVertex[r.HypoVertex], r.SectionIdx);
            for (int v = r.VertexStart; v <= r.VertexEnd; v++) Assert.AreEqual(0.0, s.Z[v]);   // every vertex is a polyline vertex (distance 0)
        }

        [Test]
        public void RuptureIsShiftedInsideAtTheEnds()
        {
            FaultSystem s = Synthetic().Systems[0];
            RuptureResult a = RuptureSolver.Solve(s, Scenario(s, 2000));
            Assert.AreEqual(0, a.VertexStart);
            Assert.AreEqual(RuptureScaling.SrlKmFromMw(7.2), a.SrlKm, 0.6);
            RuptureResult b = RuptureSolver.Solve(s, Scenario(s, 99000));
            Assert.AreEqual(s.VertexCount - 1, b.VertexEnd);
            Assert.AreEqual(RuptureScaling.SrlKmFromMw(7.2), b.SrlKm, 0.6);
            Assert.LessOrEqual(b.VertexStart, b.HypoVertex);
        }

        [Test]
        public void SlipHasPeakMdAndMeanAdWithinOnePercent()
        {
            FaultSystem s = Synthetic().Systems[0];
            foreach (double hx in new[] { 50000.0, 30000.0 })    // the 30 km case puts the bridge (40-43 km) inside the range
            {
                RuptureResult r = RuptureSolver.Solve(s, Scenario(s, hx));
                RuptureSize size = RuptureScaling.SizeFromMw(7.2);
                Assert.AreEqual(size.MdM, r.PeakSlipM, size.MdM * 1e-5);
                Assert.AreEqual(size.AdM, r.MeanSlipM, size.AdM * 0.01);
                Assert.AreEqual(size.AdM, RuptureSolver.MeanSlip(s, r.VertexStart, r.Slip), size.AdM * 0.01);
                Assert.AreEqual(r.VertexEnd - r.VertexStart + 1, r.Slip.Length);
            }
        }

        [Test]
        public void BridgedSegmentsCarryNoSlipInTheMean()
        {
            FaultSystem s = Synthetic().Systems[0];
            int b = Array.IndexOf(s.Bridge, true);
            Assert.GreaterOrEqual(b, 0);
            var slip = new double[] { 2, 2, 2 };
            // segment b is bridged: starting at vertex b-1, segments are (b-1,b) normal and (b,b+1) bridge
            double mean = RuptureSolver.MeanSlip(s, b - 1, slip);
            double l0 = s.ArcM[b] - s.ArcM[b - 1], l1 = s.ArcM[b + 1] - s.ArcM[b];
            Assert.AreEqual(2.0 * l0 / (l0 + l1), mean, 1e-12);
        }

        [Test]
        public void SlipIsSeededAndDeterministic()
        {
            FaultSystem s = Synthetic().Systems[0];
            double[] a = RuptureSolver.Solve(s, Scenario(s, 50000, 1, 7)).Slip;
            double[] a2 = RuptureSolver.Solve(s, Scenario(s, 50000, 2, 7)).Slip;
            double[] c = RuptureSolver.Solve(s, Scenario(s, 50000, 1, 8)).Slip;
            CollectionAssert.AreEqual(a, a2);
            CollectionAssert.AreNotEqual(a, c);
            Assert.Less(a[0], 0.2 * RuptureScaling.MaxDisplacementMFromMw(7.2));   // elliptical taper toward the ends
        }

        [Test]
        public void ShortSystemReducesMwNeverStretches()
        {
            FaultSystem s = FaultTraceSet.Build(new[] { Piece("Short fault", "", 0, 30000, 0) }).Systems[0];
            RuptureResult r = RuptureSolver.Solve(s, Scenario(s, 15000));
            Assert.IsTrue(r.ShortenedToSystem);
            Assert.AreEqual(RuptureScaling.MwFromSrlKm(30.0), r.Mw, 1e-9);
            Assert.Less(r.Mw, 7.2);
            Assert.AreEqual(0, r.VertexStart);
            Assert.AreEqual(s.VertexCount - 1, r.VertexEnd);
            Assert.AreEqual(30.0, r.SrlKm, 1e-6);
            Assert.AreEqual(RuptureScaling.MaxDisplacementMFromMw(r.Mw), r.MdM, 1e-12);
        }

        [Test]
        public void SrlOverrideIsHonoured()
        {
            FaultSystem s = Synthetic().Systems[0];
            QuakeScenario sc = Scenario(s, 50000); sc.SrlKmOverride = 84f;
            RuptureResult r = RuptureSolver.Solve(s, sc);
            Assert.AreEqual(84.0, r.SrlKm, 0.6);
            Assert.AreEqual(7.2, r.Mw, 1e-6);
        }

        [Test]
        public void EmergenceIsNearestVertexInRangeAndNeverOnABridge()
        {
            FaultSystem s = Synthetic().Systems[0];
            RuptureResult r = RuptureSolver.Solve(s, Scenario(s, 50000));    // range about 20..80 km, contains the 40-43 km bridge
            DistrictRefs d = Districts(); d.GGX = 41500;                      // reference sits right over the bridge
            EmergenceChoice c = RuptureSolver.ChooseEmergence(r, d);
            Assert.IsTrue(c.Found);
            Assert.GreaterOrEqual(c.VertexIdx, r.VertexStart);
            Assert.LessOrEqual(c.VertexIdx, r.VertexEnd);
            Assert.IsFalse(s.TouchesBridge(c.VertexIdx));
            Assert.AreEqual(39500.0, c.X, 0.0);                               // vertex before the bridge-touching vertex at 40 km
            // brute force nearest among allowed
            double bd = double.MaxValue; int bv = -1;
            for (int v = r.VertexStart; v <= r.VertexEnd; v++) { if (s.TouchesBridge(v)) continue; double dd = Math.Abs(s.X[v] - d.GGX) + Math.Abs(s.Z[v] - d.GGZ); if (dd < bd) { bd = dd; bv = v; } }
            Assert.AreEqual(bv, c.VertexIdx);
            Assert.AreEqual(r.EndS + SeismicAssumptions.ShakingAfterRuptureS + SeismicAssumptions.EmergenceAfterShakingS, c.TimeS, 1e-9);
            Assert.AreEqual(Math.Sqrt(Math.Pow(c.X - d.GGX, 2) + Math.Pow(c.Z - d.GGZ, 2)), c.DistToGGm, 1e-9);
            Assert.AreEqual(Math.Sqrt(Math.Pow(c.X - d.SoMaX, 2) + Math.Pow(c.Z - d.SoMaZ, 2)), c.DistToSoMaM, 1e-9);
            d.TargetGG = false;                                               // SoMa reference at 80 km: end of range side
            Assert.Greater(RuptureSolver.ChooseEmergence(r, d).X, 70000.0);
        }

        [Test]
        public void SequenceEmitsInOrderOnceAndDeterministically()
        {
            FaultTraceSet set = Synthetic();
            FaultSystem s = set.Systems[0];
            QuakeScenario sc = Scenario(s, 50000, 42);
            var sink = new Sink(); var seq = new QuakeSequence(set);
            Assert.IsNotNull(seq.Run(sc, Districts(), sink, 100.0));
            Assert.IsNull(seq.Run(sc, Districts(), sink, 100.0));             // same EventId: nothing more
            Assert.AreEqual(3, sink.Events.Count);
            Assert.IsInstanceOf<QuakeBegan>(sink.Events[0]);
            Assert.IsInstanceOf<FaultRupture>(sink.Events[1]);
            Assert.IsInstanceOf<EmergencePoint>(sink.Events[2]);
            var qb = (QuakeBegan)sink.Events[0]; var fr = (FaultRupture)sink.Events[1]; var ep = (EmergencePoint)sink.Events[2];
            Assert.AreEqual(42u, qb.EventId); Assert.AreEqual(42u, fr.EventId); Assert.AreEqual(42u, ep.EventId);
            Assert.AreEqual(100.0, qb.OriginTimeS);
            Assert.AreEqual(s.FaultId, qb.FaultId); Assert.AreEqual(s.FaultId, fr.FaultId); Assert.AreEqual(s.FaultId, ep.FaultId);
            Assert.AreEqual(0f, fr.StartS);
            Assert.Greater(ep.TimeS, fr.EndS);
            Assert.GreaterOrEqual(ep.VertexIdx, fr.VertexStart); Assert.LessOrEqual(ep.VertexIdx, fr.VertexEnd);
            Assert.AreEqual((byte)SlipSense.RightLateral, fr.SlipSense);

            var sink2 = new Sink(); new QuakeSequence(set).Run(sc, Districts(), sink2, 100.0);
            CollectionAssert.AreEqual(sink.Events, sink2.Events);
            var sink3 = new Sink(); Assert.IsNull(new QuakeSequence(set).Run(new QuakeScenario { EventId = 9, FaultId = 12345 }, Districts(), sink3));
            Assert.AreEqual(0, sink3.Events.Count);
        }
    }
}
