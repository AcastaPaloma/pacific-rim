using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Game.Core;
using Game.Hangar;
using Game.World.Geo;

namespace Game.Hangar.Tests
{
    sealed class FakeBody : IBodyDamageRecord
    {
        public readonly List<PartDamage> Parts = new List<PartDamage>();
        public FakeBody(PlayerSlot slot) { Slot = slot; }
        public PlayerSlot Slot { get; }
        public void CopyParts(List<PartDamage> into) { into.Clear(); into.AddRange(Parts); }
        public void ApplyParts(IReadOnlyList<PartDamage> parts) { Parts.Clear(); Parts.AddRange(parts); }
        public int Index(string id) { for (int i = 0; i < Parts.Count; i++) if (Parts[i].PartId == id) return i; return -1; }
        public void Hit(string id, double dmg, long step)
        {
            int i = Index(id); var p = Parts[i];
            p.Integrity = Math.Max(0.0, p.Integrity - dmg); p.Hits++; p.LastHitStep = step; Parts[i] = p;
        }
        public static FakeBody Fresh(PlayerSlot slot, string[] ids, double[] max)
        {
            var b = new FakeBody(slot);
            for (int i = 0; i < ids.Length; i++) b.Parts.Add(new PartDamage { PartId = ids[i], Integrity = max[i], MaxIntegrity = max[i] });
            return b;
        }
    }

    sealed class FakeWorld : IGeoWorld
    {
        public readonly Dictionary<BuildingId, GeoBuildingInfo> Buildings = new Dictionary<BuildingId, GeoBuildingInfo>();
        public double TerrainY = 4.25;
        public bool Loaded = true;
        public IReadOnlyList<GeoTileId> Tiles => new[] { GeoTileId.From(0, 27, 36) };
        public double WaterLevelNavd88 => 0.969;
        public bool TryGetBuilding(BuildingId id, out GeoBuildingInfo info) => Buildings.TryGetValue(id, out info);
        public int RemoveBuilding(BuildingId id) => 0;
        public bool IsRemoved(BuildingId id) => false;
        public double SampleTerrain(double x, double z) => Loaded ? TerrainY + x * 0 + z * 0 : double.NaN;
    }

    public class HangarTests
    {
        static readonly string[] Ids = { "arm_l", "arm_r", "core", "head", "leg_l", "leg_r", "pelvis", "torso" };
        static readonly double[] Max = { 80, 80, 200, 60, 120, 120, 140, 180 };
        static readonly PlayerSlot S0 = PlayerSlot.FromIndex(0);

