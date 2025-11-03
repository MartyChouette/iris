using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Obi;

[DisallowMultipleComponent]
public class StemEndCaps : MonoBehaviour
{
    [Header("Refs")]
    public ObiRope rope;

    [Tooltip("Prefab for the bottom cover (cap).")]
    public GameObject bottomCapPrefab;

    [Tooltip("Prefab for the top crown object (flower crown).")]
    public GameObject topCrownPrefab;

    [Tooltip("World-space anchor near the flower head. The top crown locks to the end closest to this.")]
    public Transform headHint;

    [Header("Placement")]
    [Tooltip("Small push outward along the end normal (meters).")]
    public float pushOut = 0.003f;

    [Tooltip("Extra lateral offset (meters), e.g. crown thickness.")]
    public float lateralOffset = 0f;

    [Tooltip("Delay to allow rope to bind to solver before arming.")]
    public float armDelay = 0.15f;

    // runtime
    GameObject _bottomCap, _topCrown;
    int _armedBottomActor = -1, _armedTopActor = -1; // kept for fallback

    void Reset() { if (!rope) rope = GetComponent<ObiRope>(); }

    IEnumerator Start()
    {
        // wait for solver to be ready
        float t = 0f;
        while (t < armDelay || rope == null || rope.solver == null || rope.activeParticleCount == 0)
        {
            t += Time.deltaTime; yield return null;
        }

        if (bottomCapPrefab) _bottomCap = Instantiate(bottomCapPrefab, transform);
        if (topCrownPrefab) _topCrown = Instantiate(topCrownPrefab, transform);

        var ends = GetEndActorIndices();
        if (ends.Count == 0) yield break;

        // Arm using headHint if available, else original Y-based pick
        if (headHint && TryPickEndsWithHeadHint(ends, out _armedTopActor, out _armedBottomActor))
        {
            PositionCaps();
        }
        else
        {
            PickInitialEndsByY(ends, out _armedBottomActor, out _armedTopActor);
            PositionCaps();
        }
    }

    void LateUpdate()
    {
        if (!rope || rope.solver == null) return;
        PositionCaps();
    }

    void PositionCaps()
    {
        var ends = GetEndActorIndices();
        if (ends.Count == 0) return;

        int currTop = -1;
        int currBottom = -1;

        // --- Hard lock: top crown always follows the end closest to headHint ---
        if (headHint)
        {
            currTop = PickEndClosestToPoint(ends, headHint.position);
            currBottom = PickOtherEndFarthestFrom(ends, currTop);
        }
        else
        {
            // Fallback to previous behavior if no head hint is provided
            currTop = ChooseClosestEndByActorIndex(ends, _armedTopActor, preferHighestY: true);
            currBottom = ChooseClosestEndByActorIndex(ends, _armedBottomActor, preferLowestY: true);

            // If both collapse to the same end, push bottom to the farthest other end
            if (currBottom == currTop)
                currBottom = PickOtherEndFarthestFrom(ends, currTop);
        }

        if (_topCrown) PlaceAtEnd(currTop, _topCrown, pushOut, lateralOffset);
        if (_bottomCap) PlaceAtEnd(currBottom, _bottomCap, pushOut, lateralOffset);
    }

    // --- helpers ---

