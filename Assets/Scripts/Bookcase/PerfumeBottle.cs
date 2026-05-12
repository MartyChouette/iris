using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A perfume bottle on the bookcase shelf. Single click to spray —
/// particle burst layers with staggered timing, mood set, bottle stays on shelf.
/// </summary>
public class PerfumeBottle : MonoBehaviour
{
    private static readonly List<PerfumeBottle> s_all = new();
    public static IReadOnlyList<PerfumeBottle> All => s_all;

    [Header("Definition")]
    [Tooltip("ScriptableObject defining this perfume.")]
    [SerializeField] private PerfumeDefinition definition;

    [Header("Spray Layers")]
    [Tooltip("Each layer fires its particle system after its delay. Add layers with the + button.")]
    [SerializeField] private List<SprayLayer> sprayLayers = new();

    /// <summary>Fired when any perfume is sprayed.</summary>
    public static event System.Action OnPerfumeSprayed;

    /// <summary>The most recently sprayed perfume definition (null if none sprayed this session).</summary>
    public static PerfumeDefinition LastSprayed { get; private set; }

    public PerfumeDefinition Definition => definition;
    public bool SprayComplete { get; private set; }

    private Vector3 _shelfPosition;
    private Material _instanceMaterial;
    private Color _baseColor;
    private bool _isHovered;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => LastSprayed = null;

    /// <summary>Clear the last sprayed perfume (call on date end).</summary>
    public static void ClearLastSprayed() => LastSprayed = null;

    private void OnEnable() => s_all.Add(this);
    private void OnDisable() => s_all.Remove(this);

    private void Awake()
    {
        _shelfPosition = transform.position;

        var rend = GetComponent<Renderer>();
        if (rend != null)
        {
            _instanceMaterial = rend.material;
            _baseColor = _instanceMaterial.color;
        }

        for (int i = 0; i < sprayLayers.Count; i++)
        {
            if (sprayLayers[i].particles != null)
                sprayLayers[i].particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    public void SetDefinition(PerfumeDefinition def) => definition = def;

    public void OnHoverEnter()
    {
        if (_isHovered) return;
        _isHovered = true;

        if (_instanceMaterial != null)
            _instanceMaterial.color = _baseColor * 1.2f;

        transform.position = _shelfPosition - transform.forward * 0.02f;
    }

    public void OnHoverExit()
    {
        if (!_isHovered) return;
        _isHovered = false;

        if (_instanceMaterial != null)
            _instanceMaterial.color = _baseColor;

        transform.position = _shelfPosition;
    }

    /// <summary>
    /// One-click spray: burst particles across all layers with staggered timing,
    /// set mood. Can be sprayed again to re-select this perfume.
    /// </summary>
    public void SprayOnce()
    {
        for (int i = 0; i < s_all.Count; i++)
        {
            if (s_all[i] == this) continue;
            s_all[i].Deactivate();
        }

        StartCoroutine(FireSprayLayers());

        if (definition != null && definition.spraySFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(definition.spraySFX);

        if (definition != null && MoodMachine.Instance != null)
        {
            MoodMachine.Instance.SetSource("Perfume", definition.moodValue);
            MoodMachine.Instance.SetOverrideProfile(definition.atmosphereProfile);
        }

        var reactable = GetComponent<ReactableTag>();
        if (reactable != null) reactable.IsActive = true;

        SprayComplete = true;
        LastSprayed = definition;
        OnPerfumeSprayed?.Invoke();
    }

    private IEnumerator FireSprayLayers()
    {
        if (sprayLayers.Count == 0) yield break;

        var sorted = new List<SprayLayer>(sprayLayers);
        sorted.Sort((a, b) => a.delay.CompareTo(b.delay));

        float elapsed = 0f;
        for (int i = 0; i < sorted.Count; i++)
        {
            var layer = sorted[i];
            if (layer.particles == null) continue;

            float wait = layer.delay - elapsed;
            if (wait > 0f)
            {
                yield return new WaitForSeconds(wait);
                elapsed += wait;
            }

            if (layer.duration <= 0f)
            {
                // Instant burst
                layer.particles.Emit(layer.burstCount);
            }
            else
            {
                // Sustained emission with fade in/out
                StartCoroutine(SustainedEmission(layer));
            }
        }
    }

    private IEnumerator SustainedEmission(SprayLayer layer)
    {
        var ps = layer.particles;
        var emission = ps.emission;
        emission.enabled = true;

        float peak = layer.emissionRate;
        float fadeIn = layer.fadeIn;
        float sustain = layer.duration - fadeIn - layer.fadeOut;
        if (sustain < 0f) sustain = 0f;
        float fadeOut = layer.fadeOut;

        // Fade in
        if (fadeIn > 0f)
        {
            ps.Play();
            float t = 0f;
            while (t < fadeIn)
            {
                t += Time.deltaTime;
                emission.rateOverTime = Mathf.Lerp(0f, peak, t / fadeIn);
                yield return null;
            }
        }
        else
        {
            emission.rateOverTime = peak;
            ps.Play();
        }

        // Sustain
        emission.rateOverTime = peak;
        if (sustain > 0f)
            yield return new WaitForSeconds(sustain);

        // Fade out
        if (fadeOut > 0f)
        {
            float t = 0f;
            while (t < fadeOut)
            {
                t += Time.deltaTime;
                emission.rateOverTime = Mathf.Lerp(peak, 0f, t / fadeOut);
                yield return null;
            }
        }

        emission.rateOverTime = 0f;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    /// <summary>Deactivate this perfume (called when a different perfume is sprayed).</summary>
    public void Deactivate()
    {
        SprayComplete = false;

        var reactable = GetComponent<ReactableTag>();
        if (reactable != null) reactable.IsActive = false;

        if (MoodMachine.Instance != null)
            MoodMachine.Instance.ClearOverrideProfile();
    }

    private void OnDestroy()
    {
        if (_instanceMaterial != null)
            Destroy(_instanceMaterial);
    }

    [System.Serializable]
    public class SprayLayer
    {
        [Tooltip("Particle system for this layer.")]
        public ParticleSystem particles;

        [Tooltip("Delay in seconds before this layer fires (0 = immediate).")]
        public float delay;

        [Header("Burst Mode (duration = 0)")]
        [Tooltip("Number of particles to emit in an instant burst.")]
        public int burstCount = 30;

        [Header("Sustained Mode (duration > 0)")]
        [Tooltip("Total duration in seconds. Set to 0 for instant burst mode.")]
        public float duration;

        [Tooltip("Peak emission rate (particles/sec) during sustained mode.")]
        public float emissionRate = 20f;

        [Tooltip("Seconds to ramp emission from 0 to peak.")]
        public float fadeIn;

        [Tooltip("Seconds to ramp emission from peak to 0.")]
        public float fadeOut;
    }
}
