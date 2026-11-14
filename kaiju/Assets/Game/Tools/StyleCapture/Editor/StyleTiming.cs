using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Rendering.Pixel;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Game.Tools.StyleCapture
{
    /// <summary>
    /// Frame timing of the pixel pipeline (reconciled spec C20) on the machine it runs on: a relative regression
    /// number, not M-008 evidence (that needs target-class Windows hardware).
    /// Method: every measured frame is rendered and then one pixel of the result is read back, which makes the
    /// GPU finish the frame, so the wall time of render + readback covers CPU submission and GPU execution. The
    /// configurations are interleaved frame by frame and the cost of a stage is the per-frame paired difference
    /// between the M0 configuration and the same frame with that stage off. Managed allocations are counted with the
    /// GC.Alloc profiler recorder around the render calls (the same witness Core's tests use).
    /// Run: unity run &lt;project&gt; -- -executeMethod Game.Tools.StyleCapture.StyleTiming.Run [-timingOut dir] [-timingFrames n]
    /// </summary>
    public static class StyleTiming
    {
        sealed class Config { public string Name; public PixelFlagState Flags; public bool Present; public readonly List<double> Ms = new List<double>(); }

        public static void Run()
        {
            string outDir = StyleCaptureCli.Arg("-timingOut", "evidence/T-004/timing");
            int frames = int.Parse(StyleCaptureCli.Arg("-timingFrames", "300"), CultureInfo.InvariantCulture);
            Directory.CreateDirectory(outDir);
            var summary = new StringBuilder();
            summary.Append("{\n \"tool\": \"Game.Tools.StyleCapture.StyleTiming.Run\",\n \"method\": \"wall time of render + one-pixel readback (GPU finished), configurations interleaved per frame, stage cost = paired difference to the M0 frame\",\n");
            summary.Append(" \"hardware\": {\"gpu\": ").Append(StyleCaptureSession.Str(SystemInfo.graphicsDeviceName)).Append(", \"graphics_api\": ").Append(StyleCaptureSession.Str(SystemInfo.graphicsDeviceType.ToString()));
            summary.Append(", \"cpu\": ").Append(StyleCaptureSession.Str(SystemInfo.processorType)).Append(", \"os\": ").Append(StyleCaptureSession.Str(SystemInfo.operatingSystem)).Append(", \"unity\": ").Append(StyleCaptureSession.Str(Application.unityVersion)).Append(", \"batch_mode\": ").Append(Application.isBatchMode ? "true" : "false").Append("},\n");
            summary.Append(" \"note\": \"Relative regression numbers on the development Mac. M-008 (60 fps on the target PC) is NOT verified by this: no target-class Windows hardware exists (Q-019).\",\n \"frames_per_configuration\": ").Append(frames).Append(",\n \"resolutions\": [");
            using (var s = new StyleCaptureSession(StyleTestScenes.StylePath, outDir))
            {
                s.SetProfile(StyleCaptureJobs.MainProfile);
                var m0 = s.Flags;
                bool firstResolution = true;
                foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(2560, 1440) })
                {
                    s.Capture.SetOutputSize(size.x, size.y);
                    var configs = new List<Config>
                    {
                        new Config { Name = "m0_world_and_present", Flags = m0, Present = true },
                        new Config { Name = "m0_world", Flags = m0, Present = false },
                        new Config { Name = "world_no_dither", Flags = m0.With(PixelFlags.Dither, false), Present = false },
                        new Config { Name = "world_no_haze", Flags = m0.With(PixelFlags.Haze, false), Present = false },
                        new Config { Name = "world_no_quantise", Flags = m0.With(PixelFlags.Quantise, false), Present = false },
                        new Config { Name = "world_store_only", Flags = m0.With(PixelFlags.Dither, false).With(PixelFlags.Haze, false).With(PixelFlags.Quantise, false), Present = false },
                    };
                    var probe = new Texture2D(1, 1, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None);
                    var watch = new System.Diagnostics.Stopwatch();
                    var pose = StyleCaptureJobs.Eye70.Pose();
                    s.Rig.Cut();
                    for (int i = -40; i < frames; i++)   // 40 warm-up frames
                    {
                        pose.YawDeg = StyleCaptureJobs.Eye70.YawDeg + i * 0.05f;   // a slow pan, so no frame is a cached repeat
                        s.Rig.SetScriptedPose(pose, StyleCaptureJobs.ReferenceDepth); s.Rig.Tick(StyleCaptureSession.Dt);
                        foreach (var c in configs)
                        {
                            s.Flags = c.Flags; s.Capture.Binder.Configure(s.Settings, c.Flags);
                            watch.Restart();
                            s.Capture.Render(c.Present);
                            Finish(c.Present ? s.Capture.OutputTarget : s.Capture.Binder.InternalColour, probe);
                            watch.Stop();
                            if (i >= 0) c.Ms.Add(watch.Elapsed.TotalMilliseconds);
                        }
                    }
                    // Allocations: count GC.Alloc samples over the render calls of the M0 configuration and of the plain target.
                    long allocM0 = CountAllocations(s, configs[0], 100), allocWorld = CountAllocations(s, configs[1], 100), allocStoreOnly = CountAllocations(s, configs[5], 100);
                    UnityEngine.Object.DestroyImmediate(probe);
                    s.Flags = m0; s.Apply();

                    var layout = s.Capture.Binder.Layout;
                    string tag = size.x + "x" + size.y;
                    var csv = new StringBuilder("frame");
                    foreach (var c in configs) csv.Append(',').Append(c.Name).Append("_ms");
                    csv.Append('\n');
                    for (int i = 0; i < frames; i++)
                    {
                        csv.Append(i);
                        foreach (var c in configs) csv.Append(',').Append(c.Ms[i].ToString("0.####", CultureInfo.InvariantCulture));
                        csv.Append('\n');
                    }
                    File.WriteAllText(Path.Combine(outDir, "frame_timing_" + tag + ".csv"), csv.ToString());

                    summary.Append(firstResolution ? "\n" : ",\n").Append("  {\"output\": \"").Append(tag).Append("\", \"internal\": \"").Append(layout.Width).Append('x').Append(layout.Height).Append("\", \"integer_scale\": ").Append(layout.Scale).Append(", \"csv\": \"frame_timing_").Append(tag).Append(".csv\",\n   \"frame_ms\": {");
                    for (int c = 0; c < configs.Count; c++) summary.Append(c > 0 ? ", " : "").Append('"').Append(configs[c].Name).Append("\": ").Append(Stats(configs[c].Ms));
                    summary.Append("},\n   \"paired_cost_ms\": {");
                    summary.Append("\"Pixel.Present (world+present minus world)\": ").Append(Stats(Diff(configs[0].Ms, configs[1].Ms)));
                    summary.Append(", \"Pixel.Dither\": ").Append(Stats(Diff(configs[1].Ms, configs[2].Ms)));
                    summary.Append(", \"Pixel.Haze\": ").Append(Stats(Diff(configs[1].Ms, configs[3].Ms)));
                    summary.Append(", \"Pixel.Quantise\": ").Append(Stats(Diff(configs[1].Ms, configs[4].Ms)));
                    summary.Append(", \"Pixel.Dither+Haze+Quantise (M0 world minus store only)\": ").Append(Stats(Diff(configs[1].Ms, configs[5].Ms)));
                    summary.Append("},\n   \"gc_alloc_samples_per_100_frames\": {\"m0_world_and_present\": ").Append(allocM0).Append(", \"m0_world\": ").Append(allocWorld).Append(", \"world_store_only\": ").Append(allocStoreOnly).Append("}}");
                    firstResolution = false;
                }
            }
            summary.Append("\n ]\n}\n");
            File.WriteAllText(Path.Combine(outDir, "timing_summary.json"), summary.ToString());
            Debug.Log("[StyleCapture] timing written to " + outDir);
        }

        static readonly Color32[] s_One = new Color32[1];

        /// <summary>Reads one pixel back: returns only when the GPU has executed everything queued for this target.</summary>
        static void Finish(RenderTexture target, Texture2D probe)
        {
            var previous = RenderTexture.active; RenderTexture.active = target;
            probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
            RenderTexture.active = previous;
        }

        static long CountAllocations(StyleCaptureSession s, Config c, int frames)
        {
            s.Flags = c.Flags; s.Capture.Binder.Configure(s.Settings, c.Flags);
            for (int i = 0; i < 10; i++) { s.Rig.Tick(StyleCaptureSession.Dt); s.Capture.Render(c.Present); }
            const ProfilerRecorderOptions options = ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
            long total = 0;
            for (int i = 0; i < frames; i++)
            {
                s.Rig.Tick(StyleCaptureSession.Dt);
                using (var recorder = new ProfilerRecorder("GC.Alloc", 1, options))
                {
                    if (!recorder.Valid) return -1;
                    recorder.Start();
                    s.Capture.Render(c.Present);
                    recorder.Stop();
                    total += recorder.Count == 1 ? recorder.GetSample(0).Count : 0;
                }
            }
            return total;
        }

        static List<double> Diff(List<double> a, List<double> b)
        {
            var d = new List<double>(a.Count);
            for (int i = 0; i < a.Count; i++) d.Add(a[i] - b[i]);
            return d;
        }

        static string Stats(List<double> values)
        {
            var sorted = new List<double>(values); sorted.Sort();
            double mean = 0; foreach (double v in sorted) mean += v; mean /= sorted.Count;
            double P(double q) => sorted[Math.Min(sorted.Count - 1, (int)Math.Floor(q * (sorted.Count - 1) + 0.5))];
            return "{\"mean\": " + F(mean) + ", \"p50\": " + F(P(0.5)) + ", \"p95\": " + F(P(0.95)) + ", \"max\": " + F(sorted[sorted.Count - 1]) + "}";
        }

        static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
