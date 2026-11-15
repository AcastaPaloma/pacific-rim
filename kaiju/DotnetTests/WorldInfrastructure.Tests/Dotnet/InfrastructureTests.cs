// T-105 World.Infrastructure tests: ids, tile parsing (synthetic infra@v1 file), spatial queries, damage through the real World.Destruction DamageModel.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Game.Core;
using Game.World.Destruction;
using NUnit.Framework;

namespace Game.World.Infrastructure.Tests
{
    public class InfrastructureTests
    {
        sealed class NullErrors : ICoreErrorSink { public void Report(CoreError error) { } }
        sealed class Registry : IServiceRegistry
        {
            readonly Dictionary<Type, object> _map = new Dictionary<Type, object>();
            public void Register<T>(T implementation) where T : class { _map.Add(typeof(T), implementation); }
            public T Resolve<T>() where T : class { return (T)_map[typeof(T)]; }
            public bool TryResolve<T>(out T implementation) where T : class { bool ok = _map.TryGetValue(typeof(T), out var o); implementation = ok ? (T)o : null; return ok; }
            public bool IsRegistered<T>() where T : class { return _map.ContainsKey(typeof(T)); }
        }
        sealed class Damaged : IEventHandler<BuildingDamaged>
        {
            public readonly List<BuildingDamaged> Got = new List<BuildingDamaged>();
            public void Handle(in BuildingDamaged e) { Got.Add(e); }
        }

        static InfraRecord Rec(InfraKind k, ulong id, double[] pts, bool area = false, double len = double.NaN, double areaM2 = double.NaN, double lanes = double.NaN, double h = double.NaN, InfraSource src = InfraSource.OsmWay)
        {
            var r = new InfraRecord { Id = InfraId.From(k, src, id), Points = pts, IsArea = area, LengthM = len, AreaM2 = areaM2, Lanes = lanes, HeightM = h };
            r.MinX = r.MaxX = pts[0]; r.MinZ = r.MaxZ = pts[1];
            for (int i = 0; i < pts.Length; i += 2) { r.MinX = Math.Min(r.MinX, pts[i]); r.MaxX = Math.Max(r.MaxX, pts[i]); r.MinZ = Math.Min(r.MinZ, pts[i + 1]); r.MaxZ = Math.Max(r.MaxZ, pts[i + 1]); }
            InfraGeometry.OwnerPoint(r, out r.X, out r.Z);
            return r;
        }

        // A bridge across z = 1000 from x = 0 to 2000, a tower at (500, 500), a substation square (3000..3100, 3000..3100), a crane node far away.
        static List<InfraRecord> Fixture() => new List<InfraRecord>
        {
            Rec(InfraKind.Bridge, 100, new[] { 0.0, 1000, 2000, 1000 }, len: 2000, lanes: 4),
            Rec(InfraKind.PowerTower, 7, new[] { 500.0, 500 }, src: InfraSource.OsmNode, h: 55),
            Rec(InfraKind.Substation, 9, new[] { 3000.0, 3000, 3100, 3000, 3100, 3100, 3000, 3100, 3000, 3000 }, area: true, areaM2: 10000),
            Rec(InfraKind.PortCrane, 11, new[] { 60000.0, 70000 }, src: InfraSource.OsmNode),
        };

        // ---------------------------------------------------------------- ids
        [Test]
        public void InfraId_StringRoundTrip_AndBuildingIdMapping()
        {
            var id = InfraId.From(InfraKind.PortCrane, InfraSource.OsmNode, 4_567_890_123UL);
            Assert.AreEqual("inf:port-crane:osm-node:4567890123", id.ToString());
            Assert.IsTrue(InfraId.TryParse(id.ToString(), out var back));
            Assert.AreEqual(id, back);
            BuildingId b = id.ToBuildingId();
            Assert.AreEqual(BuildingSource.Infrastructure, b.Source);
            Assert.IsTrue(BuildingId.TryParse(b.ToString(), out var bp)); Assert.AreEqual(b, bp);
            Assert.IsTrue(InfraId.TryFromBuildingId(b, out var again)); Assert.AreEqual(id, again);
            Assert.IsFalse(InfraId.TryFromBuildingId(BuildingId.From(BuildingSource.OsmWay, 5), out _));
        }

