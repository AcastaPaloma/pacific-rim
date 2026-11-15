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
