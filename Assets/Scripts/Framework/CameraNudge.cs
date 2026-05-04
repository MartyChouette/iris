using UnityEngine;

/// <summary>
/// Lightweight static camera shake utility.
/// Applies a brief positional nudge (XY offset, no rotation) to the main camera.
/// Consumers apply the offset via GetAndDecayOffset() each frame.
///
/// Implements: juicy feedback spec — camera nudge on placement and drawer events.
/// </summary>
public static class CameraNudge
{
    private static Vector3 _offset = Vector3.zero;

    /// <summary>
    /// Trigger a positional nudge. Intensity 0–1 scales the maximum offset.
    /// At intensity 1 the peak offset is approximately 0.02 world units,
    /// which maps to roughly 2 pixels at a typical ortho size.
    /// Respects AccessibilitySettings.ReduceMotion — no-ops when motion is reduced.
    /// </summary>
    /// <param name="intensity">Shake strength in 0–1 range. Default is a small nudge.</param>
    public static void Nudge(float intensity = 0.5f)
    {
        if (AccessibilitySettings.ReduceMotion) return;

        intensity = Mathf.Clamp01(intensity);

        // Max offset in world units. At ortho size ~5 and 1080p this is ~2 pixels.
        const float maxOffset = 0.02f;

        float scale = intensity * maxOffset;
        float angle = Random.Range(0f, Mathf.PI * 2f);
        _offset = new Vector3(
            Mathf.Cos(angle) * scale,
            Mathf.Sin(angle) * scale,
            0f
        );
    }

    /// <summary>
    /// Returns the current nudge offset and decays it toward zero.
    /// Call once per frame from wherever the camera position is written.
    /// Decay multiplies by 0.85 per call — falls off over 3–4 frames.
    /// </summary>
    /// <param name="deltaTime">Unused; accepted for caller readability and future frame-rate scaling.</param>
    public static Vector3 GetAndDecayOffset(float deltaTime)
    {
        Vector3 current = _offset;
        _offset *= 0.85f;

        // Clamp to zero below float noise floor to avoid perpetual micro-drift
        if (_offset.sqrMagnitude < 1e-10f)
            _offset = Vector3.zero;

        return current;
    }
}
