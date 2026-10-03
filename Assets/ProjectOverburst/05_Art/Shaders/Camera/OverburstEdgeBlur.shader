Shader "Hidden/OVERBURST/EdgeBlur"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            Name "EdgeBlur"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _EdgeBlurStrength;
            float4 _MomentPulse;

            half4 ApplyMoment(half4 color, float2 uv)
            {
                float distance = length((uv - _MomentPulse.xy) * float2(_BlitTexture_TexelSize.z / _BlitTexture_TexelSize.w, 1));
                float mask = smoothstep(.24, .52, distance);
                color.rgb *= 1.0 + mask * clamp(_MomentPulse.z, -.06, .05);
                return color;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 original = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
                float2 edge = smoothstep(.84, 1.0, abs(uv * 2.0 - 1.0));
                float mask = (1.0 - (1.0 - edge.x) * (1.0 - edge.y)) * _EdgeBlurStrength;
                if (mask <= 0.00001) return ApplyMoment(original, uv);

                // 1080p 기준 약 2.2px. 화면 비율에 관계없이 중앙 84%는 원본 그대로다.
                float2 offset = _BlitTexture_TexelSize.xy * (2.2 * _BlitTexture_TexelSize.w / 1080.0);
                half4 blurred = original * .25;
                blurred += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + float2(offset.x, 0), 0) * .125;
                blurred += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv - float2(offset.x, 0), 0) * .125;
                blurred += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + float2(0, offset.y), 0) * .125;
                blurred += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv - float2(0, offset.y), 0) * .125;
                blurred += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + offset, 0) * .0625;
                blurred += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv - offset, 0) * .0625;
                blurred += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + float2(offset.x, -offset.y), 0) * .0625;
                blurred += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + float2(-offset.x, offset.y), 0) * .0625;
                return ApplyMoment(half4(lerp(original.rgb, blurred.rgb, mask), original.a), uv);
            }
            ENDHLSL
        }
    }
}
