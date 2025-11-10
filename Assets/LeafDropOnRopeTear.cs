using UnityEngine;
using Obi;

[DefaultExecutionOrder(32000)]
[DisallowMultipleComponent]
public class LeafDropOnRopeTear : MonoBehaviour
{
    [Header("Wiring")]
    public ObiRopeBase rope;                // stem rope this leaf is attached to
    [Tooltip("Actor-space particle index the leaf rides/pins to.")]
    public int actorIndex = 0;
    public Rigidbody rb;                    // auto-filled if null
    [Tooltip("Follower script that writes the leaf's pose (disable when dropping).")]
    public MonoBehaviour followerToDisable; // e.g., RopeLeafFollower_Bespoke

    [Header("Debug")]
    public bool debug = true;

    bool dropped = false;

    void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody>();
    }

    void OnEnable() { RopeTearBus.OnAnyRopeTorn += HandleTear; }
    void OnDisable() { RopeTearBus.OnAnyRopeTorn -= HandleTear; }

    void HandleTear(ObiRopeBase torn, int tearActorIndex)
    {
        if (dropped || torn != rope) return;

        // If the rope was severed ABOVE/AT this leaf (toward root=0), drop it.
        // Convention: tearActorIndex <= actorIndex -> this leaf loses its upstream path.
        if (tearActorIndex <= actorIndex)
        {
            if (debug) Debug.Log($"[LeafDrop] Tear at {tearActorIndex} <= leaf {actorIndex} → DROP {name}", this);
            DropNow();
        }
        else if (debug)
        {
            Debug.Log($"[LeafDrop] Tear at {tearActorIndex} > leaf {actorIndex} → still attached", this);
        }
    }

    public void DropNow()
    {
        if (dropped) return;
        dropped = true;

        if (followerToDisable) followerToDisable.enabled = false;

        if (rb)
        {
            rb.isKinematic = false;
            rb.detectCollisions = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }
}