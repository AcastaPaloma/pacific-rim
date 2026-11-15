using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Game.Core;
using Game.Hangar;
using Game.Meta.Economy;
using Game.Meta.Roster;
using Game.Meta.Salvage;

namespace Game.Meta.Roster.Tests
{
    public class RosterTests
    {
        // ---------------------------------------------------------------- flag, module

        [Test] public void Flag_FileDefaultsOff()
        {
            string t = File.ReadAllText(Path.Combine(Kit.Root(), "Assets", "Game", "Config", "FeatureFlags", "Meta.Roster.json"));
            StringAssert.Contains("\"meta.permadeath\": false", t);
            var snap = FeatureFlagLoader.Parse(t, "Meta.Roster.json", new CollectingErrorSink());
            Assert.IsFalse(snap.IsOn(RosterFlags.Permadeath));
        }

        [Test] public void Install_OnlyWhenFlagOnAndEconomyPresent()
        {
            var eco = new EconomyService(Kit.Prices()); var alloc = new EntityIdAllocator(); var reg = new ServiceRegistry();
            Assert.IsNull(RosterModule.Install(reg, FeatureFlagSnapshot.AllOff, eco, alloc));
            Assert.IsFalse(reg.IsRegistered<IRosterService>());
            var on = new FeatureFlagSnapshot(new[] { new KeyValuePair<string, bool>(RosterFlags.Permadeath, true) });
            Assert.IsNull(RosterModule.Install(reg, on, null, alloc));
            var svc = RosterModule.Install(reg, on, eco, alloc);
            Assert.IsNotNull(svc); Assert.AreSame(svc, reg.Resolve<IRosterService>());
        }

        // ---------------------------------------------------------------- create, queries

        [Test] public void Create_IssuesUniqueIdsAndTracksPerSlot()
        {
            var r = Kit.New(out _);
            var a = r.Create(Kit.S0, "balanced", 11, 0); var b = r.Create(Kit.S0, "heavy_brawler", 12, 1); var c = r.Create(Kit.S1, "balanced", 13, 1);
            Assert.AreNotEqual(a.MechId, b.MechId); Assert.AreNotEqual(b.MechId, c.MechId);
            Assert.AreEqual(2, r.FlyableCount(Kit.S0)); Assert.AreEqual(1, r.FlyableCount(Kit.S1));
            Assert.AreEqual(a.MechId, r.ActiveMech(Kit.S0)); Assert.AreEqual(c.MechId, r.ActiveMech(Kit.S1));
            var list = new List<MechInstance>(); r.Mechs(Kit.S0, list);
            CollectionAssert.AreEqual(new[] { a.MechId, b.MechId }, list.Select(x => x.MechId));
            Assert.AreEqual(MechState.Flyable, list[0].State); Assert.AreEqual(11, list[0].SpecHash);
            Assert.IsNull(r.Get(EntityId.FromValue(999)));
            Assert.Throws<ArgumentException>(() => r.Create(Kit.S0, "", 0, 0));
            Assert.Throws<ArgumentException>(() => r.Create(default, "balanced", 0, 0));
        }

        [Test] public void SetActive_OnlyFlyableOwned()
        {
            var r = Kit.New(out _); var a = r.Create(Kit.S0, "balanced", 0, 0); var b = r.Create(Kit.S0, "balanced", 0, 0); var o = r.Create(Kit.S1, "balanced", 0, 0);
            r.SetActive(Kit.S0, b.MechId); Assert.AreEqual(b.MechId, r.ActiveMech(Kit.S0));
            Assert.Throws<InvalidOperationException>(() => r.SetActive(Kit.S0, o.MechId));
            r.Retire(b.MechId, 5);
            Assert.AreEqual(a.MechId, r.ActiveMech(Kit.S0), "active falls back to the lowest flyable after a loss");
            Assert.Throws<InvalidOperationException>(() => r.SetActive(Kit.S0, b.MechId));
        }

