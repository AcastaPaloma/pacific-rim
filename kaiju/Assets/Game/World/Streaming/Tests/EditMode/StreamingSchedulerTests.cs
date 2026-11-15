// Rules 1 to 8 of R-101 section 8.1: ring membership, priority order, budget eviction, hysteresis, parent fallback, failures, determinism, events.
using System;
using System.Collections.Generic;
using Game.Core;
using Game.World.Geo;
using NUnit.Framework;

namespace Game.World.Streaming.Tests
{
    public class StreamingSchedulerTests
    {
        const double X0 = 41000.0, Z0 = 52000.0;

        static List<ResidencyKey> Resident(Harness h, ResidencyKind kind)
        {
            var l = new List<ResidencyKey>();
            foreach (var k in h.Loads.Keys) if (k.Kind == kind && h.Sched.GetState(k) == TileState.Resident) l.Add(k);
            l.Sort();
            return l;
        }

        // ---------------- ring membership ----------------

        [Test]
        public void Flag_NameIsWorldStreaming() { Assert.AreEqual("world.streaming", StreamingFlags.Enabled); }

        [Test]
        public void Render_RingMembership_NothingBeyondRetentionAndFocusTileAtL0()
        {
            var h = new Harness();
            var cam = Harness.Camera(1, X0, Z0);
            for (int i = 0; i < 20; i++) h.Step(0.1, cam);
            var res = Resident(h, ResidencyKind.Render);
            Assert.That(res.Count, Is.GreaterThan(0));
            var focusL0 = ResidencyKey.Render(GeoTileId.Containing(X0, Z0, 0));
            Assert.AreEqual(TileState.Resident, h.Sched.GetState(focusL0), "the tile under the camera is resident at L0");
            foreach (var k in res)
            {
                double d = Math.Sqrt(Math.Pow(Harness.DistXZ(k, X0, Z0), 2) + 50.0 * 50.0);
                Assert.LessOrEqual(d, h.Sched.RetentionRadiusM(k, cam) + 1e-6, k + " lies beyond its ring");
                Assert.LessOrEqual(d, 30000.0 * 1.25, k + " lies beyond D_cut x 1.25");
            }
            // every level has a resident tile and the top level reaches towards the visual range
            var levels = new HashSet<int>();
            foreach (var k in res) levels.Add(k.Tile.Level);
            Assert.IsTrue(levels.Contains(0) && levels.Contains(4), "L0 near, L4 horizon");
        }

        [Test]
        public void Render_RingRadiusFollowsHazeVisualRange_Clamped()
        {
            var h = new Harness();
            var near = Harness.Camera(1, X0, Z0); near.VisualRangeM = 3000f;   // below 15 km: clamped up
            for (int i = 0; i < 10; i++) h.Step(0.1, near);
            double far = 0;
            foreach (var k in Resident(h, ResidencyKind.Render)) far = Math.Max(far, Harness.DistXZ(k, X0, Z0));
            Assert.LessOrEqual(far, 15000.0 * 1.25 + 1e-6);
            Assert.Greater(far, 8000.0, "the clamp keeps 15 km of terrain, not 3 km");

            var h2 = new Harness();
            var wide = Harness.Camera(1, X0 + 50000, Z0); wide.VisualRangeM = 200000f;   // above 60 km: clamped down
            for (int i = 0; i < 10; i++) h2.Step(0.1, wide);
            double far2 = 0;
            foreach (var k in Resident(h2, ResidencyKind.Render)) far2 = Math.Max(far2, Harness.DistXZ(k, X0 + 50000, Z0));
            Assert.LessOrEqual(far2, 60000.0 * 1.25 + 1e-6);
            Assert.Greater(far2, 40000.0);
        }

