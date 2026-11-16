// T-194 diagnostics: per-frame hashes of the intermediate buffers and a hash of the scene geometry, written when the
// environment variable KAIJU_CAPTURE_DIAG names a directory. Off otherwise. Used to bisect cross-process differences.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Game.Rendering.Pixel;
using Game.Tools.StyleCapture;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Tools.Capture
{
    public static class CaptureDiag
    {
        public static string Dir => Environment.GetEnvironmentVariable("KAIJU_CAPTURE_DIAG");
        public static bool On => !string.IsNullOrEmpty(Dir);
        public static bool Wants(string tag) => On && tag.Contains("p4_d");
        static readonly HashSet<string> s_GeometryDone = new HashSet<string>();

        static string Sha(byte[] b) { using (var s = SHA256.Create()) return BitConverter.ToString(s.ComputeHash(b)).Replace("-", "").ToLowerInvariant().Substring(0, 16); }
        static byte[] Bytes(float[] f) { var b = new byte[f.Length * 4]; Buffer.BlockCopy(f, 0, b, 0, b.Length); return b; }

        public static void Geometry(string tag)
        {
            if (!Wants(tag) || !s_GeometryDone.Add(tag)) return;
            Directory.CreateDirectory(Dir);
            PixelDiagTap.Enabled = true;
            string fix = Environment.GetEnvironmentVariable("KAIJU_DIAG_FIX");
            if (fix == "1" || fix == "2") { var pr = RenderSettings.ambientProbe; RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Custom; RenderSettings.ambientProbe = pr; }
            if (fix == "2") RenderSettings.reflectionIntensity = 0f;
            try
            {
                var dt = ReflectionProbe.defaultTexture; var sbr = new StringBuilder("default reflection " + (dt != null ? dt.GetType().Name + " " + dt.width + " dim " + dt.dimension : "null") + "\n");
                if (dt is Cubemap cm) { for (int face = 0; face < 6; face++) for (int mip = 0; mip < cm.mipmapCount; mip++) { var rq = AsyncGPUReadback.Request(cm, mip, 0, Math.Max(1, cm.width >> mip), 0, Math.Max(1, cm.height >> mip), face, 1); rq.WaitForCompletion(); sbr.Append("face " + face + " mip " + mip + " " + (rq.hasError ? "err" : Sha(rq.GetData<byte>().ToArray())) + "\n"); } }
                else if (dt is RenderTexture drt) { for (int face = 0; face < 6; face++) for (int mip = 0; mip < 3; mip++) { var rq = AsyncGPUReadback.Request(drt, mip, 0, Math.Max(1, drt.width >> mip), 0, Math.Max(1, drt.height >> mip), face, 1); rq.WaitForCompletion(); sbr.Append("face " + face + " mip " + mip + " " + (rq.hasError ? "err" : Sha(rq.GetData<byte>().ToArray())) + "\n"); } }
                File.WriteAllText(System.IO.Path.Combine(Dir, tag + "_reflection.txt"), sbr.ToString());
            }
            catch (Exception e) { File.WriteAllText(System.IO.Path.Combine(Dir, tag + "_reflection.txt"), "exception " + e.Message); }
            var sb = new StringBuilder(); var all = new StringBuilder();
            var rs = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var rows = new List<string>();
            foreach (var r in rs)
            {
                var mf = r.GetComponent<MeshFilter>(); var mesh = mf != null ? mf.sharedMesh : null;
                string mh = "none"; int vc = 0;
                if (mesh != null)
                {
                    var v = new List<Vector3>(); mesh.GetVertices(v); vc = v.Count;
                    var raw = new byte[v.Count * 12]; for (int i = 0; i < v.Count; i++) { Buffer.BlockCopy(BitConverter.GetBytes(v[i].x), 0, raw, i * 12, 4); Buffer.BlockCopy(BitConverter.GetBytes(v[i].y), 0, raw, i * 12 + 4, 4); Buffer.BlockCopy(BitConverter.GetBytes(v[i].z), 0, raw, i * 12 + 8, 4); }
                    var idx = new List<int>(); for (int sm = 0; sm < mesh.subMeshCount; sm++) { var t = new List<int>(); mesh.GetIndices(t, sm); idx.AddRange(t); }
                    var ib = new byte[idx.Count * 4]; Buffer.BlockCopy(idx.ToArray(), 0, ib, 0, ib.Length);
                    var c = new List<Color32>(); mesh.GetColors(c); var cb = new byte[c.Count * 4]; for (int i = 0; i < c.Count; i++) { cb[i * 4] = c[i].r; cb[i * 4 + 1] = c[i].g; cb[i * 4 + 2] = c[i].b; cb[i * 4 + 3] = c[i].a; }
                    var nn = new List<Vector3>(); mesh.GetNormals(nn); var tt = new List<Vector4>(); mesh.GetTangents(tt); var u0 = new List<Vector2>(); mesh.GetUVs(0, u0);
                    var nb = new byte[nn.Count * 12]; for (int i = 0; i < nn.Count; i++) { Buffer.BlockCopy(BitConverter.GetBytes(nn[i].x), 0, nb, i * 12, 4); Buffer.BlockCopy(BitConverter.GetBytes(nn[i].y), 0, nb, i * 12 + 4, 4); Buffer.BlockCopy(BitConverter.GetBytes(nn[i].z), 0, nb, i * 12 + 8, 4); }
                    var tb = new byte[tt.Count * 16]; for (int i = 0; i < tt.Count; i++) { Buffer.BlockCopy(BitConverter.GetBytes(tt[i].x), 0, tb, i * 16, 4); Buffer.BlockCopy(BitConverter.GetBytes(tt[i].y), 0, tb, i * 16 + 4, 4); Buffer.BlockCopy(BitConverter.GetBytes(tt[i].z), 0, tb, i * 16 + 8, 4); Buffer.BlockCopy(BitConverter.GetBytes(tt[i].w), 0, tb, i * 16 + 12, 4); }
                    var ub = new byte[u0.Count * 8]; for (int i = 0; i < u0.Count; i++) { Buffer.BlockCopy(BitConverter.GetBytes(u0[i].x), 0, ub, i * 8, 4); Buffer.BlockCopy(BitConverter.GetBytes(u0[i].y), 0, ub, i * 8 + 4, 4); }
                    mh = Sha(raw) + "/" + Sha(ib) + "/" + Sha(cb) + "/n" + Sha(nb) + "/t" + Sha(tb) + "/u" + Sha(ub);
                }
                var b = r.bounds; var m = r.sharedMaterial;
                string mp = "";
                if (m != null && m.shader != null) { var sbp = new StringBuilder(); var sh = m.shader; for (int pi = 0; pi < sh.GetPropertyCount(); pi++) { var ty = sh.GetPropertyType(pi); var nm = sh.GetPropertyName(pi); if (ty == UnityEngine.Rendering.ShaderPropertyType.Color) sbp.Append(nm + m.GetColor(nm).ToString("F6")); else if (ty == UnityEngine.Rendering.ShaderPropertyType.Vector) sbp.Append(nm + m.GetVector(nm).ToString("F6")); else if (ty == UnityEngine.Rendering.ShaderPropertyType.Float || ty == UnityEngine.Rendering.ShaderPropertyType.Range) sbp.Append(nm + m.GetFloat(nm).ToString("R")); } mp = "|mp" + Sha(Encoding.UTF8.GetBytes(sbp.ToString())); }
                rows.Add(r.GetType().Name + "|" + Path(r.transform) + "|" + (m != null ? m.name + ":" + (m.shader != null ? m.shader.name : "") + ":q" + m.renderQueue : "nomat") + "|v" + vc + "|" + mh + "|" + b.center.ToString("F4") + b.size.ToString("F4") + "|so" + r.sortingOrder + mp);
                all.Append(rows[rows.Count - 1]).Append('\n');
            }
            File.WriteAllText(System.IO.Path.Combine(Dir, tag + "_renderers_enum_order.txt"), all.ToString());
            rows.Sort(StringComparer.Ordinal);
            File.WriteAllText(System.IO.Path.Combine(Dir, tag + "_renderers_sorted.txt"), string.Join("\n", rows));
            File.WriteAllText(System.IO.Path.Combine(Dir, tag + "_geometry.txt"), "renderers " + rs.Length + " enum_hash " + Sha(Encoding.UTF8.GetBytes(all.ToString())) + " sorted_hash " + Sha(Encoding.UTF8.GetBytes(string.Join("\n", rows))) + "\n");
        }


        static readonly HashSet<string> s_ResDone = new HashSet<string>();
        static string Tap(RenderTexture rt, string tag, int frame, string name, bool dump)
        {
            if (rt == null) return "null";
            var req = AsyncGPUReadback.Request(rt, 0); req.WaitForCompletion();
            if (req.hasError) return "err";
            var b = req.GetData<byte>().ToArray();
            if (dump && frame == 33) File.WriteAllBytes(System.IO.Path.Combine(Dir, tag + "_f" + frame + "_" + name + ".bin"), b);
            return Sha(b) + "(" + rt.graphicsFormat + ")";
        }
        static string TexHash(Texture t)
        {
            var sb = new StringBuilder(t.name + " " + t.GetType().Name + " " + t.width + "x" + t.height);
            AsyncGPURead(t, sb); return sb.ToString();
        }
        static void AsyncGPURead(Texture t, StringBuilder sb)
        {
            AsyncGPUReadbackRequest rq;
            if (t is Texture3D t3) rq = AsyncGPUReadback.Request(t3, 0); else rq = AsyncGPUReadback.Request(t, 0);
            rq.WaitForCompletion();
            sb.Append(" gpu=" + (rq.hasError ? "err" : Sha(rq.GetData<byte>().ToArray())));
            if (t is Texture2D t2 && t2.isReadable) sb.Append(" cpu=" + Sha(t2.GetRawTextureData()));
            if (t is Texture3D t3b && t3b.isReadable) sb.Append(" cpu=" + Sha(t3b.GetPixelData<byte>(0).ToArray()));
        }
        // once per tag: hashes of the textures the Dither and Quantise passes read, as they sit on the GPU of this process
        static void Resources(string tag, PixelCaptureRig rig)
        {
            if (!s_ResDone.Add(tag)) return;
            var sb = new StringBuilder();
            try
            {
                if (PixelCameraBinder.TryGet(rig.World, out var binder))
                {
                    if (binder.profile != null) { sb.AppendLine("lut " + TexHash(binder.profile.LutTexture)); sb.AppendLine("palette " + TexHash(binder.profile.PaletteTexture)); }
                    var settings = binder.Settings; if (settings != null) sb.AppendLine("mask " + TexHash(PixelDitherMaskCache.Get(settings.ditherMask)));
                }
                else sb.AppendLine("no binder");
            }
            catch (Exception e) { sb.AppendLine("exception " + e); }
            File.WriteAllText(System.IO.Path.Combine(Dir, tag + "_resources.txt"), sb.ToString());
        }

        public static string CamHash(PixelCaptureRig rig)
        {
            var rt = rig.World.targetTexture; if (rt == null) return "null";
            var req = AsyncGPUReadback.Request(rt, 0); req.WaitForCompletion();
            return req.hasError ? "err" : Sha(req.GetData<byte>().ToArray());
        }

        // In-process draw-order experiment: re-registers every renderer in a different order (disable, then enable in that order) and re-renders the plan.
        public static void Permute(string tag, int n, Action<int> renderFrame, IList<int> plan)
        {
            var rs = new List<Renderer>(UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None));
            rs.Sort((a, b) => string.CompareOrdinal(Path(a.transform), Path(b.transform)));
            var lines = new StringBuilder();
            for (int k = 0; k < n; k++)
            {
                var order = new List<Renderer>(rs);
                if (k == 1) order.Reverse();
                else if (k > 1) { var rnd = new System.Random(k * 7919); for (int i = order.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); var t = order[i]; order[i] = order[j]; order[j] = t; } }
                foreach (var r in order) r.enabled = false;
                foreach (var r in order) r.enabled = true;
                foreach (int frame in plan) { renderFrame(frame); lines.Append(k).Append(' ').Append(frame).Append(' ').Append(lastHash).Append('\n'); }
            }
            File.WriteAllText(System.IO.Path.Combine(Dir, tag + "_permute.txt"), lines.ToString());
        }

        // Which renderers cover one cam-target pixel? Renders frame `frame` with all renderers off, then with each renderer alone; lists those whose pixel differs from the empty render.
        public static void Cover(string tag, int frame, int px, int py, PixelCaptureRig rig, Action<int> renderFrame)
        {
            var rs = new List<Renderer>(UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None));
            rs.Sort((a, b) => string.CompareOrdinal(Path(a.transform), Path(b.transform)));
            var rt = rig.World.targetTexture; int w = rt.width;
            byte[] Px() { var req = AsyncGPUReadback.Request(rt, 0); req.WaitForCompletion(); var d = req.GetData<byte>(); int o = (py * w + px) * 4; return new[] { d[o], d[o + 1], d[o + 2], d[o + 3] }; }
            var en = new List<bool>(); foreach (var r in rs) en.Add(r.enabled);
            var sb = new StringBuilder();
            renderFrame(frame); sb.Append("all " + BitConverter.ToString(Px()) + " hash " + CamHash(rig) + "\n");
            foreach (var r in rs) r.enabled = false;
            renderFrame(frame); var empty = Px(); sb.Append("none " + BitConverter.ToString(empty) + "\n");
            for (int i = 0; i < rs.Count; i++)
            {
                rs[i].enabled = en[i]; if (!en[i]) continue;
                renderFrame(frame); var q = Px(); rs[i].enabled = false;
                if (BitConverter.ToString(q) != BitConverter.ToString(empty))
                {
                    var m = rs[i].sharedMaterial; var b = rs[i].bounds;
                    sb.Append("COVER " + Path(rs[i].transform) + " mat=" + (m != null ? m.name + ":" + (m.shader != null ? m.shader.name : "") + ":q" + m.renderQueue : "-") + " px=" + BitConverter.ToString(q) + " bounds=" + b.center.ToString("F3") + b.size.ToString("F3") + " layer=" + rs[i].gameObject.layer + "\n");
                }
            }
            for (int i = 0; i < rs.Count; i++) rs[i].enabled = en[i];
            File.WriteAllText(System.IO.Path.Combine(Dir, tag + "_cover_f" + frame + ".txt"), sb.ToString());
        }
        public static string lastHash;

        static string Path(Transform t) { var s = t.name; while (t.parent != null) { t = t.parent; s = t.name + "/" + s; } return s; }

        // after Render+Read of one frame: camera target (pre-downsample colour), internal depth floats, internal colour bytes, index bytes
        public static void Frame(string tag, int frame, PixelCaptureRig rig, CapturedFrame f, bool dumpRaw)
        {
            if (!Wants(tag)) return;
            Directory.CreateDirectory(Dir);
            var rt = rig.World.targetTexture;
            string camHash = "null"; byte[] camBytes = null;
            if (rt != null)
            {
                var req = AsyncGPUReadback.Request(rt, 0);
                req.WaitForCompletion();
                if (!req.hasError) { camBytes = req.GetData<byte>().ToArray(); camHash = Sha(camBytes); }
            }
            Resources(tag, rig);
            string tapSrc = Tap(PixelDiagTap.Source, tag, frame, "src", dumpRaw), tapThr = Tap(PixelDiagTap.Threshold, tag, frame, "thr", dumpRaw);
            var sh = RenderSettings.ambientProbe; var shs = new StringBuilder();
            for (int c = 0; c < 3; c++) for (int k = 0; k < 9; k++) shs.Append(sh[c, k].ToString("R")).Append(',');
            File.AppendAllText(System.IO.Path.Combine(Dir, tag + "_env.txt"), frame + " mode=" + RenderSettings.ambientMode + " sky=" + (RenderSettings.skybox != null ? RenderSettings.skybox.name : "null") + " ai=" + RenderSettings.ambientIntensity + " refmode=" + RenderSettings.defaultReflectionMode + " refint=" + RenderSettings.reflectionIntensity + " custom=" + (RenderSettings.customReflectionTexture != null ? RenderSettings.customReflectionTexture.name : "null") + " amb=" + RenderSettings.ambientLight + " sh=" + shs + "\n");
            var cam = rig.World; var mt = new StringBuilder();
            void M(Matrix4x4 m) { for (int i = 0; i < 16; i++) mt.Append(BitConverter.SingleToInt32Bits(m[i]).ToString("x8")).Append(i < 15 ? "," : ""); mt.Append(" | "); }
            M(cam.worldToCameraMatrix); M(cam.projectionMatrix); M(cam.cullingMatrix); M(cam.transform.localToWorldMatrix);
            mt.Append("fov " + BitConverter.SingleToInt32Bits(cam.fieldOfView).ToString("x8") + " asp " + BitConverter.SingleToInt32Bits(cam.aspect).ToString("x8") + " rect " + cam.pixelRect + " tgt " + (cam.targetTexture != null ? cam.targetTexture.width + "x" + cam.targetTexture.height : "null") + " usePhys " + cam.usePhysicalProperties);
            File.AppendAllText(System.IO.Path.Combine(Dir, tag + "_cam_matrices.txt"), frame + " " + mt + "\n");
            string line = frame + " cam=" + camHash + " src=" + tapSrc + " thr=" + tapThr + " depthf=" + Sha(Bytes(f.Depth)) + " rgb=" + Sha(f.Rgb) + " idx=" + Sha(f.Index) + " out=" + (f.OutputRgb != null ? Sha(f.OutputRgb) : "-");
            File.AppendAllText(System.IO.Path.Combine(Dir, tag + "_frames.txt"), line + "\n");
            if (dumpRaw)
            {
                if (camBytes != null) File.WriteAllBytes(System.IO.Path.Combine(Dir, tag + "_f" + frame + "_cam.bin"), camBytes);
                File.WriteAllBytes(System.IO.Path.Combine(Dir, tag + "_f" + frame + "_depth.bin"), Bytes(f.Depth));
                File.WriteAllBytes(System.IO.Path.Combine(Dir, tag + "_f" + frame + "_idx.bin"), f.Index);
                if (rt != null) File.WriteAllText(System.IO.Path.Combine(Dir, tag + "_f" + frame + "_cam.txt"), rt.width + "x" + rt.height + " " + rt.graphicsFormat);
            }
        }
    }
}
