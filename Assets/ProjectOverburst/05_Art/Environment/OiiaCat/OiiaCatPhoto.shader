Shader "Overburst/OIIA Reference Photo"
{
    Properties
    {
        _MainTex ("Original reference frame", 2D) = "white" {}
        _UvRect ("Cat bounds in the original frame", Vector) = (0,0,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float4 _UvRect;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = _UvRect.xy + input.uv * _UvRect.zw;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half green = color.g - max(color.r, color.b);
                color.a *= 1 - smoothstep(.06, .22, green);
                color.g = min(color.g, max(color.r, color.b) + .04);
                return color;
            }
            ENDHLSL
        }
    }
}
