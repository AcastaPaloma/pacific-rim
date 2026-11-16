// T-147: builds the capture scene for one Bay tile: the tile through World.Geo (floating origin = tile corner, so scene = tile-local metres),
// one fixed low sun (same as the T-006 style frame), the pixel cameras and the ThirdPersonRig. Saved under Generated/ (git-ignored) so a
// StyleCaptureSession can open it. No file of StyleCapture or StyleFrame is edited; the camera set-up mirrors StyleFrameScene.AddPixelCameras.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Cameras;
using Game.Rendering.Pixel;
using Game.Rendering.Pixel.Editor;
using Game.Tools.StyleCapture;
using Game.World.Geo;
using Game.World.Geo.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Game.Tools.Capture
{
    public sealed class CaptureTile
    {
        public string Name, ScenePath;
        public GeoTileInstance Instance;
        public double OriginX, OriginZ;
        public Func<double, double, double> GroundRender;   // tile-local x, z -> render-surface height (NAVD88 m)
    }

    public static class CaptureScene
    {
        public const string Dir = "Assets/Game/Tools/Capture/Generated";
        // Same light as StyleFrameShots (sun from compass azimuth 195, elevation 15 deg): the capture looks like the style frames.
        public const float SunElevationDeg = 15f, SunTravelYawDeg = 15f;

        public static bool TileAvailable(string tile) => GeoTileId.TryParse(tile, out var id) && Directory.Exists(GeoTileBuilder.TileDirectory(id));

        public static CaptureTile Build(string tile, string profile, IList<string> hideBids = null)
        {
            if (!GeoTileId.TryParse(tile, out var id)) throw new ArgumentException("bad tile id " + tile);
            string dir = GeoTileBuilder.TileDirectory(id);
            if (!Directory.Exists(dir)) throw new DirectoryNotFoundException("tile folder missing: " + dir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var inst = GeoTileBuilder.Build(dir, new GeoBuildOptions { FloatingOriginX = id.OriginX, FloatingOriginZ = id.OriginZ, Settings = GeoTileBuilder.LoadSettings(), VerifyHashes = true });
            var hf = inst.Model.Context.Heights;
            // structures hidden from rendering (the observer's own tower at a deck viewpoint) are excluded before the scene is saved: a reopened scene has no live tile model
            if (hideBids != null && hideBids.Count > 0) inst.SetRenderExclusion(hideBids.Select(BuildingsLayer.ParseBid).ToArray());
            if (StableDrawOrder) StabiliseDrawOrder(inst);
            if (TerrainSinkM > 0f) SinkTerrain(inst, TerrainSinkM);
            var sun = new GameObject("Sun");
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1f; light.shadows = LightShadows.None;
            sun.transform.rotation = Quaternion.Euler(SunElevationDeg, SunTravelYawDeg, 0f);
            AddPixelCameras(profile);
            Directory.CreateDirectory(Dir);
            string path = Dir + "/CaptureBay_" + tile + (hideBids != null && hideBids.Count > 0 ? "_h" + (string.Join(",", hideBids.OrderBy(x => x, StringComparer.Ordinal)).GetHashCode() & 0xffff).ToString("x4") : "") + ".unity";
            if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException("could not save " + path);
            return new CaptureTile { Name = tile, ScenePath = path, Instance = inst, OriginX = id.OriginX, OriginZ = id.OriginZ, GroundRender = (x, z) => hf.SampleRender(x, z) };
        }

        /// <summary>T-194b: opaque surfaces that are exactly coplanar (a building footprint on terrain) tie in the depth test, and the draw order that decides the tie followed per-process renderer registration, so two Unity processes
        /// rendered one pixel differently. Give every renderer a unique, name-derived Renderer.rendererPriority (terrain, water, building chunks, then the rest, each by path) so the draw order never depends on the process.
        /// Opt-in (flag tools.capture.stable_draw_order or -captureStableOrder), off by default: the renderer-priority part alone did NOT remove the cross-process difference (4 processes, 2 outcomes); the render-queue tier is untested.</summary>
        public static bool StableDrawOrder;
        /// <summary>T-194c fallback (URP Lit has no polygon Offset): with StableDrawOrder the building and landmark renderers are lifted by this many metres so their footprint face is no longer exactly coplanar with the terrain (no depth tie). 0 turns the lift off (-captureLiftM overrides).</summary>
        public static float BuildingLiftMeters = 0.02f;
        /// <summary>T-194c (UNVERIFIED, no Unity run was left): the queue tier did not remove the 2 outcomes (7 flagged processes: 5 x c570, 2 x b6e6), so the tie is not decided by draw order. This lowers every terrain renderer by this many metres (-captureTerrainSink 0.02) so building footprints sit strictly above the terrain and the depth test no longer ties. Capture scene only; T-194d: default 0.02 (verified 3/3 processes, T-204), 0 = off.</summary>
        public static float TerrainSinkM = 0.02f;
        static void SinkTerrain(GeoTileInstance inst, float metres)
        {
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                if (r.transform.parent != null && r.transform.parent.name == "Terrain") r.transform.position += Vector3.down * metres;
        }
        static string GroupKey(string path) => (path.Contains("/Terrain/") ? "0" : path.Contains("/Water/") ? "1" : path.Contains("/BuildingChunks/") ? "2" : "3") + path;
        static void StabiliseDrawOrder(GeoTileInstance inst)
        {
            string P(Transform t) { var s = t.name; while (t.parent != null) { t = t.parent; s = t.name + "/" + s; } return s; }
            var rs = new List<Renderer>(inst.GetComponentsInChildren<Renderer>(true));
            rs.Sort((a, b) => string.CompareOrdinal(GroupKey(P(a.transform)), GroupKey(P(b.transform))));
            for (int i = 0; i < rs.Count; i++) rs[i].rendererPriority = i;
            // the sort may not honour the priority for opaque queues: terrain, then buildings (and landmarks, which share that material) are also separated by render queue, which it always honours
            var lifted = new HashSet<Transform>();
            foreach (var r in rs) { var m = r.sharedMaterial; if (m == null) continue; string path = P(r.transform); if (path.Contains("/BuildingChunks/") || path.Contains("/Landmarks/")) { m.renderQueue = 2001; if (BuildingLiftMeters > 0f && !lifted.Contains(r.transform)) { lifted.Add(r.transform); r.transform.position += Vector3.up * BuildingLiftMeters; } } }
        }

        static void AddPixelCameras(string profile)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PixelPipelineCatalog>(PixelPipelineInstaller.CatalogPath) ?? throw new InvalidOperationException("pixel pipeline assets missing: run PixelPipelineInstaller.InstallAll");
            var prof = catalog.FindProfile(profile) ?? throw new ArgumentException("unknown palette profile " + profile);
            var worldGo = new GameObject("PixelWorldCamera");
            var world = worldGo.AddComponent<Camera>();
            world.fieldOfView = StyleTestScenes.VerticalFovDeg(16f / 9f); world.nearClipPlane = StyleTestScenes.NearPlane; world.farClipPlane = StyleTestScenes.FarPlane;
            world.clearFlags = CameraClearFlags.SolidColor; world.backgroundColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            world.allowMSAA = false; world.allowHDR = true; world.depth = -10; world.useOcclusionCulling = false;
            var wd = worldGo.AddComponent<UniversalAdditionalCameraData>();
            wd.SetRenderer(catalog.worldRendererIndex);
            wd.renderPostProcessing = false; wd.antialiasing = AntialiasingMode.None;
            wd.renderShadows = false; wd.requiresDepthTexture = true; wd.requiresColorTexture = false;
            var binder = worldGo.AddComponent<PixelCameraBinder>();
            binder.catalog = catalog; binder.profile = prof;
            var presentGo = new GameObject("PixelPresentCamera");
            var present = presentGo.AddComponent<Camera>();
            present.cullingMask = 0; present.clearFlags = CameraClearFlags.SolidColor; present.backgroundColor = Color.black;
            present.allowMSAA = false; present.allowHDR = false; present.depth = 10; present.useOcclusionCulling = false;
            var pd = presentGo.AddComponent<UniversalAdditionalCameraData>();
            pd.SetRenderer(catalog.presentRendererIndex);
            pd.renderPostProcessing = false; pd.antialiasing = AntialiasingMode.None; pd.renderShadows = false;
            pd.requiresDepthTexture = false; pd.requiresColorTexture = false;
            presentGo.AddComponent<PixelPresentCamera>().source = binder;
            var rig = worldGo.AddComponent<ThirdPersonRig>();
            rig.settingsJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/Camera/ThirdPersonRig.json");
        }
    }
}
