using Game.Slice;
using NUnit.Framework;

namespace Game.Slice.Tests
{
    public class SlicePixelCheckTests
    {
        const int W = 20, H = 10;
        static readonly uint Sky = SlicePixelCheck.Pack(99, 77, 78), Sky2 = SlicePixelCheck.Pack(120, 90, 90), Grass = SlicePixelCheck.Pack(40, 120, 50), Brick = SlicePixelCheck.Pack(150, 60, 40);

        // a "rendered" frame: `colours` bands of equal width, none of them a sky colour
        static uint[] Rich(int colours, int shift = 0)
        {
            var a = new uint[W * H];
            for (int i = 0; i < a.Length; i++) a[i] = SlicePixelCheck.Pack((byte)(10 + 10 * ((i + shift) % colours)), 120, 50);
            return a;
        }

        static uint[] Fill(uint c) { var a = new uint[W * H]; for (int i = 0; i < a.Length; i++) a[i] = c; return a; }

        [Test]
        public void BlankFrames_Fail_EvenWhenTheyMatchTheSky()
        {
            var sky = Fill(Sky); sky[3] = Sky2;
            var v = SlicePixelCheck.Evaluate(sky, new[] { Fill(Sky), Fill(Sky) }, W, H, null, SlicePixelCheck.MinNonBackgroundShare);
            Assert.AreEqual(0.0, v.MinShare); Assert.IsFalse(v.SharesPass); Assert.IsFalse(v.PairsPass); Assert.IsFalse(v.Pass);
        }

        [Test]
        public void FrozenFrames_Fail_PairDifference()
        {
            var sky = Fill(Sky); var f = Rich(10);
            var v = SlicePixelCheck.Evaluate(sky, new[] { f, (uint[])f.Clone(), (uint[])f.Clone() }, W, H, null, SlicePixelCheck.MinNonBackgroundShare);
            Assert.IsTrue(v.SharesPass); Assert.AreEqual(0.0, v.MinPairDiffShare); Assert.IsFalse(v.PairsPass); Assert.IsFalse(v.Pass);
        }

        [Test]
        public void RenderedAndMovingFrames_Pass()
        {
            var sky = Fill(Sky); var a = Rich(10); var b = Rich(10); b[5] = Brick; b[6] = Brick; var c = Rich(10); c[40] = Brick;
            var v = SlicePixelCheck.Evaluate(sky, new[] { a, b, c }, W, H, null, SlicePixelCheck.MinNonBackgroundShare);
            Assert.IsTrue(v.Pass); Assert.AreEqual(1.0, v.MinShare, 1e-9); Assert.AreEqual(2.0 / (W * H), v.PairDiffShare[0, 1], 1e-9); Assert.Greater(v.MinPairDiffShare, 0.0); Assert.IsTrue(v.RichnessPass);
        }

        [Test]
        public void ShareBelowThreshold_Fails()
        {
            var sky = Fill(Sky); var a = Fill(Sky); for (int i = 0; i < 20; i++) a[i] = Rich(10)[i];   // 10 percent
            var b = (uint[])a.Clone(); b[100] = Brick;
            var v = SlicePixelCheck.Evaluate(sky, new[] { a, b }, W, H, null, 0.2);
            Assert.AreEqual(0.1, v.NonBackgroundShare[0], 1e-9); Assert.IsFalse(v.SharesPass); Assert.IsTrue(v.PairsPass); Assert.IsFalse(v.Pass);
        }

        [Test]
        public void IgnoredRectangle_IsExcluded()
        {
            var sky = Fill(Sky); var a = Fill(Sky); var b = Fill(Sky);
            for (int x = 0; x < 5; x++) { a[x] = Brick; b[x] = Grass; }   // HUD strip in row 0 only
            var ignore = new[] { new PixelRect(0, 0, W, 1) };
            var v = SlicePixelCheck.Evaluate(sky, new[] { a, b }, W, H, ignore, 0.2);
            Assert.AreEqual(0.0, v.MinShare); Assert.AreEqual(0.0, v.MinPairDiffShare); Assert.IsFalse(v.Pass);
        }

        [Test]
        public void FlatWall_FailsRichness_EvenThoughItIsNotTheSky()
        {
            var sky = Fill(Sky); var wall = Fill(Grass); var wall2 = Fill(Grass); wall2[3] = Brick;   // what the DEF-M1slice-f1 frames looked like: one flat colour plus a streak
            var v = SlicePixelCheck.Evaluate(sky, new[] { wall, wall2 }, W, H, null, SlicePixelCheck.MinNonBackgroundShare);
            Assert.IsTrue(v.SharesPass); Assert.IsTrue(v.PairsPass); Assert.IsFalse(v.RichnessPass); Assert.IsFalse(v.Pass);
            Assert.Greater(v.DominantShare[0], SlicePixelCheck.MaxDominantShare);
        }

        [Test]
        public void FewColours_FailRichness()
        {
            var sky = Fill(Sky); var a = Rich(4); var b = Rich(4, 1);
            var v = SlicePixelCheck.Evaluate(sky, new[] { a, b }, W, H, null, SlicePixelCheck.MinNonBackgroundShare);
            Assert.AreEqual(4, v.MinDistinct); Assert.IsFalse(v.RichnessPass); Assert.IsFalse(v.Pass);
        }
    }
}