        // ---------------------------------------------------------------- loss rule

        [Test] public void PlaceholderBody_DestroyedMeansLost_SurvivorCountsCycle()
        {
            var r = Kit.New(out _); var m = r.Create(Kit.S0, "balanced", 0, 0);
            var ok = r.OnFightEnd(Kit.Body(Kit.S0, 12.5), 10, 0.2);
            Assert.IsFalse(ok.Lost); Assert.AreEqual(1, r.Get(m.MechId).Cycles);
            var p = r.Pilot(Kit.S0); Assert.AreEqual(1, p.Sorties); Assert.AreEqual(0.2, p.Strain);
            var dead = r.OnFightEnd(Kit.Body(Kit.S0, 0), 20, 0.3);
            Assert.IsTrue(dead.Lost); Assert.AreEqual(LossCause.CoreDestroyed, dead.Cause); Assert.Greater(dead.WreckId, 0);
            var t = r.Get(m.MechId); Assert.AreEqual(MechState.Lost, t.State); Assert.AreEqual(dead.WreckId, t.WreckId); Assert.AreEqual(20, t.LossStep); Assert.AreEqual(1, t.Cycles);
        }

        [Test] public void LimbsLostAreNotALoss_CoreAtZeroIs()
        {
            var r = Kit.New(out _); var m = r.Create(Kit.S0, "balanced", 0, 0);
            var res = r.OnFightEnd(Kit.Full(Kit.S0, core: 40, limb: 0), 1);
            Assert.IsFalse(res.Lost); Assert.AreEqual(MechState.Flyable, r.Get(m.MechId).State);
            res = r.OnFightEnd(Kit.Full(Kit.S0, core: 0, limb: 100), 2);
            Assert.IsTrue(res.Lost);
            // reactor plant at zero counts too
            var r2 = Kit.New(out _); r2.Create(Kit.S0, "balanced", 0, 0);
            var rec = new Kit.Record(Kit.S0, ("cockpit_torso", 80), ("reactor", 0));
            Assert.IsTrue(r2.OnFightEnd(rec, 3).Lost);
        }

        [Test] public void OnFightEnd_RejectsBadRecords()
        {
            var r = Kit.New(out _); r.Create(Kit.S0, "balanced", 0, 0);
            Assert.Throws<ArgumentException>(() => r.OnFightEnd(new Kit.Record(Kit.S0, ("arm_l", 50)), 1), "no core part");
            Assert.Throws<ArgumentException>(() => r.OnFightEnd(Kit.Body(Kit.S0, double.NaN), 1));
            Assert.Throws<ArgumentException>(() => r.OnFightEnd(Kit.Body(Kit.S0, 5), 1, double.NaN));
            Assert.Throws<InvalidOperationException>(() => r.OnFightEnd(Kit.Body(Kit.S1, 5), 1), "slot without a mech");
            var id = r.ActiveMech(Kit.S0);
            Assert.Throws<ArgumentException>(() => r.OnFightEnd(id, Kit.Body(Kit.S1, 5), 1), "record of another slot");
            Assert.AreEqual(0, r.Get(id).Cycles); Assert.AreEqual(0, r.Pilot(Kit.S0).Sorties);
        }

        [Test] public void Retire_IsALossWithScrappedCause()
        {
            var bus = Kit.NewBus(); var col = new Kit.Collect(); bus.Subscribe(col);
            var r = Kit.New(out _, bus); r.Create(Kit.S0, "balanced", 0, 0); var m = r.Create(Kit.S0, "heavy_brawler", 0, 0);
            var t = r.Retire(m.MechId, 7);
            Assert.AreEqual(MechState.Lost, t.State); Assert.AreEqual(LossCause.Scrapped, t.Cause); Assert.Greater(t.WreckId, 0);
            Assert.AreEqual(1, col.Seen.Count); Assert.AreEqual((byte)LossCause.Scrapped, col.Seen[0].Cause);
            var again = r.Retire(m.MechId, 99);
            Assert.AreEqual(t.WreckId, again.WreckId); Assert.AreEqual(7, again.LossStep); Assert.AreEqual(1, col.Seen.Count);
            Assert.AreEqual(1, r.FlyableCount(Kit.S0)); Assert.AreEqual(0, r.FloorGrants(Kit.S0), "a flyable mech remains: no floor");
        }

