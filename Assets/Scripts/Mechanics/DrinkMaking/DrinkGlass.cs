using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Physical glass in the scene (highball or wine glass). Tracks poured
/// liquid layers with colors and weights. Accepts garnishes.
/// DrinkPourManager reads layer data for scoring, DrinkCutawayUI reads
/// it for the 2D cross-section display.
///
/// Static registry allows ObjectGrabber to find the nearest glass for
/// bottle magnetic snap.
/// </summary>
public class DrinkGlass : MonoBehaviour
{
    // ── Static registry ──
    private static readonly List<DrinkGlass> s_all = new();
    public static IReadOnlyList<DrinkGlass> All => s_all;
    private void OnEnable() => s_all.Add(this);
    private void OnDisable() => s_all.Remove(this);

    [Header("Glass Type")]
    [Tooltip("Glass definition (shape, capacity, fill line).")]
    [SerializeField] private GlassDefinition _glassDefinition;

    public GlassDefinition Definition => _glassDefinition;

    // ── Liquid layers ───────────────────────────────────────────────

    [System.Serializable]
    public struct LiquidLayer
    {
        public DrinkIngredientDefinition ingredient;
        public float amount; // normalized 0-1 of glass capacity
    }

    private readonly List<LiquidLayer> _layers = new();
    private readonly List<DrinkGarnishDefinition> _garnishes = new();
    private float _foamLevel;
    private bool _overflowed;

    public IReadOnlyList<LiquidLayer> Layers => _layers;
    public IReadOnlyList<DrinkGarnishDefinition> Garnishes => _garnishes;
    public float FoamLevel => _foamLevel;
    public bool IsOverflowing => _overflowed;

    /// <summary>Total liquid in the glass (sum of all layers).</summary>
    public float TotalFill
    {
        get
        {
            float sum = 0f;
            for (int i = 0; i < _layers.Count; i++)
                sum += _layers[i].amount;
            return sum;
        }
    }

    /// <summary>Order in which ingredients were poured (first = index 0).</summary>
    public List<DrinkIngredientDefinition> PourOrder { get; } = new();

    // ── Public API ──────────────────────────────────────────────────

    /// <summary>
    /// Add liquid to the glass. If the same ingredient was the last poured,
    /// it merges into that layer. Otherwise starts a new layer.
    /// Heavier ingredients sort lower in the layer list (visual sinking).
    /// </summary>
    public void AddLiquid(DrinkIngredientDefinition ingredient, float amount)
    {
        if (ingredient == null || amount <= 0f) return;

        // Track pour order (only on first pour of each ingredient)
        if (!PourOrder.Contains(ingredient))
            PourOrder.Add(ingredient);

        // Merge into existing layer or create new one
        bool merged = false;
        for (int i = 0; i < _layers.Count; i++)
        {
            if (_layers[i].ingredient == ingredient)
            {
                var layer = _layers[i];
                layer.amount += amount;
                _layers[i] = layer;
                merged = true;
                break;
            }
        }

        if (!merged)
            _layers.Add(new LiquidLayer { ingredient = ingredient, amount = amount });

        // Sort layers by weight (heaviest at bottom = index 0)
        _layers.Sort((a, b) => b.ingredient.weight.CompareTo(a.ingredient.weight));

        // Update foam
        float foamDelta = amount * (ingredient.foamRateMultiplier > 0f ? ingredient.foamRateMultiplier : 1f);
        _foamLevel = Mathf.Max(_foamLevel, TotalFill + foamDelta * 0.3f);

        // Overflow check
        if (TotalFill > 1f || _foamLevel > 1f)
            _overflowed = true;
    }

    /// <summary>Add a garnish to the glass.</summary>
    public void AddGarnish(DrinkGarnishDefinition garnish)
    {
        if (garnish == null) return;
        if (!_garnishes.Contains(garnish))
            _garnishes.Add(garnish);
    }

    /// <summary>Settle foam toward liquid level (called each frame when not pouring).</summary>
    public void SettleFoam(float settleRate)
    {
        float fill = TotalFill;
        if (_foamLevel > fill)
        {
            _foamLevel -= settleRate * Time.deltaTime;
            _foamLevel = Mathf.Max(_foamLevel, fill);
        }
    }

    /// <summary>Reset the glass to empty.</summary>
    public void Clear()
    {
        _layers.Clear();
        _garnishes.Clear();
        PourOrder.Clear();
        _foamLevel = 0f;
        _overflowed = false;
    }

    /// <summary>
    /// Get the blended color of all layers (for 3D glass visual).
    /// Weighted by amount of each ingredient.
    /// </summary>
    public Color GetBlendedColor()
    {
        float total = TotalFill;
        if (total <= 0f) return Color.clear;

        Color blended = Color.clear;
        for (int i = 0; i < _layers.Count; i++)
        {
            float weight = _layers[i].amount / total;
            blended += _layers[i].ingredient.liquidColor * weight;
        }
        blended.a = 0.8f;
        return blended;
    }
}
