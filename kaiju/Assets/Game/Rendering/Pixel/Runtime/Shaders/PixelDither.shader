// Dither stage: writes, per internal texel, the ordered-dither threshold (R) and the gradient mask (G).
// Threshold: the mask (Bayer, clustered, blue noise) and the anchoring mode are data. Modes: 0 screen, 1 direction
// (origin follows whole-texel rotation steps), 2 hybrid (surface-anchored within _PixelDitherParams.z metres,
// direction beyond it and in the sky).
// Gradient mask: 1 where the scene value differs from a neighbour on the same surface by more than
// _PixelDitherGradient.x (a shading gradient) and in the sky, 0 on flat faces. The Quantise stage mixes two palette
// entries only where the mask is 1, so flat faces stay flat and dither appears where value actually changes.
Shader "Hidden/Game/Pixel/Dither"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off
        Pass
        {
            Name "PixelDither"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "PixelCommon.hlsl"

            TEXTURE2D(_PixelMask);
            float4 _PixelDitherParams;  // x = mask size, y = anchor mode, z = hybrid far start (m), w = surface cell size in texels
            float4 _PixelDitherOrigin;  // xy = origin in texels (whole numbers)
            float4 _PixelDitherGradient;// x = smallest value step per texel that counts as a gradient (sRGB units), y = relative depth tolerance for "same surface", z = 1 when the sky is a gradient (Haze on)

            float3 ValueAt(int2 pix)
            {
                return PixelLinearToSrgb(saturate(PixelLoadSource(clamp(pix, int2(0, 0), int2(_PixelTargetSize.xy) - 1)).rgb));
            }

            // Largest value step to a 4-neighbour that lies on the same surface (depth within tolerance).
            float SurfaceGradient(int2 pix, float z, float3 value)
            {
                static const int2 offsets[4] = { int2(1, 0), int2(-1, 0), int2(0, 1), int2(0, -1) };
                float g = 0.0;
                for (int i = 0; i < 4; i++)
                {
                    int2 q = pix + offsets[i];
                    float zn = PixelEyeDepth(clamp(q, int2(0, 0), int2(_PixelTargetSize.xy) - 1));
                    if (zn > 0.0 && abs(zn - z) < _PixelDitherGradient.y * z)
                    {
                        float3 d = abs(ValueAt(q) - value);
                        g = max(g, max(d.r, max(d.g, d.b)));
                    }
                }
                return g;
            }

            float MaskAt(int2 cell)
            {
                int n = int(_PixelDitherParams.x);
                int2 m = ((cell % n) + n) % n;
                // Stored bytes are floor(256 * (rank + 0.5) / count); +0.5/256 centres them so no ratio ever equals a threshold.
                return LOAD_TEXTURE2D(_PixelMask, m).r * (255.0 / 256.0) + (0.5 / 256.0);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                int2 pix = PixelCoord(input.texcoord);
                int mode = int(_PixelDitherParams.y);
                int2 origin = mode >= 1 ? int2(_PixelDitherOrigin.xy) : int2(0, 0);
                float threshold = MaskAt(pix + origin);

                // World position of the surface under this texel (derivatives are taken outside any branch).
                float z = PixelEyeDepth(pix);
                float3 viewRay = PixelViewRay(pix);
                float3 world = _PixelCamPos.xyz + PixelWorldRay(viewRay) * max(z, 0.0);
                float3 dx = ddx(world), dy = ddy(world);
                float3 n = abs(cross(dx, dy));
                // The two world axes most parallel to the surface carry the pattern (dominant-axis triplanar).
                float2 uv = world.xz; float2 ux = dx.xz; float2 uy = dy.xz;
                if (n.x > n.y && n.x > n.z) { uv = world.zy; ux = dx.zy; uy = dy.zy; }
                else if (n.z > n.y) { uv = world.xy; ux = dx.xy; uy = dy.xy; }
                // Cell size per axis: the texel footprint rounded up to a power of two, so cells are one to two texels
                // wide at any distance and stay fixed to the surface inside an octave.
                float2 footprint = max(abs(ux), abs(uy)) * _PixelDitherParams.w;
                float2 cell = exp2(ceil(log2(max(footprint, 1e-4))));
                float surfaceThreshold = MaskAt(int2(floor(uv / cell)));

                float dist = z * length(viewRay);
                if (mode == 2 && z > 0.0 && dist < _PixelDitherParams.z) threshold = surfaceThreshold;

                float gradient = z > 0.0 ? step(_PixelDitherGradient.x, SurfaceGradient(pix, z, ValueAt(pix))) : _PixelDitherGradient.z;
                return float4(threshold, gradient, 0, 1);
            }
            ENDHLSL
        }
    }
}
