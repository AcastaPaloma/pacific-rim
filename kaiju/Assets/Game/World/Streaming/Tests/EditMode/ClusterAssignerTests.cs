// Activity-cluster origins (R-101 section 6, O6): extent cap, tile-corner origins, local bound 4096 m, rebase rules.
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Game.World.Streaming.Tests
{
    public class ClusterAssignerTests
    {
        static FocusSample B(int id, double x, double z, double y = 20) => new FocusSample { Id = id, Kind = FocusKind.Body, X = x, Y = y, Z = z, NeedsPhysics = true };

        static void Run(ClusterAssigner a, bool quiet, FocusSample[] foci, out List<ClusterAssignment> asg, out List<ClusterInfo> info)
        {
            asg = new List<ClusterAssignment>(); info = new List<ClusterInfo>();
            a.Assign(foci, quiet, asg, info);
        }

        [Test]
        public void NearBodiesShareACluster_FarBodiesDoNot()
        {
            var a = new ClusterAssigner(new StreamingConfig());
            Run(a, true, new[] { B(1, 20000, 20000), B(2, 21500, 20800), B(3, 70000, 20000) }, out var asg, out var info);
            Assert.AreEqual(2, info.Count);
            Assert.AreEqual(asg[0].ClusterId, asg[1].ClusterId);
            Assert.AreNotEqual(asg[0].ClusterId, asg[2].ClusterId);
        }

        [Test]
        public void Origin_IsATileCornerNearTheCentre_AndLocalsAreWithin4096()
        {
            var a = new ClusterAssigner(new StreamingConfig());
            var foci = new List<FocusSample>();
            for (int i = 0; i < 30; i++) foci.Add(B(i, 30000 + i * 170.0, 40000 - i * 120.0));
            Run(a, true, foci.ToArray(), out var asg, out var info);
            foreach (var c in info)
            {
                Assert.AreEqual(0.0, c.OriginX % 2048.0); Assert.AreEqual(0.0, c.OriginZ % 2048.0);
                Assert.LessOrEqual(c.ExtentM, 5120.0);
            }
            foreach (var s in asg) Assert.LessOrEqual(Math.Max(Math.Abs(s.LocalX), Math.Max(Math.Abs(s.LocalY), Math.Abs(s.LocalZ))), 4096.0);
        }

        [Test]
        public void ExtentCap_ChainOfLinkedBodiesIsSplitAtOrBelow5120()
        {
            var a = new ClusterAssigner(new StreamingConfig());
            var foci = new List<FocusSample>();
            for (int i = 0; i < 12; i++) foci.Add(B(i, 10000 + i * 1500.0, 30000));   // each pair links (1500 < 3072), the chain spans 16.5 km
            Run(a, true, foci.ToArray(), out var asg, out var info);
            Assert.AreEqual(3, info.Count, "12 bodies 1.5 km apart pack into groups of 4 (4.5 km extent each)");
            foreach (var c in info) Assert.LessOrEqual(c.ExtentM, 5120.0, "cluster " + c.ClusterId);
            foreach (var s in asg) Assert.LessOrEqual(Math.Abs(s.LocalX), 4096.0);
        }

        [Test]
        public void ClusterIds_AreStableAcrossTicks_AndRebaseOnlyWhenNeeded()
        {
            var a = new ClusterAssigner(new StreamingConfig());
            int firstId = -1, rebases = 0;
            double maxLocal = 0;
            for (int s = 0; s < 3000; s++)
            {
                double x = 8000 + s * 30.0;
                Run(a, true, new[] { B(1, x, 40000), B(2, x + 200, 40100) }, out var asg, out var info);
                Assert.AreEqual(1, info.Count);
                if (firstId < 0) firstId = info[0].ClusterId;
                Assert.AreEqual(firstId, info[0].ClusterId, "id changed at step " + s);
                if (info[0].Rebased) rebases++;
                foreach (var q in asg) maxLocal = Math.Max(maxLocal, Math.Abs(q.LocalX));
            }
            Assert.LessOrEqual(maxLocal, 4096.0);
            // 90 km at a rebase every >= 1 km: tens of rebases, never one per tick
            Assert.Greater(rebases, 10);
            Assert.Less(rebases, 90);
        }

        [Test]
        public void SoftRebaseWaitsForQuietStep_ForcedRebaseDoesNot()
        {
            var a = new ClusterAssigner(new StreamingConfig());
            Run(a, true, new[] { B(1, 10240, 10240) }, out var asg0, out var info0);   // origin = (10240, 10240): exactly a corner
            Assert.AreEqual(10240.0, info0[0].OriginX);
            // drift 3300 m east on loud steps: past 3072, below 3840 -> due, not rebased
            Run(a, false, new[] { B(1, 10240 + 3300, 10240) }, out _, out var info1);
            Assert.IsFalse(info1[0].Rebased); Assert.IsTrue(info1[0].RebaseDue);
            Assert.AreEqual(10240.0, info1[0].OriginX);
            // the first quiet step rebases
            Run(a, true, new[] { B(1, 10240 + 3300, 10240) }, out var asg2, out var info2);
            Assert.IsTrue(info2[0].Rebased);
            Assert.AreEqual(10240.0 + 2 * 2048.0 - 0.0, info2[0].OriginX + 0.0 * 1, 2048.0, "new origin is a corner near the body");
            Assert.LessOrEqual(Math.Abs(asg2[0].LocalX), 1024.0);
            // forced: loud step beyond 3840
            var b = new ClusterAssigner(new StreamingConfig());
            Run(b, true, new[] { B(1, 10240, 10240) }, out _, out _);
            Run(b, false, new[] { B(1, 10240 + 3900, 10240) }, out var asg3, out var info3);
            Assert.IsTrue(info3[0].Rebased, "forced rebase beyond 3840 m on a loud step");
            Assert.LessOrEqual(Math.Abs(asg3[0].LocalX), 4096.0);
        }

        [Test]
        public void Cluster_MergeAndSplitKeepWithinBound()
        {
            var a = new ClusterAssigner(new StreamingConfig());
            double maxLocal = 0; int maxClusters = 0;
            for (int s = 0; s < 2000; s++)
            {
                // two bodies approach, pass and separate (relative speed 40 m/s); a third sits 30 km away
                double x1 = 20000 + s * 20.0, x2 = 60000 - s * 20.0;
                Run(a, true, new[] { B(1, x1, 30000), B(2, x2, 30400), B(3, 90000, 30000) }, out var asg, out var info);
                maxClusters = Math.Max(maxClusters, info.Count);
                foreach (var q in asg) maxLocal = Math.Max(maxLocal, Math.Max(Math.Abs(q.LocalX), Math.Abs(q.LocalZ)));
                foreach (var c in info) Assert.LessOrEqual(c.ExtentM, 5120.0);
            }
            Assert.LessOrEqual(maxLocal, 4096.0);
            Assert.AreEqual(3, maxClusters);
        }

        [Test]
        public void LocalPositions_ReconstructTheWorldPositionWithin1mm()
        {
            var a = new ClusterAssigner(new StreamingConfig());
            var foci = new[] { B(1, 36123.456789, 52987.654321, 123.456), B(2, 37001.001, 52000.002, 3.0) };
            Run(a, true, foci, out var asg, out var info);
            foreach (var s in asg)
            {
                var c = info.Find(x => x.ClusterId == s.ClusterId);
                var f = foci[s.FocusId - 1];
                Assert.LessOrEqual(Math.Abs(c.OriginX + (double)s.LocalX - f.X), 1e-3);
                Assert.LessOrEqual(Math.Abs(c.OriginZ + (double)s.LocalZ - f.Z), 1e-3);
                Assert.LessOrEqual(Math.Abs((double)s.LocalY - f.Y), 1e-3);
            }
        }
    }
}
