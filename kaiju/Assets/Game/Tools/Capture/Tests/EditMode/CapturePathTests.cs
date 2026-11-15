using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Game.Tools.Capture.Tests
{
    public class CapturePathTests
    {
        internal const string Good = @"{ ""schema"": ""capture_path@v1"", ""path_id"": ""t_good"", ""pillar"": ""P4"", ""viewpoint"": {""id"":""V5"",""tile"":""L0_30_36"",""baytm"":[61591.4,75450.7],""heading_deg"":170},
          ""profile_id"": ""lavender_peach"", ""district_id"": ""SF-Test"", ""time_of_day"": ""fixed_v1"", ""fps"": 60, ""duration_s"": 1.0, ""seed"": 7, ""interpolation"": ""linear"",
          ""keyframes"": [ {""t_s"":0.0,""pos_baytm"":[0,0],""alt_navd88"":146,""yaw_deg"":350,""pitch_deg"":-2}, {""t_s"":1.0,""pos_baytm"":[60,0],""alt_navd88"":146,""yaw_deg"":10,""pitch_deg"":-2} ],
          ""shake"": [ {""t_s"":0.5,""amp_internal_px"":2.0,""decay_s"":0.4} ], ""snap"": {""enabled"":true,""dither_anchor"":""direction""},
          ""outputs"": [ {""internal_wh"":[480,270],""output_wh"":[1920,1080],""scale"":4} ], ""mode"": ""clip"", ""requires"": [] }";

        static string Edit(string from, string to) { Assert.That(Good, Does.Contain(from)); return Good.Replace(from, to); }

        [Test] public void GoodPathLintsClean()
        {
            var p = CapturePath.Parse(Good);
            Assert.That(p.Lint(), Is.Empty);
            Assert.AreEqual(60, p.FrameCount); Assert.IsTrue(p.AltIsNavd88);
        }

        [TestCase("\"fps\": 60", "\"fps\": 50", "L03")]
        [TestCase("\"duration_s\": 1.0", "\"duration_s\": 1.003", "L04")]
        [TestCase("\"dither_anchor\":\"direction\"", "\"dither_anchor\":\"screen\"", "L07")]
        [TestCase("\"enabled\":true", "\"enabled\":false", "L07")]
        [TestCase("\"output_wh\":[1920,1080]", "\"output_wh\":[1900,1080]", "L08")]
        [TestCase("\"profile_id\": \"lavender_peach\"", "\"profile_id\": \"nope\"", "L15")]
        [TestCase("\"schema\": \"capture_path@v1\"", "\"schema\": \"capture_path@v2\"", "L01")]
        [TestCase("\"path_id\": \"t_good\"", "\"path_id\": \"Bad Id\"", "L02")]
        [TestCase("\"requires\": []", "\"requires\": [\"warp_drive\"]", "L11")]
        [TestCase("\"tile\":\"L0_30_36\"", "\"tile\":\"tile9\"", "L14")]
        [TestCase("\"alt_navd88\":146,\"yaw_deg\":350", "\"alt_navd88\":146,\"alt_m\":2,\"yaw_deg\":350", "L10")]
        public void BrokenRuleIsReported(string from, string to, string rule)
        {
            var errs = CapturePath.Parse(Edit(from, to)).Lint();
            Assert.That(errs.Any(e => e.StartsWith(rule, StringComparison.Ordinal)), "expected " + rule + " in: " + string.Join(" | ", errs));
        }

        [Test] public void UnsortedKeyframesAndWrongEndsAreReported()
        {
            Assert.That(CapturePath.Parse(Edit("\"t_s\":1.0,\"pos_baytm\"", "\"t_s\":0.0,\"pos_baytm\"")).Lint().Any(e => e.StartsWith("L05")));
            Assert.That(CapturePath.Parse(Edit("\"t_s\":1.0,\"pos_baytm\"", "\"t_s\":0.9,\"pos_baytm\"")).Lint().Any(e => e.StartsWith("L05")));
        }

        [Test] public void CatmullRomNeedsFourKeyframes() =>
            Assert.That(CapturePath.Parse(Edit("\"linear\"", "\"catmull_rom\"")).Lint().Any(e => e.StartsWith("L05")));

        [Test] public void ClipAt4kAndStillWithoutFramesAreRejected()
        {
            var uhd = Edit("\"internal_wh\":[480,270],\"output_wh\":[1920,1080],\"scale\":4", "\"internal_wh\":[480,270],\"output_wh\":[3840,2160],\"scale\":8");
            Assert.That(CapturePath.Parse(uhd).Lint().Any(e => e.StartsWith("L08")));
            Assert.That(CapturePath.Parse(Edit("\"clip\"", "\"still\"")).Lint().Any(e => e.StartsWith("L09")));
            Assert.That(CapturePath.Parse(Edit("\"mode\": \"clip\"", "\"mode\": \"still\", \"still_frames\": [0, 30]")).Lint(), Is.Empty);
        }

        [Test] public void BlockedPathNamesItsFirstMissingTag()
        {
            var p = CapturePath.Parse(Edit("\"requires\": []", "\"requires\": [\"kaiju\", \"walk\"]"));
            Assert.AreEqual("kaiju", p.FirstBlocker(null));
            Assert.IsNull(CapturePath.Parse(Good).FirstBlocker(null));
        }

        [Test] public void ShippedPathFilesLintClean()
        {
            string dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "store", "paths"));
            if (!Directory.Exists(dir)) dir = Path.GetFullPath("docs/store/paths");
            Assume.That(Directory.Exists(dir), "docs/store/paths not found from " + AppContext.BaseDirectory);
            foreach (var f in Directory.GetFiles(dir, "*.json")) Assert.That(CapturePath.Parse(File.ReadAllText(f)).Lint(), Is.Empty, f);
        }
    }

    public class CapturePathSamplerTests
    {
        static CapturePath Make(string interp, string extra = "")
        {
            string kf = interp == "catmull_rom"
                ? @"{""t_s"":0,""pos_baytm"":[0,0],""alt_m"":2,""yaw_deg"":350,""pitch_deg"":0},{""t_s"":1,""pos_baytm"":[10,0],""alt_m"":2,""yaw_deg"":10,""pitch_deg"":-4},{""t_s"":2,""pos_baytm"":[20,10],""alt_m"":2,""yaw_deg"":30,""pitch_deg"":-4},{""t_s"":3,""pos_baytm"":[30,10],""alt_m"":2,""yaw_deg"":50,""pitch_deg"":0}"
                : @"{""t_s"":0,""pos_baytm"":[0,0],""alt_m"":2,""yaw_deg"":350,""pitch_deg"":0},{""t_s"":3,""pos_baytm"":[30,0],""alt_m"":8,""yaw_deg"":10,""pitch_deg"":-6}";
            return CapturePath.Parse(@"{""schema"":""capture_path@v1"",""path_id"":""s"",""pillar"":""P3"",""viewpoint"":{""id"":""V3"",""tile"":""L0_26_38"",""baytm"":[0,0],""heading_deg"":0},""profile_id"":""slate_teal"",""district_id"":""d"",""time_of_day"":""fixed_v1"",""fps"":60,""duration_s"":3,""seed"":5,""interpolation"":""" + interp + @""",""keyframes"":[" + kf + @"],""shake"":[" + extra + @"],""snap"":{""enabled"":true,""dither_anchor"":""direction""},""outputs"":[{""internal_wh"":[480,270],""output_wh"":[1920,1080],""scale"":4}],""mode"":""clip"",""requires"":[]}");
        }

        [Test] public void LinearEndsAndMidpoint()
        {
            var p = Make("linear");
            Assert.That(p.Lint(), Is.Empty);
            var a = CapturePathSampler.Sample(p, 0); var m = CapturePathSampler.Sample(p, 90); var z = CapturePathSampler.Sample(p, 180);
            Assert.AreEqual(0, a.E, 1e-9); Assert.AreEqual(15, m.E, 1e-9); Assert.AreEqual(30, z.E, 1e-9);
            Assert.AreEqual(5, m.Alt, 1e-9); Assert.AreEqual(-3, m.PitchDeg, 1e-9);
        }

        [Test] public void YawTakesTheShortestArc()
        {
            var p = Make("linear");   // 350 -> 10 passes through 0, not through 180
            Assert.AreEqual(360.0, CapturePathSampler.Sample(p, 90).YawDeg, 1e-9);
            Assert.AreEqual(20.0, CapturePathSampler.ShortestArc(350, 10), 1e-9);
            Assert.AreEqual(-20.0, CapturePathSampler.ShortestArc(10, 350), 1e-9);
        }

        [Test] public void CatmullRomHitsEveryKeyframe()
        {
            var p = Make("catmull_rom");
            Assert.That(p.Lint(), Is.Empty);
            Assert.AreEqual(10, CapturePathSampler.Sample(p, 60).E, 1e-9); Assert.AreEqual(20, CapturePathSampler.Sample(p, 120).E, 1e-9); Assert.AreEqual(30, CapturePathSampler.Sample(p, 180).E, 1e-9);
            Assert.That(CapturePathSampler.Sample(p, 150).N, Is.InRange(10.0, 11.0));   // Catmull-Rom overshoots slightly between equal keyframes
        }

        [Test] public void EaseInOutStartsSlowAndMatchesAtEnds()
        {
            var p = Make("ease_in_out");
            Assert.Less(CapturePathSampler.Sample(p, 9).E, 30.0 * 9 / 180);   // slower than linear early
            Assert.AreEqual(30, CapturePathSampler.Sample(p, 180).E, 1e-9);
        }

        [Test] public void ShakeIsDeterministicAndDecays()
        {
            var p = Make("linear", @"{""t_s"":1,""amp_internal_px"":3,""decay_s"":0.25}");
            Assert.AreEqual(0.0, CapturePathSampler.Sample(p, 59).ShakeXPx);
            var a = CapturePathSampler.Sample(p, 60); var b = CapturePathSampler.Sample(p, 60);
            Assert.AreEqual(a.ShakeXPx, b.ShakeXPx); Assert.AreEqual(a.ShakeYPx, b.ShakeYPx);
            Assert.LessOrEqual(Math.Abs(a.ShakeXPx), 3.0);
            double late = Math.Abs(CapturePathSampler.Sample(p, 175).ShakeXPx);
            Assert.Less(late, 3.0 * Math.Exp(-(175 / 60.0 - 1) / 0.25) + 1e-9);
        }

        [Test] public void NoiseIsInRangeAndOrderIndependent()
        {
            double first = CapturePathSampler.Noise(9, 12, 0);
            for (int i = 0; i < 200; i++) { double v = CapturePathSampler.Noise(9, i, i & 1); Assert.That(v, Is.InRange(-1.0, 1.0)); }
            Assert.AreEqual(first, CapturePathSampler.Noise(9, 12, 0));
            Assert.AreNotEqual(CapturePathSampler.Noise(9, 12, 0), CapturePathSampler.Noise(9, 12, 1));
        }
    }

    public class CaptureMotionSetTests
    {
        // ---- M-004 motion sets (T-191) ----
        const string MotionSetJson = "\"purpose\": \"M-004\", \"motion_set\": {\"classes\": [\"S0\",\"T1\",\"T2\",\"T3\",\"Y1\",\"Y2\"]}, ";
        static string Motion(string duration = "3.0") => CapturePathTests.Good.Replace("\"fps\": 60,", MotionSetJson + "\"fps\": 60,").Replace("\"duration_s\": 1.0", "\"duration_s\": " + duration)
            .Replace("\"t_s\":1.0,\"pos_baytm\"", "\"t_s\":" + duration + ",\"pos_baytm\"").Replace("\"shake\": [ {\"t_s\":0.5,\"amp_internal_px\":2.0,\"decay_s\":0.4} ],", "\"shake\": [],");

        [Test] public void MotionSetLintsCleanAndParses()
        {
            var p = CapturePath.Parse(Motion());
            Assert.That(p.Lint(), Is.Empty);
            Assert.IsTrue(p.IsMotionSet); Assert.AreEqual(6, p.MotionSet.Count);
        }

        [Test] public void MotionClassTableMatchesTheRegistry()
        {
            Assert.IsTrue(CapturePath.TryMotionClass("T2", out var t2)); Assert.AreEqual(0.25, t2.SpeedPxPerFrame); Assert.AreEqual(180, t2.Frames);
            Assert.IsTrue(CapturePath.TryMotionClass("S0", out var s0)); Assert.AreEqual(0.0, s0.SpeedPxPerFrame); Assert.AreEqual(60, s0.Frames);
            Assert.IsTrue(CapturePath.TryMotionClass("Y2", out var y2)); Assert.AreEqual("yaw", y2.Type); Assert.AreEqual(1.0, y2.SpeedPxPerFrame);
            Assert.IsFalse(CapturePath.TryMotionClass("T9", out _));
        }

        [TestCase("\"classes\": [\"S0\",\"T1\",\"T2\",\"T3\",\"Y1\",\"Y2\"]", "\"classes\": [\"S0\",\"T9\"]")]
        [TestCase("\"classes\": [\"S0\",\"T1\",\"T2\",\"T3\",\"Y1\",\"Y2\"]", "\"classes\": []")]
        [TestCase("\"classes\": [\"S0\",\"T1\",\"T2\",\"T3\",\"Y1\",\"Y2\"]", "\"classes\": [\"S0\",\"S0\"]")]
        [TestCase("\"purpose\": \"M-004\"", "\"purpose\": \"M-005\"")]
        [TestCase("\"mode\": \"clip\"", "\"mode\": \"still\", \"still_frames\": [0]")]
        public void BrokenMotionSetIsReported(string from, string to)
        {
            string json = Motion(); Assert.That(json, Does.Contain(from));
            Assert.That(CapturePath.Parse(json.Replace(from, to)).Lint().Any(e => e.StartsWith("L17", StringComparison.Ordinal)));
        }

        [Test] public void MotionSetNeedsEnoughFramesAndPlainPathsRejectIt()
        {
            Assert.That(CapturePath.Parse(Motion("2.0")).Lint().Any(e => e.StartsWith("L17")));   // 120 frames < 180
            Assert.That(CapturePath.Parse(CapturePathTests.Good.Replace("\"fps\": 60,", "\"motion_set\": {\"classes\": [\"S0\"]}, \"fps\": 60,")).Lint().Any(e => e.StartsWith("L17")));
        }
    }
}
