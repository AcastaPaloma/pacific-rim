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
            float4 _PixelPresentMode;  // x = 1: decode sRGB to linear (target encodes on write); y = 1: bilinear control; z = 1: depth-aware shift (T-180)
            TEXTURE2D(_PixelInternalDepthTex);
            // x = reference depth zRef (m), yz = sub-texel remainder (texels, unrounded), w = clamp (output pixels = guard * scale). Mirrors Maths/PixelDepthShift.cs.
            float4 _PixelPresentDepth;

            // Depth-aware shift: surface at depth d shifts by round(k * (zRef / d) * remainder) output pixels (ParallaxShiftMath.DepthShiftPx).
            float2 PixelDepthShiftPx(float d)
            {
                float rho = _PixelPresentDepth.x / d;
                return clamp(round(_PixelPresent.x * rho * _PixelPresentDepth.yz), -_PixelPresentDepth.w, _PixelPresentDepth.w);
            }

            // Output pixel o takes the nearest texel whose own shift lands on o (floor((o + S(d)) / k) + guard == t); a gap (no texel lands) takes the farther sample.
            // Window = every texel any admissible shift can reach (guard texels each way). Depth <= 0 or NaN counts as infinitely far (sky).
            float3 PixelDepthShiftColour(float2 o)
            {
                float k = _PixelPresent.x, guardT = _PixelPresent.y, lim = _PixelPresentDepth.w;
                float2 tLo = floor((o - lim) / k) + guardT, tHi = floor((o + lim) / k) + guardT;
                float2 maxT = _PixelPresentSize.zw - 1.0;
                float bestNear = 3.4e38, bestFar = -1.0; bool landed = false;
                int2 nearT = int2(clamp(floor((o + _PixelPresent.zw) / k) + guardT, 0.0, maxT)), farT = nearT;
                int2 lo = int2(clamp(tLo, 0.0, maxT)), hi = int2(clamp(tHi, 0.0, maxT));
                [loop] for (int y = lo.y; y <= hi.y; y++)
                {
                    [loop] for (int x = lo.x; x <= hi.x; x++)
                    {
                        float d = LOAD_TEXTURE2D(_PixelInternalDepthTex, int2(x, y)).r;
                        d = (d > 0.0 && d < 3.0e38) ? d : 3.0e38;
                        float2 s = PixelDepthShiftPx(d);
                        float2 land = floor((o + s) / k) + guardT;
                        if (all(land == float2(x, y)) && d < bestNear) { bestNear = d; nearT = int2(x, y); landed = true; }
                        if (d > bestFar) { bestFar = d; farT = int2(x, y); }
                    }
                }
                return LOAD_TEXTURE2D(_PixelInternalTex, landed ? nearT : farT).rgb;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 o = floor(input.texcoord * _PixelPresentSize.xy);
                float3 c;
                if (_PixelPresentMode.z > 0.5 && _PixelPresentMode.y < 0.5)
                {
                    c = PixelDepthShiftColour(o);
                }
                else if (_PixelPresentMode.y > 0.5)
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
