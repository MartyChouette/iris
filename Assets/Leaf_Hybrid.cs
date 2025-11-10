// File: LeafHybrid.cs
// Unity 6 + Obi 7
// Tug while attached (pin active). When pulled beyond breakDistance for breakDwellSeconds:
//  - remove only this leaf's pinned particle from its ObiParticleAttachment group (or disable local attachment),
//  - enable physics and nudge (or keep kinematic while still grabbed, if desired),
//  - play sap on break,
//  - also play sap on first collisions after detach (cooldown).

using UnityEngine;
using Obi;

[DefaultExecutionOrder(32000)]
[DisallowMultipleComponent]
public class LeafHybrid : MonoBehaviour
{
    [Header("Obi / Scene")]
    public ObiRope rope;                        // the stem rope
    [Tooltip("Actor-space particle index for this leaf. -1 = auto from attachment group (requires exactly 1 index there).")]
    public int actorIndex = -1;
    [Tooltip("Attachment that pins this leaf's particle.")]
    public ObiParticleAttachment attachment;    // prefer on the leaf; shared on rope also supported
    public Rigidbody rb;                        // leaf rigidbody
    public SapFxPool sapPool;                   // optional sap pool
    public Camera cam;

    [Header("Break settings")]
    [Min(0.001f)] public float breakDistance = 0.12f;   // meters (adjust to your scale)
    [Min(0f)] public float breakDwellSeconds = 0.05f; // continuous time beyond distance before break
    [Tooltip("Must come within this distance of the pin once before breaking is allowed (prevents instant pop).")]
    public float armThreshold = 0.03f;                  // < breakDistance
    public Vector3 breakImpulse = new Vector3(0f, 0.8f, 0f);
    public bool stayInHandAfterBreak = true;            // if true, keep kinematic until you release after tearing

    [Header("Collision Sap")]
    public float collisionBurstCooldown = 0.15f;

    [Header("Debug")]
    public bool debug = true;

    // runtime
    bool _attached = true;
    bool _armed = false;
    bool _grabbed = false;
    bool _solverReady = false;
    int _solverIndex = -1;

    Vector3 _anchorAtGrab;     // pin world pos at grab start (we measure against this)
    Vector3 _hand;             // latest hand pos
    float _beyondTimer = 0f;
    float _lastCollisionSap = -999f;

    void Awake()
    {
        if (!rope) rope = GetComponentInParent<ObiRope>();
        if (!attachment) attachment = GetComponent<ObiParticleAttachment>();
        if (!rb) rb = GetComponent<Rigidbody>();
        if (!cam) cam = Camera.main;

        if (!rope || !attachment)
        {
            if (debug) Debug.LogError("[LeafHybrid] Missing rope or attachment.", this);
            enabled = false; return;
        }

        // while pinned, leaf should be kinematic / no contacts:
        if (rb)
        {
            rb.isKinematic = true;
            rb.detectCollisions = false;
            rb.interpolation = RigidbodyInterpolation.None;
        }
    }

    // add near other fields:
    bool _initDone = false;

    // --- replace your OnEnable / OnDisable with this:
    void OnEnable()
    {
        if (!rope) return;

        // If rope is already loaded when we enable, initialize now:
        TryInitFromSolver();

        // Fallback + hot-reload path: this event exists on all Obi 7 builds.
        rope.OnSimulationStart += Rope_OnSimulationStart;
    }

    void OnDisable()
    {
        if (!rope) return;
        rope.OnSimulationStart -= Rope_OnSimulationStart;
        _initDone = false;
        _solverReady = false;
        _solverIndex = -1;
        _armed = false;
        _beyondTimer = 0f;
    }

    // called every time simulation begins (safe moment to read solver data)
    void Rope_OnSimulationStart(ObiActor actor, float stepTime, float substepTime)
    {
        TryInitFromSolver();
    }

