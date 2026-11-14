// Store stage (the low-resolution target): writes the finished internal image as sRGB-encoded bytes with the palette
// index in alpha (255 = not palette-quantised), and eye-space linear depth in metres (1e30 = sky), into the two
// persistent targets the present stage and the capture tool read.
Shader "Hidden/Game/Pixel/Store"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off
        Pass
        {
            Name "PixelStore"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "PixelCommon.hlsl"

            float4 _PixelStore; // x = 1 when the source holds encoded palette bytes with the index in alpha, y = 1 when it holds sRGB-encoded bytes that are not palette colours (continuous-tone control)

            struct Output { float4 colour : SV_Target0; float4 depth : SV_Target1; };

            Output Frag(Varyings input)
            {
                int2 pix = PixelCoord(input.texcoord);
                float4 c = PixelLoadSource(pix);
                Output o;
                o.colour = _PixelStore.x > 0.5 ? c : (_PixelStore.y > 0.5 ? float4(c.rgb, 1.0) : float4(PixelLinearToSrgb(saturate(c.rgb)), 1.0));
                float z = PixelEyeDepth(pix);
                o.depth = float4(z < 0.0 ? 1e30 : z, 0, 0, 1);
                return o;
            }
            ENDHLSL
        }
    }
}
