Shader "OVERBURST/UI/HUD Boss Emblem Fx"
{
    // 2026-10-01: 상단 보스 HUD 마름모(레벨틀) 연출. 원소 에너지 바 오라(HudResourceBarAuraUI)와 같은 노이즈 식을 마름모 거리로 계산한다.
    // _Mode 1 심홍 불꽃(가장자리에서 위로 솟는 불꽃 + 불티), 2 어둠 연기(피어오르는 검은 연기 + 붉게 타는 가장자리 + 불티),
    //       3 붉은 맥동(뒤층: 숨 쉬듯 번지는 진홍 빛 / 앞층: 금속 테두리를 따라 도는 빛줄기).
    // _Layer 0 = 마름모 뒤(프리멀티플라이, 연기는 어둡게 덮는다), 1 = 마름모 앞(순수 가산, 모드 3만 그린다).
    // 좌표: 사각형 중심 = 마름모 중심, 한 변 = 2*_Extent*R. u 오른쪽·v 위쪽, d = |u|+|v| (d=1이 마름모 바깥 꼭짓점 선).
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Mode ("Mode", Float) = 1
        _Layer ("Layer (0 back, 1 front)", Float) = 0
        _Extent ("Half Size / Diamond Radius", Float) = 2
        _ColorA ("Crimson", Color) = (0.78, 0.06, 0.04, 1)
        _CoreColor ("Flame Core", Color) = (1, 0.62, 0.25, 1)
        _SparkColor ("Spark", Color) = (1, 0.48, 0.2, 1)
        _SmokeColor ("Smoke", Color) = (0.05, 0.02, 0.02, 1)
        _RimColor ("Rim Light", Color) = (1, 0.86, 0.55, 1)
        _PulsePeriod ("Pulse Period (s)", Float) = 1.6
        _SweepPeriod ("Rim Sweep Period (s)", Float) = 1.8
        _Intensity ("Intensity", Range(0, 3)) = 1
        _TimeOffset ("Time Offset (s, capture only)", Float) = 0

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
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
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

            fixed4 _Color;
            float4 _ClipRect;
            float _Mode;
            float _Layer;
            float _Extent;
            fixed4 _ColorA;
            fixed4 _CoreColor;
            fixed4 _SparkColor;
            fixed4 _SmokeColor;
            fixed4 _RimColor;
            float _PulsePeriod;
            float _SweepPeriod;
            float _Intensity;
            float _TimeOffset;

            // 아래 노이즈 4종은 HudResourceBarAuraUI.shader와 같은 식이다(두 HUD 연출의 결을 맞춘다).
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

            float Motes(float2 xy, float density, float size)
            {
                float2 cell = floor(xy);
                float2 local = frac(xy) - 0.5;
                float h = Hash21(cell);
                float2 jitter = float2(Hash21(cell + 3.1), Hash21(cell + 7.7)) - 0.5;
                return step(1.0 - density, h) * (1.0 - smoothstep(size * 0.4, size, length(local - jitter * 0.6)));
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
                float2 p = (IN.texcoord - 0.5) * 2.0 * _Extent;
                float u = p.x;
                float v = p.y;
                float d = abs(u) + abs(v);
                float up = saturate(0.5 + 0.5 * v / (d + 1e-4));   // 윗변일수록 1: 불꽃·연기가 위로 더 길다
                float t = _Time.y + _TimeOffset;
                int mode = (int)round(_Mode);
                bool front = _Layer > 0.5;

                float3 glow = 0;
                float cover = 0;

                if (!front && mode == 1)
                {
                    float above = d - 0.9;
                    float h = 0.40 + 0.75 * up;
                    float n1 = Fbm(float2(u * 2.2, v * 2.0 - t * 2.6));
                    float n2 = Fbm(float2(u * 4.4 + 3.1, v * 4.0 - t * 3.8));
                    float shape = saturate(1.0 - above / h);
                    float flame = saturate(shape * 1.55 - (n1 * 0.95 + n2 * 0.45)) * smoothstep(-0.2, 0.08, above);
                    float ember = Motes(float2(u * 3.0, v * 3.0 - t * 2.2), 0.10, 0.16)
                        * step(0.55, v) * step(0.95, d) * saturate(2.0 - d);
                    float k = saturate(shape * shape * 1.1);
                    glow = lerp(_ColorA.rgb, _CoreColor.rgb, k) * flame * 1.5 + _CoreColor.rgb * ember * 1.6;
                    cover = flame * 0.55;   // 밝은 바닥 위에서 분홍으로 뜨지 않게 아래를 어둡게 받친다
                }
                else if (!front && mode == 2)
                {
                    float above = d - 0.85;
                    float h = 0.30 + 0.60 * up;
                    float r1 = Fbm(float2(u * 1.6, v * 1.4 - t * 0.9));
                    float r2 = Fbm(float2(u * 3.2 + 5.0, v * 2.8 - t * 1.5));
                    float shape = saturate(1.0 - above / h) * smoothstep(-0.2, 0.12, above);
                    float smoke = saturate(shape * 1.25 - r1 * 0.9);
                    float wisp = saturate(smoke - r2 * 0.45);
                    float rim = smoothstep(0.03, 0.12, wisp) * (1.0 - smoothstep(0.12, 0.3, wisp)) * shape;
                    float spark = Motes(float2(u * 3.2, v * 3.2 - t * 1.6), 0.06, 0.14) * step(0.5, v) * shape;
                    cover = smoothstep(0.1, 0.5, wisp) * 0.6;
                    glow = _ColorA.rgb * rim * 1.3 + _SparkColor.rgb * spark * 1.5;
                }
                else if (!front && mode == 3)
                {
                    float pulse = 0.55 + 0.45 * sin(6.2831853 * t / max(_PulsePeriod, 0.05));
                    float out1 = max(d - 0.95, 0.0);
                    float halo = exp(-out1 * out1 / (2.0 * 0.2 * 0.2)) * smoothstep(0.85, 1.0, d);
                    glow = _ColorA.rgb * halo * pulse * 0.8;
                    cover = halo * pulse * 0.55;
                }
                else if (front && mode == 3)
                {
                    // 레벨틀 금속 테두리(d 0.66~0.84)를 따라 도는 빛. 반대편에 약한 두 번째 줄기.
                    float ring = smoothstep(0.62, 0.68, d) * (1.0 - smoothstep(0.82, 0.88, d));
                    float a = frac(atan2(v, u) / 6.2831853);
                    float s = frac(t / max(_SweepPeriod, 0.05));
                    float da = abs(a - s);
                    da = min(da, 1.0 - da);
                    float db = abs(a - frac(s + 0.5));
                    db = min(db, 1.0 - db);
                    float streak = exp(-(da / 0.06) * (da / 0.06)) + 0.45 * exp(-(db / 0.05) * (db / 0.05));
                    glow = _RimColor.rgb * ring * streak * 2.0;
                }

                float strength = _Intensity * IN.color.a;
                float alpha = saturate(cover * strength);
                float3 rgb = glow * strength + _SmokeColor.rgb * alpha;

                #ifdef UNITY_UI_CLIP_RECT
                float clip2d = UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                rgb *= clip2d;
                alpha *= clip2d;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(max(alpha, max(rgb.r, max(rgb.g, rgb.b))) - 0.001);
                #endif

                return fixed4(rgb, alpha);
            }
            ENDCG
        }
    }
}
