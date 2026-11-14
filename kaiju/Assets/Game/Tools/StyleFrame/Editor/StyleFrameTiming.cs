// T-006 step 5 (C15): frame cost of the composed style-frame scene (tile + mech + props + pixel pipeline), relative numbers on the
// development machine only. M-008 (60 fps on the target PC) is NOT verified by this.
// Method: the scene is rebuilt (StyleFrameScene.Build), the hero camera is placed on the texel grid, and the job self-ticks on
// EditorApplication.update (one editor frame per tick; do not pass -quit, it would kill a self-ticking job). Per configuration and
// resolution: 120 warm-up frames, then 600 measured frames. Per frame: render call wall time (CPU submission), render + one-pixel
// readback wall time (GPU finished), and the Render/Memory profiler counters of the previous editor frame (draw calls, batches,
// SetPass calls, GC allocated in frame). Managed allocations inside the render call are counted with the GC.Alloc recorder.
// Configurations: pipeline_full (M0 flags, present on) and baseline_pixel_off (dither, haze, quantise off, no present).
// Run: <Unity> -batchmode -projectPath <kaiju> -logFile <log> -executeMethod Game.Tools.StyleFrame.StyleFrameTiming.TimingCli
// Options: -styleFrameTimingOut <dir> (default evidence/T-006/timing), -styleFrameTimingFrames <n> (600), -styleFrameTimingWarmup <n> (120), -styleFrameProfile <id>.
// Exit 0 on success, 1 on any error or when the hard cap is hit.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Rendering.Pixel;
using Game.Tools.StyleCapture;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Profiling;

namespace Game.Tools.StyleFrame
{
    public static class StyleFrameTiming
    {
        const string DefaultOut = "evidence/T-006/timing";
        const int HardCapTicks = 12000;          // 4 runs x (3 settle + 120 + 600 + 2) = about 2900 expected
        const double HardCapSeconds = 1500;      // wall clock
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        sealed class Series
        {
            public string Config, Output, Internal;
            public readonly List<double> RenderMs = new List<double>(), WallMs = new List<double>();
            public readonly List<double> Draw = new List<double>(), Batches = new List<double>(), SetPass = new List<double>(), GcFrameBytes = new List<double>();
            public long GcAllocSamples, GcCollections, MonoDeltaBytes; public int Frames;
            public bool CountersValid;
        }

        public static void TimingCli()
        {
            string outDir = StyleCaptureCli.Arg("-styleFrameTimingOut", DefaultOut);
            int frames = int.Parse(StyleCaptureCli.Arg("-styleFrameTimingFrames", "600"), Inv);
            int warmup = int.Parse(StyleCaptureCli.Arg("-styleFrameTimingWarmup", "120"), Inv);
            string profile = StyleCaptureCli.Arg("-styleFrameProfile", StyleFrameScene.DefaultProfile);
            var routine = Routine(outDir, frames, warmup, profile);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int ticks = 0;
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                Exception failure = null; bool more = false;
                try
                {
                    ticks++;
                    if (ticks > HardCapTicks || clock.Elapsed.TotalSeconds > HardCapSeconds) throw new TimeoutException("hard cap hit: " + ticks + " ticks, " + clock.Elapsed.TotalSeconds.ToString("0") + " s");
                    more = routine.MoveNext();
                }
                catch (Exception e) { failure = e; Debug.LogError("[StyleFrameTiming] FAILED: " + e); }
                if (more && failure == null) return;
                EditorApplication.update -= tick;
                try { (routine as IDisposable)?.Dispose(); } catch (Exception e) { if (failure == null) { failure = e; Debug.LogError("[StyleFrameTiming] dispose FAILED: " + e); } }
                if (failure == null) Debug.Log("[StyleFrameTiming] OK: " + outDir);
                EditorApplication.Exit(failure == null ? 0 : 1);
            };
            EditorApplication.update += tick;
        }

        [MenuItem("Game/Tools/Time style frame")]
        public static void TimingMenu()
        {
            var routine = Routine(DefaultOut, 600, 120, StyleFrameScene.DefaultProfile);
            EditorApplication.CallbackFunction tick = null;
            tick = () => { bool more = false; try { more = routine.MoveNext(); } catch (Exception e) { Debug.LogException(e); } if (!more) EditorApplication.update -= tick; };
            EditorApplication.update += tick;
        }