    // Build the list of actor indices whose degree==1 and invMass>0 (free tips)
    List<int> GetEndActorIndices()
    {
        var ends = new List<int>(4);
        if (rope == null || rope.solver == null || rope.activeParticleCount == 0) return ends;

        // degree per actor using rope.elements
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

    // Arm using head hint: top = closest to headHint, bottom = other end farthest from top
    bool TryPickEndsWithHeadHint(List<int> ends, out int topActor, out int bottomActor)
    {
        topActor = bottomActor = -1;
        if (!headHint || ends.Count == 0) return false;

        topActor = PickEndClosestToPoint(ends, headHint.position);
        bottomActor = PickOtherEndFarthestFrom(ends, topActor);

        // Store arm references for fallback paths (in case headHint later nulls)
        _armedTopActor = topActor;
        _armedBottomActor = bottomActor;

        return topActor >= 0;
    }

    int PickEndClosestToPoint(List<int> ends, Vector3 point)
    {
        int best = -1; float bestD = float.PositiveInfinity;
        foreach (var ai in ends)
        {
            if (!TryGetActorWorld(ai, out var p)) continue;
            float d = (p - point).sqrMagnitude;
            if (d < bestD) { bestD = d; best = ai; }
        }
        return best;
    }

    int PickOtherEndFarthestFrom(List<int> ends, int refEnd)
    {
        if (ends.Count == 0) return -1;
        if (refEnd < 0)
            return ends[0];

        if (!TryGetActorWorld(refEnd, out var refPos))
        {
            // if we can't get ref world pos, just pick a different end
            foreach (var ai in ends) if (ai != refEnd) return ai;
            return refEnd;
        }

        int pick = refEnd;
        float best = -1f;
        foreach (var ai in ends)
        {
            if (ai == refEnd) continue;
            if (!TryGetActorWorld(ai, out var p)) continue;
            float d = (p - refPos).sqrMagnitude;
            if (d > best) { best = d; pick = ai; }
        }
        return pick;
    }

    // Original Y-based arming (fallback)
    void PickInitialEndsByY(List<int> ends, out int bottomActor, out int topActor)
    {
        bottomActor = topActor = -1;
        float minY = float.MaxValue, maxY = float.MinValue;

        foreach (int ai in ends)
        {
            if (!TryGetActorWorld(ai, out var p)) continue;
            if (p.y < minY) { minY = p.y; bottomActor = ai; }
            if (p.y > maxY) { maxY = p.y; topActor = ai; }
        }

        if (topActor < 0) topActor = bottomActor;
        if (bottomActor < 0) bottomActor = topActor;
    }

    // Choose closest by actor index, with optional Y bias (fallback path)
    int ChooseClosestEndByActorIndex(List<int> ends, int refActor, bool preferLowestY = false, bool preferHighestY = false)
    {
        if (ends.Count == 0) return -1;

        if (refActor >= 0)
        {
            int best = ends[0]; int bestDiff = Mathf.Abs(ends[0] - refActor);
            for (int i = 1; i < ends.Count; i++)
            {
                int diff = Mathf.Abs(ends[i] - refActor);
                if (diff < bestDiff) { bestDiff = diff; best = ends[i]; }
            }
            return best;
        }

        if (preferLowestY || preferHighestY)
        {
            int pick = ends[0]; TryGetActorWorld(pick, out var p0);
            float bestY = p0.y;

            for (int i = 1; i < ends.Count; i++)
            {
                if (!TryGetActorWorld(ends[i], out var p)) continue;
                if (preferLowestY && p.y < bestY) { bestY = p.y; pick = ends[i]; }
                if (preferHighestY && p.y > bestY) { bestY = p.y; pick = ends[i]; }
            }
            return pick;
        }

        return ends[0];
    }

    void PlaceAtEnd(int actorIndex, GameObject go, float outOffset, float sideOffset)
    {
        if (actorIndex < 0 || go == null) return;

        if (!TryGetActorWorld(actorIndex, out var endP)) return;

        int neighborActor = FindNeighborActor(actorIndex);
        Vector3 nrm = Vector3.up;
        if (neighborActor >= 0 && TryGetActorWorld(neighborActor, out var nbP))
        {
            // outward from neighbor → end (so cap 'faces out' of the rope tip)
            nrm = (endP - nbP).normalized;
        }

        // Stable lateral for a pretty sideways offset
        Vector3 upHint = Vector3.up;
        Vector3 right = Vector3.Cross(upHint, nrm).normalized;
        if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(Vector3.forward, nrm).normalized;

        Vector3 pos = endP + nrm * outOffset + right * sideOffset;
        Quaternion rot = Quaternion.LookRotation(nrm, upHint);

        go.transform.SetPositionAndRotation(pos, rot);
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

    bool TryGetActorWorld(int actorIndex, out Vector3 worldPos)
    {
        worldPos = default;
        if (rope == null || rope.solver == null) return false;

        var siList = rope.solverIndices;
        if (siList == null || actorIndex < 0 || actorIndex >= siList.count) return false;

        int si = siList[actorIndex];
        var rp = rope.solver.renderablePositions;
        if (si < 0 || si >= rp.count) return false;

        var p4 = rp[si];
        worldPos = rope.solver.transform.TransformPoint(new Vector3(p4.x, p4.y, p4.z));
        return true;
    }
}
