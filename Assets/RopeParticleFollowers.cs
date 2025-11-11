// File: RopeLeafFollower_Stable.cs  (fixed)
// Same behavior; only change is the head-index helper signature/impl.

using UnityEngine;
using Obi;

[DefaultExecutionOrder(32000)]
[DisallowMultipleComponent]
public class RopeParticleFollowers : MonoBehaviour
{
    [Header("Rope & camera")]
    public ObiRopeBase rope;
    public Camera cam;

    [Header("Segment selection")]
    public int actorIndex = 1;
    public Transform flowerHead;

    [Header("Placement / feel")]
    public float radial = 0.5f;
    public float posLerp = 30f;
    public float rotLerp = 18f;
    public float side = +1f;

    [Header("Leaf local axes")]
    public Vector3 localOut = Vector3.right;
    public Vector3 localUp = Vector3.forward;

    [Header("Cut/tear robustness")]
    public bool autoRebind = true;
    public float rebindSearchRadius = 0.15f;

    Quaternion _baseMap;
    Vector3 _baseLocalUp;
    bool _following = true;

    void Reset() { rope = GetComponentInParent<ObiRopeBase>(); }

    void Awake()
    {
        if (!cam) cam = Camera.main;
        _baseLocalUp = localUp;
        _baseMap = Quaternion.Inverse(Quaternion.LookRotation(localUp, localOut));
    }

    public void StopFollowing() => _following = false;
    public void StartFollowing() => _following = true;

    void OnEnable() { if (rope != null) rope.OnSimulationStart += OnSimStart; }
    void OnDisable() { if (rope != null) rope.OnSimulationStart -= OnSimStart; }

    void OnSimStart(ObiActor a, float step, float substep) { ClampActorIndex(); }

    void LateUpdate()
    {
        if (!_following || rope == null || rope.solver == null) return;

        var s = rope.solver;
        var si = rope.solverIndices;
        var rp = s.renderablePositions;
        int n = rope.activeParticleCount;

        if (si == null || rp == null || n < 2) return;

        ClampActorIndex();

        // head-avoid using new helper (no NativeList in signature)
        int headActor = EstimateHeadIndexFromRope(rope, flowerHead);
        if (headActor >= 0)
        {
            if (actorIndex == headActor - 1) actorIndex = Mathf.Clamp(actorIndex - 1, 0, n - 2);
            if (actorIndex >= headActor) actorIndex = Mathf.Clamp(headActor - 2, 0, n - 2);
        }

        if (si.count <= actorIndex + 1) return;

        int siA = si[actorIndex];
        int siB = si[actorIndex + 1];
        if (!Valid(siA, rp.count) || !Valid(siB, rp.count))
        {
            if (autoRebind) TryRebindToNearestSegment();
            return;
        }

        Vector3 pA = s.transform.TransformPoint((Vector3)rp[siA]);
        Vector3 pB = s.transform.TransformPoint((Vector3)rp[siB]);
        Vector3 mid = 0.5f * (pA + pB);

        Vector3 tangent = pB - pA;
        if (tangent.sqrMagnitude < 1e-9f) tangent = Vector3.up; else tangent.Normalize();

        Vector3 viewRight = cam ? cam.transform.right : Vector3.right;
        Vector3 rightProj = Vector3.ProjectOnPlane(viewRight, tangent).normalized;
        if (rightProj.sqrMagnitude < 1e-9f) rightProj = Vector3.Cross(Vector3.up, tangent).normalized;

        Vector3 outDir = rightProj * Mathf.Sign(side);
        Vector3 targetPos = mid + outDir * radial;

        transform.position = Vector3.Lerp(transform.position, targetPos, 1f - Mathf.Exp(-posLerp * Time.deltaTime));

        float ySign = Mathf.Sign(side) >= 0f ? 1f : -1f;
        Vector3 signedLocalUp = new Vector3(_baseLocalUp.x, Mathf.Abs(_baseLocalUp.y) * ySign, _baseLocalUp.z);
        Quaternion mapNow = Quaternion.Inverse(Quaternion.LookRotation(signedLocalUp, localOut));
        Quaternion worldAim = Quaternion.LookRotation(tangent, outDir);
        Quaternion targetRot = worldAim * mapNow;

        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-rotLerp * Time.deltaTime));
    }

    void ClampActorIndex()
    {
        int n = rope.activeParticleCount;
        actorIndex = Mathf.Clamp(actorIndex, 0, Mathf.Max(0, n - 2));
    }

    static bool Valid(int idx, int count) => idx >= 0 && idx < count;

    // --- FIX: no NativeList<> in parameters; pull data from rope/solver directly.
    int EstimateHeadIndexFromRope(ObiRopeBase r, Transform head)
    {
        if (r == null || r.solver == null || head == null) return -1;

        var s = r.solver;
        var si = r.solverIndices;
        var rp = s.renderablePositions;
        int n = r.activeParticleCount;

        if (si == null || rp == null || n <= 0) return -1;

        float best = float.PositiveInfinity;
        int bestActor = -1;

        for (int i = 0; i < n; i++)
        {
            int svi = si[i];
            if (!Valid(svi, rp.count)) continue;
            Vector3 p = s.transform.TransformPoint((Vector3)rp[svi]);
            float d2 = (p - head.position).sqrMagnitude;
            if (d2 < best) { best = d2; bestActor = i; }
        }
        return bestActor;
    }

    void TryRebindToNearestSegment()
    {
        if (rope == null || rope.solver == null) return;

        var s = rope.solver;
        var rp = s.renderablePositions;
        var si = rope.solverIndices;
        if (rp == null || si == null) return;

        float best = rebindSearchRadius * rebindSearchRadius;
        int bestI = -1;

        int n = rope.activeParticleCount;
        for (int i = 0; i < n - 1; i++)
        {
            int a = si[i];
            int b = si[i + 1];
            if (!Valid(a, rp.count) || !Valid(b, rp.count)) continue;

            Vector3 pA = s.transform.TransformPoint((Vector3)rp[a]);
            Vector3 pB = s.transform.TransformPoint((Vector3)rp[b]);
            Vector3 mid = 0.5f * (pA + pB);

            float d2 = (mid - transform.position).sqrMagnitude;
            if (d2 < best) { best = d2; bestI = i; }
        }

        if (bestI >= 0) actorIndex = bestI;
    }
}
