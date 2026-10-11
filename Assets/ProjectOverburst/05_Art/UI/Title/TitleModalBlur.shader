Shader "Overburst/UI/Title Modal Blur"
{
    Properties{_MainTex("Texture",2D)="white"{}}
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;float4 _MainTex_TexelSize;
        float4 blur(float2 uv,float2 axis)
        {
            float3 color=0;float weight=0;
            for(int n=-15;n<=15;n++)
            {
                float w=exp(-float(n*n)/72);float scale=1/(_MainTex_TexelSize.y*900);float3 c=tex2D(_MainTex,uv+axis*n*scale*_MainTex_TexelSize.xy).rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                c=LinearToGammaSpace(c);
                #endif
                color+=c*w;weight+=w;
            }
            return float4(color/weight,1);
        }
        float4 horizontal(v2f_img i):SV_Target
        {
            float4 c=blur(i.uv,float2(1,0));
            #ifndef UNITY_COLORSPACE_GAMMA
            c.rgb=GammaToLinearSpace(c.rgb);
            #endif
            return c;
        }
        float4 vertical(v2f_img i):SV_Target
        {
            float4 c=blur(i.uv,float2(0,1));c.rgb*=.4;
            #ifndef UNITY_COLORSPACE_GAMMA
            c.rgb=GammaToLinearSpace(c.rgb);
            #endif
            return c;
        }
        ENDCG
        Pass{CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment horizontal
            ENDCG}
        Pass{CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment vertical
            ENDCG}
    }
}
