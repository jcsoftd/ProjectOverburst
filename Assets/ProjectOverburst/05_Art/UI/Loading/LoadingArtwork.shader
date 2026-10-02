Shader "OVERBURST/UI/LoadingArtwork"
{
    Properties
    {
        [PerRendererData] _MainTex ("UI Texture", 2D) = "white" {}
        _ArtworkTex ("Artwork", 2D) = "black" {}
        _ArtworkAspect ("Artwork Aspect", Float) = 1.777778
        _ViewportAspect ("Viewport Aspect", Float) = 1.777778
        _Color ("Tint", Color) = (1,1,1,1)
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
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
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
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 position : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _ArtworkTex;
            fixed4 _Color;
            float4 _ClipRect;
            float _ArtworkAspect, _ViewportAspect;

            v2f vert(appdata input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 artworkUv = input.uv - 0.5;
                float ratio = _ViewportAspect / max(_ArtworkAspect, 0.0001);
                artworkUv *= ratio > 1 ? float2(ratio, 1) : float2(1, 1 / max(ratio, 0.0001));
                artworkUv += 0.5;
                float inside = step(0, artworkUv.x) * step(artworkUv.x, 1) * step(0, artworkUv.y) * step(artworkUv.y, 1);
                fixed3 artwork = tex2D(_ArtworkTex, saturate(artworkUv)).rgb * inside;
                // A quiet footer keeps the existing progress bar legible.
                artwork *= 1 - 0.55 * (1 - smoothstep(0, 0.22, input.uv.y));
                fixed4 output = fixed4(artwork, 1) * input.color;
                #ifdef UNITY_UI_CLIP_RECT
                output.a *= UnityGet2DClipping(input.position.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(output.a - 0.001);
                #endif
                return output;
            }
            ENDCG
        }
    }
}