    // one-shot init, safe in all versions
    void TryInitFromSolver()
    {
        if (_initDone || rope == null || !rope.isLoaded || rope.solver == null) return;

        // auto actor-index if group has exactly one index:
        if (actorIndex < 0 && attachment.particleGroup != null &&
            attachment.particleGroup.particleIndices != null &&
            attachment.particleGroup.particleIndices.Count == 1)
        {
            actorIndex = attachment.particleGroup.particleIndices[0];
            if (debug) Debug.Log($"[LeafHybrid] Auto actorIndex = {actorIndex}", this);
        }

        _solverIndex = ActorToSolverIndex(rope, actorIndex);
        _solverReady = (_solverIndex >= 0);
        _armed = false;
        _beyondTimer = 0f;

        if (_solverReady) TryArmByProximity();

        _initDone = true;
        if (debug) Debug.Log("[LeafHybrid] Initialized from solver.", this);
    }


    // ===== Solver events (this is what actually runs) =====
    void Rope_OnAddedToSolver(ObiActor a, ObiSolver s)
    {
        // auto actor-index if group has exactly one index:
        if (actorIndex < 0 && attachment.particleGroup != null &&
            attachment.particleGroup.particleIndices != null &&
            attachment.particleGroup.particleIndices.Count == 1)
        {
            actorIndex = attachment.particleGroup.particleIndices[0];
            if (debug) Debug.Log($"[LeafHybrid] Auto actorIndex = {actorIndex}", this);
        }

        _solverIndex = ActorToSolverIndex(rope, actorIndex);
        _solverReady = (_solverIndex >= 0);
        _armed = false;
        _beyondTimer = 0f;

        TryArmByProximity(); // may arm immediately if we spawned close to the pin
    }

    void Rope_OnRemovedFromSolver(ObiActor a, ObiSolver s)
    {
        _solverReady = false;
        _solverIndex = -1;
        _armed = false;
        _beyondTimer = 0f;
    }

    void LateUpdate()
    {
        if (!_attached || !_solverReady || attachment == null || !attachment.enabled) return;

        // only allow break once armed (close at least once to the pin)
        if (!_armed) { TryArmByProximity(); return; }

        // if grabbed, measure hand vs anchor-at-grab; otherwise measure leaf vs live pin:
        float dist = _grabbed
            ? Vector3.Distance(_hand, _anchorAtGrab)
            : Vector3.Distance(transform.position, GetParticleWorld(rope, _solverIndex));

        if (debug) Debug.Log($"[LeafHybrid] dist={dist:F3} / {breakDistance}  armed={_armed} grabbed={_grabbed}", this);

        if (dist >= breakDistance)
        {
            _beyondTimer += Time.deltaTime;
            if (_beyondTimer >= breakDwellSeconds)
                DetachNow();
        }
        else
        {
            _beyondTimer = 0f;
        }
    }

    // ========== Public API for grabbers ==========

    /// Call on mouse-down (or touch begin). We'll capture the anchor at the pin.
    public void BeginGrab(Vector3 worldHitPoint)
    {
        if (!_solverReady) return;

        _grabbed = true;
        _hand = worldHitPoint;
        _anchorAtGrab = GetParticleWorld(rope, _solverIndex); // pin position at grab moment

        // ensure we're kinematic while attached so following the cursor tugs the pin:
        if (_attached && rb)
        {
            rb.isKinematic = true;
            rb.detectCollisions = false;
        }

        _armed = true;
        _beyondTimer = 0f;
        if (debug) Debug.Log("[LeafHybrid] BeginGrab -> armed.", this);
    }

    /// Call every frame while held with the world hand position you want the leaf to be at.
    public void SetHand(Vector3 worldHand)
    {
        _hand = worldHand;

        if (_attached)
        {
            if (rb && rb.isKinematic) rb.MovePosition(worldHand);
            else transform.position = worldHand;

            if (cam)
            {
                var face = Quaternion.LookRotation(-cam.transform.forward, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, face, 0.25f);
            }
        }
        else if (stayInHandAfterBreak)
        {
            // If we tore while still held, we keep it kinematic until release.
            if (rb && rb.isKinematic) rb.MovePosition(worldHand);
            else transform.position = worldHand;
        }
    }

