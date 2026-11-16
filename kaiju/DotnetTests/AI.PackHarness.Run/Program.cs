// m007_bay runner (T-115 step 2). Species numbers come from the Units.Kaiju specs (Composition KaijuSpeciesRoles) unless --defaults (R-103 0.2 numbers).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Game.AI.Pack;
using Game.AI.PackHarness;
using Game.Composition;
using Game.Units.Kaiju;
using Game.Units.Kaiju.Contracts;

internal static class Program
{
    private static int Main(string[] args)
    {
        string map = "flat"; bool plan = true; string scenario = "hunt", brain = BayBrains.Candidate, comps = "LLH", seeds = "1-1", outDir = "m007_bay_out", speciesDir = null, costRaster = null, costVersion = Environment.GetEnvironmentVariable("KAIJU_BAY_COST_VERSION") ?? BayRasterCostField.DefaultVersion;
        BayLayout huntShift = null; string variant = ""; float squadDist = 1000f; float[] mv2 = null; bool defaults = false; int par = Math.Max(1, Environment.ProcessorCount - 2); string packOver = ""; float[] mixedSpawn = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--scenario": scenario = args[++i]; break;
                case "--brain": brain = args[++i]; break;
                case "--comps": comps = args[++i]; break;
                case "--seeds": seeds = args[++i]; break;
                case "--out": outDir = args[++i]; break;
                case "--species-dir": speciesDir = args[++i]; break;
                case "--map": map = args[++i]; break;
                case "--no-route": plan = false; break;
                case "--hunt-shift": { var q = args[++i].Split(','); huntShift = BayLayout.SyntheticHuntShifted(float.Parse(q[0]), float.Parse(q[1])); break; }
                case "--cost-raster": costRaster = args[++i]; break;
                case "--cost-version": costVersion = args[++i]; break;   // T-115h: bay_cost@v1 (low-land proxy) | v2 (USGS liquefaction, v1 proxy in coverage holes); default v2
                case "--variant": variant = args[++i]; break;
                case "--squad-dist": squadDist = float.Parse(args[++i]); break;
                case "--mv2": { var q = args[++i].Split(','); mv2 = new[] { float.Parse(q[0]), float.Parse(q[1]), float.Parse(q[2]), float.Parse(q[3]) }; break; }   // kaiju spacing, picket ahead, mech spacing, patrol radius (m)
                case "--defaults": defaults = true; break;
                case "--mixed-spawn": { var q = args[++i].Split(','); mixedSpawn = new[] { float.Parse(q[0]), float.Parse(q[1]) }; break; }
                case "--pack": packOver = args[++i]; break;   // T-113c: PackConfig overrides for sweeps, "BaitFirst=true,BaitDrawSeconds=2" (any public float/bool field)
                case "--parallel": par = int.Parse(args[++i]); break;
                default: Console.Error.WriteLine("unknown arg " + args[i]); return 2;
            }
        }
        if (Environment.GetEnvironmentVariable("KAIJU_PACK_DEBUG") == "1") PackCoordinator.DebugSink = Console.Error.WriteLine;
        SpeciesRole[] table = null;
        if (!defaults)
        {
            speciesDir = speciesDir ?? FindSpeciesDir();
            var specs = new List<KaijuSpeciesSpec>();
            foreach (var n in new[] { "strider", "breacher", "bastion" }) specs.Add(KaijuSpeciesLoader.Parse(File.ReadAllText(Path.Combine(speciesDir, n + ".json"))));
            table = KaijuSpeciesRoles.Table(new KaijuSpeciesCatalog(specs, KaijuSpeciesLoader.ParseTissue(File.ReadAllText(Path.Combine(speciesDir, "kaiju_tissue_v1.json")))));
        }
        BayRasterCostField realField = null;
        if (map == "real")
        {
            costRaster = costRaster ?? Environment.GetEnvironmentVariable("KAIJU_BAY_COST") ?? BayRasterCostField.FindRaster(FindRoot(), costVersion) ?? throw new FileNotFoundException("bay_cost@" + costVersion + " raster not found; pass --cost-raster");
            Console.Error.WriteLine("bay_cost raster: " + costRaster);
            realField = BayRasterCostField.Load(costRaster);   // read only, shared by all runs; every run builds its own planner
        }
        var sp = seeds.Split('-'); ulong s0 = ulong.Parse(sp[0]), s1 = ulong.Parse(sp[sp.Length - 1]);
        string dir = Path.Combine(outDir, "m007_bay_" + scenario, brain.Replace("@", "_"));
        Directory.CreateDirectory(dir);
        var jobs = new List<(string comp, ulong seed)>();
        foreach (string c in comps.Split(',')) for (ulong s = s0; s <= s1; s++) jobs.Add((c, s));
        long ticks = Environment.TickCount64;
        long decisions = 0, blocked = 0;
        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = par }, job =>
        {
            var sc = new BayScenario { Name = scenario, Composition = job.comp, Seed = job.seed, BrainId = brain, Species = table, Map = map, PlanRoutes = plan, Field = realField, Layout = huntShift, Variant = variant, Mv1SquadDistM = squadDist };
            sc.MixedSpawn = mixedSpawn;
            if (packOver.Length > 0) sc.Pack = ApplyPack(sc.Pack, packOver);
            if (mv2 != null) { sc.Mv2KaijuSpacingM = mv2[0]; sc.Mv2PicketAheadM = mv2[1]; sc.Mv2MechSpacingM = mv2[2]; sc.Mv2PatrolM = mv2[3]; }
            string path = Path.Combine(dir, job.comp + "_" + job.seed + ".jsonl");
            using (var w = new StreamWriter(path, false, new System.Text.UTF8Encoding(false), 1 << 16))
            {
                var r = new BayHarness(sc, w).Run();
                System.Threading.Interlocked.Add(ref decisions, r.Decisions); System.Threading.Interlocked.Add(ref blocked, r.BlockedSteps);
            }
        });
        Console.WriteLine("{0} runs, {1} decisions, {2:F1} s -> {3}{4}", jobs.Count, decisions, (Environment.TickCount64 - ticks) / 1000.0, dir, map != "flat" ? ", blocked steps " + blocked : "");
        return 0;
    }

    private static PackConfig ApplyPack(PackConfig c, string spec)
    {
        object boxed = c;
        foreach (string kv in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var q = kv.Split('=');
            var f = typeof(PackConfig).GetField(q[0].Trim());
            if (f == null) throw new ArgumentException("unknown PackConfig field " + q[0]);
            f.SetValue(boxed, f.FieldType == typeof(bool) ? bool.Parse(q[1]) : (object)float.Parse(q[1], System.Globalization.CultureInfo.InvariantCulture));
        }
        return (PackConfig)boxed;
    }

    private static string FindRoot()
    {
        var d = new DirectoryInfo(Environment.CurrentDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Assets", "Game"))) d = d.Parent;
        return d == null ? "." : d.FullName;
    }

    private static string FindSpeciesDir()
    {
        var d = new DirectoryInfo(Environment.CurrentDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Assets", "Game", "Units", "Kaiju", "species"))) d = d.Parent;
        if (d == null) throw new InvalidOperationException("species dir not found; pass --species-dir");
        return Path.Combine(d.FullName, "Assets", "Game", "Units", "Kaiju", "species");
    }
}
