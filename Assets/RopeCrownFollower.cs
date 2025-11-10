using UnityEngine;
using Obi;

[DefaultExecutionOrder(32000)]
[DisallowMultipleComponent]
public class RopeCrownFollower : MonoBehaviour
{
    public ObiRopeBase rope;
    public float posLerp = 22f;
    public float rotLerp = 18f;

    [Header("Offsets")]
    public float alongTangent = 0f;
    public float lateral = 0f;
    public float worldY = 0f;

    int _tipAi = -1;

    void Reset() { if (!rope) rope = GetComponentInParent<ObiRopeBase>(); }
    void OnEnable() { _tipAi = -1; }

    void LateUpdate()
    {
        if (rope == null || rope.solver == null || rope.activeParticleCount == 0) return;

        // 1) pick / repick a free tip every frame (cheap on short stems)
        _tipAi = FindNearestFreeTip(transform.position);
        if (_tipAi < 0) return;

        // 2) read world position safely
        if (!ObiFollowerUtil.TryGetWorld(rope, _tipAi, out var p)) return;

        // 3) tangent from neighbor
        Vector3 t = ObiFollowerUtil.EstimateTangent(rope, _tipAi, p);
        Vector3 up = Vector3.up;
        if (Mathf.Abs(Vector3.Dot(up, t)) > 0.98f) up = Vector3.right;
        Vector3 right = Vector3.Cross(up, t).normalized;

        Vector3 targetPos = p + t * alongTangent + right * lateral + Vector3.up * worldY;
        Quaternion targetRot = Quaternion.LookRotation(t, up);

        float kp = 1f - Mathf.Exp(-posLerp * Time.deltaTime);
        float kr = 1f - Mathf.Exp(-rotLerp * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, targetPos, kp);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, kr);
    }

    int FindNearestFreeTip(Vector3 fromWorld)
    {
        int best = -1; float bestD2 = float.PositiveInfinity;

        for (int ai = 0; ai < rope.activeParticleCount; ai++)
        {
            if (!IsFreeTip(ai)) continue;
            if (!ObiFollowerUtil.TryGetWorld(rope, ai, out var p)) continue;

            float d2 = (p - fromWorld).sqrMagnitude;
            if (d2 < bestD2) { bestD2 = d2; best = ai; }
        }
        return best;
    }

    bool IsFreeTip(int ai)
    {
        // degree == 1 and not fixed (has mass)
        int degree = 0;
        foreach (var e in rope.elements)
        { if (e.particle1 == ai) degree++; if (e.particle2 == ai) degree++; }
        if (degree != 1) return false;

        var si = rope.solverIndices; if (si == null || ai >= si.count) return false;
        int sidx = si[ai];
        var inv = rope.solver.invMasses;
        return (sidx >= 0 && sidx < inv.count && inv[sidx] > 1e-6f);
    }
}