        [Test] public void Retire_OfTheLastFlyableMech_GrantsTheFloor()
        {
            var r = Kit.New(out var eco); var m = r.Create(Kit.S0, "balanced", 0, 0);
            r.Retire(m.MechId, 4);
            Assert.AreEqual(1, r.FlyableCount(Kit.S0)); Assert.AreEqual(1, eco.FloorGrantCount(Kit.S0));
            Assert.IsTrue(r.Get(r.ActiveMech(Kit.S0)).IsFloor);
        }

        // ---------------------------------------------------------------- Lost is absorbing

        [Test] public void Lost_IsAbsorbing_NoOperationLeavesIt()
        {
            var bus = Kit.NewBus(); var col = new Kit.Collect(); bus.Subscribe(col);
            var r = Kit.New(out _, bus); r.Create(Kit.S0, "balanced", 0, 0); var m = r.ActiveMech(Kit.S0);
            var first = r.OnFightEnd(Kit.Body(Kit.S0, 0), 5);
            Assert.IsTrue(first.Lost); Assert.AreEqual(1, col.Seen.Count);
            var snap = r.Get(m);
            // every operation applicable to a lost mech leaves it lost, with the same wreck, and publishes nothing more
            var replay = r.OnFightEnd(m, Kit.Body(Kit.S0, 100), 6);   // even a "healthy" record cannot revive it
            Assert.IsTrue(replay.Replay); Assert.IsTrue(replay.Lost); Assert.AreEqual(first.WreckId, replay.WreckId);
            r.Retire(m, 7);
            Assert.Throws<InvalidOperationException>(() => r.SetActive(Kit.S0, m));
            var after = r.Get(m);
            Assert.AreEqual(MechState.Lost, after.State); Assert.AreEqual(snap.WreckId, after.WreckId); Assert.AreEqual(snap.Cycles, after.Cycles);
            Assert.AreEqual(snap.LossStep, after.LossStep); Assert.AreEqual(snap.Cause, after.Cause);
            Assert.AreEqual(1, col.Seen.Count, "MechLost is published once");
            Assert.AreEqual(1, r.Pilot(Kit.S0).MechsLost, "the pilot's loss count does not grow on a replay");
            Assert.AreEqual(1, r.Pilot(Kit.S0).Sorties, "a replay is not a sortie");
        }

        // ---------------------------------------------------------------- wreck id

        [Test] public void WreckId_IsDeterministicPositiveAndBounded()
        {
            long a = WreckIdentity.Derive(EntityId.FromValue(5), 100);
            Assert.AreEqual(a, WreckIdentity.Derive(EntityId.FromValue(5), 100));
            var seen = new HashSet<long>();
            for (ulong id = 1; id <= 200; id++) for (long step = 0; step < 100; step++)
            {
                long w = WreckIdentity.Derive(EntityId.FromValue(id), step);
                Assert.GreaterOrEqual(w, 1); Assert.LessOrEqual(w, WreckIdentity.Modulus);
                seen.Add(w);
            }
            Assert.GreaterOrEqual(seen.Count, 19_990, "20,000 (mech, step) pairs: at most a handful of collisions in a 10^12 space");
        }

        [Test] public void WreckId_CollisionIsProbedAndStaysUnique()
        {
            // Two mechs forced to the same (id, step) hash is impossible, so force the probe by making many losses and checking uniqueness.
            var r = Kit.New(out _); var ids = new HashSet<long>();
            for (int i = 0; i < 300; i++) { var m = r.Create(Kit.S0, "balanced", 0, 0); var t = r.Retire(m.MechId, 5); Assert.IsTrue(ids.Add(t.WreckId)); }
        }

