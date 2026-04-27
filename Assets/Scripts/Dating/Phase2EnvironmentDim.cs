using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hides all renderers in the scene except specified exclusion roots during Phase 2.
/// Non-excluded renderers are disabled instantly, then re-enabled on restore.
/// Call HideEnvironment() to hide, RestoreEnvironment() to show again.
/// </summary>
public class Phase2EnvironmentDim : MonoBehaviour
{
    public static Phase2EnvironmentDim Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Instance = null;

    [Header("Exclusion Roots")]
    [Tooltip("Root transforms whose children should stay visible (drink cart, etc).")]
    [SerializeField] private Transform[] _excludeRoots;

    private readonly List<Renderer> _hiddenRenderers = new();
    private bool _isHidden;

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
    /// Hide everything except the provided roots + the serialized exclude list.
    /// Pass additional runtime roots (Nema kitchen model, date kitchen model).
    /// </summary>
    public void HideEnvironment(params Transform[] additionalExclusions)
    {
        if (_isHidden) return;
        _isHidden = true;

        // Build exclusion set
        var excluded = new HashSet<Transform>();
        if (_excludeRoots != null)
            foreach (var r in _excludeRoots)
                if (r != null) AddHierarchy(excluded, r);
        if (additionalExclusions != null)
            foreach (var r in additionalExclusions)
                if (r != null) AddHierarchy(excluded, r);

        // Disable all renderers NOT in the exclusion set
        _hiddenRenderers.Clear();
        var allRenderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        for (int i = 0; i < allRenderers.Length; i++)
        {
            var rend = allRenderers[i];
            if (rend == null) continue;
            if (!rend.enabled) continue;
            if (excluded.Contains(rend.transform)) continue;

            // Skip UI, particles, sprites
            if (rend is CanvasRenderer || rend is SpriteRenderer || rend is ParticleSystemRenderer)
                continue;

            rend.enabled = false;
            _hiddenRenderers.Add(rend);
        }

        Debug.Log($"[Phase2EnvironmentDim] Hidden {_hiddenRenderers.Count} renderers.");
    }

    /// <summary>Re-enable all hidden renderers.</summary>
    public void RestoreEnvironment()
    {
        if (!_isHidden) return;
        _isHidden = false;

        for (int i = 0; i < _hiddenRenderers.Count; i++)
        {
            if (_hiddenRenderers[i] != null)
                _hiddenRenderers[i].enabled = true;
        }

        Debug.Log($"[Phase2EnvironmentDim] Restored {_hiddenRenderers.Count} renderers.");
        _hiddenRenderers.Clear();
    }

    private static void AddHierarchy(HashSet<Transform> set, Transform root)
    {
        set.Add(root);
        for (int i = 0; i < root.childCount; i++)
            AddHierarchy(set, root.GetChild(i));
    }
}
