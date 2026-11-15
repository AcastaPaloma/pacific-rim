using Game.Combat;
using Game.Core;
using NUnit.Framework;

namespace Game.Slice.Tests
{
    // T-189: the slice's combat target query (buildings as cylinders, one kaiju cylinder, terrain).
    public class SliceCombatWorldTests
    {
        static SliceCombatWorld World(double ground = 0.0) { return new SliceCombatWorld((x, z) => ground); }
        static readonly BuildingId B1 = BuildingId.From(BuildingSource.OsmWay, 11), B2 = BuildingId.From(BuildingSource.OsmWay, 12);

        [Test]
        public void Shell_HitsBuildingSideAtFirstContact()
        {
            var w = World(); w.AddBuilding(B1, 0, 100, 20, 80);
            Assert.IsTrue(w.SweepSphere(new CVec3(0, 40, 0), new CVec3(0, 40, 200), 0.5, default, out var h));
            Assert.AreEqual(TargetKind.Building, h.Kind); Assert.AreEqual(B1, h.Building);
            Assert.AreEqual((100 - 20.5) / 200.0, h.T, 1e-9);
            Assert.AreEqual(-1.0, h.Normal.Z, 1e-9);
            Assert.Greater(h.TargetMassKg, 0);
        }

        [Test]
        public void Shell_PassesOverTheRoofAndPastTheSide()
        {
            var w = World(); w.AddBuilding(B1, 0, 100, 20, 80);
            Assert.IsFalse(w.SweepSphere(new CVec3(0, 120, 0), new CVec3(0, 120, 200), 0.5, default, out _));
            Assert.IsFalse(w.SweepSphere(new CVec3(60, 40, 0), new CVec3(60, 40, 200), 0.5, default, out _));
        }

        [Test]
        public void Shell_FallingOntoTheRoofHitsTheTopFacingUp()
        {
            var w = World(); w.AddBuilding(B1, 0, 100, 20, 80);
            Assert.IsTrue(w.SweepSphere(new CVec3(0, 200, 100), new CVec3(0, 0, 100), 1.0, default, out var h));
            Assert.AreEqual(TargetKind.Building, h.Kind);
            Assert.AreEqual(1.0, h.Normal.Y, 1e-9);
            Assert.AreEqual(81.0, h.Point.Y, 1e-6);   // sphere centre rests one radius above the roof
        }

        [Test]
        public void NearestOfTwoBuildingsWins_AndRemovedBuildingsAreSkipped()
        {
            var w = World(); w.AddBuilding(B2, 0, 300, 20, 80); w.AddBuilding(B1, 0, 100, 20, 80);
            w.SweepSphere(new CVec3(0, 40, 0), new CVec3(0, 40, 500), 0.5, default, out var h);
            Assert.AreEqual(B1, h.Building);
            w.RemoveBuilding(B1);
            w.SweepSphere(new CVec3(0, 40, 0), new CVec3(0, 40, 500), 0.5, default, out h);
            Assert.AreEqual(B2, h.Building);
        }

        [Test]
        public void Kaiju_IsHitUnlessItIsTheShooter()
        {
            var w = World(); var k = EntityId.FromValue(1);
            w.SetKaiju(k, 0, 100, 0, 15, 100, 2e7, new CVec3(0, 0, -3));
            Assert.IsTrue(w.SweepSphere(new CVec3(0, 50, 0), new CVec3(0, 50, 200), 0.5, EntityId.FromValue(101), out var h));
            Assert.AreEqual(TargetKind.Kaiju, h.Kind); Assert.AreEqual(k, h.Entity); Assert.AreEqual(-3.0, h.TargetVelocity.Z);
            Assert.IsFalse(w.SweepSphere(new CVec3(0, 50, 0), new CVec3(0, 50, 200), 0.5, k, out _));
            w.ClearKaiju();
            Assert.IsFalse(w.SweepSphere(new CVec3(0, 50, 0), new CVec3(0, 50, 200), 0.5, default, out _));
        }

        [Test]
        public void Terrain_StopsAShellThatDipsBelowIt()
        {
            var w = World(ground: 10.0);
            Assert.IsTrue(w.SweepSphere(new CVec3(0, 50, 0), new CVec3(0, -30, 0), 1.0, default, out var h));
            Assert.AreEqual(TargetKind.Ground, h.Kind); Assert.AreEqual(10.0, h.Point.Y, 1e-9);
            Assert.AreEqual((50 - 11.0) / 80.0, h.T, 0.15);
            Assert.IsFalse(w.SweepSphere(new CVec3(0, 50, 0), new CVec3(0, 40, 100), 1.0, default, out _));
        }

        [Test]
        public void Query_IsDeterministic_AndRejectsNonFinite()
        {
            var w = World(); w.AddBuilding(B1, 5, 100, 20, 80);
            w.SweepSphere(new CVec3(0, 40, 0), new CVec3(10, 41, 200), 0.5, default, out var a);
            w.SweepSphere(new CVec3(0, 40, 0), new CVec3(10, 41, 200), 0.5, default, out var b);
            Assert.AreEqual(a.T, b.T); Assert.AreEqual(a.Point, b.Point);
            Assert.IsFalse(w.SweepSphere(new CVec3(double.NaN, 0, 0), new CVec3(0, 0, 1), 1, default, out _));
        }

        [Test]
        public void BuildingMass_IsClamped()
        {
            Assert.AreEqual(SliceCombatWorld.MinBuildingMassKg, SliceCombatWorld.BuildingMassKg(3, 3));
            Assert.AreEqual(SliceCombatWorld.MaxBuildingMassKg, SliceCombatWorld.BuildingMassKg(120, 500));
        }
    }
}
