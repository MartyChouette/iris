using UnityEngine;

/// <summary>
/// Defines a glass shape used in the drink-making prototype.
/// Capacity, fill-line position, and foam headroom drive the
/// <see cref="GlassController"/> scoring and overflow logic.
/// </summary>
[CreateAssetMenu(menuName = "Iris/Glass Type")]
public class GlassDefinition : ScriptableObject
{
    [Header("Dimensions")]
    [Tooltip("Human-readable name (Shot Glass, Rocks Glass, Highball).")]
    public string glassName;

    [Tooltip("Relative capacity — shot ~0.3, rocks ~0.7, tall ~1.0.")]
    public float capacity = 1f;

    [Tooltip("Where liquid starts filling (0 for tumblers, ~0.4 for wine glasses with stems).")]
    [Range(0f, 1f)]
    public float fillStartNormalized = 0f;

    [Tooltip("Where the perfect fill line sits (0-1 of glass height).")]
    [Range(0f, 1f)]
    public float fillLineNormalized = 0.8f;

    [Tooltip("Tolerance either side of the fill line for a 'perfect' pour.")]
    public float fillLineTolerance = 0.05f;

    [Header("Foam")]
    [Tooltip("Space above the fill line before overflow (foam buffer, 0-1).")]
    public float foamHeadroom = 0.2f;

    [Header("Visual")]
    [Tooltip("Glass visual height in world units.")]
    public float worldHeight = 0.12f;

    [Tooltip("Glass visual radius in world units.")]
    public float worldRadius = 0.025f;

    [Tooltip("Glass tint colour (clear, amber, etc.).")]
    public Color glassColor = new Color(0.9f, 0.95f, 1f, 0.3f);

    [Header("Pour")]
    [Tooltip("Fill rate multiplier — narrow glasses (highball=1.5) fill faster than wide ones (wine=0.7).")]
    public float fillRateMultiplier = 1f;
}
