using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Rendering.Pixel
{
    /// <summary>Quantise stage: palette LUT lookup; the output is exact palette colours with the index in alpha.</summary>
    public sealed class PixelQuantiseFeature : PixelStageFeature
    {
        public override PixelStageId Stage => PixelStageId.Quantise;
        protected override PixelStagePass CreatePass() => new Pass();

        sealed class Pass : PixelStagePass
        {
            public Pass() : base(PixelStageId.Quantise, ScriptableRenderPassInput.None) { }
            protected override void Record(RenderGraph renderGraph, UniversalResourceData resources, PixelFrameContext context)
            {
                int scale = ColourScale(context);
                var profile = binder.profile;
                var desc = ColourDesc(renderGraph, resources, 1, "_PixelQuantised");
                desc.format = GraphicsFormat.R8G8B8A8_UNorm;
                var dst = renderGraph.CreateTexture(desc);
                using (Begin(renderGraph, resources, context, resources.activeColorTexture, scale, dst, 1, false, true, out var data))
                {
                    data.texId0 = PixelIds.Lut; data.tex0 = profile.LutTexture;
                    data.texId1 = PixelIds.Palette; data.tex1 = profile.PaletteTexture;
                    data.id0 = PixelIds.Quantise; data.v0 = new Vector4(profile.lutSize, profile.ditherSpread, binder.controlTemporalNoiseSeed, binder.controlContinuousTone ? 1 : 0);
                }
                resources.cameraColor = dst;
                context.colourIsPalette = !binder.controlContinuousTone; context.colourIsEncoded = binder.controlContinuousTone; context.downsampled = true;
            }
        }
    }
}
