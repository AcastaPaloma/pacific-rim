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
            float4 _PixelPresentMode;  // x = 1: decode sRGB to linear (target encodes on write); y = 1: bilinear control; z = 1: depth-aware shift (T-180), 2: layered slabs (T-181d); w = 1: write the log-coded depth instead (capture only)
            TEXTURE2D(_PixelInternalDepthTex);
            // x = reference depth zRef (m), yz = sub-texel remainder (texels, unrounded), w = clamp (output pixels = guard * scale). Mirrors Maths/PixelDepthShift.cs.
            float4 _PixelPresentDepth;

            // Layered path (T-181d): slab colour and eye depth (1e30 = sky), per-slab present shift and the radial slab boundaries.
            TEXTURE2D(_PixelSlabColour0); TEXTURE2D(_PixelSlabColour1); TEXTURE2D(_PixelSlabColour2);
            TEXTURE2D(_PixelSlabDepth0); TEXTURE2D(_PixelSlabDepth1); TEXTURE2D(_PixelSlabDepth2);
            float4 _PixelSlabShift01;  // xy = near shift, zw = main shift (output pixels)
            float4 _PixelSlabShift2;   // xy = far shift, z = near / main radial boundary (m), w = main / far radial boundary (m)

            // Depth-aware shift: surface at depth d shifts by round(k * (zRef / d) * remainder) output pixels (ParallaxShiftMath.DepthShiftPx).
            float2 PixelDepthShiftPx(float d)
            {
                float rho = _PixelPresentDepth.x / d;
                return clamp(round(_PixelPresent.x * rho * _PixelPresentDepth.yz), -_PixelPresentDepth.w, _PixelPresentDepth.w);
            }

            // Output pixel o takes the nearest texel whose own shift lands on o (floor((o + S(d)) / k) + guard == t); a gap (no texel lands) takes the farther sample.
            // Window = every texel any admissible shift can reach (guard texels each way). Depth <= 0 or NaN counts as infinitely far (sky).
            float3 PixelDepthShiftColour(float2 o, out float depthOut)
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
                depthOut = landed ? bestNear : bestFar;
                return LOAD_TEXTURE2D(_PixelInternalTex, landed ? nearT : farT).rgb;
            }

            float4 SlabColourAt(int s, int2 t)
            {
                if (s == 0) return LOAD_TEXTURE2D(_PixelSlabColour0, t);
                if (s == 1) return LOAD_TEXTURE2D(_PixelSlabColour1, t);
                return LOAD_TEXTURE2D(_PixelSlabColour2, t);
            }

            float SlabDepthAt(int s, int2 t)
            {
                if (s == 0) return LOAD_TEXTURE2D(_PixelSlabDepth0, t).r;
                if (s == 1) return LOAD_TEXTURE2D(_PixelSlabDepth1, t).r;
                return LOAD_TEXTURE2D(_PixelSlabDepth2, t).r;
            }

            // Layered composite for output pixel o (mirrors PixelLayerPlan.Composite): each slab is sampled at its own shift; the first slab, near to far, whose sample is
            // covered and lies in its own radial range wins; a gap takes the covered sample with the smallest radial distance; nothing covered is the far slab (sky).
            // Radial distance = eye depth * length of the texel's view ray (the haze bands are radial).
            float3 PixelLayeredColour(float2 o, out float depthOut, out float slabCode)
            {
                float2 maxT = _PixelPresentSize.zw - 1.0;
                float2 shift[3] = { _PixelSlabShift01.xy, _PixelSlabShift01.zw, _PixelSlabShift2.xy };
                float lo[3] = { 0.0, _PixelSlabShift2.z, _PixelSlabShift2.w };
                float hi[3] = { _PixelSlabShift2.z, _PixelSlabShift2.w, 3.4e38 };
                int2 texel[3]; float depth[3];
                int pick = -1, smallest = -1; float smallestRadial = 3.4e38;
                [unroll] for (int i = 0; i < 3; i++)
                {
                    float2 tf = clamp(floor((o + shift[i]) / _PixelPresent.x) + _PixelPresent.y, 0.0, maxT);
                    texel[i] = int2(tf);
                    depth[i] = SlabDepthAt(i, texel[i]);
                    if (depth[i] > 0.0 && depth[i] < 1.0e29)
                    {
                        float2 ndc = (tf + 0.5) / _PixelPresentSize.zw * 2.0 - 1.0;
                        float radial = depth[i] * length(float3(ndc * _PixelTanHalf.xy, 1.0));
                        if (pick < 0 && radial >= lo[i] && radial < hi[i]) pick = i;
                        if (radial < smallestRadial) { smallestRadial = radial; smallest = i; }
                    }
                }
                // Slab-id channel (T-196g, capture-only, _PixelPresentMode.w = 2): 1 + slab, +4 when no slab was covered and owned at its own texel (the gap rule chose), +8 when
                // the chosen sample was clamped into the guard band edge. Written to alpha; colour and every default output are unchanged.
                bool gap = pick < 0;
                if (gap) pick = smallest >= 0 ? smallest : 2;
                float2 rawT = floor((o + shift[pick]) / _PixelPresent.x) + _PixelPresent.y;
                slabCode = (1.0 + pick + (gap ? 4.0 : 0.0) + (any(rawT != float2(texel[pick])) ? 8.0 : 0.0)) / 255.0;
                depthOut = depth[pick];
                return SlabColourAt(pick, texel[pick]).rgb;
            }

            // Capture-only output (PixelCameraBinder.controlDepthOutput): the eye depth of the shown texel as a 24-bit log code in the three colour bytes,
            // v = round(16777214 * ln(z / 0.1) / ln(1e7)), 16777215 = sky / empty.
            float3 PixelDepthCode(float z)
            {
                float v = (z > 0.0 && z < 1.0e29) ? floor(clamp(log(max(z, 0.1) / 0.1) / log(1.0e7), 0.0, 1.0) * 16777214.0 + 0.5) : 16777215.0;
                float r = floor(v / 65536.0), g = floor((v - r * 65536.0) / 256.0), b = v - r * 65536.0 - g * 256.0;
                return float3(r, g, b) / 255.0;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 o = floor(input.texcoord * _PixelPresentSize.xy);
                float3 c;
                float depthOut = 3.0e38;
                float slabCode = 1.0;   // alpha 255 = no slab information
                if (_PixelPresentMode.z > 1.5 && _PixelPresentMode.y < 0.5)
                {
                    c = PixelLayeredColour(o, depthOut, slabCode);
                }
                else if (_PixelPresentMode.z > 0.5 && _PixelPresentMode.y < 0.5)
                {
                    c = PixelDepthShiftColour(o, depthOut);
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
                    int2 ti = int2(clamp(t, 0.0, _PixelPresentSize.zw - 1.0));
                    c = LOAD_TEXTURE2D(_PixelInternalTex, ti).rgb;
                    if (_PixelPresentMode.w > 0.5 && _PixelPresentMode.w < 1.5) depthOut = LOAD_TEXTURE2D(_PixelInternalDepthTex, ti).r;
                }
                if (_PixelPresentMode.w > 0.5 && _PixelPresentMode.w < 1.5) return float4(PixelDepthCode(depthOut), 1);
                if (_PixelPresentMode.x > 0.5) c = PixelSrgbToLinear(c);
                return float4(c, _PixelPresentMode.w > 1.5 ? slabCode : 1.0);
            }
            ENDHLSL
        }
    }
}
