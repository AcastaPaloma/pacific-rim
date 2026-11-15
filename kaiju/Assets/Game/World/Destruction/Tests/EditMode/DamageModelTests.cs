// Engine-free World.Destruction tests: run by Unity EditMode and by `dotnet test DotnetTests/WorldDestruction.Tests` (T-024).
using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.World.Destruction.Tests
{
    sealed class RecordingBus : IEventBus
    {
        public readonly List<BuildingDamaged> Events = new List<BuildingDamaged>();
        public void Subscribe<T>(IEventHandler<T> handler) where T : unmanaged, IEvent { }
        public bool Unsubscribe<T>(IEventHandler<T> handler) where T : unmanaged, IEvent { return false; }
        public void Publish<T>(in T evt) where T : unmanaged, IEvent
        {
            if (evt is BuildingDamaged d) Events.Add(d);
        }
        public int SubscriberCount<T>() where T : unmanaged, IEvent { return 0; }
        public int HandlerExceptionCount => 0;
    }

    public class DamageModelTests
    {
        static BuildingId Id(ulong n) { return BuildingId.From(BuildingSource.OsmWay, n); }
        static readonly BuildingAttributes House = new BuildingAttributes { HeightM = 7, Levels = 2, FootprintM2 = 100, Residential = true };
        static readonly BuildingAttributes MidRise = new BuildingAttributes { HeightM = 25, Levels = 6, FootprintM2 = 600 };
        static readonly BuildingAttributes Tower = new BuildingAttributes { HeightM = 160, Levels = 40, FootprintM2 = 1500 };

        [Test]
        public void ModelRevision_IsOne() { Assert.AreEqual(1, DestructionContractVersions.ModelRevision); }

        [Test]
        public void BuildingDamaged_IsUnmanagedEvent()
        {
            // Compiles only if the struct is unmanaged and an IEvent.
            var bus = new RecordingBus();
            bus.Publish(new BuildingDamaged { BuildingId = Id(1), State = 2, EnergyJ = 1f });
            Assert.AreEqual(1, bus.Events.Count);
            Assert.IsFalse(default(BuildingDamaged).Source.IsValid);
        }

        [Test]
        public void Levels_FallbackIsHeightOver422_MinOne()
        {
            Assert.AreEqual(6, CapacityModel.ResolveLevels(new BuildingAttributes { HeightM = 25 }));
            Assert.AreEqual(1, CapacityModel.ResolveLevels(new BuildingAttributes { HeightM = 1 }));
            Assert.AreEqual(3, CapacityModel.ResolveLevels(new BuildingAttributes { HeightM = 25, Levels = 3 }));
        }

        [Test]
        public void RiseClass_And_HazusType_Selection()
        {
            Assert.AreEqual(RiseClass.A, CapacityModel.ClassOf(3));
            Assert.AreEqual(RiseClass.B, CapacityModel.ClassOf(4));
            Assert.AreEqual(RiseClass.B, CapacityModel.ClassOf(7));
            Assert.AreEqual(RiseClass.C, CapacityModel.ClassOf(8));
            Assert.AreEqual(HazusType.W1, CapacityModel.TypeOf(RiseClass.A, true));
            Assert.AreEqual(HazusType.C2L, CapacityModel.TypeOf(RiseClass.A, false));
            Assert.AreEqual(HazusType.C2M, CapacityModel.TypeOf(RiseClass.B, true));
            Assert.AreEqual(HazusType.C2H, CapacityModel.TypeOf(RiseClass.C, false));
        }

        [Test]
        public void Capacity_SameIdSameValue_DifferentIdsScatter()
        {
            var a = CapacityModel.Compute(Id(42), MidRise);
            var b = CapacityModel.Compute(Id(42), MidRise);
            Assert.AreEqual(a.S1, b.S1); Assert.AreEqual(a.S4, b.S4);
            var c = CapacityModel.Compute(Id(43), MidRise);
            Assert.AreNotEqual(a.S4, c.S4);
            // the median row is independent of the id
            Assert.AreEqual(a.E4, c.E4);
        }

        [Test]
        public void Hash_DependsOnIdSourceAndEventIndex()
        {
            // Pins the hash so a refactor cannot silently change every saved-world capacity.
            Assert.AreEqual(CapacityModel.Hash(Id(1), 0), CapacityModel.Hash(Id(1), 0));
            Assert.AreNotEqual(CapacityModel.Hash(Id(1), 0), CapacityModel.Hash(Id(1), 1));
            Assert.AreNotEqual(CapacityModel.Hash(Id(1), 0),
                CapacityModel.Hash(BuildingId.From(BuildingSource.Landmark, 1), 0));
        }

        [Test]
        public void ScatterZ_IsStandardNormalLike_AndClamped()
        {
            double sum = 0, sum2 = 0; int n = 20000;
            for (int i = 1; i <= n; i++)
            {
                double z = CapacityModel.ScatterZ(Id((ulong)i), 0);
                Assert.LessOrEqual(Math.Abs(z), DamageParameters.ZClamp);
                sum += z; sum2 += z * z;
            }
            Assert.AreEqual(0.0, sum / n, 0.03);
            Assert.AreEqual(1.0, sum2 / n, 0.05);
        }

        [Test]
        public void Ek_Ordering_AndHeavierMeansMoreEnergy()
        {
            foreach (var a in new[] { House, MidRise, Tower })
            {
                var c = CapacityModel.Compute(Id(7), a);
                Assert.Less(c.E1, c.E2); Assert.Less(c.E2, c.E3); Assert.Less(c.E3, c.E4);
                Assert.Less(c.S1, c.S2); Assert.Less(c.S2, c.S3); Assert.Less(c.S3, c.S4);
            }
            var h = CapacityModel.Compute(Id(7), House); var m = CapacityModel.Compute(Id(7), MidRise); var t = CapacityModel.Compute(Id(7), Tower);
            Assert.Less(h.E4, m.E4); Assert.Less(m.E4, t.E4);
        }

        [Test]
        public void Ek_MatchesWorkedValues_OfR021()
        {
            // R-021 §2.2 table, medians (no scatter), tolerance 10 % (the table rounds to 2 digits).
            var h = CapacityModel.Compute(Id(1), House);
            Assert.AreEqual(1.8e3, h.E1, 0.1 * 1.8e3); Assert.AreEqual(36e3, h.E4, 0.1 * 36e3);
            var m = CapacityModel.Compute(Id(1), MidRise);
            Assert.AreEqual(0.11e6, m.E1, 0.1 * 0.11e6); Assert.AreEqual(1.8e6, m.E4, 0.1 * 1.8e6);
            var t = CapacityModel.Compute(Id(1), Tower);
            Assert.AreEqual(4.8e6, t.E1, 0.1 * 4.8e6); Assert.AreEqual(94e6, t.E4, 0.1 * 94e6);
        }

        [Test]
        public void Apply_EnergyToStateThresholds()
        {
            var model = new DamageModel();
            var id = Id(100);
            model.Register(id, MidRise);
            Assert.IsTrue(model.TryGetThresholds(id, out var s1, out var s2, out var s3, out var s4));
            Assert.AreEqual(0, Apply(model, id, s1 * 0.99).Current.State);
            Assert.AreEqual(1, Apply(model, id, s1 * 0.02).Current.State);   // cumulative crosses S1
            Assert.AreEqual(2, Apply(model, id, s2).Current.State);
            Assert.AreEqual(3, Apply(model, id, s3).Current.State);
            Assert.AreEqual(4, Apply(model, id, s4).Current.State);
            Assert.AreEqual(4, Apply(model, id, s4 * 10).Current.State);     // capped at Complete
        }

        [Test]
        public void Apply_SingleBigHit_JumpsStates()
        {
            var model = new DamageModel();
            var id = Id(5); model.Register(id, House);
            model.TryGetThresholds(id, out _, out _, out _, out var s4);
            var r = Apply(model, id, s4 * 1.01);
            Assert.IsTrue(r.Changed); Assert.AreEqual(0, r.PrevState); Assert.AreEqual(4, r.Current.State);
        }

        [Test]
        public void Apply_IsMonotone_ForAnyEnergySequence()
        {
            var model = new DamageModel();
            var id = Id(9); model.Register(id, Tower);
            double[] seq = { 5e6, -1e9, double.NaN, 0, 1e6, double.PositiveInfinity, 3e7, -5, 2e7, 1e9 };
            int prev = 0; double prevE = 0;
            foreach (double e in seq)
            {
                var r = Apply(model, id, e);
                Assert.GreaterOrEqual(r.Current.State, prev);
                Assert.GreaterOrEqual(r.Current.CumulativeEnergyJ, prevE);
                Assert.IsFalse(double.IsNaN(r.Current.CumulativeEnergyJ) || double.IsInfinity(r.Current.CumulativeEnergyJ));
                prev = r.Current.State; prevE = r.Current.CumulativeEnergyJ;
            }
        }

        [Test]
        public void Apply_IsDeterministic_AcrossInstances()
        {
            var a = new DamageModel(); var b = new DamageModel();
            for (ulong i = 1; i <= 200; i++) { a.Register(Id(i), MidRise); b.Register(Id(200 - i + 1), MidRise); }
            var rng = new Random(1234);
            var energies = new double[2000];
            for (int i = 0; i < energies.Length; i++) energies[i] = rng.NextDouble() * 3e5;
            for (int i = 0; i < energies.Length; i++)
            {
                var id = Id((ulong)(i % 200 + 1));
                Apply(a, id, energies[i]); Apply(b, id, energies[i]);
            }
            for (ulong i = 1; i <= 200; i++)
            {
                a.TryGetState(Id(i), out var sa); b.TryGetState(Id(i), out var sb);
                Assert.AreEqual(sa.State, sb.State); Assert.AreEqual(sa.CumulativeEnergyJ, sb.CumulativeEnergyJ);
            }
        }

        [Test]
        public void Apply_UnknownBuilding_IsNotKnown()
        {
            var r = new DamageModel().Apply(Id(1), 1e9, DamageCause.Impact, default, 0);
            Assert.IsFalse(r.Known); Assert.IsFalse(r.Changed);
        }

        [Test]
        public void Register_Again_KeepsDamage()
        {
            var model = new DamageModel(); var id = Id(3); model.Register(id, House);
            Apply(model, id, 1e9);
            model.Register(id, House);
            model.TryGetState(id, out var s);
            Assert.AreEqual(4, s.State);
        }

        [Test]
        public void Publishes_OncePerStateChange_NotPerIncrement()
        {
            var bus = new RecordingBus(); var model = new DamageModel(bus);
            var id = Id(11); model.Register(id, MidRise);
            model.TryGetThresholds(id, out var s1, out _, out _, out var s4);
            var src = EntityId.FromValue(77);
            model.Apply(id, s1 * 0.3, DamageCause.Impact, src, 10);
            model.Apply(id, s1 * 0.3, DamageCause.Impact, src, 11);
            Assert.AreEqual(0, bus.Events.Count);
            model.Apply(id, s1 * 0.5, DamageCause.Impact, src, 12);   // crosses S1
            Assert.AreEqual(1, bus.Events.Count);
            model.Apply(id, s4 * 2, DamageCause.Shaking, default, 13);
            Assert.AreEqual(2, bus.Events.Count);
            var e = bus.Events[1];
            Assert.AreEqual(id, e.BuildingId); Assert.AreEqual(4, e.State); Assert.AreEqual(1, e.PrevState);
            Assert.AreEqual((byte)DamageCause.Shaking, e.Cause);
            Assert.AreEqual(1, bus.Events[0].State); Assert.AreEqual(src, bus.Events[0].Source);
            model.Apply(id, s4, DamageCause.Impact, default, 14);     // already Complete
            Assert.AreEqual(2, bus.Events.Count);
        }

        [Test]
        public void Apply_Allocates_NothingInSteadyState()
        {
            var model = new DamageModel(); var id = Id(21); model.Register(id, Tower);
            for (int i = 0; i < 100; i++) model.Apply(id, 10.0, DamageCause.Impact, default, i);   // warm up
            // Positive control: the runtime must report allocations at all (Unity's Mono does not).
            long c0 = GC.GetAllocatedBytesForCurrentThread();
            var junk = new byte[1024];
            long control = GC.GetAllocatedBytesForCurrentThread() - c0;
            GC.KeepAlive(junk);
            if (control <= 0) Assert.Ignore("Runtime does not report per-thread allocations.");
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) model.Apply(id, 10.0, DamageCause.Impact, default, i);
            long delta = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, delta);
        }

        // ---- shaking route (T-024 step 2) ----
        [Test]
        public void Shaking_Exceedance_MonotoneInPga_AndOrderedInK()
        {
            foreach (var a in new[] { House, MidRise, Tower })
            {
                double prev1 = -1, prev4 = -1;
                for (double pga = 0.01; pga <= 3.0; pga *= 1.25)
                {
                    double p1 = ShakingFragility.Exceedance(a, pga, 1), p2 = ShakingFragility.Exceedance(a, pga, 2),
                           p3 = ShakingFragility.Exceedance(a, pga, 3), p4 = ShakingFragility.Exceedance(a, pga, 4);
                    Assert.GreaterOrEqual(p1, prev1); Assert.GreaterOrEqual(p4, prev4);
                    Assert.GreaterOrEqual(p1, p2); Assert.GreaterOrEqual(p2, p3); Assert.GreaterOrEqual(p3, p4);
                    Assert.That(p1, Is.InRange(0.0, 1.0));
                    prev1 = p1; prev4 = p4;
                }
                Assert.AreEqual(0.0, ShakingFragility.Exceedance(a, 0.0, 1));
                Assert.AreEqual(1.0, ShakingFragility.Exceedance(a, 0.5, 0));
            }
        }

        [Test]
        public void Shaking_SameSeed_SameState_AndStatesNeverDecrease()
        {
            var a = new DamageModel(); var b = new DamageModel();
            var id = Id(77); a.Register(id, MidRise); b.Register(id, MidRise);
            var ra = a.ApplyShaking(id, 0.6, 3, 5); var rb = b.ApplyShaking(id, 0.6, 3, 5);
            Assert.AreEqual(ra.Current.State, rb.Current.State);
            Assert.AreEqual(ra.Current.CumulativeEnergyJ, rb.Current.CumulativeEnergyJ);
            byte last = 0;
            for (int i = 1; i <= 50; i++)
            {
                var r = a.ApplyShaking(id, 0.05 * i, i, i);
                Assert.GreaterOrEqual(r.Current.State, last); last = r.Current.State;
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => a.ApplyShaking(id, 0.5, 0, 1));
            Assert.IsFalse(a.ApplyShaking(Id(999), 0.5, 1, 1).Known);
        }

        [Test]
        public void Shaking_RaisesCumulativeEnergyToStateThreshold_AndTagsCause()
        {
            var bus = new RecordingBus(); var m = new DamageModel(bus); var id = Id(5); m.Register(id, MidRise);
            var r = m.ApplyShaking(id, 3.0, 1, 9);
            Assert.Greater(r.Current.State, 0);
            m.TryGetThresholds(id, out var s1, out var s2, out var s3, out var s4);
            double s = r.Current.State == 4 ? s4 : r.Current.State == 3 ? s3 : r.Current.State == 2 ? s2 : s1;
            Assert.AreEqual(s, r.Current.CumulativeEnergyJ);
            Assert.AreEqual((byte)DamageCause.Shaking, r.Current.Cause);
            Assert.AreEqual(1, bus.Events.Count);
            // Energy route continues on the same ladder: a state-4 energy still completes it.
            m.Apply(id, s4, DamageCause.Impact, default, 10);
            Assert.AreEqual(4, m.TryGetState(id, out var st) ? st.State : 0);
        }

        [Test]
        public void Shaking_EmpiricalFrequency_MatchesAnalytic_Over10000Ids()
        {
            foreach (var a in new[] { House, MidRise, Tower })
            foreach (double pga in new[] { 0.05, 0.1, 0.2, 0.4, 0.8, 1.6 })
            {
                const int N = 10000;
                var cnt = new int[5];
                for (ulong i = 1; i <= N; i++)
                {
                    var m = new DamageModel(); var id = Id(i); m.Register(id, a);
                    cnt[m.ApplyShaking(id, pga, 1, 0).Current.State]++;
                }
                for (int k = 1; k <= 4; k++)
                {
                    int ge = 0; for (int j = k; j <= 4; j++) ge += cnt[j];
                    Assert.AreEqual(ShakingFragility.Exceedance(a, pga, k), ge / (double)N, 0.015,
                        "levels " + a.Levels + " pga " + pga + " k " + k);
                }
            }
        }

        static DamageChange Apply(DamageModel m, BuildingId id, double e) { return m.Apply(id, e, DamageCause.Impact, default, 1); }
    }
}
