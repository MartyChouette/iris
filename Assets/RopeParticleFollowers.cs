// File: RopeLeafFollower_SimplePin.cs
// Follow the exact pinned particle (from ObiParticleAttachment) + preserve authored offset.
// No rotation changes. Robust to solver not-yet-ready. Minimal moving parts.

using UnityEngine;
using Obi;

[DefaultExecutionOrder(32000)]
[DisallowMultipleComponent]
public class RopeParticleFollowers : MonoBehaviour
{
    [Header("Refs")]
    public ObiRopeBase rope;                         // your stem rope/rod
    [Tooltip("If present, we'll auto-read the first particle index from this attachment.")]
    public ObiParticleAttachment attachment;         // usually on the leaf itself

    [Header("Binding")]
    [Tooltip("Use the first index in the Attachment's particleGroup on Start/when solver loads.")]
    public bool autoDetectFromAttachment = true;

    [Tooltip("Actor-space particle index. Ignored if autoDetectFromAttachment is true and an attachment is provided.")]
    public int actorIndex = -1;                      // -1 = not set

    [Header("Offset / Feel")]
    [Tooltip("Captured automatically at first valid frame: leafPose - pinWorld.")]
    public Vector3 authoredOffset = Vector3.zero;    // you can tweak in Inspector after play starts
    [Tooltip("Extra nudge applied on top of authoredOffset.")]
    public Vector3 extraOffsetWorld = Vector3.zero;
    [Tooltip("Higher = snappier follow. 0 disables smoothing (teleports).")]
    public float posLerp = 30f;

    [Header("Rebind / Robustness")]
    [Tooltip("If we temporarily lose validity (cut/split), try to rebind from the attachment (or keep last actorIndex).")]
    public bool autoRebindWhenInvalid = true;

    [Header("Debug")]
    public bool debug = false;

    // ── runtime ──
    bool _offsetCaptured = false;
    bool _everValid = false;

    void Reset()
    {
        rope = GetComponentInParent<ObiRopeBase>();
        attachment = GetComponent<ObiParticleAttachment>();
    }

    void Awake()
    {
        if (!rope) rope = GetComponentInParent<ObiRopeBase>();
        if (!attachment) attachment = GetComponent<ObiParticleAttachment>();
    }

    void OnEnable()
    {
        if (rope != null)
            rope.OnSimulationStart += OnSimStart; // fired when solver steps → safe to bind
    }

    void OnDisable()
    {
        if (rope != null)
            rope.OnSimulationStart -= OnSimStart;
        _offsetCaptured = false;
        _everValid = false;
    }

    void OnSimStart(ObiActor a, float step, float substep)
    {
        TryAutoDetectActorIndex();
        // We'll capture the offset in LateUpdate once the first valid pin world position exists.
    }

    void Start()
    {
        TryAutoDetectActorIndex(); // safety if solver already running
    }

    void TryAutoDetectActorIndex()
    {
        if (!autoDetectFromAttachment || attachment == null) return;

        var grp = attachment.particleGroup;
        if (grp != null && grp.particleIndices != null && grp.particleIndices.Count > 0)
        {
            actorIndex = grp.particleIndices[0];
            if (debug) Debug.Log($"[LeafFollower] Auto actorIndex = {actorIndex}", this);
        }
    }

    void LateUpdate()
    {
        if (rope == null || rope.solver == null) return;

        int solverIndex = ActorToSolverIndex(rope, actorIndex);
        if (!ValidSolverIndex(rope, solverIndex))
        {
            if (debug && _everValid) Debug.Log("[LeafFollower] Lost validity; attempting rebind…", this);
            if (autoRebindWhenInvalid)
            {
                // Try refresh from attachment again (handles cuts where groups are updated externally)
                TryAutoDetectActorIndex();
                solverIndex = ActorToSolverIndex(rope, actorIndex);
                if (!ValidSolverIndex(rope, solverIndex)) return; // still invalid; wait for next frame
            }
            else return;
        }

        _everValid = true;

        Vector3 pinWorld = GetParticleWorld(rope, solverIndex);

        // Capture your original offset once (from current pose)
        if (!_offsetCaptured)
        {
            authoredOffset = transform.position - pinWorld;
            _offsetCaptured = true;
            if (debug) Debug.Log($"[LeafFollower] Captured offset = {authoredOffset}", this);
        }

        Vector3 target = pinWorld + authoredOffset + extraOffsetWorld;

        float k = (posLerp <= 0f) ? 1f : (1f - Mathf.Exp(-posLerp * Time.deltaTime));
        transform.position = Vector3.Lerp(transform.position, target, k);
        // Intentionally never touch rotation.
    }

    // ── helpers ──

    static int ActorToSolverIndex(ObiRopeBase r, int actorIdx)
    {
        if (r == null || actorIdx < 0) return -1;
#if OBI_NATIVE_COLLECTIONS
        return (actorIdx < r.solverIndices.count) ? r.solverIndices[actorIdx] : -1;
#else
        return (r.solverIndices != null && actorIdx < r.solverIndices.count) ? r.solverIndices[actorIdx] : -1;
#endif
    }

    static bool ValidSolverIndex(ObiRopeBase r, int solverIdx)
    {
        if (r == null || r.solver == null || solverIdx < 0) return false;
#if OBI_NATIVE_COLLECTIONS
        return solverIdx < r.solver.renderablePositions.count || solverIdx < r.solver.positions.count;
#else
        var s = r.solver;
        return (s.renderablePositions != null && solverIdx < s.renderablePositions.count) ||
               (s.positions != null && solverIdx < s.positions.count);
#endif
    }

    static Vector3 GetParticleWorld(ObiRopeBase r, int solverIdx)
    {
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
        return r.transform.position; // fallback
    }
}
