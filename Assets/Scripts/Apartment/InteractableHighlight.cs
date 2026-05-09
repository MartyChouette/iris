using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Highlight style options. Switch at runtime via InteractableHighlight.CurrentStyle.
/// </summary>
public enum HighlightStyle
{
    Global = -1,    // Use the global style (per-item override only)
    Outline,        // Solid line around silhouette (back-face extrusion)
    RimGlow,        // Fresnel edge glow + subtle fill (original)
    SolidOverlay,   // Flat semi-transparent color wash
    DashedOutline,  // Animated dashed/marching-ants outline
    DoubleOutline,  // Two-tone inner + outer outline
    FresnelOutline, // Outline that glows brighter at silhouette edges
    CleanOutline,   // Clean silhouette — no vertex snap, no jitter, true geometry
}

/// <summary>
/// Toggleable highlight overlay on the object's renderers.
/// Supports multiple styles (Outline, RimGlow, SolidOverlay) switchable at runtime.
/// Layers: Display, Gaze, PrepLiked, PrepDisliked, Hover.
/// Attach to any clickable object (books, records, placeables, pots, plants, etc.).
/// </summary>
public class InteractableHighlight : MonoBehaviour
{
    // ── Static registry ───────────────
    private static readonly List<InteractableHighlight> s_all = new();
    public static IReadOnlyList<InteractableHighlight> All => s_all;

    // ── Suppress visual highlight (ref-counted) ───────────────
    // Multiple systems can request highlights simultaneously.
    // Only suppress when NO system wants highlights (count == 0).
    private static int s_unsuppressCount;

    /// <summary>
    /// Ref-counted suppress. Set true to request highlights, false to release.
    /// Highlights render when at least one requester is active.
    /// </summary>
    public static bool SuppressVisuals
    {
        get => s_unsuppressCount <= 0;
        set
        {
            if (!value)
                s_unsuppressCount++;
            else
            {
                s_unsuppressCount = Mathf.Max(s_unsuppressCount - 1, 0);
                // Strip overlays only when all requesters have released
                if (s_unsuppressCount <= 0)
                {
                    for (int i = 0; i < s_all.Count; i++)
                        s_all[i].StripOverlayMaterials();
                }
            }
        }
    }

    /// <summary>Force reset the suppress counter (use on scene transitions).</summary>
    public static void ResetSuppressCount()
    {
        s_unsuppressCount = 0;
        for (int i = 0; i < s_all.Count; i++)
            s_all[i].StripOverlayMaterials();
    }

    // ── Global kill switch ───────────────
    // When true, the component still registers for cursor detection but
    // never touches renderer materials. Flip to false when the highlight
    // system is reworked.
    public static bool Disabled { get; set; } = true;

    // ── Highlight style ───────────────
    private static HighlightStyle s_currentStyle = HighlightStyle.CleanOutline;

    public static HighlightStyle CurrentStyle
    {
        get => s_currentStyle;
        set
        {
            if (s_currentStyle == value) return;
            s_currentStyle = value;
            DestroySharedMaterials();
            // Force all active highlights to rebuild with new style
            for (int i = 0; i < s_all.Count; i++)
            {
                s_all[i].EnsureSharedMaterials();
                if (s_all[i]._glitchIntensity > 0)
                    s_all[i].BuildGlitchRimMaterials();
                s_all[i].RebuildMaterials();
            }
            Debug.Log($"[InteractableHighlight] Style changed to {value}");
        }
    }

    public static void CycleStyle()
    {
        // Skip Global (-1) — that's only for per-item overrides
        int next = (int)s_currentStyle + 1;
        if (next > (int)HighlightStyle.CleanOutline)
            next = (int)HighlightStyle.Outline;
        CurrentStyle = (HighlightStyle)next;
    }

    // ── Runtime tuning overrides (driven by debug panel sliders) ──
    private static float s_tuneWidth = 0.008f;
    private static float s_tuneAlpha = 0.25f;
    private static float s_tunePulse = 0.1f;
    private static float s_tuneRim = 2.5f;

