using System;
using NUnit.Framework;

namespace Game.Rendering.Pixel.Tests
{
    /// <summary>T-181b: contract v2 (layer hints), the layerSnap flag and the engine-free validation the binder uses.</summary>
    public class PixelLayerHintsTests
    {
        const int H = 272;
        static readonly double TanV = Math.Tan(37.5 * Math.PI / 180) * 270 / 480;

        static PixelLayerHint Layer(double depth, float rx = 0, float ry = 0, float sx = 0, float sy = 0) =>
            new PixelLayerHint { ReferenceDepth = (float)depth, Grid = (float)PixelLayerPlan.GridM(depth, TanV, H), OffsetRight = rx, OffsetUp = ry, SubTexelX = sx, SubTexelY = sy };

        static PixelStabilisationHints Good()
        {
            var main = Layer(664.874, 0, 0, 0.3f, -0.2f);
            return new PixelStabilisationHints
            {
                ReferenceDepth = main.ReferenceDepth, PositionGrid = main.Grid, SubTexelX = 0.3f, SubTexelY = -0.2f, PixelScale = 4,
                LayerCount = 3, LayerNear = Layer(124.2, 0.01f, -0.02f, 0.4f, 0.1f), LayerMain = main, LayerFar = Layer(10353, 1.5f, 0.5f, 0.9f, 0.2f),
            };
        }

        [Test] public void ContractVersion_IsTwo() => Assert.AreEqual(2, PixelContractVersions.StabilisationHints);

        [Test]
        public void LayerSnapFlag_IsListed_AndDistinct()
        {
            Assert.AreEqual("rendering.pixel.layerSnap", PixelFlags.LayerSnap);
            Assert.Contains(PixelFlags.LayerSnap, PixelFlags.All);
            Assert.IsNull(PixelFlags.ForStage(PixelStageId.Present), "layerSnap gates no single stage");
        }

        [Test]
        public void V1Source_DefaultsToNoLayers_AndIsNotAnError()
        {
            var h = new PixelStabilisationHints { ReferenceDepth = 664.874f };
            Assert.AreEqual(0, h.LayerCount);
            Assert.IsFalse(PixelLayerHints.TryValidate(h, TanV, H, out string reason));
            Assert.IsNull(reason, "LayerCount 0 is the v1 path, not a rejection");
        }

        [Test]
        public void ThreeConsistentLayers_AreAccepted_AndFeedThePlan()
        {
            var h = Good();
            Assert.IsTrue(PixelLayerHints.TryValidate(h, TanV, H, out string reason), reason);
            var tuning = HazeTuning.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(PixelTestPaths.ProjectRoot(), "Assets", "Game", "Config", "Rendering", "PaletteProfileTuning.json")), "warm_rust_sepia");
            double tanH = Math.Tan(37.5 * Math.PI / 180);
            Assert.IsTrue(PixelLayerPlan.TryBuild(PixelLayerHints.ReferenceDepths(h), tuning, tanH, TanV, new PixelLayerPlanSettings(), null, out var slabs, out _, out string why), why);
            Assert.AreEqual(3, slabs.Length);
        }

        [Test]
        public void WrongLayerCount_IsRejectedWithReason()
        {
            var h = Good(); h.LayerCount = 2;
            Assert.IsFalse(PixelLayerHints.TryValidate(h, TanV, H, out string reason)); StringAssert.Contains("LayerCount", reason);
        }

        [Test]
        public void NonIncreasingDepths_AreRejected()
        {
            var h = Good(); h.LayerFar = Layer(500);
            Assert.IsFalse(PixelLayerHints.TryValidate(h, TanV, H, out string reason)); StringAssert.Contains("does not increase", reason);
            h = Good(); h.LayerNear = Layer(0);
            Assert.IsFalse(PixelLayerHints.TryValidate(h, TanV, H, out reason));
        }

        [Test]
        public void GridOffByMoreThanTolerance_IsRejected_AndWithinToleranceAccepted()
        {
            var h = Good(); h.LayerNear.Grid *= 1.001f;
            Assert.IsFalse(PixelLayerHints.TryValidate(h, TanV, H, out string reason)); StringAssert.Contains("near layer grid", reason);
            h = Good(); h.LayerFar.Grid *= 1.00002f;
            Assert.IsTrue(PixelLayerHints.TryValidate(h, TanV, H, out reason), reason);
        }

        [Test]
        public void MainLayer_MustMatchTheV1Fields()
        {
            var h = Good(); h.LayerMain.OffsetRight = 0.01f;
            Assert.IsFalse(PixelLayerHints.TryValidate(h, TanV, H, out _));
            h = Good(); h.LayerMain.SubTexelX = 0.31f;
            Assert.IsFalse(PixelLayerHints.TryValidate(h, TanV, H, out _));
            h = Good(); h.ReferenceDepth = 663f;
            Assert.IsFalse(PixelLayerHints.TryValidate(h, TanV, H, out _));
        }

        [Test]
        public void NonFinite_RemainderTooBig_AndHugeOffset_AreRejected()
        {
            var h = Good(); h.LayerNear.SubTexelX = float.NaN;
            Assert.IsFalse(PixelLayerHints.TryValidate(h, TanV, H, out _));
            h = Good(); h.LayerFar.SubTexelY = 1.5f;
            Assert.IsFalse(PixelLayerHints.TryValidate(h, TanV, H, out _));
            h = Good(); h.LayerFar.OffsetRight = 1000f;
            Assert.IsFalse(PixelLayerHints.TryValidate(h, TanV, H, out _));
        }

        [Test]
        public void LayerHint_IsBlittable()
        {
            // no references: sizeof-style check through Marshal on the engine-free runtime
            Assert.AreEqual(6 * 4 + 4, System.Runtime.InteropServices.Marshal.SizeOf(typeof(PixelLayerHint)) , "6 floats plus a 4-byte marshalled bool");
        }
    }
}
