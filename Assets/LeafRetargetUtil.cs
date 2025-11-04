using UnityEngine;
using Obi;

/// Utility to rebind nearby leaf-followers to the nearest segment on any rope
/// that shares the same solver (so they "go with" the falling piece after a tear).
public static class LeafRetargetUtil
{
    static readonly Collider[] _buf = new Collider[64];

    /// Call this after rope.Tear(...) + rope.RebuildConstraintsFromElements().
    /// - from: world-space cut point (or any point along the cut segment)
    /// - radius: how far out to look for leaves
    /// - leafMask: layer mask for leaf objects
    public static void RetargetFollowersNear(ObiRopeBase sourceRope, Vector3 from, float radius, LayerMask leafMask)
    {
        if (sourceRope == null || sourceRope.solver == null) return;

        int count = Physics.OverlapSphereNonAlloc(from, radius, _buf, leafMask, QueryTriggerInteraction.Collide);
        if (count <= 0) return;

        for (int i = 0; i < count; i++)
        {
            var col = _buf[i];
            if (!col) continue;

            // Try any follower that supports explicit retargeting:
            var retarget = col.GetComponentInParent<RopeLeafFollowerRetarget>();
            if (retarget && retarget.isActiveAndEnabled)
            {
                // Force an immediate rebind to the nearest segment (same solver).
                ForceRebindToNearest(retarget, sourceRope.solver, from, radius);
                continue;
            }

            // If you're on the simpler follower (no explicit API), just let its
            // next LateUpdate find a new segment; nothing to do here.
        }

        // clear buffer
        for (int i = 0; i < count; i++) _buf[i] = null;
    }

    static void ForceRebindToNearest(RopeLeafFollowerRetarget follower, ObiSolver solver, Vector3 from, float searchRadius)
    {
        if (follower == null || solver == null) return;

        // Use the same logic the follower uses, but invoked now instead of "next frame".
        ObiRopeBase bestRope = null;
        int bestIdx = -1;
        float bestD2 = (searchRadius > 0f) ? searchRadius * searchRadius : float.PositiveInfinity;

        var all = Object.FindObjectsOfType<ObiRopeBase>();
        foreach (var r in all)
        {
            if (!r || r.solver != solver) continue;

            int n = r.activeParticleCount;
            if (n < 2) continue;
            var si = r.solverIndices;
            var rp = r.solver.renderablePositions;
            if (si == null || rp == null) continue;

            for (int i = 0; i < n - 1; i++)
            {
                int s0 = si[i];
                int s1 = si[i + 1];
                if (s0 < 0 || s0 >= rp.count || s1 < 0 || s1 >= rp.count) continue;

                Vector3 p0 = r.solver.transform.TransformPoint((Vector3)rp[s0]);
                Vector3 p1 = r.solver.transform.TransformPoint((Vector3)rp[s1]);
                Vector3 seg = p1 - p0;
                float len2 = Mathf.Max(seg.sqrMagnitude, 1e-6f);
                float t = Mathf.Clamp01(Vector3.Dot(from - p0, seg) / len2);
                Vector3 closest = p0 + seg * t;

                float d2 = (from - closest).sqrMagnitude;
                if (d2 < bestD2) { bestD2 = d2; bestRope = r; bestIdx = i; }
            }
        }

        if (bestRope)
        {
            follower.rope = bestRope;
            follower.actorIndex = bestIdx;
            // ensure solver cache refreshes immediately
            var _ = follower.enabled; // no-op; follower will place correctly in the next LateUpdate
        }
    }
}
