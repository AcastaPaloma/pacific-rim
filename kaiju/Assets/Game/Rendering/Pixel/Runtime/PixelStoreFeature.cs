using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Rendering.Pixel
{
    /// <summary>Store stage (the low-resolution target): writes the persistent internal colour and linear-depth targets.</summary>
    public sealed class PixelStoreFeature : PixelStageFeature
    {
        public override PixelStageId Stage => PixelStageId.Store;
        protected override PixelStagePass CreatePass() => new Pass();

        sealed class Pass : PixelStagePass
        {
            public Pass() : base(PixelStageId.Store, ScriptableRenderPassInput.Depth) { }
            protected override void Record(RenderGraph renderGraph, UniversalResourceData resources, PixelFrameContext context)
            {
                if (binder.InternalColourHandle == null) return;
                int scale = ColourScale(context);
                var colour = renderGraph.ImportTexture(binder.InternalColourHandle);
                var depth = renderGraph.ImportTexture(binder.InternalDepthHandle);
                using (var builder = Begin(renderGraph, resources, context, resources.activeColorTexture, scale, colour, 1, true, false, out var data))
                {
                    data.dst2 = depth;
                    builder.SetRenderAttachment(depth, 1, AccessFlags.WriteAll);
                    builder.AllowPassCulling(false);   // the targets are read outside this graph (present camera, capture)
                    data.id0 = PixelIds.Store; data.v0 = new Vector4(context.colourIsPalette ? 1 : 0, 0, 0, 0);
                }
            }
        }
    }
}
