// SeismicModule / QuakeDirector / bridge integration and the M-053, M-054 check shapes (T-027 step 4). Engine-free NUnit.
using System;
using System.Collections.Generic;
using Game.Core;
using Game.World.Destruction;
using NUnit.Framework;

namespace Game.World.Seismic.Tests
{
    public class SeismicModuleTests
    {
        sealed class NullErrors : ICoreErrorSink { public void Report(CoreError error) { } }

        sealed class Registry : IServiceRegistry
        {
            readonly Dictionary<Type, object> _map = new Dictionary<Type, object>();
            public void Register<T>(T implementation) where T : class { _map.Add(typeof(T), implementation); }
            public T Resolve<T>() where T : class { return (T)_map[typeof(T)]; }
            public bool TryResolve<T>(out T implementation) where T : class { bool ok = _map.TryGetValue(typeof(T), out var o); implementation = ok ? (T)o : null; return ok; }
            public bool IsRegistered<T>() where T : class { return _map.ContainsKey(typeof(T)); }
            public int Count { get { return _map.Count; } }
        }

        sealed class Recorder : IEventHandler<QuakeBegan>, IEventHandler<FaultRupture>, IEventHandler<EmergencePoint>
        {
            public readonly List<string> Order = new List<string>();
            public FaultRupture Rupture; public EmergencePoint Emergence; public int Ruptures, Emergences;
            public void Handle(in QuakeBegan e) { Order.Add("QuakeBegan"); }
            public void Handle(in FaultRupture e) { Order.Add("FaultRupture"); Rupture = e; Ruptures++; }
            public void Handle(in EmergencePoint e) { Order.Add("EmergencePoint"); Emergence = e; Emergences++; }
        }

        static FeatureFlagSnapshot Flags(bool destruction, bool seismic)
        {
            return new FeatureFlagSnapshot(new[]
            {
                new KeyValuePair<string, bool>(DestructionFlags.Enabled, destruction),
                new KeyValuePair<string, bool>(SeismicFlags.Enabled, seismic),
            });
        }

        // East-west fault, vertices every 500 m: section A x 0..40 km, B x 43..100 km (3 km gap, bridged).
        static FaultPiece Piece(string section, double x0, double x1, double z)
        {
            var l = new List<double>();
            for (double x = x0; x <= x1 + 1e-9; x += 500) { l.Add(x); l.Add(z); }
            return new FaultPiece { FaultName = "Test fault zone", Section = section, SlipSense = "right lateral", Xz = l.ToArray() };
        }

        static FaultTraceSet Faults() { return FaultTraceSet.Build(new List<FaultPiece> { Piece("A section", 0, 40000, 0), Piece("B section", 43000, 100000, 0) }); }
        static DistrictRefs Districts() { return new DistrictRefs { GGX = 20000, GGZ = 5000, SoMaX = 80000, SoMaZ = 9000, TargetGG = true }; }

        static QuakeScenario Scenario(FaultSystem s, uint id = 1, uint seed = 7, float mw = SeismicAssumptions.DefaultMw)
        {
            return new QuakeScenario { EventId = id, Mw = mw, FaultId = s.FaultId, HypoX = 55000f, HypoZ = 500f, HypoDepthM = 8000f, RuptureSpeedMs = 2800f, Seed = seed };
        }

        static BuildingId Id(ulong n) { return BuildingId.From(BuildingSource.OsmWay, n); }

        sealed class World
        {
            public EventBus Bus = new EventBus(new NullErrors(), 10000);
            public Registry Reg = new Registry();
            public Recorder Rec = new Recorder();
            public QuakeDirector Director;
            public DamageModel Model;
            public SeismicShakingBridge Bridge;
            public const int Buildings = 120;

            public World(bool destruction, bool seismic)
            {
                Reg.Register<IEventBus>(Bus);
                Bus.Subscribe<QuakeBegan>(Rec); Bus.Subscribe<FaultRupture>(Rec); Bus.Subscribe<EmergencePoint>(Rec);
                var flags = Flags(destruction, seismic);
                Model = new DamageModel(Bus);
                var attrs = new BuildingAttributes { HeightM = 25, Levels = 6, FootprintM2 = 600 };
                for (ulong i = 1; i <= Buildings; i++) Model.Register(Id(i), attrs);
                Director = SeismicModule.Install(Reg, flags, Faults(), Districts());
                if (Director == null) return;
                Bridge = SeismicShakingBridge.TryCreate(flags, Bus, Model, Director.Field, Director.Vs30.Vs30At);
                if (Bridge == null) return;
                for (ulong i = 1; i <= Buildings; i++) Bridge.AddBuilding(Id(i), 40000 + i * 100.0, 200.0 + (i % 7) * 150.0);
            }

            public int Damaged()
            {
                int n = 0;
                for (ulong i = 1; i <= Buildings; i++) { Model.TryGetState(Id(i), out var st); if (st.State > 0) n++; }
                return n;
            }

