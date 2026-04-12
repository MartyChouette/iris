using UnityEngine;

/// <summary>
/// Defines a drink recipe — which glass, which ingredients in what portions,
/// whether stirring is required, and scoring parameters.
/// </summary>
[CreateAssetMenu(menuName = "Iris/Drink Recipe")]
public class DrinkRecipeDefinition : ScriptableObject
{
    [Header("Recipe")]
    [Tooltip("Display name — 'Gin & Tonic', 'Whisky Neat', etc.")]
    public string drinkName;

    [Tooltip("Which glass type to use for this drink.")]
    public GlassDefinition requiredGlass;

    [Tooltip("Ingredients in pour order.")]
    public DrinkIngredientDefinition[] ingredients;

    [Tooltip("Normalised portion of each ingredient (should sum to ~1.0).")]
    public float[] portionNormalized;

    [Header("Stir")]
    [Tooltip("Does this drink need stirring after pouring?")]
    public bool requiresStir;

    [Tooltip("Seconds of sustained good circular motion required.")]
    public float stirDuration = 2f;

    [Tooltip("Minimum angular speed (rad/sec) for 'good' stir.")]
    public float perfectStirSpeedMin = 1.5f;

    [Tooltip("Maximum angular speed before 'too fast'.")]
    public float perfectStirSpeedMax = 4f;

    [Header("Scoring")]
    [Tooltip("Maximum achievable score for perfect execution.")]
    public int baseScore = 100;

    [Header("Simple Pour")]
    [Tooltip("Target fill level for perfect score.")]
    [Range(0f, 1f)]
    public float idealFillLevel = 0.75f;

    [Tooltip("Tolerance around ideal for full score.")]
    [Range(0.01f, 0.3f)]
    public float fillTolerance = 0.10f;

    [Tooltip("Fill units per second while pouring.")]
    public float pourRate = 0.15f;

    [Tooltip("Foam rises this many times faster than liquid.")]
    public float foamRateMultiplier = 1.3f;

    [Tooltip("Oscillations per second for the moving target line.")]
    public float targetOscSpeed = 0.4f;

    [Tooltip("How far the target swings above/below idealFillLevel.")]
    public float targetOscAmplitude = 0.12f;

    [Tooltip("Units/sec foam settles when not pouring.")]
    public float foamSettleRate = 0.25f;

    [Tooltip("Liquid colour for the glass visual and coffee table delivery.")]
    public Color liquidColor = new Color(0.8f, 0.6f, 0.3f, 0.8f);

    [Header("Layer Accuracy")]
    [Tooltip("How close each ingredient's poured amount must be to its target portion (0.05 = ±5%).")]
    [Range(0.01f, 0.2f)]
    public float portionTolerance = 0.08f;

    [Header("Garnishes")]
    [Tooltip("Correct garnishes for this recipe. Player drops them on the glass for bonus points.")]
    public DrinkGarnishDefinition[] garnishes;
}