        [Test] public void LossHandoff_ToSalvage_IsIdempotentAcrossReplayAndReload()
        {
            // The composition adapter (T-122) turns the loss into a SalvageInput keyed by MechInstance.WreckId; replay and reload cannot salvage twice.
            var t = Kit.Prices(); var eco = new EconomyService(t); var yields = YieldsTable.Parse(File.ReadAllText(Path.Combine(Kit.Root(), "Assets", "Game", "Config", "Salvage", "salvage_yields@v1.json")));
            var salvage = new SalvageService(yields, eco); var roster = new RosterService(eco, new EntityIdAllocator());
            var mech = roster.Create(Kit.S0, "balanced", 0, 0);
            var loss = roster.OnFightEnd(Kit.Body(Kit.S0, 0), 33);
            var wreck = new SalvageInput
            {
                WreckId = loss.WreckId, SourceKind = SalvageSourceKind.Mech, Location = SalvageLocation.Land, FallEnergyJPerKg = 392.0, BuildPriceUsd = 331_000_000,
                Parts = new[] { new SalvagePart("body", "structure", 3_600_000, 0, 0, 100, 1_000_000_000L / 1000) },
            };
            var first = salvage.Claim(wreck, 33); long funds = eco.Funds;
            Assert.IsFalse(first.Replay);
            Assert.IsTrue(salvage.Claim(wreck, 34).Replay);
            var replay = roster.OnFightEnd(mech.MechId, Kit.Body(Kit.S0, 0), 35);
            Assert.IsTrue(replay.Replay); Assert.AreEqual(loss.WreckId, replay.WreckId);
            Assert.IsTrue(salvage.Claim(wreck, 36).Replay); Assert.AreEqual(funds, eco.Funds);
        }

        // ---------------------------------------------------------------- floor (Q-206)

        [Test] public void Floor_GrantedOnlyWhenNoFlyableMechRemains()
        {
            var r = Kit.New(out var eco, funds: 0);
            var a = r.Create(Kit.S0, "balanced", 0, 0); var b = r.Create(Kit.S0, "heavy_brawler", 0, 0);
            var res = r.OnFightEnd(Kit.Body(Kit.S0, 0), 1);
            Assert.IsTrue(res.Lost); Assert.IsFalse(res.FloorGranted, "another flyable mech: no floor");
            Assert.AreEqual(b.MechId, r.ActiveMech(Kit.S0)); Assert.AreEqual(0, eco.FloorGrantCount(Kit.S0));
            Assert.IsNull(r.GrantFloor(Kit.S0, 2), "never stacked");
            res = r.OnFightEnd(Kit.Body(Kit.S0, 0), 3);
            Assert.IsTrue(res.Lost); Assert.IsTrue(res.FloorGranted);
            var f = r.Get(res.FloorMechId);
            Assert.IsTrue(f.IsFloor); Assert.AreEqual(MechState.Flyable, f.State); Assert.AreEqual("basic_balanced", f.Chassis);
            Assert.AreEqual(1, r.FlyableCount(Kit.S0)); Assert.AreEqual(1, eco.FloorGrantCount(Kit.S0)); Assert.AreEqual(1, r.FloorGrants(Kit.S0));
            Assert.AreEqual(0, eco.Funds, "the floor chassis is free and moves no funds");
            Assert.AreEqual(f.MechId, r.ActiveMech(Kit.S0));
            Assert.IsNull(r.GrantFloor(Kit.S0, 4));
            Assert.AreEqual(1, r.FloorGrants(Kit.S0));
            _ = a;
        }