            public string Bytes() { var sys = new SaveSystem("t"); sys.Register(new DestructionSaveParticipant(Model)); return SaveCodec.Serialize(sys.Capture()); }
        }

        [Test]
        public void FlagOff_InstallsNothing_NoEvents()
        {
            var w = new World(true, false);
            Assert.IsNull(w.Director);
            Assert.IsFalse(w.Reg.IsRegistered<QuakeDirector>());
            Assert.IsFalse(w.Reg.IsRegistered<IShakingField>());
            Assert.IsFalse(w.Reg.IsRegistered<Vs30Provider>());
            Assert.AreEqual(1, w.Reg.Count);                         // only the bus the test put there
            Assert.AreEqual(0, w.Rec.Order.Count);
            Assert.IsNull(SeismicModule.Install(null, Flags(true, true), Faults(), Districts()));
            Assert.IsNull(SeismicModule.Install(w.Reg, null, Faults(), Districts()));
            Assert.IsNull(SeismicModule.Install(new Registry(), new FeatureFlagSnapshot(null), Faults(), Districts()));
        }

        [Test]
        public void FlagOn_EventsInOrder_OncePerEventId()
        {
            var w = new World(false, true);
            Assert.IsNotNull(w.Director);
            Assert.AreSame(w.Director, w.Reg.Resolve<QuakeDirector>());
            Assert.AreSame(w.Director.Field, w.Reg.Resolve<IShakingField>());
            FaultSystem s = w.Director.Faults.Systems[0];
            Assert.IsNotNull(w.Director.Run(Scenario(s)));
            CollectionAssert.AreEqual(new[] { "QuakeBegan", "FaultRupture", "EmergencePoint" }, w.Rec.Order);
            Assert.IsNull(w.Director.Run(Scenario(s)));              // same EventId: nothing more
            Assert.AreEqual(3, w.Rec.Order.Count);
            Assert.AreEqual(1, w.Rec.Ruptures); Assert.AreEqual(1, w.Rec.Emergences);
            Assert.IsNotNull(w.Director.Run(Scenario(s, 2)));
            Assert.AreEqual(6, w.Rec.Order.Count);
            Assert.IsNull(w.Director.Run(new QuakeScenario { EventId = 3, FaultId = 12345u, Mw = 7.2f }));   // unknown fault: nothing
            Assert.AreEqual(6, w.Rec.Order.Count);
            ShakingSample smp;
            Assert.IsTrue(w.Director.Field.TryGetShaking(1, 50000, 1000, 760, out smp));
            Assert.Greater(smp.PgaG, 0f);
        }

        [Test]
        public void BuildingStatesChange_OnlyWithBothFlagsOn()
        {
            var on = new World(true, true);
            Assert.IsNotNull(on.Bridge);
            on.Director.Run(Scenario(on.Director.Faults.Systems[0]));
            Assert.Greater(on.Damaged(), 0, "buildings 1 km from a Mw 7.2 rupture must show damage");

            var noSeismic = new World(true, false);
            Assert.IsNull(noSeismic.Bridge);
            Assert.AreEqual(0, noSeismic.Damaged());

            var noDestruction = new World(false, true);
            Assert.IsNotNull(noDestruction.Director);
            Assert.IsNull(noDestruction.Bridge);
            noDestruction.Director.Run(Scenario(noDestruction.Director.Faults.Systems[0]));
            Assert.AreEqual(0, noDestruction.Damaged());
        }

        [Test]
        public void SameSeedTwice_IdenticalDamageBytes()
        {
            string a = RunBytes(7), b = RunBytes(7);
            Assert.AreEqual(a, b);
            Assert.IsTrue(a.Length > 100);
        }

        static string RunBytes(uint seed)
        {
            var w = new World(true, true);
            w.Director.Run(Scenario(w.Director.Faults.Systems[0], 1, seed));
            return w.Bytes();
        }

        // ---- M-053 shape: rupture on the mapped trace, emergence on a mapped vertex ----

        [Test]
        public void M053_RuptureVerticesLieOnMappedTrace_EmergenceOnMappedVertex()
        {
            var w = new World(false, true);
            FaultSystem s = w.Director.Faults.Systems[0];
            RuptureResult r = w.Director.Run(Scenario(s));
            double maxD = 0;
            for (int v = r.VertexStart; v <= r.VertexEnd; v++) maxD = Math.Max(maxD, DistToMappedTrace(w.Director.Faults, s.X[v], s.Z[v]));
            Assert.AreEqual(0.0, maxD, 1e-6, "max rupture vertex distance from the mapped trace (m)");
            Assert.AreEqual(w.Rec.Rupture.VertexStart, r.VertexStart);
            Assert.AreEqual(w.Rec.Rupture.VertexEnd, r.VertexEnd);
            Assert.IsNotNull(w.Director.Faults.FindSystem(w.Rec.Rupture.FaultId), "FaultId maps to a source fault");

            EmergencePoint e = w.Rec.Emergence;
            Assert.IsTrue(e.VertexIdx >= r.VertexStart && e.VertexIdx <= r.VertexEnd);
            Assert.AreEqual(s.X[e.VertexIdx], e.X, 0.0); Assert.AreEqual(s.Z[e.VertexIdx], e.Z, 0.0);
            Assert.IsFalse(s.TouchesBridge(e.VertexIdx), "emergence vertex is on mapped (not bridged) trace");
            Assert.AreEqual(0.0, DistToMappedTrace(w.Director.Faults, e.X, e.Z), 1e-6);
        }

