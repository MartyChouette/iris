// File: RopeLeafFollowerPlus_Obi7.cs
// Obi 7-style: uses rope.solverIndices and solver.renderablePositions (no particleIndices!)

using UnityEngine;
using Obi;

[DefaultExecutionOrder(32000)]
[DisallowMultipleComponent]
public class RopeLeafFollowerPlus : MonoBehaviour
{
    [Header("Attach / Follow")]
    public ObiRopeBase rope;                // rope piece to follow
    [Tooltip("Actor-space segment start index (segment = i..i+1).")]
    public int actorIndex = 0;              // will clamp to [0..activeParticleCount-2]

    [Header("Optional anchors")]
    public Transform pinhole;               // socket on stem surface (for base position if you prefer)
    public Transform flowerHead;            // blossom/head transform (used to bias segment direction)

    [Header("Placement (around stem)")]
    [Tooltip("Distance from stem center to leaf base.")]
    public float radialOffset = 0.02f;
    [Tooltip("Azimuth around stem in degrees. +90 = right, -90 = left (relative to referenceUp).")]
    public float azimuthDeg = 90f;

    [Header("Reference frame")]
    [Tooltip("Stabilizes roll. World up is fine.")]
    public Vector3 referenceUp = Vector3.up;

    [Header("Leaf mesh local axes")]
    [Tooltip("Leaf local axis that should point OUT from stem.")]
    public Vector3 leafLocalOut = Vector3.right;
    [Tooltip("Leaf local axis that should align ALONG stem.")]
    public Vector3 leafLocalForward = Vector3.up;

    [Header("Smoothing")]
    public float posLerp = 25f;
    public float rotLerp = 25f;

    [Header("Detach / Physics")]
    public Rigidbody leafRigidbody;         // set kinematic while attached
    public bool autoRebindAfterTear = true; // try to hop to the nearest piece after splits

    // internals
    ObiSolver _solver;
    bool _attached = true;
    Vector3 _lastAnchor;                    // last computed center-of-segment
    static readonly float kEps = 1e-6f;

    void Awake()
    {
        if (!leafRigidbody) leafRigidbody = GetComponent<Rigidbody>();
        if (leafRigidbody) leafRigidbody.isKinematic = true;
        CacheSolver();
    }

    void OnEnable() => CacheSolver();
    void CacheSolver() => _solver = rope ? rope.solver : null;

    public void Detach()
    {
        _attached = false;
        enabled = false;
        if (leafRigidbody)
        {
            leafRigidbody.isKinematic = false;
            leafRigidbody.useGravity = true;
        }
    }

    void LateUpdate()
    {
        if (!_attached) return;

        if (!IsRopeUsable())
        {
            if (autoRebindAfterTear) TryRebindToNearestPiece();
            return;
        }

        var s = _solver;
        var siL = rope.solverIndices;          // actor->solver map
        var rp = s.renderablePositions;       // solver-space particle positions
        int n = rope.activeParticleCount;
        if (siL == null || rp == null || n < 2) return;

        // Clamp to valid segment range:
        actorIndex = Mathf.Clamp(actorIndex, 0, n - 2);

        // Read this segment’s two endpoints in world space (guarding invalid solver indices):
        if (siL.count <= actorIndex + 1) return;
        int s0 = siL[actorIndex];
        int s1 = siL[actorIndex + 1];
        if (!IsValidSolverIndex(s0, rp.count) || !IsValidSolverIndex(s1, rp.count)) { if (autoRebindAfterTear) TryRebindToNearestPiece(); return; }

        Vector3 p0 = s.transform.TransformPoint((Vector3)rp[s0]);
        Vector3 p1 = s.transform.TransformPoint((Vector3)rp[s1]);
        Vector3 center = 0.5f * (p0 + p1);

        // Tangent along stem for this segment (optionally flip so it's head->tail):
        Vector3 tangent = (p1 - p0);
        if (flowerHead)
        {
            float d0 = (p0 - flowerHead.position).sqrMagnitude;
            float d1 = (p1 - flowerHead.position).sqrMagnitude;
            bool p1IsHeadSide = d1 < d0;
            tangent = p1IsHeadSide ? (p0 - p1) : (p1 - p0);
        }
        if (tangent.sqrMagnitude < kEps) tangent = Vector3.up;
        tangent.Normalize();

        // Build stable frame: referenceUp → initial radial, then rotate by azimuth around tangent
        Vector3 upRef = referenceUp.normalized;
        if (Mathf.Abs(Vector3.Dot(upRef, tangent)) > 0.98f)
        {
            upRef = Vector3.Cross(tangent, Vector3.right).normalized;
            if (upRef.sqrMagnitude < kEps) upRef = Vector3.Cross(tangent, Vector3.forward).normalized;
        }

        Vector3 radial0 = Vector3.Cross(upRef, tangent).normalized;
        Quaternion roll = Quaternion.AngleAxis(azimuthDeg, tangent);
        Vector3 radial = (roll * radial0).normalized;
        Vector3 up = Vector3.Cross(tangent, radial).normalized;

        // Base position (allow pinhole override if you placed sockets)
        Vector3 basePos = pinhole ? pinhole.position : center;
        Vector3 targetPos = basePos + radial * radialOffset;

        // Rotation: map leafLocalForward->tangent and leafLocalOut->radial
        Quaternion alignF = Quaternion.FromToRotation(leafLocalForward.normalized, tangent);
        Vector3 outAfterF = alignF * leafLocalOut.normalized;
        Quaternion alignO = Quaternion.FromToRotation(outAfterF, radial);
        Quaternion targetRot = alignO * alignF;

        // Smooth move/rotate
        float kPos = 1f - Mathf.Exp(-posLerp * Time.deltaTime);
        float kRot = 1f - Mathf.Exp(-rotLerp * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, targetPos, kPos);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, kRot);

        _lastAnchor = center;
    }

    bool IsRopeUsable()
    {
        return rope != null && _solver != null && rope.solver == _solver;
    }

    bool IsValidSolverIndex(int si, int count)
    {
        return si >= 0 && si < count;
    }

    // Rebind by finding the rope piece (same solver) with a particle nearest our last anchor.
    void TryRebindToNearestPiece()
    {
        if (_solver == null) return;

        ObiRopeBase best = null;
        int bestActorIndex = -1;
        float bestDist = float.PositiveInfinity;

        var all = Object.FindObjectsByType<ObiRope>(0);
        foreach (var r in all)
        {
            if (r == null || r.solver != _solver) continue;

            var siL = r.solverIndices;
            var rp = _solver.renderablePositions;
            int n = r.activeParticleCount;
            if (siL == null || rp == null || n < 2) continue;

            // scan segments; pick the segment whose center is nearest our last anchor
            for (int i = 0; i < n - 1; ++i)
            {
                int s0 = siL[i];
                int s1 = siL[i + 1];
                if (!IsValidSolverIndex(s0, rp.count) || !IsValidSolverIndex(s1, rp.count)) continue;

                Vector3 p0 = _solver.transform.TransformPoint((Vector3)rp[s0]);
                Vector3 p1 = _solver.transform.TransformPoint((Vector3)rp[s1]);
                Vector3 c = 0.5f * (p0 + p1);

                float d = (c - (_lastAnchor == Vector3.zero ? transform.position : _lastAnchor)).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = r;
                    bestActorIndex = i;
                }
            }
        }

        if (best != null && bestActorIndex >= 0)
        {
            rope = best;
            actorIndex = bestActorIndex;
            _solver = rope.solver;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.006f);
    }
#endif
}