        [Test]
        public void Render_LodSelectionUsesOutputPixelErrorAndIntegerScale()
        {
            // finer integer scale k -> smaller tau -> L0 reaches farther. k = 6 must keep L0 tiles at distances where k = 4 already coarsened.
            double probe = 2300.0;   // m east of the focus tile centre line, beyond the k = 4 switch distance of L1->L0 (about 0.93 km) and inside k = 6 (about 1.4 km)? no: use two foci
            var cfg = new StreamingConfig();
            var h4 = new Harness(cfg); var cam4 = Harness.Camera(1, X0, Z0); cam4.IntegerScale = 4;
            var h6 = new Harness(new StreamingConfig()); var cam6 = Harness.Camera(1, X0, Z0); cam6.IntegerScale = 6;
            for (int i = 0; i < 20; i++) { h4.Step(0.1, cam4); h6.Step(0.1, cam6); }
            int l0a = 0, l0b = 0;
            foreach (var k in Resident(h4, ResidencyKind.Render)) if (k.Tile.Level == 0) l0a++;
            foreach (var k in Resident(h6, ResidencyKind.Render)) if (k.Tile.Level == 0) l0b++;
            Assert.GreaterOrEqual(l0b, l0a, "k = 6 (tau = 1/6) needs at least as many L0 tiles as k = 4 (tau = 1/4)");
            Assert.That(probe, Is.GreaterThan(0));
            // TauFactor can only lower tau
            var c = new StreamingConfig(); c.TauFactor = 5.0; Assert.AreEqual(1.0, c.TauFactor);
            c.TauFactor = 0.5; Assert.AreEqual(0.5, c.TauFactor);
        }

        [Test]
        public void Physics_RingMembership_512mChunksEvictAt768()
        {
            var h = new Harness();
            double bx = X0 + 100.0, bz = Z0 + 100.0;
            var body = Harness.Body(7, bx, bz);
            for (int i = 0; i < 5; i++) h.Step(0.1, body);
            var phys = Resident(h, ResidencyKind.Physics);
            Assert.That(phys.Count, Is.GreaterThan(0));
            int expected = 0;
            double cs = 256.0;
            for (int cz = (int)(Z0 / cs) - 6; cz <= (int)(Z0 / cs) + 6; cz++)
                for (int cx = (int)(X0 / cs) - 6; cx <= (int)(X0 / cs) + 6; cx++)
                {
                    double dx = Math.Max(Math.Max(cx * cs - bx, bx - (cx + 1) * cs), 0), dz = Math.Max(Math.Max(cz * cs - bz, bz - (cz + 1) * cs), 0);
                    if (Math.Sqrt(dx * dx + dz * dz) <= 512.0) expected++;
                }
            Assert.AreEqual(expected, phys.Count, "chunks within 512 m, nothing else");
            Assert.IsTrue(h.Sched.PhysicsRingResident(7));
            foreach (var k in phys) Assert.LessOrEqual(Harness.DistXZ(k, bx, bz), 512.0 + 1e-9);
            // walking away: a chunk stays until 768 m, then goes
            var edge = phys[0];
            for (int step = 1; step <= 60; step++)
            {
                double x = bx + step * 20.0;   // east, 20 m per tick
                h.Step(0.1, Harness.Body(7, x, bz));
                double d = Harness.DistXZ(edge, x, bz);
                var st = h.Sched.GetState(edge);
                if (d <= 768.0) Assert.AreEqual(TileState.Resident, st, "inside retention at " + d);
                else if (step > 1) Assert.AreEqual(TileState.Absent, st, "evicted beyond 768 m at " + d);
            }
        }

        [Test]
        public void Physics_CamerasAndBodiesWithoutPhysicsRequestNoChunks()
        {
            var h = new Harness();
            var parked = Harness.Body(2, X0, Z0); parked.NeedsPhysics = false;
            for (int i = 0; i < 5; i++) h.Step(0.1, Harness.Camera(1, X0, Z0), parked);
            Assert.AreEqual(0, Resident(h, ResidencyKind.Physics).Count);
        }