        static double DistToMappedTrace(FaultTraceSet set, double x, double z)
        {
            double best = double.MaxValue;
            foreach (FaultTrace t in set.Traces)
                for (int c = 0; c < t.ChainCount; c++)
                    for (int i = t.ChainStart[c]; i < t.ChainEnd(c); i++)
                        best = Math.Min(best, ShakingField.PointSegment(t.X[i], t.Z[i], t.X[i + 1], t.Z[i + 1], x, z));
            return best;
        }

        // ---- M-054 shape: Mw / SRL / MD / AD consistent with WC94 within one sigma (R-022 section 5) ----

        enum Verdict { Pass, Warn, Fail }

        static Verdict M054(double mw, double srlKm, double mdM, double adM, out double dML, out double dAD, out double dMD)
        {
            dML = mw - Wc94StrikeSlip.MFromSrl.Eval(Math.Log10(srlKm));
            dAD = Math.Log10(adM) - Wc94StrikeSlip.LogAdFromM.Eval(mw);
            dMD = Math.Log10(mdM) - Wc94StrikeSlip.LogMdFromM.Eval(mw);
            double a = Math.Abs(dML) / Wc94StrikeSlip.MFromSrl.Sigma, b = Math.Abs(dAD) / Wc94StrikeSlip.LogAdFromM.Sigma, c = Math.Abs(dMD) / Wc94StrikeSlip.LogMdFromM.Sigma;
            double worst = Math.Max(a, Math.Max(b, c));
            return worst <= 1.0 ? Verdict.Pass : worst <= 2.0 ? Verdict.Warn : Verdict.Fail;
        }

        [Test]
        public void M054_DefaultScenarioPasses_WithinOneSigma()
        {
            var w = new World(false, true);
            RuptureResult r = w.Director.Run(Scenario(w.Director.Faults.Systems[0]));
            FaultRupture f = w.Rec.Rupture;
            double dML, dAD, dMD;
            Assert.AreEqual(Verdict.Pass, M054(r.Mw, f.SrlKm, r.PeakSlipM, r.MeanSlipM, out dML, out dAD, out dMD));
            Assert.Less(Math.Abs(dML), Wc94StrikeSlip.MFromSrl.Sigma);
            Assert.Less(Math.Abs(dAD), Wc94StrikeSlip.LogAdFromM.Sigma);
            Assert.Less(Math.Abs(dMD), Wc94StrikeSlip.LogMdFromM.Sigma);
            Assert.LessOrEqual(f.SrlKm, w.Director.Faults.Systems[0].LengthM / 1000.0);       // SRL <= trace length
            Assert.IsTrue(RuptureScaling.MwInRange(r.Mw));
            Assert.IsTrue(Wc94StrikeSlip.MFromSrl.InRange(f.SrlKm));
            Assert.IsTrue(Wc94StrikeSlip.MFromMd.InRange(r.PeakSlipM));
            Assert.IsTrue(Wc94StrikeSlip.MFromAd.InRange(r.MeanSlipM));
        }

        [Test]
        public void M054_Discriminates_HayWiredWarns_InconsistentFails()
        {
            double a, b, c;
            // HayWired Hayward: Mw 7.0 with 84 km length: dM_L about -0.31, a warn (1 to 2 sigma).
            double md = RuptureScaling.MaxDisplacementMFromMw(7.0), ad = RuptureScaling.AvgDisplacementMFromMw(7.0);
            Assert.AreEqual(Verdict.Warn, M054(7.0, 84.0, md, ad, out a, out b, out c));
            Assert.AreEqual(-0.31, a, 0.02);
            // Mw 7.2 claimed on a 5 km rupture: beyond 2 sigma.
            Assert.AreEqual(Verdict.Fail, M054(7.2, 5.0, RuptureScaling.MaxDisplacementMFromMw(7.2), RuptureScaling.AvgDisplacementMFromMw(7.2), out a, out b, out c));
            // Shortened Mw (system shorter than SRL) stays consistent: Mw is reduced, not the trace stretched.
            var w = new World(false, true);
            FaultSystem s = w.Director.Faults.Systems[0];
            RuptureResult r = w.Director.Run(new QuakeScenario { EventId = 9, Mw = 7.9f, FaultId = s.FaultId, HypoX = 55000f, HypoZ = 500f, RuptureSpeedMs = 2800f, Seed = 1 });
            Assert.IsTrue(r.ShortenedToSystem);
            Assert.AreNotEqual(Verdict.Fail, M054(r.Mw, w.Rec.Rupture.SrlKm, r.PeakSlipM, r.MeanSlipM, out a, out b, out c));
        }
    }
}
