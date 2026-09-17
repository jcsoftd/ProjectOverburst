Shader "OVERBURST/VFX/Chain Electricity Additive"
{
    Properties
    {
        [HDR] _TintColor ("Tint", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Range(0, 8)) = 3
        _EdgePower ("Edge Softness", Range(0.5, 8)) = 2.2
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+20"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "LightningAdditive"
            Blend SrcAlpha One
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TintColor;
                half _Intensity;
                half _EdgePower;
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
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half center = saturate(1.0h - abs(input.uv.y * 2.0h - 1.0h));
                half softEdge = pow(center, _EdgePower);
                half longitudinalPulse = 0.9h + 0.1h * sin(input.uv.x * 31.0h + _Time.y * 38.0h);
                half alpha = input.color.a * _TintColor.a * softEdge;
                half3 color = input.color.rgb * _TintColor.rgb * _Intensity * longitudinalPulse;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
