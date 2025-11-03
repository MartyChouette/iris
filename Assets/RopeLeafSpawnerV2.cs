using UnityEngine;
using System.Collections;
using Obi;

[DisallowMultipleComponent]
public class RopeLeafSpawnerV2 : MonoBehaviour
{
    [Header("Refs")]
    public ObiRopeBase rope;
    public LeafPool leafPool;              // <-- use pool
    public PinholeLibrary pinholeLibrary;  // optional collector
    public Camera cam;
    public Rigidbody stemRigidbody;        // optional jiggle RB shared by leaves

    [Header("Counts / Placement")]
    public int minLeaves = 1, maxLeaves = 4;
    [Range(0f, 0.49f)] public float endClearanceFrac = 0.10f;

    [Header("Leaf Offset & Facing")]
    public float radialOffset = 0.05f;
    public bool alternateSides = true;
    public Vector3 fallbackUp = Vector3.up;
    public bool forceLeafScaleOne = true;

    void Awake() { if (!rope) rope = GetComponent<ObiRopeBase>(); }
    void OnEnable() { StartCoroutine(SpawnWhenReady()); }

    IEnumerator SpawnWhenReady()
    {
        while (rope == null ||
               rope.solver == null ||
               rope.activeParticleCount < 3 ||
               rope.solverIndices == null ||
               rope.solver.renderablePositions == null)
            yield return null;

        Spawn();
    }

    void Spawn()
    {
        if (!leafPool) { Debug.LogError("[RopeLeafSpawnerV2] Assign LeafPool."); return; }

        int count = Mathf.Clamp(Random.Range(minLeaves, maxLeaves + 1), 0, 64);
        if (count == 0) return;

        int actorCount = rope.activeParticleCount;
        float a0 = endClearanceFrac, a1 = 1f - endClearanceFrac;

        for (int k = 0; k < count; k++)
        {
            float f = Mathf.Lerp(a0, a1, (k + 0.5f) / count);
            int ai = Mathf.Clamp(Mathf.RoundToInt(f * (actorCount - 1)), 1, actorCount - 2);

            // world-space socket position from Obi
            int solverIndex = rope.solverIndices[ai];
            Vector3 socketWorld = GetRenderableWorld(rope, solverIndex);

            // compute side/tangent for initial offset/orientation
            Vector3 tangent = ComputeTangentWorld(rope, ai);
            Vector3 upRef = Vector3.Dot(tangent, Vector3.up) > 0.9f ? rope.transform.right : Vector3.up;
            Vector3 side = Vector3.Cross(upRef, tangent).normalized;
            if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(fallbackUp, tangent).normalized;

            float sgn = (!alternateSides || (k % 2 == 0)) ? +1f : -1f;
            Vector3 leafWorld = socketWorld + side * (radialOffset * sgn);
            Quaternion rot = Quaternion.LookRotation(side * sgn, tangent.sqrMagnitude > 1e-6f ? tangent : fallbackUp);

            // create a pinhole AT the socket under solver (no scale/rotation surprises)
            Transform pinhole = CreatePinholeAt(socketWorld, ai);

            // fetch leaf from pool
            GameObject leafGO = leafPool.Get();
            if (!leafGO) break;

            // place at world pose *before* parenting, then parent under rope
            leafGO.transform.SetPositionAndRotation(leafWorld, rot);
            if (forceLeafScaleOne) leafGO.transform.localScale = Vector3.one;
            leafGO.transform.SetParent(rope.transform, true);

            // wire up the behaviour
            var lpo = leafGO.GetComponent<LeafPullOff>();
            if (!lpo) { Debug.LogError("[RopeLeafSpawnerV3] Leaf prefab missing LeafPullOff."); leafPool.Return(leafGO); continue; }

            lpo.cam = cam ? cam : Camera.main;
            lpo.explicitPinhole = pinhole;
            lpo.pinholes = null;
            lpo.stemRigidbody = stemRigidbody;

            if (pinholeLibrary) pinholeLibrary.pinholePoints.Add(pinhole);
        }
    }

    // ----- Obi helpers -----
    static Vector3 GetRenderableWorld(ObiRopeBase rope, int solverIndex)
    {
        var v4 = rope.solver.renderablePositions[solverIndex]; // ObiNativeVector4List (WORLD)
        return new Vector3(v4.x, v4.y, v4.z);
    }

    static Vector3 ComputeTangentWorld(ObiRopeBase rope, int ai)
    {
        var idx = rope.solverIndices;
        int aPrev = Mathf.Max(ai - 1, 0);
        int aNext = Mathf.Min(ai + 1, rope.activeParticleCount - 1);
        Vector3 p0 = GetRenderableWorld(rope, idx[aPrev]);
        Vector3 p1 = GetRenderableWorld(rope, idx[aNext]);
        Vector3 t = p1 - p0; if (t.sqrMagnitude > 1e-8f) t.Normalize();
        return t;
    }

    Transform CreatePinholeAt(Vector3 socketWorld, int actorIndex)
    {
        var t = new GameObject($"Pinhole_ai{actorIndex}").transform;
        t.SetParent(rope.solver.transform, true);
        t.position = socketWorld;
        t.rotation = rope.solver.transform.rotation;
        t.gameObject.AddComponent<PinholeMarker>().actorIndex = actorIndex;
        return t;
    }
}
