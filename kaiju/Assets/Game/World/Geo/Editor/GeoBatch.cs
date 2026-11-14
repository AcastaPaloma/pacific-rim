// Batch entry points for T-003 evidence (Unity CLI: -executeMethod Game.World.Geo.Editor.GeoBatch.<Method>).
// Builds the tile in a fresh in-memory scene through the Editor API, exports as-built facts, statistics,
// removal and floating-origin checks, the attribution asset, and captures. Never writes scene text.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Game.Core;
using Game.World.Geo.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

namespace Game.World.Geo.Editor
{
    public static class GeoBatch
    {
        public const string AttributionAssetPath = "Assets/Game/World/Geo/Attribution/GeoAttribution.asset";

        static string Arg(string name, string fallback)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++) if (a[i] == name) return a[i + 1];
            return fallback;
        }

        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        static string OutDir()
        {
            var d = Arg("-geoOut", Path.Combine(ProjectRoot, "evidence", "T-003"));
            Directory.CreateDirectory(d);
            return d;
        }

        static void Write(string path, object json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, GeoJson.Write(json) + "\n");
        }

        static List<double[]> ControlPoints(string outDir)
        {
            var p = Path.Combine(outDir, "control_points.json");
            var res = new List<double[]>();
            if (!File.Exists(p)) return res;
            var d = GeoJson.Obj(GeoJson.Parse(File.ReadAllText(p)));
            foreach (var o in GeoJson.Arr(GeoJson.Get(d, "points"))) { var a = GeoJson.Arr(o); res.Add(new[] { (double)a[0], (double)a[1] }); }
            return res;
        }

        public static GeoTileInstance BuildTile(GeoTileId id, double ox, double oz)
        {
            return GeoTileBuilder.Build(GeoTileBuilder.TileDirectory(id), new GeoBuildOptions
            {
                FloatingOriginX = ox, FloatingOriginZ = oz, Settings = GeoTileBuilder.LoadSettings(),
            });
        }

        /// <summary>Everything in one Unity launch: build, export, stats, removal, origin shift, attribution, captures.</summary>
        public static void All()
        {
            int code = 0;
            try { Run(Arg("-geoTile", "L0_30_36"), Arg("-geoCaptures", "1") == "1"); }
            catch (Exception e) { Debug.LogError("[GeoBatch] FAILED: " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        static void Run(string tileName, bool captures)
        {
            var outDir = OutDir();
            GeoTileId.TryParse(tileName, out var id);
            var log = new List<string>();
            void L(string s) { Debug.Log("[GeoBatch] " + s); log.Add(s); }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sw = Stopwatch.StartNew();
            var inst = BuildTile(id, id.OriginX, id.OriginZ);
            Physics.SyncTransforms();
            double buildS = sw.Elapsed.TotalSeconds;
            L($"built {tileName} in {buildS:F2} s: {inst.Renderers.Count} renderers, {inst.TriangleCount()} triangles");

            var cps = ControlPoints(outDir);
            var export = inst.Export(cps);
            var stats = (Dictionary<string, object>)export["stats"];
            stats["time.editor_build_s"] = buildS;
            stats["memory.bytes"] = MemoryBytes(inst);
            stats["triangles.terrain"] = inst.TriangleCount("Terrain");
            stats["triangles.buildings"] = inst.TriangleCount("BuildingChunks");
            stats["triangles.water"] = inst.TriangleCount("Water");
            Write(Path.Combine(outDir, "export", "geo_export.json"), export);
            L($"export: {((List<object>)export["buildings"]).Count} buildings, {cps.Count} control points");

            // ---- floating origin: rebase by tile multiples, re-export positions (C17)
            var shifts = new List<object>();
            foreach (var (dx, dz) in new[] { (2048.0, -2048.0), (-4096.0, 2048.0), (0.0, 0.0) })
            {
                inst.ShiftOrigin(id.OriginX + dx, id.OriginZ + dz);
                var e2 = inst.Export(cps.Take(200).ToList());
                double maxPos = 0;
                var b1 = (List<object>)export["buildings"]; var b2 = (List<object>)e2["buildings"];
                for (int i = 0; i < b1.Count; i++)
                {
                    var a1 = (List<object>)((Dictionary<string, object>)b1[i])["anchor_baytm"];
                    var a2 = (List<object>)((Dictionary<string, object>)b2[i])["anchor_baytm"];
                    maxPos = Math.Max(maxPos, Math.Max(Math.Abs((double)a1[0] - (double)a2[0]), Math.Abs((double)a1[1] - (double)a2[1])));
                }
                double maxT = 0;
                var s1 = (List<object>)export["terrain_samples"]; var s2 = (List<object>)e2["terrain_samples"];
                for (int i = 0; i < Math.Min(200, s1.Count); i++)
                    maxT = Math.Max(maxT, Math.Abs((double)((List<object>)s1[i])[2] - (double)((List<object>)s2[i])[2]));
                shifts.Add(new Dictionary<string, object>
                {
                    ["origin_baytm"] = new List<object> { id.OriginX + dx, id.OriginZ + dz }, ["max_anchor_delta_m"] = maxPos,
                    ["max_terrain_delta_m"] = maxT, ["max_abs_scene_position_m"] = ((Dictionary<string, object>)e2["stats"])["max_abs_scene_position_m"],
                });
                L($"origin shift ({dx},{dz}): max anchor delta {maxPos:E2} m, terrain {maxT:E2} m");
            }
            Write(Path.Combine(outDir, "export", "floating_origin_check.json"), new Dictionary<string, object> { ["spec"] = "geo_origin_check@v1", ["shifts"] = shifts });

            // ---- attribution asset (C9)
            WriteAttribution(inst, outDir);

            // ---- captures (C8, C12, reported plates)
            if (captures) GeoCaptures.All(inst, outDir, stats, L);

            // ---- removal by bid at runtime (C14): 20 random buildings, then export again
            var rng = new System.Random(20261114);
            var ids = inst.Model.Buildings.Where(b => !b.HasParts).Select(b => b.Id).ToList();
            var victims = new SortedSet<BuildingId>();
            while (victims.Count < 20) victims.Add(ids[rng.Next(ids.Count)]);
            sw.Restart();
            int removed = 0;
            foreach (var v in victims) removed += inst.RemoveBuilding(v).Count;
            double remS = sw.Elapsed.TotalSeconds;
            Physics.SyncTransforms();
            var after = inst.Export(cps);
            ((Dictionary<string, object>)after["stats"])["removal_seconds"] = remS;
            after["removed_bids"] = victims.Select(v => (object)inst.Model.ById[v].Bid).ToList();
            Write(Path.Combine(outDir, "export", "geo_export_after_removal.json"), after);
            L($"removed {removed} buildings ({victims.Count} requested) in {remS * 1000:F1} ms without re-bake");

            Write(Path.Combine(outDir, "export", "tile_stats_unity.json"), stats);
            File.WriteAllLines(Path.Combine(outDir, "logs", "unity_geobatch.log"), log);
        }

        static long MemoryBytes(GeoTileInstance inst)
        {
            long b = 0;
            var seen = new HashSet<int>();
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh != null && seen.Add(mf.sharedMesh.GetInstanceID())) b += Profiler.GetRuntimeMemorySizeLong(mf.sharedMesh);
            foreach (var mc in inst.GetComponentsInChildren<MeshCollider>()) if (mc.sharedMesh != null && seen.Add(mc.sharedMesh.GetInstanceID())) b += Profiler.GetRuntimeMemorySizeLong(mc.sharedMesh);
            if (inst.Albedo != null) b += Profiler.GetRuntimeMemorySizeLong(inst.Albedo);
            if (inst.PhysicsSurface != null) b += Profiler.GetRuntimeMemorySizeLong(inst.PhysicsSurface.terrainData);
            return b;
        }

        public static void WriteAttribution(GeoTileInstance inst, string outDir)
        {
            var lines = new SortedSet<string>(inst.Model.Attributions, StringComparer.Ordinal);
            // the global fault layer ships too
            var fm = Path.Combine(Application.streamingAssetsPath, "Geo", "global", "v1", "faults_bay.manifest.json");
            if (File.Exists(fm))
                foreach (var so in GeoJson.Arr(GeoJson.Get(GeoJson.Obj(GeoJson.Parse(File.ReadAllText(fm))), "sources")))
                    lines.Add(GeoJson.Str(GeoJson.Obj(so), "attribution"));
            Directory.CreateDirectory(Path.Combine(ProjectRoot, Path.GetDirectoryName(AttributionAssetPath)));
            var asset = AssetDatabase.LoadAssetAtPath<GeoAttribution>(AttributionAssetPath);
            bool create = asset == null;
            if (create) asset = ScriptableObject.CreateInstance<GeoAttribution>();
            asset.Lines = lines.ToArray();
            asset.Tiles = new[] { inst.TileName };
            if (create) AssetDatabase.CreateAsset(asset, AttributionAssetPath); else EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Write(Path.Combine(outDir, "export", "attribution_asset_dump.json"), new Dictionary<string, object>
            {
                ["asset"] = AttributionAssetPath, ["lines"] = asset.Lines.Cast<object>().ToList(), ["tiles"] = asset.Tiles.Cast<object>().ToList(), ["notes"] = asset.Notes,
            });
        }
    }
}