    public static void SetTuningOverrides(float width, float alpha, float pulse, float rimPower)
    {
        s_tuneWidth = width;
        s_tuneAlpha = alpha;
        s_tunePulse = pulse;
        s_tuneRim = rimPower;

        // Rebuild all shared materials with new values
        DestroySharedMaterials();
        for (int i = 0; i < s_all.Count; i++)
        {
            s_all[i].EnsureSharedMaterials();
            if (s_all[i]._glitchIntensity > 0)
                s_all[i].BuildGlitchRimMaterials();
            s_all[i].RebuildMaterials();
        }
    }

    // ── Global highlight shader params (snap, jitter, normal offset) ──
    private static bool  s_hlSnapEnabled = true;
    private static float s_hlSnapRes = 160f;
    private static float s_hlNormalOffset = 0.001f;
    private static float s_hlJitter = 0f;

    private static readonly int HLSnapResID = Shader.PropertyToID("_HighlightSnapResolution");
    private static readonly int HLJitterID = Shader.PropertyToID("_HighlightJitter");
    private static readonly int HLNormalOffsetID = Shader.PropertyToID("_HighlightNormalOffset");

    public static bool  HLSnapEnabled   { get => s_hlSnapEnabled; set { s_hlSnapEnabled = value; ApplyGlobalShaderParams(); } }
    public static float HLSnapRes       { get => s_hlSnapRes; set { s_hlSnapRes = value; ApplyGlobalShaderParams(); } }
    public static float HLNormalOffset  { get => s_hlNormalOffset; set { s_hlNormalOffset = value; ApplyGlobalShaderParams(); } }
    public static float HLJitter        { get => s_hlJitter; set { s_hlJitter = value; ApplyGlobalShaderParams(); } }

