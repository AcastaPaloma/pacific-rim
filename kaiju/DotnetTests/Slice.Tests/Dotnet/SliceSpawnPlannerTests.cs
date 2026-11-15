using System.Collections.Generic;
using Game.Slice;
using NUnit.Framework;

namespace Game.Slice.Tests
{
    public class SliceSpawnPlannerTests
    {
        static double Flat(double x, double z) { return 5.0; }

        [Test]
        public void ClearWaterfront_IsPreferredOverTheCanyon()
        {
            // a dense block of 150 m towers on a 40 m grid for x in [0,600]; open ground for x > 600
            var b = new List<SliceFootprint>();
            for (double x = 0; x <= 600; x += 40) for (double z = 0; z <= 600; z += 40) b.Add(new SliceFootprint(x, z, 12, 150));
            Assert.IsTrue(SliceSpawnPlanner.TryPlan(b, Flat, 300, 300, 600, 40, 248, 27, 16, out var plan));
            Assert.AreEqual(0, plan.Blockers);
            // the camera (mech - forward * 248) is in clear air: no tall building within its footprint margin
            double yaw = plan.YawDeg * System.Math.PI / 180.0, cx = plan.MechX - System.Math.Sin(yaw) * 248, cz = plan.MechZ - System.Math.Cos(yaw) * 248;
            foreach (var f in b) Assert.Greater((f.X - cx) * (f.X - cx) + (f.Z - cz) * (f.Z - cz), (f.Radius + SliceSpawnPlanner.ClearMarginM) * (f.Radius + SliceSpawnPlanner.ClearMarginM));
            Assert.Greater(plan.VisibleBuildings, 0);
        }

        [Test]
        public void WaterIsNeverChosen()
        {
            var b = new List<SliceFootprint> { new SliceFootprint(100, 100, 10, 60) };
            double Ground(double x, double z) { return x < 300 ? -2.0 : 4.0; }   // west half is bay
            Assert.IsTrue(SliceSpawnPlanner.TryPlan(b, Ground, 200, 100, 200, 20, 248, 27, 8, out var plan));
            Assert.GreaterOrEqual(plan.GroundM, SliceSpawnPlanner.MinGroundM); Assert.GreaterOrEqual(plan.MechX, 300);
        }

        [Test]
        public void NoLand_ReturnsFalse()
        {
            Assert.IsFalse(SliceSpawnPlanner.TryPlan(new List<SliceFootprint>(), (x, z) => -3.0, 0, 0, 100, 20, 248, 27, 8, out _));
        }

        [Test]
        public void SolidWallBetweenCameraAndMech_CountsAsBlocker()
        {
            var b = new List<SliceFootprint>();
            for (double x = -300; x <= 300; x += 10) b.Add(new SliceFootprint(x, -100, 8, 120));   // a wall at z = -100
            Assert.IsTrue(SliceSpawnPlanner.TryPlan(b, Flat, 0, 0, 0, 40, 248, 27, 4, out var plan));   // one candidate (0,0), 4 yaws
            // yaw 0 puts the camera at z = -248 behind the wall: blocked; the other three yaws see no wall
            Assert.AreEqual(0, plan.Blockers); Assert.AreNotEqual(0.0, plan.YawDeg);
        }

        [Test]
        public void WallInTheWalkPath_CountsAsBlocker()
        {
            var b = new List<SliceFootprint>();
            for (double x = -300; x <= 300; x += 10) b.Add(new SliceFootprint(x, 60, 8, 120));   // a wall 60 m ahead of the mech for yaw 0: the camera follows through it within the 72 m walk
            Assert.IsTrue(SliceSpawnPlanner.TryPlan(b, Flat, 0, 0, 0, 40, 248, 27, 4, out var plan));
            Assert.AreEqual(0, plan.Blockers); Assert.AreNotEqual(0.0, plan.YawDeg);
        }
    }
}
