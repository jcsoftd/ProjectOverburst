Shader "OVERBURST/Player/Dash Afterimage"
{
    Properties
    {
        [PerRendererData] _Tint("Tint", Color) = (.65,.82,1,.2)
        [PerRendererData] _AccentTint("Edge Tint", Color) = (.65,.82,1,.2)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half4 _AccentTint;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions=GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS=positions.positionCS; output.positionWS=positions.positionWS;
                output.normalWS=TransformObjectToWorldNormal(input.normalOS);
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                half rim=pow(1-saturate(abs(dot(normalize(input.normalWS),GetWorldSpaceNormalizeViewDir(input.positionWS)))),2);
                return half4(lerp(_Tint.rgb,_AccentTint.rgb,rim)*(.75+rim*.25),_Tint.a*(.3+rim*.7));
            }
            ENDHLSL
        }
    }
}
