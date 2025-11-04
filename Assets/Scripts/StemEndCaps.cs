using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Obi;

[DisallowMultipleComponent]
public class StemEndCaps : MonoBehaviour
{
    [Header("Refs")]
    public ObiRope rope;

    [Tooltip("Prefab for the bottom cover (cap). Placed on all free tips EXCEPT the crown tip.")]
    public GameObject bottomCapPrefab;

    [Tooltip("Prefab for the flower crown. Always attached to the MAX actor index (last particle).")]
    public GameObject topCrownPrefab;

    [Header("Placement")]
    [Tooltip("Meters to push outward along the end normal. Set 0 while validating.")]
    public float pushOut = 0f;

    [Tooltip("Meters of sideways offset. Set 0 while validating.")]
    public float lateralOffset = 0f;

    [Tooltip("Delay so the rope binds to the solver before we arm.")]
    public float armDelay = 0.1f;

    [Header("Debug")]
    public bool drawEndGizmos = true;
    public float gizmoRadius = 0.004f;

    // runtime
    GameObject _topCrown;
    readonly List<GameObject> _caps = new(); // one cap per non-crown end

    void Reset() { if (!rope) rope = GetComponent<ObiRope>(); }

    IEnumerator Start()
    {
        float t = 0f;
        while (t < armDelay || rope == null || rope.solver == null || rope.activeParticleCount == 0)
        { t += Time.deltaTime; yield return null; }

        if (topCrownPrefab) _topCrown = Instantiate(topCrownPrefab, transform);
    }

    void LateUpdate()
    {
        if (!rope || rope.solver == null || rope.activeParticleCount == 0) return;

        var ends = GetFreeTips();
        if (ends.Count == 0) return;

        // Crown = highest actor index end
        int crownActor = -1;
        for (int i = 0; i < ends.Count; i++)
            if (ends[i] > crownActor) crownActor = ends[i];

        // Ensure cap pool size
        int targetCaps = Mathf.Max(0, ends.Count - 1);
        while (_caps.Count < targetCaps && bottomCapPrefab)
            _caps.Add(Instantiate(bottomCapPrefab, transform));
        for (int i = targetCaps; i < _caps.Count; i++)
            _caps[i].SetActive(false);

        // Place crown
        if (_topCrown)
        {
            PlaceAtEndExact(crownActor, _topCrown, pushOut, lateralOffset);
            _topCrown.SetActive(true);
        }

        // Place caps on all other free tips
        int capIdx = 0;
        for (int i = 0; i < ends.Count; i++)
        {
            int ai = ends[i];
            if (ai == crownActor) continue;

            if (capIdx < _caps.Count && _caps[capIdx])
            {
                _caps[capIdx].SetActive(true);
                PlaceAtEndExact(ai, _caps[capIdx], pushOut, lateralOffset);
                capIdx++;
            }
        }
    }

    // ---------- helpers ----------

    // Ends = degree==1 (tip) and not fixed (invMass > 0)
    List<int> GetFreeTips()
    {
        var ends = new List<int>(4);
        if (rope == null || rope.solver == null || rope.activeParticleCount == 0) return ends;

        // degree per actor
        var degree = new Dictionary<int, int>(rope.elements.Count * 2);
        for (int i = 0; i < rope.elements.Count; i++)
        {
            var e = rope.elements[i];
            if (!degree.ContainsKey(e.particle1)) degree[e.particle1] = 0;
            if (!degree.ContainsKey(e.particle2)) degree[e.particle2] = 0;
            degree[e.particle1]++; degree[e.particle2]++;
        }

        var siList = rope.solverIndices;
        var solver = rope.solver;

        for (int ai = 0; ai < rope.activeParticleCount; ai++)
        {
            if (!degree.TryGetValue(ai, out int d) || d != 1) continue;
            if (siList == null || ai >= siList.count) continue;

            int si = siList[ai];
            if (si < 0 || si >= solver.invMasses.count) continue;

            if (solver.invMasses[si] > 1e-6f) // not fixed
                ends.Add(ai);
        }
        return ends;
    }

    // Place object so its Anchor (if present) sits EXACTLY at the particle.
    void PlaceAtEndExact(int actorIndex, GameObject go, float outOffset, float sideOffset)
    {
        if (actorIndex < 0 || go == null) return;
        if (!TryGetParticleWorld(actorIndex, out var endP)) return;

        // choose a normal using neighbor to orient outward (does not affect exact pin)
        Vector3 nrm = Vector3.up;
        int nb = FindNeighborActor(actorIndex);
        if (nb >= 0 && TryGetParticleWorld(nb, out var nbP))
            nrm = (endP - nbP).sqrMagnitude > 1e-12f ? (endP - nbP).normalized : Vector3.up;

        // stable sideways for lateral offset
        Vector3 upHint = Vector3.up;
        Vector3 right = Vector3.Cross(upHint, nrm).normalized;
        if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(Vector3.forward, nrm).normalized;

        Vector3 finalPos = endP + nrm * outOffset + right * sideOffset;
        Quaternion finalRot = Quaternion.LookRotation(nrm, upHint);

        // If prefab has an "Anchor" child, place THAT at the particle and offset parent accordingly.
        Transform anchor = go.transform.Find("Anchor");
        if (anchor != null)
        {
            // Where would the parent need to be so that anchor lands at finalPos/finalRot?
            // P_world = Parent * AnchorLocal  ⇒  Parent = P_world * Inverse(AnchorLocal)
            Matrix4x4 A_local = Matrix4x4.TRS(anchor.localPosition, anchor.localRotation, anchor.localScale);
            Matrix4x4 P_world = Matrix4x4.TRS(finalPos, finalRot, Vector3.one);
            Matrix4x4 parentWorld = P_world * A_local.inverse;

            go.transform.SetPositionAndRotation(parentWorld.GetColumn(3), parentWorld.rotation);
        }
        else
        {
            // No anchor → just put the object itself at the exact particle pose
            go.transform.SetPositionAndRotation(finalPos, finalRot);
        }
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

    // Obi 7: renderablePositions are already in WORLD space
    bool TryGetParticleWorld(int actorIndex, out Vector3 worldPos)
    {
        worldPos = default;
        if (rope == null || rope.solver == null) return false;

        var siList = rope.solverIndices;
        if (siList == null || actorIndex < 0 || actorIndex >= siList.count) return false;

        int si = siList[actorIndex];
        var rp = rope.solver.renderablePositions;
        if (si < 0 || si >= rp.count) return false;

        var p4 = rp[si];
        worldPos = new Vector3(p4.x, p4.y, p4.z); // DO NOT TransformPoint()
        return true;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!drawEndGizmos || rope == null || rope.solver == null) return;
        var ends = GetFreeTips();
        Gizmos.color = Color.yellow;
        for (int i = 0; i < ends.Count; i++)
            if (TryGetParticleWorld(ends[i], out var p))
                Gizmos.DrawSphere(p, gizmoRadius);
    }
#endif
}
