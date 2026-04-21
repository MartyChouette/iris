Shader "Iris/Fullscreen/DoubleExposure"
{
    Properties
    {
        _FeedbackDecay ("Feedback Decay (lower = longer trails)", Range(0.01, 0.5)) = 0.05
        _BlendIntensity ("Blend Intensity", Range(0, 1)) = 0.35
        _BlurRadius ("Blur Radius (texels)", Range(0, 6)) = 2.0
        _BloomThreshold ("Bloom Threshold", Range(0, 1.5)) = 0.4
        _BloomSoftness ("Bloom Softness", Range(0, 1)) = 0.5
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

        // Pass 0: Composite — blend feedback buffer over current frame
        Pass
        {
            Name "DoubleExposureComposite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_FeedbackTex); SAMPLER(sampler_FeedbackTex);

            CBUFFER_START(UnityPerMaterial)
                float _BlendIntensity;
                float _BloomThreshold;
                float _BloomSoftness;
            CBUFFER_END

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half4 feedback = SAMPLE_TEXTURE2D(_FeedbackTex, sampler_FeedbackTex, uv);

                // Screen blend: result = 1 - (1 - A) * (1 - B)
                // Gives that luminous double-exposure look without blowing out
                half3 blended = 1.0 - (1.0 - scene.rgb) * (1.0 - feedback.rgb * _BlendIntensity);

                // Soft bloom boost on bright feedback areas
                half feedLuma = dot(feedback.rgb, half3(0.299, 0.587, 0.114));
                half bloomMask = smoothstep(_BloomThreshold, _BloomThreshold + _BloomSoftness, feedLuma);
                blended += feedback.rgb * bloomMask * _BlendIntensity * 0.3;

                return half4(blended, 1.0);
            }
            ENDHLSL
        }

        // Pass 1: Update feedback buffer — decay old + accumulate new frame (blurred)
        Pass
        {
            Name "DoubleExposureFeedback"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_FeedbackTex); SAMPLER(sampler_FeedbackTex);

            CBUFFER_START(UnityPerMaterial)
                float _FeedbackDecay;
                float _BlurRadius;
            CBUFFER_END

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 texelSize = 1.0 / _ScreenParams.xy;

                // Box blur the current frame (5-tap cross + center)
                half4 center = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half4 blurred = center;
                blurred += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(_BlurRadius, 0) * texelSize);
                blurred += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(_BlurRadius, 0) * texelSize);
                blurred += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0, _BlurRadius) * texelSize);
                blurred += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(0, _BlurRadius) * texelSize);
                blurred /= 5.0;

                // Read previous feedback and decay
                half4 prevFeedback = SAMPLE_TEXTURE2D(_FeedbackTex, sampler_FeedbackTex, uv);
                half4 decayed = prevFeedback * (1.0 - _FeedbackDecay);

                // Accumulate: max of decayed history and new blurred frame
                // Using max keeps bright trails without over-accumulating
                half3 result = max(decayed.rgb, blurred.rgb * _FeedbackDecay * 2.0);

                return half4(result, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
