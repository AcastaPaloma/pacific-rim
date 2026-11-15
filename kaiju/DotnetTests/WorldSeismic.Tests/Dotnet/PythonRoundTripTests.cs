// T-036c: the C# exporters round-trip into the Python validators. Real baked faults and real baked district tiles; skipped (Ignore) when the
// kaiju interpreter is not found (set KAIJU_PYTHON to override /opt/miniconda3/envs/kaiju/bin/python).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Game.World.Destruction;
using Game.World.Geo;
using NUnit.Framework;

namespace Game.World.Seismic.Tests
{
    public class PythonRoundTripTests
    {
        static string Root()
        {
            string d = AppContext.BaseDirectory;
            while (d != null && !Directory.Exists(Path.Combine(d, "Assets", "StreamingAssets", "Geo"))) d = Path.GetDirectoryName(d);
            return d ?? throw new DirectoryNotFoundException("project root");
        }

        static string Python()
        {
            string p = Environment.GetEnvironmentVariable("KAIJU_PYTHON") ?? "/opt/miniconda3/envs/kaiju/bin/python";
            if (!File.Exists(p)) Assert.Ignore("kaiju python not found: " + p);
            return p;
        }

        static int Run(string root, string script, string args, out string output)
        {
            var psi = new ProcessStartInfo(Python(), "\"" + Path.Combine(root, script) + "\" " + args)
            { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(120000);
                output = o;
                return p.ExitCode;
            }
        }

        static string Temp()
        {
            string d = Path.Combine(Path.GetTempPath(), "kaiju_t036c_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        [Test]
        public void RuptureDumps_PassM053AndM054_OnTheRealFaults()
        {
            string root = Root(), tmp = Temp();
            var set = FaultTraceSet.Build(RealFaults.Load());
            var dir = new QuakeDirector(set, new DistrictRefs { GGX = 54272, GGZ = 77824, SoMaX = 62464, SoMaZ = 75776, TargetGG = true }, null);
            var results = new List<RuptureResult>();
            uint ev = 1;
            foreach (string prefix in new[] { "San Andreas", "Hayward", "Calaveras" })
            {
                FaultSystem s = set.Systems.Where(x => x.Name.StartsWith(prefix)).OrderByDescending(x => x.LengthM).FirstOrDefault();
                if (s == null) continue;
                int mid = s.VertexCount / 2;
                RuptureResult r = dir.Run(new QuakeScenario { EventId = ev++, Mw = 7.2f, FaultId = s.FaultId, HypoX = (float)s.X[mid], HypoZ = (float)s.Z[mid], Seed = 11 + ev });
                Assert.IsNotNull(r, prefix);
                results.Add(r);
            }
            Assert.GreaterOrEqual(results.Count, 2);
            string faultsRel = "Assets/StreamingAssets/Geo/global/v1/faults_bay.geojson";
            string sha;
            using (var h = SHA256.Create()) sha = string.Concat(h.ComputeHash(File.ReadAllBytes(Path.Combine(root, faultsRel))).Select(b => b.ToString("x2")));
            File.WriteAllText(Path.Combine(tmp, "fr.json"), SeismicDumpExporter.BuildFaultRuptureDump(results, faultsRel, sha));
            File.WriteAllText(Path.Combine(tmp, "r.json"), SeismicDumpExporter.BuildRuptureDump(results));
            int c53 = Run(root, "Tools/Metrics/metrics.py", "m053 \"" + Path.Combine(tmp, "fr.json") + "\" --out \"" + Path.Combine(tmp, "o53") + "\" --quiet", out string out53);
            int c54 = Run(root, "Tools/Metrics/metrics.py", "m054 \"" + Path.Combine(tmp, "r.json") + "\" --out \"" + Path.Combine(tmp, "o54") + "\" --quiet", out string out54);
            Assert.AreEqual(0, c53, out53);
            Assert.AreEqual(0, c54, out54);
            StringAssert.Contains("PASS", out53);
            StringAssert.Contains("PASS", out54);
        }

        [Test]
        public void DistrictPieces_PassM005PiecesCheck_OnTheRealTiles()
        {
            string root = Root(), tmp = Temp();
            var settings = GeoSettings.Load(Path.Combine(root, "Assets", "Game", "Config", "WorldGeo.json"));
            var tiles = new List<DistrictTileBuildings>();
            foreach (string t in new[] { "L0_26_36", "L0_26_37", "L0_27_36", "L0_27_37" })   // a 2 x 2 block with interior seams
            {
                GeoTileId.TryParse(t, out GeoTileId id);
                var ctx = new GeoTileContext(Path.Combine(root, "Assets", "StreamingAssets", "Geo", "tiles", "v1", id.Folder.Replace('/', Path.DirectorySeparatorChar)), settings, false);
                tiles.Add(new DistrictTileBuildings { Tile = id, Buildings = new GeoTileModel(ctx, new IGeoLayer[] { new BuildingsLayer() }).Buildings });
            }
            var pieces = DistrictPiecesExporter.Build(tiles);
            Assert.Greater(pieces.Count, tiles.Sum(x => x.Buildings.Count) - 1, "at least one piece per building with area");
            File.WriteAllText(Path.Combine(tmp, "pieces.json"), DistrictPiecesExporter.ToJson(pieces));
            int code = Run(root, "Pipelines/geo/m005_check.py", "district --tiles L0_26_36,L0_26_37,L0_27_36,L0_27_37 --pieces \"" + Path.Combine(tmp, "pieces.json") + "\"", out string o);
            StringAssert.Contains("clipped_pieces_area", o);
            StringAssert.Contains("clipped_pieces_height_base", o);
            foreach (string line in o.Split('\n').Where(l => l.Contains("clipped_pieces"))) StringAssert.Contains("pass", line, line);
            Assert.IsFalse(o.Contains("error  "), o);
        }
    }
}
