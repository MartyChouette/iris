using UnityEngine;
using Obi;

[DisallowMultipleComponent]
public class Leaf : MonoBehaviour, LeafTarget   // ? implement LeafTarget so NewLeafGrabber can drive it
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

    bool _torn;
    float _lastCollisionBurstTime = -999f;

    // ????????? LeafTarget interface support ?????????
    Vector3 _grabPlaneNormal;
    Vector3 _handWorld;
    bool _externallyGrabbed;

    void Reset()
    {
        if (!rb) rb = GetComponent<Rigidbody>();
        if (!follower) follower = GetComponent<RopeLeafFollower>();
    }

    void OnEnable()
    {
        _torn = false;
        _externallyGrabbed = false;

        if (rb)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;          // ? fix
            rb.angularVelocity = Vector3.zero;   // ? fix
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
        if (rb)
        {
            if (TryGetComponent(out MeshCollider mc) && !mc.convex) mc.convex = true;
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.AddForce(Random.onUnitSphere * 0.6f, ForceMode.Impulse);
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

        // auto-return to pool after a short while
        Invoke(nameof(ReturnToPool), 2.5f);
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

        // while attached, let the player �stretch� the leaf:
        if (!_torn && follower) follower.enabled = false;
        // physics stays kinematic until torn; we drive transform directly
    }

    public void SetExternalHand(Vector3 handWorld)
    {
        _handWorld = handWorld;

        if (!_torn)
        {
            // show stretch while still attached
            transform.position = _handWorld;
        }
        else
        {
            // post-tear: if you want it to stay in hand, you could also set position here
            // (your grabber currently releases right after TearOff, so this is fine)
        }
    }

    public void EndExternalGrab()
    {
        _externallyGrabbed = false;

        if (!_torn)
        {
            // snap back to following if it wasn't torn
            if (follower) follower.enabled = true;

            // zero physics just in case
            if (rb)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
        else
        {
            // already dynamic
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
