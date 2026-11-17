// T-124f: managed allocation of the streaming host, in dotnet (GC.GetAllocatedBytesForCurrentThread). Bytes per StreamingHostCore.Tick on a Bay-like foci path
// (scheduler + host bookkeeping, fake reader and builder) and bytes per tile read (L0 render tile, L0 physics chunk, geo_lod@v1 L1..L4 when a baked pyramid is
// reachable) with the dominant buffers split out. Writes KAIJU_T124F_OUT (csv) when set. The asserts are loose ceilings, not the evidence; the evidence is the csv.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Game.World.Geo;
using NUnit.Framework;

namespace Game.World.Streaming.Host.Tests
{
    public class StreamingAllocBenchTests
    {
        const double X0 = 41000.0, Z0 = 52000.0;
        static readonly CultureInfo Ic = CultureInfo.InvariantCulture;

        static string ProjectRoot()
        {
            var d = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (d != null)
            {
                if (Directory.Exists(Path.Combine(d.FullName, "Assets", "StreamingAssets", "Geo"))) return d.FullName;
                var k = Path.Combine(d.FullName, "kaiju");
                if (Directory.Exists(Path.Combine(k, "Assets", "StreamingAssets", "Geo"))) return k;
                d = d.Parent;
            }
            throw new DirectoryNotFoundException("project root not found");
        }
        static string TilesRoot => Path.Combine(ProjectRoot(), "Assets", "StreamingAssets", "Geo", "tiles", "v1");
        static IReadOnlyDictionary<string, double> Settings() => GeoSettings.Load(Path.Combine(ProjectRoot(), "Assets", "Game", "Config", "WorldGeo.json"));

        internal static string RealLodRoot()
        {
            var env = Environment.GetEnvironmentVariable("KAIJU_LOD_ROOT");
            if (!string.IsNullOrEmpty(env) && Directory.Exists(Path.Combine(env, "L1"))) return env;
            var d = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (d != null)
            {
                foreach (var c in new[] { Path.Combine(d.FullName, "Pipelines", "geo", "cache", "bake", "tiles", "v1"), Path.Combine(d.FullName, "kaiju", "Pipelines", "geo", "cache", "bake", "tiles", "v1"), Path.Combine(d.FullName, "Pipelines", "geo", "cache", "tiles", "v1") })
                    if (Directory.Exists(Path.Combine(c, "L1"))) return c;
                d = d.Parent;
            }
            return null;
        }

        // ---- Tick bytes ----

        sealed class PayloadReader : ITileReader
        {
            public TilePayload Read(ResidencyKey key, CancellationToken ct) { var p = new TilePayload { Key = key, GpuBytes = 1024, CpuBytes = 512 }; return p; }
        }
        sealed class NullBuilder : ITileBuilder
        {
            public void Build(in ResidencyKey key, TilePayload payload, IBuildSink sink) { sink.Built(key, payload.GpuBytes, payload.CpuBytes); }
            public void Release(in ResidencyKey key) { }
            public void ShiftOrigin(double ox, double oz) { }
        }

        /// <summary>Camera + 2 mechs on a path at speed m/s, 3 kaiju and an anchor wandering within 3 km (as in StreamingSchedulerBenchTests), deterministic from the seed.</summary>
        sealed class Path3
        {
            readonly Random _r; double _cx = X0, _cz = Z0, _ang; readonly double[] _kx = new double[3], _kz = new double[3]; readonly double _speed;
            readonly FocusSample[] _f = new FocusSample[7];
            public Path3(int seed, double speed) { _r = new Random(seed); _speed = speed; for (int i = 0; i < 3; i++) { _kx[i] = X0 + _r.Next(-3000, 3000); _kz[i] = Z0 + _r.Next(-3000, 3000); } }
            static FocusSample Cam(int id, double x, double z, float vx, float vz) => new FocusSample { Id = id, Kind = FocusKind.Camera, X = x, Y = 50, Z = z, Vx = vx, Vz = vz };
            static FocusSample Body(int id, double x, double z, float vx = 0, float vz = 0) => new FocusSample { Id = id, Kind = FocusKind.Body, X = x, Y = 10, Z = z, Vx = vx, Vz = vz, ReachM = 100f, NeedsPhysics = true };
            public FocusSample[] Next(double dt)
            {
                _ang += (_r.NextDouble() - 0.5) * 0.2; float vx = (float)(_speed * Math.Cos(_ang)), vz = (float)(_speed * Math.Sin(_ang)); _cx += vx * dt; _cz += vz * dt;
                _f[0] = Cam(1, _cx, _cz, vx, vz); _f[1] = Body(10, _cx, _cz, vx, vz); _f[2] = Body(11, _cx + 100, _cz + 50, vx, vz);
                for (int i = 0; i < 3; i++) { _kx[i] += _r.NextDouble() - 0.5; _kz[i] += _r.NextDouble() - 0.5; _f[3 + i] = Body(20 + i, _kx[i], _kz[i]); }
                _f[6] = Body(40, _kx[0], _kz[0]);
                return _f;
            }
        }

