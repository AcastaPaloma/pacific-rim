using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Rendering.Pixel;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
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
                    long allocM0 = CountAllocations(s, configs[0], 600).Samples, allocWorld = CountAllocations(s, configs[1], 600).Samples, allocStoreOnly = CountAllocations(s, configs[5], 600).Samples;
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
                    summary.Append("},\n   \"gc_alloc_samples_per_600_frames\": {\"m0_world_and_present\": ").Append(allocM0).Append(", \"m0_world\": ").Append(allocWorld).Append(", \"world_store_only\": ").Append(allocStoreOnly).Append("}}");
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

        struct AllocResult { public long Samples; public int Frames; public int GcCollections; public long MonoUsedDeltaBytes; }

        /// <summary>
        /// Capture off: only the render call runs (no readback, no file writes, no recording). Counts GC.Alloc samples
        /// around each render call, plus GC collections (all generations) and the Mono heap delta over the whole run.
        /// </summary>
        static AllocResult CountAllocations(StyleCaptureSession s, Config c, int frames)
        {
            s.Flags = c.Flags; s.Capture.Binder.Configure(s.Settings, c.Flags);
            for (int i = 0; i < 30; i++) { s.Rig.Tick(StyleCaptureSession.Dt); s.Capture.Render(c.Present); }
            const ProfilerRecorderOptions options = ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
            var r = new AllocResult { Frames = frames };
            int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
            long heap0 = Profiler.GetMonoUsedSizeLong();
            for (int i = 0; i < frames; i++)
            {
                s.Rig.Tick(StyleCaptureSession.Dt);
                using (var recorder = new ProfilerRecorder("GC.Alloc", 1, options))
                {
                    if (!recorder.Valid) { r.Samples = -1; return r; }
                    recorder.Start();
                    s.Capture.Render(c.Present);
                    recorder.Stop();
                    r.Samples += recorder.Count == 1 ? recorder.GetSample(0).Count : 0;
                }
            }
            r.GcCollections = (GC.CollectionCount(0) - gc0) + (GC.CollectionCount(1) - gc1) + (GC.CollectionCount(2) - gc2);
            r.MonoUsedDeltaBytes = Profiler.GetMonoUsedSizeLong() - heap0;
            return r;
        }

        // ------------------------------------------------------------------------------------------------------
        // Per-pass timing (DEF-T004-perf-1..4). One editor frame per EditorApplication.update tick, because Profiler
        // recorders only roll over at frame boundaries; the per-frame values are read one tick after the render.
        // ------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Run: unity run &lt;project&gt; -- -executeMethod Game.Tools.StyleCapture.StyleTiming.PassTimingCli [-timingOut evidence/T-004/timing] [-timingFrames 1000] [-timingWarmup 120] [-gcFrames 600]
        /// (Do not pass -quit: the run is asynchronous over editor frames and exits itself via EditorApplication.Exit; exit code 1 on failure.)
        /// Needs a graphics device, so not -nographics. Writes pass_timing_1920x1080.csv, pass_timing_2560x1440.csv and pass_timing_summary.json.
        /// </summary>
        public static void PassTimingCli()
        {
            string outDir = StyleCaptureCli.Arg("-timingOut", "evidence/T-004/timing");
            int frames = int.Parse(StyleCaptureCli.Arg("-timingFrames", "1000"), CultureInfo.InvariantCulture);
            int warmup = int.Parse(StyleCaptureCli.Arg("-timingWarmup", "120"), CultureInfo.InvariantCulture);
            int gcFrames = int.Parse(StyleCaptureCli.Arg("-gcFrames", "600"), CultureInfo.InvariantCulture);
            RunPassTiming(outDir, frames, warmup, gcFrames, exitWhenDone: true, onDone: null);
        }

        /// <summary>Starts the asynchronous pass timing. Callers (a UnityTest, say) pass a short frame count and an onDone callback (null on success, else the exception).</summary>
        public static void RunPassTiming(string outDir, int frames = 1000, int warmup = 120, int gcFrames = 600, bool exitWhenDone = false, Action<Exception> onDone = null)
        {
            var routine = PassTimingRoutine(outDir, frames, warmup, gcFrames);
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                Exception failure = null; bool more = false;
                try { more = routine.MoveNext(); }
                catch (Exception e) { failure = e; Debug.LogException(e); }
                if (more && failure == null) return;
                EditorApplication.update -= tick;
                try { (routine as IDisposable)?.Dispose(); } catch (Exception e) { if (failure == null) failure = e; }
                onDone?.Invoke(failure);
                if (exitWhenDone) EditorApplication.Exit(failure == null ? 0 : 1);
            };
            EditorApplication.update += tick;
        }

        sealed class PassSeries { public string Name; public Recorder Recorder; public readonly List<double> GpuMs = new List<double>(); public readonly List<double> CpuMs = new List<double>(); }

        static IEnumerator PassTimingRoutine(string outDir, int frames, int warmup, int gcFrames)
        {
            const int Settle = 3;   // frames rendered before recorders are enabled, so every pass sampler exists
            Directory.CreateDirectory(outDir);
            var summary = new StringBuilder();
            summary.Append("{\n \"tool\": \"Game.Tools.StyleCapture.StyleTiming.PassTimingCli\",\n");
            summary.Append(" \"method\": \"Recorder (enableRecording) on each PixelProfiling sampler (Pixel.*, including Pixel.Present); one editor frame per tick; per-frame gpuElapsedNanoseconds and elapsedNanoseconds (CPU) read one tick after the render; capture off, no readback\",\n");
            summary.Append(" \"hardware\": {\"gpu\": ").Append(StyleCaptureSession.Str(SystemInfo.graphicsDeviceName)).Append(", \"graphics_api\": ").Append(StyleCaptureSession.Str(SystemInfo.graphicsDeviceType.ToString()));
            summary.Append(", \"cpu\": ").Append(StyleCaptureSession.Str(SystemInfo.processorType)).Append(", \"os\": ").Append(StyleCaptureSession.Str(SystemInfo.operatingSystem)).Append(", \"unity\": ").Append(StyleCaptureSession.Str(Application.unityVersion)).Append(", \"batch_mode\": ").Append(Application.isBatchMode ? "true" : "false").Append("},\n");
            summary.Append(" \"supports_gpu_recorder\": ").Append(SystemInfo.supportsGpuRecorder ? "true" : "false");
            summary.Append(",\n \"vsync_count\": ").Append(QualitySettings.vSyncCount).Append(", \"target_frame_rate\": ").Append(Application.targetFrameRate);
            summary.Append(",\n \"warmup_frames_discarded\": ").Append(warmup + Settle).Append(", \"frames_measured\": ").Append(frames);
            summary.Append(",\n \"note\": \"Editor frames on the development Mac: relative regression numbers, not M-008 evidence (Q-019). GPU ms is 0 when the GPU recorder is unsupported; then only CPU ms is meaningful. Confidence interval is a normal approximation 1.96*stdev/sqrt(n) of the mean and ignores frame-to-frame autocorrelation.\",\n \"resolutions\": [");

            var session = new StyleCaptureSession(StyleTestScenes.StylePath, outDir);
            bool first = true;
            var gcSummary = new StringBuilder();
            try
            {
                session.SetProfile(StyleCaptureJobs.MainProfile);
                var m0 = session.Flags;
                var pose = StyleCaptureJobs.Eye70.Pose();
                foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(2560, 1440) })
                {
                    session.Capture.SetOutputSize(size.x, size.y);
                    session.Flags = m0; session.Capture.Binder.Configure(session.Settings, m0);
                    session.Rig.Cut();
                    var series = new List<PassSeries>();
                    int total = Settle + warmup + frames + 1;
                    for (int step = 0; step < total; step++)
                    {
                        if (step == Settle)
                        {
                            foreach (var kv in PixelProfiling.All)
                            {
                                var rec = kv.Value.GetRecorder();
                                if (rec == null || !rec.isValid) continue;
                                rec.enableRecording = true;
                                series.Add(new PassSeries { Name = kv.Key, Recorder = rec });
                            }
                            series.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                        }
                        // The previous tick's frame is complete now: read it (skipping settle + warm-up).
                        if (step - 1 >= Settle + warmup && step - 1 < Settle + warmup + frames)
                            foreach (var p in series) { p.GpuMs.Add(p.Recorder.gpuElapsedNanoseconds * 1e-6); p.CpuMs.Add(p.Recorder.elapsedNanoseconds * 1e-6); }
                        if (step < total - 1)
                        {
                            pose.YawDeg = StyleCaptureJobs.Eye70.YawDeg + step * 0.05f;   // slow pan: no frame is a cached repeat
                            session.Rig.SetScriptedPose(pose, StyleCaptureJobs.ReferenceDepth); session.Rig.Tick(StyleCaptureSession.Dt);
                            session.Capture.Render(true);
                        }
                        yield return null;
                    }
                    foreach (var p in series) p.Recorder.enableRecording = false;

                    string tag = size.x + "x" + size.y;
                    var csv = new StringBuilder("frame");
                    foreach (var p in series) csv.Append(',').Append(p.Name).Append("_gpu_ms,").Append(p.Name).Append("_cpu_ms");
                    csv.Append('\n');
                    for (int i = 0; i < frames; i++)
                    {
                        csv.Append(i);
                        foreach (var p in series) csv.Append(',').Append(F(p.GpuMs[i])).Append(',').Append(F(p.CpuMs[i]));
                        csv.Append('\n');
                    }
                    File.WriteAllText(Path.Combine(outDir, "pass_timing_" + tag + ".csv"), csv.ToString());

                    var layout = session.Capture.Binder.Layout;
                    summary.Append(first ? "\n" : ",\n").Append("  {\"output\": \"").Append(tag).Append("\", \"internal\": \"").Append(layout.Width).Append('x').Append(layout.Height).Append("\", \"csv\": \"pass_timing_").Append(tag).Append(".csv\", \"frames\": ").Append(frames).Append(",\n   \"passes\": {");
                    for (int i = 0; i < series.Count; i++)
                        summary.Append(i > 0 ? ",\n    " : "\n    ").Append('"').Append(series[i].Name).Append("\": {\"gpu_ms\": ").Append(StatsCi(series[i].GpuMs)).Append(", \"cpu_ms\": ").Append(StatsCi(series[i].CpuMs)).Append('}');
                    summary.Append("\n   }}");
                    first = false;

                    // Capture off, nothing recording: managed allocation witness over gcFrames frames at this resolution.
                    var a = CountAllocations(session, new Config { Name = "m0_world_and_present", Flags = m0, Present = true }, gcFrames);
                    gcSummary.Append(gcSummary.Length > 0 ? ", " : "").Append('"').Append(tag).Append("\": {\"frames\": ").Append(a.Frames).Append(", \"gc_alloc_samples\": ").Append(a.Samples).Append(", \"gc_collections\": ").Append(a.GcCollections).Append(", \"mono_used_delta_bytes\": ").Append(a.MonoUsedDeltaBytes).Append('}');
                }
                session.Flags = m0; session.Apply();
            }
            finally { session.Dispose(); }
            summary.Append("\n ],\n \"gc_capture_off\": {").Append(gcSummary).Append("}\n}\n");
            File.WriteAllText(Path.Combine(outDir, "pass_timing_summary.json"), summary.ToString());
            Debug.Log("[StyleCapture] pass timing written to " + outDir);
        }

        static string StatsCi(List<double> values)
        {
            int n = values.Count;
            if (n == 0) return "null";
            var sorted = new List<double>(values); sorted.Sort();
            double mean = 0; foreach (double v in values) mean += v; mean /= n;
            double ss = 0; foreach (double v in values) ss += (v - mean) * (v - mean);
            double sd = n > 1 ? Math.Sqrt(ss / (n - 1)) : 0, half = 1.96 * sd / Math.Sqrt(n);
            double p95 = sorted[Math.Min(n - 1, (int)Math.Floor(0.95 * (n - 1) + 0.5))];
            return "{\"n\": " + n + ", \"mean\": " + F(mean) + ", \"stdev\": " + F(sd) + ", \"p95\": " + F(p95) + ", \"max\": " + F(sorted[n - 1]) + ", \"ci95_lo\": " + F(mean - half) + ", \"ci95_hi\": " + F(mean + half) + "}";
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
