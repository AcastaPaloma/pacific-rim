// Layered internal composite (T-181d, MODULE.md "Layer slabs"): builds the unshifted internal colour and depth targets from the three slab targets with the
// same rule as the layered present (PixelLayerPlan.Composite, mirrored here and in PixelPresent.shader): the first slab, near to far, whose texel is covered and
// owns its radial distance wins; a gap takes the covered texel with the smallest radial distance; nothing covered is the far slab (sky). Drawn by the binder with
// Graphics.DrawProceduralNow into the persistent internal targets, pass 0 into colour (sRGB bytes + palette index), pass 1 into eye depth (1e30 = sky).
Shader "Hidden/Game/Pixel/Composite"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_PixelSlabColour0); TEXTURE2D(_PixelSlabColour1); TEXTURE2D(_PixelSlabColour2);
        TEXTURE2D(_PixelSlabDepth0); TEXTURE2D(_PixelSlabDepth1); TEXTURE2D(_PixelSlabDepth2);
        float4 _PixelSlabShift2;   // zw = near / main and main / far radial boundaries (m)
        float4 _PixelSlabInfo;     // xy = target size (texels, guard band included), zw = tan of half the field of view of the whole target

        struct Varyings { float4 positionCS : SV_POSITION; };

        Varyings Vert(uint id : SV_VertexID)
        {
            Varyings o;
            float2 uv = float2((id << 1) & 2, id & 2);
            o.positionCS = float4(uv * 2.0 - 1.0, 0.0, 1.0);
            return o;
        }

        float SlabDepthAt(int s, int2 t)
        {
            if (s == 0) return LOAD_TEXTURE2D(_PixelSlabDepth0, t).r;
            if (s == 1) return LOAD_TEXTURE2D(_PixelSlabDepth1, t).r;
            return LOAD_TEXTURE2D(_PixelSlabDepth2, t).r;
        }

        float4 SlabColourAt(int s, int2 t)
        {
            if (s == 0) return LOAD_TEXTURE2D(_PixelSlabColour0, t);
            if (s == 1) return LOAD_TEXTURE2D(_PixelSlabColour1, t);
            return LOAD_TEXTURE2D(_PixelSlabColour2, t);
        }

        int PickSlab(int2 t)
        {
            float lo[3] = { 0.0, _PixelSlabShift2.z, _PixelSlabShift2.w };
            float hi[3] = { _PixelSlabShift2.z, _PixelSlabShift2.w, 3.4e38 };
            float2 ndc = (float2(t) + 0.5) / _PixelSlabInfo.xy * 2.0 - 1.0;
            float rayLength = length(float3(ndc * _PixelSlabInfo.zw, 1.0));
            int pick = -1, smallest = -1; float smallestRadial = 3.4e38;
            [unroll] for (int i = 0; i < 3; i++)
            {
                float d = SlabDepthAt(i, t);
                if (d > 0.0 && d < 1.0e29)
                {
                    float radial = d * rayLength;
                    if (pick < 0 && radial >= lo[i] && radial < hi[i]) pick = i;
                    if (radial < smallestRadial) { smallestRadial = radial; smallest = i; }
                }
            }
            return pick >= 0 ? pick : (smallest >= 0 ? smallest : 2);
        }
        ENDHLSL

        Pass
        {
            Name "PixelCompositeColour"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Frag(Varyings input) : SV_Target { int2 t = int2(input.positionCS.xy); return SlabColourAt(PickSlab(t), t); }
            ENDHLSL
        }

        Pass
        {
            Name "PixelCompositeDepth"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Frag(Varyings input) : SV_Target { int2 t = int2(input.positionCS.xy); return float4(SlabDepthAt(PickSlab(t), t), 0, 0, 1); }
            ENDHLSL
        }
    }
}
