// Haze stage: aerial perspective in posterised bands by real radial distance (R-003 §5), and the sky gradient where
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

            float4 _PixelHaze; // x = beta (1/m), y = band count, z = seam width (bands), w = airlight value (sRGB grey)
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
                float dist = z * length(viewRay);
                float transmittance = exp(-_PixelHaze.x * dist);
                float band = clamp(floor(bands * (1.0 - transmittance) + _PixelHaze.z * (threshold - 0.5)), 0.0, bands - 1.0);
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