        static string FlagFile()
        {
            var d = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "Assets", "Game", "Config", "FeatureFlags", "Hangar.json"))) d = d.Parent;
            Assert.IsNotNull(d, "repo root not found");
            return Path.Combine(d.FullName, "Assets", "Game", "Config", "FeatureFlags", "Hangar.json");
        }

        [Test] public void Flag_FileDefaultsOff()
        {
            string t = File.ReadAllText(FlagFile());
            StringAssert.Contains("\"hangar.stub\": false", t);
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(t, "\"hangar\\.").Count);
        }

        [Test] public void Install_OnlyWhenFlagOn()
        {
            var reg = new ServiceRegistry();
            Assert.IsNull(HangarModule.Install(reg, FeatureFlagSnapshot.AllOff));
            Assert.IsFalse(reg.TryResolve<IHangarService>(out _));
            var on = new FeatureFlagSnapshot(new[] { new KeyValuePair<string, bool>(HangarFlags.Stub, true) });
            var svc = HangarModule.Install(reg, on);
            Assert.IsNotNull(svc);
            Assert.AreSame(svc, reg.Resolve<IHangarService>());
            Assert.AreEqual("crissy_field", svc.Site.Id);
        }

        [Test] public void Site_IsInsideItsTile_AndTileIsGoldenGate()
        {
            var s = HangarSites.CrissyField;
            Assert.AreEqual("L0_27_36", s.Tile.ToString());
            Assert.IsTrue(s.Tile.Contains(s.BayX, s.BayZ));
            Assert.Throws<ArgumentException>(() => new HangarSite("x", "x", GeoTileId.From(0, 30, 36), s.BayX, s.BayZ, 0, ""));
        }

        [Test] public void Spawn_UsesTerrain_AndIsUnknownWhenTileNotLoaded()
        {
            var w = new FakeWorld();
            var svc = new HangarService(HangarSites.CrissyField, w);
            var sp = svc.ResolveSpawn();
            Assert.IsTrue(sp.TerrainKnown); Assert.AreEqual(4.25, sp.Y); Assert.AreEqual(HangarSites.CrissyField.BayX, sp.X); Assert.IsFalse(sp.FromAnchor);
            w.Loaded = false;
            Assert.IsFalse(svc.ResolveSpawn().TerrainKnown);
            Assert.IsTrue(double.IsNaN(new HangarService(HangarSites.CrissyField).ResolveSpawn().Y));
        }

        [Test] public void Spawn_FollowsGeoAnchorBuilding_WhenLoaded()
        {
            var bid = BuildingId.From(BuildingSource.OsmWay, 4242);
            var site = new HangarSite("crissy_field", "t", GeoTileId.From(0, 27, 36), 56821.48, 75637.64, 90, "", bid, 10, -5);
            var w = new FakeWorld();
            w.Buildings[bid] = new GeoBuildingInfo { Id = bid, AnchorX = 56900, AnchorZ = 75700 };
            var sp = new HangarService(site, w).ResolveSpawn();
            Assert.IsTrue(sp.FromAnchor); Assert.AreEqual(56910, sp.X); Assert.AreEqual(75695, sp.Z);
            w.Buildings.Clear();
            Assert.IsFalse(new HangarService(site, w).ResolveSpawn().FromAnchor);   // anchor not loaded: falls back to BayX/BayZ
        }

        [Test] public void Lifecycle_RepairOnlyQueuedParts_AndUnrepairedKeepCarriedValue()
        {
            var svc = new HangarService(HangarSites.CrissyField);
            var body = FakeBody.Fresh(S0, Ids, Max);
            body.Hit("torso", 33.3, 10); body.Hit("leg_l", 500, 11); body.Hit("head", 7.7, 12);
            svc.EnterHangar(body);
            Assert.AreEqual(HangarPhase.Hangar, svc.GetPhase(S0));
            Assert.AreEqual(3, svc.QueueAllDamaged(S0));
            var parts = new List<PartRepairState>(); Assert.IsTrue(svc.TryGetParts(S0, parts));
            Assert.AreEqual(8, parts.Count);
            Assert.AreEqual(3, svc.ProcessRepairs(S0));
            svc.TryGetParts(S0, parts);
            foreach (var p in parts)
                if (p.Damage.PartId == "torso" || p.Damage.PartId == "leg_l" || p.Damage.PartId == "head")
                { Assert.AreEqual(p.Damage.MaxIntegrity, p.Damage.Integrity); Assert.IsTrue(p.Repaired); }
                else Assert.IsFalse(p.Repaired);
            svc.LeaveHangar(body);
            Assert.AreEqual(HangarPhase.Fight, svc.GetPhase(S0)); Assert.AreEqual(1, svc.GetCycle(S0));
            Assert.AreEqual(180.0, body.Parts[body.Index("torso")].Integrity);
            Assert.AreEqual(1, body.Parts[body.Index("torso")].Hits, "hit history is carried, only integrity is restored");
        }

        [Test] public void Lifecycle_PartialRepair_LeavesRestBitwiseUnchanged_AndHitClearsRepairedFlag()
        {
            var svc = new HangarService(HangarSites.CrissyField);
            var body = FakeBody.Fresh(S0, Ids, Max);
            body.Hit("torso", 33.3, 1); body.Hit("arm_l", 12.1, 2);
            svc.EnterHangar(body);
            svc.QueueRepair(S0, "torso");
            Assert.AreEqual(1, svc.ProcessRepairs(S0));
            svc.LeaveHangar(body);
            Assert.AreEqual(180.0, body.Parts[body.Index("torso")].Integrity);
            Assert.AreEqual(Max[0] - 12.1, body.Parts[body.Index("arm_l")].Integrity);
            body.Hit("torso", 1, 3);
            svc.EnterHangar(body);
            var parts = new List<PartRepairState>(); svc.TryGetParts(S0, parts);
            Assert.IsFalse(parts.Find(p => p.Damage.PartId == "torso").Repaired);
        }

        [Test] public void Misuse_IsRejected()
        {
            var svc = new HangarService(HangarSites.CrissyField);
            var body = FakeBody.Fresh(S0, Ids, Max);
            Assert.Throws<InvalidOperationException>(() => svc.QueueRepair(S0, "torso"));
            Assert.Throws<InvalidOperationException>(() => svc.LeaveHangar(body));
            svc.EnterHangar(body);
            Assert.Throws<ArgumentException>(() => svc.QueueRepair(S0, "tail"));
            Assert.Throws<ArgumentException>(() => svc.EnterHangar(new FakeBody(S0)));
            var bad = FakeBody.Fresh(S0, new[] { "a/b" }, new[] { 10.0 });
            Assert.Throws<ArgumentException>(() => svc.EnterHangar(bad));
            var nan = FakeBody.Fresh(S0, new[] { "a" }, new[] { 10.0 }); nan.Parts[0] = new PartDamage { PartId = "a", Integrity = double.NaN, MaxIntegrity = 10 };
            Assert.Throws<ArgumentException>(() => svc.EnterHangar(nan));
        }

        [Test] public void Save_RoundTripIsByteStable_AndCoopSlotsAreIndependent()
        {
            var svc = new HangarService(HangarSites.CrissyField);
            var s1 = PlayerSlot.FromIndex(1);
            var b0 = FakeBody.Fresh(S0, Ids, Max); b0.Hit("core", 19.9, 4);
            var b1 = FakeBody.Fresh(s1, Ids, Max); b1.Hit("head", 3.3, 5);
            svc.EnterHangar(b0); svc.EnterHangar(b1); svc.QueueRepair(s1, "head");
            var sys = new SaveSystem("t"); sys.Register(svc);
            string a = SaveCodec.Serialize(sys.Capture());
            var svc2 = new HangarService(HangarSites.CrissyField);
            var sys2 = new SaveSystem("t"); sys2.Register(svc2);
            var r = SaveCodec.Deserialize(a, "t"); Assert.IsTrue(r.Ok, r.Message);
            Assert.IsTrue(sys2.Restore(r.Document).Ok);
            Assert.AreEqual(a, SaveCodec.Serialize(sys2.Capture()));
            Assert.AreEqual(HangarPhase.Hangar, svc2.GetPhase(s1));
            Assert.AreEqual(1, svc2.ProcessRepairs(s1)); Assert.AreEqual(0, svc2.ProcessRepairs(S0));
        }

        [Test] public void Save_UnrelatedSectionsSurvive()
        {
            var svc = new HangarService(HangarSites.CrissyField);
            var sys = new SaveSystem("t"); sys.Register(svc);
            var loaded = new SaveDocument("old"); loaded.AddSection("destruction", 1).Add("bld:osm-way:1").Set("state", SaveValue.Of(2L));
            var doc = sys.Capture(loaded);
            Assert.IsTrue(doc.TryGetSection("destruction", out var d)); Assert.AreEqual(1, d.Records.Count);
            Assert.IsTrue(doc.TryGetSection("hangar", out var h)); Assert.AreEqual(1, h.Version);
        }

        [Test] public void Save_CorruptSectionsAreRefused_AndStateUntouched()
        {
            var good = new HangarService(HangarSites.CrissyField);
            var body = FakeBody.Fresh(S0, Ids, Max); body.Hit("core", 1.5, 1);
            good.EnterHangar(body);
            var sys = new SaveSystem("t"); sys.Register(good);
            string text = SaveCodec.Serialize(sys.Capture());
            var mutate = new List<Action<SaveDocument>>
            {
                d => Rec(d, "slot:0/core").Set(HangarService.FieldIntegrity, SaveValue.Of(double.NaN)),
                d => Rec(d, "slot:0/core").Set(HangarService.FieldIntegrity, SaveValue.Of(1e9)),
                d => Rec(d, "slot:0/core").Set(HangarService.FieldQueue, SaveValue.Of(5L)),                 // gap
                d => Rec(d, "slot:0").Set(HangarService.FieldSite, SaveValue.Of("narnia")),
                d => Rec(d, "slot:0").Set(HangarService.FieldPhase, SaveValue.Of(9L)),
                d => d.Sections[0].Add("slot:7/ghost").Set(HangarService.FieldIntegrity, SaveValue.Of(1.0)),   // part without body
                d => Rec(d, "slot:0/core").Remove(HangarService.FieldHits),
            };
            foreach (var m in mutate)
            {
                var doc = SaveCodec.Deserialize(text, "t").Document; m(doc);
                var svc = new HangarService(HangarSites.CrissyField);
                var other = FakeBody.Fresh(S0, new[] { "z" }, new[] { 9.0 }); svc.EnterHangar(other);
                var sys2 = new SaveSystem("t"); sys2.Register(svc);
                Assert.AreEqual(SaveLoadStatus.Corrupt, sys2.Restore(doc).Status);
                var parts = new List<PartRepairState>(); svc.TryGetParts(S0, parts);
                Assert.AreEqual(1, parts.Count); Assert.AreEqual("z", parts[0].Damage.PartId, "failed Read left the service untouched");
            }
        }

        static SaveRecord Rec(SaveDocument d, string key) { Assert.IsTrue(d.TryGetSection("hangar", out var s)); Assert.IsTrue(s.TryGet(key, out var r)); return r; }
    }
}
