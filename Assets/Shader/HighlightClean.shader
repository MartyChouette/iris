Shader "Iris/HighlightClean"
{
    // Clean silhouette outline — follows real geometry exactly.
    // No vertex snapping, no jitter, no PSX effects.
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1, 0.95, 0.85, 1)
        _OutlineWidth ("Outline Width", Range(0.001, 0.05)) = 0.008
        _PulseSpeed   ("Pulse Speed", Range(0, 8)) = 2.0
        _PulseAmount  ("Pulse Amount", Range(0, 0.5)) = 0.1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+50"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "CleanOutline"
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                half4  _OutlineColor;
                half   _OutlineWidth;
                half   _PulseSpeed;
                half   _PulseAmount;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 norm = normalize(input.normalOS);
                float3 extruded = input.positionOS.xyz + norm * _OutlineWidth;
                output.positionCS = TransformObjectToHClip(extruded);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half pulse = 1.0 + sin(_Time.y * _PulseSpeed) * _PulseAmount;
                return half4(_OutlineColor.rgb, _OutlineColor.a * pulse);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
