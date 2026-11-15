// SeismicShakingBridge tests (T-027 step 4) against a fake IShakingField. Engine-free.
using System.Collections.Generic;
using Game.Core;
using Game.World.Seismic;
using NUnit.Framework;

namespace Game.World.Destruction.Tests
{
    public class SeismicShakingBridgeTests
    {
        sealed class NullErrors : ICoreErrorSink { public void Report(CoreError error) { } }

        sealed class FakeField : IShakingField
        {
            public double Pga = 1.5;
            public readonly List<double> Xs = new List<double>();
            public bool TryGetShaking(long eventId, double x, double z, double vs30, out ShakingSample s)
            {
                Xs.Add(x);
                s = new ShakingSample { PgaG = (float)Pga };
                return eventId == 1 || eventId == 2;
            }
        }

        static FeatureFlagSnapshot Flags(bool destruction, bool seismic)
        {
            return new FeatureFlagSnapshot(new[]
            {
                new KeyValuePair<string, bool>(DestructionFlags.Enabled, destruction),
                new KeyValuePair<string, bool>(SeismicFlags.Enabled, seismic),
            });
        }

        static BuildingId Id(ulong n) { return BuildingId.From(BuildingSource.OsmWay, n); }
        static readonly BuildingAttributes House = new BuildingAttributes { HeightM = 7, Levels = 2, FootprintM2 = 100, Residential = true };

        static DamageModel Model(int n)
        {
            var m = new DamageModel();
            for (ulong i = 1; i <= (ulong)n; i++) m.Register(Id(i), House);
            return m;
        }

        static FaultRupture Rupture(uint ev) { return new FaultRupture { EventId = ev, FaultId = 1 }; }

        [Test]
        public void EitherFlagOff_NoBridge_NothingSubscribed()
        {
            var bus = new EventBus(new NullErrors(), 1000);
            var model = Model(3); var field = new FakeField();
            Assert.IsNull(SeismicShakingBridge.TryCreate(Flags(true, false), bus, model, field));
            Assert.IsNull(SeismicShakingBridge.TryCreate(Flags(false, true), bus, model, field));
            Assert.IsNull(SeismicShakingBridge.TryCreate(null, bus, model, field));
            Assert.AreEqual(0, bus.SubscriberCount<FaultRupture>());
            bus.Publish(Rupture(1));
            Assert.AreEqual(0, field.Xs.Count);
        }

        [Test]
        public void BothFlagsOn_AppliesInOrdinalOrder_OncePerEvent()
        {
            var bus = new EventBus(new NullErrors(), 1000);
            var model = Model(5); var field = new FakeField();
            var b = SeismicShakingBridge.TryCreate(Flags(true, true), bus, model, field);
            Assert.IsNotNull(b);
            b.AddBuilding(Id(3), 30, 0); b.AddBuilding(Id(1), 10, 0); b.AddBuilding(Id(2), 20, 0); b.AddBuilding(Id(2), 20, 0);
            Assert.AreEqual(3, b.SiteCount);
            bus.Publish(Rupture(1));
            Assert.AreEqual(new double[] { 10, 20, 30 }, field.Xs.ToArray());
            bus.Publish(Rupture(1));                                  // same EventId again: ignored
            Assert.AreEqual(3, field.Xs.Count);
            bus.Publish(Rupture(7));                                  // unknown to the field: nothing applied
            Assert.AreEqual(6, field.Xs.Count);
        }

        [Test]
        public void StrongShaking_DamagesSomeBuildings()
        {
            var bus = new EventBus(new NullErrors(), 1000);
            var model = Model(60); var field = new FakeField();
            var b = SeismicShakingBridge.TryCreate(Flags(true, true), bus, model, field);
            for (ulong i = 1; i <= 60; i++) b.AddBuilding(Id(i), i, 0);
            bus.Publish(Rupture(1));
            int damaged = 0;
            for (ulong i = 1; i <= 60; i++) { model.TryGetState(Id(i), out var st); if (st.State > 0) damaged++; }
            Assert.Greater(damaged, 0, "PGA 1.5 g must damage some of 60 houses");
        }

        [Test]
        public void UnknownBuilding_IsSkipped_AndDetachStops()
        {
            var bus = new EventBus(new NullErrors(), 1000);
            var model = Model(1); var field = new FakeField();
            var b = SeismicShakingBridge.TryCreate(Flags(true, true), bus, model, field);
            b.AddBuilding(Id(99), 5, 5);
            bus.Publish(Rupture(1));
            Assert.IsFalse(model.TryGetState(Id(99), out _));
            b.Detach();
            Assert.AreEqual(0, bus.SubscriberCount<FaultRupture>());
        }
    }
}