    /// <summary>Push highlight globals to all shaders.</summary>
    public static void ApplyGlobalShaderParams()
    {
        float snap = s_hlSnapEnabled ? s_hlSnapRes : 0f;
        Shader.SetGlobalVector(HLSnapResID, new Vector4(snap, snap * 0.75f, 0f, 0f));
        Shader.SetGlobalFloat(HLJitterID, s_hlJitter);
        Shader.SetGlobalFloat(HLNormalOffsetID, s_hlNormalOffset);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitGlobals()
    {
        ApplyGlobalShaderParams();
    }

    [Header("Per-Item Style Override")]
    [Tooltip("Override the global highlight style for this item. Leave at Global to use the global style. Use RimGlow for glasses, bottles, cues.")]
    [SerializeField] private HighlightStyle _itemStyle = HighlightStyle.Global;

    [Tooltip("Drag Iris/Highlight shader here so it's included in builds.")]
    [SerializeField] private Shader _highlightShader;

    [Tooltip("Drag Iris/HighlightOutline shader here so it's included in builds.")]
    [SerializeField] private Shader _outlineShader;

    [Tooltip("Drag Iris/HighlightOverlay shader here so it's included in builds.")]
    [SerializeField] private Shader _overlayShader;

    [Tooltip("Drag Iris/PSXInteractable shader here so it's included in builds.")]
    [SerializeField] private Shader _interactShader;

    private static Shader s_cachedHighlightShader;
    private static Shader s_cachedOutlineShader;
    private static Shader s_cachedOverlayShader;
    private static Shader s_cachedInteractShader;
    private static Shader s_cachedDashShader;
    private static Shader s_cachedDoubleShader;
    private static Shader s_cachedFresnelShader;
    private static Shader s_cachedCleanShader;
    private static Material s_sharedRimMat;
    private static Material s_sharedGazeMat;
    private static Material s_sharedDisplayMat;
    private static Material s_sharedPrepLikedMat;
    private static Material s_sharedPrepDislikedMat;
    private static Material s_sharedInteractMat;

    /// <summary>Cached Iris/Highlight shader. Available for other scripts.</summary>
    public static Shader HighlightShader => s_cachedHighlightShader;

    private Renderer[] _renderers;
    private Material[][] _baseMaterialArrays;
    private bool _highlighted;
    private bool _gazeActive;
    private bool _displayActive;
    private bool _prepLikedActive;
    private bool _prepDislikedActive;

    private bool _interactActive;

    // Per-instance interact material (copies base texture for affine warp overlay)
    private Material _instanceInteractMat;

    // Per-instance override materials (only created when _itemStyle != Global)
    private Material _overrideRimMat;
    private Material _overrideGazeMat;
    private Material _overrideDisplayMat;
    private Material _overridePrepLikedMat;
    private Material _overridePrepDislikedMat;

    // Per-instance glitch rim materials (only created for objects with _GlitchIntensity > 0)
    private float _glitchIntensity;
    private Material _glitchRimMat;
    private Material _glitchGazeMat;
    private Material _glitchDisplayMat;
    private Material _glitchPrepLikedMat;
    private Material _glitchPrepDislikedMat;
    private Material _glitchInteractMat;

    private static readonly int GlitchID = Shader.PropertyToID("_GlitchIntensity");
    private static readonly int MainTexID = Shader.PropertyToID("_MainTex");

    // ── Layer color definitions ───────────────
    // Each layer has: color, alpha/width (intensity), pulse speed, pulse amount
    // Cool-toned colors that read well through the FF8/SotC atmosphere (desaturated + teal shadows)
    private static readonly Color HoverColor = new Color(0.9f, 0.95f, 1f, 1f);       // cool white
    private static readonly Color GazeColor = new Color(0.4f, 0.85f, 0.95f, 1f);     // bright teal
    private static readonly Color DisplayColor = new Color(0.7f, 0.8f, 0.95f, 1f);   // soft blue
    private static readonly Color PrepLikedColor = new Color(0.3f, 0.95f, 0.7f, 1f); // cyan-green
    private static readonly Color PrepDislikedColor = new Color(1f, 0.35f, 0.5f, 1f); // bright pink

    private void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>();

        // PSXLit auto-swap removed — artist materials stay as authored (URP).

        // When visuals are suppressed, skip highlight material setup.
        // The component only exists for the static registry (cursor detection).
        if (SuppressVisuals) return;

        if (_renderers.Length == 0) return;

        // Cache shaders from serialized refs or Shader.Find fallback
        if (s_cachedHighlightShader == null)
        {
            s_cachedHighlightShader = _highlightShader;
            if (s_cachedHighlightShader == null) s_cachedHighlightShader = Shader.Find("Iris/Highlight");
        }
        if (s_cachedOutlineShader == null)
        {
            s_cachedOutlineShader = _outlineShader;
            if (s_cachedOutlineShader == null) s_cachedOutlineShader = Shader.Find("Iris/HighlightOutline");
        }
        if (s_cachedOverlayShader == null)
        {
            s_cachedOverlayShader = _overlayShader;
            if (s_cachedOverlayShader == null) s_cachedOverlayShader = Shader.Find("Iris/HighlightOverlay");
        }
        if (s_cachedInteractShader == null)
        {
            s_cachedInteractShader = _interactShader;
            if (s_cachedInteractShader == null) s_cachedInteractShader = Shader.Find("Iris/PSXInteractable");
        }
        if (s_cachedDashShader == null)
            s_cachedDashShader = Shader.Find("Iris/HighlightDash");
        if (s_cachedDoubleShader == null)
            s_cachedDoubleShader = Shader.Find("Iris/HighlightDouble");
        if (s_cachedFresnelShader == null)
            s_cachedFresnelShader = Shader.Find("Iris/HighlightFresnel");
        if (s_cachedCleanShader == null)
            s_cachedCleanShader = Shader.Find("Iris/HighlightClean");

        EnsureSharedMaterials();

        _baseMaterialArrays = new Material[_renderers.Length][];
        for (int i = 0; i < _renderers.Length; i++)
            _baseMaterialArrays[i] = _renderers[i].sharedMaterials;

        DetectGlitch();
        BuildOverrideMaterials();
    }

    /// <summary>
    /// Re-cache the base materials from the renderer. Call after any external
    /// system swaps materials on the renderer (e.g. glitch apply/remove).
    /// </summary>
    public void RefreshBaseMaterials()
    {
        if (_renderers == null) return;
        _baseMaterialArrays = new Material[_renderers.Length][];
        for (int i = 0; i < _renderers.Length; i++)
            _baseMaterialArrays[i] = _renderers[i].sharedMaterials;
    }

    private void OnEnable() => s_all.Add(this);
    private void OnDisable()
    {
        s_all.Remove(this);
        // Clear any MPB color pulse when disabled
        if (_fallbackPulsing && _renderers != null)
        {
            for (int i = 0; i < _renderers.Length; i++)
                if (_renderers[i] != null) _renderers[i].SetPropertyBlock(null);
            _fallbackPulsing = false;
        }
    }

