Shader "Overburst/Appearance/ContactShadow"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION;float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0; };
            Varyings vert(Attributes i){Varyings o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;return o;}
            half4 frag(Varyings i):SV_Target
            {
                float2 centered=i.uv*2-1;
                float coverage=saturate(1-dot(centered,centered));
                return half4(0,0,0,.30*coverage*coverage*coverage);
            }
            ENDHLSL
        }
    }
}
