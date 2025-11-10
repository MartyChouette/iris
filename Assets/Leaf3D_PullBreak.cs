using UnityEngine;
using Obi;

/// Pull a leaf away from its pinned rope particle; when beyond breakDistance,
/// detach ONLY that particle's attachment and let physics take over.
/// Works whether the ObiParticleAttachment lives on the leaf (preferred)
/// or on the rope (shared). Unity 6 + Obi 7.
[DefaultExecutionOrder(32000)]
[DisallowMultipleComponent]
public class Leaf3D_PullBreak : MonoBehaviour
{
    [Header("Scene refs")]
    public ObiRope rope;                       // stem rope
    [Tooltip("Actor-space particle index pinned for this leaf. -1 = auto from attachment group")]
    public int actorIndex = -1;
    public ObiParticleAttachment attachment;   // REQUIRED: the pin that includes this particle
    public Rigidbody rb;                       // leaf rigidbody
    public SapFxPool sapPool;                  // optional
    public Camera cam;

    [Header("Detach settings")]
    [Min(0.001f)] public float breakDistance = 0.12f;       // meters
    [Tooltip("Extra time beyond breakDistance before detaching (debounce).")]
    public float breakDwellSeconds = 0.03f;
    public Vector3 breakImpulse = new Vector3(0f, 0.8f, 0f);

    [Header("Arming (prevents instant break on spawn)")]
    [Tooltip("Must come within this distance of the pin once before breaking is allowed.")]
    public float armThreshold = 0.03f;                       // < breakDistance
    public bool onlyWhileGrabbed = false;                    // set true if you arm via BeginGrab/EndGrab

    [Header("Debug")]
    public bool debug = true;

    // runtime
    bool _detached = false, _armed = false, _grabbed = false;
    int _solverIndex = -1;
    float _beyondTimer = 0f;

    void Awake()
    {
        if (!rope) rope = GetComponentInParent<ObiRope>();
        if (!attachment) attachment = GetComponent<ObiParticleAttachment>();
        if (!rb) rb = GetComponent<Rigidbody>();
        if (!cam) cam = Camera.main;

        if (!rope || !attachment)
        {
            if (debug) Debug.LogError("[Leaf3D_PullBreak] Missing rope or attachment.", this);
            enabled = false; return;
        }

        // Auto-pull the actorIndex from the attachment’s particle group if needed:
        if (actorIndex < 0 && attachment.particleGroup != null &&
            attachment.particleGroup.particleIndices != null &&
            attachment.particleGroup.particleIndices.Count > 0)
        {
            actorIndex = attachment.particleGroup.particleIndices[0];
            if (debug) Debug.Log($"[Leaf3D_PullBreak] Auto actorIndex = {actorIndex}", this);
        }

        _solverIndex = ActorToSolverIndex(rope, actorIndex);
        if (_solverIndex < 0 && debug)
            Debug.LogWarning($"[Leaf3D_PullBreak] actorIndex {actorIndex} not active on solver.", this);

        if (rb)
        {
            rb.isKinematic = true;
            rb.detectCollisions = false;
            rb.interpolation = RigidbodyInterpolation.None;
        }

        TryAutoArm();
        ValidateAttachmentOwnership();
    }

    void LateUpdate()
    {
        if (_detached || rope == null || attachment == null) return;

        if (onlyWhileGrabbed && !_grabbed) { TryAutoArm(); return; }
        if (!_armed) { TryAutoArm(); return; }

        Vector3 pinWorld = GetParticleWorld(rope, _solverIndex);
        float dist = Vector3.Distance(transform.position, pinWorld);

        if (debug) Debug.Log($"[LeafBreak] dist={dist:F3}/{breakDistance} armed={_armed} grabbed={_grabbed}", this);

        if (dist >= breakDistance)
        {
            _beyondTimer += Time.deltaTime;
            if (_beyondTimer >= breakDwellSeconds)
                DetachNow(pinWorld);
        }
        else _beyondTimer = 0f;
    }

    // ——— public hooks if using a grabber ———
    public void BeginGrab() { _grabbed = true; if (onlyWhileGrabbed && !_armed) { _armed = true; _beyondTimer = 0f; } }
    public void EndGrab() { _grabbed = false; }

