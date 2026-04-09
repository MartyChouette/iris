using UnityEngine;

/// <summary>
/// Tunable settings for context cursor fade transitions.
/// Assign to GlobalCursorManager or drop in Resources/CursorFadeSettings.
/// </summary>
[CreateAssetMenu(menuName = "Iris/Cursor Fade Settings")]
public class CursorFadeSettings : ScriptableObject
{
    [Header("Fade In (cursor appears)")]
    [Tooltip("Duration in seconds for the cursor to fade in.")]
    public float fadeInDuration = 0.14f;

    [Tooltip("Easing curve for fade-in. X = normalized time, Y = alpha (0→1).")]
    public AnimationCurve fadeInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Fade Out (cursor disappears)")]
    [Tooltip("Duration in seconds for the cursor to fade out.")]
    public float fadeOutDuration = 0.20f;

    [Tooltip("Easing curve for fade-out. X = normalized time, Y = blend factor (0→1).")]
    public AnimationCurve fadeOutCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Hover Fade (sustained hover → half transparent)")]
    [Tooltip("Seconds of hovering before the cursor starts fading to half.")]
    public float hoverDelay = 2f;

    [Tooltip("Duration of the hover fade transition.")]
    public float hoverFadeDuration = 0.33f;

    [Tooltip("Target alpha when hover-faded (0-1).")]
    [Range(0f, 1f)]
    public float hoverFadedAlpha = 0.45f;

    [Tooltip("Easing curve for hover fade. X = normalized time, Y = alpha blend toward hoverFadedAlpha.")]
    public AnimationCurve hoverFadeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
}
