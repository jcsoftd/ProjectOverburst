Shader "Overburst/UI/Title Backdrop"
{
    Properties
    {
        [PerRendererData] _MainTex("Texture",2D)="white"{}
        _Brightness("Brightness",Float)=1
        _Saturation("Saturation",Float)=1
        _Vignette("Title vignette",Float)=0
        _WindowGradient("Window gradient",Float)=0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata {float4 vertex:POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;};
            struct v2f {float4 vertex:SV_POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;float2 local:TEXCOORD1;};
            sampler2D _MainTex;float _Brightness,_Saturation,_Vignette,_WindowGradient;float4 _ClipRect;
            v2f vert(appdata v){v2f o;o.vertex=UnityObjectToClipPos(v.vertex);o.color=v.color;o.uv=v.uv;o.local=v.vertex.xy;return o;}
            float4 frag(v2f i):SV_Target
            {
                float4 c=tex2D(_MainTex,i.uv);
                #ifndef UNITY_COLORSPACE_GAMMA
                c.rgb=LinearToGammaSpace(c.rgb);
                #endif
                float l=dot(c.rgb,float3(.2126,.7152,.0722));c.rgb=lerp(l.xxx,c.rgb,_Saturation)*_Brightness;
                if(_WindowGradient>.5)
                {
                    float radius=length(float2((i.uv.x-.5)/.70710678,(1-i.uv.y)/1.41421356));
                    c.rgb=radius<.55?lerp(float3(40,32,24),float3(20,18,17),saturate(radius/.55))/255:lerp(float3(20,18,17),float3(12,12,11),saturate((radius-.55)/.45))/255;
                }
                if(_Vignette>.5)
                {
                    float x=1-i.uv.x;
                    float a=x<.19?lerp(.97,.90,x/.19):x<.39?lerp(.90,.67,(x-.19)/.20):x<.70?lerp(.67,.08,(x-.39)/.31):.08;
                    c.rgb=lerp(c.rgb,float3(6,7,8)/255,a);
                    float y=i.uv.y;
                    float b=y<.32?lerp(.65,0,y/.32):y>.80?lerp(0,.333,(y-.80)/.20):0;
                    c.rgb=lerp(c.rgb,float3(3,4,5)/255,b);
                }
                #ifndef UNITY_COLORSPACE_GAMMA
                c.rgb=GammaToLinearSpace(c.rgb);
                #endif
                c*=i.color;
                #ifdef UNITY_UI_CLIP_RECT
                c.a*=UnityGet2DClipping(i.local,_ClipRect);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
