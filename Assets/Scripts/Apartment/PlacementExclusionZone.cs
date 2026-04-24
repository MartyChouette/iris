using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks a box-shaped area where items cannot be placed.
/// No collider needed — purely logical bounds check used by ObjectGrabber.
/// Position and rotate the GameObject, set the size to cover the blocked area.
/// </summary>
public class PlacementExclusionZone : MonoBehaviour
{
    [Tooltip("Half-extents of the exclusion box in local space.")]
    [SerializeField] private Vector3 _halfExtents = new Vector3(0.5f, 0.1f, 0.5f);

    [Tooltip("Ignore height — block the full column above/below the zone (good for floor zones).")]
    [SerializeField] private bool _ignoreHeight = true;

    private static readonly List<PlacementExclusionZone> s_all = new();
    public static IReadOnlyList<PlacementExclusionZone> All => s_all;

    private void OnEnable() => s_all.Add(this);
    private void OnDisable() => s_all.Remove(this);

    /// <summary>True if a world position falls inside this exclusion zone.</summary>
    public bool Contains(Vector3 worldPos)
    {
        Vector3 local = transform.InverseTransformPoint(worldPos);
        return Mathf.Abs(local.x) <= _halfExtents.x
            && (_ignoreHeight || Mathf.Abs(local.y) <= _halfExtents.y)
            && Mathf.Abs(local.z) <= _halfExtents.z;
    }

    /// <summary>Check all active exclusion zones.</summary>
    public static bool IsExcluded(Vector3 worldPos)
    {
        for (int i = 0; i < s_all.Count; i++)
        {
            if (s_all[i].Contains(worldPos))
            {
                Debug.Log($"[ExclusionZone] BLOCKED at {worldPos} by '{s_all[i].name}'");
                return true;
            }
        }
        return false;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.3f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(Vector3.zero, _halfExtents * 2f);
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);
        Gizmos.DrawWireCube(Vector3.zero, _halfExtents * 2f);
    }
}
