Shader "OVERBURST/VFX/Combat Facing Quiet Flow"
{
    Properties
    {
        _MainTex("Owned texture", 2D) = "white" {}
        [HDR] _Tint("Tint", Color) = (1,1,1,1)
        _Emission("Radiance", Float) = 2
        _Opacity("Opacity", Range(0,1)) = 0.5
        _SurfaceKind("Ribbon 0, ring 1, mote 2", Float) = 0
        _Period("Flow period in seconds", Float) = 8
        _SilverRuntimeClock("Runtime clock override", Float) = 0
        _SilverRuntimeTime("Runtime flow time", Float) = 0
        _SilverVisibility("Visibility", Range(0,1)) = 1
        _Phase("Phase offset", Float) = 0
        _Detail("Filament detail", Range(0,1)) = 0.25
        _LineWidth("Ring width", Range(0.1,1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "SilverFlow"
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Tint;
                float _Emission, _Opacity, _SurfaceKind, _Period, _Phase, _Detail, _LineWidth;
                float _SilverRuntimeClock, _SilverRuntimeTime, _SilverVisibility;
            CBUFFER_END
            float _SilverPreviewClock, _SilverPreviewTime;
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float2 data:TEXCOORD1; half4 color:COLOR; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float2 plane:TEXCOORD1; float age:TEXCOORD2; half4 color:COLOR; };
            float Clock() { return lerp(lerp(_Time.y, _SilverPreviewTime, _SilverPreviewClock), _SilverRuntimeTime, _SilverRuntimeClock); }
            V Vert(A i)
            {
                V o;
                float3 p=i.positionOS.xyz;
                o.age=frac(Clock()/max(_Period,0.1)+i.data.x);
                if (_SurfaceKind>1.5 && _SurfaceKind<2.5)
                {
                    p.z += o.age*0.13;
                    p.y += o.age*0.075;
                    p.x += sin(o.age*PI)*i.data.y;
                }
                o.positionCS=TransformObjectToHClip(p);
                o.uv=i.uv; o.plane=i.positionOS.xz; o.color=i.color;
                return o;
            }
            half4 Frag(V i):SV_Target
            {
                float phase=Clock()/max(_Period,0.1)*TWO_PI+_Phase;
                float alpha;
                float light=1;
                if (_SurfaceKind>3.5)
                {
                    float r=length(i.uv*2-1);
                    float src=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).r;
                    alpha=src*exp(-r*r*3.0)*_Opacity*(0.97+0.03*cos(phase));
                }
                else if (_SurfaceKind>2.5)
                {
                    float across=saturate(i.uv.x);
                    float span=saturate(i.uv.y);
                    float edge=smoothstep(0.0,0.055,across)*(1-smoothstep(0.945,1.0,across));
                    float src=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,float2(0.19+span*0.28,across)).r;
                    float silk=0.78+0.22*src;
                    float forward=lerp(0.74,1.0,across);
                    alpha=silk*forward*edge*_Opacity*(0.97+0.03*cos(phase-span*3));
                }
                else if (_SurfaceKind>1.5)
                {
                    float r=length(i.uv*2-1);
                    float src=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).r;
                    alpha=src*exp(-r*r*3)*pow(sin(i.age*PI),4)*_Opacity;
                }
                else if (_SurfaceKind>0.5)
                {
                    float2 p=i.uv-0.5;
                    float turn=0.035*sin(phase)+0.018*sin(phase*2+_Phase);
                    float sn,cs; sincos(turn,sn,cs);
                    float radius=length(p)*2;
                    float2 narrow=p*((0.80+(radius-0.80)/max(_LineWidth,0.1))/max(radius,0.0001));
                    float2 uv=float2(narrow.x*cs-narrow.y*sn,narrow.x*sn+narrow.y*cs)+0.5;
                    half4 sample=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,uv);
                    float lum=pow(saturate(max(sample.r,max(sample.g,sample.b))),0.6)*sample.a;
                    float angle=atan2(p.x,p.y);
                    float spine=exp(-pow((radius-0.80)/(0.016*_LineWidth),2))*0.12;
                    float mask=smoothstep(0.48,0.62,radius)*(1-smoothstep(0.96,1.10,radius));
                    float forward=saturate(normalize(i.plane+0.00001).y*0.5+0.5);
                    float flow=0.88+0.12*cos(angle*2-phase);
                    alpha=(lum*0.78+spine)*mask*lerp(0.52,1,forward)*flow*_Opacity;
                    light=1+0.10*cos(angle-phase);
                }
                else
                {
                    // A continuous spine remains beneath the moving silk texture.
                    // Bounded, periodic modulation never cuts the ribbon into dashes.
                    float cross=i.uv.y*2-1;
                    float drift=0.050*sin(i.uv.x*19+phase)+0.022*sin(i.uv.x*31-phase*2);
                    float2 uv=float2(0.19+i.uv.x*0.28,i.uv.y+drift);
                    half4 sample=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,uv);
                    float silkLum=pow(saturate(max(sample.r,max(sample.g,sample.b))),0.6)*sample.a;
                    float veil=exp(-cross*cross*4.5);
                    float spine=exp(-cross*cross*100)*0.16*(1-_Detail*0.75);
                    float thread=pow(0.5+0.5*cos(cross*38+sin(i.uv.x*24+phase)*1.85),6);
                    float flow=0.84+0.16*cos(i.uv.x*TWO_PI*1.4-phase);
                    alpha=((0.19+silkLum*0.70)*veil+spine+thread*veil*_Detail*0.13)*flow*_Opacity;
                    light=1+0.12*cos(i.uv.x*7-phase);
                }
                alpha=saturate(alpha*i.color.a*_Tint.a*_SilverVisibility);
                return half4(_Tint.rgb*_Emission*i.color.rgb*light,alpha);
            }
            ENDHLSL
        }
    }
}