        [Test]
        public void InfraId_DistinctKindsSourcesAndIds_MapToDistinctBuildingIds()
        {
            var seen = new HashSet<BuildingId>();
            foreach (InfraKind k in Enum.GetValues(typeof(InfraKind)))
            {
                if (k == InfraKind.None) continue;
                foreach (var s in new[] { InfraSource.OsmWay, InfraSource.OsmNode })
                    foreach (ulong v in new[] { 1UL, 2UL, InfraId.MaxSourceId })
                        Assert.IsTrue(seen.Add(InfraId.From(k, s, v).ToBuildingId()));
            }
            Assert.AreEqual(10 * 2 * 3, seen.Count);
        }

        [Test]
        public void InfraId_Rejects_Invalid()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => InfraId.From(InfraKind.None, InfraSource.OsmWay, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => InfraId.From(InfraKind.Bridge, InfraSource.None, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => InfraId.From(InfraKind.Bridge, InfraSource.OsmWay, InfraId.MaxSourceId + 1));
            foreach (var s in new[] { null, "", "bld:osm-way:1", "inf:bridge:osm-way", "inf:bridge:osm-way:-1", "inf:nothing:osm-way:1", "inf:bridge:osm-rel:1", "inf:bridge:osm-way:99999999999999999999" })
                Assert.IsFalse(InfraId.TryParse(s, out _), s);
        }

        [Test]
        public void KindMasks_CoverAllTenKinds()
        {
            uint all = 0;
            for (int k = 1; k <= 10; k++) all |= InfraKinds.Mask((InfraKind)k);
            Assert.AreEqual(InfraKinds.All, all);
            Assert.AreEqual(InfraKinds.Mask(InfraKind.PowerTower, InfraKind.PowerLine, InfraKind.Substation, InfraKind.PowerPlant), InfraKinds.Power);
        }

        // ---------------------------------------------------------------- registry queries
        [Test]
        public void Registry_CountsGetAndTileLifecycle()
        {
            var reg = new InfraRegistry();
            Assert.AreEqual(4, reg.AddTile("L0_00_00", Fixture()));
            Assert.AreEqual(4, reg.Count); Assert.AreEqual(1, reg.CountOf(InfraKind.Bridge)); Assert.AreEqual(0, reg.CountOf(InfraKind.Runway));
            Assert.IsTrue(reg.TryGet(InfraId.From(InfraKind.PowerTower, InfraSource.OsmNode, 7), out var t)); Assert.AreEqual("L0_00_00", t.Tile);
            Assert.AreEqual(4, reg.RemoveTile("L0_00_00"));
            Assert.AreEqual(0, reg.Count); Assert.AreEqual(0, reg.CountOf(InfraKind.Bridge));
            Assert.AreEqual(0, reg.RemoveTile("L0_00_00"));
            reg.AddTile("L0_00_00", Fixture()); reg.AddTile("L0_00_00", Fixture());   // re-adding a tile replaces, never duplicates
            Assert.AreEqual(4, reg.Count);
        }

        [Test]
        public void Registry_QueryRadius_UsesGeometryNotOwnerPoint_SortedByDistance()
        {
            var reg = new InfraRegistry(); reg.AddTile("a", Fixture());
            var hits = new List<InfraRecord>();
            reg.QueryRadius(1900, 1050, 120, InfraKinds.All, hits);                      // 50 m from the bridge's east end, 1400 m from its owner point
            Assert.AreEqual(1, hits.Count); Assert.AreEqual(InfraKind.Bridge, hits[0].Id.Kind);
            hits.Clear(); reg.QueryRadius(1900, 1500, 120, InfraKinds.All, hits); Assert.AreEqual(0, hits.Count);
            hits.Clear(); reg.QueryRadius(1000, 800, 600, InfraKinds.All, hits);        // bridge 200 m, tower 583 m
            Assert.AreEqual(new[] { InfraKind.Bridge, InfraKind.PowerTower }, hits.Select(h => h.Id.Kind).ToArray());
            hits.Clear(); reg.QueryRadius(1000, 800, 600, InfraKinds.Power, hits);
            Assert.AreEqual(new[] { InfraKind.PowerTower }, hits.Select(h => h.Id.Kind).ToArray());
        }

        [Test]
        public void Registry_Nearest_AreaInsideIsZero_AndMaxRadius()
        {
            var reg = new InfraRegistry(); reg.AddTile("a", Fixture());
            Assert.IsTrue(reg.TryNearest(3050, 3050, InfraKinds.All, 5000, out var r, out double d));
            Assert.AreEqual(InfraKind.Substation, r.Id.Kind); Assert.AreEqual(0.0, d);
            Assert.IsTrue(reg.TryNearest(59000, 70000, InfraKinds.All, 5000, out r, out d)); Assert.AreEqual(InfraKind.PortCrane, r.Id.Kind); Assert.AreEqual(1000.0, d, 1e-9);
            Assert.IsFalse(reg.TryNearest(30000, 30000, InfraKinds.All, 5000, out _, out _));
            Assert.IsFalse(reg.TryNearest(0, 0, InfraKinds.Mask(InfraKind.Runway), double.PositiveInfinity, out _, out _));   // terminates
        }

