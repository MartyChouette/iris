Shader "Iris/PSXMirror"
{
    Properties
    {
        [Header(Mirror)]
        _ReflectionTex ("Reflection Texture", 2D) = "black" {}
        _Tint          ("Tint",   Color) = (0.85, 0.9, 0.95, 1)
        _Brightness    ("Brightness", Range(0.5, 2.0)) = 0.9

        [Header(PSX Effects)]
        _VertexSnapResolution ("Vertex Snap Resolution", Vector) = (160, 120, 0, 0)

        [Header(Retro Distortion)]
        _WarpStrength  ("Edge Warp Strength", Range(0, 0.05)) = 0.015
        _ScanlineAlpha ("Scanline Darkening",  Range(0, 0.5))  = 0.15
        _ColorBands    ("Color Bands", Range(4, 64)) = 24
        _NoiseAmount   ("Noise Grain", Range(0, 0.15)) = 0.04
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry+10"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "MirrorForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos  : TEXCOORD0;
                float  fogFactor  : TEXCOORD1;
                float2 uv         : TEXCOORD2;
            };

            TEXTURE2D(_ReflectionTex);
            SAMPLER(sampler_ReflectionTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _ReflectionTex_ST;
                half4  _Tint;
                half   _Brightness;
                float4 _VertexSnapResolution;
                half   _WarpStrength;
                half   _ScanlineAlpha;
                half   _ColorBands;
                half   _NoiseAmount;
            CBUFFER_END

            // ── Simple hash for noise ──
            float Hash(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                float4 clipPos = posInputs.positionCS;

                // ── PSX vertex snapping ──
                float2 snapRes = _VertexSnapResolution.xy;
                if (snapRes.x > 0 && snapRes.y > 0)
                {
                    clipPos.xy = floor(clipPos.xy / clipPos.w * snapRes + 0.5)
                               / snapRes * clipPos.w;
                }

                output.positionCS = clipPos;
                output.screenPos  = ComputeScreenPos(clipPos);
                output.fogFactor  = ComputeFogFactor(clipPos.z);
                output.uv         = input.uv;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Project reflection texture via screen-space UVs
                float2 screenUV = input.screenPos.xy / input.screenPos.w;

                // No manual UV flip — the reflection matrix handles mirroring

                // ── Barrel distortion (subtle CRT warp at edges) ──
                float2 centered = screenUV - 0.5;
                float r2 = dot(centered, centered);
                screenUV += centered * r2 * _WarpStrength;

                // Sample reflection
                half4 refl = SAMPLE_TEXTURE2D(_ReflectionTex, sampler_ReflectionTex, screenUV);

                // ── Color banding (posterize) ──
                refl.rgb = floor(refl.rgb * _ColorBands + 0.5) / _ColorBands;

                // Apply tint and brightness
                refl.rgb *= _Tint.rgb * _Brightness;

                // ── Scanlines (every other pixel row) ──
                float2 pixelPos = screenUV * _ScreenParams.xy;
                float scanline = step(0.5, frac(pixelPos.y * 0.5));
                refl.rgb *= 1.0 - (_ScanlineAlpha * scanline);

                // ── Film grain noise ──
                float noise = Hash(pixelPos + _Time.y * 7.31) * 2.0 - 1.0;
                refl.rgb += noise * _NoiseAmount;

                refl.rgb = MixFog(refl.rgb, input.fogFactor);

                return half4(refl.rgb, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
