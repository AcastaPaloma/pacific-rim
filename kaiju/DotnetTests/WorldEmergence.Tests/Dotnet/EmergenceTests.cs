// T-114 World.Emergence tests: planner properties (sites, clusters, mix, determinism) and the bus path with the real World.Seismic QuakeDirector.
using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.World.Seismic;
using NUnit.Framework;

namespace Game.World.Emergence.Tests
{
    public class EmergenceTests
    {
        sealed class NullErrors : ICoreErrorSink { public void Report(CoreError error) { } }

        sealed class Targets : IEmergenceTargets
        {
            readonly EmergenceTarget[] _t;
            public Targets(params EmergenceTarget[] t) { _t = t; }
            public int Count => _t.Length;
            public EmergenceTarget Get(int i) => _t[i];
        }

        sealed class Registry : IServiceRegistry
        {
            readonly Dictionary<Type, object> _map = new Dictionary<Type, object>();
            public void Register<T>(T implementation) where T : class { _map.Add(typeof(T), implementation); }
            public T Resolve<T>() where T : class { return (T)_map[typeof(T)]; }
            public bool TryResolve<T>(out T implementation) where T : class { bool ok = _map.TryGetValue(typeof(T), out var o); implementation = ok ? (T)o : null; return ok; }
            public bool IsRegistered<T>() where T : class { return _map.ContainsKey(typeof(T)); }
        }

        sealed class Traces : IEmergenceTraceSource
        {
            readonly FaultTraceSet _f;
            public Traces(FaultTraceSet f) { _f = f; }
            public bool TryGet(uint id, out EmergenceTrace t)
            {
                var s = _f.FindSystem(id);
                t = s == null ? null : new EmergenceTrace(s.FaultId, s.X, s.Z, s.ArcM, s.Bridge);
                return s != null;
            }
        }

        sealed class Log : IEventHandler<EmergenceSiteSelected>, IEventHandler<KaijuEmerged>, IEventHandler<EmergenceSequenceDone>, IEventHandler<EmergencePoint>
        {
            public readonly List<string> Order = new List<string>();
            public readonly List<EmergenceSiteSelected> Sites = new List<EmergenceSiteSelected>();
            public readonly List<KaijuEmerged> Kaiju = new List<KaijuEmerged>();
            public readonly List<EmergenceSequenceDone> Done = new List<EmergenceSequenceDone>();
            public EmergencePoint Point;
            public void Handle(in EmergenceSiteSelected e) { Order.Add("site"); Sites.Add(e); }
            public void Handle(in KaijuEmerged e) { Order.Add("kaiju"); Kaiju.Add(e); }
            public void Handle(in EmergenceSequenceDone e) { Order.Add("done"); Done.Add(e); }
            public void Handle(in EmergencePoint e) { Order.Add("point"); Point = e; }
        }

        // Straight 30 km east-west trace, vertex every 500 m, optional bridge segments.
        static EmergenceTrace Line(double lengthM = 30000, params int[] bridgeSegments)
        {
            int n = (int)(lengthM / 500) + 1;
            var x = new double[n]; var z = new double[n]; var arc = new double[n]; var br = new bool[n - 1];
            for (int i = 0; i < n; i++) { x[i] = i * 500.0; arc[i] = i * 500.0; }
            foreach (int b in bridgeSegments) br[b] = true;
            return new EmergenceTrace(42, x, z, arc, br);
        }

        static EmergenceRequest Req(uint ev = 1, float mw = 7.2f, uint seed = 5, int vs = 0, int ve = 60)
            => new EmergenceRequest { EventId = ev, Mw = mw, Seed = seed, VertexStart = vs, VertexEnd = ve, RuptureEndS = 30f };