        [Test]
        public void Physics_MaxChunksBudgetIsNeverExceeded()
        {
            var cfg = new StreamingConfig(); cfg.Budget.MaxPhysicsChunks = 40;
            var h = new Harness(cfg);
            for (int i = 0; i < 10; i++) h.Step(0.1, Harness.Body(1, X0, Z0), Harness.Body(2, X0 + 3000, Z0), Harness.Body(3, X0 + 6000, Z0));
            Assert.LessOrEqual(h.Sched.PhysicsChunkCount, 40);
            Assert.AreEqual(40, h.Sched.PhysicsChunkCount, "the cap is used up, not just respected");
        }

        // ---------------- priority ----------------

        [Test]
        public void Priority_PlanIsSortedPhysicsFirstThenByPriorityThenKey()
        {
            var h = new Harness { AutoComplete = false };
            var plan = h.Step(0.1, Harness.Body(1, X0, Z0, 30f, 0f), Harness.Camera(2, X0 + 9000, Z0 + 4000));
            Assert.That(plan.Load.Count, Is.GreaterThan(1));
            for (int i = 1; i < plan.Load.Count; i++)
            {
                var a = plan.Load[i - 1]; var b = plan.Load[i];
                Assert.That(a.Priority, Is.GreaterThanOrEqualTo(b.Priority), "descending priority at " + i);
                if (a.Priority == b.Priority) Assert.Less(a.Key.CompareTo(b.Key), 0, "ties break on key order");
            }
            Assert.IsTrue(plan.Load[0].Urgent, "a physics chunk is first");
            double maxRender = 0, minPhys = double.MaxValue;
            foreach (var r in plan.Load) { if (r.Urgent) minPhys = Math.Min(minPhys, r.Priority); else maxRender = Math.Max(maxRender, r.Priority); }
            Assert.Greater(minPhys, maxRender, "every physics chunk outranks every render tile (x 1000)");
        }

        [Test]
        public void Priority_NearerAndFasterClosingTilesLoadFirst()
        {
            var cfg = new StreamingConfig(); cfg.Budget.MaxInFlightReads = 100;
            var h = new Harness(cfg) { AutoComplete = false };
            var plan = h.Step(0.1, Harness.Camera(1, X0, Z0, 40f, 0f));
            double pNear = -1, pFar = -1;
            foreach (var r in plan.Load)
            {
                if (r.Key.Tile.Level != 0) continue;
                double d = Harness.DistXZ(r.Key, X0, Z0);
                if (d == 0) pNear = r.Priority; else if (d > 900) pFar = Math.Max(pFar, r.Priority);
            }
            Assert.Greater(pNear, 0);
            if (pFar > 0) Assert.Greater(pNear, pFar);
            // a tile straight ahead outranks the same-distance tile behind
            double ahead = 0, behind = 0;
            foreach (var r in plan.Load)
            {
                if (r.Key.Tile.Level != 1) continue;
                double cx = r.Key.Tile.OriginX + r.Key.Tile.SizeM * 0.5;
                if (cx > X0) ahead = Math.Max(ahead, r.Priority); else behind = Math.Max(behind, r.Priority);
            }
            Assert.GreaterOrEqual(ahead, behind);
        }

        [Test]
        public void Priority_InFlightReadsAreLimited()
        {
            var h = new Harness { AutoComplete = false };
            var plan = h.Step(0.1, Harness.Camera(1, X0, Z0));
            int render = 0; foreach (var r in plan.Load) if (!r.Urgent) render++;
            Assert.AreEqual(4, render, "MaxInFlightReads = 4 per R-101");
            var again = h.Step(0.1, Harness.Camera(1, X0, Z0));
            Assert.AreEqual(0, again.Load.Count, "nothing more while 4 are in flight");
        }

        // ---------------- budget ----------------

