using UnityEngine;
using Obi;

[DefaultExecutionOrder(32000)] // run late, after rope sim updates
[DisallowMultipleComponent]
public class RopeCrownFollower : MonoBehaviour
{
    public enum TargetMode { OriginalTopActor, HighestWorldY, HighestActorIndex }

    [Header("Refs")]
    public ObiRopeBase rope;

    [Header("Targeting")]
    public TargetMode mode = TargetMode.OriginalTopActor;
    [Tooltip("Delay to let the rope bind to the solver before sampling its tip.")]
    public float armDelay = 0.1f;

    [Header("Offsets (meters)")]
    [Tooltip("Push along the stem tangent (out of the tip).")]
    public float alongTangent = 0f;
    [Tooltip("Push sideways (perpendicular to tangent & world up).")]
    public float lateral = 0f;
    [Tooltip("Pure world-space vertical nudge (use negative if it spawns too high).")]
    public float worldY = 0f;

    [Header("Orientation")]
    [Tooltip("World up used to stabilize rotation about the tangent.")]
    public Vector3 upHint = Vector3.up;

    int _originalTopActor = -1;
    bool _armed;

    void Awake()
    {
        if (!rope) rope = GetComponentInParent<ObiRopeBase>();
    }

    System.Collections.IEnumerator Start()
    {
        float t = 0f;
        while (t < armDelay || rope == null || rope.solver == null || rope.activeParticleCount == 0)
        { t += Time.deltaTime; yield return null; }

        _originalTopActor = FindInitialTopActorIndex(); // lock once
        _armed = true;
    }

    void LateUpdate()
    {
        if (!_armed || rope == null || rope.solver == null || rope.activeParticleCount == 0) return;

        int actorIndex = ResolveActorIndex();                       // which particle to follow
        if (!TryGetParticleWorld(actorIndex, out var p)) return;    // world pos of that particle

        // neighbor → tangent (points outward from stem into the tip)
        Vector3 tangent = Vector3.up;
        int nb = FindNeighborActor(actorIndex);
        if (nb >= 0 && TryGetParticleWorld(nb, out var nbP))
        {
            tangent = (p - nbP);
            if (tangent.sqrMagnitude > 1e-10f) tangent.Normalize();
            else tangent = Vector3.up;
        }

        // build a stable “right” on the stem plane for lateral offset
        Vector3 right = Vector3.Cross(upHint, tangent).normalized;
        if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(Vector3.forward, tangent).normalized;

        // final pose
        Vector3 finalPos = p + tangent * alongTangent + right * lateral + Vector3.up * worldY;
        Quaternion finalRot = Quaternion.LookRotation(tangent, upHint);

        // If this object has an "Anchor" child, place that exactly at final pose:
        Transform anchor = transform.Find("Anchor");
        if (anchor != null)
        {
            Matrix4x4 A_local = Matrix4x4.TRS(anchor.localPosition, anchor.localRotation, anchor.localScale);
            Matrix4x4 P_world = Matrix4x4.TRS(finalPos, finalRot, Vector3.one);
            Matrix4x4 parentWorld = P_world * A_local.inverse;
            transform.SetPositionAndRotation(parentWorld.GetColumn(3), parentWorld.rotation);
        }
        else
        {
            transform.SetPositionAndRotation(finalPos, finalRot);
        }
    }

    // ---------- selection helpers ----------

    int ResolveActorIndex()
    {
        switch (mode)
        {
            case TargetMode.HighestWorldY:
                return FindHighestWorldYEnd();
            case TargetMode.HighestActorIndex:
                return FindHighestIndexEnd();
            case TargetMode.OriginalTopActor:
            default:
                // if the captured one still exists, use it; else fall back to highest Y
                if (TryGetParticleWorld(_originalTopActor, out _)) return _originalTopActor;
                return FindHighestWorldYEnd();
        }
    }

    int FindInitialTopActorIndex()
    {
        // choose visually “top” tip at arm time
        return FindHighestWorldYEnd();
    }

    int FindHighestWorldYEnd()
    {
        int best = -1; float bestY = float.NegativeInfinity;
        int n = rope.activeParticleCount;
        for (int ai = 0; ai < n; ai++)
        {
            if (!IsFreeTip(ai)) continue;
            if (TryGetParticleWorld(ai, out var p) && p.y > bestY)
            { bestY = p.y; best = ai; }
        }
        return (best >= 0) ? best : Mathf.Max(0, n - 1);
    }

    int FindHighestIndexEnd()
    {
        int best = -1;
        int n = rope.activeParticleCount;
        for (int ai = 0; ai < n; ai++)
            if (IsFreeTip(ai) && ai > best) best = ai;
        return (best >= 0) ? best : Mathf.Max(0, n - 1);
    }

    bool IsFreeTip(int ai)
    {
        // degree==1 and not fixed
        int deg = 0;
        for (int i = 0; i < rope.elements.Count; i++)
        {
            var e = rope.elements[i];
            if (e.particle1 == ai) deg++;
            if (e.particle2 == ai) deg++;
        }
        if (deg != 1) return false;

        var siL = rope.solverIndices;
        if (siL == null || ai >= siL.count) return false;
        int si = siL[ai];
        var s = rope.solver;
        return (si >= 0 && si < s.invMasses.count && s.invMasses[si] > 1e-6f);
    }

    int FindNeighborActor(int actorIndex)
    {
        for (int i = 0; i < rope.elements.Count; i++)
        {
            var e = rope.elements[i];
            if (e.particle1 == actorIndex) return e.particle2;
            if (e.particle2 == actorIndex) return e.particle1;
        }
        return -1;
    }

    // Obi 7: renderablePositions are in WORLD space already
    bool TryGetParticleWorld(int actorIndex, out Vector3 worldPos)
    {
        worldPos = default;
        if (rope == null || rope.solver == null) return false;

        var siL = rope.solverIndices;
        if (siL == null || actorIndex < 0 || actorIndex >= siL.count) return false;

        int si = siL[actorIndex];
        var rp = rope.solver.renderablePositions;
        if (si < 0 || si >= rp.count) return false;

        var p4 = rp[si];
        worldPos = new Vector3(p4.x, p4.y, p4.z);
        return true;
    }
}