        [Test] public void Floor_CannotBeScrapped_NoGrantAndScrapLoop()
        {
            var r = Kit.New(out _); r.Create(Kit.S0, "balanced", 0, 0);
            var res = r.OnFightEnd(Kit.Body(Kit.S0, 0), 1);
            Assert.Throws<InvalidOperationException>(() => r.Retire(res.FloorMechId, 2));
            Assert.AreEqual(MechState.Flyable, r.Get(res.FloorMechId).State);
        }

        [Test] public void Floor_LossOfTheFloorChassisGrantsAnother_NeverADeadEnd()
        {
            var r = Kit.New(out var eco); r.Create(Kit.S0, "balanced", 0, 0);
            for (int i = 0; i < 12; i++)
            {
                var res = r.OnFightEnd(Kit.Body(Kit.S0, 0), 10 + i);
                Assert.IsTrue(res.Lost); Assert.IsTrue(res.FloorGranted, "loss " + i);
                Assert.AreEqual(1, r.FlyableCount(Kit.S0));
            }
            Assert.AreEqual(12, eco.FloorGrantCount(Kit.S0)); Assert.AreEqual(12, r.FloorGrants(Kit.S0));
            Assert.AreEqual(13, new Func<int>(() => { var l = new List<MechInstance>(); r.Mechs(Kit.S0, l); return l.Count; })(), "tombstones stay in the roster");
        }

        [Test] public void Floor_IsPerSlot()
        {
            var r = Kit.New(out _); r.Create(Kit.S0, "balanced", 0, 0); r.Create(Kit.S1, "balanced", 0, 0);
            r.OnFightEnd(Kit.Body(Kit.S0, 0), 1);
            Assert.AreEqual(1, r.FloorGrants(Kit.S0)); Assert.AreEqual(0, r.FloorGrants(Kit.S1));
            Assert.AreEqual(1, r.FlyableCount(Kit.S1));
        }

        // ---------------------------------------------------------------- pilot

        [Test] public void Pilot_SurvivesALoss_WithStrainPenaltyAndOneSortieOfRecovery()
        {
            var r = Kit.New(out _); r.Create(Kit.S0, "balanced", 0, 0);
            r.OnFightEnd(Kit.Body(Kit.S0, 50), 1, 0.1);
            var res = r.OnFightEnd(Kit.Body(Kit.S0, 0), 2, 0.2);
            var p = r.Pilot(Kit.S0);
            Assert.IsTrue(res.Lost); Assert.AreEqual(0.5, p.Strain, "full strain 0.5 after a loss"); Assert.AreEqual(1, p.RecoverySorties); Assert.AreEqual(1, p.MechsLost); Assert.AreEqual(2, p.Sorties);
            r.OnFightEnd(Kit.Body(Kit.S0, 80), 3, 0.15);   // first sortie in the floor chassis
            p = r.Pilot(Kit.S0);
            Assert.AreEqual(0, p.RecoverySorties); Assert.AreEqual(0.15, p.Strain); Assert.AreEqual(3, p.Sorties);
            // a pilot already above 0.5 keeps the higher strain
            r.OnFightEnd(Kit.Body(Kit.S0, 0), 4, 0.9);
            Assert.AreEqual(0.9, r.Pilot(Kit.S0).Strain);
            r.OnFightEnd(Kit.Body(Kit.S0, 80), 5, 7.0);   // clamped
            Assert.AreEqual(1.0, r.Pilot(Kit.S0).Strain);
        }

        [Test] public void Pilot_RecordIsPerSlot()
        {
            var r = Kit.New(out _); r.Create(Kit.S0, "balanced", 0, 0); r.Create(Kit.S1, "balanced", 0, 0);
            r.OnFightEnd(Kit.Body(Kit.S0, 5), 1, 0.4);
            Assert.AreEqual(1, r.Pilot(Kit.S0).Sorties); Assert.AreEqual(0, r.Pilot(Kit.S1).Sorties);
        }

        // ---------------------------------------------------------------- event

