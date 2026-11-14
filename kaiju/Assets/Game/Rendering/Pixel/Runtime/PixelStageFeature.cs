using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Rendering.Pixel
{
    /// <summary>
    /// Per-camera, per-frame state shared by the stages through the Render Graph frame data (no shader globals):
    /// which stages have run, the dither threshold texture, and what the current colour texture holds.
    /// </summary>
    public sealed class PixelFrameContext : ContextItem
    {
        public int executedMask;
        public bool hasThreshold;
        public TextureHandle threshold;
        public bool colourIsPalette;   // the colour texture holds sRGB-encoded palette bytes with the index in alpha
        public bool downsampled;       // the colour texture is at the internal resolution although the camera supersamples
        public bool colourIsEncoded;   // the colour texture holds sRGB-encoded bytes that are not palette colours (continuous-tone control)

        public override void Reset()
        {
            executedMask = 0; hasThreshold = false; threshold = TextureHandle.nullHandle; colourIsPalette = false; downsampled = false; colourIsEncoded = false;
        }
    }

    /// <summary>Data of one full-screen stage pass. One class for every stage keeps the render function static and allocation-free.</summary>
    public sealed class PixelPassData
    {
        public TextureHandle src, dst, dst2, depth, threshold;
        public bool hasDepth, hasThreshold;
        public Material material;
        public MaterialPropertyBlock block;
        public Vector4 targetSize, thresholdInfo, camPos, camRight, camUp, camForward, tanHalf;
        public int id0, id1, id2; public Vector4 v0, v1, v2;
        public int texId0, texId1; public Texture tex0, tex1;
    }

    static class PixelIds
    {
        public static readonly int BlitTexture = Shader.PropertyToID("_BlitTexture");
        public static readonly int BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
        public static readonly int DepthTex = Shader.PropertyToID("_PixelDepthTex");
        public static readonly int ThresholdTex = Shader.PropertyToID("_PixelThresholdTex");
        public static readonly int TargetSize = Shader.PropertyToID("_PixelTargetSize");
        public static readonly int ThresholdInfo = Shader.PropertyToID("_PixelThresholdInfo");
        public static readonly int CamPos = Shader.PropertyToID("_PixelCamPos");
        public static readonly int CamRight = Shader.PropertyToID("_PixelCamRight");
        public static readonly int CamUp = Shader.PropertyToID("_PixelCamUp");
        public static readonly int CamForward = Shader.PropertyToID("_PixelCamForward");
        public static readonly int TanHalf = Shader.PropertyToID("_PixelTanHalf");
        public static readonly int Mask = Shader.PropertyToID("_PixelMask");
        public static readonly int DitherParams = Shader.PropertyToID("_PixelDitherParams");
        public static readonly int DitherOrigin = Shader.PropertyToID("_PixelDitherOrigin");
        public static readonly int DitherGradient = Shader.PropertyToID("_PixelDitherGradient");
        public static readonly int Haze = Shader.PropertyToID("_PixelHaze");
        public static readonly int Haze2 = Shader.PropertyToID("_PixelHaze2");
        public static readonly int Sky = Shader.PropertyToID("_PixelSky");
        public static readonly int Outline = Shader.PropertyToID("_PixelOutline");
        public static readonly int Lut = Shader.PropertyToID("_PixelLut");
        public static readonly int Palette = Shader.PropertyToID("_PixelPalette");
        public static readonly int Quantise = Shader.PropertyToID("_PixelQuantise");
        public static readonly int Store = Shader.PropertyToID("_PixelStore");
        public static readonly int InternalTex = Shader.PropertyToID("_PixelInternalTex");
        public static readonly int Present = Shader.PropertyToID("_PixelPresent");
        public static readonly int PresentSize = Shader.PropertyToID("_PixelPresentSize");
        public static readonly int PresentMode = Shader.PropertyToID("_PixelPresentMode");
    }

    /// <summary>One profiling sampler per pass name (Pixel.Dither, Pixel.Haze, ...), shared by every renderer, so tools can read CPU and GPU time per pass.</summary>
    public static class PixelProfiling
    {
        static readonly Dictionary<string, ProfilingSampler> s_Samplers = new Dictionary<string, ProfilingSampler>();
        public static ProfilingSampler Get(string name)
        {
            if (!s_Samplers.TryGetValue(name, out var sampler)) { sampler = new ProfilingSampler(name); s_Samplers[name] = sampler; }
            return sampler;
        }
        public static IEnumerable<KeyValuePair<string, ProfilingSampler>> All => s_Samplers;
    }

    public static class PixelLog
    {
        static readonly HashSet<string> s_Seen = new HashSet<string>();
        /// <summary>Count of order violations seen since the last reset (tests assert on it).</summary>
        public static int OrderViolations;
        public static void WarnOnce(string message) { if (s_Seen.Add(message)) Debug.LogWarning("[Pixel] " + message); }
        public static void Reset() { s_Seen.Clear(); OrderViolations = 0; }
    }

    /// <summary>
    /// Base of every world-camera stage: one renderer feature per stage (MODULARITY §4.7). Features are listed on the
    /// world renderer in execution order; all inject at the same event, so the list order is the stage order.
    /// A stage runs only for cameras with a PixelCameraBinder whose flag state has the stage on.
    /// </summary>
    public abstract class PixelStageFeature : ScriptableRendererFeature
    {
        [Tooltip("Stage shader; assigned by PixelPipelineInstaller.")]
        public Shader shader;
        Material m_Material;
        PixelStagePass m_Pass;

        public abstract PixelStageId Stage { get; }
        protected abstract PixelStagePass CreatePass();

        public override void Create()
        {
            m_Pass = CreatePass();
            m_Pass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var type = renderingData.cameraData.cameraType;
            if (shader == null || type == CameraType.Preview || type == CameraType.Reflection) return;
            if (!PixelCameraBinder.TryGet(renderingData.cameraData.camera, out var binder) || !binder.StageEnabled(Stage)) return;
            if (m_Material == null) m_Material = CoreUtils.CreateEngineMaterial(shader);
            m_Pass.Setup(binder, m_Material);
            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(m_Material);
            m_Material = null;
        }
    }

    public abstract class PixelStagePass : ScriptableRenderPass
    {
        protected PixelCameraBinder binder;
        protected Material material;
        readonly PixelStageId m_Stage;
        readonly string m_PassName;
        readonly MaterialPropertyBlock m_Block = new MaterialPropertyBlock();

        protected PixelStagePass(PixelStageId stage, ScriptableRenderPassInput inputs)
        {
            m_Stage = stage;
            m_PassName = "Pixel." + stage;
            profilingSampler = PixelProfiling.Get(m_PassName);
            requiresIntermediateTexture = true;   // never read or write the camera target directly
            ConfigureInput(inputs);
        }

        public void Setup(PixelCameraBinder cameraBinder, Material stageMaterial) { binder = cameraBinder; material = stageMaterial; }

        public sealed override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer) return;
            var context = frameData.GetOrCreate<PixelFrameContext>();
            if (!PixelStageOrder.CanRunAfter(context.executedMask, m_Stage, out string reason))
            {
                PixelLog.OrderViolations++;
                PixelLog.WarnOnce("stage " + m_Stage + " skipped: illegal stage order (" + reason + ")");
                return;
            }
            Record(renderGraph, resources, context);
            context.executedMask |= 1 << (int)m_Stage;
        }

        protected abstract void Record(RenderGraph renderGraph, UniversalResourceData resources, PixelFrameContext context);

        /// <summary>Size of the current colour texture in pixels per internal texel (the supersample factor until Downsample has run).</summary>
        protected int ColourScale(PixelFrameContext context) => context.downsampled ? 1 : binder.Layout.Supersample;

        protected TextureDesc ColourDesc(RenderGraph renderGraph, UniversalResourceData resources, int scale, string name)
        {
            var desc = renderGraph.GetTextureDesc(resources.activeColorTexture);
            desc.name = name; desc.clearBuffer = false; desc.msaaSamples = MSAASamples.None; desc.filterMode = FilterMode.Point;
            desc.width = binder.Layout.TargetWidth * scale; desc.height = binder.Layout.TargetHeight * scale;
            return desc;
        }

        /// <summary>Opens a raster pass and fills the inputs every stage shares. <paramref name="targetScale"/> is target pixels per internal texel.</summary>
        protected IRasterRenderGraphBuilder Begin(RenderGraph renderGraph, UniversalResourceData resources, PixelFrameContext context,
            TextureHandle source, int sourceScale, TextureHandle destination, int targetScale, bool useDepth, bool useThreshold, out PixelPassData data)
        {
            var builder = renderGraph.AddRasterRenderPass(m_PassName, out data, profilingSampler);
            var layout = binder.Layout;
            data.material = material; data.block = m_Block;
            data.src = source; data.dst = destination; data.dst2 = TextureHandle.nullHandle;
            data.hasDepth = useDepth; data.hasThreshold = useThreshold && context.hasThreshold;
            data.depth = useDepth ? resources.cameraDepthTexture : TextureHandle.nullHandle;
            data.threshold = data.hasThreshold ? context.threshold : TextureHandle.nullHandle;
            data.targetSize = new Vector4(layout.TargetWidth * targetScale, layout.TargetHeight * targetScale, sourceScale / targetScale, layout.Supersample / targetScale);
            data.thresholdInfo = new Vector4(data.hasThreshold ? 1 : 0, targetScale, 0, 0);
            var t = binder.Camera.transform;
            data.camPos = t.position; data.camRight = t.right; data.camUp = t.up; data.camForward = t.forward;
            data.tanHalf = binder.TargetTanHalfFov();
            data.id0 = data.id1 = data.id2 = 0; data.texId0 = data.texId1 = 0; data.tex0 = data.tex1 = null;
            if (source.IsValid()) builder.UseTexture(source, AccessFlags.Read);
            if (useDepth) builder.UseTexture(data.depth, AccessFlags.Read);
            if (data.hasThreshold) builder.UseTexture(data.threshold, AccessFlags.Read);
            builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
            builder.SetRenderFunc(static (PixelPassData d, RasterGraphContext ctx) => Execute(d, ctx));
            return builder;
        }

        internal static void Execute(PixelPassData d, RasterGraphContext ctx)
        {
            var b = d.block;
            b.Clear();
            bool flip = false;
            if (d.src.IsValid())
            {
                b.SetTexture(PixelIds.BlitTexture, (RTHandle)d.src);
                flip = ctx.GetTextureUVOrigin(d.src) != ctx.GetTextureUVOrigin(d.dst);
            }
            if (d.hasDepth) b.SetTexture(PixelIds.DepthTex, (RTHandle)d.depth);
            if (d.hasThreshold) b.SetTexture(PixelIds.ThresholdTex, (RTHandle)d.threshold);
            b.SetVector(PixelIds.BlitScaleBias, flip ? new Vector4(1, -1, 0, 1) : new Vector4(1, 1, 0, 0));
            b.SetVector(PixelIds.TargetSize, d.targetSize);
            b.SetVector(PixelIds.ThresholdInfo, d.thresholdInfo);
            b.SetVector(PixelIds.CamPos, d.camPos); b.SetVector(PixelIds.CamRight, d.camRight);
            b.SetVector(PixelIds.CamUp, d.camUp); b.SetVector(PixelIds.CamForward, d.camForward);
            b.SetVector(PixelIds.TanHalf, d.tanHalf);
            if (d.id0 != 0) b.SetVector(d.id0, d.v0);
            if (d.id1 != 0) b.SetVector(d.id1, d.v1);
            if (d.id2 != 0) b.SetVector(d.id2, d.v2);
            if (d.tex0 != null) b.SetTexture(d.texId0, d.tex0);
            if (d.tex1 != null) b.SetTexture(d.texId1, d.tex1);
            ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, 0, MeshTopology.Triangles, 3, 1, b);
        }
    }
}