        /// <summary>Bytes allocated by StreamingHostCore.Tick per tick on the path (fake reader and builder: only the payload object is theirs), split into ticks that dispatch loads and ticks that do not.</summary>
        internal static void TickAlloc(double speed, double replanMoveM, out long perTick, out long perQuietTick, out long perLoadTick, out int loadTicks, out int ticks)
        {
            var cfg = new StreamingConfig { PhysicsPrefetchRadiusM = 768, ReplanMoveM = replanMoveM };
            var s = new StreamingScheduler(cfg, new DefaultTileCostModel());
            var h = new StreamingHostCore(s, new PayloadReader(), new NullBuilder(), new InlineWorkRunner(), new StreamingHostOptions { MainThreadSliceMs = 1e9 });
            var p = new Path3(5, speed);
            long all = 0, quiet = 0, load = 0; int nq = 0, nl = 0, n = 0;
            for (int t = 0; t < 6000; t++)
            {
                var foci = p.Next(0.1);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                h.Tick(0.1, foci);
                long a = GC.GetAllocatedBytesForCurrentThread() - a0;
                if (t < 500) continue;
                n++; all += a;
                if (h.LastPlan.Load.Count > 0 || h.LastPlan.Evict.Count > 0) { nl++; load += a; } else { nq++; quiet += a; }
            }
            perTick = all / n; perQuietTick = nq > 0 ? quiet / nq : 0; perLoadTick = nl > 0 ? load / nl : 0; loadTicks = nl; ticks = n;
        }

        // ---- read bytes ----

        static long Measure(Action a, out double ms)
        {
            long a0 = GC.GetAllocatedBytesForCurrentThread(); long t0 = Stopwatch.GetTimestamp();
            a();
            ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            return GC.GetAllocatedBytesForCurrentThread() - a0;
        }

        static string Row(string what, Action a)
        {
            a(); // warm (JIT, first-read validation caches)
            var bytes = new long[3]; var mss = new double[3];
            for (int i = 0; i < 3; i++) bytes[i] = Measure(a, out mss[i]);
            Array.Sort(bytes); Array.Sort(mss);
            return what + "," + bytes[1].ToString(Ic) + "," + mss[1].ToString("F1", Ic) + "\n";
        }

