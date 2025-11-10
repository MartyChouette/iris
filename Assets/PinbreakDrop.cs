// File: PinBreakSap.cs
// Unity 6 + Obi 7
// Fire a SapFxPool burst when an Obi pin breaks (hits its breakThreshold).
// Monitors one attachment + actorIndex safely at solver timing.

using UnityEngine;
using Obi;

[DefaultExecutionOrder(32000)]
[DisallowMultipleComponent]
public class PinBreakSap : MonoBehaviour
{
    [Header("Obi refs")]
    public ObiRope rope;
    [Tooltip("The pin/attachment that can break. Can live on the leaf or on the rope.")]
    public ObiParticleAttachment attachment;
    [Tooltip("Actor-space particle index that this listener cares about. -1 = auto (only if the group has exactly one index).")]
    public int actorIndex = -1;

    [Header("Sap FX")]
    public SapFxPool sapPool;
    public Camera cam;
    [Tooltip("If ON, aim sap toward camera. If OFF, use a stem tangent-derived normal.")]
    public bool faceTowardCamera = true;

    [Header("Debug")]
    public bool debug;

    // runtime
    bool _wasPinned = false;          // state at the start of a frame
    bool _init = false;
    int _solverIndex = -1;

    void Awake()
    {
        if (!rope) rope = GetComponentInParent<ObiRope>();
        if (!attachment) attachment = GetComponent<ObiParticleAttachment>();
        if (!cam) cam = Camera.main;

        if (!rope || !attachment)
        {
            if (debug) Debug.LogError("[PinBreakSap] Missing rope or attachment.", this);
            enabled = false; return;
        }
    }

    void OnEnable()
    {
        if (rope != null)
        {
            // Use solver callbacks to avoid reading solver arrays too early.
            rope.OnSimulationStart += OnSimStart;
            rope.OnSimulationEnd += OnSimEnd;
        }
    }

    void OnDisable()
    {
        if (rope != null)
        {
            rope.OnSimulationStart -= OnSimStart;
            rope.OnSimulationEnd -= OnSimEnd;
        }
        _init = false;
        _solverIndex = -1;
        _wasPinned = false;
    }

    // Safe time to resolve solver indices & initial state
    void OnSimStart(ObiActor actor, float stepTime, float substepTime)
    {
        if (!_init)
        {
            // Resolve actorIndex if requested.
            if (actorIndex < 0 &&
                attachment.particleGroup != null &&
                attachment.particleGroup.particleIndices != null &&
                attachment.particleGroup.particleIndices.Count == 1)
            {
                actorIndex = attachment.particleGroup.particleIndices[0];
                if (debug) Debug.Log($"[PinBreakSap] Auto actorIndex = {actorIndex}", this);
            }

            _solverIndex = ActorToSolverIndex(rope, actorIndex);
            _init = true;
        }

        _wasPinned = IsCurrentlyPinned();
    }

    // After solving, check whether the pin disappeared this step.
    void OnSimEnd(ObiActor actor, float stepTime, float substepTime)
    {
        bool nowPinned = IsCurrentlyPinned();

        if (_wasPinned && !nowPinned)
        {
            // The pin for our actorIndex broke during this step → fire sap.
            Vector3 pos = GetParticleWorld(rope, _solverIndex);
            Vector3 nrm = ComputeNormal(pos);
            if (sapPool != null)
                sapPool.Play(pos, nrm);

            if (debug) Debug.Log("[PinBreakSap] Pin broke → sap burst.", this);
        }

        _wasPinned = nowPinned;
    }

    // ---------- helpers ----------

    bool IsCurrentlyPinned()
    {
        if (!attachment || !attachment.enabled) return false;
        if (attachment.particleGroup == null || attachment.particleGroup.particleIndices == null) return false;
        // Consider it pinned only if our actor index is still in the group:
        return attachment.particleGroup.particleIndices.Contains(actorIndex);
    }

    Vector3 ComputeNormal(Vector3 pinWorld)
    {
        if (faceTowardCamera && cam != null) return -cam.transform.forward;

        // Try a stem tangent for a nicer directional burst:
        if (_solverIndex >= 0)
        {
            Vector3 t = EstimateTangent(rope, _solverIndex, pinWorld);
            Vector3 up = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(up, t)) > 0.98f) up = Vector3.right;
            return Vector3.Cross(t, up).normalized; // perpendicular to the stem
        }
        return Vector3.up;
    }

    static Vector3 EstimateTangent(ObiRope r, int solverIdx, Vector3 fallback)
    {
        // Find immediate neighbor in the rope elements and use segment direction
        int ai = SolverToActorIndex(r, solverIdx);
        if (ai < 0) return Vector3.forward;

        // Linear scan (short ropes are cheap)
        foreach (var e in r.elements)
        {
            if (e.particle1 == ai || e.particle2 == ai)
            {
                int otherAi = (e.particle1 == ai) ? e.particle2 : e.particle1;
                int otherSi = ActorToSolverIndex(r, otherAi);
                Vector3 a = GetParticleWorld(r, solverIdx);
                Vector3 b = GetParticleWorld(r, otherSi);
                Vector3 dir = (b - a);
                if (dir.sqrMagnitude > 1e-6f) return dir.normalized;
            }
        }
        return Vector3.forward;
    }

    static int ActorToSolverIndex(ObiRope r, int actorIdx)
    {
        if (r == null || actorIdx < 0) return -1;
#if OBI_NATIVE_COLLECTIONS
        return (actorIdx < r.solverIndices.count) ? r.solverIndices[actorIdx] : -1;
#else
        return (r.solverIndices != null && actorIdx < r.solverIndices.count) ? r.solverIndices[actorIdx] : -1;
#endif
    }

    static int SolverToActorIndex(ObiRope r, int solverIdx)
    {
#if OBI_NATIVE_COLLECTIONS
        // Obi doesn't expose a direct reverse map; linear search is fine for short stems:
        for (int ai = 0; ai < r.activeParticleCount; ai++)
            if (r.solverIndices[ai] == solverIdx) return ai;
#else
        if (r.solverIndices != null)
            for (int ai = 0; ai < r.solverIndices.count; ai++)
                if (r.solverIndices[ai] == solverIdx) return ai;
#endif
        return -1;
    }

    static Vector3 GetParticleWorld(ObiRope r, int solverIdx)
    {
        if (r == null || r.solver == null || solverIdx < 0) return Vector3.zero;
        var s = r.solver;
#if OBI_NATIVE_COLLECTIONS
        if (s.renderablePositions.count > solverIdx)
            return s.transform.TransformPoint((Vector3)s.renderablePositions[solverIdx]);
        if (s.positions.count > solverIdx)
            return s.transform.TransformPoint((Vector3)s.positions[solverIdx]);
#else
        if (s.renderablePositions != null && s.renderablePositions.count > solverIdx)
            return s.transform.TransformPoint((Vector3)s.renderablePositions[solverIdx]);
        if (s.positions != null && s.positions.count > solverIdx)
            return s.transform.TransformPoint((Vector3)s.positions[solverIdx]);
#endif
        return Vector3.zero;
    }
}
