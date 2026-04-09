using UnityEngine;

/// <summary>
/// Scene-scoped singleton that aggregates per-area tidiness from four signals:
///   1. Stain cleanliness (CleaningManager)
///   2. Object mess (misplaced PlaceableObjects with non-General category)
///   3. Smell (sum of ReactableTag.SmellAmount in area)
///   4. Floor clutter (items resting on the floor)
///
/// Each area score ranges 0 (filthy) to 1 (spotless).
/// Uses PlaceableObject.All static registry instead of FindObjectsByType.
/// </summary>
public class TidyScorer : MonoBehaviour
{
    public static TidyScorer Instance { get; private set; }

    [Header("Weights")]
    [Tooltip("Weight for stain cleanliness in the tidiness formula.")]
    [SerializeField] private float _stainWeight = 0.45f;

    [Tooltip("Weight for object mess in the tidiness formula.")]
    [SerializeField] private float _objectWeight = 0.25f;

    [Tooltip("Weight for smell cleanliness in the tidiness formula.")]
    [SerializeField] private float _smellWeight = 0.15f;

    [Tooltip("Weight for floor clutter in the tidiness formula.")]
    [SerializeField] private float _clutterWeight = 0.10f;

    [Header("Thresholds")]
    [Tooltip("Maximum expected mess items per area before objectClean = 0.")]
    [SerializeField] private int _maxExpectedMess = 3;

    [Tooltip("Maximum expected floor items per area before clutterClean = 0.")]
    [SerializeField] private int _maxExpectedClutter = 3;

    [Tooltip("Smell amount per area above which smellClean = 0.")]
    [SerializeField] private float _smellThreshold = 1.5f;

    [Header("Area Bounds (drag boxes in Scene View)")]
    [SerializeField] private Bounds _kitchenBounds = new Bounds(
        new Vector3(-3.5f, 1.5f, 1f), new Vector3(5f, 4f, 11f));

    [SerializeField] private Bounds _livingRoomBounds = new Bounds(
        new Vector3(2f, 1.5f, 1f), new Vector3(6f, 4f, 11f));

    [SerializeField] private Bounds _entranceBounds = new Bounds(
        new Vector3(0f, 1.5f, -6f), new Vector3(12f, 4f, 3f));

