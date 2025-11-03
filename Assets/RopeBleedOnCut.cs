// RopeBleedOnCut.cs
using UnityEngine;
using System.Collections;
using Obi;

[DisallowMultipleComponent]
public class RopeBleedOnCut : MonoBehaviour
{
    [Header("Refs")]
    public ObiRopeBase rope;      // the rope this bleeder belongs to
    public SapFxPool sapPool;     // your pool (ParticleSystem or ObiEmitter)

    [Header("Burst on cut")]
    public float burstLifetime = 1.0f;  // pass null to use pool default
    public float dirBias = 0.35f;       // how strongly to bias velocity along tangent

    [Header("Optional: short drip after cut")]
    public bool doDrip = true;
    public float dripEvery = 0.12f;
    public int dripCount = 10;

    // --- call this from your scissors with the actor-space index you tore ---
    public void BleedAtActorIndex(int actorIndex)
    {
        if (!rope || !rope.solver || sapPool == null) return;

        // world pos of this particle:
        int solverIndex = rope.solverIndices[actorIndex];
        var v4 = rope.solver.renderablePositions[solverIndex];
        Vector3 pos = new Vector3(v4.x, v4.y, v4.z);

        // tangent (cut direction-ish) for spray orientation:
        Vector3 tan = ComputeTangentWorld(rope, actorIndex);
        if (tan.sqrMagnitude < 1e-6f) tan = Vector3.up;

        // 1) big burst right now (uses pool)
        sapPool.Play(pos, tan, rope.solver.transform, burstLifetime);

        // 2) little dripping after (optional)
        if (doDrip) StartCoroutine(Drip(pos, tan));
    }

    IEnumerator Drip(Vector3 startPos, Vector3 tan)
    {
        // drip from the same spot (simple & cheap). If you want the *new end* of the rope,
        // track the new actor index after tearing and recompute world pos each tick.
        for (int i = 0; i < dripCount; i++)
        {
            if (sapPool) sapPool.Play(startPos, -Physics.gravity.normalized, rope.solver.transform, 0.4f);
            yield return new WaitForSeconds(dripEvery);
        }
    }

    // --- helpers ---
    static Vector3 ComputeTangentWorld(ObiRopeBase rope, int ai)
    {
        var idx = rope.solverIndices;
        int aPrev = Mathf.Max(ai - 1, 0);
        int aNext = Mathf.Min(ai + 1, rope.activeParticleCount - 1);

        var vPrev = rope.solver.renderablePositions[idx[aPrev]];
        var vNext = rope.solver.renderablePositions[idx[aNext]];
        Vector3 p0 = new(vPrev.x, vPrev.y, vPrev.z);
        Vector3 p1 = new(vNext.x, vNext.y, vNext.z);

        Vector3 t = p1 - p0;
        if (t.sqrMagnitude > 1e-8f) t.Normalize();
        return t;
    }
}
