using System.Collections.Generic;
using UnityEngine;

public class MoodMachine : MonoBehaviour
{
    public static MoodMachine Instance { get; private set; }

    // ──────────────────────────────────────────────────────────────
    // Configuration
    // ──────────────────────────────────────────────────────────────
    [Header("Profile")]
    [Tooltip("ScriptableObject defining how the scene looks at each mood value.")]
    [SerializeField] private MoodMachineProfile profile;

    [Header("References")]
    [Tooltip("Directional light to drive color, intensity, and angle.")]
    [SerializeField] private Light directionalLight;

    [Tooltip("Optional rain particle system. Emission rate driven by profile.")]
    [SerializeField] private ParticleSystem rainParticles;

    [Tooltip("God ray quad transforms. Y-rotation driven by profile.")]
    [SerializeField] private Transform[] godRayQuads;

    [Header("Audio")]
    [Tooltip("Looping room tone clip. Volume driven by profile.ambienceVolume curve.")]
    [SerializeField] private AudioClip _ambienceClip;

    [Tooltip("Looping rain/storm clip. Volume driven by profile.weatherVolume curve.")]
    [SerializeField] private AudioClip _weatherClip;

    [Header("Settings")]
    [Tooltip("Speed of mood lerp (units per second). 0.5 ≈ 2 seconds for full traverse.")]
    [SerializeField] private float lerpSpeed = 0.5f;

    [Tooltip("Speed of profile crossfade (0→1 per second).")]
    [SerializeField] private float profileBlendSpeed = 0.8f;

    // ──────────────────────────────────────────────────────────────
    // Public read-only
    // ──────────────────────────────────────────────────────────────
    public float Mood => _currentMood;
    public IReadOnlyDictionary<string, float> Sources => _sources;

    // ──────────────────────────────────────────────────────────────
    // Runtime state
    // ──────────────────────────────────────────────────────────────
    private readonly Dictionary<string, float> _sources = new Dictionary<string, float>();
    private float _currentMood;

    // Profile crossfade
    private MoodMachineProfile _overrideProfile;
    private float _profileBlend; // 0 = base, 1 = override

    // Cache god ray base rotations
    private Quaternion[] _godRayBaseRotations;

