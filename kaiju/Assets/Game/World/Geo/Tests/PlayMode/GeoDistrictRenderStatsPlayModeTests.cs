// T-021 step 4b / M-018: render budget at district scale (15 tiles). Same protocol as GeoRenderStatsPlayModeTests:
// Play mode, graphics on, 1920x1080 RT, soft-shadow sun, 30 warm-up + 120 recorded frames per view; median and max. Relative numbers only.
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using Game.World.Geo.Unity;

namespace Game.World.Geo.PlayMode.Tests
{
    [Category("M018")]
    public class GeoDistrictRenderStatsPlayModeTests
    {
        static readonly string[] Counters = { "Draw Calls Count", "Batches Count", "SetPass Calls Count", "Triangles Count", "Vertices Count", "Shadow Casters Count" };

        struct View { public string Name; public string District; public Vector3 Pos; public Quaternion Rot; public float HFov; }

        static float VFov(float h) => 2f * Mathf.Rad2Deg * Mathf.Atan(Mathf.Tan(h * 0.5f * Mathf.Deg2Rad) * 1080f / 1920f);

        static string TilesRoot => Path.Combine(Application.dataPath, "StreamingAssets", "Geo", "tiles", "v1");

        // Views for one district, positions relative to the floating origin (the tile corner of 'centre').
        // Centre of a tile is (origin + 1024 m); camera frame = world minus that origin.
        static IEnumerable<View> DistrictViews(string district, GeoTileId centre, GeoTileId overviewCentre)
        {
            var target = new Vector3(1024f, 40f, 1024f);
            foreach (var b in new[] { 270f, 235f, 305f })
            {
                var dir = Quaternion.Euler(0, b, 0) * Vector3.forward;
                var pos = target - dir * 2600f + Vector3.up * 700f;
                yield return new View { Name = district + "_oblique_" + (int)b, District = district, Pos = pos, Rot = Quaternion.LookRotation(target - pos), HFov = 60 };
            }
            // High overview of the whole district, looking north-down from above its middle tile (offset from the origin tile in metres).
            var ox = (float)(overviewCentre.OriginX - centre.OriginX) + 1024f;
            var oz = (float)(overviewCentre.OriginZ - centre.OriginZ) + 1024f;
            var opos = new Vector3(ox, 9000f, oz - 4500f);
            yield return new View { Name = district + "_overview", District = district, Pos = opos, Rot = Quaternion.LookRotation(new Vector3(ox, 0f, oz) - opos), HFov = 60 };
        }

        [UnityTest]
        public IEnumerator DistrictRenderStats_FromProfilerCounters()
        {
            var ggRef = GeoTileId.From(0, 26, 37);
            var somaRef = GeoTileId.From(0, 30, 36);
            var ids = new List<GeoTileId>();
            for (int i = 25; i <= 27; i++) for (int j = 36; j <= 38; j++) ids.Add(GeoTileId.From(0, i, j));
            for (int i = 29; i <= 31; i++) for (int j = 35; j <= 36; j++) ids.Add(GeoTileId.From(0, i, j));
            var absent = GeoTileSetLoader.Absent(TilesRoot, ids);
            if (absent.Count > 0) Assert.Ignore("tile folders absent: " + string.Join(", ", absent));
            var set = GeoTileSetLoader.Load(TilesRoot, ids, verifyHashes: false);
            // Floating origin starts at the reference tile corner (GG centre tile); the builder places every other tile relative to it.
            var builder = new GeoTileSetBuilder(set, ggRef, null) { BuildingColliders = false, TerrainCollider = true };

            var tileBuild = new List<object>();
            int renderers = 0;
            while (!builder.Done)
            {
                var sw = Stopwatch.StartNew();
                var inst = builder.BuildNext();
                sw.Stop();
                renderers += inst.Renderers.Count;
                tileBuild.Add(new Dictionary<string, object> { ["tile"] = inst.name, ["build_ms"] = sw.Elapsed.TotalMilliseconds, ["renderers"] = (double)inst.Renderers.Count });
                yield return null;
            }

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(42f, 150f, 0f);
            var cam = new GameObject("GameplayCamera").AddComponent<Camera>();
            cam.nearClipPlane = 0.5f; cam.farClipPlane = 30000f; cam.clearFlags = CameraClearFlags.SolidColor;
            var rt = new RenderTexture(1920, 1080, 24);
            cam.targetTexture = rt;

            var views = new List<View>();
            views.AddRange(DistrictViews("GG", ggRef, ggRef));
            views.AddRange(DistrictViews("SoMa", somaRef, somaRef));
            var results = new List<object>();
            foreach (var v in views)
            {
                // Origin at the district's reference tile corner (SoMa views shift the origin once).
                var wantRef = v.District == "GG" ? ggRef : somaRef;
                if (builder.FloatingOriginX != wantRef.OriginX || builder.FloatingOriginZ != wantRef.OriginZ)
                { builder.ShiftOrigin(wantRef.OriginX, wantRef.OriginZ); yield return null; }
                cam.transform.SetPositionAndRotation(v.Pos, v.Rot);
                cam.fieldOfView = VFov(v.HFov);
                var recs = new List<ProfilerRecorder>();
                foreach (var c in Counters) recs.Add(ProfilerRecorder.StartNew(ProfilerCategory.Render, c));
                for (int f = 0; f < 30; f++) yield return null;
                var series = new Dictionary<string, List<double>>();
                foreach (var c in Counters) series[c] = new List<double>();
                var dt = new List<double>();
                for (int f = 0; f < 120; f++)
                {
                    yield return null;
                    for (int i = 0; i < Counters.Length; i++) series[Counters[i]].Add(recs[i].LastValue);
                    dt.Add(Time.unscaledDeltaTime * 1000.0);
                }
                var row = new Dictionary<string, object> { ["view"] = v.Name, ["district"] = v.District, ["hfov_deg"] = (double)v.HFov };
                foreach (var kv in series)
                {
                    kv.Value.Sort();
                    row[kv.Key + ".median"] = kv.Value[kv.Value.Count / 2];
                    row[kv.Key + ".max"] = kv.Value[kv.Value.Count - 1];
                }
                dt.Sort();
                row["frame_ms_median_editor_batch"] = dt[dt.Count / 2];
                row["frame_ms_max_editor_batch"] = dt[dt.Count - 1];
                foreach (var r in recs) r.Dispose();
                results.Add(row);
            }

            var outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "evidence", "T-021");
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "m018_district_render_stats.json"), GeoJson.Write(new Dictionary<string, object>
            {
                ["protocol"] = "M-018 at district scale: Play mode (PlayMode test), graphics on, 1920x1080 target, soft-shadow sun, 30 warm-up + 120 recorded frames per view, ProfilerRecorder Render counters (LastValue per frame); GG i25-27 j36-38 + SoMa i29-31 j35-36 loaded together",
                ["tiles_loaded"] = (double)builder.Instances.Count, ["renderers_loaded"] = (double)renderers,
                ["tile_build"] = tileBuild, ["views"] = results,
                ["note"] = "relative numbers on the dev Mac (editor batch, no target PC); counters are frame totals over every pass the engine rendered",
            }) + "\n");

            cam.targetTexture = null; rt.Release();
            Object.Destroy(rt);
            Object.Destroy(cam.gameObject);
            Object.Destroy(sun.gameObject);
            Object.Destroy(builder.Root);
            yield return null;
            Assert.AreEqual(ids.Count, builder.Instances.Count);
            Assert.Greater((double)((Dictionary<string, object>)results[0])["Draw Calls Count.max"], 0.0, "profiler counters must report draw calls with a graphics device");
        }
    }
}
