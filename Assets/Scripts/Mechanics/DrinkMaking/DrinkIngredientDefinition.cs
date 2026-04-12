using UnityEngine;

/// <summary>
/// Defines a single drink ingredient (spirit, mixer, etc.) with pour behaviour
/// and fizz properties that drive the <see cref="GlassController"/> simulation.
/// </summary>
[CreateAssetMenu(menuName = "Iris/Drink Ingredient")]
public class DrinkIngredientDefinition : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Display name shown in HUD and recipe card.")]
    public string ingredientName;

    [Tooltip("Visual colour when poured into the glass.")]
    public Color liquidColor = new Color(0.8f, 0.8f, 0.6f);

    [Header("Pour Behavior")]
    [Tooltip("Liquid fill units per second while pouring (0-1 scale of glass capacity).")]
    public float pourRate = 0.15f;

    [Tooltip("0 = still (milk), 1 = very fizzy (champagne). Drives foam generation.")]
    [Range(0f, 1f)]
    public float fizziness;

    [Tooltip("Multiplier for how much faster foam rises vs liquid on fizzy drinks (2-3x typical).")]
    public float foamRateMultiplier = 1f;

    [Tooltip("How fast foam decays toward liquid level when not pouring (units/sec).")]
    public float foamSettleRate = 0.3f;

    [Tooltip("Carbonated drinks lose fizz over time if poured too slowly (0 = doesn't flatten).")]
    public float flattenRate;

    [Header("Mixing")]
    [Tooltip("Affects stir difficulty — thick (milk ~0.8) vs thin (gin ~0.3).")]
    [Range(0f, 1f)]
    public float viscosity = 0.5f;

    [Header("Layering")]
    [Tooltip("Weight for layer sorting. Heavier liquids (1.0 = grenadine) sink below lighter ones (0.1 = cream).")]
    [Range(0f, 1f)]
    public float weight = 0.5f;
}
