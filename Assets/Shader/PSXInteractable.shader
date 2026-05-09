Shader "Iris/PSXInteractable"
{
    // General-purpose highlight overlay. Renders a tinted color wash
    // over the object with fresnel edge emphasis and optional pulse.
    // Added as an extra material slot — no shadow casting, additive blend.
    Properties
    {
        _Color         ("Tint",        Color) = (1, 0.95, 0.85, 0.3)

        [Header(Fresnel)]
        _FresnelPower  ("Fresnel Power", Range(0.5, 8)) = 2.0
        _FresnelMin    ("Fresnel Min (center opacity)", Range(0, 1)) = 0.3

        [Header(Pulse)]
        _PulseSpeed    ("Pulse Speed",  Range(0, 6)) = 1.5
        _PulseAmount   ("Pulse Amount", Range(0, 1)) = 0.3

        [HideInInspector] _ZTest ("ZTest", Float) = 4
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+10"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "HighlightOverlay"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha One   // Additive with alpha control
            ZWrite Off
            ZTest [_ZTest]
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewDirWS  : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half4  _Color;
                half   _FresnelPower;
                half   _FresnelMin;
                half   _PulseSpeed;
                half   _PulseAmount;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS  = GetWorldSpaceNormalizeViewDir(posInputs.positionWS);
                output.fogFactor  = ComputeFogFactor(posInputs.positionCS.z);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Fresnel: bright at edges, fades toward center
                half3 normalWS = normalize(input.normalWS);
                half3 viewDir  = normalize(input.viewDirWS);
                half fresnel   = 1.0 - saturate(dot(normalWS, viewDir));
                fresnel = pow(fresnel, _FresnelPower);

                // Pulse
                half pulse = 1.0 - _PulseAmount * (sin(_Time.y * _PulseSpeed) * 0.5 + 0.5);

                // Alpha: min opacity across surface, stronger at edges
                half alpha = _Color.a * lerp(_FresnelMin, 1.0, fresnel) * pulse;

                half3 result = _Color.rgb;
                result = MixFog(result, input.fogFactor);

                return half4(result, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
