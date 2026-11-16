using System;
using NUnit.Framework;

namespace Game.Rendering.Pixel.Tests
{
    /// <summary>T-196: one output-pixel lattice phase for the three slab shifts (flag layerSharedPhase), and the C11 bound it costs.</summary>
    public class PixelSharedPhaseTests
    {
        const int K = 4, Limit = 8;
        static readonly double[] Depths = { 124.2, 664.874, 10353.0 };

        /// <summary>Hints of the T1 lateral translation (camera c in main-reference texels): each layer snaps up (ceil) to its own grid; remainder in own texels.</summary>
        static PixelStabilisationHints T1(double c, double y = 0)
        {
            var l = new PixelLayerHint[3];
            for (int i = 0; i < 3; i++)
            {
                double rho = Depths[1] / Depths[i], u = c * rho, v = y * rho;
                l[i] = new PixelLayerHint { ReferenceDepth = (float)Depths[i], SubTexelX = (float)(u - Math.Ceiling(u)), SubTexelY = (float)(v - Math.Ceiling(v)) };
            }
            return new PixelStabilisationHints { LayerCount = 3, LayerNear = l[0], LayerMain = l[1], LayerFar = l[2], SubTexelX = l[1].SubTexelX, SubTexelY = l[1].SubTexelY };
        }

        [Test]
        public void Flag_IsListed_AndShipsOff()
        {
            Assert.AreEqual("rendering.pixel.layerSharedPhase", PixelFlags.LayerSharedPhase);
            Assert.Contains(PixelFlags.LayerSharedPhase, PixelFlags.All);
        }

        [Test]
        public void EveryFrame_AllSlabsShareOneLatticePhase_InsideTheGuardBand()
        {
            var sp = new PixelSharedPhase(); var s = new int[6];
            for (int i = 0; i < 400; i++)
            {
                sp.Resolve(T1(0.6153 + 0.0902 * i, 0.31 + 0.037 * i), K, Limit, i == 0, s);
                for (int a = 0; a < 2; a++)
                {
                    int p = PixelSharedPhase.Mod(s[a], K);
                    for (int sl = 0; sl < 3; sl++)
                    {
                        Assert.AreEqual(p, PixelSharedPhase.Mod(s[2 * sl + a], K), "frame " + i + " slab " + sl + " axis " + a);
                        Assert.LessOrEqual(Math.Abs(s[2 * sl + a]), Limit);
                    }
                }
            }
        }

        [Test]
        public void CutFrame_HasNoDeviation_AndAFrameLaterTheReferenceIsKept()
        {
            var sp = new PixelSharedPhase(); var s = new int[6];
            sp.Resolve(T1(3.3), K, Limit, true, s);
            Assert.AreEqual(0.0, sp.LastWorstX, 1e-9); Assert.AreEqual(0.0, sp.LastWorstY, 1e-9);
            sp.Resolve(T1(3.3), K, Limit, false, s);
            Assert.AreEqual(0.0, sp.LastWorstX, 1e-9, "same pose, same shifts");
        }

        [Test]
        public void EqualRemainders_GiveEqualShifts()
        {
            // Yaw-only motion and stills: every layer carries the same remainder, so the shared phase is no constraint.
            var sp = new PixelSharedPhase(); var s = new int[6];
            var h = new PixelStabilisationHints { LayerCount = 3 };
            for (int i = 0; i < 50; i++)
            {
                float r = -(float)((i * 0.173) % 1.0);
                h.LayerNear.SubTexelX = h.LayerMain.SubTexelX = h.LayerFar.SubTexelX = r;
                sp.Resolve(h, K, Limit, i == 0, s);
                Assert.AreEqual(s[0], s[2]); Assert.AreEqual(s[2], s[4]);
                Assert.AreEqual(0.0, sp.LastWorstX, 0.5 + 1e-6);
            }
        }

        /// <summary>
        /// The C11 cost (T1, 180 frames, 24 starts x 16 edge phases folded into the start): per-shot deviation is at most 1.5 output px for every slab, and no integer
        /// lattice phase can do better than about 1.5 px on some frame (two slabs drifting apart sweep every residue mod k). The per-slab path holds 0.5 px.
        /// </summary>
        [Test]
        public void T1Translation_DeviationIsBoundedBy1p5Px_AndReachesIt()
        {
            double worst = 0;
            for (int j = 0; j < 24; j++)
            {
                var sp = new PixelSharedPhase(); var s = new int[6];
                double off = ((j + 0.5) * 0.6180339887498949) % 1.0 * 7;
                for (int i = 0; i < 180; i++)
                {
                    sp.Resolve(T1(0.6153 + off + 0.0902 * i), K, Limit, i == 0, s);
                    Assert.LessOrEqual(sp.LastWorstX, 1.5 + 1e-6, "start " + j + " frame " + i);
                    worst = Math.Max(worst, sp.LastWorstX);
                }
            }
            Assert.Greater(worst, 1.25, "the shared lattice cannot hold the 1 px C11 gate on a lateral translation");
        }

        [Test]
        public void Nearest_StaysCongruent_AndInsideTheLimitWhenPossible()
        {
            Assert.AreEqual(-7, PixelSharedPhase.Nearest(-6.6, 1, K, Limit));
            Assert.AreEqual(5, PixelSharedPhase.Nearest(9.4, 1, K, Limit), "9 is outside the 8 px guard band");
            Assert.AreEqual(3, PixelSharedPhase.Mod(-1, K));
        }

        [Test]
        public void Sidecar_WritesSharedPhase_OnlyWhenApplied()
        {
            var h = T1(1.7); h.LayerNear.Grid = 1; h.LayerMain.Grid = 1; h.LayerFar.Grid = 1;
            Assert.IsTrue(PixelLayerPlan.TryBuild(new[] { 124.2, 664.874, 10353.0 }, null, 0.77, 0.43, null, null, out var slabs, out var bands, out _));
            string on = PixelLayerSidecar.Build(h, slabs, bands, new int[6], K, 2, true, false, true);
            string off = PixelLayerSidecar.Build(h, slabs, bands, new int[6], K, 1, true, false);
            StringAssert.Contains("\"shared_phase\": true", on);
            StringAssert.DoesNotContain("shared_phase", off);
        }
    }
}
