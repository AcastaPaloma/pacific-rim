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
        string scenario = "hunt", brain = BayBrains.Candidate, comps = "LLH", seeds = "1-1", outDir = "m007_bay_out", speciesDir = null;
        bool defaults = false; int par = Math.Max(1, Environment.ProcessorCount - 2);
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
                case "--defaults": defaults = true; break;
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
        var sp = seeds.Split('-'); ulong s0 = ulong.Parse(sp[0]), s1 = ulong.Parse(sp[sp.Length - 1]);
        string dir = Path.Combine(outDir, "m007_bay_" + scenario, brain.Replace("@", "_"));
        Directory.CreateDirectory(dir);
        var jobs = new List<(string comp, ulong seed)>();
        foreach (string c in comps.Split(',')) for (ulong s = s0; s <= s1; s++) jobs.Add((c, s));
        long ticks = Environment.TickCount64;
        long decisions = 0;
        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = par }, job =>
        {
            var sc = new BayScenario { Name = scenario, Composition = job.comp, Seed = job.seed, BrainId = brain, Species = table };
            string path = Path.Combine(dir, job.comp + "_" + job.seed + ".jsonl");
            using (var w = new StreamWriter(path, false, new System.Text.UTF8Encoding(false), 1 << 16))
            {
                var r = new BayHarness(sc, w).Run();
                System.Threading.Interlocked.Add(ref decisions, r.Decisions);
            }
        });
        Console.WriteLine("{0} runs, {1} decisions, {2:F1} s -> {3}", jobs.Count, decisions, (Environment.TickCount64 - ticks) / 1000.0, dir);
        return 0;
    }

    private static string FindSpeciesDir()
    {
        var d = new DirectoryInfo(Environment.CurrentDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Assets", "Game", "Units", "Kaiju", "species"))) d = d.Parent;
        if (d == null) throw new InvalidOperationException("species dir not found; pass --species-dir");
        return Path.Combine(d.FullName, "Assets", "Game", "Units", "Kaiju", "species");
    }
}
