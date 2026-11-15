using System;
using System.Collections.Generic;
using System.IO;
using Game.Units.Mech.Unity;
using UnityEngine;

namespace Game.Units.Mech.Scenarios
{
    /// <summary>
    /// Frame strips for the feel panel: an orthographic side view and a perspective three-quarter view per sample, composed into contact sheets (PNG).
    /// The scene carries a 1.8 m figure marker and a 100 m scale bar with 10 m divisions next to the mech; primitive visuals only (the model is T-007's).
    /// </summary>
    public sealed class StripCapture : IDisposable
    {
        const int TileW = 320, TileH = 240;
        readonly MechUnit _unit;
        readonly Camera _side, _quarter;
        readonly RenderTexture _rt;
        readonly List<Color32[]> _sideFrames = new List<Color32[]>(), _quarterFrames = new List<Color32[]>();
        readonly List<double> _times = new List<double>();
        readonly GameObject _root;
        readonly Texture2D _tile;
        readonly int _max;
        public int Count => _times.Count;

        public StripCapture(MechUnit unit, int maxFrames)
        {
            _unit = unit; _max = maxFrames;
            _root = new GameObject("strip_capture");
            var sun = new GameObject("sun"); sun.transform.SetParent(_root.transform); var light = sun.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f; sun.transform.rotation = Quaternion.Euler(45, -120, 0);
            _side = MakeCamera("side", true); _quarter = MakeCamera("quarter", false);
            _rt = new RenderTexture(TileW, TileH, 24); _tile = new Texture2D(TileW, TileH, TextureFormat.RGB24, false);
            // ground reference: 20 m checker stripes so foot travel and slip can be seen; scale bar; 1.8 m figure
            for (int i = -20; i <= 60; i++)
            {
                var stripe = Visual(PrimitiveType.Cube, new Vector3(0, -0.45f, i * 40f), new Vector3(400, 1, 20), new Color(0.55f, 0.55f, 0.5f));
                stripe.name = "stripe";
                var cross = Visual(PrimitiveType.Cube, new Vector3(i * 40f, -0.44f, 0), new Vector3(20, 1, 400), new Color(0.5f, 0.52f, 0.5f));
                cross.name = "stripe_x";
            }
            Visual(PrimitiveType.Cube, new Vector3(0, -1f, 0), new Vector3(6000, 1, 6000), new Color(0.72f, 0.72f, 0.68f)).name = "ground_visual";
        }

        GameObject Visual(PrimitiveType type, Vector3 pos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(type); UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(_root.transform); go.transform.position = pos; go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>(); var m = new Material(r.sharedMaterial); m.color = color; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color); r.sharedMaterial = m;
            return go;
        }

        Camera MakeCamera(string name, bool ortho)
        {
            var go = new GameObject(name); go.transform.SetParent(_root.transform);
            var cam = go.AddComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.62f, 0.70f, 0.80f); cam.nearClipPlane = 1; cam.farClipPlane = 3000; cam.enabled = false;
            cam.orthographic = ortho; cam.orthographicSize = 55; cam.fieldOfView = 35;
            return cam;
        }

        public void Capture(double time)
        {
            if (_times.Count >= _max) return;
            var c = _unit.Body.Com; var com = new Vector3(c.X, 0, c.Z);
            // scale bar (100 m, 10 m divisions) and 1.8 m figure travel with the view so every tile has them
            var props = new GameObject("props"); props.transform.SetParent(_root.transform);
            for (int i = 0; i < 10; i++) { var seg = Visual(PrimitiveType.Cube, com + new Vector3(-22, 0.5f, -50 + 10 * i + 5), new Vector3(1.5f, 1, 10), i % 2 == 0 ? Color.black : Color.white); seg.transform.SetParent(props.transform); }
            var figure = Visual(PrimitiveType.Cube, com + new Vector3(-22, 0.9f, 58), new Vector3(0.6f, 1.8f, 0.6f), Color.red); figure.transform.SetParent(props.transform);
            var pole = Visual(PrimitiveType.Cube, com + new Vector3(-22, 40f, 62), new Vector3(0.8f, 80f, 0.8f), new Color(1f, 0.5f, 0f)); pole.transform.SetParent(props.transform);
            _side.transform.position = com + new Vector3(-600, 45, 0); _side.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
            Vector3 q = com + new Vector3(-170, 95, 210); _quarter.transform.position = q; _quarter.transform.rotation = Quaternion.LookRotation(com + new Vector3(0, 38, 0) - q, Vector3.up);
            _sideFrames.Add(Render(_side)); _quarterFrames.Add(Render(_quarter)); _times.Add(time);
            UnityEngine.Object.DestroyImmediate(props);
        }

        Color32[] Render(Camera cam)
        {
            cam.targetTexture = _rt; cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = _rt;
            _tile.ReadPixels(new Rect(0, 0, TileW, TileH), 0, 0); _tile.Apply();
            RenderTexture.active = prev; cam.targetTexture = null;
            return _tile.GetPixels32();
        }

        public void Save(string dir, string name)
        {
            if (_times.Count == 0) return;
            Directory.CreateDirectory(dir);
            Sheet(_sideFrames, Path.Combine(dir, name + ".side.png"));
            Sheet(_quarterFrames, Path.Combine(dir, name + ".quarter.png"));
            File.WriteAllText(Path.Combine(dir, name + ".times.txt"), "tile order: left to right, top to bottom; times (s): " + string.Join(", ", _times.ConvertAll(t => t.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))) + "\nprops: black/white bar = 100 m in 10 m divisions; red block = 1.8 m person; orange pole = 80 m; ground stripes every 40 m (20 m wide)\n");
        }

        void Sheet(List<Color32[]> frames, string path)
        {
            int cols = Math.Min(6, frames.Count), rows = (frames.Count + cols - 1) / cols;
            var sheet = new Texture2D(cols * TileW, rows * TileH, TextureFormat.RGB24, false);
            var blank = new Color32[TileW * TileH];
            for (int i = 0; i < cols * rows; i++) sheet.SetPixels32((i % cols) * TileW, (rows - 1 - i / cols) * TileH, TileW, TileH, i < frames.Count ? frames[i] : blank);
            sheet.Apply(); File.WriteAllBytes(path, sheet.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(sheet);
        }

        public void Dispose() { _rt.Release(); UnityEngine.Object.DestroyImmediate(_rt); UnityEngine.Object.DestroyImmediate(_tile); UnityEngine.Object.DestroyImmediate(_root); }
    }
}
