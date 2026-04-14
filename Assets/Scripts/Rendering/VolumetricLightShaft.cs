using UnityEngine;

/// <summary>
/// Attach to a quad/plane at a window to create a PS2-style fake volumetric light shaft.
/// Color and intensity automatically follow the directional light (time-of-day).
/// Place the quad so UV Y=0 is at the window and Y=1 is the far end of the beam.
/// Easily repositioned — just move the GameObject in the scene.
/// </summary>
public class VolumetricLightShaft : MonoBehaviour
{
    [Header("Appearance")]
    [Tooltip("Base shaft color (will be tinted by directional light).")]
    [SerializeField] private Color _baseColor = new Color(1f, 0.97f, 0.92f, 0.12f);

    [Tooltip("Intensity multiplier.")]
    [Range(0f, 3f)]
    [SerializeField] private float _intensity = 1f;

    [Tooltip("How far the shaft reaches before fully fading (0-1 UV space).")]
    [Range(0.2f, 1f)]
    [SerializeField] private float _fadeEnd = 0.8f;

    [Header("Noise")]
    [Tooltip("Animated noise for dusty volumetric feel.")]
    [Range(0f, 1f)]
    [SerializeField] private float _noiseAmount = 0.3f;

    [SerializeField] private float _noiseScale = 3f;
    [SerializeField] private float _noiseSpeed = 0.3f;

    [Header("Time Gate")]
    [Tooltip("Sun shaft visible when directional light intensity is above this.")]
    [Range(0f, 1f)]
    [SerializeField] private float _minLightIntensity = 0.3f;

    [Header("Moonlight")]
    [Tooltip("Enable a cool blue moonbeam at night instead of fading to nothing.")]
    [SerializeField] private bool _enableMoonlight = true;

    [Tooltip("Moonlight color — cool blue-silver.")]
    [SerializeField] private Color _moonColor = new Color(0.35f, 0.45f, 0.7f, 0.06f);

    [Tooltip("Sun intensity below which moonlight starts fading in.")]
    [Range(0f, 1f)]
    [SerializeField] private float _moonFadeStart = 0.25f;

    private Material _mat;
    private Renderer _renderer;

    private static readonly int ShaftColorID = Shader.PropertyToID("_ShaftColor");
    private static readonly int FadeEndID = Shader.PropertyToID("_FadeEnd");
    private static readonly int NoiseAmountID = Shader.PropertyToID("_NoiseAmount");
    private static readonly int NoiseScaleID = Shader.PropertyToID("_NoiseScale");
    private static readonly int NoiseSpeedID = Shader.PropertyToID("_NoiseSpeed");

    private void Start()
    {
        _renderer = GetComponent<Renderer>();
        if (_renderer == null)
        {
            Debug.LogWarning("[VolumetricLightShaft] No Renderer found on " + name);
            return;
        }

        var shader = Shader.Find("Iris/VolumetricShaft");
        if (shader == null)
        {
            Debug.LogWarning("[VolumetricLightShaft] Iris/VolumetricShaft shader not found.");
            return;
        }

        _mat = new Material(shader);
        _renderer.material = _mat;
        ApplyStaticProperties();
    }

    private void LateUpdate()
    {
        if (_mat == null) return;

        // Read directional light color + intensity
        var sun = RenderSettings.sun;
        Color lightCol = Color.white;
        float lightInt = 1f;

        if (sun != null)
        {
            lightCol = sun.color;
            lightInt = sun.intensity;
        }

        // Sun shaft: fade out when light dims
        float sunFade = Mathf.InverseLerp(_minLightIntensity, _minLightIntensity + 0.3f, lightInt);
        Color sunShaft = _baseColor * lightCol;
        sunShaft.a = _baseColor.a * _intensity * sunFade;

        if (_enableMoonlight)
        {
            // Moon shaft: fade IN when sun fades out
            float moonFade = Mathf.InverseLerp(_moonFadeStart + 0.15f, _moonFadeStart, lightInt);
            Color moonShaft = _moonColor;
            moonShaft.a = _moonColor.a * _intensity * moonFade;

            // Crossfade — whichever is stronger wins
            Color finalShaft = sunFade > moonFade ? sunShaft : moonShaft;
            _mat.SetColor(ShaftColorID, finalShaft);
        }
        else
        {
            _mat.SetColor(ShaftColorID, sunShaft);
        }
    }

    private void ApplyStaticProperties()
    {
        if (_mat == null) return;
        _mat.SetFloat(FadeEndID, _fadeEnd);
        _mat.SetFloat(NoiseAmountID, _noiseAmount);
        _mat.SetFloat(NoiseScaleID, _noiseScale);
        _mat.SetFloat(NoiseSpeedID, _noiseSpeed);
    }

    private void OnValidate()
    {
        ApplyStaticProperties();
    }

    private void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
    }

    /// <summary>Runtime intensity control.</summary>
    public float Intensity
    {
        get => _intensity;
        set => _intensity = value;
    }
}
