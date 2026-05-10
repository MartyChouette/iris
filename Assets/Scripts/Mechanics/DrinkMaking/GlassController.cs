using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Core liquid/foam simulation for the drink-making prototype.
/// Tracks fill level, foam level, blended colour, and overflow state.
/// Called by <see cref="DrinkMakingManager"/> during the pouring phase.
/// </summary>
[DisallowMultipleComponent]
public class GlassController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Glass shape definition (capacity, fill line, foam headroom).")]
    public GlassDefinition definition;

    [Tooltip("Renderer for the liquid visual inside the glass.")]
    public Renderer liquidRenderer;

    [Tooltip("Renderer for the foam layer on top of the liquid.")]
    public Renderer foamRenderer;

    [Tooltip("Small marker showing the target fill height.")]
    public Transform fillLineMarker;

    [Tooltip("Transform scaled to show liquid height.")]
    public Transform liquidTransform;

    [Tooltip("Transform scaled/positioned to show foam height.")]
    public Transform foamTransform;

    [Header("State (Read-Only)")]
    [SerializeField] private float _liquidLevel;
    [SerializeField] private float _foamLevel;
    [SerializeField] private Color _currentColor = Color.clear;
    [SerializeField] private bool _overflowed;

    // Internal
    private float _rushPenalty;
    private float _totalPoured;
    private readonly List<(DrinkIngredientDefinition ingredient, float amount)> _ingredients
        = new List<(DrinkIngredientDefinition, float)>();

    // Glow overlay
    private Renderer _glassRenderer;
    private Material _rimMat;
    private Material[] _baseMaterials;
    private bool _glowing;

    // ── Public read-only API ────────────────────────────────────────────

    public float LiquidLevel => _liquidLevel;
    public float FoamLevel => _foamLevel;
    public bool Overflowed => _overflowed;
    public Color CurrentColor => _currentColor;

    /// <summary>
    /// How close the liquid level is to the fill line (1 = perfect, 0 = way off).
    /// </summary>
    public float FillAccuracy
    {
        get
        {
            if (definition == null) return 0f;
            float dist = Mathf.Abs(_liquidLevel - definition.fillLineNormalized);
            return Mathf.Clamp01(1f - dist / Mathf.Max(definition.fillLineTolerance, 0.001f));
        }
    }

    // ── Pour API (called by DrinkMakingManager) ────────────────────────

    /// <summary>
    /// Pour a given ingredient for one frame. Advances liquid and foam levels.
    /// </summary>
    public void Pour(DrinkIngredientDefinition ingredient, float dt)
    {
        if (definition == null || ingredient == null) return;

        float liquidDelta = ingredient.pourRate * dt;
        _liquidLevel += liquidDelta;
        _totalPoured += liquidDelta;

        // Foam rises faster for fizzy ingredients
        float foamDelta = liquidDelta * ingredient.foamRateMultiplier * (1f + ingredient.fizziness);
        foamDelta += _rushPenalty * dt;
        _foamLevel += foamDelta;

        // Rush penalty increases while continuously pouring fizzy drinks
        _rushPenalty += ingredient.fizziness * 0.1f * dt;

        // Track ingredient contribution
        AddIngredient(ingredient, liquidDelta);

        // Clamp liquid to 0-1
        _liquidLevel = Mathf.Clamp01(_liquidLevel);

        // Foam can exceed 1.0 → overflow
        if (_foamLevel > 1f)
        {
            _foamLevel = 1f;
            _overflowed = true;
        }

        // Foam never below liquid
        _foamLevel = Mathf.Max(_foamLevel, _liquidLevel);
    }

    /// <summary>
    /// Signal the end of a pour. Resets rush accumulator.
    /// </summary>
    public void StopPouring()
    {
        _rushPenalty = 0f;
    }

    /// <summary>
    /// Reset the glass to empty.
    /// </summary>
    public void Clear()
    {
        _liquidLevel = 0f;
        _foamLevel = 0f;
        _currentColor = Color.clear;
        _overflowed = false;
        _rushPenalty = 0f;
        _totalPoured = 0f;
        _ingredients.Clear();
    }

    // ── Glow API ─────────────────────────────────────────────────────

    /// <summary>Add a rim light glow to the glass shell.</summary>
    public void EnableGlow()
    {
        if (_glowing) return;
        if (_glassRenderer == null) return;

        if (_rimMat == null)
        {
            var shader = Shader.Find("Iris/Highlight");
            if (shader == null) return;
            _rimMat = new Material(shader);
            _rimMat.SetColor("_HighlightColor", new Color(0.6f, 0.9f, 1f, 0.15f));
            _rimMat.SetColor("_RimColor", new Color(0.6f, 0.9f, 1f, 0.55f));
            _rimMat.SetFloat("_RimPower", 2.5f);
            _rimMat.SetFloat("_PulseSpeed", 2f);
            _rimMat.SetFloat("_PulseAmount", 0.1f);
        }

        _baseMaterials = _glassRenderer.sharedMaterials;
        var mats = new Material[_baseMaterials.Length + 1];
        _baseMaterials.CopyTo(mats, 0);
        mats[mats.Length - 1] = _rimMat;
        _glassRenderer.materials = mats;
        _glowing = true;
    }

    /// <summary>Remove the rim light glow from the glass shell.</summary>
    public void DisableGlow()
    {
        if (!_glowing) return;
        if (_glassRenderer == null) return;

        if (_baseMaterials != null)
            _glassRenderer.materials = _baseMaterials;
        _glowing = false;
    }

    // ── MonoBehaviour ──────────────────────────────────────────────────

    void Awake()
    {
        _glassRenderer = GetComponent<Renderer>();
        // Check children if renderer not on root (common with FBX imports)
        if (_glassRenderer == null)
            _glassRenderer = GetComponentInChildren<Renderer>();
    }

    void Update()
    {
        SettleFoam(Time.deltaTime);
        UpdateVisuals();
        UpdateGlowPulse();
    }

    // ── Glow pulse (while pouring) ──────────────────────────────────────

    private bool _isPouring;

    /// <summary>Signal that liquid is actively being poured into this glass.</summary>
    public void SetPouring(bool pouring) { _isPouring = pouring; }

    private void UpdateGlowPulse()
    {
        if (!_glowing || _rimMat == null) return;

        if (_isPouring)
        {
            // Pulse the rim intensity while actively pouring
            float pulse = 1.0f + Mathf.Sin(Time.time * 4f) * 0.4f;
            _rimMat.SetFloat("_RimIntensity", pulse);
            _rimMat.SetColor("_RimColor", new Color(0.5f, 1f, 0.6f, 0.65f));
        }
        else
        {
            // Idle glow — gentle blue rim
            _rimMat.SetFloat("_RimIntensity", 1.2f);
            _rimMat.SetColor("_RimColor", new Color(0.6f, 0.9f, 1f, 0.55f));
        }
    }

    // ── Internals ──────────────────────────────────────────────────────

    private void SettleFoam(float dt)
    {
        if (_ingredients.Count == 0) return;

        // Weighted average of settle rates
        float weightedSettle = 0f;
        float totalWeight = 0f;
        for (int i = 0; i < _ingredients.Count; i++)
        {
            float w = _ingredients[i].amount;
            weightedSettle += _ingredients[i].ingredient.foamSettleRate * w;
            totalWeight += w;
        }
        if (totalWeight > 0f)
            weightedSettle /= totalWeight;

        _foamLevel = Mathf.MoveTowards(_foamLevel, _liquidLevel, weightedSettle * dt);
    }

    private void AddIngredient(DrinkIngredientDefinition ingredient, float amount)
    {
        // Find existing entry
        for (int i = 0; i < _ingredients.Count; i++)
        {
            if (_ingredients[i].ingredient == ingredient)
            {
                _ingredients[i] = (ingredient, _ingredients[i].amount + amount);
                BlendColor();
                return;
            }
        }
        _ingredients.Add((ingredient, amount));
        BlendColor();
    }

    private void BlendColor()
    {
        if (_ingredients.Count == 0)
        {
            _currentColor = Color.clear;
            return;
        }

        float r = 0f, g = 0f, b = 0f, a = 0f;
        float totalWeight = 0f;
        for (int i = 0; i < _ingredients.Count; i++)
        {
            float w = _ingredients[i].amount;
            Color c = _ingredients[i].ingredient.liquidColor;
            r += c.r * w;
            g += c.g * w;
            b += c.b * w;
            a += c.a * w;
            totalWeight += w;
        }

        if (totalWeight > 0f)
        {
            _currentColor = new Color(r / totalWeight, g / totalWeight,
                                      b / totalWeight, a / totalWeight);
        }
    }

    private void UpdateVisuals()
    {
        if (definition == null) return;

        float glassHeight = definition.worldHeight;

        // Liquid transform — scale Y from fill start (above stem for wine glasses)
        if (liquidTransform != null)
        {
            float fillStart = definition.fillStartNormalized * glassHeight;
            float fillRange = glassHeight - fillStart;
            float liquidHeight = _liquidLevel * fillRange;
            liquidTransform.localScale = new Vector3(
                liquidTransform.localScale.x,
                Mathf.Max(liquidHeight, 0.001f),
                liquidTransform.localScale.z);
            liquidTransform.localPosition = new Vector3(0f, fillStart + liquidHeight * 0.5f, 0f);
        }

        // Liquid renderer colour
        if (liquidRenderer != null)
        {
            var mpb = new MaterialPropertyBlock();
            liquidRenderer.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", _currentColor);
            liquidRenderer.SetPropertyBlock(mpb);
        }

        // Foam transform — sits on top of liquid, height = foam - liquid.
        // Fades out as foam settles so it disappears cleanly.
        if (foamTransform != null)
        {
            float fillStart = definition.fillStartNormalized * glassHeight;
            float fillRange = glassHeight - fillStart;
            float liquidHeight = fillStart + _liquidLevel * fillRange;
            float foamHeight = (_foamLevel - _liquidLevel) * fillRange;
            foamHeight = Mathf.Max(foamHeight, 0f);

            // Fade foam opacity based on how much foam remains above liquid
            float foamRatio = _liquidLevel > 0.001f ? (_foamLevel - _liquidLevel) / Mathf.Max(_liquidLevel, 0.01f) : 0f;
            float foamAlpha = Mathf.Clamp01(foamRatio * 5f); // fades out over last 20%

            if (foamAlpha < 0.01f)
            {
                foamTransform.gameObject.SetActive(false);
            }
            else
            {
                foamTransform.gameObject.SetActive(true);
                foamTransform.localScale = new Vector3(
                    foamTransform.localScale.x,
                    Mathf.Max(foamHeight, 0.001f),
                    foamTransform.localScale.z);
                foamTransform.localPosition = new Vector3(0f, liquidHeight + foamHeight * 0.5f, 0f);

                if (foamRenderer != null)
                {
                    var mpb = new MaterialPropertyBlock();
                    foamRenderer.GetPropertyBlock(mpb);
                    Color c = mpb.GetColor("_BaseColor");
                    if (c.a == 0f) c = Color.white;
                    c.a = foamAlpha;
                    mpb.SetColor("_BaseColor", c);
                    foamRenderer.SetPropertyBlock(mpb);
                }
            }
        }

        // Fill line marker
        if (fillLineMarker != null)
        {
            float fillY = definition.fillLineNormalized * glassHeight;
            fillLineMarker.localPosition = new Vector3(0f, fillY, 0f);
        }
    }
}
