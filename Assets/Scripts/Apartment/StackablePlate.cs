using UnityEngine;

/// <summary>
/// Marker + grab helper for plates. Stacking is now handled exclusively by
/// PairableItem (AnyOfCategory mode); this component only tracks parent-stack
/// state for grab/release and gives DishDropZone something to detect on.
/// Attach alongside PlaceableObject + PairableItem + Rigidbody + Collider.
/// </summary>
public class StackablePlate : MonoBehaviour
{
    /// <summary>The plate directly below this one in a stack (null if free/bottom).</summary>
    public StackablePlate ParentPlate { get; private set; }

    private Rigidbody _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    /// <summary>
    /// Called before ObjectGrabber picks this plate up.
    /// Detaches from parent stack, re-enables physics. Children above stay attached.
    /// </summary>
    public void PrepareForGrab()
    {
        if (ParentPlate != null)
        {
            transform.SetParent(null);
            ParentPlate = null;
        }

        if (_rb != null)
            _rb.isKinematic = false;
    }
}
