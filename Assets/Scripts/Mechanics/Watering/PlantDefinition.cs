using UnityEngine;

/// <summary>
/// Defines a single potted plant for the watering prototype.
/// Controls water needs, foam behaviour, and scoring thresholds.
/// </summary>
[CreateAssetMenu(menuName = "Iris/Plant Definition")]
public class PlantDefinition : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Display name shown in HUD.")]
    public string plantName = "Plant";

    [Tooltip("Flavour text shown when browsing.")]
    [TextArea(1, 3)]
    public string description = "";

    [Header("Appearance")]
    [Tooltip("Tint for the pot mesh.")]
    public Color potColor = new Color(0.72f, 0.45f, 0.20f);

    [Tooltip("Soil colour when dry.")]
    public Color dryColor = new Color(0.55f, 0.40f, 0.25f);

    [Tooltip("Soil colour when fully saturated.")]
    public Color wetColor = new Color(0.30f, 0.22f, 0.12f);

    [Tooltip("Bubbly foam tint when dirt foams up.")]
    public Color foamColor = new Color(0.50f, 0.38f, 0.22f, 0.7f);

    [Tooltip("Stem/leaf tint for the plant visual on the shelf.")]
    public Color plantColor = new Color(0.2f, 0.6f, 0.2f);

    [Header("Watering")]
    [Tooltip("Target fill level (0-1). The fill line sits here.")]
    [Range(0f, 1f)]
    public float idealWaterLevel = 0.7f;

    [Tooltip("Tolerance around ideal level for a perfect score.")]
    [Range(0.01f, 0.2f)]
    public float waterTolerance = 0.08f;

    [Tooltip("Water units per second while pouring.")]
    public float pourRate = 0.12f;

    [Tooltip("Foam rises this many times faster than water (dirt bubbles up).")]
    public float foamRateMultiplier = 1.3f;

    [Tooltip("(Legacy — unused. Target line is now fixed at idealWaterLevel.)")]
    [HideInInspector] public float targetOscSpeed = 0f;

    [Tooltip("(Legacy — unused. Target line is now fixed at idealWaterLevel.)")]
    [HideInInspector] public float targetOscAmplitude = 0f;

    [Tooltip("Units/sec foam collapses toward water level when not pouring.")]
    public float foamSettleRate = 0.25f;

    [Tooltip("Units/sec water absorbs into soil (reduces visible foam).")]
    public float absorptionRate = 0.05f;

    [Header("Scoring")]
    [Tooltip("Maximum possible score for this plant.")]
    public int baseScore = 100;

    // ── Pot Shape & Persistence ─────────────────────────────────────

    public enum PotShape
    {
        [InspectorName("Tall Cylinder (steady even fill)")]
        TallCylinder,
        [InspectorName("Round Bulb (fast bottom, slow middle, fast top)")]
        RoundBulb,
        [InspectorName("Tapered Cone (slow bottom, fast top)")]
        TaperedCone,
        [InspectorName("Hourglass (fast-slow-fast pinch)")]
        Hourglass
    }

    [Header("Pot Shape")]
    [Tooltip("Shape of the vase cross-section. Affects fill rate curve and UI silhouette.")]
    public PotShape potShape = PotShape.TallCylinder;

    [Header("Water Persistence")]
    [Tooltip("Fraction of water lost overnight (before weather multiplier). 0.15 = loses 15%.")]
    [Range(0f, 0.5f)]
    public float dryingRatePerDay = 0.15f;

    [Tooltip("How much water this plant ideally needs (maps to idealWaterLevel for display).")]
    [Range(0f, 1f)]
    public float thirstLevel = 0.7f;

    [Tooltip("Chance of shedding a leaf when under/overwatered (per morning check).")]
    [Range(0f, 1f)]
    public float leafSheddingChance = 0.5f;

    /// <summary>
    /// Fill rate multiplier based on the vase's cross-sectional width at a
    /// given height. Narrow sections fill fast, wide sections fill slow.
    /// This creates the "volume shift" feel as water rises through the shape.
    /// </summary>
    public float GetShapeFillMultiplier(float normalizedHeight)
    {
        float h = Mathf.Clamp01(normalizedHeight);
        switch (potShape)
        {
            case PotShape.TallCylinder:
                // Consistent width → steady fill rate
                return 1.0f;

            case PotShape.RoundBulb:
                // Wide belly at 0.4-0.6, narrow neck at top and bottom
                // sin curve: narrow at 0 and 1, widest at 0.5
                float bulbWidth = 0.5f + 0.5f * Mathf.Sin(h * Mathf.PI);
                return 1.0f / Mathf.Max(bulbWidth, 0.3f); // invert: wide = slow, narrow = fast

            case PotShape.TaperedCone:
                // Wide bottom → narrow top. Starts slow, fills faster as it rises.
                float coneWidth = Mathf.Lerp(1.4f, 0.5f, h);
                return 1.0f / Mathf.Max(coneWidth, 0.3f);

            case PotShape.Hourglass:
                // Narrow pinch at 0.5, wide at top and bottom
                // cos curve: wide at 0 and 1, narrowest at 0.5
                float hourglassWidth = 0.5f + 0.5f * Mathf.Cos(h * Mathf.PI);
                hourglassWidth = Mathf.Max(hourglassWidth, 0.35f); // don't go too narrow at pinch
                return 1.0f / hourglassWidth;

            default:
                return 1.0f;
        }
    }

    /// <summary>
    /// Normalized width of the vase at a given height (0-1). Used by the UI
    /// to draw the vase silhouette shape.
    /// </summary>
    public float GetShapeWidth(float normalizedHeight)
    {
        float h = Mathf.Clamp01(normalizedHeight);
        switch (potShape)
        {
            case PotShape.TallCylinder:
                return 1.0f;

            case PotShape.RoundBulb:
                return 0.5f + 0.5f * Mathf.Sin(h * Mathf.PI);

            case PotShape.TaperedCone:
                return Mathf.Lerp(1.3f, 0.6f, h);

            case PotShape.Hourglass:
                return 0.5f + 0.5f * Mathf.Cos(h * Mathf.PI);

            default:
                return 1.0f;
        }
    }
}
