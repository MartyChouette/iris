using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Volume parameter for <see cref="DitherPattern"/> enum.</summary>
[System.Serializable]
public sealed class DitherPatternParameter : VolumeParameter<DitherPattern>
{
    public DitherPatternParameter(DitherPattern value, bool overrideState = false)
        : base(value, overrideState) { }
}

/// <summary>
/// URP Volume override for PSX post-processing. Add to any Volume profile
/// to control resolution, color depth, dithering, and tilt-shift from the
/// standard post-processing UI.
///
/// When present and active, these override PSXRenderController's Inspector values.
/// </summary>
[System.Serializable, VolumeComponentMenu("Post-processing/PSX")]
public class PSXPostProcessVolume : VolumeComponent
{
    [Header("Resolution")]
    [Tooltip("Divide screen resolution by this value. Higher = chunkier pixels.")]
    public ClampedFloatParameter resolutionDivisor = new ClampedFloatParameter(3f, 1f, 6f);

    [Header("Vertex Snapping")]
    [Tooltip("Target resolution for vertex position snapping. Lower = more wobble.")]
    public ClampedFloatParameter vertexSnapResolution = new ClampedFloatParameter(160f, 16f, 640f);

    [Header("Affine Texture Mapping")]
    [Tooltip("0 = perspective-correct, 1 = fully affine (PSX-style warping).")]
    public ClampedFloatParameter affineIntensity = new ClampedFloatParameter(1f, 0f, 1f);

    [Header("Color Depth")]
    [Tooltip("Color levels per channel. Lower = heavier posterization.")]
    public ClampedFloatParameter colorDepth = new ClampedFloatParameter(32f, 4f, 256f);

    [Header("Dithering")]
    [Tooltip("Ordered dither strength. 0 = off, 1 = full.")]
    public ClampedFloatParameter ditherIntensity = new ClampedFloatParameter(0.5f, 0f, 1f);

    [Tooltip("Dither pattern for mid-shadows.")]
    public DitherPatternParameter finePattern = new DitherPatternParameter(DitherPattern.Bayer4x4);

    [Tooltip("Dither pattern for deep shadows.")]
    public DitherPatternParameter coarsePattern = new DitherPatternParameter(DitherPattern.Bayer2x2);

    [Tooltip("Luminance above which no dither appears.")]
    public ClampedFloatParameter shadowThreshold = new ClampedFloatParameter(0.5f, 0f, 1f);

    [Tooltip("Luminance below which the coarse pattern fully takes over.")]
    public ClampedFloatParameter deepShadowThreshold = new ClampedFloatParameter(0.15f, 0f, 1f);

    [Tooltip("Camera ortho size at which dither is 1:1 scale. 0 = no zoom scaling.")]
    public FloatParameter ditherZoomReference = new FloatParameter(5f);

    [Header("Shadow Dithering (Object Shader)")]
    [Tooltip("PSX-style dithered shadows on PSXLit materials. 0 = smooth, 1 = fully stippled.")]
    public ClampedFloatParameter shadowDitherIntensity = new ClampedFloatParameter(1f, 0f, 1f);

    [Header("Tilt-Shift")]
    [Tooltip("Blur amount at screen edges. 0 = off, 1 = full.")]
    public ClampedFloatParameter tiltShiftAmount = new ClampedFloatParameter(0f, 0f, 1f);

    [Tooltip("Vertical center of the focus band (0 = bottom, 1 = top).")]
    public ClampedFloatParameter tiltShiftCenter = new ClampedFloatParameter(0.5f, 0f, 1f);

    [Tooltip("Half-width of the sharp focus band in UV space.")]
    public ClampedFloatParameter tiltShiftWidth = new ClampedFloatParameter(0.15f, 0.01f, 0.5f);

    [Tooltip("Maximum blur radius in texels.")]
    public ClampedFloatParameter tiltShiftRadius = new ClampedFloatParameter(8f, 1f, 20f);

}
