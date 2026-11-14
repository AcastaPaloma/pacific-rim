// Haze stage: aerial perspective in posterised bands by real radial distance (bands evenly spaced in log distance,
// HazeMath.cs), and the sky gradient where
// there is no geometry. Works in scene space: the colour is pulled toward the airlight value; the palette LUT then
// grades scene value onto the profile's haze ramp, so far layers land on the lighter ramp colours.
Shader "Hidden/Game/Pixel/Haze"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off
        Pass
        {
            Name "PixelHaze"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "PixelCommon.hlsl"

            float4 _PixelHaze;  // x = start distance (m), y = band count, z = largest seam width (bands), w = airlight value (sRGB grey)
            float4 _PixelHaze2; // x = 1 / ln(full distance / start distance), y = seam half width in texels, z = curve gamma, w = relative depth tolerance for "same surface"

            // Continuous haze level in [0, bands - 1] at a radial distance (HazeMath.Level).
            float HazeLevel(float dist)
            {
                return (_PixelHaze.y - 1.0) * pow(saturate(log(max(dist, _PixelHaze.x) / _PixelHaze.x) * _PixelHaze2.x), _PixelHaze2.z);
            }
            float4 _PixelSky;  // x = horizon value, y = zenith value, z = elevation (rad) of the zenith value, w = direction (+1 normal, -1 reversed control)

            float4 Frag(Varyings input) : SV_Target
            {
                int2 pix = PixelCoord(input.texcoord);
                float3 colour = PixelLoadSource(pix).rgb;
                float threshold = PixelThreshold(pix);
                float z = PixelEyeDepth(pix);
                float3 viewRay = PixelViewRay(pix);
                if (z < 0.0)
                {
                    float elevation = asin(clamp(normalize(PixelWorldRay(viewRay)).y, -1.0, 1.0));
                    float t = saturate(elevation / max(_PixelSky.z, 1e-4));
                    float v = lerp(_PixelSky.x, _PixelSky.y, t);
                    return float4(PixelSrgbToLinear(float3(v, v, v)), 1);
                }
                float bands = _PixelHaze.y;
                float level = HazeLevel(z * length(viewRay));
                // Seam: the band boundary is dithered over a few texels on each side, measured along the surface, so a
                // receding surface gets a dithered edge of constant width and a face at one distance takes one band.
                static const int2 offsets[4] = { int2(1, 0), int2(-1, 0), int2(0, 1), int2(0, -1) };
                float gradient = 0.0;   // largest change of level to a neighbouring texel on the same surface
                for (int i = 0; i < 4; i++)
                {
                    int2 q = clamp(pix + offsets[i], int2(0, 0), int2(_PixelTargetSize.xy) - 1);
                    float zn = PixelEyeDepth(q);
                    if (zn > 0.0 && abs(zn - z) < _PixelHaze2.w * z) gradient = max(gradient, abs(HazeLevel(zn * length(PixelViewRay(q))) - level));
                }
                float boundary = floor(level + 0.5);                                   // nearest band boundary
                float halfWidth = min(_PixelHaze2.y * gradient, 0.5 * _PixelHaze.z);   // seam half width in bands
                float band = floor(level);
                if (_PixelThresholdInfo.x > 0.5 && halfWidth > 1e-5 && abs(level - boundary) < halfWidth)
                    band = threshold < 0.5 + (level - boundary) / (2.0 * halfWidth) ? boundary : boundary - 1.0;
                band = clamp(band, 0.0, bands - 1.0);
                float amount = bands > 1.0 ? band / (bands - 1.0) : 0.0;
                if (_PixelSky.w < 0.0) amount = 1.0 - amount; // reversed-haze negative control (near light, far dark)
                float3 s = PixelLinearToSrgb(saturate(colour));
                s = lerp(s, _PixelHaze.www, amount);
                return float4(PixelSrgbToLinear(s), 1);
            }
            ENDHLSL
        }
    }
}
