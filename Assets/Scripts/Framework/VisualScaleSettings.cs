using UnityEngine;

/// <summary>
/// Central tuning knobs for cursor size, particle size, and eye icon size.
/// Drop in Resources/ or assign directly. Supports optional zoom-scaling
/// so visuals shrink/grow with camera zoom to maintain world-space consistency.
/// </summary>
[CreateAssetMenu(menuName = "Iris/Visual Scale Settings")]
public class VisualScaleSettings : ScriptableObject
{
    [Header("Cursors")]
    [Tooltip("Multiplier on cursor texture size (1 = native resolution).")]
    [Range(0.25f, 4f)] public float cursorScale = 1f;
    [Tooltip("When true, cursors shrink when zoomed in and grow when zoomed out.")]
    public bool cursorScalesWithZoom = false;

    [Header("Particles — Hearts")]
    [Range(0.25f, 4f)] public float heartScale = 1f;
    public bool heartScalesWithZoom = false;

    [Header("Particles — Smoke")]
    [Range(0.25f, 4f)] public float smokeScale = 1f;
    public bool smokeScalesWithZoom = false;

    [Header("Eye Icons")]
    [Range(0.25f, 4f)] public float eyeScale = 1f;
    public bool eyeScalesWithZoom = false;

    [Header("Zoom Reference")]
    [Tooltip("Ortho size at which scale = 1x. Visuals shrink below this, grow above.")]
    public float referenceOrthoSize = 5f;

    /// <summary>Ratio of current zoom to reference. &lt;1 when zoomed in, &gt;1 when zoomed out.</summary>
    public float ZoomFactor
    {
        get
        {
            if (ApartmentManager.Instance == null || referenceOrthoSize <= 0f) return 1f;
            return ApartmentManager.Instance.CurrentOrthoSize / referenceOrthoSize;
        }
    }

    public float GetCursorScale() => cursorScale * (cursorScalesWithZoom ? ZoomFactor : 1f);
    public float GetHeartScale() => heartScale * (heartScalesWithZoom ? ZoomFactor : 1f);
    public float GetSmokeScale() => smokeScale * (smokeScalesWithZoom ? ZoomFactor : 1f);
    public float GetEyeScale() => eyeScale * (eyeScalesWithZoom ? ZoomFactor : 1f);

    // ── Singleton access ──
    private static VisualScaleSettings s_instance;
    private static bool s_loaded;

    public static VisualScaleSettings Instance
    {
        get
        {
            if (!s_loaded)
            {
                s_loaded = true;
                s_instance = Resources.Load<VisualScaleSettings>("VisualScaleSettings")
                          ?? Resources.Load<VisualScaleSettings>("Visual Scale Settings");
                if (s_instance == null)
                    s_instance = CreateInstance<VisualScaleSettings>();
            }
            return s_instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { s_instance = null; s_loaded = false; }
}
