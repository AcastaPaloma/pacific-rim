using System;
using System.IO;
using Game.Core;
using Game.Rendering.Pixel;
using Game.Rendering.Pixel.Editor;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Tools.StyleCapture
{
    /// <summary>One captured frame: buffers are reused between frames. Rows are bottom-up (Unity texture order).</summary>
    public sealed class CapturedFrame
    {
        public int Width, Height, OutputWidth, OutputHeight;
        public byte[] Rgb;        // internal colour, 3 bytes per pixel, sRGB-encoded
        public byte[] Index;      // palette index per pixel, 255 = not palette-quantised
        public float[] Depth;     // eye-space linear depth (m); >= 1e29 = sky
        public byte[] OutputRgb;  // presented frame, 3 bytes per pixel (null when the output was not read)
    }

    /// <summary>
    /// Renders the scene's pixel cameras explicitly and reads the results back (R-003 §1.4: batch mode presents no
    /// swapchain, so capture is explicit rendering into render textures). Everything written to disk comes straight
    /// from the engine's targets: nothing is post-processed outside the renderer.
    /// </summary>
    public sealed class PixelCaptureRig : IDisposable
    {
        public readonly Camera World, Present;
        public readonly PixelCameraBinder Binder;
        public readonly PixelPresentCamera PresentComponent;
        RenderTexture m_Output;
        Texture2D m_ColourTex, m_DepthTex, m_OutputTex;
        readonly CapturedFrame m_Frame = new CapturedFrame();
        readonly UniversalRenderPipeline.SingleCameraRequest m_Request = new UniversalRenderPipeline.SingleCameraRequest();

        public PixelCaptureRig()
        {
            Binder = UnityEngine.Object.FindFirstObjectByType<PixelCameraBinder>() ?? throw new InvalidOperationException("scene has no PixelCameraBinder");
            PresentComponent = UnityEngine.Object.FindFirstObjectByType<PixelPresentCamera>() ?? throw new InvalidOperationException("scene has no PixelPresentCamera");
            World = Binder.Camera; Present = PresentComponent.GetComponent<Camera>();
        }

        /// <summary>Session flags from Config/FeatureFlags (all false in the repository) plus a configuration file's enables.</summary>
        public static PixelFlagState LoadFlags(string configurationPath)
        {
            var errors = new CollectingErrorSink();
            IFeatureFlags flags = FeatureFlagLoader.LoadFolder("Assets/Game/Config/FeatureFlags", errors);
            var configuration = configurationPath != null ? PixelConfiguration.FromJson(File.ReadAllText(configurationPath)) : null;
            return PixelFlagState.From(flags, configuration);
        }

        public static PixelPipelineSettings LoadSettings() => PixelPipelineSettings.FromJson(File.ReadAllText(PixelPipelineInstaller.SettingsPath));

        public void SetOutputSize(int width, int height)
        {
            Binder.outputSize = new Vector2Int(width, height);
            if (m_Output != null && m_Output.width == width && m_Output.height == height) return;
            if (m_Output != null) { m_Output.Release(); UnityEngine.Object.DestroyImmediate(m_Output); }
            // UNorm (not sRGB): the present stage writes the palette bytes unchanged, so the PNG holds exact palette values.
            m_Output = new RenderTexture(width, height, 0, GraphicsFormat.R8G8B8A8_UNorm) { name = "PixelCaptureOutput", filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            m_Output.Create();
        }

        /// <summary>Renders the world camera into the internal targets, then the present camera into the output target.</summary>
        public void Render(bool renderOutput = true)
        {
            Binder.PrepareFrame();
            m_Request.destination = World.targetTexture;
            if (!RenderPipeline.SupportsRenderRequest(World, m_Request)) throw new InvalidOperationException("the active render pipeline does not support single-camera requests (is the pixel pipeline active?)");
            RenderPipeline.SubmitRenderRequest(World, m_Request);
            if (!renderOutput) return;
            if (m_Output == null) SetOutputSize(Binder.outputSize.x, Binder.outputSize.y);
            Present.targetTexture = m_Output;
            m_Request.destination = m_Output;
            RenderPipeline.SubmitRenderRequest(Present, m_Request);
            Present.targetTexture = null;
        }

        /// <summary>Reads the visible internal image (guard band cropped) and optionally the presented output.</summary>
        public CapturedFrame Read(bool readOutput)
        {
            var layout = Binder.Layout;
            int w = layout.Width, h = layout.Height, g = layout.Guard;
            var f = m_Frame;
            if (f.Width != w || f.Height != h || f.Rgb == null)
            {
                f.Width = w; f.Height = h; f.Rgb = new byte[w * h * 3]; f.Index = new byte[w * h]; f.Depth = new float[w * h];
                Destroy(ref m_ColourTex); Destroy(ref m_DepthTex);
                m_ColourTex = new Texture2D(w, h, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None) { hideFlags = HideFlags.HideAndDontSave };
                m_DepthTex = new Texture2D(w, h, GraphicsFormat.R32_SFloat, TextureCreationFlags.None) { hideFlags = HideFlags.HideAndDontSave };
            }
            var previous = RenderTexture.active;
            RenderTexture.active = Binder.InternalColour;
            m_ColourTex.ReadPixels(new Rect(g, g, w, h), 0, 0, false);
            NativeArray<byte> rgba = m_ColourTex.GetRawTextureData<byte>();
            for (int i = 0, n = w * h; i < n; i++)
            {
                f.Rgb[i * 3] = rgba[i * 4]; f.Rgb[i * 3 + 1] = rgba[i * 4 + 1]; f.Rgb[i * 3 + 2] = rgba[i * 4 + 2];
                f.Index[i] = rgba[i * 4 + 3];
            }
            RenderTexture.active = Binder.InternalDepth;
            m_DepthTex.ReadPixels(new Rect(g, g, w, h), 0, 0, false);
            m_DepthTex.GetRawTextureData<float>().CopyTo(f.Depth);
            if (readOutput && m_Output != null)
            {
                int ow = m_Output.width, oh = m_Output.height;
                if (f.OutputRgb == null || f.OutputWidth != ow || f.OutputHeight != oh)
                {
                    f.OutputWidth = ow; f.OutputHeight = oh; f.OutputRgb = new byte[ow * oh * 3];
                    Destroy(ref m_OutputTex);
                    m_OutputTex = new Texture2D(ow, oh, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None) { hideFlags = HideFlags.HideAndDontSave };
                }
                RenderTexture.active = m_Output;
                m_OutputTex.ReadPixels(new Rect(0, 0, ow, oh), 0, 0, false);
                NativeArray<byte> o = m_OutputTex.GetRawTextureData<byte>();
                for (int i = 0, n = ow * oh; i < n; i++) { f.OutputRgb[i * 3] = o[i * 4]; f.OutputRgb[i * 3 + 1] = o[i * 4 + 1]; f.OutputRgb[i * 3 + 2] = o[i * 4 + 2]; }
            }
            else if (!readOutput) { f.OutputRgb = null; f.OutputWidth = f.OutputHeight = 0; }
            RenderTexture.active = previous;
            return f;
        }

        public static void WriteRgbPng(string path, byte[] rgb, int width, int height) =>
            File.WriteAllBytes(path, ImageConversion.EncodeArrayToPNG(rgb, GraphicsFormat.R8G8B8_UNorm, (uint)width, (uint)height, (uint)(width * 3)));

        public static void WriteGreyPng(string path, byte[] grey, int width, int height) =>
            File.WriteAllBytes(path, ImageConversion.EncodeArrayToPNG(grey, GraphicsFormat.R8_UNorm, (uint)width, (uint)height, (uint)width));

        public static void WriteGrey16Png(string path, ushort[] grey, int width, int height) =>
            File.WriteAllBytes(path, ImageConversion.EncodeArrayToPNG(grey, GraphicsFormat.R16_UNorm, (uint)width, (uint)height, (uint)(width * 2)));

        /// <summary>style_capture@v1 depth: v = round(65535 * ln(z / near) / ln(far / near)), at most 65534 for geometry; 65535 = sky.</summary>
        public static void EncodeDepthLog16(float[] depth, float near, float far, ushort[] encoded)
        {
            double scale = 65535.0 / Math.Log(far / (double)near);
            for (int i = 0; i < depth.Length; i++)
            {
                float z = depth[i];
                if (z >= 1e29f || z >= far) { encoded[i] = 65535; continue; }
                double v = Math.Log(Math.Max(z, near) / near) * scale;
                encoded[i] = (ushort)Math.Min(65534, Math.Max(0, (int)Math.Floor(v + 0.5)));
            }
        }

        static void Destroy(ref Texture2D t) { if (t != null) UnityEngine.Object.DestroyImmediate(t); t = null; }

        public void Dispose()
        {
            Destroy(ref m_ColourTex); Destroy(ref m_DepthTex); Destroy(ref m_OutputTex);
            if (m_Output != null) { m_Output.Release(); UnityEngine.Object.DestroyImmediate(m_Output); m_Output = null; }
        }
    }
}
