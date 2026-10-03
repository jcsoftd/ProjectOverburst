Shader "OVERBURST/DashHeavyFocus"
{
    Properties
    {
        _Intensity("Intensity", Float)=1
        _Mode("Orb mode", Float)=0
        _Glint("Glint", Float)=0
        _Tint("Tint", Color)=(1,1,1,1)
    }
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
            float _Intensity,_Mode,_Glint;
            float4 _Tint;
            CBUFFER_END
            Varyings vert(Attributes input)
            {
                Varyings output;output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.color=input.color;output.uv=input.uv;return output;
            }
            half4 frag(Varyings input):SV_Target
            {
                float2 centered=input.uv*2-1;
                float radius=dot(centered,centered);
                float orb=exp2(-radius*6)*0.15+exp2(-radius*52)*1.6;
                float glint=exp2(-abs(centered.x)*65-abs(centered.y)*7)+exp2(-abs(centered.y)*65-abs(centered.x)*7);
                orb+=glint*_Glint;
                orb*=1-smoothstep(.65,1,radius);
                float strip=exp2(-centered.y*centered.y*7.5);
                strip*=smoothstep(0,.10,input.uv.x)*(1-smoothstep(.95,1,input.uv.x));
                float alpha=lerp(strip,orb,saturate(_Mode));
                return half4(input.color.rgb*_Tint.rgb*_Intensity,input.color.a*_Tint.a*alpha);
            }
            ENDHLSL
        }
    }
}
