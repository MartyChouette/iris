Shader "Iris/HighlightClean"
{
    // Clean silhouette outline — uniform screen-space width.
    // Extrudes in clip space so the line is the same pixel width
    // regardless of mesh complexity, distance, or vertex count.
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1, 0.95, 0.85, 1)
        _OutlineWidth ("Outline Width (px)", Range(0.5, 10)) = 2.0
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
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;

                // Transform vertex and normal to clip space
                float4 clipPos = TransformObjectToHClip(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 normalCS = mul((float3x3)UNITY_MATRIX_VP, normalWS);

                // Push outward in screen space — width is in pixels
                float2 offset = normalize(normalCS.xy) * _OutlineWidth * 2.0 / _ScreenParams.xy * clipPos.w;
                clipPos.xy += offset;

                output.positionCS = clipPos;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
