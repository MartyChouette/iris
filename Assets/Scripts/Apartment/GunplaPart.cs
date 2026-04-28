using UnityEngine;

/// <summary>
/// A collectible Gunpla accessory (Sword or Wings). Standalone PlaceableObject
/// until snapped onto a GunplaBody. ObjectGrabber detects proximity to GunplaBody
/// and magnetically snaps the part, then parents it to the correct attach point.
/// </summary>
public class GunplaPart : MonoBehaviour
{
    public enum PartType { Sword, Wings }

    [Header("Part")]
    [Tooltip("Which part this is.")]
    [SerializeField] private PartType _partType;

    public PartType Type => _partType;
    public bool IsAttached { get; private set; }

    /// <summary>Attach this part to a body. Disables physics, parents to attach point.</summary>
    public void AttachTo(Transform attachPoint)
    {
        if (IsAttached) return;
        IsAttached = true;

        // Disable standalone behavior
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        var col = GetComponent<Collider>();
        if (col == null) col = GetComponentInChildren<Collider>();
        if (col != null) col.enabled = false;

        var po = GetComponent<PlaceableObject>();
        if (po != null)
        {
            po.SetRenderOnTop(false);
            po.enabled = false;
        }

        // Parent to attach point and zero out local transform
        transform.SetParent(attachPoint, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        Debug.Log($"[GunplaPart] {_partType} attached.");
    }

    /// <summary>Detach from body (for reset/debug).</summary>
    public void Detach()
    {
        if (!IsAttached) return;
        IsAttached = false;

        transform.SetParent(null, true);

        var rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = false;

        var col = GetComponent<Collider>();
        if (col == null) col = GetComponentInChildren<Collider>();
        if (col != null) col.enabled = true;

        var po = GetComponent<PlaceableObject>();
        if (po != null) po.enabled = true;
    }
}
