using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Rendering.Pixel
{
    /// <summary>Outline stage (optional): relative depth edges in the near field, darkened before quantisation.</summary>
    public sealed class PixelOutlineFeature : PixelStageFeature
    {
        public override PixelStageId Stage => PixelStageId.Outline;
        protected override PixelStagePass CreatePass() => new Pass();

        sealed class Pass : PixelStagePass
        {
            public Pass() : base(PixelStageId.Outline, ScriptableRenderPassInput.Depth) { }
            protected override void Record(RenderGraph renderGraph, UniversalResourceData resources, PixelFrameContext context)
            {
                int scale = ColourScale(context);
                var settings = binder.Settings;
                var dst = renderGraph.CreateTexture(ColourDesc(renderGraph, resources, scale, "_PixelOutlined"));
                using (Begin(renderGraph, resources, context, resources.activeColorTexture, scale, dst, scale, true, false, out var data))
                {
                    data.id0 = PixelIds.Outline; data.v0 = new Vector4(settings.outlineDepthThreshold, settings.outlineMaxDistanceM, settings.outlineStrength, 0);
                }
                resources.cameraColor = dst;
            }
        }
    }
}
