using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Rendering.Pixel
{
    /// <summary>Downsample stage: box filter from the supersampled camera target to the internal resolution.</summary>
    public sealed class PixelDownsampleFeature : PixelStageFeature
    {
        public override PixelStageId Stage => PixelStageId.Downsample;
        protected override PixelStagePass CreatePass() => new Pass();

        sealed class Pass : PixelStagePass
        {
            public Pass() : base(PixelStageId.Downsample, ScriptableRenderPassInput.None) { }
            protected override void Record(RenderGraph renderGraph, UniversalResourceData resources, PixelFrameContext context)
            {
                int ss = binder.Layout.Supersample;
                if (ss <= 1 || context.downsampled) return;
                var dst = renderGraph.CreateTexture(ColourDesc(renderGraph, resources, 1, "_PixelDownsampled"));
                using (Begin(renderGraph, resources, context, resources.activeColorTexture, ss, dst, 1, false, false, out _)) { }
                resources.cameraColor = dst;
                context.downsampled = true;
            }
        }
    }
}
