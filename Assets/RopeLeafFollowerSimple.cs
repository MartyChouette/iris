using UnityEngine;
using Obi;

/// Super-light follower that places the leaf beside a rope segment or a pinhole.
/// Add this to the leaf. You can also set pinner/pinhole at runtime.
[DefaultExecutionOrder(32000)]
[DisallowMultipleComponent]
public class RopeLeafFollowerSimple : MonoBehaviour
{
    [Header("Rope / Target")]
    public ObiRopeBase rope;                 // rope this leaf rides (optional if using pinhole)
    public int actorIndex = 0;               // segment start index (i..i+1)
    public Transform pinhole;                // overrides segment center if assigned

    [Header("Placement")]
    public float radialOffset = 0.02f;       // distance from stem center
    [Range(-180, 180)] public float azimuthDeg = 90f; // around-tangent roll
    public Vector3 referenceUp = Vector3.up; // stabilizes roll

    [Header("Leaf local axes")]
    public Vector3 leafLocalOut = Vector3.right;   // leaf local axis that points outward
    public Vector3 leafLocalForward = Vector3.up;  // leaf local axis along stem

    [Header("Smoothing")]
    public float posLerp = 25f;
    public float rotLerp = 25f;

    ObiSolver _solver;
    static readonly float kEps = 1e-6f;

    void OnEnable() { _solver = rope ? rope.solver : null; }
    void LateUpdate()
    {
        if (pinhole) { FollowPinhole(); return; }
        if (!rope || !_solver) return;

        var si = rope.solverIndices;
        var rp = _solver.renderablePositions;
        int n = rope.activeParticleCount;
        if (si == null || rp == null || n < 2) return;

        actorIndex = Mathf.Clamp(actorIndex, 0, n - 2);
        if (si.count <= actorIndex + 1) return;

        int s0 = si[actorIndex];
        int s1 = si[actorIndex + 1];
        if (s0 < 0 || s0 >= rp.count || s1 < 0 || s1 >= rp.count) return;

        Vector3 p0 = _solver.transform.TransformPoint((Vector3)rp[s0]);
        Vector3 p1 = _solver.transform.TransformPoint((Vector3)rp[s1]);
        PlaceBesideSegment(p0, p1);
    }

    void FollowPinhole()
    {
        // Fake a tiny segment at the pinhole so orientation math is stable
        Vector3 p = pinhole.position;
        Vector3 fwd = pinhole.up; // or rope tangent if you store it
        if (fwd.sqrMagnitude < kEps) fwd = Vector3.up;
        PlaceBesideSegment(p - fwd * 0.01f, p + fwd * 0.01f, p);
    }

    void PlaceBesideSegment(Vector3 p0, Vector3 p1, Vector3? baseOverride = null)
    {
        Vector3 center = baseOverride ?? (Vector3?)((p0 + p1) * 0.5f) ?? Vector3.zero;
        Vector3 tangent = (p1 - p0);
        if (tangent.sqrMagnitude < kEps) tangent = Vector3.up;
        tangent.Normalize();

        Vector3 upRef = referenceUp.normalized;
        if (Mathf.Abs(Vector3.Dot(upRef, tangent)) > 0.98f)
        {
            upRef = Vector3.Cross(tangent, Vector3.right).normalized;
            if (upRef.sqrMagnitude < kEps) upRef = Vector3.Cross(tangent, Vector3.forward).normalized;
        }

        Vector3 radial0 = Vector3.Cross(upRef, tangent).normalized;
        Quaternion roll = Quaternion.AngleAxis(azimuthDeg, tangent);
        Vector3 radial = (roll * radial0).normalized;

        Vector3 basePos = baseOverride ?? (Vector3)((p0 + p1) * 0.5f);
        Vector3 targetPos = basePos + radial * radialOffset;

        Quaternion alignF = Quaternion.FromToRotation(leafLocalForward.normalized, tangent);
        Vector3 outAfter = alignF * leafLocalOut.normalized;
        Quaternion alignO = Quaternion.FromToRotation(outAfter, radial);
        Quaternion targetRot = alignO * alignF;

        float kp = 1f - Mathf.Exp(-posLerp * Time.deltaTime);
        float kr = 1f - Mathf.Exp(-rotLerp * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, targetPos, kp);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, kr);
    }
}
