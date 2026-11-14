// Capture-path smoke test (T-004 step 1): one full-screen Render Graph pass. Inverts the scene and draws a
// magenta marker in the top-left corner so the capture orientation can be checked by a script.
Shader "Hidden/Game/Pixel/Smoke"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off
        Pass
        {
            Name "PixelSmoke"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half3 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).rgb;
                if (uv.x < 0.1 && uv.y > 0.9) return half4(1, 0, 1, 1);
                return half4(1 - saturate(c), 1);
            }
            ENDHLSL
        }
    }
}
