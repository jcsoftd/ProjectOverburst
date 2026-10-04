Shader "OVERBURST/Environment/Ground Haze"
{
    Properties
    {
        _Noise ("Seamless density", 3D) = "white" {}
        _GroundHeight ("Ground height", 2D) = "black" {}
        _FogColor ("Cool ambient scattering", Color) = (0.57,0.65,0.69,1)
        _SunTint ("Warm sunlight", Color) = (0.92,0.87,0.72,1)
        _Density ("Extinction per metre", Range(0,1)) = 0.42
        _Height ("Layer height", Range(0.2,4)) = 1.5
        _NoiseScale ("World noise scale", Float) = 0.12
        _Wind ("Wind metres per second", Vector) = (0.065,0,-0.025,0)
        _BoundsMin ("Volume minimum", Vector) = (-30,-2,-20,0)
        _BoundsMax ("Volume maximum", Vector) = (26,4,31,0)
        _GroundBounds ("Ground XZ minimum and extent", Vector) = (-36,-30,108,76)
        _ClearCenter ("Open camp XZ and radii", Vector) = (0,2,8.5,9)
        _CenterDensity ("Camp centre density multiplier", Range(0,1)) = 0.18
        [HideInInspector] _PreviewTime ("Preview time", Float) = -1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+30" "RenderType"="Transparent" }
        Pass
        {
            Name "GroundHaze"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Front
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE3D(_Noise); SAMPLER(sampler_Noise);
            TEXTURE2D(_GroundHeight); SAMPLER(sampler_GroundHeight);
            CBUFFER_START(UnityPerMaterial)
                float4 _FogColor, _SunTint, _Wind, _BoundsMin, _BoundsMax, _GroundBounds, _ClearCenter;
                float4 _GroundHeight_TexelSize;
                float _Density, _Height, _NoiseScale, _PreviewTime;
                float _CenterDensity;
            CBUFFER_END

            struct Attributes { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS=TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }
            float HeightAt(float2 xz)
            {
                float2 uv=saturate((xz-_GroundBounds.xy)/_GroundBounds.zw);
                uv=uv*(1-_GroundHeight_TexelSize.xy)+.5*_GroundHeight_TexelSize.xy;
                return SAMPLE_TEXTURE2D_LOD(_GroundHeight,sampler_GroundHeight,uv,0).r;
            }
            float DensityAt(float3 p, float t)
            {
                // Keep the foot of the layer attached to the actual baked ground.
                float h=p.y-HeightAt(p.xz);
                if(h<0 || h>_Height*1.7) return 0;
                float2 edge=min(p.xz-_BoundsMin.xz,_BoundsMax.xz-p.xz);
                float boundary=smoothstep(0,5,min(edge.x,edge.y));
                float clearing=smoothstep(.55,1.55,length((p.xz-_ClearCenter.xy)/_ClearCenter.zw));
                float3 q=(p-_Wind.xyz*t)*_NoiseScale;
                float cloud=SAMPLE_TEXTURE3D_LOD(_Noise,sampler_Noise,q*.30,0).r;
                float detail=SAMPLE_TEXTURE3D_LOD(_Noise,sampler_Noise,q+float3(.21,.37,.13),0).r;
                float wisps=smoothstep(.28,.68,cloud*.65+detail*.35);
                // The nonuniform top surface avoids a flat horizontal fog ceiling.
                float top=_Height*lerp(.48,1.55,cloud);
                float vertical=exp2(-h*2.0/_Height)*smoothstep(top,top*.48,h);
                float groundFade=smoothstep(0,.16,h);
                return _Density*wisps*vertical*groundFade*boundary*lerp(_CenterDensity,1,clearing);
            }
            half4 Frag(Varyings i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv=i.positionCS.xy/_ScaledScreenParams.xy;
                float depth=SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                    depth=lerp(UNITY_NEAR_CLIP_VALUE,1,depth);
                #endif
                float3 hit=ComputeWorldSpacePosition(uv,depth,UNITY_MATRIX_I_VP);
                float nearDepth=UNITY_NEAR_CLIP_VALUE;
                #if UNITY_REVERSED_Z
                    nearDepth=1;
                #endif
                float3 nearWS=ComputeWorldSpacePosition(uv,nearDepth,UNITY_MATRIX_I_VP);
                float3 origin=unity_OrthoParams.w>.5 ? nearWS : GetCameraPositionWS();
                float3 delta=hit-origin;
                float rayLength=length(delta);
                float3 dir=delta/max(rayLength,.00001);
                float3 safeDir=sign(dir+1e-7)*max(abs(dir),1e-6);
                float3 a=(_BoundsMin.xyz-origin)/safeDir;
                float3 b=(_BoundsMax.xyz-origin)/safeDir;
                float3 lo=min(a,b), hi=max(a,b);
                float start=max(0,max(lo.x,max(lo.y,lo.z)));
                float end=min(rayLength,min(hi.x,min(hi.y,hi.z)));
                if(end<=start) return 0;
                const int steps=32;
                float ds=(end-start)/steps;
                // Fixed subpixel stratification avoids animated grain in stationary views.
                float jitter=frac(52.9829189*frac(dot(i.positionCS.xy,float2(.06711056,.00583715))));
                float t=_PreviewTime>=0 ? _PreviewTime : _Time.y;
                float optical=0;
                [loop] for(int n=0;n<steps;n++)
                    optical+=DensityAt(origin+dir*(start+(n+jitter)*ds),t)*ds;
                float opacity=1-exp(-optical);
                Light sun=GetMainLight();
                float forward=pow(saturate(dot(dir,-sun.direction)*.5+.5),4);
                half3 scattering=lerp(_FogColor.rgb,_SunTint.rgb*max(sun.color,.3),.15+forward*.27);
                return half4(scattering*opacity,opacity);
            }
            ENDHLSL
        }
    }
}
