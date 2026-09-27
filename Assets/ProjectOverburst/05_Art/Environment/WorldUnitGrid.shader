Shader "Overburst/World Unit Grid"
{
    Properties
    {
        _LineColor ("Line Color", Color) = (0.88, 0.91, 0.96, 0.24)
        _LineWidth ("Line Width In World Units", Range(0.002, 0.05)) = 0.012
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _LineColor;
                float _LineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 worldXZ : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.worldXZ = TransformObjectToWorld(input.positionOS.xyz).xz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 cell = frac(input.worldXZ);
                float2 edgeDistance = min(cell, 1.0 - cell);
                float2 pixelWidth = max(fwidth(input.worldXZ), 0.001);
                float2 gridCoverage = 1.0 - smoothstep(_LineWidth, _LineWidth + pixelWidth, edgeDistance);
                float fade = saturate(0.5 / max(pixelWidth.x, pixelWidth.y));
                return half4(_LineColor.rgb, _LineColor.a * max(gridCoverage.x, gridCoverage.y) * fade);
            }
            ENDHLSL
        }
    }
}
