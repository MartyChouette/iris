Shader "Iris/CursorShadow"
{
    Properties
    {
        [Header(Cursor Texture)]
        _MainTex       ("Cursor Texture", 2D) = "white" {}
        _UseTexture    ("Use Texture", Float) = 0

        [Header(Shadow)]
        _ShadowColor   ("Shadow Color", Color) = (0.05, 0.03, 0.08, 0.5)
        _ShadowRadius  ("Shadow Radius", Range(0.01, 1.0)) = 0.15
        _ShadowSoftness("Shadow Softness", Range(0.01, 1.0)) = 0.4

        [Header(Reflection)]
        _ReflColor     ("Reflection Color", Color) = (1, 1, 1, 0.12)
        _ReflRadius    ("Reflection Radius", Range(0.01, 0.5)) = 0.08
        _ReflSoftness  ("Reflection Softness", Range(0.01, 1.0)) = 0.3

        [Header(Animation)]
        _PulseSpeed    ("Pulse Speed", Range(0, 8)) = 2.0
        _PulseAmount   ("Pulse Amount", Range(0, 0.3)) = 0.05
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Overlay"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "CursorShadow"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half   _UseTexture;
                half4  _ShadowColor;
                half   _ShadowRadius;
                half   _ShadowSoftness;
                half4  _ReflColor;
                half   _ReflRadius;
                half   _ReflSoftness;
                half   _PulseSpeed;
                half   _PulseAmount;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 centered = input.uv - 0.5;
                float dist = length(centered);
                float pulse = 1.0 + sin(_Time.y * _PulseSpeed) * _PulseAmount;

                if (_UseTexture > 0.5)
                {
                    // Texture mode: cursor silhouette as shadow
                    half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                    half alpha = tex.a * _ShadowColor.a;
                    clip(alpha - 0.001);
                    return half4(_ShadowColor.rgb, alpha);
                }
                else
                {
                    // Procedural circle mode (original behavior)
                    float shadowEdge = _ShadowRadius * pulse;
                    float shadowAlpha = 1.0 - smoothstep(shadowEdge * (1.0 - _ShadowSoftness), shadowEdge, dist);
                    half4 shadow = half4(_ShadowColor.rgb, _ShadowColor.a * shadowAlpha);

                    float reflEdge = _ReflRadius * pulse;
                    float reflAlpha = 1.0 - smoothstep(reflEdge * (1.0 - _ReflSoftness), reflEdge, dist);
                    half4 refl = half4(_ReflColor.rgb, _ReflColor.a * reflAlpha);

                    half3 color = lerp(shadow.rgb, refl.rgb, reflAlpha * refl.a);
                    half alpha = max(shadow.a, refl.a);
                    clip(alpha - 0.001);
                    return half4(color, alpha);
                }
            }
            ENDHLSL
        }
    }

    FallBack Off
}
