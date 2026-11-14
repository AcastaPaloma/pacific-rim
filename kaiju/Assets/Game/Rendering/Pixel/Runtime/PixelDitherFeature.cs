using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Rendering.Pixel
{
    /// <summary>Dither stage: writes the per-texel threshold (mask and anchoring mode are data) and the gradient mask that confines mixing to places where the scene value changes.</summary>
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
                desc.format = GraphicsFormat.R8G8_UNorm;   // R = threshold, G = gradient mask
                var dst = renderGraph.CreateTexture(desc);
                using (Begin(renderGraph, resources, context, resources.activeColorTexture, ColourScale(context), dst, 1, true, false, out var data))
                {
                    Texture2D mask = PixelDitherMaskCache.Get(settings.ditherMask);
                    data.texId0 = PixelIds.Mask; data.tex0 = mask;
                    data.id0 = PixelIds.DitherParams; data.v0 = new Vector4(mask.width, (int)settings.Anchor, settings.hybridFarStartM, settings.surfaceCellTexels);
                    var hints = binder.Hints;
                    data.id1 = PixelIds.DitherOrigin; data.v1 = binder.HasHints ? new Vector4(hints.DitherOriginX, hints.DitherOriginY, 0, 0) : Vector4.zero;
                    var profile = binder.profile;
                    float gradientMin = profile != null ? profile.ditherGradientMin : 0.001f;
                    data.id2 = PixelIds.DitherGradient; data.v2 = new Vector4(gradientMin, settings.gradientDepthTolerance, binder.StageEnabled(PixelStageId.Haze) ? 1 : 0, 0);
                }
                context.threshold = dst; context.hasThreshold = true;
            }
        }
    }
}