        [Test]
        public void Registry_IsDeterministic_AcrossInsertOrders()
        {
            var recs = Fixture();
            var a = new InfraRegistry(); a.AddTile("t", recs);
            var b = new InfraRegistry(); var rev = new List<InfraRecord>(recs); rev.Reverse(); b.AddTile("t", rev);
            var ha = new List<InfraRecord>(); var hb = new List<InfraRecord>();
            a.QueryRadius(1500, 1500, 3000, InfraKinds.All, ha); b.QueryRadius(1500, 1500, 3000, InfraKinds.All, hb);
            Assert.AreEqual(ha.Select(h => h.Id).ToArray(), hb.Select(h => h.Id).ToArray());
        }

        // ---------------------------------------------------------------- damage through IDamageModel
        [Test]
        public void Damage_FlowsThroughDamageModel_AndPublishesBuildingDamaged()
        {
            var bus = new EventBus(new NullErrors(), 1000);
            var seen = new Damaged(); bus.Subscribe<BuildingDamaged>(seen);
            var dm = new DamageModel(bus);
            var reg = new InfraRegistry(dm); reg.AddTile("a", Fixture());
            var bridge = InfraId.From(InfraKind.Bridge, InfraSource.OsmWay, 100);

            Assert.AreEqual(DamageState.None, reg.StateOf(bridge)); Assert.IsTrue(reg.IsOperational(bridge));
            Assert.IsTrue(dm.TryGetThresholds(bridge.ToBuildingId(), out double s, out _, out _, out double c));
            Assert.Greater(s, 0); Assert.Greater(c, s);
            var none = reg.Apply(bridge, s * 0.5, DamageCause.Impact, EntityId.FromValue(5), 1);
            Assert.IsTrue(none.Known); Assert.IsFalse(none.Changed); Assert.AreEqual(0, seen.Got.Count);
            var ch = reg.Apply(bridge, c * 2, DamageCause.Impact, EntityId.FromValue(5), 2);
            Assert.IsTrue(ch.Changed); Assert.AreEqual(DamageState.Complete, ch.Current); Assert.AreEqual(DamageState.None, ch.Prev);
            Assert.AreEqual(DamageState.Complete, reg.StateOf(bridge)); Assert.IsFalse(reg.IsOperational(bridge));
            Assert.AreEqual(1, seen.Got.Count);
            Assert.IsTrue(InfraId.TryFromBuildingId(seen.Got[0].BuildingId, out var evId)); Assert.AreEqual(bridge, evId);
            Assert.AreEqual((byte)DamageState.Complete, seen.Got[0].State);
        }

        [Test]
        public void Damage_IsMonotone_AndUnknownAndNonPositiveAreIgnored()
        {
            var dm = new DamageModel(); var reg = new InfraRegistry(dm); reg.AddTile("a", Fixture());
            var tower = InfraId.From(InfraKind.PowerTower, InfraSource.OsmNode, 7);
            Assert.IsTrue(dm.TryGetThresholds(tower.ToBuildingId(), out double s1, out double s2, out _, out _));
            reg.Apply(tower, s2 * 1.01, DamageCause.Shaking, default, 1);
            DamageState st = reg.StateOf(tower); Assert.GreaterOrEqual((int)st, (int)DamageState.Moderate);
            reg.Apply(tower, -5, DamageCause.Impact, default, 2); reg.Apply(tower, double.NaN, DamageCause.Impact, default, 3);
            Assert.AreEqual(st, reg.StateOf(tower));
            Assert.IsFalse(reg.Apply(InfraId.From(InfraKind.Bridge, InfraSource.OsmWay, 999), 1e9, DamageCause.Impact, default, 4).Known);
            Assert.IsFalse(reg.IsOperational(InfraId.From(InfraKind.Bridge, InfraSource.OsmWay, 999)));
        }

