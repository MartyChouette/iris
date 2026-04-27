using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dims all renderers in the scene except specified exclusion roots during Phase 2.
/// Uses MaterialPropertyBlock to tint renderers dark without creating material instances.
/// Call DimEnvironment() to fade non-excluded objects to a dim color,
/// RestoreEnvironment() to fade them back.
/// </summary>
public class Phase2EnvironmentDim : MonoBehaviour
{
    public static Phase2EnvironmentDim Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Instance = null;

    [Header("Dim Settings")]
    [Tooltip("How dark non-excluded objects get (0 = black, 1 = no change).")]
    [SerializeField, Range(0f, 1f)] private float _dimAmount = 0.15f;

    [Tooltip("Seconds to fade to dim.")]
    [SerializeField] private float _fadeDuration = 0.8f;

    [Header("Exclusion Roots")]
    [Tooltip("Root transforms whose children should NOT be dimmed (drink cart, etc).")]
    [SerializeField] private Transform[] _excludeRoots;

    // Runtime state
    private readonly List<RendererState> _dimmedRenderers = new();
    private Coroutine _fadeCoroutine;
    private bool _isDimmed;

    private static readonly int s_baseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int s_colorId = Shader.PropertyToID("_Color");

    private struct RendererState
    {
        public Renderer renderer;
        public Color originalColor;
        public bool usesBaseColor;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Dim everything except the provided roots + the serialized exclude list.
    /// Pass additional runtime roots (Nema kitchen model, date kitchen model).
    /// </summary>
    public void DimEnvironment(params Transform[] additionalExclusions)
    {
        if (_isDimmed) return;
        _isDimmed = true;

        // Build exclusion set
        var excluded = new HashSet<Transform>();
        if (_excludeRoots != null)
            foreach (var r in _excludeRoots)
                if (r != null) AddHierarchy(excluded, r);
        if (additionalExclusions != null)
            foreach (var r in additionalExclusions)
                if (r != null) AddHierarchy(excluded, r);

        // Gather all renderers NOT in the exclusion set
        _dimmedRenderers.Clear();
        var allRenderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        for (int i = 0; i < allRenderers.Length; i++)
        {
            var rend = allRenderers[i];
            if (rend == null || !rend.gameObject.activeInHierarchy) continue;
            if (excluded.Contains(rend.transform)) continue;

            // Skip UI canvases, particles, sprites
            if (rend is CanvasRenderer || rend is SpriteRenderer || rend is ParticleSystemRenderer)
                continue;

            // Read current color
            bool usesBaseColor = rend.sharedMaterial != null && rend.sharedMaterial.HasProperty(s_baseColorId);
            int propId = usesBaseColor ? s_baseColorId : s_colorId;

            Color original;
            var mpb = new MaterialPropertyBlock();
            rend.GetPropertyBlock(mpb);
            if (rend.sharedMaterial != null && rend.sharedMaterial.HasProperty(propId))
                original = rend.sharedMaterial.GetColor(propId);
            else
                original = Color.white;

            _dimmedRenderers.Add(new RendererState
            {
                renderer = rend,
                originalColor = original,
                usesBaseColor = usesBaseColor
            });
        }

        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = StartCoroutine(FadeRoutine(_dimAmount, _fadeDuration));
    }

    /// <summary>Restore all dimmed renderers to their original colors.</summary>
    public void RestoreEnvironment()
    {
        if (!_isDimmed) return;
        _isDimmed = false;

        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = StartCoroutine(FadeRoutine(1f, _fadeDuration));
    }

    private IEnumerator FadeRoutine(float targetMul, float duration)
    {
        // Determine starting multiplier from current state
        float startMul = targetMul < 1f ? 1f : _dimAmount;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            float mul = Mathf.Lerp(startMul, targetMul, t);
            ApplyDim(mul);
            yield return null;
        }
        ApplyDim(targetMul);

        // Clean up MPBs when fully restored
        if (targetMul >= 1f)
        {
            for (int i = 0; i < _dimmedRenderers.Count; i++)
            {
                if (_dimmedRenderers[i].renderer != null)
                    _dimmedRenderers[i].renderer.SetPropertyBlock(null);
            }
            _dimmedRenderers.Clear();
        }

        _fadeCoroutine = null;
    }

    private void ApplyDim(float mul)
    {
        var mpb = new MaterialPropertyBlock();
        for (int i = 0; i < _dimmedRenderers.Count; i++)
        {
            var state = _dimmedRenderers[i];
            if (state.renderer == null) continue;

            Color dimmed = state.originalColor * mul;
            dimmed.a = state.originalColor.a; // preserve alpha

            state.renderer.GetPropertyBlock(mpb);
            mpb.SetColor(state.usesBaseColor ? s_baseColorId : s_colorId, dimmed);
            state.renderer.SetPropertyBlock(mpb);
        }
    }

    private static void AddHierarchy(HashSet<Transform> set, Transform root)
    {
        set.Add(root);
        for (int i = 0; i < root.childCount; i++)
            AddHierarchy(set, root.GetChild(i));
    }
}
