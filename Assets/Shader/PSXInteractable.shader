Shader "Iris/PSXInteractable"
{
    // Overlay pass: renders affine-warped texture on top of the base material.
    // Used by InteractableHighlight to show interactable/pickupable objects.
    // Added as an extra material slot — no shadow casting, additive blend.
    Properties
    {
        _MainTex ("Albedo (from base)", 2D) = "white" {}
        _Color   ("Tint",   Color) = (1, 0.95, 0.85, 0.3)

        [Header(Affine Warp)]
        _WarpIntensity ("Warp Intensity", Range(0, 1)) = 0.4
        _WarpSpeed     ("Warp Pulse Speed", Range(0, 4)) = 1.2
        _WarpMin       ("Warp Pulse Min", Range(0, 1)) = 0.3

        [Header(Vertex Jitter)]
        _JitterAmount  ("Vertex Jitter", Range(0, 0.02)) = 0.002
        _GlitchIntensity ("Glitch Intensity (match PSXLitGlitch)", Range(0, 0.1)) = 0.0

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
            Name "AffineOverlay"
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
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uvCorrect  : TEXCOORD0;
                float3 uvAffine   : TEXCOORD1;  // xy = uv * w, z = w
                float3 normalWS   : TEXCOORD2;
                float3 viewDirWS  : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;
                half   _WarpIntensity;
                half   _WarpSpeed;
                half   _WarpMin;
                half   _JitterAmount;
                half   _GlitchIntensity;
            CBUFFER_END

            // Simple hash for vertex jitter
            float hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 posOS = input.positionOS.xyz;

                // Optional vertex jitter (subtle wobble to feel alive)
                if (_JitterAmount > 0 || _GlitchIntensity > 0)
                {
                    float t = _Time.y;
                    float jitter = _JitterAmount + _GlitchIntensity;
                    float3 offset = float3(
                        hash(posOS.xy + t) - 0.5,
                        hash(posOS.yz + t) - 0.5,
                        hash(posOS.xz + t) - 0.5
                    ) * jitter * 2.0;
                    posOS += offset;
                }

                VertexPositionInputs posInputs = GetVertexPositionInputs(posOS);
                float4 clipPos = posInputs.positionCS;

                output.positionCS = clipPos;
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS  = GetWorldSpaceNormalizeViewDir(posInputs.positionWS);
                output.fogFactor  = ComputeFogFactor(clipPos.z);

                float2 uv = TRANSFORM_TEX(input.uv, _MainTex);

                // Perspective-correct UV
                output.uvCorrect = uv;

                // Affine UV: multiply by clip w before interpolation
                output.uvAffine = float3(uv * clipPos.w, clipPos.w);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Pulsing warp intensity
                half pulse = lerp(_WarpMin, 1.0, (sin(_Time.y * _WarpSpeed) * 0.5 + 0.5));
                half warp = _WarpIntensity * pulse;

                // Reconstruct both UV modes
                float2 correctUV = input.uvCorrect;
                float2 affineUV  = input.uvAffine.xy / input.uvAffine.z;

                // Blend between correct and affine based on warp
                float2 finalUV = lerp(correctUV, affineUV, warp);

                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, finalUV);

                // Fresnel edge glow — stronger at grazing angles
                half3 normalWS = normalize(input.normalWS);
                half3 viewDir  = normalize(input.viewDirWS);
                half fresnel   = 1.0 - saturate(dot(normalWS, viewDir));
                fresnel = pow(fresnel, 2.0);

                // Combine: tinted texture with fresnel edge emphasis
                half3 color = tex.rgb * _Color.rgb;

                // Alpha: visible across entire surface, stronger at edges
                half alpha = _Color.a * (0.6 + fresnel * 0.4) * warp;

                half3 result = color;
                result = MixFog(result, input.fogFactor);

                return half4(result, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
