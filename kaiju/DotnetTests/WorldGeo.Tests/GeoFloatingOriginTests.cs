// T-021 step 4a: engine-free floating-origin policy (M-015).
using NUnit.Framework;

namespace Game.World.Geo.Tests
{
    public class GeoFloatingOriginTests
    {
        const double T = 2048.0;

        [Test]
        public void InsideRebaseDistance_Unchanged()
        {
            GeoFloatingOrigin.Update(4 * T, 6 * T, 4 * T + 3000, 6 * T - 3000, out var x, out var z);
            Assert.AreEqual(4 * T, x); Assert.AreEqual(6 * T, z);
        }

        [Test]
        public void BeyondRebaseDistance_MovesToContainingTileCorner()
        {
            GeoFloatingOrigin.Update(4 * T, 6 * T, 4 * T + 3100, 6 * T + 100, out var x, out var z);
            Assert.AreEqual(5 * T, x); Assert.AreEqual(6 * T, z, "z not moved to a tile it is not in");   // x tile index 1 -> 5T; z tile index 0 -> 6T
        }

        [Test]
        public void NegativeSide_FloorsToLowerTile()
        {
            GeoFloatingOrigin.Update(4 * T, 6 * T, 4 * T - 3100, 6 * T - 3100, out var x, out var z);
            Assert.AreEqual(2 * T, x); Assert.AreEqual(4 * T, z);
            Assert.AreEqual(0.0, (x % T)); Assert.AreEqual(0.0, (z % T));
        }

        [Test]
        public void AfterRebase_BodyWithinOneTileOfOrigin_AndStable()
        {
            double bx = 10000.7, bz = -5300.2, ox = 0, oz = 0;
            GeoFloatingOrigin.Update(ox, oz, bx, bz, out ox, out oz);
            Assert.That(bx - ox, Is.InRange(0.0, T)); Assert.That(bz - oz, Is.InRange(0.0, T));
            GeoFloatingOrigin.Update(ox, oz, bx, bz, out var x2, out var z2);
            Assert.AreEqual(ox, x2); Assert.AreEqual(oz, z2);
        }
    }
}
