using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marker component placed on each plant pot in the apartment scene.
/// WateringManager and ObjectGrabber use the static registry to find
/// nearby plants for the watering can magnetic snap.
/// Also stores the persistent water level for the watering system.
/// </summary>
public class WaterablePlant : MonoBehaviour
{
    // ── Static registry (avoids FindObjectsByType) ──
    private static readonly List<WaterablePlant> s_all = new();
    public static IReadOnlyList<WaterablePlant> All => s_all;

    private void OnEnable() => s_all.Add(this);
    private void OnDisable() => s_all.Remove(this);

    [Tooltip("Which plant definition this pot uses.")]
    public PlantDefinition definition;

    [Tooltip("Current water level (0-1). Persists between watering sessions.")]
    [SerializeField] private float _waterLevel;

    /// <summary>Current water level (0-1). Persists across sessions within a day.</summary>
    public float WaterLevel
    {
        get => _waterLevel;
        set => _waterLevel = Mathf.Clamp01(value);
    }
}
