// T-115d: the Composition adapter over the T-102 Bay raster (IBayCostField for GridRoutePlanner). Engine-free: dotnet (DotnetTests/Composition.Adapters.Tests) and Unity EditMode.
using System;
using System.IO;
using Game.AI;
using Game.AI.Pack;
using Game.Composition;
using Game.Core;
using NUnit.Framework;

namespace Game.Composition.Tests
{
    public class BayRasterCostFieldTests
    {
        static byte[] Make(int nx, int ny, Func<int, int, ushort> depthDm, Func<int, int, byte> soft)
        {
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write(new[] { (byte)'B', (byte)'C', (byte)'F', (byte)'1' }); w.Write(nx); w.Write(ny); w.Write(100f); w.Write(200f); w.Write(256f);
            for (int y = 0; y < ny; y++) for (int x = 0; x < nx; x++) w.Write(depthDm(x, y));
            for (int y = 0; y < ny; y++) for (int x = 0; x < nx; x++) w.Write(soft(x, y));
            return ms.ToArray();
        }

        static string FindRaster()
        {
            var d = new DirectoryInfo(Environment.CurrentDirectory);
            while (d != null)
            {
                string p = Path.Combine(d.FullName, "evidence", "T-115", BayRasterCostField.DefaultFileName);
                if (File.Exists(p)) return p;
                d = d.Parent;
            }
            return null;
        }

        [Test]
        public void RoundTripKeepsDepthSoftnessAndFrame()
        {
            var f = BayRasterCostField.FromBytes(Make(4, 3, (x, y) => (ushort)(x * 100 + y * 10), (x, y) => (byte)(x == 3 ? 255 : 0)));
            Assert.AreEqual(4, f.Nx); Assert.AreEqual(3, f.Ny); Assert.AreEqual(100f, f.OriginX); Assert.AreEqual(200f, f.OriginY); Assert.AreEqual(256f, f.CellM);
            Assert.AreEqual(22.0f, f.DepthM(2, 2), 1e-4f); Assert.AreEqual(1f, f.Softness(3, 0), 1e-6f); Assert.AreEqual(0f, f.Softness(1, 1));
        }

        [Test]
        public void RejectsBadMagicAndSize()
        {
            var b = Make(2, 2, (x, y) => 0, (x, y) => 0);
            var bad = (byte[])b.Clone(); bad[0] = (byte)'X';
            Assert.Throws<InvalidDataException>(() => BayRasterCostField.FromBytes(bad));
            Assert.Throws<InvalidDataException>(() => BayRasterCostField.FromBytes(b.AsSpan(0, b.Length - 1).ToArray()));
        }

        [Test]
        public void FeedsTheRoutePlannerBlockedColumn()
        {
            // 40 x 10 cells of 256 m, a 6-cell wide 100 m deep column across x = 18..23 except one shallow row at the top: the planner must go round it
            var f = BayRasterCostField.FromBytes(Make(40, 10, (x, y) => (ushort)(x >= 18 && x < 24 && y < 9 ? 1000 : 0), (x, y) => 0));
            var pl = new GridRoutePlanner(new Shifted(f));
            var from = new Float2(f.CellM * 5.5f, f.CellM * 1.5f); var to = new Float2(f.CellM * 34.5f, f.CellM * 1.5f);
            float straight = 29f * f.CellM, len = pl.PathLength(from, to, SpeciesRole.Strider);
            Assert.Greater(len, straight * 1.1f); Assert.Less(len, float.PositiveInfinity);
        }

        /// <summary>Same raster in a local frame with origin (0, 0).</summary>
        sealed class Shifted : IBayCostField
        {
            readonly BayRasterCostField _f; public Shifted(BayRasterCostField f) { _f = f; }
            public float OriginX => 0f; public float OriginY => 0f; public float CellM => _f.CellM; public int Nx => _f.Nx; public int Ny => _f.Ny;
            public float DepthM(int ix, int iy) => _f.DepthM(ix, iy); public float Softness(int ix, int iy) => _f.Softness(ix, iy);
        }

