using NUnit.Framework;

namespace Game.UI.Tests
{
    public class HudLinearUploadTests
    {
        [Test]
        public void RoundTrip_AllBytes_ThroughHalf_IsExact()
        {
            for (int b = 0; b < 256; b++)
            {
                float lin = HudLinearUpload.FromHalf(HudLinearUpload.DecodedHalf((byte)b));
                Assert.AreEqual(b, HudLinearUpload.Encode(HudLinearUpload.GuiGamma(lin)), "byte " + b);
            }
        }

        [Test]
        public void RoundTrip_EveryPaletteEntry_IsExact()
        {
            var table = HudColourTable.Parse(DataFiles.Read("Assets/Game/Config/UI/hud_colours.json"));
            Assert.Greater(table.Entries.Count, 0);
            int n = 0;
            foreach (var e in table.Entries)
            {
                var all = new System.Collections.Generic.List<Rgb> { e.Panel, e.Text };
                all.AddRange(e.Fills);
                foreach (var c in all)
                {
                    var src = new byte[] { c.R, c.G, c.B, 255 };
                    var dst = new ushort[4];
                    HudLinearUpload.ToHalfRgba(src, dst);
                    Assert.AreEqual(c.R, HudLinearUpload.Encode(HudLinearUpload.GuiGamma(HudLinearUpload.FromHalf(dst[0]))), e.Profile + " " + c);
                    Assert.AreEqual(c.G, HudLinearUpload.Encode(HudLinearUpload.GuiGamma(HudLinearUpload.FromHalf(dst[1]))), e.Profile + " " + c);
                    Assert.AreEqual(c.B, HudLinearUpload.Encode(HudLinearUpload.GuiGamma(HudLinearUpload.FromHalf(dst[2]))), e.Profile + " " + c);
                    Assert.AreEqual(0x3C00, dst[3]);
                    n++;
                }
            }
            Assert.Greater(n, 0);
        }

        [Test]
        public void Known_Panel_And_Text_Decode_To_Expected_Linear()
        {
            // The observed bug: raw 0x03 encoded once gave 0x0c-ish brightening; decoded value must be far smaller than raw.
            Assert.Less(HudLinearUpload.Decode(0x03), 3f / 255f);
            Assert.AreEqual(0f, HudLinearUpload.Decode(0), 0f);
            Assert.AreEqual(1f, HudLinearUpload.Decode(255), 1e-6f);
        }

        [Test]
        public void ObservedPlayerReadback_Is_Reproduced_By_The_Model()
        {
            // Player run (T-154b it.1, pure-decode upload): 031117 read 064955 and fbc585 read fde4bf. Model: Encode(GuiGamma(Decode(b))).
            int[] src = { 0x03, 0x11, 0x17, 0xfb, 0xc5, 0x85 }; int[] obs = { 0x06, 0x49, 0x55, 0xfd, 0xe4, 0xbf };
            for (int i = 0; i < 6; i++) Assert.AreEqual(obs[i], HudLinearUpload.Encode(HudLinearUpload.GuiGamma(HudLinearUpload.Decode(src[i]))), 1, "channel " + i);
        }

        [Test]
        public void Alpha_Zero_And_Full_Preserved()
        {
            var dst = new ushort[8];
            HudLinearUpload.ToHalfRgba(new byte[] { 0, 0, 0, 0, 255, 255, 255, 255 }, dst);
            Assert.AreEqual(0, dst[3]); Assert.AreEqual(0x3C00, dst[7]); Assert.AreEqual(0x3C00, dst[4]);
        }
    }
}
