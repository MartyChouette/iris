Shader "UI/PlantRevealUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _RevealProgress ("Reveal Progress", Range(0, 1)) = 1
        _SoftEdge ("Soft Edge Width", Range(0.01, 0.3)) = 0.08
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
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

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                float4 mask : TEXCOORD1; // object-space pos for rect clipping
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _RevealProgress;
            float _SoftEdge;
            float4 _ClipRect;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.mask = v.vertex; // object-space for UnityGet2DClipping
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * i.color;

                // Bottom-to-top reveal: pixels below the reveal line are visible,
                // above are clipped. Soft edge gives a gentle fade at the boundary.
                float revealLine = _RevealProgress * 1.15;
                float revealAlpha = smoothstep(revealLine, revealLine - _SoftEdge, i.uv.y);

                // Subtle green glow at the reveal edge
                float edgeDist = abs(i.uv.y - (revealLine - _SoftEdge * 0.5));
                float edgeGlow = saturate(exp(-edgeDist * edgeDist * 400.0) * 0.35 * _RevealProgress);
                col.rgb += fixed3(0.3, 0.8, 0.4) * edgeGlow;

                col.a *= revealAlpha;

                // Unity UI rect clipping
                col.a *= UnityGet2DClipping(i.mask.xy, _ClipRect);

                clip(col.a - 0.001);
                return col;
            }
            ENDCG
        }
    }
}