        [Test]
        public void RealBayRasterGoldenGateBlocksStriderAndForcesADetour()
        {
            string p = FindRaster();
            if (p == null) { Assert.Ignore("bay_cost_256m_v1.bin not found (evidence/T-115)"); return; }
            var f = BayRasterCostField.Load(p);
            Assert.AreEqual(576, f.Nx); Assert.AreEqual(256f, f.CellM);
            // Golden Gate narrows (BayTM 55.6 km, 76.5 km area): deep water; Pacific side open ocean deep too
            float max = 0f; for (int iy = 290; iy < 300; iy++) for (int ix = 215; ix < 222; ix++) max = Math.Max(max, f.DepthM(ix, iy));
            Assert.Greater(max, 40f, "Golden Gate channel depth (blocks Strider 24 m and Breacher 40 m)");
            var pl = new GridRoutePlanner(f);
            var marin = new Float2(51555f, 82974f); var ggbSf = new Float2(56029f, 75861f);
            float straight = (float)Math.Sqrt((marin.X - ggbSf.X) * (marin.X - ggbSf.X) + (marin.Y - ggbSf.Y) * (marin.Y - ggbSf.Y));
            float len = pl.PathLength(marin, ggbSf, SpeciesRole.Strider);
            Assert.Greater(len, 2f * straight, "Marin to the SF abutment must go round the Golden Gate (or be unreachable)");
        }

        // ---- T-115h: bay_cost@v2 versioned switch ----
        [Test]
        public void VersionSwitchNamesAndRejectsUnknown()
        {
            Assert.AreEqual("bay_cost_256m_v1.bin", BayRasterCostField.FileNameFor("v1"));
            Assert.AreEqual("bay_cost_256m_v2.bin", BayRasterCostField.FileNameFor("v2"));
            Assert.AreEqual(BayRasterCostField.DefaultFileName, BayRasterCostField.FileNameFor(BayRasterCostField.V1));
            Assert.Throws<ArgumentException>(() => BayRasterCostField.FileNameFor("v3"));
            Assert.Throws<ArgumentException>(() => BayRasterCostField.FindRaster(Environment.CurrentDirectory, "v3"));
        }

        [Test]
        public void V2KeepsDepthAndV1ProxyInCoverageHolesAndRaisesSoftGround()
        {
            string p1 = BayRasterCostField.FindRaster(Environment.CurrentDirectory, "v1"), p2 = BayRasterCostField.FindRaster(Environment.CurrentDirectory, "v2");
            if (p1 == null || p2 == null) { Assert.Ignore("bay_cost rasters not found (evidence/T-115, evidence/T-115g)"); return; }
            var a = BayRasterCostField.Load(p1); var b = BayRasterCostField.Load(p2);
            Assert.AreEqual(a.Nx, b.Nx); Assert.AreEqual(a.Ny, b.Ny); Assert.AreEqual(a.CellM, b.CellM); Assert.AreEqual(a.OriginX, b.OriginX); Assert.AreEqual(a.OriginY, b.OriginY);
            int same = 0, soft1 = 0, soft2 = 0;
            for (int y = 0; y < a.Ny; y++) for (int x = 0; x < a.Nx; x++)
            {
                Assert.AreEqual(a.DepthM(x, y), b.DepthM(x, y), "depth is copied unchanged");
                if (a.Softness(x, y) == b.Softness(x, y)) same++;
                if (a.Softness(x, y) > 0.5f) soft1++; if (b.Softness(x, y) > 0.5f) soft2++;
            }
            Assert.Greater(same, a.Nx * a.Ny / 2, "outside the USGS coverage (holes, ocean) v2 keeps the v1 values");
            Assert.AreEqual(27408, soft1); Assert.AreEqual(48688, soft2);   // numbers recorded in evidence/T-115g/bay_cost_256m_v2.json
        }
    }
}
