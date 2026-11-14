// R-006 §4 / DEF-T003-perf-1: draw calls from the engine's Rendering profiler counters, in Play mode, graphics on.
// 1920x1080 gameplay camera, main light with soft shadows, 30 warm-up + 120 recorded frames per view; median and max.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.World.Geo.PlayMode.Tests
{
    public class GeoRenderStatsPlayModeTests
    {
        static readonly string[] Counters = { "Draw Calls Count", "Batches Count", "SetPass Calls Count", "Triangles Count", "Vertices Count", "Shadow Casters Count" };

        struct View { public string Name; public Vector3 Pos; public Quaternion Rot; public float HFov; }

        static float VFov(float h) => 2f * Mathf.Rad2Deg * Mathf.Atan(Mathf.Tan(h * 0.5f * Mathf.Deg2Rad) * 1080f / 1920f);

        [UnityTest]
        public IEnumerator TileRenderStats_FromProfilerCounters()
        {
            var id = GeoTileId.From(0, 30, 36);
            var tile = Unity.GeoTileBuilder.Build(Unity.GeoTileBuilder.TileDirectory(id), new Unity.GeoBuildOptions { FloatingOriginX = id.OriginX, FloatingOriginZ = id.OriginZ });
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(42f, 150f, 0f);
            var cam = new GameObject("GameplayCamera").AddComponent<Camera>();
            cam.nearClipPlane = 0.5f; cam.farClipPlane = 30000f; cam.clearFlags = CameraClearFlags.SolidColor;
            var rt = new RenderTexture(1920, 1080, 24);
            cam.targetTexture = rt;
            Vector3 S(double x, double y, double z) => new Vector3((float)(x - id.OriginX), (float)y, (float)(z - id.OriginZ));
            var views = new List<View>();
            var target = S(62300, 40, 74600);
            foreach (var b in new[] { 270f, 235f, 305f })
            {
                var dir = Quaternion.Euler(0, b, 0) * Vector3.forward;
                var pos = target - dir * 2600f + Vector3.up * 700f;
                views.Add(new View { Name = "oblique_" + (int)b, Pos = pos, Rot = Quaternion.LookRotation(target - pos), HFov = 60 });
            }
            views.Add(new View { Name = "V4_v1.1", Pos = S(63300, 10, 74900), Rot = Quaternion.Euler(0, 257.6f, 0), HFov = 75 });
            views.Add(new View { Name = "V5", Pos = S(61591.4, 146, 75450.7), Rot = Quaternion.Euler(0, 170f, 0), HFov = 60 });
            var results = new List<object>();
            foreach (var v in views)
            {
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
                var row = new Dictionary<string, object> { ["view"] = v.Name, ["hfov_deg"] = (double)v.HFov };
                foreach (var kv in series)
                {
                    kv.Value.Sort();
                    row[kv.Key + ".median"] = kv.Value[kv.Value.Count / 2];
                    row[kv.Key + ".max"] = kv.Value[kv.Value.Count - 1];
                }
                dt.Sort();
                row["frame_ms_median_editor_batch"] = dt[dt.Count / 2];
                foreach (var r in recs) r.Dispose();
                results.Add(row);
            }
            var outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "evidence", "T-003", "export");
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "render_stats_playmode.json"), GeoJson.Write(new Dictionary<string, object>
            {
                ["protocol"] = "R-006 §4: Play mode (Unity Test Framework PlayMode), graphics device on (Metal), 1920x1080 target, main light soft shadows, 30 warm-up + 120 recorded frames per view, ProfilerRecorder Render counters (LastValue per frame)",
                ["renderers_in_tile"] = tile.Renderers.Count, ["views"] = results,
                ["note"] = "frame time is an editor batch measurement on the dev Mac (no target PC, Q-019); counters are frame totals over every pass the engine rendered",
            }) + "\n");
            cam.targetTexture = null; rt.Release();
            Object.Destroy(tile.gameObject);
            var first = (Dictionary<string, object>)results[0];
            Assert.Greater((double)first["Draw Calls Count.max"], 0.0, "profiler counters must report draw calls with a graphics device");
            foreach (Dictionary<string, object> r in results) Assert.LessOrEqual((double)r["Draw Calls Count.max"], 1500.0, (string)r["view"]);
        }
    }
}