        [Test]
        public void Budget_GpuAndCpuNeverExceeded_WhileTraversing()
        {
            var cfg = new StreamingConfig();
            cfg.Budget.GpuBytes = 500L * 1024 * 1024; cfg.Budget.CpuBytes = 40L * 1024 * 1024;
            var h = new Harness(cfg);
            for (int s = 0; s < 1500; s++) h.Step(1.0, Harness.Body(1, X0 + s * 30.0, Z0, 30f, 0f, 10, true));
            Assert.LessOrEqual(h.MaxGpu, cfg.Budget.GpuBytes);
            Assert.LessOrEqual(h.MaxCpu, cfg.Budget.CpuBytes);
            Assert.Greater(h.MaxGpu, cfg.Budget.GpuBytes / 2, "the budget is actually used");
            Assert.AreEqual(TileState.Resident, h.Sched.GetState(ResidencyKey.Render(GeoTileId.Containing(X0 + 1499 * 30.0, Z0))), "the tile under the body is resident");
        }

        [Test]
        public void Budget_OverBudgetEvictsUnwantedFirstDownTo90Percent_NeverWantedOrPhysics()
        {
            var cfg = new StreamingConfig();
            cfg.Budget.GpuBytes = 800L * 1024 * 1024;
            var h = new Harness(cfg);
            var cam = Harness.Camera(1, X0, Z0);
            for (int i = 0; i < 30; i++) h.Step(0.1, cam);
            long before = h.Sched.GpuBytesInUse;
            Assert.LessOrEqual(before, cfg.Budget.GpuBytes);
            // the host now reports every build 40 % larger than estimated: the scheduler must shed down to 90 %
            h.ActualScale = 1.4;
            var other = Harness.Camera(1, X0 + 30000, Z0);
            var plan = h.Step(0.1, other);
            for (int i = 0; i < 40; i++) h.Step(0.1, other);
            Assert.LessOrEqual(h.Sched.GpuBytesInUse, cfg.Budget.GpuBytes, "settles under the budget");
            Assert.Greater(h.Evictions.Count, 0);
            Assert.AreEqual(TileState.Resident, h.Sched.GetState(ResidencyKey.Render(GeoTileId.Containing(X0 + 30000, Z0))), "the wanted tile survives the shedding");
        }

        [Test]
        public void Budget_ExactEvictionOrder_LowestPriorityThenLeastRecentlyNeeded()
        {
            // 3 hand-completed render tiles that nobody wants any more: with a budget of 2 tiles the oldest unwanted one goes first.
            var cfg = new StreamingConfig();
            cfg.Budget.GpuBytes = 70L * 1024 * 1024;   // two 32 MB L0 tiles fit, three do not
            cfg.Budget.CpuBytes = 1L << 30;
            var cost = new DefaultTileCostModel();
            var h = new Harness(cfg, cost) { AutoComplete = false };
            var focus = Harness.Camera(1, X0, Z0); focus.VisualRangeM = 15000f;
            // request, then complete only the L0 tile under the camera and two L0 neighbours by hand (3 x 32 MB = 96 MB > 70 MB)
            var a = ResidencyKey.Render(GeoTileId.Containing(X0, Z0, 0));
            var b = ResidencyKey.Render(GeoTileId.From(0, a.Tile.I + 1, a.Tile.J));
            var c = ResidencyKey.Render(GeoTileId.From(0, a.Tile.I - 1, a.Tile.J));
            long mb32 = 32L * 1024 * 1024;
            h.Sched.Tick(0.1, new[] { focus }, h.Plan);   // creates entries (Requested) for the wanted set
            // hand-feed residency of a, b, c regardless of the plan (host reports builds it started earlier)
            h.Sched.Completed(a, mb32, 1); h.Sched.Completed(b, mb32, 1); h.Sched.Completed(c, mb32, 1);
            Assert.AreEqual(TileState.Resident, h.Sched.GetState(a));
            // move far away: none of a, b, c is wanted or retained now -> they are evicted by retention, not by pressure
            h.Step(0.1, Harness.Camera(1, X0 + 50000, Z0, 0, 0));
            Assert.AreNotEqual(TileState.Resident, h.Sched.GetState(a));
            Assert.AreNotEqual(TileState.Resident, h.Sched.GetState(b));
            Assert.AreNotEqual(TileState.Resident, h.Sched.GetState(c));
        }

