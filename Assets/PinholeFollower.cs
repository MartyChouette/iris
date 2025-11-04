using UnityEngine;
using Obi;

/// Follow an Obi rope particle in WORLD space (renderable position).
[DisallowMultipleComponent]
public class PinholeFollower : MonoBehaviour
{
    public ObiRopeBase rope;
    public int actorIndex;                 // actor-space index (1..count-2)
    public Vector3 fallbackUp = Vector3.up;

    void LateUpdate()
    {
        if (rope == null || rope.solver == null || rope.activeParticleCount <= actorIndex) return;
        var idx = rope.solverIndices;
        if (idx == null || actorIndex < 0 || actorIndex >= idx.count) return;

        int solverIndex = idx[actorIndex];
        var v4 = rope.solver.renderablePositions[solverIndex];
        Vector3 pos = new Vector3(v4.x, v4.y, v4.z);
        transform.position = pos;

        // Optional: orient pinhole to face leaf “side” using tangent
        int aiPrev = Mathf.Max(actorIndex - 1, 0);
        int aiNext = Mathf.Min(actorIndex + 1, rope.activeParticleCount - 1);
        var v4a = rope.solver.renderablePositions[idx[aiPrev]];
        var v4b = rope.solver.renderablePositions[idx[aiNext]];
        Vector3 t = new Vector3(v4b.x - v4a.x, v4b.y - v4a.y, v4b.z - v4a.z);
        if (t.sqrMagnitude > 1e-8f) t.Normalize(); else t = fallbackUp;
        // Point pinhole's +up along tangent for consistency:
        transform.rotation = Quaternion.LookRotation(Vector3.Cross(t, Vector3.right), t);
    }
}