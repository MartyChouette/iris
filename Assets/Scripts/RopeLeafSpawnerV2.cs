using UnityEngine;
using System.Collections;
using Obi;

[DisallowMultipleComponent]
public class RopeLeafSpawnerV2 : MonoBehaviour
{
    [Header("Refs")]
    public ObiRopeBase rope;
    public LeafPool leafPool;
    public Camera cam;
    public Rigidbody stemRigidbody;
    public SapFxPool sapPool;   // optional

    [Header("Counts / Placement")]
    public int minLeaves = 1, maxLeaves = 4;
    [Range(0f, 0.49f)] public float endClearanceFrac = 0.10f;

    [Header("Leaf Offset & Facing")]
    public float radialOffset = 0.05f;
    public bool alternateSides = true;
    public Vector3 fallbackUp = Vector3.up;

    [Header("Scale")]
    public bool forceLeafScaleOne = false;
    public bool overrideWorldScale = false;
    public float leafWorldScale = 1f;

    void Awake() { if (!rope) rope = GetComponent<ObiRopeBase>(); }
    void OnEnable() { StartCoroutine(SpawnWhenReady()); }

    IEnumerator SpawnWhenReady()
    {
        while (rope == null || rope.solver == null ||
               rope.activeParticleCount < 3 ||
               rope.solverIndices == null ||
               rope.solver.renderablePositions == null)
            yield return null;

        Spawn();
    }

    void Spawn()
    {
        if (!leafPool) { Debug.LogError("[RopeLeafSpawnerV3] Assign LeafPool."); return; }

        int count = Mathf.Clamp(Random.Range(minLeaves, maxLeaves + 1), 0, 64);
        if (count == 0) return;

        int actorCount = rope.activeParticleCount;
        float a0 = endClearanceFrac, a1 = 1f - endClearanceFrac;

        for (int k = 0; k < count; k++)
        {
            float f = Mathf.Lerp(a0, a1, (k + 0.5f) / count);
            int ai = Mathf.Clamp(Mathf.RoundToInt(f * (actorCount - 1)), 1, actorCount - 2);

            // world socket
            int solverIndex = rope.solverIndices[ai];
            Vector3 socketWorld = GetRenderableWorld(rope, solverIndex);

            // orientation & side
            Vector3 tangent = ComputeTangentWorld(rope, ai);
            Vector3 upRef = (Mathf.Abs(Vector3.Dot(tangent.normalized, Vector3.up)) > 0.9f)
                                ? rope.transform.right
                                : Vector3.up;
            Vector3 side = Vector3.Cross(upRef, tangent).normalized;
            if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(fallbackUp, tangent).normalized;

            float sgn = (!alternateSides || (k % 2 == 0)) ? +1f : -1f;
            Vector3 leafWorld = socketWorld + side * (radialOffset * sgn);
            Quaternion rot = Quaternion.LookRotation(side * sgn, tangent.sqrMagnitude > 1e-6f ? tangent : fallbackUp);

            // create a UNIQUE pinhole that follows this rope particle:
            Transform pinhole = new GameObject($"Pinhole_ai{ai}").transform;
            pinhole.SetParent(rope.solver.transform, true);
            pinhole.position = socketWorld;
            pinhole.rotation = rope.solver.transform.rotation;
            var follower = pinhole.gameObject.AddComponent<PinholeFollower>();
            follower.rope = rope;
            follower.actorIndex = ai;
            follower.fallbackUp = fallbackUp;

            // get a leaf from pool
            GameObject leafGO = leafPool.Get();
            if (!leafGO) break;

            // world pose -> parent
            leafGO.transform.SetPositionAndRotation(leafWorld, rot);
            if (forceLeafScaleOne) leafGO.transform.localScale = Vector3.one;
            leafGO.transform.SetParent(rope.transform, true);

            if (overrideWorldScale)
            {
                var parentLossy = leafGO.transform.parent ? leafGO.transform.parent.lossyScale : Vector3.one;
                Vector3 safeDiv = new Vector3(
                    parentLossy.x == 0 ? 1 : parentLossy.x,
                    parentLossy.y == 0 ? 1 : parentLossy.y,
                    parentLossy.z == 0 ? 1 : parentLossy.z
                );
                Vector3 desiredWorld = Vector3.one * Mathf.Max(0.0001f, leafWorldScale);
                leafGO.transform.localScale = new Vector3(
                    desiredWorld.x / safeDiv.x,
                    desiredWorld.y / safeDiv.y,
                    desiredWorld.z / safeDiv.z
                );
            }

            // wire behaviour (joint-free) — leaf OWNS the pinhole:
            var lpo = leafGO.GetComponent<LeafPullOff>();
            if (!lpo) { Debug.LogError("[RopeLeafSpawnerV3] Leaf prefab missing LeafPullOff."); Destroy(pinhole.gameObject); continue; }

            //lpo.Setup(cam ? cam : Camera.main, pinhole, stemRigidbody, sapPool);
        }
    }

    // Obi helpers
    static Vector3 GetRenderableWorld(ObiRopeBase rope, int solverIndex)
    {
        var v4 = rope.solver.renderablePositions[solverIndex];
        return new Vector3(v4.x, v4.y, v4.z);
    }

    static Vector3 ComputeTangentWorld(ObiRopeBase rope, int ai)
    {
        var idx = rope.solverIndices;
        int aPrev = Mathf.Max(ai - 1, 0);
        int aNext = Mathf.Min(ai + 1, rope.activeParticleCount - 1);
        Vector3 p0 = GetRenderableWorld(rope, idx[aPrev]);
        Vector3 p1 = GetRenderableWorld(rope, idx[aNext]);
        Vector3 t = p1 - p0;
        if (t.sqrMagnitude > 1e-8f) t.Normalize();
        return t;
    }
}
