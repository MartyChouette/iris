using UnityEngine;
using Obi;

[DisallowMultipleComponent]
public class RopeLeafSpawner : MonoBehaviour
{
    public ObiRopeBase rope;          // ObiRope or ObiRod
    public LeafPool leafPool;
    public SapFxPool sapPool;

    public int minLeaves = 1, maxLeaves = 4;
    [Range(0, 0.49f)] public float endClearanceFrac = 0.1f;
    public float radialOffset = 0.5f;

    void Awake() { if (!rope) rope = GetComponent<ObiRopeBase>(); }
    void OnEnable() { StartCoroutine(SpawnWhenReady()); }

    System.Collections.IEnumerator SpawnWhenReady()
    {
        while (!rope || !rope.solver || rope.activeParticleCount < 4 ||
               rope.solverIndices == null || rope.solver.renderablePositions == null)
            yield return null;
        Spawn();
    }

    void Spawn()
    {
        if (!leafPool) { Debug.LogError("Assign LeafPool"); return; }

        int count = Mathf.Clamp(Random.Range(minLeaves, maxLeaves + 1), 0, 10);
        if (count == 0) return;

        int actorCount = rope.activeParticleCount;
        float a0 = endClearanceFrac, a1 = 1f - endClearanceFrac;

        for (int k = 0; k < count; k++)
        {
            float f = Mathf.Lerp(a0, a1, (k + 0.5f) / count);
            int ai = Mathf.Clamp(Mathf.RoundToInt(f * (actorCount - 1)), 1, actorCount - 2);

            var go = leafPool.Get(); if (!go) break;

            // parent under rope for tidy hierarchy (not required for follower)
            go.transform.SetParent(rope.transform, true);

            // wire leaf script
            var leaf = go.GetComponent<Leaf>();
            if (leaf)
            {
                leaf.leafPool = leafPool;
                leaf.sapPool = sapPool;
            }

            // follower setup
            var fol = go.GetComponent<RopeLeafFollower>();
            if (!fol) fol = go.AddComponent<RopeLeafFollower>();
            fol.rope = rope;
            fol.actorIndex = ai;
            fol.side = (k % 2 == 0) ? +1f : -1f;

            //Tilt leaf for side
            if (fol.side == 1)
            {
                fol.localOut.y = -1;
            }
            else
            {
                fol.localOut.y = 1;
            }

            fol.cam = Camera.main; // or your gameplay cam

            // reset a reasonable initial pose (it will snap next LateUpdate)
            go.transform.position = rope.transform.position;
            go.transform.rotation = rope.transform.rotation;
        }
    }
}