        [Test] public void MechLost_PublishedOnceWithTheLossFacts()
        {
            var bus = Kit.NewBus(); var col = new Kit.Collect(); bus.Subscribe(col);
            var r = Kit.New(out _, bus); var m = r.Create(Kit.S1, "balanced", 0, 0);
            var res = r.OnFightEnd(Kit.Body(Kit.S1, 0), 77);
            r.OnFightEnd(m.MechId, Kit.Body(Kit.S1, 0), 78);
            Assert.AreEqual(1, col.Seen.Count);
            var e = col.Seen[0];
            Assert.AreEqual(m.MechId, e.MechId); Assert.AreEqual(Kit.S1, e.Slot); Assert.AreEqual(res.WreckId, e.WreckId); Assert.AreEqual((byte)LossCause.CoreDestroyed, e.Cause); Assert.AreEqual(77, e.Step);
        }

        // ---------------------------------------------------------------- randomised invariants

        [Test] public void Invariants_30Seeds_NeverNoFlyable_LostNeverReverts_WreckIdsUnique()
        {
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var rng = new Kit.Rng(seed); var r = Kit.New(out var eco);
                var slots = new[] { Kit.S0, Kit.S1, PlayerSlot.FromIndex(2) };
                foreach (var s in slots) r.Create(s, "balanced", 0, 0);
                var lost = new Dictionary<ulong, long>(); var wrecks = new HashSet<long>(); long step = 0;
                for (int i = 0; i < 300; i++)
                {
                    step += 1 + rng.Below(5); var s = slots[rng.Below(slots.Length)];
                    switch (rng.Below(4))
                    {
                        case 0: r.Create(s, rng.Below(2) == 0 ? "balanced" : "heavy_brawler", 0, step); break;
                        case 1: { var l = new List<MechInstance>(); r.Mechs(s, l); var f = l.Where(x => x.State == MechState.Flyable && !x.IsFloor).ToList(); if (f.Count > 0) r.Retire(f[rng.Below(f.Count)].MechId, step); break; }
                        default: r.OnFightEnd(Kit.Body(s, rng.Unit() < 0.25 ? 0 : 1 + rng.Below(100)), step, rng.Unit()); break;
                    }
                    foreach (var sl in slots)
                    {
                        var l = new List<MechInstance>(); r.Mechs(sl, l);
                        foreach (var m in l)
                        {
                            if (m.State == MechState.Lost) { if (!lost.ContainsKey(m.MechId.Value)) { lost[m.MechId.Value] = m.WreckId; Assert.IsTrue(wrecks.Add(m.WreckId), "wreck ids are unique"); } else Assert.AreEqual(lost[m.MechId.Value], m.WreckId); }
                            else Assert.IsFalse(lost.ContainsKey(m.MechId.Value), "a lost mech became flyable again");
                        }
                    }
                    // after any fight-end the slot has a flyable mech (the floor is granted when a loss leaves none)
                    foreach (var sl in slots) Assert.GreaterOrEqual(r.FlyableCount(sl), 1, "seed " + seed + " step " + i);
                }
                Assert.GreaterOrEqual(eco.Funds, 0);
            }
        }

        // ---------------------------------------------------------------- save: roster@v1

        static (SaveSystem sys, RosterService svc, EntityIdAllocator alloc) Fresh()
        {
            var alloc = new EntityIdAllocator(); var svc = new RosterService(new EconomyService(Kit.Prices()), alloc);
            var sys = new SaveSystem("t"); sys.Register(svc); return (sys, svc, alloc);
        }

