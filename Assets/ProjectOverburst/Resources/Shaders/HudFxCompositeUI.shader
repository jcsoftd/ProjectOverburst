Shader "OVERBURST/UI/HUD Fx Composite"
{
    // 2026-09-30: 전용 카메라가 투명 배경에 그린 파티클 렌더 텍스처를 HUD에 얹는다.
    // 렌더 텍스처 색은 이미 알파가 곱해진 값(투명 바탕 위 알파 블렌드 결과)이라 프리멀티플라이로 합성한다.
    // 가산·굴절 파티클은 색이 없는 곳에도 알파를 1로 써서 바를 검게 가리므로, _AlphaWeight로 알파 반영을 원소별로 정한다
    // (어둠처럼 바를 어둡게 덮어야 하는 원소만 1, 나머지는 0 = 순수 가산).
    // _EdgeFade: 좌우 끝 여유 구간(uv 비율)에서 서서히 사라지게 해 잘린 경계가 보이지 않게 한다.
    // _EdgeFadeTop / _EdgeFadeBottom: 위로 피어오르는 냉기·불꽃이 캡처 위아래 끝에서 일직선으로 잘리지 않게 같은 처리를 한다.
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "black" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _EdgeFade ("Edge Fade (uv)", Range(0, 0.5)) = 0.04
        _EdgeFadeTop ("Top Fade (uv)", Range(0, 1)) = 0.3
        _EdgeFadeBottom ("Bottom Fade (uv)", Range(0, 1)) = 0.12
        _AlphaWeight ("Alpha Weight (0 add, 1 premultiplied)", Range(0, 1)) = 1

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float2 edge : TEXCOORD2;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float4 _ClipRect;
            float _EdgeFade;
            float _EdgeFadeTop;
            float _EdgeFadeBottom;
            float _AlphaWeight;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                // The fade is symmetric, so a mirrored RawImage uvRect (1..0) fades the same two ends.
                OUT.edge = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, IN.texcoord);
                float fade = _EdgeFade > 0.0001
                    ? saturate(IN.edge.x / _EdgeFade) * saturate((1.0 - IN.edge.x) / _EdgeFade)
                    : 1.0;
                // Rising vapour and tall flames leave through the top of the capture: fade them out before the
                // texture edge so no horizontal cut line shows. smoothstep keeps the fall-off soft.
                if (_EdgeFadeTop > 0.0001) fade *= smoothstep(0.0, 1.0, saturate((1.0 - IN.edge.y) / _EdgeFadeTop));
                if (_EdgeFadeBottom > 0.0001) fade *= smoothstep(0.0, 1.0, saturate(IN.edge.y / _EdgeFadeBottom));
                float k = fade * IN.color.a;
                c.rgb *= IN.color.rgb * k;
                // Additive/refraction particles write alpha 1 over their whole quad even where they add no colour,
                // which would black out the bar. 0 = pure additive (colour only), 1 = true premultiplied (can darken).
                c.a *= k * _AlphaWeight;

                #ifdef UNITY_UI_CLIP_RECT
                float clip2d = UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                c *= clip2d;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(max(c.a, max(c.r, max(c.g, c.b))) - 0.001);
                #endif

                return c;
            }
            ENDCG
        }
    }
}
