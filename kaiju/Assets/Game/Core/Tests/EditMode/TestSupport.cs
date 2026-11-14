using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace Game.Core.Tests
{
    // Test-only events. Gameplay events live in their owning modules' Contracts, never in Core.
    public struct PingEvent : IEvent { public int Value; public PingEvent(int v) { Value = v; } }
    public struct PongEvent : IEvent { public int Value; public PongEvent(int v) { Value = v; } }
    public struct StepEvent : IEvent { public long Step; public int Index; }

    /// <summary>A 96-byte event, to prove large structs are not boxed or copied to the heap.</summary>
    public struct BigEvent : IEvent
    {
        public long A, B, C, D, E, F, G, H, I, J, K, L;
        public BigEvent(long seed) { A = seed; B = C = D = E = F = G = H = I = J = K = L = seed + 1; }
    }

    /// <summary>Handler built from a delegate, for compact tests (the delegate is created once, outside measured loops).</summary>
    public sealed class Handler<T> : IEventHandler<T> where T : unmanaged, IEvent
    {
        private readonly Action<T> _on;
        public Handler(Action<T> on) { _on = on; }
        public int Calls;
        public void Handle(in T evt) { Calls++; _on?.Invoke(evt); }
    }

    /// <summary>Appends "name:value" to a shared trace.</summary>
    public sealed class TraceHandler : IEventHandler<PingEvent>, IEventHandler<PongEvent>, IEventHandler<StepEvent>
    {
        private readonly string _name; private readonly StringBuilder _trace;
        public TraceHandler(string name, StringBuilder trace) { _name = name; _trace = trace; }
        public void Handle(in PingEvent e) => _trace.Append(_name).Append(":ping").Append(e.Value).Append(';');
        public void Handle(in PongEvent e) => _trace.Append(_name).Append(":pong").Append(e.Value).Append(';');
        public void Handle(in StepEvent e) => _trace.Append(_name).Append(":step").Append(e.Step).Append('.').Append(e.Index).Append(';');
    }

    /// <summary>Deterministic PRNG (SplitMix64): same seed, same sequence, in every runtime.</summary>
    public struct SplitMix64
    {
        private ulong _s;
        public SplitMix64(ulong seed) { _s = seed; }
        public ulong Next()
        {
            unchecked
            {
                ulong z = _s += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
        public int NextInt(int maxExclusive) => (int)(Next() % (ulong)maxExclusive);
    }

    public static class StableHash
    {
        /// <summary>FNV-1a 64 over UTF-16 code units; independent of runtime string hashing.</summary>
        public static ulong Fnv1a64(string s)
        {
            unchecked
            {
                ulong h = 0xCBF29CE484222325UL;
                foreach (char c in s) { h ^= (byte)c; h *= 0x100000001B3UL; h ^= (byte)(c >> 8); h *= 0x100000001B3UL; }
                return h;
            }
        }
        public static string Hex(ulong h) => h.ToString("X16", System.Globalization.CultureInfo.InvariantCulture);
    }

    public readonly struct AllocResult
    {
        public readonly long Bytes;   // managed heap bytes (GC.GetAllocatedBytesForCurrentThread)
        public readonly long Count;   // Unity only: GC.Alloc profiler samples on this thread; -1 where unavailable
        public AllocResult(long bytes, long count) { Bytes = bytes; Count = count; }
        public override string ToString() => "bytes=" + Bytes + " gcAllocCount=" + (Count < 0 ? "n/a" : Count.ToString());
    }

    public static class AllocMeter
    {
#if UNITY_2017_1_OR_NEWER
        public const string Runner = "unity";
#else
        public const string Runner = "dotnet";
#endif

        /// <summary>Measures managed allocations made by <paramref name="action"/> on the current thread.</summary>
        public static AllocResult Measure(Action action)
        {
#if UNITY_2017_1_OR_NEWER
            // Same recorder setup as the Unity Test Framework's Is.Not.AllocatingGCMemory() constraint.
            const Unity.Profiling.ProfilerRecorderOptions options =
                Unity.Profiling.ProfilerRecorderOptions.SumAllSamplesInFrame | Unity.Profiling.ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
            using (var recorder = new Unity.Profiling.ProfilerRecorder("GC.Alloc", 1, options))
            {
                bool valid = recorder.Valid;
                long before = GC.GetAllocatedBytesForCurrentThread();
                if (valid) recorder.Start();
                action();
                if (valid) recorder.Stop();
                long after = GC.GetAllocatedBytesForCurrentThread();
                long count = valid && recorder.Count == 1 ? recorder.GetSample(0).Count : -1;
                return new AllocResult(after - before, count);
            }
#else
            long before = GC.GetAllocatedBytesForCurrentThread();
            action();
            return new AllocResult(GC.GetAllocatedBytesForCurrentThread() - before, -1);
#endif
        }
    }

    /// <summary>Writes measurement lines to $KAIJU_EVIDENCE_DIR/&lt;runner&gt;-&lt;file&gt; when that variable is set.</summary>
    public static class Evidence
    {
        /// <summary>Set while SuiteHygieneTests re-runs the suite, so evidence files get one line per real run.</summary>
        [ThreadStatic] public static bool Suppressed;

        public static void Line(string file, string line)
        {
            if (Suppressed) return;
            TestContext.WriteLine(file + ": " + line);
            var dir = Environment.GetEnvironmentVariable("KAIJU_EVIDENCE_DIR");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, AllocMeter.Runner + "-" + file), line + "\n");
        }
    }

    public static class ProjectPaths
    {
        /// <summary>The Unity project root (folder holding Assets/Game/Core), found from the working or base directory.</summary>
        public static string Root
        {
            get
            {
                foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
                {
                    var d = new DirectoryInfo(start);
                    while (d != null)
                    {
                        if (Directory.Exists(Path.Combine(d.FullName, "Assets", "Game", "Core"))) return d.FullName;
                        d = d.Parent;
                    }
                }
                throw new DirectoryNotFoundException("Project root with Assets/Game/Core not found");
            }
        }

        public static string Of(params string[] parts)
        {
            var all = new List<string> { Root };
            all.AddRange(parts);
            return Path.Combine(all.ToArray());
        }

        public static string Fixture(string name) => Of("Assets", "Game", "Core", "Tests", "EditMode", "Fixtures", name);

        public static string NewTempDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "kaiju-core-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
