using UnityEngine;
using Obi;

[DisallowMultipleComponent]
public class Leaf : MonoBehaviour
{
    [Header("External refs")]
    public SapFxPool sapPool;
    public LeafPool leafPool;

    [Header("Attach")]
    public RopeLeafFollower follower;   // required (on same GO)
    public Rigidbody rb;                // on the leaf

    [Header("FX")]
    public float sapLifetime = 1.25f;

    bool _torn;

    void Reset()
    {
        if (!rb) rb = GetComponent<Rigidbody>();
        if (!follower) follower = GetComponent<RopeLeafFollower>();
    }

    void OnEnable()
    {
        _torn = false;
        if (rb) { rb.isKinematic = true; rb.useGravity = false; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        if (follower) follower.enabled = true;
    }

    public void TearOff()
    {
        if (_torn) return;
        _torn = true;

        // stop following & enable physics
        if (follower) follower.enabled = false;
        if (rb)
        {
            // convex for safety when dynamic
            if (TryGetComponent(out MeshCollider mc) && !mc.convex) mc.convex = true;
            rb.isKinematic = false; rb.useGravity = true;
            rb.AddForce(Random.onUnitSphere * 0.6f, ForceMode.Impulse);
        }

        // spawn sap at the bound particle position if we can get it:
        Vector3 pos = transform.position, nrm = Vector3.up;
        var rope = follower ? follower.rope : null;
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
}
