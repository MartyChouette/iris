using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Scene-scoped singleton that exposes all PSX rendering parameters.
/// Sets global shader properties for PSXLit and drives PSXPostProcessFeature settings.
/// Press F2 to toggle the effect on/off for A/B comparison.
/// </summary>
public class PSXRenderController : MonoBehaviour
{
    public static PSXRenderController Instance { get; private set; }

    // ──────────────────────────────────────────────────────────────
    // Object Shader Settings (global shader properties for PSXLit)
    // ──────────────────────────────────────────────────────────────
    [Header("Vertex Snapping")]
    [Tooltip("Target resolution for vertex position snapping. Lower = more wobble.")]
    [SerializeField] private Vector2 _vertexSnapResolution = new Vector2(160, 120);

    [Header("Affine Texture Mapping")]
    [Tooltip("0 = perspective-correct, 1 = fully affine (PSX-style warping).")]
    [Range(0f, 1f)]
    [SerializeField] private float _affineIntensity = 1f;

    // ──────────────────────────────────────────────────────────────
    // Post-Process Settings (drives PSXPostProcessFeature)
    // ──────────────────────────────────────────────────────────────
    [Header("Resolution Downscale")]
    [Tooltip("Divide screen resolution by this value. Higher = chunkier pixels.")]
    [Range(1, 6)]
    [SerializeField] private int _resolutionDivisor = 3;

    [Header("Color Depth")]
    [Tooltip("Color levels per channel. Lower = heavier posterization.")]
    [Range(4, 256)]
    [SerializeField] private float _colorDepth = 32f;

    [Header("Dithering")]
    [Tooltip("Ordered dither strength. 0 = off, 1 = full.")]
    [Range(0f, 1f)]
    [SerializeField] private float _ditherIntensity = 0.5f;

    [Header("Shadow Dithering")]
    [Tooltip("PSX-style dithered shadows. 0 = smooth shadows, 1 = fully stippled.")]
    [Range(0f, 1f)]
    [SerializeField] private float _shadowDitherIntensity = 1f;

    // ──────────────────────────────────────────────────────────────
    // Public accessors
    // ──────────────────────────────────────────────────────────────
    public Vector2 VertexSnapResolution
    {
        get => _vertexSnapResolution;
        set { _vertexSnapResolution = value; ApplyGlobals(); }
    }

    public float AffineIntensity
    {
        get => _affineIntensity;
        set { _affineIntensity = Mathf.Clamp01(value); ApplyGlobals(); }
    }

    public int ResolutionDivisor
    {
        get => _resolutionDivisor;
        set { _resolutionDivisor = Mathf.Clamp(value, 1, 6); ApplyFeatureSettings(); }
    }

    public float ColorDepth
    {
        get => _colorDepth;
        set { _colorDepth = Mathf.Clamp(value, 4f, 256f); ApplyFeatureSettings(); }
    }

    public float DitherIntensity
    {
        get => _ditherIntensity;
        set { _ditherIntensity = Mathf.Clamp01(value); ApplyFeatureSettings(); }
    }

    public float ShadowDitherIntensity
    {
        get => _shadowDitherIntensity;
        set { _shadowDitherIntensity = Mathf.Clamp01(value); ApplyGlobals(); }
    }

    // ──────────────────────────────────────────────────────────────
    // Shader property IDs
    // ──────────────────────────────────────────────────────────────
    private static readonly int SnapResID = Shader.PropertyToID("_VertexSnapResolution");
    private static readonly int AffineID = Shader.PropertyToID("_AffineIntensity");
    private static readonly int ShadowDitherID = Shader.PropertyToID("_ShadowDitherIntensity");

    // ──────────────────────────────────────────────────────────────
    // Input
    // ──────────────────────────────────────────────────────────────
    private InputAction _toggleAction;

    // ──────────────────────────────────────────────────────────────
    // Cached feature reference
    // ──────────────────────────────────────────────────────────────
    private PSXPostProcessFeature _feature;

    // ──────────────────────────────────────────────────────────────
    // Runtime shader swap (URP Lit → PSXLit on all scene renderers)
    // ──────────────────────────────────────────────────────────────
    [Header("Shader Reference")]
    [Tooltip("Drag PSXLit shader here. Shader.Find does not work in builds.")]
    [SerializeField] private Shader _psxLitShaderRef;
    private Shader _psxLitShader;
    public Shader PSXLitShader => _psxLitShader;
    private readonly Dictionary<Material, Shader> _originalShaders = new Dictionary<Material, Shader>();
    private Renderer[] _cachedRenderers;
    private static readonly HashSet<string> s_swappableShaders = new HashSet<string>
    {
        "Universal Render Pipeline/Lit",
        "Universal Render Pipeline/Simple Lit",
        "Standard"
    };

