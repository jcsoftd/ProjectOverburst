Shader "OVERBURST/VFX/Sword Distortion Trail"
{
    Properties
    {
        [Normal] _WaveNormal("Sword Slash Wave Normal", 2D) = "bump" {}
        _DistortionPixels("Refraction (pixels)", Range(0,40)) = 18
        _RimStrength("Pressure Crest", Range(0,1)) = 0.18
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+20" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            TEXTURE2D(_WaveNormal); SAMPLER(sampler_WaveNormal);
            CBUFFER_START(UnityPerMaterial)
                float _DistortionPixels, _RimStrength;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv; o.color = v.color;
                return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float across = (i.uv.y - 0.5) * 2.0;
                float edge = pow(saturate(1.0 - across * across), 2.0);
                float ends = smoothstep(0.0, 0.09, i.uv.x) * (1.0 - smoothstep(0.80, 1.0, i.uv.x));
                float mask = edge * ends * i.color.a;
                float3 n = UnpackNormal(SAMPLE_TEXTURE2D(_WaveNormal, sampler_WaveNormal,
                    float2(i.uv.x * 1.5 - _Time.y * .7, i.uv.y * .6)));
                float2 direction = float2(ddx(i.uv.y), ddy(i.uv.y));
                direction /= max(length(direction), 0.00001);
                float2 offset = (direction * (across + n.y * .35) + n.xy * .20)
                    * _DistortionPixels / _ScaledScreenParams.xy * mask;
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                half3 col = SampleSceneColor(saturate(screenUV + offset));
                float crest = exp2(-pow((abs(across) - .48) * 9.0, 2.0));
                col += half3(.65,.83,.9) * crest * _RimStrength * mask;
                return half4(col, mask * .92);
            }
            ENDHLSL
        }
    }
}
