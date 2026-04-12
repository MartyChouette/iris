using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marker component placed on each plant pot in the apartment scene.
/// WateringManager and ObjectGrabber use the static registry to find
/// nearby plants for the watering can magnetic snap.
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
}
