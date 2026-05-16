Shader "Iris/Fullscreen/PSXPost"
{
    Properties
    {
        _ColorDepth     ("Color Depth (levels per channel)", Float) = 32
        _DitherIntensity ("Dither Intensity", Range(0, 1)) = 0.5
        _FinePattern     ("Fine Pattern (0=2x2, 1=4x4, 2=8x8)", Float) = 1
        _CoarsePattern   ("Coarse Pattern (0=2x2, 1=4x4, 2=8x8)", Float) = 0
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.5
        _DeepShadowThreshold ("Deep Shadow Threshold", Range(0, 1)) = 0.15
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        ZWrite Off
        Cull Off
        ZTest Always

        Pass
        {
            Name "PSXPost"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _ColorDepth;
                half  _DitherIntensity;
                float _FinePattern;
                float _CoarsePattern;
                half  _ShadowThreshold;
                half  _DeepShadowThreshold;
                float _DitherZoom; // camera zoom factor for world-space-feel dither
                float2 _DitherResolution; // low-res pixel grid size for dither alignment
            CBUFFER_END

            // Tilt-shift globals (set from C# via Shader.SetGlobalFloat)
            float _TiltShiftAmount;   // 0 = off, 1 = full blur
            float _TiltShiftCenter;   // focus band center (0.5 = middle)
            float _TiltShiftWidth;    // half-width of sharp band in UV (0.15 default)
            float _TiltShiftRadius;   // max blur radius in texels (8 default)

            // ── Bayer dither matrices (normalized to 0–1) ──
            static const float Bayer2[4] =
            {
                0.0 / 4.0, 2.0 / 4.0,
                3.0 / 4.0, 1.0 / 4.0
            };

            static const float Bayer4[16] =
            {
                 0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
                12.0 / 16.0,  4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
                 3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
                15.0 / 16.0,  7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0
            };

            static const float Bayer8[64] =
            {
                 0.0/64.0, 32.0/64.0,  8.0/64.0, 40.0/64.0,  2.0/64.0, 34.0/64.0, 10.0/64.0, 42.0/64.0,
                48.0/64.0, 16.0/64.0, 56.0/64.0, 24.0/64.0, 50.0/64.0, 18.0/64.0, 58.0/64.0, 26.0/64.0,
                12.0/64.0, 44.0/64.0,  4.0/64.0, 36.0/64.0, 14.0/64.0, 46.0/64.0,  6.0/64.0, 38.0/64.0,
                60.0/64.0, 28.0/64.0, 52.0/64.0, 20.0/64.0, 62.0/64.0, 30.0/64.0, 54.0/64.0, 22.0/64.0,
                 3.0/64.0, 35.0/64.0, 11.0/64.0, 43.0/64.0,  1.0/64.0, 33.0/64.0,  9.0/64.0, 41.0/64.0,
                51.0/64.0, 19.0/64.0, 59.0/64.0, 27.0/64.0, 49.0/64.0, 17.0/64.0, 57.0/64.0, 25.0/64.0,
                15.0/64.0, 47.0/64.0,  7.0/64.0, 39.0/64.0, 13.0/64.0, 45.0/64.0,  5.0/64.0, 37.0/64.0,
                63.0/64.0, 31.0/64.0, 55.0/64.0, 23.0/64.0, 61.0/64.0, 29.0/64.0, 53.0/64.0, 21.0/64.0
            };

            // Sample Bayer pattern: 0 = 2x2, 1 = 4x4, 2 = 8x8
            // Returns threshold centered around 0 (range ~ -0.5 to +0.5)
            float SampleBayer(float2 pixelPos, int pattern)
            {
                if (pattern <= 0)
                {
                    int2 c = int2(fmod(pixelPos, 2.0));
                    return Bayer2[c.y * 2 + c.x] - 0.5;
                }
                else if (pattern >= 2)
                {
                    int2 c = int2(fmod(pixelPos, 8.0));
                    return Bayer8[c.y * 8 + c.x] - 0.5;
                }
                else
                {
                    int2 c = int2(fmod(pixelPos, 4.0));
                    return Bayer4[c.y * 4 + c.x] - 0.5;
                }
            }

            // 8 offsets in a disc pattern (unit circle)
            static const float2 DiscOffsets[8] =
            {
                float2( 1.0,  0.0),
                float2( 0.707,  0.707),
                float2( 0.0,  1.0),
                float2(-0.707,  0.707),
                float2(-1.0,  0.0),
                float2(-0.707, -0.707),
                float2( 0.0, -1.0),
                float2( 0.707, -0.707)
            };

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                // ── Tilt-shift blur ──
                // Compute blur mask: 0 in focus band, ramps to 1 at screen edges
                float center = _TiltShiftCenter > 0 ? _TiltShiftCenter : 0.5;
                float halfW  = _TiltShiftWidth > 0 ? _TiltShiftWidth : 0.15;
                float maxRad = _TiltShiftRadius > 0 ? _TiltShiftRadius : 8.0;

                float dist = abs(uv.y - center);
                float blurMask = smoothstep(halfW * 0.5, halfW, dist);
                float blurRadius = blurMask * _TiltShiftAmount * maxRad;

                half4 screen;
                if (blurRadius > 0.5)
                {
                    // Disc blur: 8 taps + center, linear sampling for smooth blend
                    float2 texelSize = 1.0 / _ScreenParams.xy;
                    screen = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                    half4 blurred = screen;
                    [unroll]
                    for (int i = 0; i < 8; i++)
                    {
                        float2 offset = DiscOffsets[i] * blurRadius * texelSize;
                        blurred += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + offset);
                    }
                    screen = blurred / 9.0;
                }
                else
                {
                    screen = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);
                }

                float levels = max(_ColorDepth, 2.0);

                // ── Dual-pattern ordered dithering ──
                float2 ditherRes = _DitherResolution.x > 0 ? _DitherResolution : _ScreenParams.xy;
                ditherRes *= max(_DitherZoom, 0.01); // scale with camera zoom
                float2 pixelPos = uv * ditherRes;

                float3 c = screen.rgb;
                float luminance = dot(c, float3(0.299, 0.587, 0.114));

                // Dither strength ramps up below _ShadowThreshold (bright = clean)
                float ditherStrength = 1.0 - smoothstep(0.0, _ShadowThreshold, luminance);

                // Blend from fine to coarse pattern below _DeepShadowThreshold
                float coarseBlend = 1.0 - smoothstep(0.0, _DeepShadowThreshold, luminance);

                // Sample both patterns and blend
                float fineSample   = SampleBayer(pixelPos, (int)_FinePattern);
                float coarseSample = SampleBayer(pixelPos, (int)_CoarsePattern);
                float ditherSample = lerp(fineSample, coarseSample, coarseBlend);

                c += ditherSample * (1.0 / levels) * _DitherIntensity * ditherStrength;

                // ── Color depth reduction (posterization) ──
                c = floor(c * levels + 0.5) / levels;

                return half4(saturate(c), 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
