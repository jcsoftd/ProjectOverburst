Shader "OVERBURST/DashHeavyFocus"
{
    Properties { _Intensity ("Intensity",Float)=1.25 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
            float _Intensity;
            CBUFFER_END
            Varyings vert(Attributes input)
            {
                Varyings output;output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.color=input.color;output.uv=input.uv;return output;
            }
            half4 frag(Varyings input):SV_Target
            {
                float edge=saturate(1.0-abs(input.uv.y*2.0-1.0));
                return half4(input.color.rgb*_Intensity,input.color.a*edge*edge);
            }
            ENDHLSL
        }
    }
}