        /// <summary>Where the bytes of one L2 tile read go: the dominant buffers, each measured on its own with the same calls the reader makes.</summary>
        static string Components(string lod)
        {
            var id = GeoTileId.From(2, 8, 8); var dir = LodSpec.TileFolder(lod, id);
            var sb = new StringBuilder("== components of one L2 read (" + id + ") ==\nwhat,bytes,ms\n");
            var man = GeoJson.Obj(GeoJson.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json"))));
            var t = GeoJson.Obj(GeoJson.Get(man, "terrain"));
            int n = (int)GeoJson.Num(t, "samples", LodSpec.Samples); double sp = GeoJson.Num(t, "spacing_m");
            byte[] raw = null; GeoHeightField hf = null;
            sb.Append(Row("terrain.r16 File.ReadAllBytes", () => raw = File.ReadAllBytes(Path.Combine(dir, GeoJson.Str(t, "file")))));
            sb.Append(Row("GeoHeightField.FromR16", () => hf = GeoHeightField.FromR16(raw, n, sp, GeoJson.Num(t, "h_min"), GeoJson.Num(t, "h_max"))));
            double thr = GeoJson.Num(t, "rtin_threshold_m");
            sb.Append(Row("RtinMesher.Build", () => RtinMesher.Build(hf.Data, n, sp, (float)thr, 256, "terrain", null)));
            var forms = GeoJson.Obj(GeoJson.Get(man, "buildings"));
            foreach (var form in LodSpec.FormsOfLevel(id.Level))
            {
                var fo = GeoJson.Obj(GeoJson.Get(forms, "form_" + form)); byte[] mb = null;
                sb.Append(Row("form " + form + " ReadAllBytes + LodMesh.Decode", () => { mb = File.ReadAllBytes(Path.Combine(dir, GeoJson.Str(fo, "file"))); LodMesh.Decode(mb); }));
                sb.Append(Row("form " + form + " LodBuildingIndex.Parse", () => LodBuildingIndex.Parse(File.ReadAllText(Path.Combine(dir, GeoJson.Str(fo, "index"))))));
            }
            if (File.Exists(Path.Combine(dir, "water.png"))) sb.Append(Row("water.png ReadAllBytes + PngGray8.Decode", () => PngGray8.Decode(File.ReadAllBytes(Path.Combine(dir, "water.png")), out _, out _)));
            return sb.ToString();
        }

        [Test]
        public void Bench_AllocationPerTickAndPerRead()
        {
            var sb = new StringBuilder(); sb.Append("== host tick (bytes/tick, scheduler + host, fake reader/builder) ==\nspeed_mps,replan_move_m,bytes_per_tick,bytes_per_quiet_tick,bytes_per_load_or_evict_tick,load_ticks,ticks\n");
            TickAlloc(30, 0, out _, out _, out _, out _, out _);   // JIT
            foreach (double sp in new[] { 6.0, 30.0, 100.0 })
                foreach (double mv in new[] { 0.0, 3.0 })
                {
                    TickAlloc(sp, mv, out long a, out long q, out long l, out int nl, out int n);
                    sb.Append(sp.ToString(Ic)).Append(',').Append(mv.ToString(Ic)).Append(',').Append(a).Append(',').Append(q).Append(',').Append(l).Append(',').Append(nl).Append(',').Append(n).Append('\n');
                    Assert.Less(q, 4096, "a tick without loads or evictions must stay near zero allocation (speed " + sp + ")");
                }
            sb.Append("== tile reads (bytes allocated on the reading thread, median of 3 after a warm-up) ==\nwhat,bytes,ms\n");
            var l0 = new GeoL0TileReader(TilesRoot, Settings(), 3);
            var t0 = GeoTileId.From(0, 30, 36);
            sb.Append(Row("L0 render 30_36", () => l0.Read(ResidencyKey.Render(t0), CancellationToken.None)));
            sb.Append(Row("L0 physics chunk 30_36 (3,3)", () => l0.Read(ResidencyKey.PhysicsChunk(t0, 3, 3), CancellationToken.None)));
            sb.Append(Row("L0 physics chunk 30_36 (5,2) fresh reader (model rebuild)", () => new GeoL0TileReader(TilesRoot, Settings(), 3).Read(ResidencyKey.PhysicsChunk(t0, 5, 2), CancellationToken.None)));
            var lod = RealLodRoot();
            if (lod != null)
                for (int level = 1; level <= 4; level++)
                {
                    var dirs = Directory.GetDirectories(Path.Combine(lod, "L" + level)); Array.Sort(dirs);
                    var name = Path.GetFileName(dirs[dirs.Length / 2]).Split('_');
                    var id = GeoTileId.From(level, int.Parse(name[0]), int.Parse(name[1]));
                    var r = new GeoLodTileReader(lod);
                    sb.Append(Row("L" + level + " render " + id.I + "_" + id.J, () => r.Read(ResidencyKey.Render(id), CancellationToken.None)));
                }
            else sb.Append("(no baked geo_lod@v1 pyramid: set KAIJU_LOD_ROOT)\n");
            if (lod != null) sb.Append(Components(lod));
            Console.WriteLine("T124F_BENCH\n" + sb);
            var path = Environment.GetEnvironmentVariable("KAIJU_T124F_OUT");
            if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, sb.ToString());
        }
    }
}
