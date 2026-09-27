Shader "OVERBURST/ElementStatusBillboard"
{
    Properties { _Tint("Tint",Color)=(2.8,.65,.06,1) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float seed:TEXCOORD1; };
            float4 _Tint;
            v2f vert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv;
                o.seed=unity_ObjectToWorld._m03*3.1+unity_ObjectToWorld._m23*2.3;
                return o;
            }
            half4 frag(v2f i):SV_Target
            {
                float t=_Time.y*6+i.seed;
                float wave=sin(i.uv.y*17-t)*.065+sin(i.uv.y*31-t*1.7)*.035;
                float width=lerp(.42,.035,i.uv.y);
                float body=saturate((width-abs(i.uv.x-.5+wave))/max(.03,width));
                float alpha=body*body*smoothstep(0,.18,i.uv.y)*(1-i.uv.y)*.7;
                return half4(_Tint.rgb,alpha);
            }
            ENDHLSL
        }
    }
}