    private static readonly int ColorFilterID = Shader.PropertyToID("_MoodColorFilter");

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[MoodMachine] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Cache base rotations for god ray quads
        if (godRayQuads != null && godRayQuads.Length > 0)
        {
            _godRayBaseRotations = new Quaternion[godRayQuads.Length];
            for (int i = 0; i < godRayQuads.Length; i++)
            {
                if (godRayQuads[i] != null)
                    _godRayBaseRotations[i] = godRayQuads[i].localRotation;
            }
        }
    }

    private void Start()
    {
        if (AudioManager.Instance != null)
        {
            if (_ambienceClip != null)
                AudioManager.Instance.PlayAmbience(_ambienceClip, 0f);
            if (_weatherClip != null)
                AudioManager.Instance.PlayWeather(_weatherClip, 0f);
        }

        // Initialize color filter to white (no tint)
        Shader.SetGlobalColor(ColorFilterID, Color.white);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            AudioManager.Instance?.StopAmbience();
            AudioManager.Instance?.StopWeather();
            Shader.SetGlobalColor(ColorFilterID, Color.white);
            Instance = null;
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Source API
    // ──────────────────────────────────────────────────────────────

    public void SetSource(string key, float value)
    {
        _sources[key] = Mathf.Clamp01(value);
    }

    public void RemoveSource(string key)
    {
        _sources.Remove(key);
    }

    // ──────────────────────────────────────────────────────────────
    // Profile Override API (for perfume atmospheres)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Crossfade to an override profile (e.g. from a perfume).
    /// Pass null to fade back to the base profile.
    /// </summary>
    public void SetOverrideProfile(MoodMachineProfile overrideProfile)
    {
        if (overrideProfile != null)
        {
            _overrideProfile = overrideProfile;
            // blend will ramp toward 1 in Update
        }
        else
        {
            // blend will ramp toward 0, then we clear the reference
        }
    }

    /// <summary>Clear the override and fade back to base profile.</summary>
    public void ClearOverrideProfile()
    {
        SetOverrideProfile(null);
    }

    // ──────────────────────────────────────────────────────────────
    // Update
    // ──────────────────────────────────────────────────────────────

    private void Update()
    {
        float target = ComputeTarget();
        _currentMood = Mathf.MoveTowards(_currentMood, target, lerpSpeed * Time.deltaTime);

        // Profile crossfade
        float blendTarget = _overrideProfile != null ? 1f : 0f;
        _profileBlend = Mathf.MoveTowards(_profileBlend, blendTarget, profileBlendSpeed * Time.deltaTime);

        // Clear reference once fully faded out
        if (_profileBlend <= 0f && _overrideProfile != null && blendTarget == 0f)
            _overrideProfile = null;

        ApplyMood(_currentMood);
    }

    private float ComputeTarget()
    {
        if (_sources.Count == 0) return 0f;

        float sum = 0f;
        foreach (var kv in _sources)
            sum += kv.Value;

        return Mathf.Clamp01(sum / _sources.Count);
    }

    private void ApplyMood(float t)
    {
        if (profile == null) return;

        // Evaluate base profile
        Color baseLightCol = profile.lightColor.Evaluate(t);
        float baseLightInt = profile.lightIntensity.Evaluate(t);
        float baseLightAngle = profile.lightAngleX.Evaluate(t);
        Color baseAmbient = profile.ambientColor.Evaluate(t);
        Color baseFogCol = profile.fogColor.Evaluate(t);
        float baseFogDens = profile.fogDensity.Evaluate(t);
        float baseRainRate = profile.rainRate.Evaluate(t);
        float baseAmbienceVol = profile.ambienceVolume.Evaluate(t);
        float baseWeatherVol = profile.weatherVolume.Evaluate(t);
        Color baseColorFilter = profile.colorFilter != null ? profile.colorFilter.Evaluate(t) : Color.white;
        float baseGodRayRot = profile.godRayRotation.Evaluate(t);

        // Blend with override profile if active
        if (_profileBlend > 0f && _overrideProfile != null)
        {
            float b = _profileBlend;

            baseLightCol = Color.Lerp(baseLightCol, _overrideProfile.lightColor.Evaluate(t), b);
            baseLightInt = Mathf.Lerp(baseLightInt, _overrideProfile.lightIntensity.Evaluate(t), b);
            baseLightAngle = Mathf.Lerp(baseLightAngle, _overrideProfile.lightAngleX.Evaluate(t), b);
            baseAmbient = Color.Lerp(baseAmbient, _overrideProfile.ambientColor.Evaluate(t), b);
            baseFogCol = Color.Lerp(baseFogCol, _overrideProfile.fogColor.Evaluate(t), b);
            baseFogDens = Mathf.Lerp(baseFogDens, _overrideProfile.fogDensity.Evaluate(t), b);
            baseRainRate = Mathf.Lerp(baseRainRate, _overrideProfile.rainRate.Evaluate(t), b);
            baseAmbienceVol = Mathf.Lerp(baseAmbienceVol, _overrideProfile.ambienceVolume.Evaluate(t), b);
            baseWeatherVol = Mathf.Lerp(baseWeatherVol, _overrideProfile.weatherVolume.Evaluate(t), b);

            Color overrideFilter = _overrideProfile.colorFilter != null
                ? _overrideProfile.colorFilter.Evaluate(t)
                : Color.white;
            baseColorFilter = Color.Lerp(baseColorFilter, overrideFilter, b);

            baseGodRayRot = Mathf.Lerp(baseGodRayRot, _overrideProfile.godRayRotation.Evaluate(t), b);
        }

        // Apply directional light
        if (directionalLight != null)
        {
            directionalLight.color = baseLightCol;
            directionalLight.intensity = baseLightInt;

            Vector3 euler = directionalLight.transform.eulerAngles;
            euler.x = baseLightAngle;
            directionalLight.transform.eulerAngles = euler;
        }

        // Ambient + Fog
        RenderSettings.ambientLight = baseAmbient;
        RenderSettings.fogColor = baseFogCol;
        RenderSettings.fogDensity = baseFogDens;

        // Rain particles
        if (rainParticles != null)
        {
            var emission = rainParticles.emission;
            emission.rateOverTime = baseRainRate;
        }

        // Audio
        if (AudioManager.Instance != null)
        {
            if (AudioManager.Instance.ambienceSource != null && AudioManager.Instance.ambienceSource.isPlaying)
                AudioManager.Instance.ambienceSource.volume = baseAmbienceVol;
            if (AudioManager.Instance.weatherSource != null && AudioManager.Instance.weatherSource.isPlaying)
                AudioManager.Instance.weatherSource.volume = baseWeatherVol;
        }

        // Color filter (global shader property)
        Shader.SetGlobalColor(ColorFilterID, baseColorFilter);

        // God ray quads
        if (godRayQuads != null && _godRayBaseRotations != null)
        {
            for (int i = 0; i < godRayQuads.Length; i++)
            {
                if (godRayQuads[i] == null) continue;
                godRayQuads[i].localRotation = _godRayBaseRotations[i]
                    * Quaternion.Euler(0f, baseGodRayRot, 0f);
            }
        }
    }
}
