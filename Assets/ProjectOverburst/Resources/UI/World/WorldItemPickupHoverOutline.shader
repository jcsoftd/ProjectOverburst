Shader "OVERBURST/UI/WorldItemPickupHoverOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1.0, 0.72, 0.18, 1.0)
        _OutlineWidthPixels ("Outline Width Pixels", Range(0.5, 4.0)) = 3.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry+100"
        }

        Pass
        {
            Name "PickupHoverOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidthPixels;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float4 positionCS = TransformObjectToHClip(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 normalVS = TransformWorldToViewDir(normalWS, true);
                float2 projectedNormal = normalVS.xy;
                float lengthSquared = dot(projectedNormal, projectedNormal);
                projectedNormal = lengthSquared > 1e-8
                    ? projectedNormal * rsqrt(lengthSquared)
                    : float2(0.0, 0.0);
                float2 pixelToNdc = 2.0 / max(_ScaledScreenParams.xy, float2(1.0, 1.0));
                positionCS.xy += projectedNormal * _OutlineWidthPixels * pixelToNdc * positionCS.w;
                output.positionCS = positionCS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