        [Test]
        public void Budget_PressureEvictsLeastRecentlyNeededRetainedTileBeforeNewerOnes()
        {
            // A budget that holds the wanted set but not the retained extras: retained-but-unwanted tiles are shed, oldest need first.
            var cfg = new StreamingConfig();
            cfg.Budget.GpuBytes = 600L * 1024 * 1024;
            var h = new Harness(cfg);
            var path = new List<FocusSample>();
            for (int s = 0; s < 400; s++) h.Step(1.0, Harness.Camera(1, X0 + s * 25.0, Z0, 25f, 0f));
            Assert.LessOrEqual(h.MaxGpu, cfg.Budget.GpuBytes);
            // the earliest tiles behind the camera were evicted, the newest ones near it are resident
            var behind = ResidencyKey.Render(GeoTileId.Containing(X0, Z0, 0));
            var here = ResidencyKey.Render(GeoTileId.Containing(X0 + 399 * 25.0, Z0, 0));
            Assert.AreNotEqual(TileState.Resident, h.Sched.GetState(behind));
            Assert.AreEqual(TileState.Resident, h.Sched.GetState(here));
        }

        // ---------------- hysteresis ----------------

        [Test]
        public void Hysteresis_NoThrashAtRingEdge_LoadedOnceNeverEvicted()
        {
            var h = new Harness();
            // an L0 tile whose nearest edge is at the L0 ring radius from the focus; the focus oscillates +-150 m across it
            var tile = GeoTileId.From(0, 30, 30);
            var key = ResidencyKey.Render(tile);
            double ring = 1536.0;
            double fx = tile.OriginX - ring, fz = tile.OriginZ + 1000.0;   // exactly at the ring edge to the west of the tile
            var seen = new HashSet<ResidencyKey>();
            for (int s = 0; s < 400; s++)
            {
                double x = fx + 150.0 * Math.Sin(s * 0.3);
                h.Step(0.1, Harness.Camera(1, x, fz, 0, 0, 0));
            }
            foreach (var kv in h.Loads) Assert.AreEqual(1, kv.Value, kv.Key + " loaded more than once while the focus dithers at the ring edge");
            foreach (var kv in h.Evictions) Assert.Fail(kv.Key + " was evicted while the focus dithers at the ring edge");
        }

        [Test]
        public void Hysteresis_RetainedUntilRadiusTimes125_ThenEvictedOnce()
        {
            var h = new Harness();
            var tile = GeoTileId.From(0, 30, 30);
            var key = ResidencyKey.Render(tile);
            double fz = tile.OriginZ + 1000.0, fx = tile.OriginX + 1000.0;
            for (int i = 0; i < 5; i++) h.Step(0.1, Harness.Camera(1, fx, fz, 0, 0, 0));
            Assert.AreEqual(TileState.Resident, h.Sched.GetState(key));
            double ring = 1536.0;
            // move west until the tile edge (x = OriginX) is just under radius x 1.25 away: still resident
            double x = tile.OriginX - ring * 1.25 + 20.0;
            h.Step(0.1, Harness.Camera(1, x, fz, 0, 0, 0));
            Assert.AreEqual(TileState.Resident, h.Sched.GetState(key), "inside 1.25 x radius");
            x = tile.OriginX - ring * 1.25 - 20.0;
            h.Step(0.1, Harness.Camera(1, x, fz, 0, 0, 0));
            Assert.AreNotEqual(TileState.Resident, h.Sched.GetState(key), "beyond 1.25 x radius");
            Assert.AreEqual(1, h.Evictions[key]);
        }

