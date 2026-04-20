using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Menu tool that finds all PlaceableObjects in the scene missing a ReactableTag.
/// Iris > Audit > Missing ReactableTags
/// </summary>
public static class ReactableTagAudit
{
    [MenuItem("Iris/Audit/Missing ReactableTags")]
    private static void FindMissing()
    {
        var all = Object.FindObjectsByType<PlaceableObject>(FindObjectsSortMode.None);
        var missing = new List<PlaceableObject>();

        foreach (var po in all)
        {
            if (po.GetComponent<ReactableTag>() == null)
                missing.Add(po);
        }

        if (missing.Count == 0)
        {
            Debug.Log("[ReactableTagAudit] All PlaceableObjects have a ReactableTag.");
            return;
        }

        Debug.LogWarning($"[ReactableTagAudit] {missing.Count} PlaceableObject(s) missing ReactableTag:");
        foreach (var po in missing)
            Debug.LogWarning($"  • {po.gameObject.name}", po.gameObject);
    }
}
