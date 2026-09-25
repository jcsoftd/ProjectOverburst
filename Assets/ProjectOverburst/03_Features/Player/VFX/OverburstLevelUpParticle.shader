Shader "ProjectOverburst/LevelUpParticle"
{
    Properties
    {
        _MainTex ("Mask", 2D) = "white" {}
        _Tint ("Tint", Color) = (1, 0.8, 0.15, 1)
        _Intensity ("Intensity", Range(0, 8)) = 1.5
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Name "LevelUp"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Tint;
                half _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half mask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).g;
                half strength = saturate(mask * input.color.a * _Tint.a);
                return half4(_Tint.rgb * input.color.rgb * strength * _Intensity, strength);
            }
            ENDHLSL
        }
    }
}