        static IEnumerator Routine(string outDir, int frames, int warmup, string profile)
        {
            const int Settle = 3;
            Directory.CreateDirectory(outDir);
            StyleFrameScene.Build(profile);
            var plan = StyleFrameShots.LastPlan ?? throw new InvalidOperationException("no shot plan after the build");
            var hero = plan.Hero;
            yield return null;

            var all = new List<Series>();
            var probe = new Texture2D(1, 1, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None);
            var session = new StyleCaptureSession(StyleFrameScene.ScenePath, outDir);
            try
            {
                session.SetProfile(profile);
                var m0 = session.Flags;
                var off = m0.With(PixelFlags.Dither, false).With(PixelFlags.Haze, false).With(PixelFlags.Quantise, false);
                var runs = new[]
                {
                    new { Name = "pipeline_full", Flags = m0, Present = true, W = 1920, H = 1080 },
                    new { Name = "pipeline_full", Flags = m0, Present = true, W = 2560, H = 1440 },
                    new { Name = "baseline_pixel_off", Flags = off, Present = false, W = 1920, H = 1080 },
                    new { Name = "baseline_pixel_off", Flags = off, Present = false, W = 2560, H = 1440 },
                };
                var recDraw = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
                var recBatch = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
                var recSetPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
                var recGcFrame = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
                var watch = new System.Diagnostics.Stopwatch();
                try
                {
                    foreach (var run in runs)
                    {
                        session.Capture.SetOutputSize(run.W, run.H);
                        session.Flags = run.Flags; session.Capture.Binder.Configure(session.Settings, run.Flags);
                        session.Rig.Cut();
                        var layout = session.Capture.Binder.Layout;
                        var s = new Series { Config = run.Name, Output = run.W + "x" + run.H, Internal = layout.Width + "x" + layout.Height, Frames = frames, CountersValid = recDraw.Valid && recBatch.Valid && recSetPass.Valid && recGcFrame.Valid };
                        var pose = hero.Pose;
                        int total = Settle + warmup + frames + 1;
                        long gc0 = 0; int g0 = 0, g1 = 0, g2 = 0; long heap0 = 0;
                        for (int step = 0; step < total; step++)
                        {
                            // counters now hold the previous editor frame (complete): read them for the previous measured render
                            int prev = step - 1 - Settle - warmup;
                            if (prev >= 0 && prev < frames)
                            {
                                if (s.CountersValid)
                                {
                                    s.Draw.Add(recDraw.LastValue); s.Batches.Add(recBatch.LastValue); s.SetPass.Add(recSetPass.LastValue); s.GcFrameBytes.Add(recGcFrame.LastValue);
                                }
                            }
                            if (step < total - 1)
                            {
                                bool measured = step >= Settle + warmup;
                                if (step == Settle + warmup) { g0 = GC.CollectionCount(0); g1 = GC.CollectionCount(1); g2 = GC.CollectionCount(2); heap0 = Profiler.GetMonoUsedSizeLong(); }
                                pose.YawDeg = (float)hero.YawDeg + step * 0.01f;   // tiny pan so no frame is a cached repeat
                                session.Rig.SetScriptedPose(pose, (float)hero.ReferenceDepth); session.Rig.Tick(StyleCaptureSession.Dt);
                                if (measured)
                                {
                                    const ProfilerRecorderOptions options = ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
                                    using (var gcRec = new ProfilerRecorder("GC.Alloc", 1, options))
                                    {
                                        gcRec.Start();
                                        watch.Restart();
                                        session.Capture.Render(run.Present);
                                        watch.Stop();
                                        gcRec.Stop();
                                        s.RenderMs.Add(watch.Elapsed.TotalMilliseconds);
                                        if (!gcRec.Valid) s.GcAllocSamples = -1_000_000_000;
                                        else if (gcRec.Count == 1) s.GcAllocSamples += gcRec.GetSample(0).Count;
                                    }
                                    var target = run.Present ? session.Capture.OutputTarget : session.Capture.Binder.InternalColour;
                                    var previous = RenderTexture.active; RenderTexture.active = target;
                                    probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                                    RenderTexture.active = previous;
                                    watch.Stop();   // render + readback measured from the same start
                                    s.WallMs.Add(watch.Elapsed.TotalMilliseconds);
                                }
                                else session.Capture.Render(run.Present);
                            }
                            yield return null;
                        }
                        s.GcCollections = (GC.CollectionCount(0) - g0) + (GC.CollectionCount(1) - g1) + (GC.CollectionCount(2) - g2);
                        s.MonoDeltaBytes = Profiler.GetMonoUsedSizeLong() - heap0;
                        all.Add(s);
                        Debug.Log("[StyleFrameTiming] " + s.Config + " " + s.Output + ": render p50 " + P(s.RenderMs, 0.5).ToString("0.###", Inv) + " ms, draw calls " + (s.Draw.Count > 0 ? P(s.Draw, 0.5).ToString("0", Inv) : "n/a"));
                    }
                }
                finally { recDraw.Dispose(); recBatch.Dispose(); recSetPass.Dispose(); recGcFrame.Dispose(); }
                session.Flags = m0; session.Apply();
            }
            finally { session.Dispose(); UnityEngine.Object.DestroyImmediate(probe); }
            Write(outDir, all, frames, warmup, profile);
        }