    /// <summary>Read-only access for custom editor.</summary>
    public Bounds KitchenBounds { get => _kitchenBounds; set => _kitchenBounds = value; }
    public Bounds LivingRoomBounds { get => _livingRoomBounds; set => _livingRoomBounds = value; }
    public Bounds EntranceBounds { get => _entranceBounds; set => _entranceBounds = value; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[TidyScorer] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Tidiness of a specific area (0 = filthy, 1 = spotless).</summary>
    public float GetAreaTidiness(ApartmentArea area)
    {
        float stainClean = GetStainClean(area);
        float objectClean = GetObjectClean(area);
        float smellClean = GetSmellClean(area);
        float clutterClean = GetClutterClean(area);

        return stainClean * _stainWeight
             + objectClean * _objectWeight
             + smellClean * _smellWeight
             + clutterClean * _clutterWeight;
    }

    /// <summary>Average tidiness across all areas.</summary>
    public float OverallTidiness
    {
        get
        {
            float sum = 0f;
            int count = 0;
            foreach (ApartmentArea area in System.Enum.GetValues(typeof(ApartmentArea)))
            {
                sum += GetAreaTidiness(area);
                count++;
            }
            return count > 0 ? sum / count : 1f;
        }
    }

    /// <summary>Assign an area based on an object's world position.</summary>
    public ApartmentArea ClassifyPosition(Vector3 worldPos)
    {
        if (_kitchenBounds.Contains(worldPos)) return ApartmentArea.Kitchen;
        if (_livingRoomBounds.Contains(worldPos)) return ApartmentArea.LivingRoom;
        if (_entranceBounds.Contains(worldPos)) return ApartmentArea.Entrance;

        // Fallback: nearest box
        float dK = _kitchenBounds.SqrDistance(worldPos);
        float dL = _livingRoomBounds.SqrDistance(worldPos);
        float dE = _entranceBounds.SqrDistance(worldPos);

        if (dK <= dL && dK <= dE) return ApartmentArea.Kitchen;
        if (dL <= dE) return ApartmentArea.LivingRoom;
        return ApartmentArea.Entrance;
    }

    private float GetStainClean(ApartmentArea area)
    {
        if (CleaningManager.Instance == null) return 1f;
        return CleaningManager.Instance.GetAreaCleanPercent(area);
    }

    private float GetObjectClean(ApartmentArea area)
    {
        // Weighted by each item's surface multiplier — a misplaced mug on the
        // coffee table (3x) counts three times as much mess as one tucked in a
        // closet (1x). Items hidden in closed cubbies are skipped entirely —
        // out of sight, out of mess score.
        int messCount = 0;
        var placeables = PlaceableObject.All;
        for (int i = 0; i < placeables.Count; i++)
        {
            var p = placeables[i];
            if (p.Category == ItemCategory.General) continue;
            if (p.IsAtHome) continue;
            if (p.CurrentState == PlaceableObject.State.Held) continue;
            if (p.IsHiddenInCubby) continue;
            if (ClassifyPosition(p.transform.position) == area)
                messCount += p.CurrentEffectMultiplier;
        }

        return 1f - Mathf.Clamp01((float)messCount / _maxExpectedMess);
    }

    /// <summary>Clutter cleanliness for an area: items on the floor that aren't on surfaces.</summary>
    public float GetClutterClean(ApartmentArea area)
    {
        // Floor items are by definition off-surface, so they all count as 1
        // (CurrentEffectMultiplier falls back to 1 when _lastPlacedSurface is null).
        // Belt-and-suspenders: also skip anything flagged hidden in a cubby,
        // even though floor items shouldn't ever be private.
        int floorItems = 0;
        var placeables = PlaceableObject.All;
        for (int i = 0; i < placeables.Count; i++)
        {
            var p = placeables[i];
            if (!p.IsOnFloor) continue;
            if (p.IsAtHome) continue;
            if (p.IsHiddenInCubby) continue;
            if (ClassifyPosition(p.transform.position) == area)
                floorItems += p.CurrentEffectMultiplier;
        }

        return 1f - Mathf.Clamp01((float)floorItems / _maxExpectedClutter);
    }

    /// <summary>Count of floor items in an area (for debug overlay).</summary>
    public int GetFloorItemCount(ApartmentArea area)
    {
        int count = 0;
        var placeables = PlaceableObject.All;
        for (int i = 0; i < placeables.Count; i++)
        {
            var p = placeables[i];
            if (!p.IsOnFloor) continue;
            if (p.IsAtHome) continue;
            if (ClassifyPosition(p.transform.position) == area)
                count++;
        }
        return count;
    }

    private float GetSmellClean(ApartmentArea area)
    {
        float totalSmell = 0f;
        foreach (var tag in ReactableTag.All)
        {
            if (tag.SmellAmount <= 0f) continue;
            if (ClassifyPosition(tag.transform.position) == area)
                totalSmell += tag.SmellAmount;
        }

        return 1f - Mathf.Clamp01(totalSmell / _smellThreshold);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        DrawAreaGizmo(_kitchenBounds, new Color(1f, 0.6f, 0.2f, 0.12f), new Color(1f, 0.6f, 0.2f, 0.7f), "Kitchen");
        DrawAreaGizmo(_livingRoomBounds, new Color(0.3f, 0.6f, 1f, 0.12f), new Color(0.3f, 0.6f, 1f, 0.7f), "Living Room");
        DrawAreaGizmo(_entranceBounds, new Color(0.3f, 0.9f, 0.4f, 0.12f), new Color(0.3f, 0.9f, 0.4f, 0.7f), "Entrance");
    }

    private static void DrawAreaGizmo(Bounds b, Color fill, Color wire, string label)
    {
        Gizmos.color = fill;
        Gizmos.DrawCube(b.center, b.size);
        Gizmos.color = wire;
        Gizmos.DrawWireCube(b.center, b.size);

        UnityEditor.Handles.Label(b.center + Vector3.up * (b.extents.y + 0.3f), label,
            new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = wire }
            });
    }
#endif
}