        static string Fingerprint(EmergencePlan p)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var s in p.Sites)
            {
                sb.Append(s.VertexIdx).Append('@').Append(s.TimeS).Append(':');
                foreach (var k in s.Kaiju) sb.Append(k.SpeciesId).Append(',').Append(k.X).Append(',').Append(k.TimeS).Append(';');
            }
            return sb.ToString();
        }

        [Test]
        public void Plan_IsDeterministicForSeedAndDiffersAcrossSeeds()
        {
            var t = Line(); var tg = new Targets(new EmergenceTarget(15000, 12000));
            var opt = new EmergenceOptions { SiteCount = 5 };
            string a = Fingerprint(EmergencePlanner.Plan(Req(), t, tg, opt));
            string b = Fingerprint(EmergencePlanner.Plan(Req(), t, tg, opt));
            string c = Fingerprint(EmergencePlanner.Plan(Req(seed: 6), t, tg, opt));
            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, c);
        }

        [Test]
        public void Sites_FiveOnA30kmSegment_SpacedAndInsideRange()
        {
            var t = Line(); var tg = new Targets(new EmergenceTarget(15000, 12000));
            var opt = new EmergenceOptions { SiteCount = 5 };
            for (uint seed = 1; seed <= 30; seed++)
            {
                var plan = EmergencePlanner.Plan(Req(seed: seed, vs: 4, ve: 56), t, tg, opt);
                Assert.AreEqual(5, plan.Sites.Length, "seed " + seed);
                foreach (var s in plan.Sites) Assert.That(s.VertexIdx, Is.InRange(4, 56));
                for (int i = 0; i < plan.Sites.Length; i++)
                    for (int j = i + 1; j < plan.Sites.Length; j++)
                    {
                        double dx = plan.Sites[i].X - plan.Sites[j].X, dz = plan.Sites[i].Z - plan.Sites[j].Z;
                        Assert.GreaterOrEqual(Math.Sqrt(dx * dx + dz * dz), opt.MinSiteSpacingM - 1e-6, "seed " + seed);
                    }
            }
        }

        [Test]
        public void Sites_NeverOnBridgeVertices_AndTooShortRangeGivesFewerSites()
        {
            var t = Line(30000, 28, 29, 30, 31, 32);        // vertices 28..33 touch a bridge
            var plan = EmergencePlanner.Plan(Req(), t, null, new EmergenceOptions { SiteCount = 12 });
            foreach (var s in plan.Sites) Assert.IsFalse(t.TouchesBridge(s.VertexIdx), "vertex " + s.VertexIdx);
            Assert.Less(plan.Sites.Length, 12);   // 3 km spacing on 30 km, minus the bridge
            Assert.AreEqual(12, plan.SitesRequested);
            Assert.IsNull(EmergencePlanner.Plan(Req(vs: 29, ve: 31), t, null, new EmergenceOptions()), "every vertex of the range touches the bridge");
        }

        [Test]
        public void Sites_PreferTheTargetBand()
        {
            var t = Line(); var tg = new Targets(new EmergenceTarget(0, 15000));   // in band (5-20 km) for x up to about 13 km
            var opt = new EmergenceOptions { SiteCount = 2 };
            int inBand = 0, total = 0;
            for (uint seed = 1; seed <= 60; seed++)
                foreach (var s in EmergencePlanner.Plan(Req(seed: seed), t, tg, opt).Sites)
                { total++; if (s.DistToTargetM >= opt.BandMinM && s.DistToTargetM <= opt.BandMaxM) inBand++; }
            Assert.GreaterOrEqual(inBand / (double)total, 0.9, inBand + "/" + total);
        }

        [Test]
        public void Cluster_SizeSpreadWindowAndMapped()
        {
            var t = Line(30000, 40);   // bridge segment 40 (x 20000..20500)
            var tg = new Targets(new EmergenceTarget(15000, 12000));
            var opt = new EmergenceOptions { SiteCount = 3 };
            var sizes = new HashSet<int>();
            for (uint seed = 1; seed <= 60; seed++)
            {
                var plan = EmergencePlanner.Plan(Req(seed: seed), t, tg, opt);
                foreach (var s in plan.Sites)
                {
                    sizes.Add(s.Kaiju.Length);
                    Assert.That(s.Kaiju.Length, Is.InRange(1, 5));
                    Assert.AreEqual(s.TimeS, s.Kaiju[0].TimeS, 1e-4);
                    for (int i = 0; i < s.Kaiju.Length; i++)
                    {
                        var k = s.Kaiju[i];
                        Assert.That(k.TimeS, Is.InRange(s.TimeS - 1e-4f, s.TimeS + opt.WindowS + 1e-4f));
                        if (i > 0) Assert.GreaterOrEqual(k.TimeS, s.Kaiju[i - 1].TimeS);
                        Assert.LessOrEqual(Math.Abs(k.X - s.X), opt.ClusterSpreadM + 1e-6);   // straight trace: arc distance == x distance
                        Assert.That(k.SpeciesId, Is.InRange(EmergenceSpecies.Strider, EmergenceSpecies.Bastion));
                        // never across the bridge: same side as the site
                        Assert.AreEqual(s.X < 20000, k.X <= 20000 + 1e-6, "kaiju crossed the bridge");
                    }
                }
                Assert.That(plan.Sites[0].TimeS, Is.EqualTo(30f + 15f).Within(1e-4));
            }
            Assert.That(sizes, Is.SupersetOf(new[] { 1, 2, 3, 4 }), "cluster sizes seen");
        }

        [Test]
        public void Cluster_KaijuAreSeparated()
        {
            var t = Line();
            var opt = new EmergenceOptions { SiteCount = 1, KMin = 5, KMax = 5 };
            for (uint seed = 1; seed <= 40; seed++)
            {
                var k = EmergencePlanner.Plan(Req(seed: seed), t, null, opt).Sites[0].Kaiju;
                Assert.AreEqual(5, k.Length);
                int close = 0;
                for (int i = 0; i < k.Length; i++) for (int j = i + 1; j < k.Length; j++) if (Math.Abs(k[i].X - k[j].X) < opt.MinKaijuSpacingM) close++;
                Assert.LessOrEqual(close, 1, "seed " + seed);   // 12 redraws; rare corner clamps may still touch
            }
        }

        [Test]
        public void Mix_LargerEventGivesLargerSpecies()
        {
            var t = Line();
            double Heavy(float mw)
            {
                int h = 0, n = 0;
                for (uint seed = 1; seed <= 300; seed++)
                    foreach (var s in EmergencePlanner.Plan(Req(mw: mw, seed: seed), t, null, new EmergenceOptions { SiteCount = 2, KMin = 4, KMax = 4 }).Sites)
                        foreach (var k in s.Kaiju) { n++; if (k.SpeciesId == EmergenceSpecies.Bastion) h++; }
                return h / (double)n;
            }
            double lo = Heavy(6.2f), mid = Heavy(6.8f), hi = Heavy(7.8f);
            Assert.Less(lo, mid); Assert.Less(mid, hi);
            Assert.That(lo, Is.EqualTo(0.05).Within(0.03));
            Assert.That(hi, Is.EqualTo(0.40).Within(0.05));
        }

        [Test]
        public void ClusterSize_GrowsWithMagnitude()
        {
            var o = new EmergenceOptions();
            Assert.AreEqual(1, o.ResolveKMax(5.5f));
            Assert.AreEqual(3, o.ResolveKMax(6.5f));
            Assert.AreEqual(4, o.ResolveKMax(7.2f));
            Assert.AreEqual(5, o.ResolveKMax(8.0f));
        }

        [Test]
        public void Heading_PointsAtNearestTarget()
        {
            var t = Line(); var tg = new Targets(new EmergenceTarget(15000, 10000));
            foreach (var k in EmergencePlanner.Plan(Req(), t, tg, new EmergenceOptions()).Sites[0].Kaiju)
            {
                double want = Math.Atan2(10000 - 0, 15000 - k.X);
                Assert.AreEqual(want, k.HeadingRad, 1e-4);
            }
        }

        // ---- bus path with the real World.Seismic QuakeDirector ----

        static FaultPiece Piece(string section, double x0, double x1, double z)
        {
            var l = new List<double>();
            for (double x = x0; x <= x1 + 1e-9; x += 500) { l.Add(x); l.Add(z); }
            return new FaultPiece { FaultName = "Test fault zone", Section = section, SlipSense = "right lateral", Xz = l.ToArray() };
        }

        sealed class Rig
        {
            public EventBus Bus = new EventBus(new NullErrors(), 10000);
            public Registry Reg = new Registry();
            public FaultTraceSet Faults = FaultTraceSet.Build(new List<FaultPiece> { Piece("A section", 0, 40000, 0), Piece("B section", 43000, 100000, 0) });
            public QuakeDirector Quake;
            public Log Log = new Log();
            public EmergenceDirector Emergence;

            public Rig(bool flag, EmergenceOptions opt = null)
            {
                Reg.Register<IEventBus>(Bus);
                Reg.Register(new EntityIdAllocator());
                Bus.Subscribe<EmergenceSiteSelected>(Log); Bus.Subscribe<KaijuEmerged>(Log); Bus.Subscribe<EmergenceSequenceDone>(Log); Bus.Subscribe<EmergencePoint>(Log);
                var flags = new FeatureFlagSnapshot(new[] { new KeyValuePair<string, bool>(EmergenceFlags.Enabled, flag) });
                Emergence = EmergenceModule.Install(Reg, flags, new Traces(Faults), new Targets(new EmergenceTarget(60000, 12000)), opt);
                Quake = new QuakeDirector(Faults, new DistrictRefs { GGX = 20000, GGZ = 5000, SoMaX = 80000, SoMaZ = 9000, TargetGG = true }, Bus);
            }

            public RuptureResult Run(uint ev = 1, uint seed = 7, float mw = 7.2f)
            {
                var sys = Faults.Systems[0];
                return Quake.Run(new QuakeScenario { EventId = ev, Mw = mw, FaultId = sys.FaultId, HypoX = 55000f, HypoZ = 500f, HypoDepthM = 8000f, RuptureSpeedMs = 2800f, Seed = seed });
            }
        }

        [Test]
        public void Bus_FlagOff_RegistersNothingAndPublishesNothing()
        {
            var rig = new Rig(false);
            Assert.IsNull(rig.Emergence);
            Assert.IsFalse(rig.Reg.IsRegistered<EmergenceDirector>());
            Assert.AreEqual(0, rig.Bus.SubscriberCount<QuakeBegan>());
            Assert.IsNotNull(rig.Run());
            Assert.AreEqual(0, rig.Log.Sites.Count);
            Assert.AreEqual(0, rig.Log.Kaiju.Count);
        }

        [Test]
        public void Bus_FlagOn_PublishesOrderedSequenceOnTheRupture()
        {
            var rig = new Rig(true, new EmergenceOptions { SiteCount = 3 });
            Assert.IsNotNull(rig.Emergence);
            var r = rig.Run();
            Assert.IsNotNull(r);
            Assert.AreEqual(3, rig.Log.Sites.Count);
            Assert.AreEqual(1, rig.Log.Done.Count);
            var done = rig.Log.Done[0];
            Assert.AreEqual(rig.Log.Kaiju.Count, done.Kaiju);
            Assert.AreEqual(3, done.Sites);
            // order: the Seismic point comes after the rupture; ours are one cluster each (site, its kaiju), then done last
            Assert.AreEqual("done", rig.Log.Order.Where(o => o != "point").Last());
            Assert.AreEqual("point", rig.Log.Order.Last(), "Seismic EmergencePoint follows our sequence (it is published after FaultRupture is fully handled)");
            int si = 0;
            foreach (var tag in rig.Log.Order.Where(o => o != "point")) { if (tag == "site") si++; else if (tag == "kaiju") Assert.Greater(si, 0, "kaiju before its site"); }
            // inside the ruptured range, never on a bridge, times at or after the Seismic emergence base
            var sys = rig.Faults.Systems[0];
            foreach (var s in rig.Log.Sites)
            {
                Assert.That(s.VertexIdx, Is.InRange(r.VertexStart, r.VertexEnd));
                Assert.IsFalse(sys.TouchesBridge(s.VertexIdx));
                Assert.AreEqual((float)(r.EndS + 15.0), s.TimeS, 1e-3);
                Assert.AreEqual(sys.FaultId, s.FaultId);
            }
            Assert.AreEqual(rig.Log.Kaiju.Select(k => k.KaijuId).Distinct().Count(), rig.Log.Kaiju.Count, "ids unique");
            Assert.IsTrue(rig.Log.Kaiju.All(k => k.KaijuId.IsValid));
        }

        [Test]
        public void Bus_RepeatedEventIdAndUnknownFault_PublishNothingMore()
        {
            var rig = new Rig(true, new EmergenceOptions { SiteCount = 2 });
            rig.Run(1);
            int n = rig.Log.Kaiju.Count;
            Assert.IsNull(rig.Run(1));          // Seismic refuses a repeated EventId
            Assert.AreEqual(n, rig.Log.Kaiju.Count);
            rig.Run(2, seed: 9);
            Assert.Greater(rig.Log.Kaiju.Count, n);
            Assert.AreEqual(2, rig.Emergence.SequencesRun);
        }

        [Test]
        public void Bus_SameInputsGiveTheSameSequence()
        {
            var a = new Rig(true, new EmergenceOptions { SiteCount = 4 }); a.Run(1, 11);
            var b = new Rig(true, new EmergenceOptions { SiteCount = 4 }); b.Run(1, 11);
            Assert.AreEqual(Fingerprint(a.Emergence.LastPlan), Fingerprint(b.Emergence.LastPlan));
        }
    }
}