        [Test]
        public void Damage_SurvivesTileUnloadAndReload()
        {
            var dm = new DamageModel(); var reg = new InfraRegistry(dm); reg.AddTile("a", Fixture());
            var sub = InfraId.From(InfraKind.Substation, InfraSource.OsmWay, 9);
            dm.TryGetThresholds(sub.ToBuildingId(), out _, out _, out _, out double c);
            reg.Apply(sub, c * 3, DamageCause.Impact, default, 1);
            Assert.AreEqual(DamageState.Complete, reg.StateOf(sub));
            reg.RemoveTile("a");
            Assert.AreEqual(DamageState.None, reg.StateOf(sub));                      // not loaded: unknown to the registry
            reg.AddTile("a", Fixture());
            Assert.AreEqual(DamageState.Complete, reg.StateOf(sub));                  // the damage model kept it
        }

        [Test]
        public void Damage_BiggerObjectsNeedMoreEnergy_AndWithoutModelNothingHappens()
        {
            var dm = new DamageModel(); var reg = new InfraRegistry(dm);
            var small = Rec(InfraKind.Bridge, 1, new[] { 0.0, 0, 30, 0 }, len: 30, lanes: 2);
            var big = Rec(InfraKind.Bridge, 2, new[] { 0.0, 0, 3000, 0 }, len: 3000, lanes: 6);
            reg.AddTile("a", new[] { small, big });
            dm.TryGetThresholds(small.Id.ToBuildingId(), out double a1, out _, out _, out _);
            dm.TryGetThresholds(big.Id.ToBuildingId(), out double b1, out _, out _, out _);
            Assert.Greater(b1, a1);
            var bare = new InfraRegistry(); bare.AddTile("a", new[] { small });
            Assert.IsFalse(bare.Apply(small.Id, 1e12, DamageCause.Impact, default, 1).Known);
            Assert.AreEqual(DamageState.None, bare.StateOf(small.Id));
        }

        [Test]
        public void Profile_UsesTaggedValues_ElseAssumedDefaults()
        {
            Assert.AreEqual(55.0, InfraDamageProfile.AttributesOf(Fixture()[1]).HeightM);
            Assert.AreEqual(InfraDamageProfile.TowerHeightM, InfraDamageProfile.AttributesOf(Rec(InfraKind.PowerTower, 1, new[] { 0.0, 0 }, src: InfraSource.OsmNode)).HeightM);
            Assert.AreEqual(2000 * 4 * InfraDamageProfile.BridgeLaneM, InfraDamageProfile.AttributesOf(Fixture()[0]).FootprintM2, 1e-9);
            foreach (InfraKind k in Enum.GetValues(typeof(InfraKind)))
            {
                if (k == InfraKind.None) continue;
                var a = InfraDamageProfile.AttributesOf(Rec(k, 1, new[] { 0.0, 0 }, src: InfraSource.OsmNode));
                Assert.Greater(a.HeightM, 0); Assert.GreaterOrEqual(a.FootprintM2, InfraDamageProfile.MinFootprintM2);
            }
        }

        // ---------------------------------------------------------------- tile parser
        static string TileJson(out string manifest)
        {
            string json = "{\"crs\":{\"properties\":{\"name\":\"kaiju:BayTM@v1\"},\"type\":\"name\"},\"features\":[" +
                "{\"geometry\":{\"coordinates\":[[100.5,200.25],[400.5,200.25],[900,260]],\"type\":\"LineString\"},\"id\":\"bridge:w100\",\"properties\":{\"class\":\"motorway\",\"footway\":0,\"kind\":\"bridge\",\"kind_id\":1,\"landmark\":\"bay_bridge_w\",\"lanes\":4,\"layer\":1,\"length_m\":812.5,\"movable\":0,\"osm\":\"w100\",\"width_m\":null},\"type\":\"Feature\"}," +
                "{\"geometry\":{\"coordinates\":[500,500],\"type\":\"Point\"},\"id\":\"power_tower:n7\",\"properties\":{\"height_m\":55,\"kind\":\"power_tower\",\"kind_id\":2,\"osm\":\"n7\"},\"type\":\"Feature\"}," +
                "{\"geometry\":{\"coordinates\":[[[0,0],[100,0],[100,60],[0,60],[0,0]]],\"type\":\"Polygon\"},\"id\":\"substation:w9\",\"properties\":{\"area_m2\":6000,\"kind\":\"substation\",\"kind_id\":4,\"osm\":\"w9\",\"voltage_v\":115000},\"type\":\"Feature\"}" +
                "],\"spec\":\"infra@v1\",\"type\":\"FeatureCollection\"}";
            manifest = "{\"count\":3,\"file\":\"infrastructure.geojson\",\"sha256\":\"" + InfraTileParser.Sha256Hex(json) + "\",\"spec\":\"infra@v1\",\"tile\":\"L0_30_36\"}";
            return json;
        }

