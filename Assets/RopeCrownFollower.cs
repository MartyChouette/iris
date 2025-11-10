// File: RopeCrownAttachByIndex.cs
// Follow a specific actor-space particle on an Obi rope/rod.
// Robust to solver rebuilds, cuts, and both native/managed Obi buffers.

using UnityEngine;
using Obi;


public class RopeCrownFollower : MonoBehaviour
{
    [Header("Obi")]
    public ObiRopeBase rope;
    [Tooltip("Actor-space particle index on the rope to follow.")]
    public int actorIndex = 0;

    [Header("Offsets")]
    public float alongTangent = 0f;  // forward/back along rope
    public float lateral = 0f;       // right from rope
    public float liftY = 0f;         // world up

    [Header("Smoothing")]
    public float posLerp = 20f;
    public float rotLerp = 18f;

    [Header("Orientation")]
    public bool faceAlongRope = true;
    public Vector3 fallbackUp = Vector3.up;

    // runtime
    bool _ready;
    int _solverIndex = -1;
    int _lastActiveCount = -1;

    void Reset() { if (!rope) rope = GetComponentInParent<ObiRopeBase>(); }
    void OnEnable() { _ready = false; StartCoroutine(InitWhenRopeReady()); }
    void OnDisable() { _ready = false; _solverIndex = -1; }

    System.Collections.IEnumerator InitWhenRopeReady()
    {
        while (!IsRopeReady(rope)) yield return null;
        MapActorToSolver();
        _ready = (_solverIndex >= 0);
    }

    void LateUpdate()
    {
        if (!_ready || rope == null) return;

        // if rope rebuilt (cut/loaded), remap once:
        if (rope.activeParticleCount != _lastActiveCount) { MapActorToSolver(); if (!_ready) return; }

        if (!TryGetWorld(rope, _solverIndex, out var p, out var tan)) return;

        // local frame on rope:
        Vector3 t = tan.sqrMagnitude > 1e-6f ? tan.normalized : Vector3.forward;
        Vector3 up = fallbackUp;
        if (Mathf.Abs(Vector3.Dot(up, t)) > 0.98f) up = Vector3.right;
        Vector3 right = Vector3.Cross(up, t).normalized;

        Vector3 targetPos = p + t * alongTangent + right * lateral + Vector3.up * liftY;
        Quaternion targetRot = faceAlongRope ? Quaternion.LookRotation(t, up) : transform.rotation;

        float kp = 1f - Mathf.Exp(-posLerp * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, targetPos, kp);

        if (faceAlongRope)
        {
            float kr = 1f - Mathf.Exp(-rotLerp * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, kr);
        }
    }

    // ---------- mapping & safety ----------
    void MapActorToSolver()
    {
        _lastActiveCount = rope.activeParticleCount;
        _solverIndex = ActorToSolverIndex(rope, actorIndex);
        _ready = SolverIndexValid(rope, _solverIndex);
    }

    static bool IsRopeReady(ObiRopeBase r)
    {
        if (r == null || r.solver == null) return false;
#if OBI_NATIVE_COLLECTIONS
        return r.solver.positions.count > 0 && r.solverIndices.count > 0 && r.activeParticleCount > 0;
#else
        return r.solver.positions != null && r.solver.positions.count > 0 &&
               r.solverIndices != null && r.solverIndices.count > 0 &&
               r.activeParticleCount > 0;
#endif
    }

    static int ActorToSolverIndex(ObiRopeBase r, int ai)
    {
        if (r == null || ai < 0) return -1;
#if OBI_NATIVE_COLLECTIONS
        return (ai < r.solverIndices.count) ? r.solverIndices[ai] : -1;
#else
        return (r.solverIndices != null && ai < r.solverIndices.count) ? r.solverIndices[ai] : -1;
#endif
    }

    static bool SolverIndexValid(ObiRopeBase r, int si)
    {
        if (r == null || r.solver == null || si < 0) return false;
#if OBI_NATIVE_COLLECTIONS
        return si < r.solver.positions.count;
#else
        return r.solver.positions != null && si < r.solver.positions.count;
#endif
    }

    static bool TryGetWorld(ObiRopeBase r, int solverIdx, out Vector3 pos, out Vector3 tangent)
    {
        pos = default; tangent = Vector3.forward;
        if (!SolverIndexValid(r, solverIdx)) return false;

        var s = r.solver;

#if OBI_NATIVE_COLLECTIONS
        pos = s.transform.TransformPoint((Vector3)s.renderablePositions[solverIdx]);
#else
        pos = s.transform.TransformPoint((Vector3)s.renderablePositions[solverIdx]);
#endif

        // estimate tangent using a neighbor actor index
        int ai = SolverToActorIndex(r, solverIdx);
        int neighborAi = Mathf.Clamp(ai + (ai == 0 ? +1 : -1), 0, r.activeParticleCount - 1);
        int nsi = ActorToSolverIndex(r, neighborAi);
        if (!SolverIndexValid(r, nsi)) { tangent = Vector3.forward; return true; }

#if OBI_NATIVE_COLLECTIONS
        Vector3 pn = s.transform.TransformPoint((Vector3)s.renderablePositions[nsi]);
#else
        Vector3 pn = s.transform.TransformPoint((Vector3)s.renderablePositions[nsi]);
#endif
        tangent = (ai == 0 ? (pn - pos) : (pos - pn));
        return true;
    }

    static int SolverToActorIndex(ObiRopeBase r, int solverIdx)
    {
#if OBI_NATIVE_COLLECTIONS
        // Obi doesn't provide a direct inverse map; linear scan is fine for short stems.
        for (int ai = 0; ai < r.activeParticleCount; ai++)
            if (ai < r.solverIndices.count && r.solverIndices[ai] == solverIdx) return ai;
#else
        for (int ai = 0; ai < r.activeParticleCount; ai++)
            if (r.solverIndices != null && ai < r.solverIndices.count && r.solverIndices[ai] == solverIdx) return ai;
#endif
        return 0;
    }
}
