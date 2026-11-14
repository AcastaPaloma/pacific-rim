using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Rendering.Pixel
{
    /// <summary>Capture-path smoke test feature: one full-screen raster pass that swaps the camera colour (R-003 §1.2).</summary>
    public sealed class PixelSmokeFeature : ScriptableRendererFeature
    {
        public Shader shader;
        Material m_Material;
        SmokePass m_Pass;

        public override void Create()
        {
            m_Pass = new SmokePass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var type = renderingData.cameraData.cameraType;
            if (shader == null || type == CameraType.Preview || type == CameraType.Reflection) return;
            if (m_Material == null) m_Material = CoreUtils.CreateEngineMaterial(shader);
            m_Pass.material = m_Material;
            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(m_Material);
            m_Material = null;
        }

        sealed class SmokePass : ScriptableRenderPass
        {
            public Material material;
            static readonly int s_BlitTexture = Shader.PropertyToID("_BlitTexture");
            static readonly int s_BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
            readonly MaterialPropertyBlock m_Block = new MaterialPropertyBlock();

            sealed class Data { public TextureHandle src, dst; public Material material; public MaterialPropertyBlock block; }

            public SmokePass() { requiresIntermediateTexture = true; profilingSampler = new ProfilingSampler("Pixel.Smoke"); }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var res = frameData.Get<UniversalResourceData>();
                if (res.isActiveTargetBackBuffer) return;
                var desc = renderGraph.GetTextureDesc(res.activeColorTexture);
                desc.name = "_PixelSmoke"; desc.clearBuffer = false; desc.msaaSamples = MSAASamples.None; desc.filterMode = FilterMode.Point;
                using var builder = renderGraph.AddRasterRenderPass<Data>("Pixel.Smoke", out var data, profilingSampler);
                data.src = res.activeColorTexture; data.dst = renderGraph.CreateTexture(desc); data.material = material; data.block = m_Block;
                builder.UseTexture(data.src, AccessFlags.Read);
                builder.SetRenderAttachment(data.dst, 0, AccessFlags.WriteAll);
                builder.SetRenderFunc(static (Data d, RasterGraphContext ctx) =>
                {
                    bool flip = ctx.GetTextureUVOrigin(d.src) != ctx.GetTextureUVOrigin(d.dst);
                    d.block.Clear();
                    d.block.SetTexture(s_BlitTexture, d.src);
                    d.block.SetVector(s_BlitScaleBias, flip ? new Vector4(1, -1, 0, 1) : new Vector4(1, 1, 0, 0));
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, 0, MeshTopology.Triangles, 3, 1, d.block);
                });
                res.cameraColor = data.dst;
            }
        }
    }
}
