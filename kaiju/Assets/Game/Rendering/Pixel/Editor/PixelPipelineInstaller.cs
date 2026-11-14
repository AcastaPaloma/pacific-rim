using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Rendering.Pixel.Editor
{
    /// <summary>
    /// Creates the dedicated pixel pipeline assets by Editor script (reconciled spec C15): a URP asset with two
    /// renderers (world: one feature per stage in the given order; present: the present feature) and the catalog the
    /// runtime uses to find them. The template PC_RPAsset / PC_Renderer are never touched.
    /// Run: unity run &lt;project&gt; -- -executeMethod Game.Rendering.Pixel.Editor.PixelPipelineInstaller.InstallAll
    /// </summary>
    public static class PixelPipelineInstaller
    {
        public const string ConfigFolder = "Assets/Game/Config/Rendering";
        public const string PipelineFolder = ConfigFolder + "/Pipeline";
        public const string PipelinePath = PipelineFolder + "/PixelPipeline.asset";
        public const string WorldRendererPath = PipelineFolder + "/PixelWorld_Renderer.asset";
        public const string PresentRendererPath = PipelineFolder + "/PixelPresent_Renderer.asset";
        public const string CatalogPath = ConfigFolder + "/PixelPipelineCatalog.asset";
        public const string SettingsPath = ConfigFolder + "/PixelPipeline.json";

        static readonly Dictionary<PixelStageId, (Type type, string shader)> s_Stages = new Dictionary<PixelStageId, (Type, string)>
        {
            { PixelStageId.Downsample, (typeof(PixelDownsampleFeature), "Hidden/Game/Pixel/Downsample") },
            { PixelStageId.Dither, (typeof(PixelDitherFeature), "Hidden/Game/Pixel/Dither") },
            { PixelStageId.Haze, (typeof(PixelHazeFeature), "Hidden/Game/Pixel/Haze") },
            { PixelStageId.Outline, (typeof(PixelOutlineFeature), "Hidden/Game/Pixel/Outline") },
            { PixelStageId.Quantise, (typeof(PixelQuantiseFeature), "Hidden/Game/Pixel/Quantise") },
            { PixelStageId.Store, (typeof(PixelStoreFeature), "Hidden/Game/Pixel/Store") },
            { PixelStageId.Present, (typeof(PixelPresentFeature), "Hidden/Game/Pixel/Present") },
        };

        [MenuItem("Game/Rendering/Pixel/Install pipeline assets (default order)")]
        public static void InstallAll()
        {
            PaletteProfileGenerator.Generate();
            Install(PixelStageOrder.Default);
            AssetDatabase.SaveAssets();
            Debug.Log("[Pixel] installed pipeline assets in " + PipelineFolder + " with order " + string.Join(", ", PixelStageOrder.Default));
        }

        /// <summary>Installs the default asset set with the given stage order. Throws on an illegal order: nothing is written.</summary>
        public static PixelPipelineCatalog Install(IReadOnlyList<PixelStageId> order) => Install(order, PipelinePath, WorldRendererPath, PresentRendererPath, CatalogPath);

        public static PixelPipelineCatalog Install(IReadOnlyList<PixelStageId> order, string pipelinePath, string worldPath, string presentPath, string catalogPath)
        {
            string error = PixelStageOrder.Validate(order);
            if (error != null) throw new ArgumentException("illegal pixel stage order: " + error);
            bool hasStore = false, hasPresent = false;
            foreach (var s in order) { hasStore |= s == PixelStageId.Store; hasPresent |= s == PixelStageId.Present; }
            if (!hasStore || !hasPresent) throw new ArgumentException("illegal pixel stage order: Store and Present are required");

            var worldFeatures = new List<PixelPipelineAssets.FeatureSpec>();
            foreach (var stage in order)
                if (stage != PixelStageId.Present) worldFeatures.Add(Spec(stage));
            var world = PixelPipelineAssets.CreateOrUpdateRenderer(worldPath, worldFeatures, ConfigureRenderer);
            var present = PixelPipelineAssets.CreateOrUpdateRenderer(presentPath, new[] { Spec(PixelStageId.Present) }, ConfigureRenderer);
            var pipeline = PixelPipelineAssets.CreateOrUpdatePipeline(pipelinePath, new ScriptableRendererData[] { world, present });

            PixelPipelineAssets.EnsureFolder(System.IO.Path.GetDirectoryName(catalogPath).Replace('\\', '/'));
            var catalog = AssetDatabase.LoadAssetAtPath<PixelPipelineCatalog>(catalogPath);
            if (catalog == null) { catalog = ScriptableObject.CreateInstance<PixelPipelineCatalog>(); AssetDatabase.CreateAsset(catalog, catalogPath); }
            catalog.pipeline = pipeline; catalog.worldRendererIndex = 0; catalog.presentRendererIndex = 1;
            catalog.settingsJson = AssetDatabase.LoadAssetAtPath<TextAsset>(SettingsPath);
            var profiles = new List<PaletteProfile>();
            foreach (string guid in AssetDatabase.FindAssets("t:PaletteProfile", new[] { PaletteProfileGenerator.ProfilesFolder }))
                profiles.Add(AssetDatabase.LoadAssetAtPath<PaletteProfile>(AssetDatabase.GUIDToAssetPath(guid)));
            profiles.Sort((a, b) => string.CompareOrdinal(a.profileId, b.profileId));
            catalog.profiles = profiles.ToArray();
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        static PixelPipelineAssets.FeatureSpec Spec(PixelStageId stage)
        {
            var (type, shaderName) = s_Stages[stage];
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException("shader not found: " + shaderName);
            return new PixelPipelineAssets.FeatureSpec(type, "Pixel" + stage, f =>
            {
                if (f is PixelStageFeature s) s.shader = shader; else ((PixelPresentFeature)f).shader = shader;
            });
        }

        static void ConfigureRenderer(UniversalRendererData data)
        {
            var so = new SerializedObject(data);
            so.FindProperty("m_RenderingMode").intValue = (int)RenderingMode.Forward;
            so.FindProperty("m_DepthPrimingMode").intValue = (int)DepthPrimingMode.Disabled;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>The stage order currently installed on a world renderer followed by Present (read back from the asset).</summary>
        public static List<PixelStageId> ReadOrder(ScriptableRendererData world)
        {
            var order = new List<PixelStageId>();
            foreach (var feature in world.rendererFeatures)
                if (feature is PixelStageFeature stage) order.Add(stage.Stage);
            order.Add(PixelStageId.Present);
            return order;
        }
    }
}
