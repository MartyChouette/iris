using UnityEngine;

/// <summary>
/// Drives scene lighting based on GameClock hour. Makes the apartment
/// genuinely dark in the evening so the player wants to turn on lights.
///
/// Controls:
///   - Directional light (sun) intensity + color
///   - RenderSettings ambient color
///   - AtmosphereController post-exposure override
///
/// Morning = bright warm light. Afternoon = golden hour. Evening = dim
/// orange. Night = near-dark blue. Interior lights (via LightSwitch)
/// become the only real illumination after sunset.
///
/// Auto-spawns as a scene singleton. Listens to GameClock.OnHourChanged.
/// </summary>
public class DayNightLighting : MonoBehaviour
{
    public static DayNightLighting Instance { get; private set; }

    [Header("Sun Light")]
    [Tooltip("Directional light acting as the sun. Auto-finds RenderSettings.sun if null.")]
    [SerializeField] private Light _sunLight;

    [Header("Sun Intensity Curve")]
    [Tooltip("Maps game hour (0-30) to sun intensity. 0=midnight dark, 1=full daylight.")]
    [SerializeField] private AnimationCurve _sunIntensityCurve = new AnimationCurve(
        new Keyframe(0f, 0.02f),   // midnight — nearly off
        new Keyframe(6f, 0.05f),   // pre-dawn
        new Keyframe(7f, 0.4f),    // sunrise
        new Keyframe(9f, 0.9f),    // morning
        new Keyframe(12f, 1.0f),   // noon — full
        new Keyframe(16f, 0.9f),   // afternoon
        new Keyframe(18f, 0.6f),   // golden hour
        new Keyframe(19.5f, 0.25f),// sunset
        new Keyframe(21f, 0.06f),  // dusk — almost dark
        new Keyframe(23f, 0.02f),  // night
        new Keyframe(26f, 0.02f),  // late night (past midnight)
        new Keyframe(30f, 0.02f)   // safety
    );

    [Header("Sun Color (gradient over 24h)")]
    [Tooltip("Hour 0=midnight blue, 7=dawn orange, 12=white, 18=golden, 21=deep orange, 23=blue")]
    [SerializeField] private Gradient _sunColorGradient;

    [Header("Ambient Light")]
    [Tooltip("Maps game hour to ambient intensity multiplier.")]
    [SerializeField] private AnimationCurve _ambientCurve = new AnimationCurve(
        new Keyframe(0f, 0.08f),
        new Keyframe(7f, 0.3f),
        new Keyframe(12f, 0.5f),
        new Keyframe(18f, 0.35f),
        new Keyframe(20f, 0.12f),
        new Keyframe(23f, 0.06f),
        new Keyframe(26f, 0.06f)
    );

    [Tooltip("Base ambient color at full brightness (noon).")]
    [SerializeField] private Color _ambientDayColor = new Color(0.45f, 0.48f, 0.55f);

    [Tooltip("Ambient color at night (deep blue).")]
    [SerializeField] private Color _ambientNightColor = new Color(0.06f, 0.07f, 0.12f);

    [Header("Atmosphere Override")]
    [Tooltip("Post-exposure at noon (bright).")]
    [SerializeField] private float _noonExposure = 0.4f;

    [Tooltip("Post-exposure at night (dark).")]
    [SerializeField] private float _nightExposure = -0.6f;

    private float _baseSunIntensity;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Default sun color gradient if not set
        if (_sunColorGradient == null || _sunColorGradient.colorKeys.Length < 2)
        {
            _sunColorGradient = new Gradient();
            _sunColorGradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(0.15f, 0.18f, 0.35f), 0f),     // midnight blue
                    new GradientColorKey(new Color(0.9f, 0.5f, 0.25f), 0.29f),    // 7h dawn orange
                    new GradientColorKey(new Color(1f, 0.95f, 0.88f), 0.38f),     // 9h morning warm
                    new GradientColorKey(new Color(1f, 0.98f, 0.95f), 0.5f),      // noon white
                    new GradientColorKey(new Color(1f, 0.85f, 0.6f), 0.71f),      // 17h afternoon gold
                    new GradientColorKey(new Color(1f, 0.55f, 0.2f), 0.79f),      // 19h sunset orange
                    new GradientColorKey(new Color(0.6f, 0.3f, 0.2f), 0.85f),     // 20.5h deep orange
                    new GradientColorKey(new Color(0.15f, 0.18f, 0.35f), 0.96f),  // 23h night blue
                },
                new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
            );
        }

        if (_sunLight == null)
            _sunLight = RenderSettings.sun;
        if (_sunLight != null)
            _baseSunIntensity = _sunLight.intensity;
    }

    private void OnEnable()
    {
        if (GameClock.Instance != null)
            GameClock.Instance.OnHourChanged.AddListener(OnHourChanged);
    }

    private void OnDisable()
    {
        if (GameClock.Instance != null)
            GameClock.Instance.OnHourChanged.RemoveListener(OnHourChanged);
    }

    private void Start()
    {
        // Subscribe after GameClock Awake
        if (GameClock.Instance != null)
        {
            GameClock.Instance.OnHourChanged.AddListener(OnHourChanged);
            // Apply immediately for current hour
            OnHourChanged(GameClock.Instance.CurrentHour);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnHourChanged(float hour)
    {
        float displayHour = Mathf.Repeat(hour, 24f);
        float t24 = displayHour / 24f; // 0-1 normalized for gradient

        // ── Sun intensity ──
        float sunIntensity = _sunIntensityCurve.Evaluate(hour);
        if (_sunLight != null)
        {
            _sunLight.intensity = _baseSunIntensity * sunIntensity;
            _sunLight.color = _sunColorGradient.Evaluate(t24);
        }

        // ── Ambient ──
        float ambientMul = _ambientCurve.Evaluate(hour);
        Color ambient = Color.Lerp(_ambientNightColor, _ambientDayColor, ambientMul);
        RenderSettings.ambientLight = ambient;
        RenderSettings.ambientSkyColor = ambient;

        // ── Atmosphere post-exposure ──
        if (AtmosphereController.Instance != null)
        {
            float exposure = Mathf.Lerp(_noonExposure, _nightExposure, 1f - sunIntensity);
            AtmosphereController.Instance.PostExposure = exposure;
        }
    }
}
