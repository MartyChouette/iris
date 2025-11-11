using UnityEngine;
using Obi;

[DisallowMultipleComponent]
public class Leaf : MonoBehaviour, LeafTarget
{
    [Header("External refs")]
    public SapFxPool sapPool;
    public LeafPool leafPool;

    [Header("Attach")]
    public RopeLeafFollower follower;   // required (on same GO)
    public Rigidbody rb;                // on the leaf

    [Header("FX")]
    public float sapLifetime = 8f;

    [Header("Global Juice (optional)")]
    public JuiceSettings juiceSettings;

    [Header("Collision Burst (after detach)")]
    public bool burstOnCollision = true;
    public float collisionBurstCooldown = 0.15f;

    // ---------- Integration controls (to not interfere with 2D leaf systems) ----------
    [Header("Integration / Cooperation")]
    [Tooltip("If ON, this script manages Rigidbody states (kinematic/collisions/gravity). Turn OFF to let 2D systems manage physics.")]
    public bool manageRigidbodyStates = false;   // [ADDED]

    [Tooltip("If ON, disables RopeLeafFollower while the leaf is externally grabbed.")]
    public bool disableFollowerWhileGrabbed = false; // [ADDED]

    [Tooltip("If ON, while attached & grabbed this script drives transform.position to show stretch. Turn OFF if your 2D leaf script moves it.")]
    public bool driveTransformWhileAttached = false;  // [ADDED]

    [Tooltip("If ON, when external grab ends and the leaf wasn't torn, follower is re-enabled. Turn OFF if another system will handle it.")]
    public bool reenableFollowerIfNotTorn = false;    // [ADDED]
    // -----------------------------------------------------------------------


    bool _torn;
    float _lastCollisionBurstTime = -999f;

    // LeafTarget support
    Vector3 _grabPlaneNormal;
    Vector3 _handWorld;
    bool _externallyGrabbed;

    // Convenience for other scripts
    public bool IsTorn => _torn;                    // [ADDED]
    public bool IsExternallyGrabbed => _externallyGrabbed; // [ADDED]

    void Reset()
    {
        if (!rb) rb = GetComponent<Rigidbody>();
        if (!follower) follower = GetComponent<RopeLeafFollower>();
    }

    void OnEnable()
    {
        _torn = false;
        _externallyGrabbed = false;

        if (manageRigidbodyStates && rb) // [CHANGED] gated by toggle
        {
            rb.isKinematic = true;
            rb.detectCollisions = false;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.useGravity = false;
        }

        if (follower) follower.enabled = true;
    }

    // ============= PUBLIC: called by grabber or other systems =============
    public void TearOff()
    {
        if (_torn) return;
        _torn = true;

        // stop following & enable physics
        if (follower) follower.enabled = false;

        LeafDetachUtil.DetachAllFor(transform);

        if (rb && manageRigidbodyStates) // [CHANGED] gated by toggle
        {
            rb.isKinematic = false;
            rb.detectCollisions = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.useGravity = true; // [ADDED] ensure gravity after tear
            rb.AddForce(Random.onUnitSphere * 0.1f, ForceMode.Impulse);
        }

        // spawn sap at the rope particle this leaf was riding, if available
        Vector3 pos = transform.position, nrm = Vector3.up;
        var rope = follower ? follower.rope as ObiRopeBase : null;
        if (rope && rope.solver && rope.solverIndices != null && follower.actorIndex < rope.activeParticleCount)
        {
            int si = rope.solverIndices[follower.actorIndex];
            if (si >= 0 && si < rope.solver.renderablePositions.count)
            {
                var p4 = rope.solver.renderablePositions[si];
                pos = rope.solver.transform.TransformPoint(new Vector3(p4.x, p4.y, p4.z));
                nrm = (transform.position - pos).normalized; if (nrm.sqrMagnitude < 1e-6f) nrm = Vector3.up;
            }
        }
        if (sapPool) sapPool.Play(pos, nrm, rope ? rope.transform : null, sapLifetime);

        // NOTE: pool return is intentionally not automatic here to avoid interference.
        // If you need it, call ReturnToPool() from your 2D system at the right time.
        //Invoke(nameof(ReturnToPool), 2.5f);
    }

    void ReturnToPool()
    {
        if (leafPool) leafPool.Return(gameObject);
        else gameObject.SetActive(false); // fallback
    }

    // ============= LeafTarget (so NewLeafGrabber can drive it) =============
    public void BeginExternalGrab(Vector3 anchorWorld, Vector3 planeNormal)
    {
        _externallyGrabbed = true;
        _grabPlaneNormal = planeNormal;

        if (disableFollowerWhileGrabbed && !_torn && follower) // [CHANGED] gated by toggle
            follower.enabled = false;

        // physics stays kinematic until torn; transform drive is optional (see SetExternalHand)
    }

    public void SetExternalHand(Vector3 handWorld)
    {
        _handWorld = handWorld;

        if (!_torn)
        {
            // show stretch while still attached, only if allowed
            if (driveTransformWhileAttached) // [ADDED] cooperate with 2D mover
                transform.position = _handWorld;
        }
        else
        {
            // post-tear: let 2D system decide; don't force-move here
        }
    }

    public void EndExternalGrab()
    {
        _externallyGrabbed = false;

        if (!_torn)
        {
            // snap back to following if it wasn't torn, but only if desired
            if (reenableFollowerIfNotTorn && follower) // [CHANGED] gated by toggle
                follower.enabled = true;

            if (rb && manageRigidbodyStates) // [CHANGED] gated by toggle
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
        else
        {
            // already dynamic; let other systems take over
        }
    }

    public Vector3 GetPosition() => transform.position;

    // ============= Collision sap after detach =============
    void OnCollisionEnter(Collision c)
    {
        if (!_torn || !burstOnCollision || sapPool == null) return;

        float now = Time.time;
        if (now - _lastCollisionBurstTime < collisionBurstCooldown) return;
        _lastCollisionBurstTime = now;

        if (c.contactCount > 0)
        {
            var cp = c.GetContact(0);
            sapPool.Play(cp.point, cp.normal);
        }
        else
        {
            sapPool.Play(transform.position, Vector3.up);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (!_torn || !burstOnCollision || sapPool == null) return;

        float now = Time.time;
        if (now - _lastCollisionBurstTime < collisionBurstCooldown) return;
        _lastCollisionBurstTime = now;

        Vector3 pos = other.ClosestPoint(transform.position);
        Vector3 normal = (transform.position - pos).sqrMagnitude > 1e-6f ? (transform.position - pos).normalized : Vector3.up;
        sapPool.Play(pos, normal);
    }
}
