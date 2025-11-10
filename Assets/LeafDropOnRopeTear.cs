using UnityEngine;
using Obi;

[DefaultExecutionOrder(32000)]
public class LeafDropOnRopeTear : MonoBehaviour
{
    public ObiRopeBase rope;          // the stem rope this leaf is attached to
    public int actorIndex = 0;        // particle index the leaf rides/pins to
    public Rigidbody rb;              // optional auto-find
    public MonoBehaviour followerToDisable; // e.g., your RopeLeafFollower
    public bool debug = true;

    bool dropped = false;

    void Awake() { if (!rb) rb = GetComponent<Rigidbody>(); }

    void OnEnable() { RopeTearBus.OnAnyRopeTorn += HandleTear; }
    void OnDisable() { RopeTearBus.OnAnyRopeTorn -= HandleTear; }

    void HandleTear(ObiRopeBase tornRope, int tearIndex)
    {
        if (dropped || tornRope != rope) return;

        // severed above this leaf (toward root=0)? then drop:
        if (tearIndex <= actorIndex)
        {
            if (debug) Debug.Log($"[LeafDrop] Tear at {tearIndex} → DROP {name}", this);
            DropNow();
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