    // ──────────────────────────────────────────────────────────────
    // Lifecycle
    // ──────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[PSXRenderController] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _toggleAction = new InputAction("PSXToggle", InputActionType.Button, "<Keyboard>/f4");
        _toggleAction.performed += OnTogglePerformed;
        _toggleAction.Enable();
        _psxLitShader = _psxLitShaderRef != null ? _psxLitShaderRef : Shader.Find("Iris/PSXLit");

        FindFeature();
    }

    private Vector2 _defaultSnapResolution;
    private float _defaultAffineIntensity;

    private void OnEnable()
    {
        // Check if PSX should be disabled via accessibility settings
        if (!AccessibilitySettings.PSXEnabled)
        {
            enabled = false;
            return;
        }

        _defaultSnapResolution = _vertexSnapResolution;
        _defaultAffineIntensity = _affineIntensity;

        // Defer shader swap to end of frame so it doesn't block scene load
        StartCoroutine(DeferredSwap());
        ApplyAll();
        ApplyReduceMotion();

        AccessibilitySettings.OnSettingsChanged += OnAccessibilityChanged;
    }

    private void OnDisable()
    {
        AccessibilitySettings.OnSettingsChanged -= OnAccessibilityChanged;
        RestoreOriginalShaders();
        ResetGlobals();
        SetFeatureActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        AccessibilitySettings.OnSettingsChanged -= OnAccessibilityChanged;

        if (_toggleAction != null)
        {
            _toggleAction.performed -= OnTogglePerformed;
            _toggleAction.Disable();
            _toggleAction.Dispose();
            _toggleAction = null;
        }
    }

    private void OnTogglePerformed(InputAction.CallbackContext ctx)
    {
        if (!AccessibilitySettings.PSXEnabled) return;
        enabled = !enabled;
        Debug.Log($"[PSXRenderController] PSX effect {(enabled ? "ON" : "OFF")}");
    }

    private void OnAccessibilityChanged()
    {
        if (!AccessibilitySettings.PSXEnabled && enabled)
        {
            enabled = false;
            return;
        }

        ApplyReduceMotion();
    }

    private void ApplyReduceMotion()
    {
        if (AccessibilitySettings.ReduceMotion)
        {
            // Disable vertex snapping and affine warping for motion-sensitive users
            Shader.SetGlobalVector(SnapResID, new Vector4(4096, 4096, 0, 0));
            Shader.SetGlobalFloat(AffineID, 0f);
        }
        else
        {
            Shader.SetGlobalVector(SnapResID, new Vector4(_vertexSnapResolution.x, _vertexSnapResolution.y, 0, 0));
            Shader.SetGlobalFloat(AffineID, _affineIntensity);
        }
    }

    private void Update()
    {
        // PSX toggle is handled via InputAction callback (OnTogglePerformed)
    }

    private void OnValidate()
    {
        if (Application.isPlaying && enabled)
            ApplyAll();
    }

    // ──────────────────────────────────────────────────────────────
    // Apply / Reset
    // ──────────────────────────────────────────────────────────────
    private void ApplyAll()
    {
        ApplyGlobals();
        ApplyFeatureSettings();
        SetFeatureActive(true);
    }

    private void ApplyGlobals()
    {
        Shader.SetGlobalVector(SnapResID, new Vector4(_vertexSnapResolution.x, _vertexSnapResolution.y, 0, 0));
        Shader.SetGlobalFloat(AffineID, _affineIntensity);
        Shader.SetGlobalFloat(ShadowDitherID, _shadowDitherIntensity);
    }

    private void ResetGlobals()
    {
        // High snap resolution = effectively no snapping; affine 0 = perspective-correct
        Shader.SetGlobalVector(SnapResID, new Vector4(4096, 4096, 0, 0));
        Shader.SetGlobalFloat(AffineID, 0f);
        Shader.SetGlobalFloat(ShadowDitherID, 0f);
    }

    private void ApplyFeatureSettings()
    {
        if (_feature == null)
            FindFeature();
        if (_feature == null)
            return;

        _feature.settings.resolutionDivisor = _resolutionDivisor;
        _feature.settings.colorDepth = _colorDepth;
        _feature.settings.ditherIntensity = _ditherIntensity;
    }

    private void SetFeatureActive(bool active)
    {
        if (_feature == null)
            FindFeature();
        if (_feature != null)
            _feature.SetActive(active);
    }

    // ──────────────────────────────────────────────────────────────
    // Shader Swap (URP Lit → PSXLit on all scene renderers)
    // ──────────────────────────────────────────────────────────────
    private System.Collections.IEnumerator DeferredSwap()
    {
        // Wait one frame so scene is fully loaded before scanning renderers
        yield return null;
        SwapShadersToRetro();
    }

    private void SwapShadersToRetro()
    {
        if (_psxLitShader == null) return;

        if (_cachedRenderers == null)
            _cachedRenderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        var renderers = _cachedRenderers;
        int swapped = 0;
        foreach (var r in renderers)
        {
            foreach (var mat in r.sharedMaterials)
            {
                if (mat == null || mat.shader == null) continue;
                if (_originalShaders.ContainsKey(mat)) continue;

                if (s_swappableShaders.Contains(mat.shader.name))
                {
                    _originalShaders[mat] = mat.shader;
                    mat.shader = _psxLitShader;
                    swapped++;
                }
            }
        }

        // Vertex snapping only on special items (pairables + authored mess).
        // Everything else gets snap disabled so furniture/environment stays clean.
        foreach (var r in renderers)
        {
            if (r == null) continue;
            var go = r.gameObject;
            if (go.GetComponent<PairableItem>() != null) continue;
            if (go.GetComponentInParent<PairableItem>() != null) continue;
            var po = go.GetComponent<PlaceableObject>() ?? go.GetComponentInParent<PlaceableObject>();
            if (po != null && po.Category == ItemCategory.Trash) continue;
            if (go.GetComponent<PSXObjectSettings>() != null) continue;

            var psx = go.AddComponent<PSXObjectSettings>();
            psx.SnapResolution = 0f;
        }

        if (swapped > 0)
            Debug.Log($"[PSXRenderController] Swapped {swapped} materials to PSXLit.");
    }

    /// <summary>
    /// Swap any unswapped URP/Standard materials on a GameObject that was
    /// inactive during the initial scene scan (e.g. Nema phase models).
    /// Call after activating a previously-hidden model.
    /// </summary>
    public void EnsureSwapped(GameObject go)
    {
        if (_psxLitShader == null || go == null) return;

        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var mat in r.sharedMaterials)
            {
                if (mat == null || mat.shader == null) continue;
                if (_originalShaders.ContainsKey(mat)) continue;

                if (s_swappableShaders.Contains(mat.shader.name))
                {
                    _originalShaders[mat] = mat.shader;
                    mat.shader = _psxLitShader;
                }
            }

            // Disable vertex snap on non-special objects
            if (r.GetComponent<PairableItem>() == null
                && r.GetComponentInParent<PairableItem>() == null
                && r.GetComponent<PSXObjectSettings>() == null)
            {
                var po = r.GetComponent<PlaceableObject>() ?? r.GetComponentInParent<PlaceableObject>();
                if (po == null || po.Category != ItemCategory.Trash)
                {
                    var psx = r.gameObject.AddComponent<PSXObjectSettings>();
                    psx.SnapResolution = 0f;
                }
            }
        }
    }

    private void RestoreOriginalShaders()
    {
        foreach (var kvp in _originalShaders)
        {
            if (kvp.Key != null)
                kvp.Key.shader = kvp.Value;
        }

        Debug.Log($"[PSXRenderController] Restored {_originalShaders.Count} materials.");
        _originalShaders.Clear();
    }

    private void FindFeature()
    {
        // Access renderer features via the pipeline asset (public API)
        var pipeline = UniversalRenderPipeline.asset;
        if (pipeline == null) return;

        int count = pipeline.rendererDataList.Length;
        for (int i = 0; i < count; i++)
        {
            var data = pipeline.rendererDataList[i];
            if (data == null) continue;

            foreach (var feature in data.rendererFeatures)
            {
                if (feature is PSXPostProcessFeature psxFeature)
                {
                    _feature = psxFeature;
                    return;
                }
            }
        }

        Debug.LogWarning("[PSXRenderController] PSXPostProcessFeature not found on URP renderer. " +
                         "Add it to your Renderer asset's feature list.");
    }
}
