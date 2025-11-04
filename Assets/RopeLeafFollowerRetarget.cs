using UnityEngine;
using Obi;

/// Follows a rope segment or pinhole and can rebind to the nearest segment in the same solver
/// after a cut, so the leaf stays with the falling piece.
[DefaultExecutionOrder(32000)]
[DisallowMultipleComponent]
public class RopeLeafFollowerRetarget : MonoBehaviour
{
    [Header("Target")]
    public ObiRopeBase rope;         // current rope actor to follow
    public int actorIndex = 0;       // segment start (i..i+1)
    public Transform pinhole;        // optional socket (orientation/placement hint)

    [Header("Placement")]
    public float radialOffset = 0.02f;
    [Range(-180, 180)] public float azimuthDeg = 90f;
    public Vector3 referenceUp = Vector3.up;

    [Header("Leaf local axes")]
    public Vector3 leafLocalOut = Vector3.right;
    public Vector3 leafLocalForward = Vector3.up;

    [Header("Smoothing")]
    public float posLerp = 25f;
    public float rotLerp = 25f;

    [Header("Retarget")]
    public bool autoRetargetOnCut = true;
    public float retargetSearchRadius = 1.0f;

    ObiSolver _solver;
    Vector3 _lastWorldPos;
    const float kEps = 1e-6f;

    void OnEnable() { _solver = rope ? rope.solver : null; _lastWorldPos = transform.position; }

    void LateUpdate()
    {
        if (autoRetargetOnCut && !HasValidSegment())
            ForceRebindToNearest(_solver, _lastWorldPos, retargetSearchRadius);

        if (HasValidSegment())
        {
            GetSegWorld(rope, actorIndex, out var p0, out var p1);
            PlaceBesideSegment(p0, p1, pinhole ? pinhole.position : (Vector3?)null);
        }
        else if (pinhole)
        {
            var p = pinhole.position;
            var f = pinhole.up; if (f.sqrMagnitude < kEps) f = Vector3.up;
            PlaceBesideSegment(p - f * 0.01f, p + f * 0.01f, p);
        }

        _lastWorldPos = transform.position;
    }

    bool HasValidSegment()
    {
        if (!rope) return false;
        _solver ??= rope.solver;
        if (_solver == null) return false;

        int n = rope.activeParticleCount;
        if (n < 2) return false;

        var si = rope.solverIndices;
        if (si == null || si.count <= actorIndex + 1) return false;

        int s0 = si[actorIndex], s1 = si[actorIndex + 1];
        var rp = _solver.renderablePositions;
        return s0 >= 0 && s0 < rp.count && s1 >= 0 && s1 < rp.count;
    }

    // Called by LeafRetargetUtil
    public void ForceRebindToNearest(ObiSolver solver, Vector3 from, float searchRadius)
    {
        if (solver == null) return;

        ObiRopeBase bestRope = null; int bestIdx = -1;
        float bestD2 = (searchRadius > 0f) ? searchRadius * searchRadius : float.PositiveInfinity;

        var all = Object.FindObjectsOfType<ObiRopeBase>();
        foreach (var r in all)
        {
            if (!r || r.solver != solver) continue;
            int n = r.activeParticleCount; if (n < 2) continue;
            var si = r.solverIndices; var rp = r.solver.renderablePositions; if (si == null || rp == null) continue;

            for (int i = 0; i < n - 1; i++)
            {
                int s0 = si[i], s1 = si[i + 1];
                if (s0 < 0 || s0 >= rp.count || s1 < 0 || s1 >= rp.count) continue;

                Vector3 p0 = r.solver.transform.TransformPoint((Vector3)rp[s0]);
                Vector3 p1 = r.solver.transform.TransformPoint((Vector3)rp[s1]);
                Vector3 seg = p1 - p0; float len2 = Mathf.Max(seg.sqrMagnitude, 1e-6f);
                float t = Mathf.Clamp01(Vector3.Dot(from - p0, seg) / len2);
                Vector3 closest = p0 + seg * t;

                float d2 = (from - closest).sqrMagnitude;
                if (d2 < bestD2) { bestD2 = d2; bestRope = r; bestIdx = i; }
            }
        }

        if (bestRope)
        {
            rope = bestRope;
            actorIndex = bestIdx;
            _solver = rope.solver;
        }
    }

    static void GetSegWorld(ObiRopeBase r, int idx, out Vector3 p0, out Vector3 p1)
    {
        var s = r.solver; var si = r.solverIndices; var rp = s.renderablePositions;
        p0 = s.transform.TransformPoint((Vector3)rp[si[idx]]);
        p1 = s.transform.TransformPoint((Vector3)rp[si[idx + 1]]);
    }

    void PlaceBesideSegment(Vector3 p0, Vector3 p1, Vector3? baseOverride)
    {
        Vector3 center = baseOverride ?? (Vector3?)((p0 + p1) * 0.5f) ?? Vector3.zero;
        Vector3 tangent = (p1 - p0); if (tangent.sqrMagnitude < kEps) tangent = Vector3.up; tangent.Normalize();

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