    // Fallback color pulse — used when custom highlight shaders are missing (builds)
    private bool _fallbackPulsing;
    private MaterialPropertyBlock _cachedMPB;
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorID = Shader.PropertyToID("_Color");

    private void Update()
    {
        bool anyActive = _highlighted || _gazeActive || _displayActive
            || _prepLikedActive || _prepDislikedActive;

        if (!anyActive)
        {
            if (_fallbackPulsing)
            {
                _fallbackPulsing = false;
                if (_renderers != null)
                    for (int i = 0; i < _renderers.Length; i++)
                        if (_renderers[i] != null) _renderers[i].SetPropertyBlock(null);
            }
            return;
        }

        // Only use fallback pulse if the overlay materials are missing (shader not in build)
        bool overlayWorking = s_sharedRimMat != null || s_sharedGazeMat != null || s_sharedDisplayMat != null;
        if (overlayWorking) return;

        // Pulse the base material color directly via MPB
        _fallbackPulsing = true;
        Color pulseColor = _displayActive ? DisplayColor
            : _gazeActive ? GazeColor
            : _prepLikedActive ? PrepLikedColor
            : _prepDislikedActive ? PrepDislikedColor
            : HoverColor;

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f);
        Color tint = Color.Lerp(Color.white, pulseColor, pulse * 0.6f);

