// Unstyled captures of a built tile (T-003 evidence): top-down, oblique x3, shoreline, skyline V4/V5
// (+ masks, 270-row, 70 m eye), z-fighting distance series, per-view draw calls and triangles.
using System;
using System.Collections.Generic;
using System.IO;
using Game.World.Geo.Unity;
using UnityEditor;
using UnityEngine;

namespace Game.World.Geo.Editor
{
    public static class GeoCaptures
    {
        static readonly Color Sky = new Color(0.72f, 0.80f, 0.88f);

        sealed class View
        {
            public string Name; public string Hide; public Vector3 Pos; public Quaternion Rot; public float VFov; public int W, H; public bool Ortho; public float OrthoSize;
        }

        static float VFov(float hfovDeg, int w, int h) => 2f * Mathf.Rad2Deg * Mathf.Atan(Mathf.Tan(hfovDeg * 0.5f * Mathf.Deg2Rad) * h / w);

        static Vector3 Scene(GeoTileInstance t, double x, double y, double z) =>
            new Vector3((float)(x - t.FloatingOriginX), (float)y, (float)(z - t.FloatingOriginZ));

        public static void All(GeoTileInstance t, string outDir, Dictionary<string, object> stats, Action<string> log)
        {
            _tile = t; _hidden = null;
            var cdir = Path.Combine(outDir, "captures");
            Directory.CreateDirectory(cdir);
            var lightGo = new GameObject("Sun");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.1f; light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(42f, 150f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.5f);
            var camGo = new GameObject("CaptureCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Sky;
            cam.nearClipPlane = 0.5f; cam.farClipPlane = 30000f; cam.allowMSAA = false; cam.allowHDR = false;

            double cx = t.Model.Context.OriginX + 1024, cz = t.Model.Context.OriginZ + 1024;
            var views = new List<View>
            {
                new View { Name = "topdown", Pos = Scene(t, cx, 2000, cz), Rot = Quaternion.LookRotation(Vector3.down, Vector3.forward), Ortho = true, OrthoSize = 1024, W = 2048, H = 2048 },
            };
            // oblique from the Bay at three bearings, looking at the downtown waterfront
            var target = Scene(t, 62300, 40, 74600);
            foreach (var bearing in new[] { 270f, 235f, 305f })
            {
                var dir = Quaternion.Euler(0, bearing, 0) * Vector3.forward;            // camera looks along bearing
                var pos = target - dir * 2600f + Vector3.up * 700f;
                views.Add(new View { Name = "oblique_" + (int)bearing, Pos = pos, Rot = Quaternion.LookRotation(target - pos), VFov = VFov(60, 1600, 900), W = 1600, H = 900 });
            }
            var shore = Scene(t, 62700, 2, 74760);
            var spos = shore + new Vector3(260f, 70f, -120f);
            views.Add(new View { Name = "shoreline_embarcadero", Pos = spos, Rot = Quaternion.LookRotation(shore - spos), VFov = VFov(60, 1600, 900), W = 1600, H = 900 });

            // skyline viewpoints (R-004 §6.7): eye heights NAVD88, hFOV 60, 1024 x 576
            var lm = GeoJson.Obj(GeoJson.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Geo", "global", "v1", "landmarks.json"))));
            var sky = new List<View>();
            foreach (var vo in GeoJson.Arr(GeoJson.Get(lm, "viewpoints")))
            {
                var v = GeoJson.Obj(vo);
                if (GeoJson.Str(v, "tile") != t.TileName) continue;
                var p = GeoJson.Arr(GeoJson.Get(v, "baytm"));
                double eye = GeoJson.Num(v, "eye_navd88_m");
                float heading = (float)GeoJson.Num(v, "heading_deg");
                string idv = GeoJson.Str(v, "id");
                string hide = idv == "V5" ? "osm:w28824850" : null;   // the observer stands on Coit Tower: its own structure is not skyline
                sky.Add(new View { Hide = hide, Name = "skyline_" + idv, Pos = Scene(t, (double)p[0], eye, (double)p[1]), Rot = Quaternion.Euler(0, heading, 0), VFov = VFov(60, 1024, 576), W = 1024, H = 576 });
                sky.Add(new View { Hide = hide, Name = "skyline_" + idv + "_270rows", Pos = Scene(t, (double)p[0], eye, (double)p[1]), Rot = Quaternion.Euler(0, heading, 0), VFov = VFov(60, 480, 270), W = 480, H = 270 });
                if (idv == "V4")
                    sky.Add(new View { Name = "skyline_V4_eye70", Pos = Scene(t, (double)p[0], 70.0, (double)p[1]), Rot = Quaternion.Euler(0, heading, 0), VFov = VFov(60, 1024, 576), W = 1024, H = 576 });
            }
            views.AddRange(sky);

            // synchronous shader compilation, then a warm-up frame: async compilation would drop not-yet-compiled variants
            ShaderUtil.allowAsyncCompilation = false;
            Place(cam, views[0]);
            UnityEngine.Object.DestroyImmediate(Render(cam, 64, 64));
            var viewStats = new List<object>();
            foreach (var v in views)
            {
                Place(cam, v);
                var img = Render(cam, v.W, v.H);
                File.WriteAllBytes(Path.Combine(cdir, v.Name + ".png"), img.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(img);
                viewStats.Add(ViewStats(t, cam, v.Name));
            }
            // sky masks: everything non-sky white on black (M-005.7 game masks)
            var white = GeoMaterials.Make(Color.white, null, "mask");
            white.shader = Shader.Find("Universal Render Pipeline/Unlit") ?? white.shader;
            white.SetColor("_BaseColor", Color.white);
            var saved = new Dictionary<Renderer, Material>();
            foreach (var r in t.Renderers) { saved[r] = r.sharedMaterial; r.sharedMaterial = white; }
            cam.backgroundColor = Color.black;
            foreach (var v in sky)
            {
                Place(cam, v);
                var img = Render(cam, v.W, v.H);
                File.WriteAllBytes(Path.Combine(cdir, v.Name + "_mask.png"), img.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(img);
            }
            foreach (var kv in saved) kv.Key.sharedMaterial = kv.Value;
            if (_hidden != null) { t.SetRenderExclusion(null); _hidden = null; }
            cam.backgroundColor = Sky;

            // z-fighting distance series: two renders 1 mm apart; z-fighting shows as pixels that flip
            var zf = new List<object>();
            var zt = Scene(t, 62640, 3, 74690);
            foreach (var d in new[] { 10f, 100f, 1000f, 3000f })
            {
                var dir = Quaternion.Euler(18f, 250f, 0) * Vector3.forward;
                var pos = zt - dir * d;
                var v = new View { Name = "zfight_" + (int)d + "m", Pos = pos, Rot = Quaternion.LookRotation(zt - pos), VFov = VFov(60, 1280, 720), W = 1280, H = 720 };
                Place(cam, v);
                var a = Render(cam, v.W, v.H);
                cam.transform.position = pos + new Vector3(0.001f, 0.0005f, -0.001f);
                var b = Render(cam, v.W, v.H);
                File.WriteAllBytes(Path.Combine(cdir, v.Name + ".png"), a.EncodeToPNG());
                var pa = a.GetPixels32(); var pb = b.GetPixels32();
                int flips = 0;
                for (int i = 0; i < pa.Length; i++) if (Math.Abs(pa[i].r - pb[i].r) + Math.Abs(pa[i].g - pb[i].g) + Math.Abs(pa[i].b - pb[i].b) > 24) flips++;
                // 1/4-resolution nearest-neighbour downsample (pixel-pipeline proxy) of both frames
                int lw = v.W / 4, lh = v.H / 4, lflips = 0;
                for (int y = 0; y < lh; y++)
                    for (int x = 0; x < lw; x++)
                    {
                        int i = (y * 4) * v.W + x * 4;
                        if (Math.Abs(pa[i].r - pb[i].r) + Math.Abs(pa[i].g - pb[i].g) + Math.Abs(pa[i].b - pb[i].b) > 24) lflips++;
                    }
                zf.Add(new Dictionary<string, object> { ["distance_m"] = (double)d, ["flip_fraction"] = (double)flips / pa.Length, ["flip_fraction_quarter_res"] = (double)lflips / (lw * lh), ["camera_shift_m"] = 0.0015 });
                log($"zfight {d} m: flips {flips} / {pa.Length}, quarter-res {lflips}");
                UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b);
            }
            stats["captures.views"] = viewStats;
            stats["captures.zfight"] = zf;
            File.WriteAllText(Path.Combine(outDir, "export", "capture_views.json"), GeoJson.Write(new Dictionary<string, object> { ["views"] = viewStats, ["zfight"] = zf, ["camera"] = new Dictionary<string, object> { ["near_m"] = 0.5, ["far_m"] = 30000.0, ["reversed_z"] = SystemInfo.usesReversedZBuffer, ["graphics"] = SystemInfo.graphicsDeviceType.ToString() } }) + "\n");
            log("captures: " + views.Count + " views + " + sky.Count + " masks + z-fight series");
        }

        static GeoTileInstance _tile;
        static string _hidden;

        static void Place(Camera cam, View v)
        {
            if (_tile != null && v.Hide != _hidden)
            {
                _tile.SetRenderExclusion(v.Hide == null ? null : new[] { BuildingsLayer.ParseBid(v.Hide) });
                _hidden = v.Hide;
            }
            cam.transform.SetPositionAndRotation(v.Pos, v.Rot);
            cam.orthographic = v.Ortho;
            if (v.Ortho) cam.orthographicSize = v.OrthoSize; else cam.fieldOfView = v.VFov;
            cam.aspect = (float)v.W / v.H;
        }

        public static Texture2D Render(Camera cam, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, 1);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        /// <summary>Renderers whose bounds intersect the view frustum = unbatched draw calls (one material, one submesh each).</summary>
        static Dictionary<string, object> ViewStats(GeoTileInstance t, Camera cam, string name)
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            int dc = 0; long tris = 0;
            foreach (var r in t.Renderers)
            {
                if (r == null || !r.enabled || !GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                dc += mf.sharedMesh.subMeshCount;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) tris += mf.sharedMesh.GetIndexCount(s) / 3;
            }
            return new Dictionary<string, object>
            {
                ["view"] = name, ["draw_calls_visible_renderers"] = dc, ["triangles_submitted"] = tris,
                ["note"] = "unbatched: one draw call per visible renderer (single material, single submesh); SRP batcher reduces set-pass cost, not this count",
            };
        }
    }
}
