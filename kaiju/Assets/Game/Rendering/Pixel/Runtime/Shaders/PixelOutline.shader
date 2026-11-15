// Outline stage (optional, off in M0): relative depth edges at the internal resolution (R-003 §6), near field only.
// Runs before Quantise, so the darkened edge colour is mapped to a palette colour like everything else.
Shader "Hidden/Game/Pixel/Outline"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off
        Pass
        {
            Name "PixelOutline"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "PixelCommon.hlsl"

            float4 _PixelOutline; // x = relative depth threshold, y = max distance (m), z = strength

            float DepthOrFar(int2 pix)
            {
                float z = PixelEyeDepth(clamp(pix, int2(0, 0), int2(_PixelTargetSize.xy) - 1));
                return z < 0.0 ? 1e7 : z;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                int2 pix = PixelCoord(input.texcoord);
                float3 colour = PixelLoadSource(pix).rgb;
                float d00 = DepthOrFar(pix), d11 = DepthOrFar(pix + int2(1, 1));
                float d10 = DepthOrFar(pix + int2(1, 0)), d01 = DepthOrFar(pix + int2(0, 1));
                float nearest = min(min(d00, d11), min(d10, d01));
                float edge = max(abs(d00 - d11), abs(d10 - d01)) / max(nearest, 1e-3);
                // The edge belongs to the nearer surface: only darken texels on the near side of the step.
                if (edge > _PixelOutline.x && nearest < _PixelOutline.y && d00 <= nearest * (1.0 + _PixelOutline.x))
                    colour *= 1.0 - _PixelOutline.z;
                return float4(colour, 1);
            }
            ENDHLSL
        }
    }
}