        // NOTE on wall time: the render-only stopwatch is stopped before the readback; the wall stopwatch is stopped after it (same start), so
        // wall_ms = render + one-pixel readback = CPU submission + GPU execution of the frame.

        static void Write(string outDir, List<Series> all, int frames, int warmup, string profile)
        {
            string gpu = SystemInfo.graphicsDeviceName, cpu = SystemInfo.processorType;
            string hardware = "GPU " + gpu + " (" + SystemInfo.graphicsDeviceType + "), CPU " + cpu + ", " + SystemInfo.operatingSystem + ", Unity " + Application.unityVersion;
            var j = new StringBuilder();
            j.Append("{\n \"tool\": \"Game.Tools.StyleFrame.StyleFrameTiming.TimingCli\",\n \"requirement\": \"C15\",\n");
            j.Append(" \"method\": \"self-ticked on EditorApplication.update; render_ms = Stopwatch around Capture.Render (CPU submission); wall_ms = render + one-pixel readback (GPU finished); draw calls, batches, SetPass and GC allocated in frame are ProfilerRecorder values of the previous editor frame; gc_alloc_samples counts GC.Alloc samples inside the render call only\",\n");
            j.Append(" \"hardware\": {\"gpu\": ").Append(StyleCaptureSession.Str(gpu)).Append(", \"graphics_api\": ").Append(StyleCaptureSession.Str(SystemInfo.graphicsDeviceType.ToString())).Append(", \"cpu\": ").Append(StyleCaptureSession.Str(cpu)).Append(", \"os\": ").Append(StyleCaptureSession.Str(SystemInfo.operatingSystem)).Append(", \"unity\": ").Append(StyleCaptureSession.Str(Application.unityVersion)).Append(", \"batch_mode\": ").Append(Application.isBatchMode ? "true" : "false").Append("},\n");
            j.Append(" \"note\": \"Relative numbers on the development machine. M-008 unverified (not the target PC).\",\n");
            j.Append(" \"profile\": ").Append(StyleCaptureSession.Str(profile)).Append(", \"warmup_frames\": ").Append(warmup).Append(", \"frames_measured\": ").Append(frames).Append(",\n \"runs\": [");
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                j.Append(i > 0 ? ",\n  " : "\n  ").Append("{\"config\": \"").Append(s.Config).Append("\", \"output\": \"").Append(s.Output).Append("\", \"internal\": \"").Append(s.Internal).Append("\", \"frames\": ").Append(s.RenderMs.Count);
                j.Append(", \"render_ms\": ").Append(Stats(s.RenderMs)).Append(", \"wall_ms\": ").Append(Stats(s.WallMs));
                j.Append(", \"draw_calls\": ").Append(s.CountersValid ? Stats(s.Draw) : "null").Append(", \"batches\": ").Append(s.CountersValid ? Stats(s.Batches) : "null").Append(", \"setpass_calls\": ").Append(s.CountersValid ? Stats(s.SetPass) : "null");
                j.Append(", \"gc_allocated_in_editor_frame_bytes\": ").Append(s.CountersValid ? Stats(s.GcFrameBytes) : "null");
                j.Append(", \"gc_alloc_samples_in_render_total\": ").Append(s.GcAllocSamples).Append(", \"gc_collections\": ").Append(s.GcCollections).Append(", \"mono_used_delta_bytes\": ").Append(s.MonoDeltaBytes).Append("}");
            }
            j.Append("\n ]\n}\n");
            File.WriteAllText(Path.Combine(outDir, "timing.json"), j.ToString());

