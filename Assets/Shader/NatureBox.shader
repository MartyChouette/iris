Shader "Iris/NatureBox"
{
    Properties
    {
        [Header(Time)]
        _TimeOfDay ("Time of Day (0 midnight, 0.5 noon)", Range(0, 1)) = 0.5

        [Header(Sky)]
        _SunSize ("Sun Moon Sharpness", Range(0.90, 1.0)) = 0.97
        _SunGlow ("Sun Glow", Range(0, 1)) = 0.4

        [Header(Clouds)]
        _CloudSpeed ("Cloud Speed", Float) = 0.015
        _CloudScale ("Cloud Scale", Float) = 2.5
        _CloudDensity ("Cloud Coverage", Range(0, 1)) = 0.45
        _CloudSharpness ("Cloud Edge Sharpness", Range(0.01, 0.5)) = 0.25

        [Header(Terrain)]
        _MountainScale ("Mountain Scale", Float) = 2.5
        _MountainHeight ("Mountain Height", Range(0, 0.35)) = 0.2
        _TreelineHeight ("Treeline Height", Range(0, 0.15)) = 0.07

        [Header(Atmosphere)]
        _HorizonFog ("Horizon Fog", Range(0, 1)) = 0.55
        _StarDensity ("Star Density", Range(0.95, 1.0)) = 0.985

        [Header(Weather)]
        _RainIntensity ("Rain Intensity", Range(0, 1)) = 0
        _SnowIntensity ("Snow Intensity", Range(0, 1)) = 0
        _LeafIntensity ("Falling Leaf Intensity", Range(0, 1)) = 0
        _OvercastDarken ("Overcast Darkening", Range(0, 1)) = 0
        _SnowCapIntensity ("Mountain Snow Caps", Range(0, 1)) = 0
        _RainSpeed ("Rain Speed", Float) = 1.5
        _RainScale ("Rain Scale", Float) = 80.0
        _SnowSpeed ("Snow Speed", Float) = 0.3
        _SnowScale ("Snow Scale", Float) = 60.0
        _LeafSpeed ("Leaf Speed", Float) = 0.4
        _LeafScale ("Leaf Scale", Float) = 25.0

        [Header(World Curve)]
        _HorizonCurve ("Horizon Curve (push horizon down)", Range(0, 0.3)) = 0

        [Header(Retro)]
        _PixelDensity ("Pixel Density (0 off, 240 N64, 480 PS2)", Float) = 320
        _ColorDepth ("Color Depth per channel (0 off, 16 N64, 32 PS1)", Float) = 24
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Background"
            "Queue" = "Background"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        ZWrite Off

        Pass
        {
            Name "NatureBox"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _TimeOfDay;
                float _SunSize;
                float _SunGlow;
                float _CloudSpeed;
                float _CloudScale;
                float _CloudDensity;
                float _CloudSharpness;
                float _MountainScale;
                float _MountainHeight;
                float _TreelineHeight;
                float _HorizonFog;
                float _StarDensity;
                float _RainIntensity;
                float _SnowIntensity;
                float _LeafIntensity;
                float _OvercastDarken;
                float _SnowCapIntensity;
                float _RainSpeed;
                float _RainScale;
                float _SnowSpeed;
                float _SnowScale;
                float _LeafSpeed;
                float _LeafScale;
                float _HorizonCurve;
                float _PixelDensity;
                float _ColorDepth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            // ── Noise ────────────────────────────────────────────────

            float hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float noise2D(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = hash21(i);
                float b = hash21(i + float2(1.0, 0.0));
                float c = hash21(i + float2(0.0, 1.0));
                float d = hash21(i + float2(1.0, 1.0));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // Simpler FBM for Y2K/PS2 aesthetic — fewer octaves = blockier, less painterly
            float fbm2(float2 p)
            {
                float v = 0.5 * noise2D(p) + 0.25 * noise2D(p * 2.01);
                return v / 0.75;
            }

            float fbm3(float2 p)
            {
                float v = 0.5 * noise2D(p) + 0.25 * noise2D(p * 2.01) + 0.125 * noise2D(p * 4.03);
                return v / 0.875;
            }

            float fbm4(float2 p)
            {
                float v = 0.0;
                float amp = 0.5;
                float freq = 1.0;
                v += amp * noise2D(p * freq); amp *= 0.5; freq *= 2.01;
                v += amp * noise2D(p * freq); amp *= 0.5; freq *= 2.02;
                v += amp * noise2D(p * freq); amp *= 0.5; freq *= 2.01;
                v += amp * noise2D(p * freq);
                return v / 0.9375;
            }

            // ── Color palette ────────────────────────────────────────
            // Branchless 4-stop cyclic lerp.
            // t: 0 = midnight, 0.25 = dawn, 0.5 = noon, 0.75 = dusk.

            float3 palette4(float t, float3 c0, float3 c1, float3 c2, float3 c3)
            {
                t = frac(t) * 4.0;
                float3 ab = lerp(c0, c1, saturate(t));
                float3 bc = lerp(c1, c2, saturate(t - 1.0));
                float3 cd = lerp(c2, c3, saturate(t - 2.0));
                float3 da = lerp(c3, c0, saturate(t - 3.0));

                float w0 = step(t, 1.0);
                float w1 = step(1.0, t) * step(t, 2.0);
                float w2 = step(2.0, t) * step(t, 3.0);
                float w3 = step(3.0, t);

                return ab * w0 + bc * w1 + cd * w2 + da * w3;
            }

            // ── FF8 / SotC desaturated blue-teal palette ──
            // Muted, washed out, melancholy. Blue-teal shadows, pale warm highlights.

            float3 zenithColor(float t)
            {
                return palette4(t,
                    float3(0.02, 0.03, 0.08),   // midnight: deep blue-black
                    float3(0.18, 0.16, 0.30),   // dawn: muted lavender
                    float3(0.40, 0.50, 0.62),   // noon: desaturated blue-gray
                    float3(0.15, 0.12, 0.28));  // dusk: muted purple
            }

            float3 horizColor(float t)
            {
                return palette4(t,
                    float3(0.04, 0.04, 0.10),   // midnight: deep blue
                    float3(0.55, 0.38, 0.35),   // dawn: muted peach (less vivid)
                    float3(0.55, 0.60, 0.70),   // noon: pale blue-gray haze
                    float3(0.48, 0.28, 0.25));  // dusk: muted warm
            }

            float3 sunColor(float t)
            {
                return palette4(t,
                    float3(0.35, 0.40, 0.50),   // midnight: cool moon
                    float3(0.75, 0.55, 0.35),   // dawn: soft gold (desaturated)
                    float3(0.85, 0.82, 0.75),   // noon: pale warm
                    float3(0.70, 0.32, 0.18));  // dusk: muted sunset
            }

            float3 sunDirection(float t)
            {
                float a = t * 6.28318;
                return normalize(float3(sin(a), -cos(a) * 0.75 + 0.05, 0.25));
            }

            float starField(float2 p, float density)
            {
                float2 id = floor(p * 150.0);
                float h = hash21(id);
                float star = step(density, h);
                star *= 0.5 + 0.5 * sin(_Time.y * (1.5 + h * 3.0) + h * 50.0);
                return star;
            }

            // ── Vertex ───────────────────────────────────────────────

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                return o;
            }

            // ── Fragment ─────────────────────────────────────────────

            half4 frag(Varyings input) : SV_Target
            {
                float3 viewDir;
                if (unity_OrthoParams.w > 0.5)
                {
                    // Orthographic: all rays are parallel, so reconstruct a fake
                    // perspective viewDir from screen position so the sky still
                    // renders with proper gradient/clouds/weather.
                    float2 ndc = (input.positionCS.xy / _ScreenParams.xy) * 2.0 - 1.0;
                    float3 forward = float3(UNITY_MATRIX_V._m20, UNITY_MATRIX_V._m21, UNITY_MATRIX_V._m22);
                    float3 right   = float3(UNITY_MATRIX_V._m00, UNITY_MATRIX_V._m01, UNITY_MATRIX_V._m02);
                    float3 up      = float3(UNITY_MATRIX_V._m10, UNITY_MATRIX_V._m11, UNITY_MATRIX_V._m12);
                    viewDir = normalize(-forward + right * ndc.x * 0.8 + up * ndc.y * 0.8);
                }
                else
                {
                    viewDir = normalize(input.positionWS - _WorldSpaceCameraPos);
                }

                // ── Pixelation: quantize view direction in spherical coords ──
                if (_PixelDensity > 0.0)
                {
                    float res = _PixelDensity * 0.5;
                    float theta = atan2(viewDir.z, viewDir.x);
                    float yCoord = viewDir.y;

                    theta = floor(theta * res + 0.5) / res;
                    yCoord = floor(yCoord * res + 0.5) / res;
                    yCoord = clamp(yCoord, -1.0, 1.0);

                    float r = sqrt(max(1.0 - yCoord * yCoord, 0.0));
                    viewDir = float3(r * cos(theta), yCoord, r * sin(theta));
                }

                float elevation = viewDir.y + _HorizonCurve;
                float2 horiz = viewDir.xz / max(length(viewDir.xz), 0.001);

                float t = _TimeOfDay;

                float nightFade = 1.0 - smoothstep(0.20, 0.30, t) + smoothstep(0.72, 0.82, t);
                nightFade = saturate(nightFade);

                float3 sunDir = sunDirection(t);
                float sunDot = dot(viewDir, sunDir);
                float3 sCol = sunColor(t);

                // ── 1. Sky gradient ──
                float skyBlend = saturate(elevation * 1.8 + 0.5);
                skyBlend = skyBlend * skyBlend;

                float3 zenith = zenithColor(t);
                float3 hCol = horizColor(t);
                float3 col = lerp(hCol, zenith, skyBlend);

                // ── 1b. Overcast darkening (desaturate + darken sky) ──
                if (_OvercastDarken > 0.01)
                {
                    float lum = dot(col, float3(0.299, 0.587, 0.114));
                    float3 grey = float3(lum, lum, lum);
                    col = lerp(col, grey * 0.6, _OvercastDarken * 0.7);
                }

                // ── 2. Sun / moon ──
                float disc = smoothstep(_SunSize, _SunSize + 0.008, sunDot);
                float glow = pow(saturate(sunDot), 6.0) * _SunGlow;
                col += sCol * (disc * 1.5 + glow);

                // ── 3. Stars ──
                float starMask = nightFade * step(0.05, elevation);
                if (starMask > 0.01)
                {
                    float2 starUV = horiz * 3.0 + float2(0.0, elevation * 5.0);
                    col += starField(starUV, _StarDensity) * starMask
                         * smoothstep(0.05, 0.20, elevation) * 0.7;
                }

                // ── 4. Clouds ──
                float cloudVisible = smoothstep(-0.10, 0.10, elevation);
                if (cloudVisible > 0.01)
                {
                    float2 cloudUV = viewDir.xz / (elevation + 0.35);
                    cloudUV = cloudUV * _CloudScale;
                    cloudUV.x += _Time.y * _CloudSpeed;
                    cloudUV.y += _Time.y * _CloudSpeed * 0.3;

                    float cn = fbm2(cloudUV); // fbm2 = blockier, less painterly (Y2K)
                    float thr = 0.50 - _CloudDensity * 0.35;
                    float cloudMask = smoothstep(thr, thr + _CloudSharpness, cn) * cloudVisible;

                    float3 cloudCol = lerp(float3(0.92, 0.90, 0.85),
                                           float3(0.06, 0.06, 0.10), nightFade);
                    cloudCol += sCol * pow(saturate(sunDot), 3.0) * 0.15 * (1.0 - nightFade);

                    col = lerp(col, cloudCol, cloudMask * 0.88);
                }

                // ── 5. Far mountains ──
                float mountainN = fbm2(horiz * _MountainScale); // simpler silhouettes
                float mountainLine = mountainN * _MountainHeight;
                float mountainMask = smoothstep(mountainLine + 0.008,
                                                mountainLine - 0.004, elevation);
                float3 mountainCol = lerp(
                    float3(0.12, 0.14, 0.22),
                    float3(0.06, 0.08, 0.14),
                    smoothstep(0.0, _MountainHeight, elevation));
                mountainCol = mountainCol * lerp(1.0, 0.25, nightFade);
                col = lerp(col, mountainCol, mountainMask);

                // ── 5b. Mountain snow caps ──
                if (_SnowCapIntensity > 0.01 && mountainMask > 0.01)
                {
                    float peakBlend = smoothstep(mountainLine * 0.5, mountainLine, elevation);
                    float snowNoise = noise2D(horiz * _MountainScale * 4.0 + float2(7.7, 3.1));
                    float snowMask = peakBlend * smoothstep(0.3, 0.6, snowNoise) * mountainMask;
                    float3 snowCol = float3(0.85, 0.88, 0.92) * lerp(1.0, 0.3, nightFade);
                    col = lerp(col, snowCol, snowMask * _SnowCapIntensity);
                }

                // ── 6. Near treeline ──
                float treeN = fbm2(horiz * _MountainScale * 3.5 + float2(17.3, 42.1));
                float treeLine = treeN * _TreelineHeight;
                float treeMask = smoothstep(treeLine + 0.004, treeLine - 0.002, elevation);
                float treeDetail = noise2D(horiz * _MountainScale * 20.0);
                treeMask = treeMask * smoothstep(0.3, 0.5, treeDetail + 0.3);
                float3 treeCol = float3(0.015, 0.035, 0.018) * lerp(1.0, 0.15, nightFade);
                col = lerp(col, treeCol, treeMask);

                // ── 7. Ground ──
                float groundBlend = smoothstep(0.0, -0.12, elevation);
                float3 groundCol = float3(0.025, 0.04, 0.025) * lerp(1.0, 0.15, nightFade);
                col = lerp(col, groundCol, groundBlend);

                // ── 8. Horizon fog ──
                float fogBand = exp(-abs(elevation) * 12.0) * _HorizonFog;
                float3 fogCol = lerp(hCol, sCol, 0.25) * lerp(1.0, 0.3, nightFade);
                col = lerp(col, fogCol, fogBand);

                // ── 9. Rain streaks (vertical) ──
                if (_RainIntensity > 0.01)
                {
                    float skyMask = smoothstep(-0.05, 0.05, elevation);

                    // Always use screen UV — rain falls straight down on screen
                    float2 screenUV = input.positionCS.xy / _ScreenParams.xy;

                    float t_rain = _Time.y * _RainSpeed;
                    float rain = 0.0;

                    // Three layers of vertical streaks at different scales/speeds.
                    // Each layer: hash a column, if selected draw a tall thin streak
                    // that scrolls downward. The streak is a narrow band in X and
                    // an elongated moving window in Y.
                    for (int layer = 0; layer < 4; layer++)
                    {
                        float scale = _RainScale * (1.0 + layer * 0.35);
                        float speed = 0.8 + layer * 0.25;
                        float seed = layer * 53.7;
                        float sparsity = 0.55 + layer * 0.05; // lower = more drops

                        // Column index — thin vertical columns
                        float colX = screenUV.x * scale;
                        float colID = floor(colX);
                        float colFrac = frac(colX);

                        // Per-column random: does this column have rain?
                        float colHash = hash21(float2(colID, seed));
                        if (colHash < sparsity) continue;

                        // Horizontal narrowness — streak is thin
                        float xCenter = frac(colHash * 7.3);
                        float xDist = abs(colFrac - xCenter);
                        float xMask = 1.0 - smoothstep(0.0, 0.04, xDist);

                        // Vertical scrolling — tall streak window
                        float scrollY = screenUV.y * scale * 3.0 + t_rain * speed + colHash * 100.0;
                        float streakPhase = frac(scrollY * 0.15);
                        // Streak length: visible for ~15% of the cycle
                        float yMask = smoothstep(0.0, 0.02, streakPhase)
                                    * (1.0 - smoothstep(0.12, 0.15, streakPhase));

                        rain += xMask * yMask;
                    }

                    rain = saturate(rain) * skyMask;
                    float3 rainCol = lerp(float3(0.7, 0.75, 0.85), float3(0.2, 0.22, 0.3), nightFade);
                    col = lerp(col, rainCol, rain * _RainIntensity * 0.6);
                }

                // ── 10. Snow flakes (two parallax layers) ──
                if (_SnowIntensity > 0.01)
                {
                    float skyMask = smoothstep(-0.1, 0.05, elevation);

                    // Layer 1 — near, larger
                    float2 snowUV1 = float2(horiz.x, elevation) * _SnowScale;
                    snowUV1.y -= _Time.y * _SnowSpeed;
                    snowUV1.x += sin(_Time.y * 0.3 + elevation * 4.0) * 0.8;
                    float2 snowCell1 = floor(snowUV1);
                    float2 snowF1 = frac(snowUV1) - 0.5;
                    float h1 = hash21(snowCell1);
                    float2 offset1 = float2(h1 - 0.5, frac(h1 * 17.3) - 0.5) * 0.6;
                    float d1 = length(snowF1 - offset1);
                    float flake1 = smoothstep(0.08, 0.03, d1) * step(0.75, h1);

                    // Layer 2 — far, smaller, slower
                    float2 snowUV2 = float2(horiz.x, elevation) * _SnowScale * 1.6;
                    snowUV2.y -= _Time.y * _SnowSpeed * 0.7;
                    snowUV2.x += sin(_Time.y * 0.2 + elevation * 3.0 + 2.0) * 0.6;
                    float2 snowCell2 = floor(snowUV2);
                    float2 snowF2 = frac(snowUV2) - 0.5;
                    float h2 = hash21(snowCell2 + float2(99.0, 77.0));
                    float2 offset2 = float2(h2 - 0.5, frac(h2 * 13.7) - 0.5) * 0.5;
                    float d2 = length(snowF2 - offset2);
                    float flake2 = smoothstep(0.06, 0.02, d2) * step(0.80, h2);

                    float snow = saturate(flake1 + flake2 * 0.6) * skyMask;
                    float3 snowCol = lerp(float3(0.9, 0.92, 0.95), float3(0.5, 0.52, 0.6), nightFade);
                    col = lerp(col, snowCol, snow * _SnowIntensity * 0.8);
                }

                // ── 11. Falling leaves ──
                if (_LeafIntensity > 0.01)
                {
                    float skyMask = smoothstep(-0.05, 0.08, elevation);

                    float2 leafUV = float2(horiz.x, elevation) * _LeafScale;
                    leafUV.y -= _Time.y * _LeafSpeed;
                    leafUV.x += _Time.y * _LeafSpeed * 0.5
                              + sin(_Time.y * 0.8 + elevation * 5.0) * 1.2;

                    float2 leafCell = floor(leafUV);
                    float2 leafF = frac(leafUV) - 0.5;
                    float lh = hash21(leafCell + float2(33.0, 55.0));

                    // Only sparse cells get a leaf
                    if (lh > 0.85)
                    {
                        float2 leafOff = float2(lh - 0.5, frac(lh * 23.7) - 0.5) * 0.4;
                        float2 lp = leafF - leafOff;

                        // Rotate the leaf
                        float angle = lh * 6.28 + _Time.y * (0.5 + lh);
                        float ca = cos(angle); float sa = sin(angle);
                        float2 rl = float2(lp.x * ca - lp.y * sa, lp.x * sa + lp.y * ca);

                        // Elliptical leaf shape
                        float leafShape = length(rl * float2(1.0, 2.5));
                        float leafMask = smoothstep(0.12, 0.06, leafShape) * skyMask;

                        // Color: orange / red / brown selection
                        float cSel = frac(lh * 30.0);
                        float3 leafCol = cSel < 0.33
                            ? float3(0.85, 0.45, 0.12)
                            : (cSel < 0.66
                                ? float3(0.75, 0.20, 0.10)
                                : float3(0.50, 0.30, 0.10));
                        leafCol *= lerp(1.0, 0.3, nightFade);

                        col = lerp(col, leafCol, leafMask * _LeafIntensity * 0.7);
                    }
                }

                col = saturate(col);

                // ── 12. Color quantization ──
                if (_ColorDepth > 0.0)
                {
                    col = floor(col * _ColorDepth + 0.5) / _ColorDepth;
                }

                return half4(col, 1.0);
            }

            ENDHLSL
        }
    }

    Fallback Off
}
