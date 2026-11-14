using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Game.Rendering.Pixel.Tests
{
    /// <summary>Engine-free tests, run by Unity EditMode and by `dotnet test DotnetTests/Rendering.Pixel.Tests`.</summary>
    internal static class PixelTestPaths
    {
        public static string ProjectRoot()
        {
            foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
                for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                    if (Directory.Exists(Path.Combine(d.FullName, "Assets", "Game", "Rendering", "Pixel"))) return d.FullName;
            throw new DirectoryNotFoundException("project root with Assets/Game/Rendering/Pixel not found");
        }

        public static string PaletteSetPath() => Path.Combine(ProjectRoot(), "Assets", "Game", "Config", "Rendering", "palette_set.json");
        public static List<PaletteProfileData> Profiles() => PaletteSetReader.Read(File.ReadAllText(PaletteSetPath()));

        public static PaletteProfileData Profile(string id)
        {
            foreach (var p in Profiles()) if (p.Id == id) return p;
            throw new KeyNotFoundException(id);
        }

        public static PaletteLutInput LutInput(PaletteProfileData p) => new PaletteLutInput { Colours = p.Colours, ValueRamp = p.RampUniform, AccentIndex = p.AccentIndex, Size = 16 };
    }

    public class ColourMathTests
    {
        // Sharma, Wu, Dalal (2005) CIEDE2000 test data (pairs 1-4, 17, 19, 20, 25, 26, 34).
        static readonly double[][] Sharma =
        {
            new[] { 50.0, 2.6772, -79.7751, 50.0, 0.0, -82.7485, 2.0425 },
            new[] { 50.0, 3.1571, -77.2803, 50.0, 0.0, -82.7485, 2.8615 },
            new[] { 50.0, 2.8361, -74.0200, 50.0, 0.0, -82.7485, 3.4412 },
            new[] { 50.0, -1.3802, -84.2814, 50.0, 0.0, -82.7485, 1.0000 },
            new[] { 50.0, 2.5, 0.0, 50.0, 0.0, -2.5, 4.3065 },
            new[] { 50.0, 2.5, 0.0, 73.0, 25.0, -18.0, 27.1492 },
            new[] { 50.0, 2.5, 0.0, 61.0, -5.0, 29.0, 22.8977 },
            new[] { 60.2574, -34.0099, 36.2677, 60.4626, -34.1751, 39.4387, 1.2644 },
            new[] { 63.0109, -31.0961, -5.8663, 62.8187, -29.7946, -4.0864, 1.2630 },
            new[] { 2.0776, 0.0795, -1.1350, 0.9033, -0.0636, -0.5514, 0.9082 },
        };

        [Test]
        public void DeltaE2000_MatchesPublishedTestData()
        {
            foreach (var p in Sharma)
            {
                var a = new Vec3d(p[0], p[1], p[2]); var b = new Vec3d(p[3], p[4], p[5]);
                Assert.AreEqual(p[6], ColourMath.DeltaE2000(a, b), 1e-4, "pair " + p[0] + "," + p[1] + "," + p[2]);
                Assert.AreEqual(p[6], ColourMath.DeltaE2000(b, a), 1e-4, "symmetric");
            }
        }

        [Test]
        public void Lab_OfKnownColours()
        {
            var white = ColourMath.ToLab(new Rgb8(255, 255, 255));
            Assert.AreEqual(100.0, white.X, 1e-3); Assert.AreEqual(0.0, white.Y, 1e-2); Assert.AreEqual(0.0, white.Z, 1e-2);
            Assert.AreEqual(53.585, ColourMath.ToLab(new Rgb8(128, 128, 128)).X, 1e-2);
            var red = ColourMath.ToLab(new Rgb8(255, 0, 0));
            Assert.AreEqual(53.24, red.X, 0.02); Assert.AreEqual(80.09, red.Y, 0.05); Assert.AreEqual(67.20, red.Z, 0.05);
            Assert.AreEqual(0.0, ColourMath.ToLab(new Rgb8(0, 0, 0)).X, 1e-9);
        }

        [Test]
        public void Lab_RoundTripsThroughLinear()
        {
            foreach (var c in new[] { new Rgb8(12, 200, 99), new Rgb8(255, 255, 255), new Rgb8(1, 2, 3), new Rgb8(149, 164, 192) })
                Assert.AreEqual(c, ColourMath.ToRgb8(ColourMath.LabToLinear(ColourMath.ToLab(c))));
        }

        [Test]
        public void EightBitRounding_StaysUnderTheTolerance()
        {
            // R-002 F1: one code value of drift in a channel costs at most about 1.22 dE00, below the M-003a tolerance 2.0.
            double worst = 0; uint s = 7;
            for (int i = 0; i < 4000; i++)
            {
                s = s * 1664525u + 1013904223u; byte r = (byte)(s >> 24); s = s * 1664525u + 1013904223u; byte g = (byte)(s >> 24); s = s * 1664525u + 1013904223u; byte b = (byte)(s >> 24);
                var a = ColourMath.ToLab(new Rgb8(r, g, b));
                var o = ColourMath.ToLab(i % 3 == 0 ? new Rgb8((byte)Math.Min(255, r + 1), g, b) : i % 3 == 1 ? new Rgb8(r, (byte)Math.Max(0, g - 1), b) : new Rgb8(r, g, (byte)Math.Min(255, b + 1)));
                worst = Math.Max(worst, ColourMath.DeltaE2000(a, o));
            }
            Assert.Less(worst, 2.0);
        }

        [Test]
        public void NearestIndex_ExactColourIsItself_AndTiesTakeTheFirst()
        {
            var palette = new[] { new Rgb8(10, 20, 30), new Rgb8(200, 100, 50), new Rgb8(10, 20, 30), new Rgb8(90, 90, 90) };
            var lab = ColourMath.ToLab(palette);
            Assert.AreEqual(1, ColourMath.NearestIndex(lab, lab[1], out double d1)); Assert.AreEqual(0.0, d1, 1e-12);
            Assert.AreEqual(0, ColourMath.NearestIndex(lab, lab[2], out _), "duplicate colours: first index wins");
            Assert.AreEqual(3, ColourMath.NearestIndex(lab, ColourMath.ToLab(new Rgb8(96, 92, 88)), out double d3)); Assert.Greater(d3, 0);
        }

        [Test]
        public void Hex_RoundTrips_AndRejectsMalformed()
        {
            Assert.AreEqual(new Rgb8(0x24, 0x3a, 0x43), Rgb8.FromHex("#243a43"));
            Assert.AreEqual("#95a4c0", Rgb8.FromHex("#95A4C0").ToHex());
            Assert.Throws<FormatException>(() => Rgb8.FromHex("243a43"));
            Assert.Throws<FormatException>(() => Rgb8.FromHex("#12345"));
        }

        [Test]
        public void PaletteSha256_IsTheCanonicalCompactJsonHash()
        {
            // sha256 of the UTF-8 bytes of ["#000000","#ffffff"] (computed independently with Python hashlib).
            Assert.AreEqual("7cbabe6a63979ed77c93f311f788fe10f19efe25da700100d9e5d6c97067175d",
                ColourMath.PaletteSha256(new[] { new Rgb8(0, 0, 0), new Rgb8(255, 255, 255) }));
        }
    }

    public class DitherMaskTests
    {
        [Test]
        public void Bayer2_And_Bayer4_AreTheReferenceMatrices()
        {
            CollectionAssert.AreEqual(new[] { 0, 2, 3, 1 }, DitherMasks.Bayer(2));
            CollectionAssert.AreEqual(new[] { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 }, DitherMasks.Bayer(4));
        }

        [TestCase("bayer4", 4)] [TestCase("bayer8", 8)] [TestCase("cluster8", 8)] [TestCase("bluenoise16", 16)]
        public void Masks_ArePermutations_AndDeterministic(string id, int expectedSize)
        {
            int[] m = DitherMasks.Create(id, out int size);
            Assert.AreEqual(expectedSize, size); Assert.AreEqual(size * size, m.Length);
            var seen = new bool[m.Length];
            foreach (int v in m) { Assert.IsTrue(v >= 0 && v < m.Length && !seen[v], "rank " + v); seen[v] = true; }
            CollectionAssert.AreEqual(m, DitherMasks.Create(id, out _));
        }

        [Test]
        public void Bayer4_HalfMixIsACheckerboard()
        {
            int[] m = DitherMasks.Bayer(4);
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    Assert.AreEqual((x + y) % 2 == 0, m[y * 4 + x] < 8, "cell " + x + "," + y);
        }

        [Test]
        public void ClusteredDot_HalfMixFormsClumps_UnlikeBayer()
        {
            // Share of 4-neighbour pairs that differ at a 50% mix: a checker is 1.0, clumps are far lower.
            Assert.AreEqual(1.0, NeighbourDifference(DitherMasks.Bayer(8), 8), 1e-9);
            Assert.Less(NeighbourDifference(DitherMasks.ClusteredDot(8), 8), 0.6);
        }

        static double NeighbourDifference(int[] m, int size)
        {
            int diff = 0, total = 0, half = m.Length / 2;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool on = m[y * size + x] < half;
                    if ((m[y * size + (x + 1) % size] < half) != on) diff++;
                    if ((m[((y + 1) % size) * size + x] < half) != on) diff++;
                    total += 2;
                }
            return diff / (double)total;
        }

        [Test]
        public void ThresholdBytes_AreStrictlyOrderedByRank_AndCentred()
        {
            int[] ranks = DitherMasks.Bayer(4); byte[] t = DitherMasks.ToThresholdBytes(ranks);
            for (int i = 0; i < 16; i++)
                for (int j = 0; j < 16; j++)
                    if (ranks[i] < ranks[j]) Assert.Less(t[i], t[j]);
            Assert.AreEqual(8, t[0]);      // rank 0  -> floor(256 * 0.5 / 16)
            Assert.AreEqual(248, t[12]);   // rank 15 -> floor(256 * 15.5 / 16)
        }
    }

    public class PaletteLutTests
    {
        [Test]
        public void Bake_IsDeterministic_AndEveryCellIsAValidPair()
        {
            var p = PixelTestPaths.Profile("slate_teal"); var input = PixelTestPaths.LutInput(p);
            byte[] lut = PaletteLutBaker.Bake(input);
            Assert.AreEqual(16 * 16 * 16 * 4, lut.Length);
            CollectionAssert.AreEqual(lut, PaletteLutBaker.Bake(PixelTestPaths.LutInput(p)));
            var lab = ColourMath.ToLab(p.Colours);
            for (int o = 0; o < lut.Length; o += 4)
            {
                Assert.Less(lut[o], p.Colours.Count); Assert.Less(lut[o + 1], p.Colours.Count); Assert.AreEqual(255, lut[o + 3]);
                Assert.LessOrEqual(lab[lut[o]].X, lab[lut[o + 1]].X, "index a is the darker colour");
                if (lut[o] == lut[o + 1]) Assert.AreEqual(0, lut[o + 2], "a single colour has ratio 0");
            }
        }

        [TestCase("warm_rust_sepia")] [TestCase("warm_amber_haze")] [TestCase("slate_teal")] [TestCase("lavender_peach")] [TestCase("slate_crimson")]
        public void GreyAxis_MapsDarkToLightAlongTheRamp(string id)
        {
            var p = PixelTestPaths.Profile(id); var input = PixelTestPaths.LutInput(p); input.Size = 32;
            byte[] lut = PaletteLutBaker.Bake(input); var lab = ColourMath.ToLab(p.Colours); var ramp = ColourMath.ToLab(p.RampUniform);
            double previous = -1;
            for (int i = 0; i < 32; i++)
            {
                double v = (i + 0.5) / 32;
                PaletteLutBaker.Lookup(lut, 32, v, v, v, out int a, out int b, out double t);
                double mixL = lab[a].X * (1 - t) + lab[b].X * t;
                Assert.GreaterOrEqual(mixL, previous - 1.5, id + ": lightness along the grey axis at cell " + i);
                previous = Math.Max(previous, mixL);
            }
            PaletteLutBaker.Lookup(lut, 32, 0, 0, 0, out int a0, out _, out double t0);
            Assert.Less(Math.Abs(lab[a0].X - ramp[0].X), 6.0, id + ": black lands at the dark end of the ramp"); Assert.Less(t0, 0.5);
            PaletteLutBaker.Lookup(lut, 32, 1, 1, 1, out int a1, out int b1, out double t1);
            double end = lab[a1].X * (1 - t1) + lab[b1].X * t1;
            Assert.Less(Math.Abs(end - ramp[ramp.Length - 1].X), 6.0, id + ": white lands at the light end of the ramp");
        }

        [Test]
        public void SaturatedSceneColour_MapsToTheAccent_AndNeutralNeverDoes()
        {
            var p = PixelTestPaths.Profile("slate_crimson"); Assert.GreaterOrEqual(p.AccentIndex, 0);
            var input = PixelTestPaths.LutInput(p); byte[] lut = PaletteLutBaker.Bake(input);
            PaletteLutBaker.Lookup(lut, 16, 1.0, 0.0, 0.0, out int a, out int b, out double t);
            Assert.AreEqual(p.AccentIndex, PaletteLutBaker.Select(a, b, t, 0.5, 1.0));
            for (int i = 0; i < 16; i++)
            {
                double v = (i + 0.5) / 16;
                PaletteLutBaker.Lookup(lut, 16, v, v, v, out int ga, out int gb, out _);
                Assert.AreNotEqual(p.AccentIndex, ga); Assert.AreNotEqual(p.AccentIndex, gb);
            }
        }

        [Test]
        public void ChangingOneColour_ChangesTheLut()
        {
            var p = PixelTestPaths.Profile("slate_teal");
            byte[] before = PaletteLutBaker.Bake(PixelTestPaths.LutInput(p));
            var edited = new List<Rgb8>(p.Colours); edited[0] = new Rgb8(250, 250, 250);   // the darkest ramp colour becomes near-white
            var input = PixelTestPaths.LutInput(p); input.Colours = edited;
            CollectionAssert.AreNotEqual(before, PaletteLutBaker.Bake(input));
        }

        [Test]
        public void Select_FollowsTheThresholdAndSpread()
        {
            Assert.AreEqual(2, PaletteLutBaker.Select(1, 2, 0.6, 0.5, 1.0));   // threshold 0.5 < 0.6 -> lighter colour
            Assert.AreEqual(1, PaletteLutBaker.Select(1, 2, 0.4, 0.5, 1.0));
            Assert.AreEqual(1, PaletteLutBaker.Select(1, 2, 0.3, 0.1, 0.25)); // spread 0.25: ratio 0.3 is shaped to 0 -> flat
            Assert.AreEqual(2, PaletteLutBaker.Select(1, 2, 0.3, 0.1, 1.0));  // full spread: threshold 0.1 < 0.3
            Assert.AreEqual(2, PaletteLutBaker.Select(1, 2, 0.6, 0.99, 0.0)); // no dither: nearest of the pair
        }

        [Test]
        public void Bake_RejectsBadInput()
        {
            Assert.Throws<ArgumentException>(() => PaletteLutBaker.Bake(new PaletteLutInput { Colours = new Rgb8[0], ValueRamp = new[] { new Rgb8(0, 0, 0), new Rgb8(9, 9, 9) } }));
            Assert.Throws<ArgumentException>(() => PaletteLutBaker.Bake(new PaletteLutInput { Colours = new[] { new Rgb8(0, 0, 0) }, ValueRamp = new[] { new Rgb8(0, 0, 0) } }));
        }
    }

    public class PaletteSetTests
    {
        [Test]
        public void RepoPaletteSet_HasTheFiveProfiles_WithCanonicalHashes()
        {
            var profiles = PixelTestPaths.Profiles();
            var expected = new Dictionary<string, string>
            {
                // sha256 of the compact JSON colour list, computed independently with Python hashlib from research/R-002/palettes.json.
                { "warm_rust_sepia", "a73d2e1944e65bd287783557faa2a96d7ce7d55cae449d8c1196078b89156db6" },
                { "warm_amber_haze", "957a794ccb3a5754c4d62b81e81b48903b10dcdb57584ce8479b17cc77607610" },
                { "slate_teal", "a35522b1467269ab253cdb6524f46ce9c006dd5e82bcbbe532cff482b208f719" },
                { "lavender_peach", "13c46b46796f2d36dcadd196ec337ea7c6b9f2dc1167a0527bf4bb7afa0bd29e" },
                { "slate_crimson", "409f3e1df17ec3a11aa5b27a957db31bcd87cc4b7d8fa3afcfc1dea51cee7dc9" },
            };
            Assert.AreEqual(5, profiles.Count);
            foreach (var p in profiles)
            {
                Assert.IsTrue(expected.ContainsKey(p.Id), p.Id);
                Assert.AreEqual(expected[p.Id], p.Sha256, p.Id);
                Assert.AreEqual(8, p.RampUniform.Count, p.Id);
                Assert.LessOrEqual(p.Colours.Count, 24, p.Id);
                Assert.LessOrEqual(PaletteSetReader.RampStepCv(p.RampUniform), 0.35, p.Id + ": M-012 ramp uniformity");
                if (p.AccentIndex >= 0)
                    foreach (var r in p.RampUniform) Assert.AreNotEqual(p.Colours[p.AccentIndex], r, p.Id + ": the accent is not part of the haze ramp");
            }
            Assert.AreEqual(-1, PixelTestPaths.Profile("slate_teal").AccentIndex);
        }

        [Test]
        public void ConfigCopy_IsTheResearchFileVerbatim()
        {
            string research = Path.Combine(PixelTestPaths.ProjectRoot(), "research", "R-002", "palettes.json");
            if (!File.Exists(research)) Assert.Ignore("research file not present in this checkout");
            CollectionAssert.AreEqual(File.ReadAllBytes(research), File.ReadAllBytes(PixelTestPaths.PaletteSetPath()));
        }

        [Test]
        public void Reader_RejectsWrongSchema_AndUnknownRoles()
        {
            Assert.Throws<FormatException>(() => PaletteSetReader.Read("{\"schema\":\"palette_set@v2\",\"profiles\":{}}"));
            Assert.Throws<FormatException>(() => PaletteSetReader.Read("{\"schema\":\"palette_set@v1\",\"profiles\":{\"x\":{\"colours\":[{\"role\":\"glow\",\"hex\":\"#000000\"}]}}}"));
        }

        [Test]
        public void MiniJson_ParsesNestedValues_AndRejectsTrailingText()
        {
            var o = (MiniJson.Obj)MiniJson.Parse("{ \"a\": [1, 2.5, -3e2, \"x\\n\", true, null], \"b\": {\"c\": false} }");
            var a = o.Array("a");
            Assert.AreEqual(1.0, a[0]); Assert.AreEqual(2.5, a[1]); Assert.AreEqual(-300.0, a[2]); Assert.AreEqual("x\n", a[3]); Assert.AreEqual(true, a[4]); Assert.IsNull(a[5]);
            Assert.AreEqual(false, o.Object("b")["c"]);
            CollectionAssert.AreEqual(new[] { "a", "b" }, o.Order);
            Assert.Throws<FormatException>(() => MiniJson.Parse("{} x"));
            Assert.Throws<FormatException>(() => MiniJson.Parse("{\"a\":1,\"a\":2}"));
        }
    }

    public class HazeAndStageTests
    {
        [Test]
        public void HazeBands_FollowDistanceMonotonically_OutTo30Km()
        {
            const double start = 60, full = 20000;
            int previous = 0;
            for (double d = 10; d <= 30000; d *= 1.2)
            {
                int band = HazeMath.Band(d, start, full, 8);
                Assert.GreaterOrEqual(band, previous); previous = band;
            }
            Assert.AreEqual(0, HazeMath.Band(50, start, full, 8), "nothing nearer than the start distance is hazed");
            Assert.AreEqual(7, HazeMath.Band(30000, start, full, 8), "30 km is in the last band (pure airlight)");
            Assert.AreEqual(1.0, HazeMath.Amount(7, 8), 1e-12); Assert.AreEqual(0.0, HazeMath.Amount(0, 8), 1e-12);
            int b150 = HazeMath.Band(150, start, full, 8), b1 = HazeMath.Band(1000, start, full, 8), b3 = HazeMath.Band(3000, start, full, 8), b10 = HazeMath.Band(10000, start, full, 8), b30 = HazeMath.Band(30000, start, full, 8);
            Assert.IsTrue(b150 < b1 && b1 < b3 && b3 < b10 && b10 < b30, "150 m, 1, 3, 10 and 30 km fall in successive bands");
        }

        [Test]
        public void HazeCurveGamma_OneIsTheLogCurve_BelowOneLiftsNearLevels()
        {
            for (double d = 70; d <= 20000; d *= 1.5)
            {
                double t = Math.Log(d / 60) / Math.Log(20000.0 / 60);
                Assert.AreEqual(7 * t, HazeMath.Level(d, 60, 20000, 8), 1e-12);
                Assert.AreEqual(HazeMath.Level(d, 60, 20000, 8), HazeMath.Level(d, 60, 20000, 8, 1.0), 1e-12);
                Assert.Greater(HazeMath.Level(d, 60, 20000, 8, 0.7), HazeMath.Level(d, 60, 20000, 8, 1.0));
            }
            Assert.AreEqual(7.0, HazeMath.Level(20000, 60, 20000, 8, 0.7), 1e-9, "full distance still ends at the last band");
            Assert.AreEqual(0.0, HazeMath.Level(60, 60, 20000, 8, 0.7), 1e-12);
        }

        [Test]
        public void BandStart_IsTheInverseOfBand()
        {
            for (int b = 1; b < 8; b++)
            {
                double d = HazeMath.BandStart(b, 60, 20000, 8);
                Assert.AreEqual(b - 1, HazeMath.Band(d * 0.999, 60.0, 20000.0, 8)); Assert.AreEqual(b, HazeMath.Band(d * 1.001, 60.0, 20000.0, 8));
            }
            Assert.AreEqual(20000.0, HazeMath.BandStart(7, 60, 20000, 8), 1e-6);
        }

        [Test]
        public void HazeSeam_MixesOnlyTheTwoBandsAtABoundary_AndAFlatFaceGetsNoSeam()
        {
            for (double level = 0; level <= 7; level += 0.013)
            {
                int hard = HazeMath.Band(level, 8);
                Assert.AreEqual(hard, HazeMath.Band(level, 8, 0.0, 3, 0.7, 0.01), "no change of level along the surface: one band, whatever the threshold");
                foreach (double gradient in new[] { 0.01, 0.05, 0.3, 2.0 })
                {
                    int boundary = (int)Math.Floor(level + 0.5);
                    for (double t = 0.03; t < 1; t += 0.0625)
                    {
                        int b = HazeMath.Band(level, 8, gradient, 3, 0.7, t);
                        Assert.LessOrEqual(Math.Abs(b - hard), 1);
                        Assert.IsTrue(b == Math.Min(7, boundary) || b == Math.Max(0, boundary - 1) || b == hard, "level " + level + ": only the two bands that meet at the nearest boundary");
                    }
                }
            }
            // Three texels to each side: with a gradient of 0.05 bands per texel the seam is 0.15 bands to each side.
            Assert.AreEqual(2, HazeMath.Band(2.84, 8, 0.05, 3, 0.7, 0.99)); Assert.AreEqual(3, HazeMath.Band(2.9, 8, 0.05, 3, 0.7, 0.01));
            Assert.AreEqual(3, HazeMath.Band(3.16, 8, 0.05, 3, 0.7, 0.01)); Assert.AreEqual(2, HazeMath.Band(3.1, 8, 0.05, 3, 0.7, 0.99));
        }

        [Test]
        public void DefaultOrder_IsLegal()
        {
            Assert.IsNull(PixelStageOrder.Validate(PixelStageOrder.Default));
            Assert.IsNull(PixelStageOrder.Validate(new[] { PixelStageId.Downsample, PixelStageId.Dither, PixelStageId.Outline, PixelStageId.Haze, PixelStageId.Quantise, PixelStageId.Store, PixelStageId.Present }));
            Assert.IsNull(PixelStageOrder.Validate(new[] { PixelStageId.Downsample, PixelStageId.Outline, PixelStageId.Dither, PixelStageId.Haze, PixelStageId.Quantise, PixelStageId.Store, PixelStageId.Present }));
            Assert.IsNull(PixelStageOrder.Validate(new[] { PixelStageId.Quantise, PixelStageId.Store, PixelStageId.Present }), "optional stages may be absent");
        }

        [Test]
        public void IllegalOrders_AreRejectedWithAReason()
        {
            StringAssert.Contains("Haze must come before Quantise", PixelStageOrder.Validate(new[] { PixelStageId.Quantise, PixelStageId.Haze, PixelStageId.Store, PixelStageId.Present }));
            StringAssert.Contains("Present is the last stage", PixelStageOrder.Validate(new[] { PixelStageId.Haze, PixelStageId.Present, PixelStageId.Quantise, PixelStageId.Store }));
            StringAssert.Contains("Downsample must come before Dither", PixelStageOrder.Validate(new[] { PixelStageId.Dither, PixelStageId.Downsample, PixelStageId.Quantise, PixelStageId.Store, PixelStageId.Present }));
            StringAssert.Contains("Downsample must come before Quantise", PixelStageOrder.Validate(new[] { PixelStageId.Quantise, PixelStageId.Downsample, PixelStageId.Store, PixelStageId.Present }));
            StringAssert.Contains("Outline must come before Quantise", PixelStageOrder.Validate(new[] { PixelStageId.Quantise, PixelStageId.Outline, PixelStageId.Store, PixelStageId.Present }));
            StringAssert.Contains("Dither must come before Quantise", PixelStageOrder.Validate(new[] { PixelStageId.Quantise, PixelStageId.Dither, PixelStageId.Store, PixelStageId.Present }));
            StringAssert.Contains("must come before Store", PixelStageOrder.Validate(new[] { PixelStageId.Store, PixelStageId.Quantise, PixelStageId.Present }));
            StringAssert.Contains("listed twice", PixelStageOrder.Validate(new[] { PixelStageId.Haze, PixelStageId.Haze }));
            StringAssert.Contains("unknown stage", PixelStageOrder.Validate(new[] { (PixelStageId)42 }));
        }

        [Test]
        public void RunTimeCheck_BlocksAStageThatArrivesTooLate()
        {
            int executed = (1 << (int)PixelStageId.Quantise);
            Assert.IsFalse(PixelStageOrder.CanRunAfter(executed, PixelStageId.Haze, out string reason)); StringAssert.Contains("Haze must come before Quantise", reason);
            Assert.IsTrue(PixelStageOrder.CanRunAfter(executed, PixelStageId.Store, out _));
            Assert.IsFalse(PixelStageOrder.CanRunAfter(executed, PixelStageId.Quantise, out _), "a stage runs once per frame");
            Assert.IsTrue(PixelStageOrder.CanRunAfter(0, PixelStageId.Haze, out _));
        }

        [TestCase(1920, 1080, 4, 480, 270)] [TestCase(2560, 1440, 5, 512, 288)] [TestCase(3840, 2160, 6, 640, 360)]
        [TestCase(1366, 768, 4, 342, 192)] [TestCase(1280, 720, 4, 320, 180)]
        public void ScaleRule_GivesIntegerScale(int w, int h, int expectedScale, int expectedW, int expectedH)
        {
            int k = PixelScaleRule.ChooseScale(h, 270, 4, 6);
            PixelScaleRule.InternalSize(w, h, k, out int iw, out int ih);
            Assert.AreEqual(expectedScale, k); Assert.AreEqual(expectedW, iw); Assert.AreEqual(expectedH, ih);
            Assert.GreaterOrEqual(iw * k, w); Assert.Less(iw * k - w, k, "the internal image covers the output with less than one texel to spare");
        }

        [Test]
        public void EveryStageWithAFlag_HasADistinctFlagName()
        {
            var seen = new HashSet<string>();
            foreach (string f in PixelFlags.All) { Assert.IsTrue(f.StartsWith("rendering.pixel.", StringComparison.Ordinal)); Assert.IsTrue(seen.Add(f)); }
            foreach (PixelStageId s in Enum.GetValues(typeof(PixelStageId)))
                if (PixelFlags.ForStage(s) != null) Assert.Contains(PixelFlags.ForStage(s), PixelFlags.All);
        }
    }
}
