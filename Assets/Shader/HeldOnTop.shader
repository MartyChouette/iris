Shader "Iris/HeldOnTop"
{
    // A deliberately simple unlit shader used while an item is held. ZTest
    // Always + Overlay queue means the held item is drawn AFTER every other
    // opaque renderer in the scene, regardless of depth — so walls, furniture,
    // and paired sibling items can't occlude it. Swap onto the material via
    // PlaceableObject.SetRenderOnTop; the original shader is restored on drop.
    //
    // Properties match URP/Lit (_BaseMap / _BaseColor / _MainTex / _Color) so
    // textures and tints carry across the swap without extra bookkeeping.
    Properties
    {
        [MainTexture] _BaseMap   ("Base Map",   2D)    = "white" {}
        [MainColor]   _BaseColor ("Base Color", Color) = (1, 1, 1, 1)

        // URP/Lit uses _BaseMap/_BaseColor, but other shaders in the project
        // (Iris/PSXLit*) use _MainTex/_Color. Declaring both keeps the swap
        // working for either source.
        _MainTex ("Main Tex",  2D)    = "white" {}
        _Color   ("Color",     Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType"    = "Opaque"
            "Queue"         = "Overlay"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "HeldOnTop"
            Tags { "LightMode" = "UniversalForward" }

            ZTest Always
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _MainTex_ST;
                float4 _BaseColor;
                float4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                // Use _BaseMap_ST if present, else _MainTex_ST.
                OUT.uv = IN.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Blend the two convention pairs — whichever is set to white/1
                // falls out, so materials using either naming get the right
                // albedo + tint.
                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half4 mainTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                half4 tex = baseTex * mainTex;
                half4 tint = _BaseColor * _Color;
                return tex * tint;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
