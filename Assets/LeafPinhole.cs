using UnityEngine;
using Obi;

/// Marker component for where leaves attach on the rope.
/// Add this to empty "pinhole" transforms placed near rope particles.
[DisallowMultipleComponent]
public class LeafPinhole : MonoBehaviour
{
    [Tooltip("Optional: set if you want to restrict leaves to a specific rope.")]
    public ObiRopeBase rope;
}