        [Test]
        public void Parser_ReadsAllThreeGeometryTypes_AndTheOwnerRule()
        {
            var recs = InfraTileParser.Parse(TileJson(out string man), man);
            Assert.AreEqual(3, recs.Count);
            var b = recs[0];
            Assert.AreEqual(InfraId.From(InfraKind.Bridge, InfraSource.OsmWay, 100), b.Id);
            Assert.AreEqual("bay_bridge_w", b.Landmark); Assert.AreEqual("motorway", b.Class); Assert.AreEqual(4.0, b.Lanes); Assert.AreEqual(1, b.Layer);
            Assert.IsTrue(double.IsNaN(b.WidthM)); Assert.AreEqual(812.5, b.LengthM); Assert.AreEqual("L0_30_36", b.Tile);
            // arc-length midpoint: legs 300 and 503.07 m, half = 401.54 m, i.e. 101.54 m into the second leg
            Assert.AreEqual(501.31, b.X, 0.01); Assert.AreEqual(212.30, b.Z, 0.01);
            Assert.AreEqual(InfraSource.OsmNode, recs[1].Id.Source); Assert.AreEqual(55.0, recs[1].HeightM); Assert.AreEqual(500.0, recs[1].X);
            Assert.IsTrue(recs[2].IsArea); Assert.AreEqual(50.0, recs[2].X); Assert.AreEqual(30.0, recs[2].Z); Assert.AreEqual(115000.0, recs[2].VoltageV);
            Assert.AreEqual(0.0, InfraGeometry.Distance(recs[2], 50, 30)); Assert.AreEqual(40.0, InfraGeometry.Distance(recs[2], 140, 30), 1e-9);
        }

        [Test]
        public void Parser_RejectsTamperedOrWrongFiles()
        {
            string json = TileJson(out string man);
            Assert.Throws<FormatException>(() => InfraTileParser.Parse(json.Replace("55", "56"), man));
            Assert.Throws<FormatException>(() => InfraTileParser.Parse(json, man.Replace("\"count\":3", "\"count\":4")));
            Assert.Throws<FormatException>(() => InfraTileParser.Parse("{\"spec\":\"geo_tile@v1\",\"features\":[]}"));
            Assert.Throws<FormatException>(() => InfraTileParser.Parse(json.Replace("\"osm\":\"n7\"", "\"osm\":\"x7\"")));
        }

        [Test]
        public void Module_RegistersOnlyWhenFlagIsOn_AndUsesRegisteredDamageModel()
        {
            var off = new Registry();
            Assert.IsNull(InfrastructureModule.Install(off, FeatureFlagSnapshot.AllOff)); Assert.IsFalse(off.IsRegistered<IInfrastructureRegistry>());
            var on = new Registry(); var dm = new DamageModel(); on.Register<IDamageModel>(dm);
            var flags = new FeatureFlagSnapshot(new[] { new KeyValuePair<string, bool>(InfrastructureFlags.Enabled, true) });
            var reg = InfrastructureModule.Install(on, flags);
            Assert.NotNull(reg); Assert.AreSame(reg, on.Resolve<IInfrastructureRegistry>());
            reg.AddTile("a", Fixture());
            Assert.IsTrue(reg.Apply(InfraId.From(InfraKind.Bridge, InfraSource.OsmWay, 100), 1e12, DamageCause.Impact, default, 1).Known);
        }

        // Optional: parse a real baked tile (set KAIJU_INFRA_TILE_DIR to <cache>/bake/tiles/v1/L0/30_36).
        [Test]
        public void RealTile_WhenProvided_ParsesAndMatchesItsManifest()
        {
            string dir = Environment.GetEnvironmentVariable("KAIJU_INFRA_TILE_DIR");
            if (string.IsNullOrEmpty(dir) || !File.Exists(Path.Combine(dir, "infrastructure.geojson"))) Assert.Ignore("KAIJU_INFRA_TILE_DIR not set");
            string json = File.ReadAllText(Path.Combine(dir, "infrastructure.geojson"), new UTF8Encoding(false));
            var recs = InfraTileParser.Parse(json, File.ReadAllText(Path.Combine(dir, "infrastructure.manifest.json")));
            Assert.Greater(recs.Count, 0);
            var reg = new InfraRegistry(new DamageModel()); Assert.AreEqual(recs.Count, reg.AddTile("real", recs));
            foreach (var r in recs.Take(200)) Assert.IsTrue(reg.TryNearest(r.X, r.Z, InfraKinds.Mask(r.Id.Kind), 10, out _, out _));
        }
    }
}
