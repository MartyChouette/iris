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

    [Tooltip("Optional counter transform where fridge bottles land during drink-making (Phase 2). Outside of Phase 2, bottles return to their original fridge position.")]
    [SerializeField] private Transform _counterHome;

    /// <summary>Where the bottle returns when released during drink-making.</summary>
    public Vector3 HomePosition { get; private set; }
    public Quaternion HomeRotation { get; private set; }
    public bool HasHome { get; private set; }

    // Original position (fridge shelf) — restored after Phase 2
    private Vector3 _originalHome;
    private Quaternion _originalHomeRot;

    private void Awake()
    {
        _originalHome = transform.position;
        _originalHomeRot = transform.rotation;
        HomePosition = _originalHome;
        HomeRotation = _originalHomeRot;
        HasHome = true;
    }

    /// <summary>
    /// Switch to counter home for drink-making. Call at Phase 2 start.
    /// If no _counterHome is assigned, home stays at the original position.
    /// </summary>
    public void UseCounterHome()
    {
        if (_counterHome != null)
        {
            HomePosition = _counterHome.position;
            HomeRotation = _counterHome.rotation;
        }
    }

    /// <summary>
    /// Restore original home (fridge shelf). Call when Phase 2 ends.
    /// </summary>
    public void UseOriginalHome()
    {
        HomePosition = _originalHome;
        HomeRotation = _originalHomeRot;
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
        {
            po.SetRenderOnTop(false);
            po.OnDropped(); // resets state to Resting so bottle can be picked up again
        }
    }
}