    // ——— detach implementation ———
    void DetachNow(Vector3 pinWorld)
    {
        if (_detached) return;
        _detached = true;

        if (IsAttachmentLocalToLeaf())
        {
            // Attachment component is on this leaf → safe to disable whole component.
            attachment.enabled = false;
            if (debug) Debug.Log("[LeafBreak] Disabled local attachment.", this);
        }
        else
        {
            // Shared attachment (likely on the rope): remove ONLY our particle from the group.
            RemoveActorFromAttachmentGroup(attachment, actorIndex);
            if (debug) Debug.Log($"[LeafBreak] Removed actor {actorIndex} from shared attachment.", this);
            // If the group ends up empty, you can disable the component:
            if (attachment.particleGroup != null &&
                (attachment.particleGroup.particleIndices == null ||
                 attachment.particleGroup.particleIndices.Count == 0))
            {
                attachment.enabled = false;
            }
            // Tell Obi constraints changed:
            rope.SetConstraintsDirty(Oni.ConstraintType.Pin);
        }

        if (rb)
        {
            rb.isKinematic = false;
            rb.detectCollisions = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.AddForce(breakImpulse, ForceMode.VelocityChange);
        }

        if (sapPool)
        {
            Vector3 normal = cam ? -cam.transform.forward : Vector3.up;
            sapPool.Play(transform.position, normal);
        }
    }

    // ——— helpers ———
    void TryAutoArm()
    {
        if (_armed || attachment == null || !attachment.enabled) return;
        Vector3 pinWorld = GetParticleWorld(rope, _solverIndex);
        float dist = Vector3.Distance(transform.position, pinWorld);
        if (dist <= armThreshold || (onlyWhileGrabbed && _grabbed))
        {
            _armed = true;
            _beyondTimer = 0f;
            if (debug) Debug.Log($"[LeafBreak] ARMED (dist={dist:F3} <= {armThreshold}).", this);
        }
    }

    bool IsAttachmentLocalToLeaf()
    {
        // safest simple test: the attachment component is on this same GameObject
        return attachment.gameObject == this.gameObject;
    }

    void ValidateAttachmentOwnership()
    {
        if (!IsAttachmentLocalToLeaf() && debug)
        {
            Debug.LogWarning(
                $"[LeafBreak] Attachment is not on this leaf GameObject. " +
                $"Will remove ONLY actor {actorIndex} from its particle group instead of disabling the whole component.",
                attachment);
        }

        // Also verify our actorIndex exists in that group:
        if (attachment.particleGroup == null ||
            attachment.particleGroup.particleIndices == null ||
            !attachment.particleGroup.particleIndices.Contains(actorIndex))
        {
            Debug.LogWarning(
                $"[LeafBreak] actorIndex {actorIndex} not found in attachment group '{attachment.name}'. " +
                $"Ensure this attachment actually pins this leaf’s particle.", this);
        }
    }

    static void RemoveActorFromAttachmentGroup(ObiParticleAttachment a, int actorIdx)
    {
        if (a == null || a.particleGroup == null || a.particleGroup.particleIndices == null) return;
        var list = a.particleGroup.particleIndices;
        for (int i = list.Count - 1; i >= 0; --i)
            if (list[i] == actorIdx) list.RemoveAt(i);
    }

    static int ActorToSolverIndex(ObiRope r, int actorIdx)
    {
        if (r == null || actorIdx < 0) return -1;
#if OBI_NATIVE_COLLECTIONS
        if (actorIdx < r.solverIndices.count) return r.solverIndices[actorIdx];
#else
        if (r.solverIndices != null && actorIdx < r.solverIndices.count) return r.solverIndices[actorIdx];
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
        return s.transform.TransformPoint((Vector3)s.positions[solverIdx]);
#else
        if (s.renderablePositions != null && s.renderablePositions.count > solverIdx)
            return s.transform.TransformPoint((Vector3)s.renderablePositions[solverIdx]);
        return s.transform.TransformPoint((Vector3)s.positions[solverIdx]);
#endif
    }
}
