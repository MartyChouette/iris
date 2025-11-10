// File: ObiFollowerUtil.cs
using UnityEngine;
using Obi;

public static class ObiFollowerUtil
{
    /// Finds the closest actor-space particle to 'worldPos'. Returns -1 if none within 'maxRadius'.
    public static int FindNearestActorIndex(ObiRopeBase rope, Vector3 worldPos, float maxRadius)
    {
        if (rope == null || rope.solver == null || rope.activeParticleCount <= 0) return -1;

        var s = rope.solver;
        var si = rope.solverIndices; // actor->solver index map
        var rp = s.renderablePositions;

        if (si == null || rp == null || rp.count == 0) return -1;

        int best = -1;
        float bestSq = maxRadius > 0 ? maxRadius * maxRadius : float.PositiveInfinity;

        int n = rope.activeParticleCount;
        for (int i = 0; i < n; i++)
        {
            int solverIndex = si[i];
            if (solverIndex < 0 || solverIndex >= rp.count) continue;

            var p4 = rp[solverIndex];                       // Obi uses float4
            Vector3 p = s.transform.TransformPoint(new Vector3(p4.x, p4.y, p4.z));

            float d2 = (p - worldPos).sqrMagnitude;
            if (d2 < bestSq) { bestSq = d2; best = i; }
        }
        return best;
    }

    /// Gets world position of actor-space particle 'ai'. Returns false if invalid (e.g., just cut).
    public static bool TryGetWorld(ObiRopeBase rope, int ai, out Vector3 world)
    {
        world = default;
        if (rope == null || rope.solver == null || rope.activeParticleCount <= 0) return false;

        var s = rope.solver;
        var si = rope.solverIndices;
        var rp = s.renderablePositions;
        if (si == null || rp == null || ai < 0 || ai >= rope.activeParticleCount) return false;

        int solverIndex = si[ai];
        if (solverIndex < 0 || solverIndex >= rp.count) return false;

        var p4 = rp[solverIndex];
        world = s.transform.TransformPoint(new Vector3(p4.x, p4.y, p4.z));
        return true;
    }

    /// Estimate rope tangent at actor particle 'ai' using neighbors.
    public static Vector3 EstimateTangent(ObiRopeBase rope, int ai, Vector3 fallbackPos)
    {
        if (rope == null || rope.solver == null || rope.activeParticleCount <= 1)
            return Vector3.up;

        int n = rope.activeParticleCount;
        int a0 = Mathf.Max(0, ai - 1);
        int a1 = Mathf.Min(n - 1, ai + 1);

        Vector3 p0, p1;
        if (!TryGetWorld(rope, a0, out p0)) p0 = fallbackPos;
        if (!TryGetWorld(rope, a1, out p1)) p1 = fallbackPos;

        Vector3 t = (p1 - p0);
        if (t.sqrMagnitude < 1e-6f) t = Vector3.up;
        return t.normalized;
    }
}
