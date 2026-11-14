// Present stage: point upscale of the internal image at an exact integer scale, shifted by whole output pixels for
// sub-pixel compensation (R-003 §2, §7). Output pixel o shows internal texel floor((o + shift) / scale) + guard.
Shader "Hidden/Game/Pixel/Present"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off
        Pass
        {
            Name "PixelPresent"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "PixelCommon.hlsl"

            TEXTURE2D(_PixelInternalTex);
            float4 _PixelPresent;      // x = integer scale, y = guard texels, zw = shift in output pixels
            float4 _PixelPresentSize;  // xy = output size, zw = internal target size (guard included)
            float4 _PixelPresentMode;  // x = 1: decode sRGB to linear (target encodes on write); y = 1: bilinear control

            float4 Frag(Varyings input) : SV_Target
            {
                float2 o = floor(input.texcoord * _PixelPresentSize.xy);
                float3 c;
                if (_PixelPresentMode.y > 0.5)
                {
                    // Negative control only (C1/C2): a filtered upscale, which must fail grid integrity and adherence.
                    float2 uv = ((o + 0.5 + _PixelPresent.zw) / _PixelPresent.x + _PixelPresent.y) / _PixelPresentSize.zw;
                    c = SAMPLE_TEXTURE2D_LOD(_PixelInternalTex, sampler_LinearClamp, uv, 0).rgb;
                }
                else
                {
                    float2 t = floor((o + _PixelPresent.zw) / _PixelPresent.x) + _PixelPresent.y;
                    c = LOAD_TEXTURE2D(_PixelInternalTex, int2(clamp(t, 0.0, _PixelPresentSize.zw - 1.0))).rgb;
                }
                if (_PixelPresentMode.x > 0.5) c = PixelSrgbToLinear(c);
                return float4(c, 1);
            }
            ENDHLSL
        }
    }
}
