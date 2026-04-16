using UnityEngine;

/// <summary>
/// The physical vinyl disc that can be grabbed, carried, and placed on the turntable.
/// Starts as a child of its AlbumSleeve. Detached on grab, re-attached on return.
/// Carries the RecordDefinition reference and applies label color.
/// </summary>
public class VinylDisc : MonoBehaviour
{
    [Header("Visuals")]
    [Tooltip("Renderer for the center label (color set from RecordDefinition.labelColor).")]
    [SerializeField] private Renderer _labelRenderer;

    /// <summary>The record data (cached from parent AlbumSleeve on Awake).</summary>
    public RecordDefinition Definition { get; private set; }

    /// <summary>The sleeve this vinyl belongs to (for return-to-sleeve flow).</summary>
    public AlbumSleeve HomeSleeve { get; private set; }

    private Material _labelMat;

    private void Awake()
    {
        // Cache definition from parent AlbumSleeve
        var sleeve = GetComponentInParent<AlbumSleeve>();
        if (sleeve != null)
        {
            HomeSleeve = sleeve;
            Definition = sleeve.Definition;
        }

        ApplyLabelColor();
    }

    private void OnDestroy()
    {
        if (_labelMat != null)
            Destroy(_labelMat);
    }

    private void ApplyLabelColor()
    {
        if (_labelRenderer == null || Definition == null) return;

        _labelMat = new Material(_labelRenderer.sharedMaterial);
        _labelMat.color = Definition.labelColor;
        _labelRenderer.material = _labelMat;
    }

    /// <summary>Prepare for grab: unparent from sleeve, enable physics + colliders.</summary>
    public void ConfigureForGrab()
    {
        transform.SetParent(null, true);

        // Ensure Rigidbody exists (might not if vinyl was a static child)
        var rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.useGravity = false;

        // Ensure at least one collider is present and enabled
        var cols = GetComponents<Collider>();
        if (cols.Length == 0)
        {
            var box = gameObject.AddComponent<BoxCollider>();
            var rend = GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                box.center = transform.InverseTransformPoint(rend.bounds.center);
                box.size = transform.InverseTransformVector(rend.bounds.size);
            }
        }
        else
        {
            foreach (var col in cols)
                col.enabled = true;
        }

        // Ensure PlaceableObject exists for ObjectGrabber
        var po = GetComponent<PlaceableObject>();
        if (po == null) po = gameObject.AddComponent<PlaceableObject>();
        if (!po.enabled) po.enabled = true;
    }

    /// <summary>Prepare for turntable: disable physics, keep colliders for click detection.</summary>
    public void ConfigureForTurntable()
    {
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    /// <summary>Prepare for return to sleeve: disable physics + colliders.</summary>
    public void ConfigureForSleeve()
    {
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        foreach (var col in GetComponents<Collider>())
            col.enabled = false;
    }
}
