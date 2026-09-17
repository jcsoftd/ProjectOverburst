Shader "OVERBURST/UI/Experimental Slot Outline UI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1, 0.45, 0.1, 1)
        _OutlineThickness ("Outline Thickness", Range(0.001, 0.5)) = 0.035
        _GlowIntensity ("Glow Intensity", Range(0, 8)) = 3.5
        _GlowSize ("Glow Size", Range(0, 0.5)) = 0.055
        _NoiseScale ("Noise Scale", Range(1, 80)) = 42
        _NoiseSpeed ("Noise Speed", Range(0, 10)) = 2
        _NoiseStrength ("Noise Strength", Range(0, 2)) = 0.75
        _PulseSpeed ("Pulse Speed", Range(0, 10)) = 1.4
        _ShapeMode ("Shape Mode", Float) = 0
        _EffectMode ("Effect Mode", Float) = 1
        _EdgeSoftness ("Edge Softness", Range(0.001, 0.2)) = 0.035
        _CornerCut ("Corner Cut", Range(0, 0.18)) = 0.06
        _Alpha ("Alpha", Range(0, 1)) = 1

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
        Blend SrcAlpha OneMinusSrcAlpha
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
            float _OutlineThickness;
            float _GlowIntensity;
            float _GlowSize;
            float _NoiseScale;
            float _NoiseSpeed;
            float _NoiseStrength;
            float _PulseSpeed;
            float _ShapeMode;
            float _EffectMode;
            float _EdgeSoftness;
            float _CornerCut;
            float _Alpha;

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
                float2 uv = IN.texcoord;
                float2 centered = uv - 0.5;
                float radius = length(centered);
                float leftEdge = uv.x;
                float rightEdge = 1.0 - uv.x;
                float bottomEdge = uv.y;
                float topEdge = 1.0 - uv.y;
                float minEdge = min(min(leftEdge, rightEdge), min(bottomEdge, topEdge));
                float cornerCut = _CornerCut * (1.0 - step(0.5, _ShapeMode));
                float chamferBL = (uv.x + uv.y - cornerCut) * 0.70710678;
                float chamferBR = ((1.0 - uv.x) + uv.y - cornerCut) * 0.70710678;
                float chamferTL = (uv.x + (1.0 - uv.y) - cornerCut) * 0.70710678;
                float chamferTR = ((1.0 - uv.x) + (1.0 - uv.y) - cornerCut) * 0.70710678;
                float chamferEdge = min(minEdge, min(min(chamferBL, chamferBR), min(chamferTL, chamferTR)));
                float squareEdge = lerp(minEdge, chamferEdge, step(0.0001, cornerCut));
                float circleEdge = abs(0.5 - radius);
                float edgeDistance = lerp(squareEdge, circleEdge, step(0.5, _ShapeMode));

                float squareFlow = uv.x;
                if (leftEdge <= rightEdge && leftEdge <= bottomEdge && leftEdge <= topEdge)
                    squareFlow = uv.y;
                else if (rightEdge <= leftEdge && rightEdge <= bottomEdge && rightEdge <= topEdge)
                    squareFlow = 1.0 + (1.0 - uv.y);
                else if (bottomEdge <= leftEdge && bottomEdge <= rightEdge && bottomEdge <= topEdge)
                    squareFlow = 2.0 + uv.x;
                else
                    squareFlow = 3.0 + (1.0 - uv.x);

                squareFlow *= 0.25;
                float circleFlow = atan2(centered.y, centered.x) / 6.2831853 + 0.5;
                float edgeFlow = lerp(squareFlow, circleFlow, step(0.5, _ShapeMode));

                float outerCircleMask = 1.0 - smoothstep(0.5 + _GlowSize, 0.5 + _GlowSize + _EdgeSoftness, radius);
                float squareShapeMask = smoothstep(-_EdgeSoftness, 0.0, squareEdge);
                float shapeMask = lerp(squareShapeMask, outerCircleMask, step(0.5, _ShapeMode));
                float outerSoftness = max(0.001, min(_EdgeSoftness * 0.2, _OutlineThickness * 0.35));
                float innerFade = smoothstep(0.0, outerSoftness, edgeDistance);
                float boundaryFadeWidth = max(0.002, min(_EdgeSoftness * 0.36, _OutlineThickness * 0.42));
                float innerBoundary = max(outerSoftness + boundaryFadeWidth, _OutlineThickness * 1);
                float innerBoundaryFade = 1.0 - smoothstep(innerBoundary, innerBoundary + boundaryFadeWidth, edgeDistance);
                float outline = saturate(innerFade * innerBoundaryFade);
                float glow = saturate((1.0 - smoothstep(innerBoundary, innerBoundary + boundaryFadeWidth + _GlowSize * 0.18, edgeDistance)) * innerFade * 0.5);

                float time = _Time.y * _NoiseSpeed;
                float flowNoise = Fbm(float2(edgeFlow * _NoiseScale + time * 1.7, edgeDistance * 90.0 - time));
                float pulse = 0.96 + 0.04 * sin(_Time.y * _PulseSpeed * 6.28318);
                float spark = smoothstep(0.55, 0.98, flowNoise) * _NoiseStrength;
                float ring = saturate(outline * (0.55 + spark * 0.95) + glow * 0.08);

                if (_EffectMode > 1.5 && _EffectMode < 2.5)
                {
                    flowNoise = Fbm(float2(edgeFlow * _NoiseScale * 1.65 + time * 3.1, edgeDistance * 130.0 - time * 2.0));
                    pulse = 0.98 + 0.02 * sin(_Time.y * _PulseSpeed * 6.28318);
                    spark = smoothstep(0.67, 0.98, flowNoise) * _NoiseStrength;
                    ring = saturate(outline * (0.24 + spark * 1.55) + glow * 0.035);
                }
                else if (_EffectMode > 2.5 && _EffectMode < 3.5)
                {
                    flowNoise = Fbm(float2(edgeFlow * _NoiseScale * 0.9 + time * 0.8, edgeDistance * 80.0));
                    float travel = abs(frac(edgeFlow * 2.25 - time * 0.18) - 0.5);
                    float arc = 1.0 - smoothstep(0.04, 0.18, travel);
                    pulse = 0.9 + 0.1 * sin(_Time.y * _PulseSpeed * 6.28318);
                    spark = smoothstep(0.55, 0.95, flowNoise) * _NoiseStrength;
                    ring = saturate(outline * (0.32 + arc * 1.2 + spark * 0.28) + glow * 0.055);
                }
                else if (_EffectMode > 3.5 && _EffectMode < 4.5)
                {
                    flowNoise = Fbm(float2(edgeFlow * _NoiseScale * 0.75 - time * 0.55, edgeDistance * 65.0 + time * 0.3));
                    pulse = 0.87 + 0.13 * sin(_Time.y * _PulseSpeed * 6.28318);
                    spark = smoothstep(0.42, 0.88, flowNoise) * _NoiseStrength;
                    ring = saturate(outline * (0.42 + spark * 0.95) + glow * 0.13);
                }
                else if (_EffectMode > 4.5 && _EffectMode < 5.5)
                {
                    flowNoise = Fbm(float2(edgeFlow * _NoiseScale * 1.25 + time * 2.6, edgeDistance * 42.0 - time * 0.75));
                    float cornerDistance = min(min(length(uv), length(uv - float2(1.0, 0.0))), min(length(uv - float2(0.0, 1.0)), length(uv - 1.0)));
                    float cornerFlare = 1.0 - smoothstep(0.035, 0.22, cornerDistance);
                    float aura = (1.0 - smoothstep(_OutlineThickness * 0.2, _OutlineThickness * 3.6 + _GlowSize * 2.2, edgeDistance)) * innerFade;
                    pulse = 0.72 + 0.28 * sin(_Time.y * _PulseSpeed * 6.28318);
                    spark = smoothstep(0.48, 0.9, flowNoise) * _NoiseStrength;
                    ring = saturate(outline * (0.5 + spark * 1.1) + glow * 0.22 + aura * 0.18 + cornerFlare * (0.18 + spark * 0.34));
                }
                else if (_EffectMode > 5.5 && _EffectMode < 6.5)
                {
                    flowNoise = Fbm(uv * _NoiseScale * 0.9 + float2(time * 1.2, -time * 0.85));
                    float wave = sin(_Time.y * 2.1);
                    float diagonalA = 1.0 - smoothstep(0.006, 0.045, abs((uv.x - uv.y) + wave * 0.08));
                    float diagonalB = 1.0 - smoothstep(0.006, 0.05, abs((uv.x + uv.y - 1.0) - wave * 0.06));
                    float fracture = smoothstep(0.56, 0.96, flowNoise) * _NoiseStrength;
                    float edgeStorm = (1.0 - smoothstep(_OutlineThickness, _OutlineThickness * 4.2 + _GlowSize, edgeDistance)) * innerFade;
                    pulse = 0.78 + 0.22 * sin(_Time.y * _PulseSpeed * 6.28318);
                    spark = fracture;
                    ring = saturate(outline * (0.38 + spark * 1.1) + glow * 0.2 + edgeStorm * spark * 0.24 + (diagonalA + diagonalB) * spark * 0.34);
                }
                else if (_EffectMode > 6.5 && _EffectMode < 7.5)
                {
                    flowNoise = Fbm((centered + 0.5) * _NoiseScale * 1.35 + float2(time * 0.9, time * 1.4));
                    float radialWave = abs(frac(radius * 4.5 - _Time.y * 0.68) - 0.5);
                    float nova = 1.0 - smoothstep(0.025, 0.16, radialWave);
                    float centerAura = 1.0 - smoothstep(0.05, 0.68, radius);
                    float edgeBreath = 1.0 - smoothstep(_OutlineThickness * 0.5, _OutlineThickness * 5.0 + _GlowSize * 2.0, edgeDistance);
                    pulse = 0.66 + 0.34 * sin(_Time.y * _PulseSpeed * 6.28318);
                    spark = smoothstep(0.52, 0.94, flowNoise) * _NoiseStrength;
                    ring = saturate(outline * (0.46 + spark) + glow * 0.26 + edgeBreath * spark * 0.2 + nova * centerAura * (0.12 + spark * 0.22));
                }
                else if (_EffectMode > 7.5 && _EffectMode < 8.5)
                {
                    flowNoise = Fbm(float2(edgeFlow * _NoiseScale * 1.05 + time * 0.7, edgeDistance * 54.0 - time * 0.22));
                    float outerCenter = _OutlineThickness * 0.28;
                    float outerWidth = max(0.003, _OutlineThickness * 0.16);
                    float outerLine = (1.0 - smoothstep(outerWidth, outerWidth + _EdgeSoftness * 0.55, abs(edgeDistance - outerCenter))) * innerFade;

                    float innerCenter = _OutlineThickness * 1.38;
                    float innerWidth = max(0.006, _OutlineThickness * 0.38);
                    float innerLine = (1.0 - smoothstep(innerWidth, innerWidth + _EdgeSoftness * 0.85, abs(edgeDistance - innerCenter))) * innerFade;
                    float innerGradient = (1.0 - smoothstep(innerCenter, innerCenter + _GlowSize * 2.15, edgeDistance))
                        * smoothstep(innerCenter * 0.58, innerCenter, edgeDistance)
                        * innerFade;

                    pulse = 1.0;
                    spark = smoothstep(0.57, 0.96, flowNoise) * _NoiseStrength;
                    ring = saturate(outerLine * (0.66 + spark * 0.28) + innerLine * 0.42 + innerGradient * (0.36 + spark * 0.2));
                    glow = saturate(innerGradient * 0.58 + outerLine * 0.1);
                    outline = saturate(outerLine + innerLine * 0.25);
                }
                else if (_EffectMode > 8.5 && _EffectMode < 9.5)
                {
                    flowNoise = Fbm(float2(edgeFlow * _NoiseScale * 1.5 + time * 1.9, edgeDistance * 70.0 - time));
                    float orbitA = abs(frac(edgeFlow - time * 0.18) - 0.5);
                    float orbitB = abs(frac(edgeFlow * 1.6 + time * 0.13 + 0.31) - 0.5);
                    float sparkA = 1.0 - smoothstep(0.014, 0.08, orbitA);
                    float sparkB = 1.0 - smoothstep(0.01, 0.06, orbitB);
                    float particleBand = 1.0 - smoothstep(_OutlineThickness * 0.75, _OutlineThickness * 3.8 + _GlowSize, edgeDistance);
                    pulse = 1.0;
                    spark = smoothstep(0.52, 0.97, flowNoise) * _NoiseStrength;
                    ring = saturate(outline * (0.32 + spark * 0.9) + glow * 0.18 + particleBand * (sparkA * 0.75 + sparkB * 0.45) * (0.55 + spark));
                }
                else if (_EffectMode > 9.5 && _EffectMode < 10.5)
                {
                    flowNoise = Fbm(float2(edgeFlow * _NoiseScale * 0.85 - time * 0.75, edgeDistance * 38.0 + time * 1.5));
                    float flameBand = (1.0 - smoothstep(_OutlineThickness * 0.3, _OutlineThickness * 5.2 + _GlowSize * 1.5, edgeDistance)) * innerFade;
                    float licking = smoothstep(0.35, 0.86, flowNoise + flameBand * 0.3) * _NoiseStrength;
                    float flameTongue = 1.0 - smoothstep(0.035, 0.22, abs(frac(edgeFlow * 3.1 + time * 0.12) - 0.5));
                    pulse = 0.95 + 0.05 * sin(_Time.y * _PulseSpeed * 6.28318);
                    spark = licking;
                    ring = saturate(outline * (0.35 + spark * 0.9) + glow * 0.28 + flameBand * (0.18 + licking * 0.42 + flameTongue * licking * 0.28));
                }
                else if (_EffectMode > 10.5 && _EffectMode < 11.5)
                {
                    float2 starUv = uv * 8.0 + float2(time * 0.15, -time * 0.08);
                    float2 starCell = floor(starUv);
                    float2 starLocal = frac(starUv) - 0.5;
                    float starSeed = Hash21(starCell);
                    float starCore = (1.0 - smoothstep(0.025, 0.09, length(starLocal))) * step(0.78, starSeed);
                    float starRay = (1.0 - smoothstep(0.01, 0.08, min(abs(starLocal.x), abs(starLocal.y)))) * step(0.88, starSeed);
                    flowNoise = Fbm(float2(edgeFlow * _NoiseScale + time * 0.9, edgeDistance * 40.0));
                    float edgeDust = (1.0 - smoothstep(_OutlineThickness * 0.7, _OutlineThickness * 4.0 + _GlowSize, edgeDistance)) * innerFade;
                    pulse = 1.0;
                    spark = smoothstep(0.54, 0.96, flowNoise) * _NoiseStrength;
                    ring = saturate(outline * (0.3 + spark * 0.65) + glow * 0.18 + edgeDust * spark * 0.3 + (starCore + starRay * 0.35) * edgeDust * 0.78);
                }
                else if (_EffectMode > 11.5 && _EffectMode < 12.5)
                {
                    flowNoise = Fbm(uv * _NoiseScale * 0.72 + float2(time * 0.55, time * 0.2));
                    float prismA = 1.0 - smoothstep(0.015, 0.075, abs(frac((uv.x + uv.y) * 2.8 - time * 0.1) - 0.5));
                    float prismB = 1.0 - smoothstep(0.018, 0.08, abs(frac((uv.x - uv.y) * 2.4 + time * 0.12) - 0.5));
                    float prismMask = (1.0 - smoothstep(_OutlineThickness * 0.6, _OutlineThickness * 4.8 + _GlowSize, edgeDistance)) * innerFade;
                    pulse = 1.0;
                    spark = smoothstep(0.46, 0.92, flowNoise) * _NoiseStrength;
                    ring = saturate(outline * (0.42 + spark * 0.42) + glow * 0.22 + prismMask * (prismA * 0.28 + prismB * 0.22 + spark * 0.2));
                }
                else if (_EffectMode > 12.5 && _EffectMode < 13.5)
                {
                    flowNoise = Fbm(float2(edgeFlow * _NoiseScale * 1.2 - time * 0.35, edgeDistance * 58.0 + time));
                    float pulseRingA = 1.0 - smoothstep(0.018, 0.1, abs(edgeDistance - (_OutlineThickness * 1.2 + 0.018 * sin(_Time.y * 1.4))));
                    float pulseRingB = 1.0 - smoothstep(0.02, 0.11, abs(edgeDistance - (_OutlineThickness * 3.0 + 0.02 * cos(_Time.y * 1.1))));
                    float ember = smoothstep(0.62, 0.96, flowNoise) * _NoiseStrength;
                    pulse = 1.0;
                    spark = ember;
                    ring = saturate(outline * (0.34 + ember * 0.7) + glow * 0.24 + pulseRingA * (0.3 + ember * 0.22) + pulseRingB * ember * 0.3);
                }
                else if (_EffectMode > 13.5 && _EffectMode < 14.5)
                {
                    flowNoise = Fbm(float2(edgeFlow * _NoiseScale * 1.4 + time * 1.1, edgeDistance * 64.0));
                    float cometHead = 1.0 - smoothstep(0.012, 0.06, abs(frac(edgeFlow - time * 0.16) - 0.5));
                    float cometTail = 1.0 - smoothstep(0.0, 0.26, frac(edgeFlow - time * 0.16));
                    float cometBand = (1.0 - smoothstep(_OutlineThickness * 0.35, _OutlineThickness * 4.4 + _GlowSize, edgeDistance)) * innerFade;
                    pulse = 1.0;
                    spark = smoothstep(0.48, 0.92, flowNoise) * _NoiseStrength;
                    ring = saturate(outline * (0.34 + spark * 0.7) + glow * 0.18 + cometBand * (cometHead * 0.92 + cometTail * spark * 0.34));
                }
                else if (_EffectMode > 14.5 && _EffectMode < 15.5)
                {
                    flowNoise = Fbm(float2(edgeFlow * _NoiseScale * 1.55 + time * 1.6, edgeDistance * 76.0 - time * 0.6));
                    float stormA = 1.0 - smoothstep(0.012, 0.07, abs(frac(edgeFlow * 1.3 - time * 0.2) - 0.5));
                    float stormB = 1.0 - smoothstep(0.015, 0.08, abs(frac(edgeFlow * 2.1 + time * 0.16 + 0.25) - 0.5));
                    float flame = smoothstep(0.42, 0.88, Fbm(float2(edgeFlow * _NoiseScale * 0.72 - time * 0.85, edgeDistance * 48.0 + time))) * _NoiseStrength;
                    float star = smoothstep(0.72, 0.98, Hash21(floor(uv * 10.0 + time))) * (1.0 - smoothstep(0.08, 0.45, edgeDistance));
                    float stormBand = (1.0 - smoothstep(_OutlineThickness * 0.35, _OutlineThickness * 5.4 + _GlowSize * 1.6, edgeDistance)) * innerFade;
                    pulse = 0.98 + 0.02 * sin(_Time.y * _PulseSpeed * 6.28318);
                    spark = smoothstep(0.45, 0.95, flowNoise) * _NoiseStrength;
                    ring = saturate(outline * (0.42 + spark * 0.85) + glow * 0.3 + stormBand * (stormA * 0.5 + stormB * 0.38 + flame * 0.36 + star * 0.28));
                }
                else if (_EffectMode > 15.5 && _EffectMode < 16.5)
                {
                    flowNoise = Fbm(float2(edgeFlow * _NoiseScale * 0.92 - time * 0.72, edgeDistance * 46.0 + time * 1.28));
                    float flameBand = (1.0 - smoothstep(_OutlineThickness * 0.25, _OutlineThickness * 2.35 + _GlowSize * 0.38, edgeDistance)) * innerFade;
                    float licking = smoothstep(0.34, 0.84, flowNoise + flameBand * 0.18) * _NoiseStrength;
                    float flameTongue = 1.0 - smoothstep(0.04, 0.18, abs(frac(edgeFlow * 3.4 + time * 0.1) - 0.5));
                    pulse = 0.99 + 0.01 * sin(_Time.y * _PulseSpeed * 6.28318);
                    spark = licking;
                    ring = saturate(outline * (0.4 + spark * 0.88) + glow * 0.18 + flameBand * (0.12 + licking * 0.36 + flameTongue * licking * 0.22));
                }
                else if (_EffectMode > 16.5 && _EffectMode < 17.5)
                {
                    flowNoise = Fbm(uv * _NoiseScale * 0.78 + float2(time * 0.46, time * 0.16));
                    float prismA = 1.0 - smoothstep(0.018, 0.065, abs(frac((uv.x + uv.y) * 2.9 - time * 0.09) - 0.5));
                    float prismB = 1.0 - smoothstep(0.02, 0.07, abs(frac((uv.x - uv.y) * 2.35 + time * 0.11) - 0.5));
                    float prismMask = (1.0 - smoothstep(_OutlineThickness * 0.45, _OutlineThickness * 2.55 + _GlowSize * 0.42, edgeDistance)) * innerFade;
                    pulse = 1.0;
                    spark = smoothstep(0.48, 0.9, flowNoise) * _NoiseStrength;
                    ring = saturate(outline * (0.46 + spark * 0.38) + glow * 0.16 + prismMask * (prismA * 0.24 + prismB * 0.2 + spark * 0.18));
                }
                else if (_EffectMode > 17.5 && _EffectMode < 18.5)
                {
                    flowNoise = Fbm(float2(edgeFlow * _NoiseScale * 1.2 + time * 1.05, edgeDistance * 58.0 - time * 0.2));
                    float cometHead = 1.0 - smoothstep(0.014, 0.06, abs(frac(edgeFlow - time * 0.14) - 0.5));
                    float cometTail = 1.0 - smoothstep(0.0, 0.22, frac(edgeFlow - time * 0.14));
                    float prismLine = 1.0 - smoothstep(0.016, 0.075, abs(frac((uv.x + uv.y) * 2.7 - time * 0.08) - 0.5));
                    float cometBand = (1.0 - smoothstep(_OutlineThickness * 0.35, _OutlineThickness * 3.3 + _GlowSize * 0.85, edgeDistance)) * innerFade;
                    pulse = 1.0;
                    spark = smoothstep(0.5, 0.94, flowNoise) * _NoiseStrength;
                    ring = saturate(outline * (0.38 + spark * 0.72) + glow * 0.2 + cometBand * (cometHead * 0.72 + cometTail * spark * 0.28 + prismLine * 0.22));
                }
                else if (_EffectMode > 18.5)
                {
                    flowNoise = Fbm(uv * _NoiseScale * 1.04 + float2(time * 0.38, -time * 0.18));
                    float2 crystalUv = uv * 7.0 + float2(time * 0.08, time * 0.05);
                    float2 crystalLocal = abs(frac(crystalUv) - 0.5);
                    float crystalSeed = Hash21(floor(crystalUv));
                    float crystal = (1.0 - smoothstep(0.02, 0.13, max(crystalLocal.x, crystalLocal.y))) * step(0.72, crystalSeed);
                    float edgeCrystalMask = (1.0 - smoothstep(_OutlineThickness * 0.42, _OutlineThickness * 3.0 + _GlowSize * 0.65, edgeDistance)) * innerFade;
                    float fineLine = 1.0 - smoothstep(0.012, 0.055, abs(frac(edgeFlow * 4.0 + time * 0.1) - 0.5));
                    pulse = 1.0;
                    spark = smoothstep(0.52, 0.96, flowNoise) * _NoiseStrength;
                    ring = saturate(outline * (0.44 + spark * 0.56) + glow * 0.16 + edgeCrystalMask * (crystal * 0.46 + fineLine * spark * 0.18));
                }

                float energy = saturate(0.48 + spark);

                float vertexAlpha = max(IN.color.a, 0.2);
                float alpha = saturate(ring * energy * pulse * _Alpha * vertexAlpha * shapeMask);
                float defaultBoost = saturate(ring * 0.35 + outline * (0.55 + spark) + glow * 0.08);
                float3 color = IN.color.rgb * (1.0 + _GlowIntensity * defaultBoost);

                #ifdef UNITY_UI_CLIP_RECT
                alpha *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - 0.001);
                #endif

                return fixed4(color, alpha);
            }
            ENDCG
        }
    }
}
