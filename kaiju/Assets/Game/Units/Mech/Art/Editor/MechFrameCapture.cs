using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Units.Mech.Art.Editor
{
    /// <summary>
    /// Four orthographic views of the imported prefab (front, left side, back, front-left 3/4) with a
    /// 1.8 m human at the left toe, written to evidence/T-007/raw/unity_*.png plus unity_views.json.
    /// Grid, 67 m deck and 80 m lines are drawn from unity_views.json by compose_unity.py.
    /// Uses an unsaved in-memory scene; never touches project scenes.
    ///
    ///   unity run &lt;kaiju&gt; --no-tail --log-file &lt;log&gt; --timeout 900 -- -executeMethod Game.Units.Mech.Art.Editor.MechFrameCapture.Run
    /// </summary>
    public static class MechFrameCapture
    {
        const int W = 960, H = 1080;
        const float OrthoHalfHeight = 46f, CentreY = 41f;

        public static void Run()
        {
            try { Capture(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        static void Capture()
        {
            // fresh import of the model so this run's log shows its import messages (C29: none expected)
            Debug.Log("[MechFrameCapture] reimport begin " + MechFrameImport.ModelPath);
            AssetDatabase.ImportAsset(MechFrameImport.ModelPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[MechFrameCapture] reimport end");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MechFrameImport.PrefabPath);
            var mech = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            var sun = new GameObject("sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.56f, 0.6f);

            // 1.8 m human scale figure in front of the left toe (same spot as the Blender turnaround)
            var human = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            human.name = "scale_human_1.8m";
            UnityEngine.Object.DestroyImmediate(human.GetComponent<Collider>());
            human.transform.localScale = new Vector3(0.45f, 0.9f, 0.45f);
            human.transform.position = new Vector3(-13f, 0.9f, 9f);
            var hm = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            hm.SetColor("_BaseColor", new Color(0.85f, 0.05f, 0.05f));
            human.GetComponent<MeshRenderer>().sharedMaterial = hm;

            var cam = new GameObject("capture_cam").AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = OrthoHalfHeight;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.93f, 0.93f, 0.92f);
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 1000f;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;

            var bounds = new Bounds();
            bool first = true;
            foreach (var r in mech.GetComponentsInChildren<Renderer>())
            {
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }

            var outDir = Path.Combine(MechFrameImport.EvidenceDir, "raw");
            Directory.CreateDirectory(outDir);
            var views = new (string name, Vector3 dir)[]
            {
                ("front", Vector3.forward), ("side", Vector3.left), ("back", Vector3.back),
                ("three_quarter", new Vector3(-1f, 0f, 1f).normalized),
            };
            var json = new System.Text.StringBuilder();
            json.Append("{\n \"w\": ").Append(W).Append(", \"h\": ").Append(H)
                .Append(", \"px_per_m\": ").Append((H / (2f * OrthoHalfHeight)).ToString("R"))
                .Append(", \"y_centre\": ").Append(CentreY.ToString("R"))
                .Append(",\n \"bounds_min\": [").Append(V(bounds.min)).Append("], \"bounds_max\": [").Append(V(bounds.max)).Append("]")
                .Append(",\n \"height_m\": ").Append((bounds.max.y - bounds.min.y).ToString("F4"))
                .Append(",\n \"views\": [");
            for (int i = 0; i < views.Length; i++)
            {
                var (name, dir) = views[i];
                var centre = new Vector3(0f, CentreY, 0f);
                cam.transform.position = centre + dir * 300f;
                cam.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(outDir, $"unity_{name}.png"), tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                json.Append(i == 0 ? "" : ",").Append("\"").Append(name).Append("\"");
            }
            json.Append("]\n}\n");
            File.WriteAllText(Path.Combine(outDir, "unity_views.json"), json.ToString());
            cam.targetTexture = null;
            rt.Release();
            Debug.Log($"[MechFrameCapture] bounds {bounds.min} .. {bounds.max}, height {bounds.max.y - bounds.min.y:F4} m");
        }

        static string V(Vector3 v) => $"{v.x:F4}, {v.y:F4}, {v.z:F4}";
    }
}