        [Test] public void Save_RoundTrip_AndAllocatorNeverReissuesALoadedId()
        {
            var (sys1, a, _) = Fresh();
            a.Create(Kit.S0, "balanced", 0xABCDEF, 1); a.Create(Kit.S0, "heavy_brawler", -5, 2); a.Create(Kit.S1, "balanced", 0, 3);
            a.OnFightEnd(Kit.Body(Kit.S0, 70), 10, 0.3); a.OnFightEnd(Kit.Body(Kit.S0, 0), 11, 0.4);
            a.Retire(a.ActiveMech(Kit.S1), 12);
            var (sys2, b, alloc2) = Fresh();
            Assert.IsTrue(sys2.Restore(sys1.Capture()).Ok);
            foreach (var s in new[] { Kit.S0, Kit.S1 })
            {
                var la = new List<MechInstance>(); var lb = new List<MechInstance>(); a.Mechs(s, la); b.Mechs(s, lb);
                Assert.AreEqual(la.Count, lb.Count);
                for (int i = 0; i < la.Count; i++)
                {
                    Assert.AreEqual(la[i].MechId, lb[i].MechId); Assert.AreEqual(la[i].Chassis, lb[i].Chassis); Assert.AreEqual(la[i].State, lb[i].State);
                    Assert.AreEqual(la[i].SpecHash, lb[i].SpecHash); Assert.AreEqual(la[i].WreckId, lb[i].WreckId); Assert.AreEqual(la[i].Cycles, lb[i].Cycles);
                    Assert.AreEqual(la[i].IsFloor, lb[i].IsFloor); Assert.AreEqual(la[i].Cause, lb[i].Cause); Assert.AreEqual(la[i].LossStep, lb[i].LossStep);
                }
                var pa = a.Pilot(s); var pb = b.Pilot(s);
                Assert.AreEqual(BitConverter.DoubleToInt64Bits(pa.Strain), BitConverter.DoubleToInt64Bits(pb.Strain)); Assert.AreEqual(pa.Sorties, pb.Sorties);
                Assert.AreEqual(pa.RecoverySorties, pb.RecoverySorties); Assert.AreEqual(pa.MechsLost, pb.MechsLost);
                Assert.AreEqual(a.FloorGrants(s), b.FloorGrants(s)); Assert.AreEqual(a.ActiveMech(s), b.ActiveMech(s));
            }
            Assert.GreaterOrEqual(alloc2.NextValue, a.NextEntityValue, "the allocator was advanced past every loaded id");
            var fresh = b.Create(Kit.S1, "balanced", 0, 20);
            Assert.IsNull(a.Get(fresh.MechId), "a new mech never reuses a saved id");
        }

