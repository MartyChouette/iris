using UnityEngine;

/// <summary>
/// Defines a single authored mess that can appear in the apartment.
/// Each mess implies a story — tied to date outcomes, off-screen events, or daily chaos.
/// Supports both stains (wipe to clean) and objects (pick up / throw away).
/// </summary>
[CreateAssetMenu(menuName = "Iris/Mess Blueprint")]
public class MessBlueprint : ScriptableObject
{
    public enum MessCategory { DateAftermath, OffScreen, General }
    public enum MessType { Stain, Object }

    [Header("Identity")]
    [Tooltip("Human-readable name for this mess.")]
    public string messName = "Unnamed Mess";

    [TextArea(2, 4)]
    [Tooltip("Flavor text shown via PickupDescriptionHUD when interacted with.")]
    public string description = "";

    [Header("Classification")]
    [Tooltip("What triggers this mess to appear.")]
    public MessCategory category;

    [Tooltip("Whether this mess is a stain (wipeable) or an object (pickable).")]
    public MessType messType;

    [Header("Stain Settings (if MessType == Stain)")]
    [Tooltip("SpillDefinition to assign to a CleanableSurface slot.")]
    public SpillDefinition spillDefinition;

    [Header("Object Settings (if MessType == Object)")]
    [Tooltip("Prefab to instantiate. Null = use procedural box.")]
    public GameObject objectPrefab;

    [Tooltip("Scale of the procedural box (used when objectPrefab is null).")]
    public Vector3 objectScale = Vector3.one * 0.1f;

    [Tooltip("Color of the procedural box (used when objectPrefab is null).")]
    public Color objectColor = Color.gray;

    [Header("Conditions")]
    [Tooltip("If DateAftermath: require last date succeeded?")]
    public bool requireDateSuccess;

    [Tooltip("If DateAftermath: require last date failed?")]
    public bool requireDateFailure;

    [Tooltip("Minimum affection from last date (0 = no minimum).")]
    public float minAffection;

    [Tooltip("Maximum affection from last date (100 = no max).")]
    public float maxAffection = 100f;

    [Tooltip("Require a specific reaction item tag (e.g. 'wine' if drink was served).")]
    public string requireReactionTag = "";

    [Tooltip("Only eligible if last flower trim score < 40.")]
    public bool requireBadFlowerTrim;

    [Tooltip("Only eligible if last flower trim score >= 80.")]
    public bool requireGoodFlowerTrim;

    [Tooltip("Minimum day number for this mess to appear (1 = first day).")]
    public int minDay = 1;

    [Tooltip("Maximum day number for this mess to appear (0 = no limit). Use to restrict messes to specific days.")]
    public int maxDay = 0;

    [Header("Placement")]
    [Tooltip("Exact world position to spawn this mess. Each blueprint owns its location.")]
    public Vector3 spawnPosition;

    [Tooltip("Spawn rotation (Euler angles).")]
    public Vector3 spawnRotation;

    [Tooltip("Which area(s) this mess can spawn in (used by TidyScorer, not placement).")]
    public ApartmentArea[] allowedAreas;

    [Tooltip("Weight for random selection within the eligible pool.")]
    [Range(0.1f, 5f)]
    public float weight = 1f;
}