        if (_cachedMPB == null) _cachedMPB = new MaterialPropertyBlock();
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] == null) continue;
            _renderers[i].GetPropertyBlock(_cachedMPB);
            _cachedMPB.SetColor(BaseColorID, tint);
            _cachedMPB.SetColor(ColorID, tint);
            _renderers[i].SetPropertyBlock(_cachedMPB);
        }
    }

    public void SetHighlighted(bool on)
    {
        if (Disabled) return; // kill switch only blocks general hover
        if (_renderers == null || _renderers.Length == 0 || on == _highlighted) return;
        _highlighted = on;
        RebuildMaterials();
    }

    public void SetGazeHighlighted(bool on)
    {
        if (_renderers == null || _renderers.Length == 0 || on == _gazeActive) return;
        _gazeActive = on;
        RebuildMaterials();
    }

    public void SetDisplayHighlighted(bool on)
    {
        if (_renderers == null || _renderers.Length == 0 || on == _displayActive) return;
        _displayActive = on;
        RebuildMaterials();
    }

    public void SetPrepLikedHighlighted(bool on)
    {
        if (_renderers == null || _renderers.Length == 0 || on == _prepLikedActive) return;
        _prepLikedActive = on;
        RebuildMaterials();
    }

    public void SetPrepDislikedHighlighted(bool on)
    {
        if (_renderers == null || _renderers.Length == 0 || on == _prepDislikedActive) return;
        _prepDislikedActive = on;
        RebuildMaterials();
    }

    public void SetInteractHighlighted(bool on)
    {
        if (_renderers == null || _renderers.Length == 0 || on == _interactActive) return;
        _interactActive = on;
        if (!on && _instanceInteractMat != null)
        {
            Destroy(_instanceInteractMat);
            _instanceInteractMat = null;
        }
        RebuildMaterials();
    }

    // ── Glitch support ───────────────

    private void DetectGlitch()
    {
        for (int r = 0; r < _renderers.Length; r++)
        {
            if (_renderers[r] == null) continue;
            foreach (var mat in _baseMaterialArrays[r])
            {
                if (mat == null || !mat.HasFloat(GlitchID)) continue;
                float gi = mat.GetFloat(GlitchID);
                if (gi > 0.001f)
                {
                    _glitchIntensity = gi;
                    BuildGlitchRimMaterials();
                    return;
                }
            }
        }
    }

    private void BuildGlitchRimMaterials()
    {
        _glitchRimMat = MakeGlitchVariant(s_sharedRimMat);
        _glitchGazeMat = MakeGlitchVariant(s_sharedGazeMat);
        _glitchDisplayMat = MakeGlitchVariant(s_sharedDisplayMat);
        _glitchPrepLikedMat = MakeGlitchVariant(s_sharedPrepLikedMat);
        _glitchPrepDislikedMat = MakeGlitchVariant(s_sharedPrepDislikedMat);
        _glitchInteractMat = MakeGlitchVariant(s_sharedInteractMat);
    }

    private Material MakeGlitchVariant(Material source)
    {
        if (source == null) return null;
        var mat = new Material(source);
        mat.SetFloat(GlitchID, _glitchIntensity);
        return mat;
    }

    private Material GetInteractMat(Material[] baseMats)
    {
        if (_instanceInteractMat != null) return _instanceInteractMat;

        var source = _glitchIntensity > 0 && _glitchInteractMat != null
            ? _glitchInteractMat
            : s_sharedInteractMat;
        if (source == null) return null;

        // Shader no longer samples a texture — shared material works directly
        _instanceInteractMat = new Material(source);
        return _instanceInteractMat;
    }

    private Material PickRim(Material shared, Material glitch, Material overrideMat = null)
    {
        if (overrideMat != null) return overrideMat;
        return _glitchIntensity > 0 && glitch != null ? glitch : shared;
    }

    private void BuildOverrideMaterials()
    {
        if (_itemStyle == HighlightStyle.Global) return;
        DestroyOverrideMaterials();
        _overrideRimMat = MakeMatForStyle(_itemStyle, HoverColor, 0.5f, 2f, 0.1f);
        _overrideGazeMat = MakeMatForStyle(_itemStyle, GazeColor, 0.4f, 1.5f, 0.08f);
        _overrideDisplayMat = MakeMatForStyle(_itemStyle, DisplayColor, 0.25f, 1f, 0.05f);
        _overridePrepLikedMat = MakeMatForStyle(_itemStyle, PrepLikedColor, 0.4f, 1.5f, 0.08f);
        _overridePrepDislikedMat = MakeMatForStyle(_itemStyle, PrepDislikedColor, 0.4f, 1.5f, 0.08f);
    }

    private void DestroyOverrideMaterials()
    {
        if (_overrideRimMat != null) { Destroy(_overrideRimMat); _overrideRimMat = null; }
        if (_overrideGazeMat != null) { Destroy(_overrideGazeMat); _overrideGazeMat = null; }
        if (_overrideDisplayMat != null) { Destroy(_overrideDisplayMat); _overrideDisplayMat = null; }
        if (_overridePrepLikedMat != null) { Destroy(_overridePrepLikedMat); _overridePrepLikedMat = null; }
        if (_overridePrepDislikedMat != null) { Destroy(_overridePrepDislikedMat); _overridePrepDislikedMat = null; }
    }

    // ── Material rebuild ───────────────

    /// <summary>Reset renderers to base materials only (strip all highlight overlays).</summary>
    private void StripOverlayMaterials()
    {
        if (_renderers == null || _baseMaterialArrays == null) return;
        for (int r = 0; r < _renderers.Length; r++)
        {
            if (_renderers[r] == null) continue;
            if (r >= _baseMaterialArrays.Length || _baseMaterialArrays[r] == null) continue;
            var current = _renderers[r].sharedMaterials;
            int baseLen = _baseMaterialArrays[r].Length;
            if (current.Length > baseLen)
            {
                var trimmed = new Material[baseLen];
                System.Array.Copy(current, trimmed, baseLen);
                _renderers[r].sharedMaterials = trimmed;
            }
            _baseMaterialArrays[r] = _renderers[r].sharedMaterials;
        }
    }

    private void RebuildMaterials()
    {

        // When visuals are suppressed, do nothing — don't touch materials at all
        if (SuppressVisuals)
            return;

        // Materials weren't set up if visuals were suppressed during Awake —
        // initialize them now on first actual use.
        if (_baseMaterialArrays == null && _renderers != null && _renderers.Length > 0)
        {
            EnsureSharedMaterials();
            _baseMaterialArrays = new Material[_renderers.Length][];
            for (int i = 0; i < _renderers.Length; i++)
                _baseMaterialArrays[i] = _renderers[i].sharedMaterials;
            DetectGlitch();
        }

        if (_baseMaterialArrays == null)
            return;

        int extraCount = (_interactActive ? 1 : 0)
                       + (_displayActive ? 1 : 0)
                       + (_prepLikedActive ? 1 : 0)
                       + (_prepDislikedActive ? 1 : 0)
                       + (_gazeActive ? 1 : 0)
                       + (_highlighted ? 1 : 0);

        for (int r = 0; r < _renderers.Length; r++)
        {
            if (_renderers[r] == null) continue;

            // Always read current base materials from the renderer so we
            // never use stale/destroyed refs from a previous glitch swap.
            var current = _renderers[r].sharedMaterials;
            // Base material count = total minus any overlay slots we previously added
            int baseLen = _baseMaterialArrays[r] != null ? _baseMaterialArrays[r].Length : current.Length;
            if (current.Length >= baseLen)
            {
                var fresh = new Material[baseLen];
                System.Array.Copy(current, fresh, baseLen);
                _baseMaterialArrays[r] = fresh;
            }
            var baseMats = _baseMaterialArrays[r];

            if (extraCount == 0)
            {
                _renderers[r].sharedMaterials = baseMats;
                continue;
            }

            // Re-read base from renderer to avoid using stale/destroyed refs
            baseMats = _renderers[r].sharedMaterials;
            // Trim to base count if overlays were previously appended
            int bLen = _baseMaterialArrays[r].Length;
            if (baseMats.Length > bLen)
            {
                var t = new Material[bLen];
                System.Array.Copy(baseMats, t, bLen);
                baseMats = t;
            }
            _baseMaterialArrays[r] = baseMats;

            var mats = new Material[baseMats.Length + extraCount];
            for (int i = 0; i < baseMats.Length; i++)
                mats[i] = baseMats[i];

            int slot = baseMats.Length;

            if (_interactActive)
            {
                var m = GetInteractMat(baseMats);
                if (m != null) mats[slot++] = m;
            }
            if (_displayActive)
            {
                var m = PickRim(s_sharedDisplayMat, _glitchDisplayMat, _overrideDisplayMat);
                if (m != null) mats[slot++] = m;
            }
            if (_prepLikedActive)
            {
                var m = PickRim(s_sharedPrepLikedMat, _glitchPrepLikedMat, _overridePrepLikedMat);
                if (m != null) mats[slot++] = m;
            }
            if (_prepDislikedActive)
            {
                var m = PickRim(s_sharedPrepDislikedMat, _glitchPrepDislikedMat, _overridePrepDislikedMat);
                if (m != null) mats[slot++] = m;
            }
            if (_gazeActive)
            {
                var m = PickRim(s_sharedGazeMat, _glitchGazeMat, _overrideGazeMat);
                if (m != null) mats[slot++] = m;
            }
            if (_highlighted)
            {
                var m = PickRim(s_sharedRimMat, _glitchRimMat, _overrideRimMat);
                if (m != null) mats[slot++] = m;
            }

            _renderers[r].sharedMaterials = mats;
        }
    }

    private void OnDestroy()
    {
        if (_instanceInteractMat != null) Destroy(_instanceInteractMat);
        if (_glitchRimMat != null) Destroy(_glitchRimMat);
        if (_glitchGazeMat != null) Destroy(_glitchGazeMat);
        if (_glitchDisplayMat != null) Destroy(_glitchDisplayMat);
        if (_glitchPrepLikedMat != null) Destroy(_glitchPrepLikedMat);
        if (_glitchPrepDislikedMat != null) Destroy(_glitchPrepDislikedMat);
        if (_glitchInteractMat != null) Destroy(_glitchInteractMat);
        DestroyOverrideMaterials();
    }

    // ── PSXLit swap ───────────────

    private static readonly HashSet<string> s_swappableShaders = new()
    {
        "Universal Render Pipeline/Lit",
        "Universal Render Pipeline/Simple Lit",
        "Standard"
    };

    private static Shader s_cachedPSXLitShader;

    private void SwapToPSXLit()
    {
        if (PSXRenderController.Instance == null || !PSXRenderController.Instance.enabled) return;

        if (s_cachedPSXLitShader == null) s_cachedPSXLitShader = Shader.Find("Iris/PSXLit");
        var psxShader = s_cachedPSXLitShader;
        if (psxShader == null) return;

        for (int r = 0; r < _renderers.Length; r++)
        {
            if (_renderers[r] == null) continue;
            foreach (var mat in _renderers[r].sharedMaterials)
            {
                if (mat == null || mat.shader == null) continue;
                if (s_swappableShaders.Contains(mat.shader.name))
                    mat.shader = psxShader;
            }
        }
    }

    // ── Material factory ───────────────

    private static Material MakeMatForStyle(Color color, float intensityScale, float pulseSpeed, float pulseAmount)
    {
        return MakeMatForStyle(s_currentStyle, color, intensityScale, pulseSpeed, pulseAmount);
    }

    private static Material MakeMatForStyle(HighlightStyle style, Color color, float intensityScale, float pulseSpeed, float pulseAmount)
    {
        // Scale per-layer intensity by the global tuning alpha
        float alpha = s_tuneAlpha * intensityScale * 2f;
        float pulse = s_tunePulse;

        switch (style)
        {
            case HighlightStyle.Outline:
                return MakeOutlineMat(color, s_tuneWidth * intensityScale * 2f, pulseSpeed, pulse);
            case HighlightStyle.SolidOverlay:
                return MakeOverlayMat(color, alpha, pulseSpeed, pulse);
            case HighlightStyle.DashedOutline:
                return MakeDashMat(color, s_tuneWidth * intensityScale * 2f, pulseSpeed, pulse);
            case HighlightStyle.DoubleOutline:
                return MakeDoubleMat(color, s_tuneWidth * intensityScale * 2f, pulseSpeed, pulse);
            case HighlightStyle.FresnelOutline:
                return MakeFresnelMat(color, s_tuneWidth * intensityScale * 2f, pulseSpeed, pulse);
            case HighlightStyle.CleanOutline:
                return MakeCleanMat(color, s_tuneWidth * intensityScale * 2f, pulseSpeed, pulse);
            case HighlightStyle.RimGlow:
            default:
                return MakeRimGlowMat(color, alpha, pulseSpeed, pulse);
        }
    }

    private static Material MakeOutlineMat(Color color, float width, float pulseSpeed, float pulseAmount)
    {
        var shader = s_cachedOutlineShader;
        if (shader == null) return null;
        var mat = new Material(shader);
        mat.SetColor("_OutlineColor", color);
        mat.SetFloat("_OutlineWidth", Mathf.Clamp(width, 0.002f, 0.05f));
        mat.SetFloat("_PulseSpeed", pulseSpeed);
        mat.SetFloat("_PulseAmount", pulseAmount);
        return mat;
    }

    private static Material MakeOverlayMat(Color color, float alpha, float pulseSpeed, float pulseAmount)
    {
        var shader = s_cachedOverlayShader;
        if (shader == null) return null;
        var mat = new Material(shader);
        mat.SetColor("_OverlayColor", new Color(color.r, color.g, color.b, alpha));
        mat.SetFloat("_PulseSpeed", pulseSpeed);
        mat.SetFloat("_PulseAmount", pulseAmount);
        return mat;
    }

    private static Material MakeRimGlowMat(Color color, float intensity, float pulseSpeed, float pulseAmount)
    {
        var shader = s_cachedHighlightShader;
        if (shader == null) return null;
        var mat = new Material(shader);
        float fillAlpha = intensity * 0.5f;
        float rimAlpha = intensity;
        mat.SetColor("_HighlightColor", new Color(color.r, color.g, color.b, fillAlpha));
        mat.SetColor("_RimColor", new Color(color.r, color.g, color.b, rimAlpha));
        mat.SetFloat("_RimPower", s_tuneRim);
        mat.SetFloat("_PulseSpeed", pulseSpeed);
        mat.SetFloat("_PulseAmount", pulseAmount);
        return mat;
    }

    private static Material MakeDashMat(Color color, float width, float pulseSpeed, float pulseAmount)
    {
        var shader = s_cachedDashShader;
        if (shader == null) return null;
        var mat = new Material(shader);
        mat.SetColor("_OutlineColor", color);
        mat.SetFloat("_OutlineWidth", Mathf.Clamp(width, 0.002f, 0.05f));
        mat.SetFloat("_DashFreq", 20f);
        mat.SetFloat("_DashRatio", 0.5f);
        mat.SetFloat("_ScrollSpeed", 2f);
        mat.SetFloat("_PulseSpeed", pulseSpeed);
        mat.SetFloat("_PulseAmount", pulseAmount);
        return mat;
    }

    private static Material MakeDoubleMat(Color color, float width, float pulseSpeed, float pulseAmount)
    {
        var shader = s_cachedDoubleShader;
        if (shader == null) return null;
        var mat = new Material(shader);
        mat.SetColor("_OutlineColor", color);
        mat.SetColor("_InnerColor", new Color(color.r * 1.2f, color.g * 0.9f, color.b * 0.7f, 0.5f));
        mat.SetFloat("_OutlineWidth", Mathf.Clamp(width, 0.002f, 0.05f));
        mat.SetFloat("_InnerWidth", Mathf.Clamp(width * 0.4f, 0.002f, 0.03f));
        mat.SetFloat("_PulseSpeed", pulseSpeed);
        mat.SetFloat("_PulseAmount", pulseAmount);
        return mat;
    }

    private static Material MakeFresnelMat(Color color, float width, float pulseSpeed, float pulseAmount)
    {
        var shader = s_cachedFresnelShader;
        if (shader == null) return null;
        var mat = new Material(shader);
        mat.SetColor("_OutlineColor", color);
        mat.SetFloat("_OutlineWidth", Mathf.Clamp(width, 0.002f, 0.05f));
        mat.SetFloat("_FresnelPower", s_tuneRim);
        mat.SetFloat("_FresnelMin", 0.3f);
        mat.SetFloat("_PulseSpeed", pulseSpeed);
        mat.SetFloat("_PulseAmount", pulseAmount);
        return mat;
    }

    private static Material MakeCleanMat(Color color, float width, float pulseSpeed, float pulseAmount)
    {
        var shader = s_cachedCleanShader;
        if (shader == null) return null;
        var mat = new Material(shader);
        mat.SetColor("_OutlineColor", color);
        mat.SetFloat("_OutlineWidth", Mathf.Clamp(width, 0.002f, 0.05f));
        mat.SetFloat("_PulseSpeed", pulseSpeed);
        mat.SetFloat("_PulseAmount", pulseAmount);
        return mat;
    }

    private static void DestroySharedMaterials()
    {
        if (s_sharedRimMat != null) { Destroy(s_sharedRimMat); s_sharedRimMat = null; }
        if (s_sharedGazeMat != null) { Destroy(s_sharedGazeMat); s_sharedGazeMat = null; }
        if (s_sharedDisplayMat != null) { Destroy(s_sharedDisplayMat); s_sharedDisplayMat = null; }
        if (s_sharedPrepLikedMat != null) { Destroy(s_sharedPrepLikedMat); s_sharedPrepLikedMat = null; }
        if (s_sharedPrepDislikedMat != null) { Destroy(s_sharedPrepDislikedMat); s_sharedPrepDislikedMat = null; }
        // Don't destroy interact mat — it uses a different shader unrelated to style
    }

    private void EnsureSharedMaterials()
    {
        // Hover — warm ivory, strongest
        if (s_sharedRimMat == null)
        {
            s_sharedRimMat = MakeMatForStyle(HoverColor, 0.5f, 2f, 0.1f);
            if (s_sharedRimMat == null)
                Debug.LogWarning("[InteractableHighlight] Highlight shader not found for current style.");
        }

        // Gaze — teal (bottles during drink phase — must be very visible)
        if (s_sharedGazeMat == null)
            s_sharedGazeMat = MakeMatForStyle(GazeColor, 0.8f, 2.5f, 0.3f);

        // Display — bright blue (glasses during drink phase — must be very visible)
        if (s_sharedDisplayMat == null)
            s_sharedDisplayMat = MakeMatForStyle(DisplayColor, 0.8f, 2.5f, 0.3f);

        // Prep liked — green
        if (s_sharedPrepLikedMat == null)
            s_sharedPrepLikedMat = MakeMatForStyle(PrepLikedColor, 0.7f, 2f, 0.2f);

        // Prep disliked — red
        if (s_sharedPrepDislikedMat == null)
            s_sharedPrepDislikedMat = MakeMatForStyle(PrepDislikedColor, 0.4f, 1.5f, 0.08f);

        // Interact — general-purpose highlight overlay (fresnel + pulse)
        if (s_sharedInteractMat == null)
        {
            var shader = s_cachedInteractShader;
            if (shader != null)
            {
                s_sharedInteractMat = new Material(shader);
                s_sharedInteractMat.SetColor("_Color", new Color(1f, 0.95f, 0.85f, 0.3f));
                s_sharedInteractMat.SetFloat("_FresnelPower", 2f);
                s_sharedInteractMat.SetFloat("_FresnelMin", 0.3f);
                s_sharedInteractMat.SetFloat("_PulseSpeed", 1.5f);
                s_sharedInteractMat.SetFloat("_PulseAmount", 0.3f);
            }
        }
    }
}
