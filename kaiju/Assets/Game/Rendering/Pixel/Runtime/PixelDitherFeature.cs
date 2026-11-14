using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Rendering.Pixel
{
    /// <summary>Dither stage: writes the per-texel threshold from the mask and the anchoring mode (both data).</summary>
    public sealed class PixelDitherFeature : PixelStageFeature
    {
        public override PixelStageId Stage => PixelStageId.Dither;
        protected override PixelStagePass CreatePass() => new Pass();

        sealed class Pass : PixelStagePass
        {
            public Pass() : base(PixelStageId.Dither, ScriptableRenderPassInput.Depth) { }
            protected override void Record(RenderGraph renderGraph, UniversalResourceData resources, PixelFrameContext context)
            {
                var settings = binder.Settings;
                var desc = ColourDesc(renderGraph, resources, 1, "_PixelDitherThreshold");
                desc.format = GraphicsFormat.R8_UNorm;
                var dst = renderGraph.CreateTexture(desc);
                using (Begin(renderGraph, resources, context, TextureHandle.nullHandle, 1, dst, 1, true, false, out var data))
                {
                    Texture2D mask = PixelDitherMaskCache.Get(settings.ditherMask);
                    data.texId0 = PixelIds.Mask; data.tex0 = mask;
                    data.id0 = PixelIds.DitherParams; data.v0 = new Vector4(mask.width, (int)settings.Anchor, settings.hybridFarStartM, settings.surfaceCellTexels);
                    var hints = binder.Hints;
                    data.id1 = PixelIds.DitherOrigin; data.v1 = binder.HasHints ? new Vector4(hints.DitherOriginX, hints.DitherOriginY, 0, 0) : Vector4.zero;
                }
                context.threshold = dst; context.hasThreshold = true;
            }
        }
    }
}
