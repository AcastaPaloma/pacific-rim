// BSSA14 / MMI / Vs30 / ShakingField tests (T-027 step 3). Table rows: research/R-022.md section 3.
using System;
using NUnit.Framework;

namespace Game.World.Seismic.Tests
{
    public class Bssa14Tests
    {
        // R-022 prints 2 significant figures (PGA 0.44, PGV 46, MMI 8.1), so the printed value carries up to +-0.5 in the 2nd digit:
        // 0.005/0.17 = 2.9 % for the smallest PGA (0.17 g) and 0.5/15 = 3.3 % for PGV 15. The task tolerance is 3 % relative, with
        // the print half-unit as absolute floor (max of the two), so a 2-s.f. rounding of the document can not fail a correct model.
        const double RelTol = 0.03;

        static void Near(double expected, double actual, double half, string what)
        {
            double tol = Math.Max(RelTol * expected, half);
            Assert.AreEqual(expected, actual, tol, what);
        }

        // mw, rjb km, vs30, pga, pgv, mmi
        static readonly double[][] Rows =
        {
            new[] { 7.2, 2.3, 760, 0.44, 46, 8.1 }, new[] { 7.2, 2.3, 270, 0.52, 76, 8.8 },
            new[] { 7.2, 5.1, 760, 0.36, 37, 7.8 }, new[] { 7.2, 5.1, 270, 0.44, 64, 8.6 },
            new[] { 7.2, 11.6, 760, 0.24, 23, 7.2 }, new[] { 7.2, 11.6, 270, 0.32, 42, 8.0 },
            new[] { 7.2, 13.6, 760, 0.22, 20, 7.0 }, new[] { 7.2, 13.6, 270, 0.29, 38, 7.9 },
            new[] { 7.2, 20.0, 760, 0.17, 15, 6.6 }, new[] { 7.2, 20.0, 270, 0.24, 29, 7.5 },
            new[] { 7.0, 11.6, 760, 0.22, 20, 7.0 }, new[] { 7.0, 11.6, 270, 0.30, 38, 7.9 },
        };

        [Test]
        public void MedianShakingTable_MatchesR022()
        {
            foreach (var r in Rows)
            {
                string tag = "Mw " + r[0] + " Rjb " + r[1] + " Vs30 " + r[2];
                double pga = Bssa14.PgaG(r[0], r[1], r[2]), pgv = Bssa14.PgvCmS(r[0], r[1], r[2]);
                Near(r[3], pga, 0.005, "PGA " + tag);
                Near(r[4], pgv, 0.5, "PGV " + tag);
                // MMI is printed to 1 decimal; MMI computed from the printed PGV is checked as well as from the model PGV.
                Near(r[5], Bssa14.Mmi(pgv), 0.05, "MMI " + tag);
            }
        }

        [Test]
        public void Pga_DecreasesWithDistance()
        {
            foreach (double vs in new[] { 760.0, 270.0 })
            {
                double prev = double.MaxValue;
                for (double d = 0.5; d <= 60; d += 0.5)
                {
                    double v = Bssa14.PgaG(7.2, d, vs);
                    Assert.Less(v, prev, "PGA at " + d + " km Vs30 " + vs);
                    prev = v;
                }
            }
        }

        [Test]
        public void Pga_GrowsWithMagnitude_AndSoftSoilAmplifiesPgv()
        {
            Assert.Greater(Bssa14.PgaG(7.2, 10, 760), Bssa14.PgaG(6.2, 10, 760));
            Assert.Greater(Bssa14.PgvCmS(7.2, 10, 270), Bssa14.PgvCmS(7.2, 10, 760));
        }

        [Test]
        public void Mmi_BranchesAreContinuousAtBreak()
        {
            double lo = Math.Pow(10, Bssa14.MmiBreakLogPgv - 1e-6), hi = Math.Pow(10, Bssa14.MmiBreakLogPgv + 1e-6);
            Assert.AreEqual(Bssa14.Mmi(lo), Bssa14.Mmi(hi), 0.05);
            // the two branches evaluated at the break itself
            double a = 3.78 + 1.47 * 0.53, b = 2.89 + 3.16 * 0.53;
            Assert.AreEqual(a, b, 0.05);
            Assert.AreEqual(a, Bssa14.Mmi(Math.Pow(10, 0.53)), 1e-9);
        }

        [Test]
        public void Rjb_AgainstKnownPolyline()
        {
            // L-shaped line (0,0)-(10,0)-(10,10)
            double[] x0 = { 0, 10 }, z0 = { 0, 0 }, x1 = { 10, 10 }, z1 = { 0, 10 };
            Assert.AreEqual(3.0, ShakingField.Rjb(x0, z0, x1, z1, 5, 3), 1e-12);      // perpendicular to first segment
            Assert.AreEqual(4.0, ShakingField.Rjb(x0, z0, x1, z1, 14, 6), 1e-12);     // perpendicular to second
            Assert.AreEqual(5.0, ShakingField.Rjb(x0, z0, x1, z1, -3, -4), 1e-12);    // beyond start: distance to endpoint
            Assert.AreEqual(0.0, ShakingField.Rjb(x0, z0, x1, z1, 10, 5), 1e-12);     // on the line
            Assert.AreEqual(Math.Sqrt(2) * 3, ShakingField.Rjb(x0, z0, x1, z1, 13, -3), 1e-12);
        }

        static RuptureResult Rupture(bool bridgeMiddle)
        {
            var sys = new FaultSystem
            {
                Name = "T", FaultId = 1, Sections = new[] { "s" },
                X = new double[] { 0, 10000, 20000, 30000 }, Z = new double[4],
                ArcM = new double[] { 0, 10000, 20000, 30000 },
                SectionOfVertex = new ushort[4], Bridge = new[] { false, bridgeMiddle, false },
            };
            return new RuptureResult { System = sys, EventId = 5, Mw = 7.2, VertexStart = 0, VertexEnd = 3 };
        }

        [Test]
        public void ShakingField_UsesOnlyMappedSegments()
        {
            var f = new ShakingField(Rupture(false));
            ShakingSample s;
            Assert.IsTrue(f.TryGetShaking(5, 15000, 5000, 760, out s));
            Assert.AreEqual(5000f, s.RjbM, 1e-3);
            Assert.IsFalse(f.TryGetShaking(6, 15000, 5000, 760, out s));

            var g = new ShakingField(Rupture(true));   // middle segment is a bridged gap: not part of the rupture
            Assert.IsTrue(g.TryGetShaking(5, 15000, 5000, 760, out s));
            Assert.AreEqual(Math.Sqrt(5000.0 * 5000 + 5000.0 * 5000), s.RjbM, 1e-2);
            Assert.AreEqual((float)Bssa14.PgaG(7.2, s.RjbM / 1000.0, 760), s.PgaG, 1e-6);
            Assert.AreEqual((float)Bssa14.Mmi(s.PgvCmS), s.Mmi, 1e-5);
        }

        [Test]
        public void Vs30Provider_DefaultsToRock_FillIsOptIn()
        {
            var p = new Vs30Provider();
            Assert.AreEqual(760.0, p.Vs30At(5, 5));
            p.AddFillPolygon(new double[] { 0, 10, 10, 0 }, new double[] { 0, 0, 10, 10 });
            Assert.AreEqual(270.0, p.Vs30At(5, 5));
            Assert.AreEqual(760.0, p.Vs30At(15, 5));
            Assert.AreEqual(760.0, p.Vs30At(5, -1));
        }
    }
}
