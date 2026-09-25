Shader "OVERBURST/VFX/Sword Pressure Wave"
{
    Properties
    {
        [Normal] _WaveNormal("Sword Slash Wave Normal", 2D) = "bump" {}
        _DistortionPixels("Refraction (pixels)", Range(0, 40)) = 14
        _RimColor("Pressure Crest", Color) = (0.65, 0.83, 0.9, 1)
        _RimStrength("Crest Strength", Range(0, 1)) = 0.16
        _ArcHalfAngle("Arc Half Angle", Range(0, 180)) = 67.5
        _TipFadeDegrees("Tip Fade", Range(1, 60)) = 24
        _StartRadius("Starting Radius", Range(0, 1)) = 0.5
        _BandWidth("Pressure Band Width", Range(0.02, 0.3)) = 0.13
        _DepthFade("Intersection Fade", Float) = 0.08
        [HideInInspector] _WaveProgress("Progress", Float) = 0
        [HideInInspector] _WaveOpacity("Opacity", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+20" }
        Pass
        {
            Name "PressureWave"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_WaveNormal); SAMPLER(sampler_WaveNormal);
            CBUFFER_START(UnityPerMaterial)
                float4 _RimColor;
                float _DistortionPixels, _RimStrength, _ArcHalfAngle, _TipFadeDegrees;
                float _StartRadius, _BandWidth, _DepthFade, _WaveProgress, _WaveOpacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 plane : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.plane = input.positionOS.xz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float t = saturate(_WaveProgress);
                float2 p = input.plane;
                float radius = length(p);
                float angle = abs(atan2(p.x, p.y)) * 57.29578;
                float tip = _ArcHalfAngle > 179.0 ? 1.0 :
                    1.0 - smoothstep(_ArcHalfAngle - _TipFadeDegrees, _ArcHalfAngle, angle);
                float life = smoothstep(0.0, 0.075, t) * (1.0 - smoothstep(0.30, 1.0, t));
                float front = lerp(_StartRadius, 0.965, 1.0 - pow(1.0 - t, 2.4));
                float width = _BandWidth * lerp(0.25, 1.0, tip);
                float d = (radius - front) / max(width, 0.005);
                float compression = exp2(-d * d * 5.0);
                float tailD = (radius - front + width * 1.5) / max(width, 0.005);
                float tail = exp2(-tailD * tailD * 7.0) * 0.32;
                float mask = (compression + tail) * tip * life * _WaveOpacity;
                mask *= 1.0 - smoothstep(0.975, 1.0, radius);

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float rawDepth = SampleSceneDepth(screenUV);
                float orthoDepth = rawDepth;
                #if UNITY_REVERSED_Z
                    orthoDepth = 1.0 - orthoDepth;
                #endif
                float sceneDepth = lerp(LinearEyeDepth(rawDepth, _ZBufferParams),
                    lerp(_ProjectionParams.y, _ProjectionParams.z, orthoDepth), unity_OrthoParams.w);
                float waveDepth = -TransformWorldToView(input.positionWS).z;
                mask *= saturate((sceneDepth - waveDepth) / max(_DepthFade, 0.001));

                // 원본 Sword Slash의 방사형 노멀 패턴을 파면의 압축/잔물결에 사용한다.
                float2 normalUV = float2(p.y, -p.x) * (0.52 + t * 0.16) + 0.5;
                float3 waveNormal = UnpackNormal(SAMPLE_TEXTURE2D(_WaveNormal, sampler_WaveNormal, normalUV));
                float2 radial = p / max(radius, 0.001);
                float3 directionOS = float3(radial.x - waveNormal.y * 0.24, 0,
                    radial.y + waveNormal.x * 0.24);
                float3 directionVS = TransformWorldToViewDir(TransformObjectToWorldDir(directionOS));
                float2 screenDirection = directionVS.xy / max(length(directionVS.xy), 0.2);
                float signedPressure = compression - tail * 1.4;
                float2 offset = screenDirection * (_DistortionPixels / _ScaledScreenParams.xy)
                    * signedPressure * tip * life * (0.8 + waveNormal.z * 0.2);
                half3 refracted = SampleSceneColor(saturate(screenUV + offset));
                float crest = exp2(-d * d * 90.0);
                refracted += _RimColor.rgb * crest * _RimStrength * tip * life;
                return half4(refracted, saturate(mask * 0.92));
            }
            ENDHLSL
        }
    }
}
