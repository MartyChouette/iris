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

    [Tooltip("Prefab for the flower crown. Prefer attaching to the original top particle.")]
    public GameObject topCrownPrefab;

    [Header("Placement (all ends)")]
    [Tooltip("Meters to push outward along the end normal. Set 0 while validating.")]
    public float pushOut = 0f;

    [Tooltip("Meters of sideways offset. Set 0 while validating.")]
    public float lateralOffset = 0f;

    [Tooltip("Delay so the rope binds to the solver before we arm.")]
    public float armDelay = 0.1f;

    [Header("Crown targeting")]
    [Tooltip("If ON, the crown locks to the original top actor index captured after armDelay.")]
    public bool lockCrownToOriginalTop = true;

    [Tooltip("Optional override. If >= 0, force this actor index as the crown target.")]
    public int forcedTopActorIndex = -1;

    [Tooltip("World-space vertical (Y) nudge for the crown only (meters).")]
    public float crownVerticalOffset = 0f;

    [Header("Debug")]
    public bool drawEndGizmos = true;
    public float gizmoRadius = 0.004f;

    // runtime
    GameObject _topCrown;
    readonly List<GameObject> _caps = new(); // one cap per non-crown end
    int _originalTopActorIndex = -1;
    bool _armed = false;

    void Reset() { if (!rope) rope = GetComponent<ObiRope>(); }

    IEnumerator Start()
    {
        float t = 0f;
        while (t < armDelay || rope == null || rope.solver == null || rope.activeParticleCount == 0)
        { t += Time.deltaTime; yield return null; }

        // Capture the original top actor index once (highest actor index among initial free tips).
        _originalTopActorIndex = FindInitialTopActorIndex();
        _armed = true;

        if (topCrownPrefab) _topCrown = Instantiate(topCrownPrefab, transform);
    }

    void LateUpdate()
    {
        if (!_armed || !rope || rope.solver == null || rope.activeParticleCount == 0) return;

        var ends = GetFreeTips();               // current free tips (degree==1 & not fixed)
        if (ends.Count == 0 && _topCrown == null) return;

        // Decide crown target actor index
        int crownActor = ResolveCrownActor(ends);

        // Ensure cap pool size (ends minus crown if crown is a free tip; harmless if not)
        int targetCaps = Mathf.Max(0, ends.Count - (ends.Contains(crownActor) ? 1 : 0));
        while (_caps.Count < targetCaps && bottomCapPrefab)
            _caps.Add(Instantiate(bottomCapPrefab, transform));
        for (int i = 0; i < _caps.Count; i++)
            _caps[i].SetActive(i < targetCaps);

        // Place crown (always, even if its actor isn't a current free tip)
        if (_topCrown)
        {
            PlaceAtEndExact(crownActor, _topCrown, pushOut, lateralOffset, crownVerticalOffset);
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
                PlaceAtEndExact(ai, _caps[capIdx], pushOut, lateralOffset, 0f);
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

    // Capture once at arm time: pick the highest-index free tip if possible; otherwise fallback to last actor.
    int FindInitialTopActorIndex()
    {
        var ends = GetFreeTips();
        int best = -1;
        for (int i = 0; i < ends.Count; i++)
            if (ends[i] > best) best = ends[i];

        if (best >= 0) return best;

        // Fallback: last actor index by count-1
        return Mathf.Max(0, rope.activeParticleCount - 1);
    }

    // Decide which actor index to use for the crown *this frame*
    int ResolveCrownActor(List<int> currentEnds)
    {
        // Hard override
        if (forcedTopActorIndex >= 0) return forcedTopActorIndex;

        // Preferred: the original top we captured
        if (lockCrownToOriginalTop && TryGetParticleWorld(_originalTopActorIndex, out _))
            return _originalTopActorIndex;

        // Fallback: current highest-index free tip
        int crownActor = -1;
        for (int i = 0; i < currentEnds.Count; i++)
            if (currentEnds[i] > crownActor) crownActor = currentEnds[i];

        if (crownActor >= 0) return crownActor;

        // Final fallback: last actor index (even if not a free tip)
        return Mathf.Max(0, rope.activeParticleCount - 1);
    }

    // Place object so its Anchor (if present) sits EXACTLY at the particle.
    // Adds world-space Y nudge via verticalOffset for the crown.
    void PlaceAtEndExact(int actorIndex, GameObject go, float outOffset, float sideOffset, float verticalOffset)
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

        Vector3 finalPos = endP + nrm * outOffset + right * sideOffset + Vector3.up * verticalOffset;
        Quaternion finalRot = Quaternion.LookRotation(nrm, upHint);

        // If prefab has an "Anchor" child, place THAT at the particle and offset parent accordingly.
        Transform anchor = go.transform.Find("Anchor");
        if (anchor != null)
        {
            Matrix4x4 A_local = Matrix4x4.TRS(anchor.localPosition, anchor.localRotation, anchor.localScale);
            Matrix4x4 P_world = Matrix4x4.TRS(finalPos, finalRot, Vector3.one);
            Matrix4x4 parentWorld = P_world * A_local.inverse;

            go.transform.SetPositionAndRotation(parentWorld.GetColumn(3), parentWorld.rotation);
        }
        else
        {
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