    /// Call on mouse/touch release.
    public void EndGrab()
    {
        bool wasHeld = _grabbed;
        _grabbed = false;

        // If we’re already detached and were keeping it in hand, hand over to physics now.
        if (!_attached && rb)
        {
            if (stayInHandAfterBreak && wasHeld)
            {
                rb.isKinematic = false;
                rb.detectCollisions = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
            }
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    /// If you ever want to force a tear (e.g., for testing).
    public void ForceTear() { if (_attached) DetachNow(); }

    // ========== Detach & SAP ==========

    void DetachNow()
    {
        if (!_attached) return;
        _attached = false;

        // If the attachment lives on this leaf and pins only this particle → safe to disable.
        if (attachment != null && attachment.gameObject == gameObject)
        {
            attachment.enabled = false;
            if (debug) Debug.Log("[LeafHybrid] Disabled local attachment.", this);
        }
        else
        {
            // Shared attachment (on the rope): remove only our actorIndex from its group.
            RemoveActorFromAttachmentGroup(attachment, actorIndex);
            // Optional: disable the component if the group is empty
            if (attachment.particleGroup != null &&
                (attachment.particleGroup.particleIndices == null ||
                 attachment.particleGroup.particleIndices.Count == 0))
            {
                attachment.enabled = false;
            }
            rope.SetConstraintsDirty(Oni.ConstraintType.Pin);
            if (debug) Debug.Log($"[LeafHybrid] Removed actor {actorIndex} from shared attachment.", this);
        }

        // Physics handoff:
        if (rb)
        {
            // If we're still holding it and want it to stay in hand, keep it kinematic until EndGrab().
            if (!(stayInHandAfterBreak && _grabbed))
            {
                rb.isKinematic = false;
                rb.detectCollisions = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
            }

            rb.linearVelocity = Vector3.zero;          // <-- fixed
            rb.angularVelocity = Vector3.zero;   // <-- fixed
            rb.AddForce(breakImpulse, ForceMode.VelocityChange);

            // MeshCollider safety:
            var mc = GetComponent<MeshCollider>();
            if (mc && !mc.convex) mc.convex = true;
        }

        // sap on break:
        if (sapPool)
        {
            Vector3 normal = cam ? -cam.transform.forward : Vector3.up;
            sapPool.Play(transform.position, normal);
        }

        if (debug) Debug.Log("[LeafHybrid] DETACHED: physics enabled + sap burst.", this);
    }

    // ========== Collision sap after detach ==========
    void OnCollisionEnter(Collision c)
    {
        if (_attached || sapPool == null) return;
        float now = Time.time;
        if (now - _lastCollisionSap < collisionBurstCooldown) return;
        _lastCollisionSap = now;

        if (c.contactCount > 0)
        {
            var cp = c.GetContact(0);
            sapPool.Play(cp.point, cp.normal);
        }
        else
        {
            sapPool.Play(transform.position, Vector3.up);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (_attached || sapPool == null) return;
        float now = Time.time;
        if (now - _lastCollisionSap < collisionBurstCooldown) return;
        _lastCollisionSap = now;

        var pos = other.ClosestPoint(transform.position);
        var nrm = (transform.position - pos);
        sapPool.Play(pos, nrm.sqrMagnitude > 1e-6f ? nrm.normalized : Vector3.up);
    }

    // ========== helpers ==========
    void TryArmByProximity()
    {
        if (_armed || !_solverReady || attachment == null || !attachment.enabled) return;
        if (!GroupContains(attachment, actorIndex)) return;

        float d = Vector3.Distance(transform.position, GetParticleWorld(rope, _solverIndex));
        if (d <= armThreshold)
        {
            _armed = true;
            _beyondTimer = 0f;
            if (debug) Debug.Log($"[LeafHybrid] ARMED (d={d:F3} <= {armThreshold}).", this);
        }
    }

    static void RemoveActorFromAttachmentGroup(ObiParticleAttachment a, int actorIdx)
    {
        if (a == null || a.particleGroup == null || a.particleGroup.particleIndices == null) return;
        var list = a.particleGroup.particleIndices;
        for (int i = list.Count - 1; i >= 0; --i)
            if (list[i] == actorIdx) list.RemoveAt(i);
    }

    static bool GroupContains(ObiParticleAttachment a, int actorIdx)
    {
        return a != null && a.particleGroup != null &&
               a.particleGroup.particleIndices != null &&
               a.particleGroup.particleIndices.Contains(actorIdx);
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
