using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Rendering.Pixel.Editor
{
    /// <summary>
    /// T-004 step 1: proves the batch-mode capture path on this machine. One in-memory URP asset with a dedicated
    /// renderer and one full-screen Render Graph pass; rendered with SubmitRenderRequest into a RenderTexture and
    /// written to PNG. Run: unity run &lt;project&gt; -- -executeMethod Game.Rendering.Pixel.Editor.PixelSmokeCapture.Run [-pixelOut dir]
    /// The smoke pipeline assets are created by Editor script; QualitySettings is restored.
    /// </summary>
    public static class PixelSmokeCapture
    {
        public static void Run()
        {
            string outDir = Arg("-pixelOut") ?? "evidence/T-004/smoke";
            Directory.CreateDirectory(outDir);
            PixelPipelineScope scope = null;
            UniversalRendererData rendererData = null; UniversalRenderPipelineAsset pipeline = null; RenderTexture rt = null;
            try
            {
                var shader = Shader.Find("Hidden/Game/Pixel/Smoke");
                if (shader == null) throw new InvalidOperationException("smoke shader not found");
                rendererData = PixelPipelineAssets.CreateOrUpdateRenderer("Assets/Game/Config/Rendering/Pipeline/PixelSmoke_Renderer.asset",
                    new[] { new PixelPipelineAssets.FeatureSpec(typeof(PixelSmokeFeature), "PixelSmoke", f => ((PixelSmokeFeature)f).shader = shader) });
                pipeline = PixelPipelineAssets.CreateOrUpdatePipeline("Assets/Game/Config/Rendering/Pipeline/PixelSmoke.asset", new ScriptableRendererData[] { rendererData });
                AssetDatabase.SaveAssets();
                scope = new PixelPipelineScope(pipeline);

                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var camGo = new GameObject("SmokeCamera");
                var cam = camGo.AddComponent<Camera>();
                cam.transform.SetPositionAndRotation(new Vector3(0, 2, -8), Quaternion.Euler(10, 0, 0));
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.2f, 0.4f, 0.8f, 1f);
                cam.allowMSAA = false;
                var extra = camGo.AddComponent<UniversalAdditionalCameraData>();
                extra.renderPostProcessing = false; extra.antialiasing = AntialiasingMode.None;
                var lightGo = new GameObject("Sun"); var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional; lightGo.transform.rotation = Quaternion.Euler(50, -30, 0);
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = Color.white };

                const int w = 482, h = 272;
                rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { filterMode = FilterMode.Point };
                rt.Create();
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                bool supported = RenderPipeline.SupportsRenderRequest(cam, request);
                Debug.Log("[PixelSmoke] device=" + SystemInfo.graphicsDeviceType + " '" + SystemInfo.graphicsDeviceName + "' batch=" + Application.isBatchMode + " renderRequestSupported=" + supported);
                if (supported) RenderPipeline.SubmitRenderRequest(cam, request);
                else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }

                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(false);
                RenderTexture.active = prev;
                string path = Path.Combine(outDir, "smoke.png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Debug.Log("[PixelSmoke] wrote " + path);
                UnityEngine.Object.DestroyImmediate(tex);
            }
            finally
            {
                scope?.Dispose();
                if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            }
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
