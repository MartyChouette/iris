Shader "Iris/Fullscreen/PSXPost"
{
    Properties
    {
        _ColorDepth     ("Color Depth (levels per channel)", Float) = 32
        _DitherIntensity ("Dither Intensity", Range(0, 1)) = 0.5
        _DitherShadowBias ("Dither Shadow Bias (0 = uniform, 1 = dark areas only)", Range(0, 1)) = 0.7
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
                half  _DitherShadowBias;
                float2 _DitherResolution; // low-res pixel grid size for dither alignment
            CBUFFER_END

            // Tilt-shift globals (set from C# via Shader.SetGlobalFloat)
            float _TiltShiftAmount;   // 0 = off, 1 = full blur
            float _TiltShiftCenter;   // focus band center (0.5 = middle)
            float _TiltShiftWidth;    // half-width of sharp band in UV (0.15 default)
            float _TiltShiftRadius;   // max blur radius in texels (8 default)

            // ── Bayer 4×4 ordered dither matrix (normalized to 0–1) ──
            static const float BayerMatrix[16] =
            {
                 0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
                12.0 / 16.0,  4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
                 3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
                15.0 / 16.0,  7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0
            };

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

                // ── Ordered dithering ──
                // Use low-res pixel grid so dither pattern stays constant
                // regardless of resolution divisor
                float2 ditherRes = _DitherResolution.x > 0 ? _DitherResolution : _ScreenParams.xy;
                float2 pixelPos = uv * ditherRes;
                int2 ditherCoord = int2(fmod(pixelPos, 4.0));
                int idx = ditherCoord.y * 4 + ditherCoord.x;
                float threshold = BayerMatrix[idx] - 0.5; // center around 0

                float3 c = screen.rgb;

                // Shadow-biased dither: at _DitherShadowBias = 1, dither only
                // appears in dark areas (classic PS1 stipple shadows). At 0, uniform.
                float luminance = dot(c, float3(0.299, 0.587, 0.114));
                float darknessMask = 1.0 - saturate(luminance * 2.0); // 1 in dark, 0 in bright
                float biasedIntensity = lerp(_DitherIntensity, _DitherIntensity * darknessMask, _DitherShadowBias);

                c += threshold * (1.0 / levels) * biasedIntensity;

                // ── Color depth reduction (posterization) ──
                c = floor(c * levels + 0.5) / levels;

                return half4(saturate(c), 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