        [Test] public void Save_CorruptSectionsAreRejectedAndServiceUntouched()
        {
            var (sys, svc, _) = Fresh();
            svc.Create(Kit.S0, "balanced", 1, 1); svc.Create(Kit.S0, "balanced", 2, 1); svc.Create(Kit.S1, "balanced", 3, 1);
            svc.OnFightEnd(Kit.Body(Kit.S0, 50), 5, 0.2); svc.OnFightEnd(Kit.Body(Kit.S0, 0), 6, 0.1); svc.OnFightEnd(Kit.Body(Kit.S0, 0), 7, 0.1);
            var l0 = new List<MechInstance>(); svc.Mechs(Kit.S0, l0);
            Assert.IsTrue(l0.Any(x => x.IsFloor) && l0.Any(x => x.State == MechState.Lost));
            string Digest() { var d = sys.Capture(); d.TryGetSection("roster", out var s); return string.Join("|", s.Records.Select(r => r.Key + ":" + string.Join(",", r.FieldNames.OrderBy(f => f, StringComparer.Ordinal).Select(f => f + "=" + r.Get(f).Kind + r.Get(f).GetHashCode())))); }
            string before = Digest();
            SaveRecord Rec(SaveSection s, Func<SaveRecord, bool> pick) => s.Records.First(pick);
            bool Lost(SaveRecord r) => r.Key.StartsWith("mech:") && r.Get("state").AsInt64 == 1;
            bool Flyable(SaveRecord r) => r.Key.StartsWith("mech:") && r.Get("state").AsInt64 == 0;
            bool Floor(SaveRecord r) => r.Key.StartsWith("mech:") && r.Get("floor").AsBool;
            var cases = new List<Action<SaveSection>>
            {
                s => s.Remove("totals"),
                s => { s.TryGet("totals", out var r); r.Set("mechs", SaveValue.Of(r.Get("mechs").AsInt64 + 1)); },
                s => { s.TryGet("totals", out var r); r.Set("next_entity", SaveValue.Of(1L)); },                                 // ids not below next_entity
                s => { s.TryGet("totals", out var r); r.Set("next_entity", SaveValue.Of(1.5)); },
                s => Rec(s, Lost).Set("wreck_id", SaveValue.Of(0L)),
                s => Rec(s, Lost).Set("cause", SaveValue.Of(0L)),
                s => Rec(s, Lost).Set("cause", SaveValue.Of(7L)),
                s => Rec(s, Lost).Set("state", SaveValue.Of(2L)),
                s => Rec(s, Lost).Set("wreck_id", SaveValue.Of(WreckIdentity.Modulus + 1)),
                s => Rec(s, Flyable).Set("wreck_id", SaveValue.Of(5L)),                                                         // flyable with loss data
                s => Rec(s, Flyable).Set("cause", SaveValue.Of(1L)),
                s => Rec(s, Flyable).Set("cycles", SaveValue.Of(-1L)),
                s => Rec(s, Flyable).Set("chassis", SaveValue.Of("")),
                s => Rec(s, Flyable).Set("chassis", SaveValue.Of(3L)),
                s => Rec(s, Flyable).Set("slot", SaveValue.Of(-2L)),
                s => Rec(s, Flyable).Set("slot", SaveValue.Of(9L)),                                                             // slot without a pilot record
                s => Rec(s, Floor).Set("floor", SaveValue.Of(false)),                                                           // floor count no longer matches
                s => s.Records.First(r => r.Key.StartsWith("floor:")).Set("granted_count", SaveValue.Of(5L)),
                s => s.Remove(s.Records.First(r => r.Key.StartsWith("floor:")).Key),
                s => s.Records.First(r => r.Key.StartsWith("pilot:slot:0")).Set("lost", SaveValue.Of(0L)),
                s => s.Records.First(r => r.Key.StartsWith("pilot:slot:0")).Set("strain", SaveValue.Of(1.5)),
                s => s.Records.First(r => r.Key.StartsWith("pilot:slot:0")).Set("strain", SaveValue.Of(double.NaN)),
                s => s.Records.First(r => r.Key.StartsWith("pilot:slot:0")).Set("sorties", SaveValue.Of(0L)),                      // fewer sorties than fights fought
                s => s.Records.First(r => r.Key.StartsWith("pilot:slot:0")).Set("recovery", SaveValue.Of(2L)),
                s => s.Records.First(r => r.Key.StartsWith("pilot:slot:0")).Set("active", SaveValue.Of(Rec(s, Lost).Key.Length > 0 ? long.Parse(Rec(s, Lost).Key.Substring(5)) : 0L)),   // active is a lost mech
                s => s.Records.First(r => r.Key.StartsWith("pilot:slot:0")).Set("active", SaveValue.Of(77L)),
                s => s.Add("mech:000000000099").Set("slot", SaveValue.Of(0L)),
                s => s.Add("mystery:1"),
                s => s.Add("floor:slot:x").Set("granted_count", SaveValue.Of(1L)),
            };
            int n = 0;
            foreach (var edit in cases)
            {
                var good = sys.Capture(); edit(good.Sections.First(x => x.Name == "roster"));
                var res = sys.Restore(good);
                Assert.AreEqual(SaveLoadStatus.Corrupt, res.Status, "case " + n + ": " + res.Message);
                Assert.AreEqual(before, Digest(), "service untouched after case " + n);
                n++;
            }
        }
    }
}