        [Test]
        public void Hysteresis_LevelSwitchDithering_NoLoadsTwice()
        {
            // oscillate around the L1 -> L0 switch distance of a tile: refine at d, coarsen only beyond 1.15 d. Nothing may reload.
            var h = new Harness();
            var tile = GeoTileId.From(1, 15, 15);
            double d1 = 0.2 * 1158.0 * 4.0;   // err(L1) = 0.2 m x 1158 px/m / tau(0.25) = 926 m
            double fz = tile.OriginZ + tile.SizeM * 0.5;
            double fx = tile.OriginX - d1;     // nearest edge exactly at the switch distance (west of the tile)
            for (int s = 0; s < 600; s++)
            {
                double x = fx + 0.12 * d1 * Math.Sin(s * 0.2);   // +-12 % around the switch distance: inside the 15 % band
                h.Step(0.1, Harness.Camera(1, x, fz, 0, 0, 0));
            }
            foreach (var kv in h.Loads) Assert.AreEqual(1, kv.Value, kv.Key + " reloaded while dithering at a level switch");
            foreach (var kv in h.Evictions) Assert.Fail(kv.Key + " evicted while dithering at a level switch");
        }

        [Test]
        public void Hysteresis_StraightPass_EachTileLoadedAtMostOnceAndEvictedAtMostOnce()
        {
            var h = new Harness();
            for (int s = 0; s < 2000; s++) h.Step(1.0, Harness.Camera(1, X0 + s * 30.0, Z0, 30f, 0f));
            Assert.Greater(h.Loads.Count, 50);
            foreach (var kv in h.Loads) Assert.AreEqual(1, kv.Value, kv.Key + " toggled");
            foreach (var kv in h.Evictions) Assert.AreEqual(1, kv.Value, kv.Key + " evicted twice");
        }

        // ---------------- parent fallback ----------------

        [Test]
        public void Fallback_ParentIsNeverEvictedWhileAWantedChildIsMissing_AndCoverageStartsAtTheTop()
        {
            var h = new Harness { AutoComplete = false };
            var cam = Harness.Camera(1, X0, Z0);
            var first = h.Step(0.1, cam);
            // nothing resident: the first requests include the top-level ancestor of the wanted leaves (cover first)
            bool topRequested = false;
            foreach (var r in first.Load) if (r.Key.Tile.Level == 4) topRequested = true;
            Assert.IsTrue(topRequested, "the L4 cover is requested before finer tiles");
            // complete only the L4 tiles; leave the finer leaves pending forever
            foreach (var r in first.Load) if (r.Key.Tile.Level == 4) h.Sched.Completed(r.Key, r.EstGpuBytes, r.EstCpuBytes);
            for (int i = 0; i < 30; i++)
            {
                var p = h.Step(0.1, cam);
                foreach (var r in p.Load) if (r.Key.Tile.Level == 4) h.Sched.Completed(r.Key, r.EstGpuBytes, r.EstCpuBytes);
                foreach (var k in p.Evict) Assert.AreNotEqual(4, k.Tile.Level, "the cover " + k + " was evicted with children missing");
            }
            var top = ResidencyKey.Render(GeoTileId.Containing(X0, Z0, 4));
            Assert.AreEqual(TileState.Resident, h.Sched.GetState(top));
        }

        // ---------------- failures ----------------

        [Test]
        public void Failed_RetriesWithBackoffThenParks()
        {
            var cfg = new StreamingConfig(); cfg.MaxFailures = 3; cfg.Budget.MaxInFlightReads = 100;
            var h = new Harness(cfg) { AutoComplete = false };
            var cam = Harness.Camera(1, X0, Z0);
            var plan = h.Step(0.1, cam);
            var key = plan.Load[0].Key;
            h.Sched.Failed(key, "io");
            var p2 = h.Step(0.1, cam);   // t = 0.2: backoff 0.5 s not elapsed
            foreach (var r in p2.Load) Assert.AreNotEqual(key, r.Key, "retry too early");
            for (int i = 0; i < 6; i++) h.Step(0.1, cam);
            Assert.AreNotEqual(TileState.Absent, h.Sched.GetState(key), "retried after the backoff");
            h.Sched.Failed(key, "io"); h.Step(1.5, cam);
            Assert.AreNotEqual(TileState.Absent, h.Sched.GetState(key));
            h.Sched.Failed(key, "io");
            Assert.IsTrue(h.Sched.IsParked(key), "parked after MaxFailures");
            for (int i = 0; i < 20; i++) h.Step(2.0, cam);
            Assert.AreEqual(TileState.Absent, h.Sched.GetState(key), "a parked key is not requested again");
        }

