// Quantise stage: palette LUT lookup (R-003 §3–4). Each LUT cell holds two palette indices and a mix ratio. Where the
// gradient mask is set the dither threshold picks one of the two inside a transition band around the boundary; on
// flat faces (mask 0) the texel takes the nearer entry. Either way every output texel is an exact palette colour. Output: sRGB-encoded palette bytes in
// RGB and the palette index / 255 in alpha, in an R8G8B8A8_UNorm target.
Shader "Hidden/Game/Pixel/Quantise"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off
        Pass
        {
            Name "PixelQuantise"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "PixelCommon.hlsl"

            TEXTURE3D(_PixelLut);
            TEXTURE2D(_PixelPalette);
            float4 _PixelQuantise; // x = LUT size, y = dither spread, z = temporal-noise control seed (0 = off), w = 1: continuous-tone control

            float4 Frag(Varyings input) : SV_Target
            {
                int2 pix = PixelCoord(input.texcoord);
                float3 s = PixelLinearToSrgb(saturate(PixelLoadSource(pix).rgb));
                int size = int(_PixelQuantise.x);
                int3 cell = min(int3(s * float(size)), size - 1);
                float4 entry = LOAD_TEXTURE3D(_PixelLut, cell);
                int a = int(round(entry.r * 255.0)), b = int(round(entry.g * 255.0));
                float ratio = entry.b;
                float spread = _PixelQuantise.y;
                float nearest = step(0.5, ratio);
                float shaped = spread <= 1e-6 ? nearest : saturate((ratio - 0.5) / spread + 0.5);
                if (PixelGradientMask(pix) < 0.5) shaped = nearest;   // flat face: no mixing
                if (_PixelQuantise.w > 0.5)
                {
                    // Negative control only: the same grade as a continuous-tone colour image (no palette, no dither).
                    float3 ca = LOAD_TEXTURE2D(_PixelPalette, int2(a, 0)).rgb, cb = LOAD_TEXTURE2D(_PixelPalette, int2(b, 0)).rgb;
                    return float4(PixelLinearToSrgb(lerp(PixelSrgbToLinear(ca), PixelSrgbToLinear(cb), ratio)), 1.0);
                }
                float threshold = PixelThreshold(pix);
                if (_PixelQuantise.z > 0.0) // negative control only: white noise that changes every frame
                    threshold = frac(sin(dot(float2(pix) + _PixelQuantise.z * 17.0, float2(12.9898, 78.233))) * 43758.5453);
                int index = threshold < shaped ? b : a;
                return LOAD_TEXTURE2D(_PixelPalette, int2(index, 0));
            }
            ENDHLSL
        }
    }
}
