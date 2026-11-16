using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

namespace Game.Rendering.Pixel
{
    /// <summary>
    /// T-194b diagnostics, default off: copies the pre-quantise source colour and the dither threshold texture of the last
    /// rendered camera into persistent render textures so a tool can hash them. Enabled only by setting <see cref="Enabled"/>
    /// (the capture diagnostics do it when KAIJU_CAPTURE_DIAG is set). No effect on rendering output.
    /// </summary>
    public static class PixelDiagTap
    {
        public static bool Enabled;
        public static RenderTexture Source, Threshold;
        static RTHandle s_SourceH, s_ThresholdH;

        static TextureHandle Import(RenderGraph g, TextureHandle src, ref RenderTexture rt, ref RTHandle h, string name)
        {
            var d = g.GetTextureDesc(src);
            if (rt == null || rt.width != d.width || rt.height != d.height || rt.graphicsFormat != d.format)
            {
                h?.Release(); if (rt != null) Object.DestroyImmediate(rt);
                rt = new RenderTexture(d.width, d.height, 0, d.format) { name = name, filterMode = FilterMode.Point, enableRandomWrite = false };
                rt.Create(); h = RTHandles.Alloc(rt);
            }
            return g.ImportTexture(h);
        }

        internal static void Tap(RenderGraph g, TextureHandle source, PixelFrameContext context)
        {
            if (!Enabled) return;
            if (source.IsValid()) { var dst = Import(g, source, ref Source, ref s_SourceH, "PixelDiagSource"); g.AddCopyPass(source, dst, "Pixel.DiagTapSource"); }
            if (context.hasThreshold && context.threshold.IsValid()) { var dst = Import(g, context.threshold, ref Threshold, ref s_ThresholdH, "PixelDiagThreshold"); g.AddCopyPass(context.threshold, dst, "Pixel.DiagTapThreshold"); }
        }
    }
}
