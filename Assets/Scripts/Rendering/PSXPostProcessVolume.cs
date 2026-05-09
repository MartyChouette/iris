using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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

    [Tooltip("0 = uniform dither everywhere, 1 = dither only in dark areas (PS1 stipple).")]
    public ClampedFloatParameter ditherShadowBias = new ClampedFloatParameter(0.7f, 0f, 1f);

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
