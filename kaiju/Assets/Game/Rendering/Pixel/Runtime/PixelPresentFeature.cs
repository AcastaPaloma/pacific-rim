using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Rendering.Pixel
{
    /// <summary>
    /// Present stage, on the present renderer: point upscale of the stored internal image at the exact integer scale,
    /// shifted by whole output pixels for sub-pixel compensation. Writes the camera's colour target directly.
    /// </summary>
    public sealed class PixelPresentFeature : ScriptableRendererFeature
    {
        [Tooltip("Present shader; assigned by PixelPipelineInstaller.")]
        public Shader shader;
        Material m_Material;
        Pass m_Pass;

        public override void Create() { m_Pass = new Pass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing }; }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (shader == null || !PixelPresentCamera.TryGet(renderingData.cameraData.camera, out var present)) return;
            var binder = present.source;
            if (binder == null || !binder.IsPrepared || !binder.StageEnabled(PixelStageId.Present)) return;
            if (m_Material == null) m_Material = CoreUtils.CreateEngineMaterial(shader);
            m_Pass.Setup(binder, present, m_Material, renderingData.cameraData.camera);
            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing) { CoreUtils.Destroy(m_Material); m_Material = null; }

        sealed class Pass : ScriptableRenderPass
        {
            PixelCameraBinder m_Binder; PixelPresentCamera m_Present; Material m_Material; Camera m_Camera;
            readonly MaterialPropertyBlock m_Block = new MaterialPropertyBlock();

            public Pass() { profilingSampler = new ProfilingSampler("Pixel.Present"); requiresIntermediateTexture = false; }
            public void Setup(PixelCameraBinder binder, PixelPresentCamera present, Material material, Camera camera) { m_Binder = binder; m_Present = present; m_Material = material; m_Camera = camera; }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var layout = m_Binder.Layout;
                var source = renderGraph.ImportTexture(m_Binder.InternalColourHandle);
                using var builder = renderGraph.AddRasterRenderPass<PixelPassData>("Pixel.Present", out var data, profilingSampler);
                data.material = m_Material; data.block = m_Block;
                data.src = source; data.dst = resources.activeColorTexture; data.dst2 = TextureHandle.nullHandle;
                data.hasDepth = false; data.hasThreshold = false;
                data.targetSize = Vector4.zero; data.thresholdInfo = Vector4.zero;
                Vector2Int shift = m_Binder.PresentShift();
                data.id0 = PixelIds.Present; data.v0 = new Vector4(layout.Scale, layout.Guard, shift.x, shift.y);
                data.id1 = PixelIds.PresentSize; data.v1 = new Vector4(layout.OutputWidth, layout.OutputHeight, layout.TargetWidth, layout.TargetHeight);
                // Linear project: an sRGB-format target encodes on write, so the stored sRGB bytes are decoded first;
                // a UNorm capture target receives the bytes unchanged (exact palette values).
                var target = m_Camera.targetTexture;
                bool decode = target == null || GraphicsFormatUtility.IsSRGBFormat(target.graphicsFormat);
                data.id2 = PixelIds.PresentMode; data.v2 = new Vector4(decode ? 1 : 0, m_Present.controlBilinear ? 1 : 0, 0, 0);
                data.texId0 = 0; data.texId1 = 0; data.tex0 = null; data.tex1 = null;
                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(data.dst, 0, AccessFlags.WriteAll);
                builder.SetRenderFunc(static (PixelPassData d, RasterGraphContext ctx) =>
                {
                    var b = d.block; b.Clear();
                    b.SetTexture(PixelIds.InternalTex, (RTHandle)d.src);
                    bool flip = ctx.GetTextureUVOrigin(d.src) != ctx.GetTextureUVOrigin(d.dst);
                    b.SetVector(PixelIds.BlitScaleBias, flip ? new Vector4(1, -1, 0, 1) : new Vector4(1, 1, 0, 0));
                    b.SetVector(d.id0, d.v0); b.SetVector(d.id1, d.v1); b.SetVector(d.id2, d.v2);
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, 0, MeshTopology.Triangles, 3, 1, b);
                });
            }
        }
    }
}
