using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
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
    [Tooltip("Divide screen resolution by this value. Higher = chunkier pixels. Supports fractional values (e.g. 2.5).")]
    [Range(1f, 6f)]
    [SerializeField] private float _resolutionDivisor = 3f;

    [Header("Color Depth")]
    [Tooltip("Color levels per channel. Lower = heavier posterization.")]
    [Range(4, 256)]
    [SerializeField] private float _colorDepth = 32f;

    [Header("Dithering")]
    [Tooltip("Ordered dither strength. 0 = off, 1 = full.")]
    [Range(0f, 1f)]
    [SerializeField] private float _ditherIntensity = 0.5f;

    [Tooltip("Dither shadow bias. 0 = uniform dither everywhere, 1 = dither only in dark areas (PS1 stipple).")]
    [Range(0f, 1f)]
    [SerializeField] private float _ditherShadowBias = 0.7f;

    [Header("Shadow Dithering (Object Shader)")]
    [Tooltip("PSX-style dithered shadows on PSXLit materials. 0 = smooth shadows, 1 = fully stippled.")]
    [Range(0f, 1f)]
    [SerializeField] private float _shadowDitherIntensity = 1f;

    [Header("Tilt-Shift")]
    [Tooltip("Tilt-shift blur amount. 0 = off, 1 = full blur at edges.")]
    [Range(0f, 1f)]
    [SerializeField] private float _tiltShiftAmount = 0f;

    [Tooltip("Vertical center of the focus band (0 = bottom, 1 = top).")]
    [Range(0f, 1f)]
    [SerializeField] private float _tiltShiftCenter = 0.5f;

    [Tooltip("Half-width of the sharp focus band in UV space.")]
    [Range(0.01f, 0.5f)]
    [SerializeField] private float _tiltShiftWidth = 0.15f;

    [Tooltip("Maximum blur radius in texels.")]
    [Range(1f, 20f)]
    [SerializeField] private float _tiltShiftRadius = 8f;

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

    public float ResolutionDivisor
    {
        get => _resolutionDivisor;
        set { _resolutionDivisor = Mathf.Clamp(value, 1f, 6f); ApplyAll(); }
    }

    public float ColorDepth
    {
        get => _colorDepth;
        set { _colorDepth = Mathf.Clamp(value, 4f, 256f); ApplyAll(); }
    }

    public float DitherIntensity
    {
        get => _ditherIntensity;
        set { _ditherIntensity = Mathf.Clamp01(value); ApplyAll(); }
    }

    public float DitherShadowBias
    {
        get => _ditherShadowBias;
        set { _ditherShadowBias = Mathf.Clamp01(value); ApplyAll(); }
    }

    public float ShadowDitherIntensity
    {
        get => _shadowDitherIntensity;
        set { _shadowDitherIntensity = Mathf.Clamp01(value); ApplyGlobals(); }
    }

    public float TiltShiftAmount
    {
        get => _tiltShiftAmount;
        set { _tiltShiftAmount = Mathf.Clamp01(value); ApplyGlobals(); }
    }

    public float TiltShiftCenter
    {
        get => _tiltShiftCenter;
        set { _tiltShiftCenter = Mathf.Clamp01(value); ApplyGlobals(); }
    }

    public float TiltShiftWidth
    {
        get => _tiltShiftWidth;
        set { _tiltShiftWidth = Mathf.Clamp(value, 0.01f, 0.5f); ApplyGlobals(); }
    }

    public float TiltShiftRadius
    {
        get => _tiltShiftRadius;
        set { _tiltShiftRadius = Mathf.Clamp(value, 1f, 20f); ApplyGlobals(); }
    }

    // ──────────────────────────────────────────────────────────────
    // Shader property IDs
    // ──────────────────────────────────────────────────────────────
    private static readonly int SnapResID = Shader.PropertyToID("_VertexSnapResolution");
    private static readonly int AffineID = Shader.PropertyToID("_AffineIntensity");
    private static readonly int ShadowDitherID = Shader.PropertyToID("_ShadowDitherIntensity");
    private static readonly int TiltAmountID = Shader.PropertyToID("_TiltShiftAmount");
    private static readonly int TiltCenterID = Shader.PropertyToID("_TiltShiftCenter");
    private static readonly int TiltWidthID = Shader.PropertyToID("_TiltShiftWidth");
    private static readonly int TiltRadiusID = Shader.PropertyToID("_TiltShiftRadius");

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
        // Read from Volume stack if a PSXPostProcessVolume override exists
        var stack = VolumeManager.instance.stack;
        var vol = stack.GetComponent<PSXPostProcessVolume>();
        if (vol != null && vol.active)
        {
            bool changed = false;

            if (vol.resolutionDivisor.overrideState && !Mathf.Approximately(_resolutionDivisor, vol.resolutionDivisor.value))
            { _resolutionDivisor = vol.resolutionDivisor.value; changed = true; }

            if (vol.colorDepth.overrideState && !Mathf.Approximately(_colorDepth, vol.colorDepth.value))
            { _colorDepth = vol.colorDepth.value; changed = true; }

            if (vol.ditherIntensity.overrideState && !Mathf.Approximately(_ditherIntensity, vol.ditherIntensity.value))
            { _ditherIntensity = vol.ditherIntensity.value; changed = true; }

            if (vol.ditherShadowBias.overrideState && !Mathf.Approximately(_ditherShadowBias, vol.ditherShadowBias.value))
            { _ditherShadowBias = vol.ditherShadowBias.value; changed = true; }

            if (vol.vertexSnapResolution.overrideState)
            {
                float snapX = vol.vertexSnapResolution.value;
                if (!Mathf.Approximately(_vertexSnapResolution.x, snapX))
                { _vertexSnapResolution = new Vector2(snapX, snapX * 0.75f); changed = true; }
            }

            if (vol.affineIntensity.overrideState && !Mathf.Approximately(_affineIntensity, vol.affineIntensity.value))
            { _affineIntensity = vol.affineIntensity.value; changed = true; }

            if (vol.shadowDitherIntensity.overrideState && !Mathf.Approximately(_shadowDitherIntensity, vol.shadowDitherIntensity.value))
            { _shadowDitherIntensity = vol.shadowDitherIntensity.value; changed = true; }

            if (vol.tiltShiftAmount.overrideState && !Mathf.Approximately(_tiltShiftAmount, vol.tiltShiftAmount.value))
            { _tiltShiftAmount = vol.tiltShiftAmount.value; changed = true; }

            if (vol.tiltShiftCenter.overrideState && !Mathf.Approximately(_tiltShiftCenter, vol.tiltShiftCenter.value))
            { _tiltShiftCenter = vol.tiltShiftCenter.value; changed = true; }

            if (vol.tiltShiftWidth.overrideState && !Mathf.Approximately(_tiltShiftWidth, vol.tiltShiftWidth.value))
            { _tiltShiftWidth = vol.tiltShiftWidth.value; changed = true; }

            if (vol.tiltShiftRadius.overrideState && !Mathf.Approximately(_tiltShiftRadius, vol.tiltShiftRadius.value))
            { _tiltShiftRadius = vol.tiltShiftRadius.value; changed = true; }

            if (changed) ApplyAll();
        }
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
        Shader.SetGlobalFloat(TiltAmountID, _tiltShiftAmount);
        Shader.SetGlobalFloat(TiltCenterID, _tiltShiftCenter);
        Shader.SetGlobalFloat(TiltWidthID, _tiltShiftWidth);
        Shader.SetGlobalFloat(TiltRadiusID, _tiltShiftRadius);
    }

    private void ResetGlobals()
    {
        Shader.SetGlobalVector(SnapResID, new Vector4(4096, 4096, 0, 0));
        Shader.SetGlobalFloat(AffineID, 0f);
        Shader.SetGlobalFloat(ShadowDitherID, 0f);
        Shader.SetGlobalFloat(TiltAmountID, 0f);
        Shader.SetGlobalFloat(TiltCenterID, 0.5f);
        Shader.SetGlobalFloat(TiltWidthID, 0.15f);
        Shader.SetGlobalFloat(TiltRadiusID, 8f);
    }

    private void ApplyFeatureSettings()
    {
        // Feature now reads directly from PSXPostProcessVolume on the
        // volume stack — push our values there so everything stays in sync.
        var stack = VolumeManager.instance.stack;
        var vol = stack.GetComponent<PSXPostProcessVolume>();
        if (vol == null) return;

        vol.resolutionDivisor.Override(_resolutionDivisor);
        vol.colorDepth.Override(_colorDepth);
        vol.ditherIntensity.Override(_ditherIntensity);
        vol.ditherShadowBias.Override(_ditherShadowBias);
        vol.vertexSnapResolution.Override(_vertexSnapResolution.x);
        vol.affineIntensity.Override(_affineIntensity);
        vol.shadowDitherIntensity.Override(_shadowDitherIntensity);
        vol.tiltShiftAmount.Override(_tiltShiftAmount);
        vol.tiltShiftCenter.Override(_tiltShiftCenter);
        vol.tiltShiftWidth.Override(_tiltShiftWidth);
        vol.tiltShiftRadius.Override(_tiltShiftRadius);
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
        // PSX shader swap disabled — artist materials stay as authored.
        Debug.Log("[PSXRenderController] Shader swap disabled — using authored materials.");
    }

    /// <summary>
    /// Swap any unswapped URP/Standard materials on a GameObject that was
    /// inactive during the initial scene scan (e.g. Nema phase models).
    /// Call after activating a previously-hidden model.
    /// </summary>
    public void EnsureSwapped(GameObject go)
    {
        // PSX shader swap disabled — artist materials stay as authored.
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
