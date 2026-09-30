Shader "OVERBURST/UI/HUD Resource Bar"
{
    // 2026-09-30: HUD 자원 바(원소 에너지·체력) 위에 가산으로 얹는 층. 채워진 구간에만 그린다(Fill과 같은 Filled 메시).
    // 충전량에 따라 흐름 속도·밝기가 오르고, 끝부분 빛·불꽃, 맥동(가득 참·위험), 빛 쓸기, 빛 과충전 금빛 층을 더한다.
    // 흐름은 두 색(_ElementColor ↔ _AccentColor)을 잡음으로 섞어 원소별 두 톤을 낸다.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _ElementColor ("Flow Color A", Color) = (1, 0.4, 0.1, 1)
        _AccentColor ("Flow Color B", Color) = (1, 0.6, 0.1, 1)
        _CoreColor ("Core Color", Color) = (1, 0.85, 0.45, 1)
        _OverColor ("Overcharge Color", Color) = (1, 0.8, 0.35, 1)
        _UvRect ("Sprite UV Rect (xMin, yMin, xMax, yMax)", Vector) = (0, 0, 1, 1)
        _Fill ("Fill", Range(0, 1)) = 1
        _FromRight ("Fill From Right", Float) = 0
        _Charge ("Charge", Range(0, 1)) = 0
        _Full ("Pulse", Range(0, 1)) = 0
        _PulseSpeed ("Pulse Speed", Range(0, 12)) = 5.2
        _Flash ("Flash", Range(0, 1)) = 0
        _ShinePos ("Shine Position", Float) = -1
        _Over ("Overcharge", Range(0, 1)) = 0
        _Drain ("Overcharge Draining", Range(0, 1)) = 0
        _NoiseScale ("Noise Scale", Range(0.1, 4)) = 1
        _Intensity ("Intensity", Range(0, 3)) = 1

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
            "CanUseSpriteAtlas" = "True"
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
        Blend SrcAlpha One
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
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            fixed4 _ElementColor;
            fixed4 _AccentColor;
            fixed4 _CoreColor;
            fixed4 _OverColor;
            float4 _UvRect;
            float _Fill;
            float _FromRight;
            float _Charge;
            float _Full;
            float _PulseSpeed;
            float _Flash;
            float _ShinePos;
            float _Over;
            float _Drain;
            float _NoiseScale;
            float _Intensity;

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 uv)
            {
                float2 i = floor(uv);
                float2 f = frac(uv);
                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm(float2 uv)
            {
                float value = 0.0;
                float amplitude = 0.5;
                for (int i = 0; i < 3; i++)
                {
                    value += ValueNoise(uv) * amplitude;
                    uv = uv * 2.03 + 17.17;
                    amplitude *= 0.5;
                }
                return value;
            }

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float shape = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd).a;

                // Bar space: s = 0..1 across the sprite, p = distance from the fill origin, v = 0..1 across the height.
                float2 uvSize = max(_UvRect.zw - _UvRect.xy, float2(1e-5, 1e-5));
                float s = saturate((IN.texcoord.x - _UvRect.x) / uvSize.x);
                float v = saturate((IN.texcoord.y - _UvRect.y) / uvSize.y);
                float p = lerp(s, 1.0 - s, step(0.5, _FromRight));
                float fill = max(_Fill, 1e-4);
                float charge = saturate(_Charge);
                float t = _Time.y;

                // Stable pattern in canvas space so the texture does not stretch while the fill moves.
                float2 q = IN.worldPosition.xy * 0.02 * _NoiseScale;
                float speed = lerp(0.35, 2.4, charge);
                float dir = lerp(-1.0, 1.0, step(0.5, _FromRight));
                float n1 = Fbm(float2(q.x * 1.6 + dir * t * speed, q.y * 3.0 + t * 0.25));
                float n2 = Fbm(float2(q.x * 3.4 + dir * t * speed * 1.8, q.y * 5.0 - t * 0.6));

                float band = 1.0 - abs(v - 0.5) * 2.0;
                float flow = saturate(n1 * 0.9 + n2 * 0.55 - 0.42) * lerp(0.45, 1.25, charge);
                float lead = saturate(1.0 - (fill - p) / 0.12);
                float edge = lead * lead * lerp(0.35, 1.1, charge);
                float sparks = smoothstep(0.7, 0.95, n2) * smoothstep(0.66, 0.8, charge) * saturate(1.0 - (fill - p) / 0.35);
                float pulse = _Full * (0.28 + 0.22 * sin(t * _PulseSpeed));
                float shine = 1.0 - smoothstep(0.0, 0.06, abs(p - _ShinePos));
                float3 flowColor = lerp(_ElementColor.rgb, _AccentColor.rgb, smoothstep(0.3, 0.75, n2));

                float3 col = flowColor * (flow * 0.85 + band * 0.22 * (0.4 + charge) + pulse)
                    + _CoreColor.rgb * (edge * (0.55 + band * 0.6) + sparks * 1.1 + shine * 1.3 + _Flash * 0.9);

                // Light overcharge (101..200): a second shimmering layer from the origin, flickering while it drains.
                float overMask = step(0.0001, _Over) * saturate((_Over - p) / 0.015);
                float overNoise = Fbm(float2(q.x * 2.2 - dir * t * 1.4, q.y * 4.0 + t));
                float flicker = 1.0 - _Drain * 0.45 * (0.5 + 0.5 * sin(t * 17.0));
                col += _OverColor.rgb * overMask * (0.35 + 0.75 * overNoise) * flicker;

                float alpha = saturate(shape * IN.color.a) * _Intensity;

                #ifdef UNITY_UI_CLIP_RECT
                alpha *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - 0.001);
                #endif

                return fixed4(col * IN.color.rgb, alpha);
            }
            ENDCG
        }
    }
}
