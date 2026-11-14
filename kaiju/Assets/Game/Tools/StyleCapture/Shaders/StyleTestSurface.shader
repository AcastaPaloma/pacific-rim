// Surface shader of the neutral style test scene (T-004): flat value, one fixed sun direction, hemisphere ambient,
// no shadows, no textures, so captures are deterministic and every tone comes from the palette pipeline.
// Lighting is computed on sRGB-encoded values ("value space") and written as linear colour.
Shader "Game/StyleTest/Surface"
{
    Properties
    {
        _Value ("Albedo value (sRGB grey)", Range(0, 1)) = 0.3
        _TintSrgb ("Tint (sRGB, not converted)", Vector) = (1, 1, 1, 0)
        _Unlit ("Unlit (1 = value is written as is)", Float) = 0
        _SunDir ("Direction to the sun (world)", Vector) = (0.42, 0.64, -0.64, 0)
        _Light ("Ambient from below, ambient from above, sun", Vector) = (0.35, 0.45, 0.55, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _Value;
            float4 _TintSrgb;
            float _Unlit;
            float4 _SunDir;
            float4 _Light;
        CBUFFER_END
        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
        struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
        Varyings Vert(Attributes input)
        {
            Varyings o;
            o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            o.normalWS = TransformObjectToWorldNormal(input.normalOS);
            return o;
        }
        ENDHLSL
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float ambient = lerp(_Light.x, _Light.y, n.y * 0.5 + 0.5);
                float lit = _Unlit > 0.5 ? 1.0 : ambient + _Light.z * saturate(dot(n, normalize(_SunDir.xyz)));
                float3 value = saturate(_Value * _TintSrgb.rgb * lit);
                return float4(SRGBToLinear(value), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Frag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
