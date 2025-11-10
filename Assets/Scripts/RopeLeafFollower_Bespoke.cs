using UnityEngine;
using Obi;

[DefaultExecutionOrder(32000)]
[DisallowMultipleComponent]
public class RopeLeafFollower_Bespoke : MonoBehaviour
{
    [Header("Refs")]
    public ObiRopeBase rope;
    public Rigidbody rb;                   // kinematic while attached

    [Header("Feel")]
    public float radialOffset = 0.02f;
    public float posLerp = 24f;
    public float rotLerp = 18f;

    [Header("Rebind")]
    public bool autoRebind = true;
    public float rebindSearchRadius = 0.15f;   // how far we’re allowed to jump
    public float lostGraceSeconds = 0.15f;     // wait a moment after a cut

    int _ai = -1;
    float _lostTimer;

    void Reset()
    {
        rb = GetComponent<Rigidbody>();
        if (!rope) rope = GetComponentInParent<ObiRopeBase>();
    }

    void OnEnable() { _ai = -1; _lostTimer = 0f; }

    void LateUpdate()
    {
        // if torn (rb dynamic), stop following:
        if (rb && !rb.isKinematic) return;
        if (rope == null || rope.solver == null || rope.activeParticleCount == 0) return;

        // bind or rebind to nearest actor:
        if (_ai < 0 || _lostTimer > 0f)
        {
            int nearest = ObiFollowerUtil.FindNearestActorIndex(rope, transform.position, rebindSearchRadius);
            if (nearest >= 0) _ai = nearest;
        }

        if (_ai < 0) return;

        if (!ObiFollowerUtil.TryGetWorld(rope, _ai, out var p))
        {
            // particle list changed this frame (cut) → wait a tick and rebind
            _lostTimer = Mathf.Max(_lostTimer, lostGraceSeconds);
            _ai = -1;
            return;
        }

        // estimate tangent + a stable right (for radial push)
        Vector3 t = ObiFollowerUtil.EstimateTangent(rope, _ai, p);
        Vector3 up = Vector3.up;
        if (Mathf.Abs(Vector3.Dot(up, t)) > 0.98f) up = Vector3.right;
        Vector3 right = Vector3.Cross(up, t).normalized;

        Vector3 targetPos = p + right * radialOffset;
        Quaternion targetRot = Quaternion.LookRotation(t, right);

        float kp = 1f - Mathf.Exp(-posLerp * Time.deltaTime);
        float kr = 1f - Mathf.Exp(-rotLerp * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, targetPos, kp);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, kr);

        // cool down “lost” timer
        if (_lostTimer > 0f) _lostTimer -= Time.deltaTime;
    }
}