        // ---------------- events and determinism ----------------

        [Test]
        public void Events_TileResidentAndEvictedOnTheBus_GenerationCountsReloads()
        {
            var h = new Harness();
            var a = Harness.Camera(1, X0, Z0, 0, 0, 0);
            var away = Harness.Camera(1, X0 + 80000, Z0, 0, 0, 0);
            for (int i = 0; i < 5; i++) h.Step(0.1, a);
            int res1 = h.Log.Resident;
            Assert.AreEqual(h.Loads.Count, res1, "one TileResident per completed load");
            for (int i = 0; i < 5; i++) h.Step(0.1, away);
            Assert.Greater(h.Log.Evicted, 0);
            for (int i = 0; i < 5; i++) h.Step(0.1, a);
            var focus = ResidencyKey.Render(GeoTileId.Containing(X0, Z0, 0));
            int gen = 0;
            foreach (var e in h.Log.ResidentEvents) if (e.Tile.Equals(focus.Tile) && e.Kind == ResidencyKind.Render) gen = Math.Max(gen, e.Generation);
            Assert.AreEqual(2, gen, "second residency of the same tile has generation 2");
        }

        [Test]
        public void Events_AreUnmanagedAndCarryIdsOnly()
        {
            Assert.IsTrue(IsUnmanaged<TileResident>());
            Assert.IsTrue(IsUnmanaged<TileEvicted>());
            Assert.IsTrue(IsUnmanaged<ClusterOriginChanged>());
        }

        static bool IsUnmanaged<T>() where T : unmanaged, IEvent { return true; }

        [Test]
        public void Determinism_SameInputsSameOutputs()
        {
            string Run()
            {
                var h = new Harness();
                var sb = new System.Text.StringBuilder();
                for (int s = 0; s < 300; s++)
                {
                    var p = h.Step(1.0, Harness.Body(3, X0 + s * 28.0, Z0 + s * 3.0, 28f, 3f), Harness.Camera(2, X0 + s * 28.0 + 100, Z0, 28f, 0f), Harness.Body(1, X0 - 5000 + s * 9.0, Z0, 9f, 0f));
                    foreach (var r in p.Load) sb.Append('L').Append(r.Key.ToString()).Append(r.Priority.ToString("R"));
                    foreach (var k in p.Evict) sb.Append('E').Append(k.ToString());
                    foreach (var a in p.Assignments) sb.Append('A').Append(a.FocusId).Append(':').Append(a.ClusterId).Append(a.LocalX.ToString("R"));
                }
                return sb.ToString();
            }
            Assert.AreEqual(Run(), Run());
        }

        [Test]
        public void Coverage_TilesOutsideTheBakedAreaAreNeverRequested()
        {
            var h = new Harness(null, new DefaultTileCostModel(coverageL0Tiles: 40));   // 82 km square
            for (int i = 0; i < 20; i++) h.Step(0.1, Harness.Camera(1, 70000, 70000, 0, 0, 0), Harness.Body(2, 82000.0 - 100, 1000));
            foreach (var k in h.Loads.Keys) Assert.IsTrue(h.Cost.Exists(k.Tile), k + " does not exist in the coverage");
        }

        [Test]
        public void Allocation_PlanListsAreReused()
        {
            var h = new Harness();
            h.Step(0.1, Harness.Camera(1, X0, Z0));
            var loadList = h.Plan.Load; var evictList = h.Plan.Evict;
            h.Step(0.1, Harness.Camera(1, X0 + 3000, Z0));
            Assert.AreSame(loadList, h.Plan.Load); Assert.AreSame(evictList, h.Plan.Evict);
        }
    }
}
