Shader "Iris/PSXLitGlitch"
{
    Properties
    {
        [Header(Texture)]
        _MainTex ("Albedo", 2D) = "white" {}
        _Color   ("Tint",   Color) = (1, 1, 1, 1)

        [Header(PSX Effects)]
        _VertexSnapResolution ("Vertex Snap Resolution", Vector) = (160, 120, 0, 0)
        _AffineIntensity      ("Affine Intensity", Range(0, 1)) = 1.0

        [Header(Dither Shadows)]
        _ShadowDitherIntensity ("Shadow Dither Intensity", Range(0, 1)) = 1.0

        [Header(Glitch)]
        _GlitchIntensity ("Glitch Intensity", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        // ── Forward Lit Pass ──────────────────────────────────────────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float  fogFactor   : TEXCOORD2;
                float2 uvCorrect   : TEXCOORD3;
                float3 uvAffine    : TEXCOORD4;
                float4 screenPos   : TEXCOORD5;
                float  glitchBand  : TEXCOORD6; // per-vertex band seed
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;
                float4 _VertexSnapResolution;
                half   _AffineIntensity;
                half   _ShadowDitherIntensity;
                half   _GlitchIntensity;
            CBUFFER_END

            // ── Hash functions ──
            float Hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // ── Bayer 8x8 ordered dither matrix ──
            static const float Bayer8x8[64] =
            {
                 0.0/64.0, 48.0/64.0, 12.0/64.0, 60.0/64.0,  3.0/64.0, 51.0/64.0, 15.0/64.0, 63.0/64.0,
                32.0/64.0, 16.0/64.0, 44.0/64.0, 28.0/64.0, 35.0/64.0, 19.0/64.0, 47.0/64.0, 31.0/64.0,
                 8.0/64.0, 56.0/64.0,  4.0/64.0, 52.0/64.0, 11.0/64.0, 59.0/64.0,  7.0/64.0, 55.0/64.0,
                40.0/64.0, 24.0/64.0, 36.0/64.0, 20.0/64.0, 43.0/64.0, 27.0/64.0, 39.0/64.0, 23.0/64.0,
                 2.0/64.0, 50.0/64.0, 14.0/64.0, 62.0/64.0,  1.0/64.0, 49.0/64.0, 13.0/64.0, 61.0/64.0,
                34.0/64.0, 18.0/64.0, 46.0/64.0, 30.0/64.0, 33.0/64.0, 17.0/64.0, 45.0/64.0, 29.0/64.0,
                10.0/64.0, 58.0/64.0,  6.0/64.0, 54.0/64.0,  9.0/64.0, 57.0/64.0,  5.0/64.0, 53.0/64.0,
                42.0/64.0, 26.0/64.0, 38.0/64.0, 22.0/64.0, 41.0/64.0, 25.0/64.0, 37.0/64.0, 21.0/64.0
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                float4 clipPos = posInputs.positionCS;

                // ── Vertex snapping (world space — distance-independent) ──
                float2 snapRes = _VertexSnapResolution.xy;
                if (snapRes.x > 0 && snapRes.y > 0)
                {
                    float3 posWS = posInputs.positionWS;
                    posWS = floor(posWS * snapRes.x + 0.5) / snapRes.x;
                    clipPos = TransformWorldToHClip(posWS);
                }

                // ── Vertex jitter (glitch) ──
                // Vertices twitch in random directions, pulsing over time
                if (_GlitchIntensity > 0.001)
                {
                    // Slow time pulse so glitch comes in waves
                    float timeSeed = floor(_Time.y * 6.0); // 6 ticks/sec — jumpy, not smooth
                    float vertexSeed = Hash21(input.positionOS.xz + timeSeed);

                    // Only some vertices glitch each tick (stochastic)
                    float glitchChance = _GlitchIntensity * 0.6;
                    if (vertexSeed < glitchChance)
                    {
                        float jitterX = (Hash11(vertexSeed * 127.1 + timeSeed) - 0.5) * 2.0;
                        float jitterY = (Hash11(vertexSeed * 269.5 + timeSeed) - 0.5) * 2.0;

                        // Displace in clip space — scales with distance naturally
                        float strength = _GlitchIntensity * 0.08 * clipPos.w;
                        clipPos.xy += float2(jitterX, jitterY) * strength;
                    }
                }

                output.positionCS  = clipPos;
                output.positionWS  = posInputs.positionWS;
                output.normalWS    = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor   = ComputeFogFactor(clipPos.z);
                output.screenPos   = ComputeScreenPos(clipPos);

                float2 uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.uvCorrect = uv;
                output.uvAffine = float3(uv * clipPos.w, clipPos.w);

                // Pass a per-vertex band value for fragment UV corruption
                output.glitchBand = Hash11(input.positionOS.y * 43.17 + floor(_Time.y * 4.0));

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Reconstruct both UV modes
                float2 correctUV = input.uvCorrect;
                float2 affineUV  = input.uvAffine.xy / input.uvAffine.z;

                // Glitch cranks affine harder + adds UV band distortion
                float effectiveAffine = _AffineIntensity;

                float2 uvOffset = float2(0, 0);
                half colorShift = 0;

                if (_GlitchIntensity > 0.001)
                {
                    // Push affine mapping harder — textures warp more
                    effectiveAffine = saturate(_AffineIntensity + _GlitchIntensity * 0.5);

                    // ── UV band corruption ──
                    // Horizontal bands of the screen shift UVs sideways
                    float2 screenUV = input.screenPos.xy / input.screenPos.w;
                    float bandY = floor(screenUV.y * 30.0); // 30 horizontal bands
                    float timeSeed = floor(_Time.y * 5.0);
                    float bandHash = Hash11(bandY * 7.13 + timeSeed);

                    // Only some bands corrupt (more bands at higher intensity)
                    if (bandHash < _GlitchIntensity * 0.35)
                    {
                        float shift = (Hash11(bandY * 31.7 + timeSeed * 3.1) - 0.5);
                        uvOffset.x = shift * _GlitchIntensity * 0.15;
                    }

                    // ── Color channel split seed ──
                    colorShift = _GlitchIntensity * 0.02;
                }

                float2 finalUV = lerp(correctUV, affineUV, effectiveAffine) + uvOffset;

                // ── Sample with optional channel split ──
                half4 tex;
                if (colorShift > 0.001)
                {
                    // RGB channels sample from slightly offset UVs
                    tex.r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, finalUV + float2(colorShift, 0)).r;
                    tex.g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, finalUV).g;
                    tex.b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, finalUV - float2(colorShift, 0)).b;
                    tex.a = 1.0;
                }
                else
                {
                    tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, finalUV);
                }

                half4 albedo = tex * _Color;

                // ── Lighting ──
                float3 normalWS = normalize(input.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half NdotL = saturate(dot(normalWS, mainLight.direction));
                half shadowAtten = mainLight.shadowAttenuation;

                // ── PSX Dither Shadows ──
                if (_ShadowDitherIntensity > 0.001)
                {
                    float2 screenUV = input.screenPos.xy / input.screenPos.w;
                    float2 pixelPos = screenUV * _ScreenParams.xy;
                    int2 ditherCoord = int2(fmod(abs(pixelPos), 8.0));
                    int idx = ditherCoord.y * 8 + ditherCoord.x;
                    float threshold = Bayer8x8[idx];

                    half ditheredShadow = shadowAtten > threshold ? 1.0 : 0.0;
                    shadowAtten = lerp(shadowAtten, ditheredShadow, _ShadowDitherIntensity);
                }

                half3 diffuse = mainLight.color * NdotL * shadowAtten;

                // Additional lights (point/spot lamps in the apartment)
                uint additionalLightCount = GetAdditionalLightsCount();
                for (uint li = 0; li < additionalLightCount; li++)
                {
                    Light addLight = GetAdditionalLight(li, input.positionWS);
                    half addNdotL = saturate(dot(normalWS, addLight.direction));
                    diffuse += addLight.color * addNdotL * addLight.distanceAttenuation * addLight.shadowAttenuation;
                }

                // Ambient: use SH probe lighting (URP doesn't populate UNITY_LIGHTMODEL_AMBIENT)
                half3 ambient = SampleSH(normalWS);

                half3 lit = albedo.rgb * (diffuse + ambient);
                lit = MixFog(lit, input.fogFactor);
                return half4(lit, 1.0);
            }
            ENDHLSL
        }

        // ── ShadowCaster Pass ─────────────────────────────────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

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
                float4 _MainTex_ST;
                half4  _Color;
                float4 _VertexSnapResolution;
                half   _AffineIntensity;
                half   _ShadowDitherIntensity;
                half   _GlitchIntensity;
            CBUFFER_END

            float3 _LightDirection;

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;

                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(
                    ApplyShadowBias(posWS, normalWS, _LightDirection));

                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Simple Lit"
}
