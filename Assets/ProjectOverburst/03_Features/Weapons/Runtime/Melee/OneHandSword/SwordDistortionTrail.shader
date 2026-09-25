Shader "OVERBURST/VFX/Sword Distortion Trail"
{
    Properties
    {
        [Normal] _WaveNormal("Sword Slash Wave Normal", 2D) = "bump" {}
        _DistortionPixels("Refraction (pixels)", Range(0,80)) = 42
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+20" }
        Pass
        {
            Tags { "LightMode"="SwordRefraction" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            TEXTURE2D_X(_SwordSceneColor);
            TEXTURE2D(_WaveNormal); SAMPLER(sampler_WaveNormal);
            CBUFFER_START(UnityPerMaterial)
                float _DistortionPixels;
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
                float2 offset = (direction * (sin(across * 4.5) + n.y * .65) + n.xy * .45)
                    * _DistortionPixels / _ScaledScreenParams.xy * mask;
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                half3 col = SAMPLE_TEXTURE2D_X(_SwordSceneColor, sampler_LinearClamp,
                    UnityStereoTransformScreenSpaceTex(saturate(screenUV + offset))).rgb;
                return half4(col, mask * .92);
            }
            ENDHLSL
        }
    }
}
