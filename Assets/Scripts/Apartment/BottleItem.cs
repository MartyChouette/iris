using UnityEngine;

/// <summary>
/// Marker component on a grabbable drink bottle. ObjectGrabber detects
/// this for magnetic snap toward DrinkGlass objects. References the
/// ingredient definition for pour rate, color, and weight.
/// During Phase 2 drink-making, bottles return to their home position
/// when released instead of being placed on surfaces.
/// </summary>
public class BottleItem : MonoBehaviour
{
    [Tooltip("Which ingredient this bottle contains.")]
    [SerializeField] private DrinkIngredientDefinition _ingredient;

    [Tooltip("Transform at the bottle's pour spout (where liquid comes from).")]
    [SerializeField] private Transform _pourPoint;

    public DrinkIngredientDefinition Ingredient => _ingredient;
    public Transform PourPoint => _pourPoint != null ? _pourPoint : transform;

    /// <summary>Captured on Awake — bottle returns here when released during drink-making.</summary>
    public Vector3 HomePosition { get; private set; }
    public Quaternion HomeRotation { get; private set; }
    public bool HasHome { get; private set; }

    private void Awake()
    {
        HomePosition = transform.position;
        HomeRotation = transform.rotation;
        HasHome = true;
    }

    /// <summary>Snap the bottle back to its starting position and disable physics.</summary>
    public void ReturnHome()
    {
        transform.position = HomePosition;
        transform.rotation = HomeRotation;

        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Re-enable colliders for future pickup
        foreach (var col in GetComponentsInChildren<Collider>())
            col.enabled = true;

        var po = GetComponent<PlaceableObject>();
        if (po != null)
            po.ForceRestoreMaterial();
    }
}
