using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Rendering.Pixel
{
    /// <summary>Haze stage: aerial perspective in posterised bands by real radial distance, and the sky gradient.</summary>
    public sealed class PixelHazeFeature : PixelStageFeature
    {
        public override PixelStageId Stage => PixelStageId.Haze;
        protected override PixelStagePass CreatePass() => new Pass();

        sealed class Pass : PixelStagePass
        {
            public Pass() : base(PixelStageId.Haze, ScriptableRenderPassInput.Depth) { }
            protected override void Record(RenderGraph renderGraph, UniversalResourceData resources, PixelFrameContext context)
            {
                int scale = ColourScale(context);
                var profile = binder.profile;
                var dst = renderGraph.CreateTexture(ColourDesc(renderGraph, resources, scale, "_PixelHazed"));
                using (Begin(renderGraph, resources, context, resources.activeColorTexture, scale, dst, scale, true, true, out var data))
                {
                    data.id0 = PixelIds.Haze;
                    data.v0 = new Vector4(Mathf.Max(0.01f, profile.hazeStartM), profile.HazeBandCount, profile.hazeSeamWidth, profile.airlightValue);
                    data.id1 = PixelIds.Sky;
                    data.v1 = new Vector4(profile.skyHorizonValue, profile.skyZenithValue, profile.skyZenithElevationDeg * Mathf.Deg2Rad, binder.controlReversedHaze ? -1f : 1f);
                    data.id2 = PixelIds.Haze2; data.v2 = new Vector4(1f / Mathf.Log(Mathf.Max(1.0001f, profile.hazeFullM / Mathf.Max(0.01f, profile.hazeStartM))), profile.hazeSeamTexels, 0, binder.Settings.gradientDepthTolerance);
                }
                resources.cameraColor = dst;
            }
        }
    }
}
