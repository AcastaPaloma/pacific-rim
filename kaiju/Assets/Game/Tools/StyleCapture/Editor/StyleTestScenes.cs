using System.Collections.Generic;
using Game.Rendering.Pixel;
using Game.Rendering.Pixel.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Game.Tools.StyleCapture
{
    /// <summary>
    /// Builds the neutral style test scene and the haze calibration scene by Editor script (T-004 scope 5; the .unity
    /// files are never text-edited). Only boxes, a cylinder and a sphere: large simple forms from 10 m to 30 km, a
    /// ground plane, a gradient-lit curved surface, 1.8 m markers and 80 m blocks. Nothing in it is taken from the
    /// reference images. All tones are neutral greys; the palette pipeline supplies every colour.
    /// Run: unity run &lt;project&gt; -- -executeMethod Game.Tools.StyleCapture.StyleTestScenes.BuildAll
    /// </summary>
    public static class StyleTestScenes
    {
        public const string Folder = "Assets/Game/Tools/StyleCapture/Scenes";
        public const string StylePath = Folder + "/StyleTest.unity";
        public const string HazeCalPath = Folder + "/HazeCal.unity";
        public const string SurfaceShader = "Game/StyleTest/Surface";

        public const float HorizontalFovDeg = 60f;   // R-002 §5 capture protocol
        public const float NearPlane = 0.5f, FarPlane = 60000f;
        public static readonly float[] HazeCalDistances = { 200f, 500f, 1000f, 2000f, 4000f, 8000f };
        public const float HazeCalPanelSize = 0.03f;      // panel edge = 0.03 * distance (R-002 §4 c2)
        public const float HazeCalAzimuthStep = 0.09f;    // radians between pairs
        public const float HazeCalDarkAlbedo = 0.1f, HazeCalLightAlbedo = 0.9f;

        /// <summary>Where the named scene objects are, for the capture plans and the evidence (metres).</summary>
        public static readonly Vector3 Block80Near = new Vector3(0, 40, 0);
        public static readonly Vector3 Block80Far = new Vector3(220, 40, 850);
        public static readonly Vector3 MarkerNear = new Vector3(-18, 0.9f, -10);
        public static readonly Vector3 MarkerFar = new Vector3(-60, 0.9f, 447);
        public static readonly Vector3 GradientSurface = new Vector3(-230, 75, 230);

        [MenuItem("Game/Tools/Style capture/Build test scenes")]
        public static void BuildAll()
        {
            PixelPipelineAssets.EnsureFolder(Folder);
            BuildStyleScene();
            BuildHazeCalScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[StyleCapture] built " + StylePath + " and " + HazeCalPath);
        }

        static readonly Dictionary<string, Material> s_Materials = new Dictionary<string, Material>();

        static Material Mat(float value, Vector3 tint, bool unlit = false)
        {
            string key = value.ToString("F3") + "/" + tint.ToString("F2") + "/" + unlit;
            if (s_Materials.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Shader.Find(SurfaceShader)) { name = "StyleTest_" + key };
            m.SetFloat("_Value", value); m.SetVector("_TintSrgb", tint); m.SetFloat("_Unlit", unlit ? 1 : 0);
            s_Materials[key] = m;
            return m;
        }

        static GameObject Shape(PrimitiveType type, string name, Vector3 centre, Vector3 scale, Material material, Transform parent)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.position = centre; go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return go;
        }

        static GameObject Box(string name, Vector3 centre, Vector3 size, float value, Transform parent) => Shape(PrimitiveType.Cube, name, centre, size, Mat(value, Vector3.one), parent);

        static void BuildStyleScene()
        {
            s_Materials.Clear();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("StyleTestWorld").transform;
            const float form = 0.30f;   // albedo value of the neutral forms
            // Ground: one plane, 120 km across (the Unity plane is 10 m).
            Shape(PrimitiveType.Plane, "Ground", Vector3.zero, new Vector3(12000, 1, 12000), Mat(0.34f, Vector3.one), root);
            // Near field: forms at 10, 30 and 60 m from the street viewpoint.
            Box("Form_10m", new Vector3(-12, 1.5f, -140), new Vector3(4, 3, 4), form, root);
            Box("Form_30m", new Vector3(8, 4, -121), new Vector3(6, 8, 6), form, root);
            Box("Form_60m", new Vector3(-30, 7, -95), new Vector3(10, 14, 8), form, root);
            // The 80 m blocks (150 m and about 1 km from the main viewpoint) and the 1.8 m markers.
            Box("Block80_Near", Block80Near, new Vector3(30, 80, 20), form, root);
            Box("Block80_Far", Block80Far, new Vector3(30, 80, 20), form, root);
            var markerTint = new Vector3(1.0f, 0.25f, 0.2f);   // saturated: the palette's accent where it has one
            Shape(PrimitiveType.Cube, "Marker1p8_Near", MarkerNear, new Vector3(1.6f, 1.8f, 0.4f), Mat(1f, markerTint, true), root);
            Shape(PrimitiveType.Cube, "Marker1p8_Far", MarkerFar, new Vector3(1.6f, 1.8f, 0.4f), Mat(1f, markerTint, true), root);
            // Mid field.
            Box("Slab_700m", new Vector3(-420, 30, 380), new Vector3(300, 60, 40), form, root);
            Box("Tower_600m", new Vector3(330, 80, 420), new Vector3(40, 160, 40), form, root);
            // The gradient-lit large surface: a pale cylinder whose shading turns smoothly from sun to shade, and a dome.
            Shape(PrimitiveType.Cylinder, "GradientSurface", GradientSurface, new Vector3(220, 75, 220), Mat(0.80f, Vector3.one), root);
            Shape(PrimitiveType.Sphere, "Dome_2km", new Vector3(700, 0, 1900), new Vector3(360, 360, 360), Mat(0.55f, Vector3.one), root);
            // Depth layers at about 1, 3, 6, 10 and 30 km.
            Box("Layer_1km", new Vector3(-150, 60, 900), new Vector3(260, 120, 60), form, root);
            Box("Layer_3km_A", new Vector3(-900, 130, 2850), new Vector3(1200, 260, 300), form, root);
            Box("Layer_3km_B", new Vector3(900, 190, 3100), new Vector3(600, 380, 200), form, root);
            Box("Layer_6km", new Vector3(-4200, 250, 6000), new Vector3(2500, 500, 500), form, root);
            Box("Layer_10km_A", new Vector3(1500, 350, 9800), new Vector3(5000, 700, 800), form, root);
            Box("Layer_10km_B", new Vector3(-3500, 500, 10200), new Vector3(3000, 1000, 600), form, root);
            Box("Layer_30km_A", new Vector3(-2000, 1100, 30000), new Vector3(20000, 2200, 2000), form, root);
            Box("Layer_30km_B", new Vector3(9000, 1600, 29500), new Vector3(9000, 3200, 2000), form, root);
            AddCameras(new Vector3(0, 70, -150), Quaternion.identity);
            EditorSceneManager.SaveScene(scene, StylePath);
        }

        static void BuildHazeCalScene()
        {
            s_Materials.Clear();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("HazeCalWorld").transform;
            // Unlit panels: the written value is the sRGB encoding of the linear albedo.
            float dark = Mathf.LinearToGammaSpace(HazeCalDarkAlbedo), light = Mathf.LinearToGammaSpace(HazeCalLightAlbedo);
            for (int i = 0; i < HazeCalDistances.Length; i++)
            {
                float d = HazeCalDistances[i], size = HazeCalPanelSize * d;
                float azimuth = (i - (HazeCalDistances.Length - 1) * 0.5f) * HazeCalAzimuthStep;
                var rotation = Quaternion.Euler(0, azimuth * Mathf.Rad2Deg, 0);
                Vector3 centre = rotation * new Vector3(0, 0, d);
                Vector3 right = rotation * Vector3.right;
                var a = Shape(PrimitiveType.Quad, "Pair" + (int)d + "m_Dark", centre - right * size * 0.5f, new Vector3(size, size, 1), Mat(dark, Vector3.one, true), root);
                var b = Shape(PrimitiveType.Quad, "Pair" + (int)d + "m_Light", centre + right * size * 0.5f, new Vector3(size, size, 1), Mat(light, Vector3.one, true), root);
                a.transform.rotation = rotation; b.transform.rotation = rotation;   // the quad faces -Z: toward the camera at the origin
            }
            AddCameras(Vector3.zero, Quaternion.identity);
            EditorSceneManager.SaveScene(scene, HazeCalPath);
        }

        public static float VerticalFovDeg(float aspect) => 2f * Mathf.Atan(Mathf.Tan(HorizontalFovDeg * 0.5f * Mathf.Deg2Rad) / aspect) * Mathf.Rad2Deg;

        /// <summary>The world camera (pixel renderer, binder) and the present camera (present renderer, nothing to cull).</summary>
        static void AddCameras(Vector3 position, Quaternion rotation)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PixelPipelineCatalog>(PixelPipelineInstaller.CatalogPath);
            if (catalog == null) throw new System.InvalidOperationException("run PixelPipelineInstaller.InstallAll first");

            var worldGo = new GameObject("PixelWorldCamera");
            worldGo.transform.SetPositionAndRotation(position, rotation);
            var world = worldGo.AddComponent<Camera>();
            world.fieldOfView = VerticalFovDeg(16f / 9f); world.nearClipPlane = NearPlane; world.farClipPlane = FarPlane;
            world.clearFlags = CameraClearFlags.SolidColor; world.backgroundColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            world.allowMSAA = false; world.allowHDR = true; world.depth = -10; world.useOcclusionCulling = false;
            var worldData = worldGo.AddComponent<UniversalAdditionalCameraData>();
            worldData.SetRenderer(catalog.worldRendererIndex);
            worldData.renderPostProcessing = false; worldData.antialiasing = AntialiasingMode.None;
            worldData.renderShadows = false; worldData.requiresDepthTexture = true; worldData.requiresColorTexture = false;
            var binder = worldGo.AddComponent<PixelCameraBinder>();
            binder.catalog = catalog;
            binder.profile = catalog.FindProfile(catalog.LoadSettings().defaultProfile);

            var presentGo = new GameObject("PixelPresentCamera");
            var present = presentGo.AddComponent<Camera>();
            present.cullingMask = 0; present.clearFlags = CameraClearFlags.SolidColor; present.backgroundColor = Color.black;
            present.allowMSAA = false; present.allowHDR = false; present.depth = 10; present.useOcclusionCulling = false;
            var presentData = presentGo.AddComponent<UniversalAdditionalCameraData>();
            presentData.SetRenderer(catalog.presentRendererIndex);
            presentData.renderPostProcessing = false; presentData.antialiasing = AntialiasingMode.None; presentData.renderShadows = false;
            presentData.requiresDepthTexture = false; presentData.requiresColorTexture = false;
            presentGo.AddComponent<PixelPresentCamera>().source = binder;
        }
    }
}