            string cmd = "/Applications/Unity/Hub/Editor/6000.6.9f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath <kaiju> -logFile <log> -executeMethod Game.Tools.StyleFrame.StyleFrameTiming.TimingCli";
            var m = new StringBuilder();
            m.Append("# T-006 C15: frame cost of the composed style-frame scene\n\n");
            m.Append("Command (no -quit; the job self-ticks and exits itself):\n\n```\n").Append(cmd).Append("\n```\n\n");
            m.Append("Hardware: ").Append(hardware).Append("; M-008 unverified (not the target PC). Relative numbers only.\n\n");
            m.Append("Scene: tile + mech + props + pixel pipeline, hero camera, profile ").Append(profile).Append(", ").Append(warmup).Append(" warm-up frames then ").Append(frames).Append(" measured frames per row. ");
            m.Append("render = CPU submission of the render call; wall = render + one-pixel readback (GPU finished). Draw calls, batches, SetPass: ProfilerRecorder, p50 (p95). ");
            m.Append("baseline_pixel_off = dither, haze, quantise off and no present blit.\n\n");
            m.Append("| config | output (internal) | render p50 / p95 ms | wall p50 / p95 ms | draw calls | batches | SetPass | GC.Alloc samples in render | GC collections |\n|---|---|---|---|---|---|---|---|---|\n");
            foreach (var s in all)
            {
                m.Append("| ").Append(s.Config).Append(" | ").Append(s.Output).Append(" (").Append(s.Internal).Append(") | ")
                 .Append(P(s.RenderMs, 0.5).ToString("0.00", Inv)).Append(" / ").Append(P(s.RenderMs, 0.95).ToString("0.00", Inv)).Append(" | ")
                 .Append(P(s.WallMs, 0.5).ToString("0.00", Inv)).Append(" / ").Append(P(s.WallMs, 0.95).ToString("0.00", Inv)).Append(" | ")
                 .Append(Cell(s.Draw, s.CountersValid)).Append(" | ").Append(Cell(s.Batches, s.CountersValid)).Append(" | ").Append(Cell(s.SetPass, s.CountersValid)).Append(" | ")
                 .Append(s.GcAllocSamples).Append(" | ").Append(s.GcCollections).Append(" |\n");
            }
            m.Append("\nZero per-frame GC after warm-up: GC.Alloc samples in the render call must be 0 in every row (GC collections over the measured window should also be 0). Full per-run statistics, including GC allocated in the whole editor frame, are in timing.json.\n");
            File.WriteAllText(Path.Combine(outDir, "timing.md"), m.ToString());
        }

        static string Cell(List<double> v, bool valid) => valid && v.Count > 0 ? P(v, 0.5).ToString("0", Inv) + " (" + P(v, 0.95).ToString("0", Inv) + ")" : "n/a";

        static double P(List<double> values, double q)
        {
            if (values.Count == 0) return 0;
            var sorted = new List<double>(values); sorted.Sort();
            return sorted[Math.Min(sorted.Count - 1, (int)Math.Floor(q * (sorted.Count - 1) + 0.5))];
        }

        static string Stats(List<double> values)
        {
            if (values.Count == 0) return "null";
            double mean = 0; foreach (double v in values) mean += v; mean /= values.Count;
            var sorted = new List<double>(values); sorted.Sort();
            return "{\"mean\": " + F(mean) + ", \"p50\": " + F(P(values, 0.5)) + ", \"p95\": " + F(P(values, 0.95)) + ", \"max\": " + F(sorted[sorted.Count - 1]) + "}";
        }

        static string F(double v) => v.ToString("0.####", Inv);
    }
}
