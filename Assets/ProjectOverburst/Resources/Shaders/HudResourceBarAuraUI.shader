Shader "OVERBURST/UI/HUD Resource Bar Aura"
{
    // 2026-09-30: 원소 에너지 바 주변 원소 연출. 바보다 위아래로 큰 사각형에 그리고, 채워진 구간에서만 나온다.
    // _Mode 1 불(바 위로 일렁이는 불꽃 + 불티), 2 얼음(가장자리 서리 결정·반짝임 + 냉기), 3 번개(바를 따라 지직이는 전류 + 튀는 번개),
    //       4 어둠(피어오르는 검은 연기 + 붉은 테두리), 5 빛(솟는 빛줄기 + 빛 알갱이).
    // 좌표는 바 높이 단위(X = 바 길이 방향, Y = 0 바 아래 ~ 1 바 위)라 해상도와 무관하다.
    // 프리멀티플라이 합성: rgb는 더하는 빛, a는 어둡게 덮는 양(어둠 연기용).
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Mode ("Mode", Float) = 0
        _ColorA ("Color A", Color) = (1, 0.4, 0.1, 1)
        _ColorB ("Color B", Color) = (1, 0.6, 0.1, 1)
        _CoreColor ("Core Color", Color) = (1, 0.85, 0.45, 1)
        _Fill ("Fill", Range(0, 1)) = 1
        _FromRight ("Fill From Right", Float) = 0
        _Charge ("Charge", Range(0, 1)) = 0
        _Full ("Full", Range(0, 1)) = 0
        _BarBand ("Bar Band (yMin, yMax)", Vector) = (0.16, 0.48, 0, 0)
        _BarAspect ("Bar Length / Height", Float) = 10
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
            fixed4 _ColorA;
            fixed4 _ColorB;
            fixed4 _CoreColor;
            float _Fill;
            float _FromRight;
            float _Charge;
            float _Full;
            float4 _BarBand;
            float _BarAspect;
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

            // Nearest and second-nearest cell distances (ice crystals).
            float2 Voronoi(float2 x)
            {
                float2 n = floor(x);
                float2 f = frac(x);
                float f1 = 8.0;
                float f2 = 8.0;
                for (int j = -1; j <= 1; j++)
                {
                    for (int i = -1; i <= 1; i++)
                    {
                        float2 g = float2(i, j);
                        float2 o = float2(Hash21(n + g), Hash21(n + g + 17.3));
                        float d = length(g + o - f);
                        if (d < f1) { f2 = f1; f1 = d; }
                        else if (d < f2) { f2 = d; }
                    }
                }
                return float2(f1, f2);
            }

            // Small round sparks rising through a grid of cells.
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
                float2 uv = IN.texcoord;
                float p = lerp(uv.x, 1.0 - uv.x, step(0.5, _FromRight));
                float barH = max(_BarBand.y - _BarBand.x, 1e-4);
                float Y = (uv.y - _BarBand.x) / barH;      // 0 = bar bottom, 1 = bar top
                float X = p * _BarAspect;                   // along the bar, in bar heights
                float fill = _Fill;
                float xMask = saturate((fill - p) * _BarAspect / 0.25) * step(0.001, fill);
                float c = saturate(_Charge);
                float power = saturate(0.2 + 0.8 * c) * (1.0 + _Full * 0.35);
                float t = _Time.y;
                int mode = (int)round(_Mode);

                float3 glow = 0;
                float cover = 0;
                float3 coverColor = 0;

                if (mode == 1)
                {
                    // Fire: tongues licking up from the top edge, taller with charge, plus rising embers.
                    float h = lerp(0.45, 1.35, c) * (1.0 + _Full * 0.25);
                    float above = Y - 0.8;
                    float n1 = Fbm(float2(X * 1.3, Y * 1.1 - t * 2.4));
                    float n2 = Fbm(float2(X * 2.6 + 3.1, Y * 2.2 - t * 3.6));
                    float shape = saturate(1.0 - above / h);
                    float flame = saturate(shape * 1.45 - (n1 * 0.95 + n2 * 0.45)) * smoothstep(-0.25, 0.1, above);
                    float ember = Motes(float2(X * 2.2, Y * 2.2 - t * 2.8), lerp(0.04, 0.1, c), 0.16)
                        * step(0.9, Y) * saturate(1.0 - above / (h * 1.9));
                    float3 fireColor = lerp(_ColorA.rgb, _CoreColor.rgb, saturate(shape * shape * 1.2));
                    glow = fireColor * flame * 1.35 + _CoreColor.rgb * ember * 1.6;
                }
                else if (mode == 2)
                {
                    // Ice: frost crust creeping in from both edges, faint crystal cracks, round twinkles,
                    // icicles hanging under the bar (longer with charge) and a faint cold mist above.
                    float edgeDist = min(abs(Y - 1.0), abs(Y));
                    float thick = lerp(0.08, 0.28, c) * (0.6 + Fbm(float2(X * 1.5, Y * 1.5)) * 0.8);
                    float crust = saturate(1.0 - edgeDist / thick) * step(-0.3, Y) * step(Y, 1.3);
                    float2 v = Voronoi(float2(X * 3.2, Y * 3.2));
                    float crack = (1.0 - smoothstep(0.0, 0.05, v.y - v.x)) * 0.5;
                    float frost = crust * (0.35 + crack);
                    float2 twinkleXY = float2(X * 4.0, Y * 4.0);
                    float phase = Hash21(floor(twinkleXY) + 9.1);
                    float twinkle = Motes(twinkleXY, 0.08, 0.2) * pow(0.5 + 0.5 * sin(t * 5.0 + phase * 40.0), 6.0) * crust;
                    float icicleCol = X * 4.0;
                    float icicleSeed = Hash21(float2(floor(icicleCol), 4.2));
                    float icicleLen = icicleSeed * icicleSeed * lerp(0.15, 0.75, c);
                    float icicleDepth = saturate(-Y / max(icicleLen, 1e-3));
                    float icicleWidth = (1.0 - icicleDepth) * 0.32;
                    float icicle = step(Y, 0.0) * step(icicleDepth, 0.999) * step(0.35, icicleSeed)
                        * (1.0 - smoothstep(icicleWidth * 0.6, icicleWidth, abs(frac(icicleCol) - 0.5)));
                    float mist = Fbm(float2(X * 0.8, Y * 0.8 - t * 0.5)) * saturate(1.0 - (Y - 1.0) / 0.9) * step(1.0, Y) * 0.35 * c;
                    glow = _ColorB.rgb * (frost * 0.95 + icicle * 0.8) + _CoreColor.rgb * (twinkle * 1.8 + icicle * (1.0 - icicleDepth) * 0.35)
                        + _ColorA.rgb * mist;
                }
                else if (mode == 3)
                {
                    // Electric: a jagged current crackling along the bar and bolts jumping out of it, re-rolled many times a second.
                    float rate = lerp(7.0, 18.0, c);
                    float tick = floor(t * rate);
                    float jag = (ValueNoise(float2(X * 2.2, tick * 1.7)) - 0.5) * 0.8
                        + (ValueNoise(float2(X * 7.0, tick * 3.1)) - 0.5) * 0.3;
                    float dist = abs(Y - (0.5 + jag));
                    float seg = step(0.5 - c * 0.3, ValueNoise(float2(X * 0.45, tick * 0.9)));
                    float arc = (1.0 - smoothstep(0.0, 0.05, dist)) * seg;
                    float halo = (1.0 - smoothstep(0.0, 0.4, dist)) * seg * 0.22;

                    float bolts = 0.0;
                    for (int k = 0; k < 2; k++)
                    {
                        float kt = tick + k * 13.0;
                        float bx = Hash21(float2(kt, 11.1)) * max(fill, 0.001) * _BarAspect;
                        float on = step(0.4 - c * 0.3, Hash21(float2(kt, 5.3)));
                        float len = lerp(0.5, 1.4, Hash21(float2(kt, 7.7))) * lerp(0.6, 1.0, c);
                        float up = step(0.5, Hash21(float2(kt, 2.9)));
                        float boltStart = up;
                        float reach = lerp(-len, len, up);
                        float s = saturate((Y - boltStart) / reach);
                        float wob = (ValueNoise(float2(Y * 7.0, kt * 2.3)) - 0.5) * 0.35;
                        float bd = abs(X - bx - wob);
                        bolts += (1.0 - smoothstep(0.0, 0.05, bd)) * step(0.0, (Y - boltStart) * sign(reach)) * step(s, 0.999) * on;
                    }
                    float flick = 0.7 + 0.3 * Hash21(float2(floor(t * 30.0), 1.3));
                    glow = (_ColorB.rgb * halo + _CoreColor.rgb * (arc + bolts) * 1.5 + _ColorA.rgb * bolts * 0.6) * flick;
                }
                else if (mode == 4)
                {
                    // Dark: black smoke rising off the bar with a smouldering crimson rim and a few red sparks.
                    float h = lerp(0.6, 1.5, c);
                    float above = Y - 0.65;
                    float r1 = Fbm(float2(X * 1.0, Y * 0.9 - t * 0.7));
                    float r2 = Fbm(float2(X * 2.0 + 5.0, Y * 1.7 - t * 1.2));
                    float shape = saturate(1.0 - above / h) * smoothstep(-0.25, 0.15, above);
                    float smoke = saturate(shape * 1.25 - r1 * 0.9);
                    float wisp = saturate(smoke - r2 * 0.45);
                    // 몸통은 검게 덮고, 붉은색은 연기 가장자리 얇은 띠와 불티만.
                    float rim = smoothstep(0.03, 0.12, wisp) * (1.0 - smoothstep(0.12, 0.3, wisp)) * shape;
                    float spark = Motes(float2(X * 2.4, Y * 2.4 - t * 1.4), lerp(0.03, 0.07, c), 0.14) * step(0.9, Y) * shape;
                    cover = smoothstep(0.1, 0.5, wisp) * 0.92;
                    coverColor = _ColorA.rgb;
                    glow = _ColorB.rgb * rim * 0.55 + _CoreColor.rgb * spark * 1.4;
                }
                else if (mode == 5)
                {
                    // Light: soft rays rising from the bar and motes drifting up, ivory with a cool blue tint.
                    float above = Y - 0.9;
                    float colN = ValueNoise(float2(X * 2.5, t * 0.6));
                    float ray = pow(colN, 5.0) * saturate(1.0 - above / lerp(0.8, 1.6, c)) * smoothstep(-0.2, 0.1, above);
                    float moteSeed = Hash21(floor(float2(X * 2.6, Y * 2.6 - t * 1.1)));
                    float mote = Motes(float2(X * 2.6, Y * 2.6 - t * 1.1), lerp(0.05, 0.12, c), 0.15) * step(0.8, Y)
                        * saturate(1.0 - above / 1.8);
                    float3 moteColor = lerp(_ColorA.rgb, _ColorB.rgb, step(0.5, moteSeed));
                    glow = lerp(_ColorA.rgb, _ColorB.rgb, 0.35) * ray * 0.9 + moteColor * mote * 1.5;
                }

                float strength = xMask * power * _Intensity * IN.color.a;
                float alpha = saturate(cover * strength);
                float3 rgb = glow * strength + coverColor * alpha;

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
