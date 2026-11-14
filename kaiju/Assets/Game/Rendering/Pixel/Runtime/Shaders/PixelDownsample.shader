// Downsample stage: box filter from the supersampled camera target to the internal resolution (R-003 §7).
Shader "Hidden/Game/Pixel/Downsample"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off
        Pass
        {
            Name "PixelDownsample"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "PixelCommon.hlsl"

            float4 Frag(Varyings input) : SV_Target
            {
                int2 pix = PixelCoord(input.texcoord);
                int n = int(_PixelTargetSize.z);
                float3 sum = 0;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                        sum += LOAD_TEXTURE2D_X(_BlitTexture, pix * n + int2(x, y)).rgb;
                return float4(sum / float(n * n), 1);
            }
            ENDHLSL
        }
    }
}
