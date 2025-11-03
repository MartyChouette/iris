using UnityEngine;

/// Tag a pinhole with the rope actor-space particle index it represents.
[DisallowMultipleComponent]
public class PinholeMarker : MonoBehaviour
{
    public int actorIndex;
}