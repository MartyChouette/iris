// File: Leaf3D_PullBreak.cs
// Unity 6 + Obi 7 (safe init + stay-in-hand + timed lifetime after release)

using UnityEngine;
using Obi;

[DefaultExecutionOrder(32000)]
[DisallowMultipleComponent]
public class Leaf3D_PullBreak : MonoBehaviour
{
    [Header("Obi refs")]
    public ObiRope rope;
    [Tooltip("Actor-space particle index for this leaf. -1 = auto (first index in attachment group).")]
    public int actorIndex = -1;
    [Tooltip("Pin that contains this leaf's actorIndex. Can live on the leaf or on the rope.")]
    public ObiParticleAttachment attachment;
    public Rigidbody rb;
    public SapFxPool sapPool;
    public Camera cam;

    [Header("Break tuning")]
    [Min(0.001f)] public float breakDistance = 0.12f;     // meters
    [Min(0f)] public float breakDwellSeconds = 0.05f;     // time stretched beyond distance
    [Min(0f)] public float minAttachSeconds = 0.10f;      // must remain attached at least this long
    public Vector3 breakImpulse = new Vector3(0f, 0.8f, 0f);

    [Header("Hold feel")]
    public bool stayInHandAfterBreak = true;
    [Tooltip("Destroy the leaf this many seconds after you release it (only after tear).")]
    public float holdLifetimeAfterRelease = 10f;

    [Header("Collision sap")]
    public float collisionBurstCooldown = 0.15f;

    [Header("Arming (prevents instant break on spawn)")]
    [Tooltip("Must come within this distance of the pin once before breaking is allowed.")]
    public float armThreshold = 0.03f;          // < breakDistance

    [Header("Debug")]
    public bool debug = true;

    // runtime
    bool _detached, _armed, _grabbed, _initDone, _solverReady;
    int _solverIndex = -1;
    float _beyondTimer, _attachedSince, _lastCollisionSap = -999f;
    Vector3 _anchorAtGrab, _hand;

    void Awake()
    {
        if (!rope) rope = GetComponentInParent<ObiRope>();
        if (!attachment) attachment = GetComponent<ObiParticleAttachment>();
        if (!rb) rb = GetComponent<Rigidbody>();
        if (!cam) cam = Camera.main;

        if (!rope || !attachment)
        {
            if (debug) Debug.LogError("[LeafBreak] Missing rope or attachment.", this);
            enabled = false; return;
        }

        if (rb)
        {
            // while attached: kinematic, no contacts
            rb.isKinematic = true;
            rb.detectCollisions = false;
            rb.interpolation = RigidbodyInterpolation.None;
        }

        _attachedSince = Time.time;
    }

    void OnEnable()
    {
        if (rope != null)
            rope.OnSimulationStart += Rope_OnSimulationStart;
    }

    void OnDisable()
    {
        if (rope != null)
            rope.OnSimulationStart -= Rope_OnSimulationStart;

        _initDone = _solverReady = false;
        _solverIndex = -1;
        _armed = false;
        _beyondTimer = 0f;
    }

    // Called each time the solver starts stepping (safe time to touch solver lists)
    void Rope_OnSimulationStart(ObiActor actor, float step, float substep)
    {
        TryInitFromSolver();
    }

    void TryInitFromSolver()
    {
        if (_initDone || rope == null || !rope.isLoaded || rope.solver == null) return;

        // auto actor index from the group if needed
        if (actorIndex < 0 && attachment.particleGroup != null &&
            attachment.particleGroup.particleIndices != null &&
            attachment.particleGroup.particleIndices.Count > 0)
        {
            actorIndex = attachment.particleGroup.particleIndices[0];
            if (debug) Debug.Log($"[LeafBreak] Auto actorIndex = {actorIndex}", this);
        }

        _solverIndex = ActorToSolverIndex(rope, actorIndex);
        _solverReady = (_solverIndex >= 0);
        _armed = false;
        _beyondTimer = 0f;

        if (_solverReady) TryArmByProximity();
        _initDone = true;

        if (debug) Debug.Log($"[LeafBreak] Init: solverReady={_solverReady} solverIndex={_solverIndex}", this);
    }

    void LateUpdate()
    {
        if (_detached || !_solverReady || attachment == null || !attachment.enabled) return;

        // must live at least minAttachSeconds
        if (Time.time - _attachedSince < minAttachSeconds) return;

        if (!_armed) { TryArmByProximity(); return; }

        // measure hand vs anchor if grabbed; otherwise leaf vs current pin
        float dist = _grabbed
            ? Vector3.Distance(_hand, _anchorAtGrab)
            : Vector3.Distance(transform.position, GetParticleWorld(rope, _solverIndex));

        if (debug) Debug.Log($"[LeafBreak] dist={dist:F3}/{breakDistance} armed={_armed} grabbed={_grabbed}", this);

        if (dist >= breakDistance)
        {
            _beyondTimer += Time.deltaTime;
            if (_beyondTimer >= breakDwellSeconds) DetachNow();
        }
        else _beyondTimer = 0f;
    }

    // ===== Optional public hooks for your LMB grabber =====
    public void BeginGrab(Vector3 worldHitPoint)
    {
        if (!_solverReady) return;

        _grabbed = true;
        _hand = worldHitPoint;
        _anchorAtGrab = GetParticleWorld(rope, _solverIndex);

        // while attached, keep kinematic so dragging tugs the pin:
        if (!_detached && rb)
        {
            rb.isKinematic = true;
            rb.detectCollisions = false;
        }

        _armed = true;          // allow breaking after dwell
        _beyondTimer = 0f;
    }

    public void SetHand(Vector3 worldHand)
    {
        _hand = worldHand;

        if (!_detached)
        {
            if (rb && rb.isKinematic) rb.MovePosition(worldHand);
            else transform.position = worldHand;

            if (cam)
            {
                var face = Quaternion.LookRotation(-cam.transform.forward, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, face, 0.25f);
            }
        }
        else if (stayInHandAfterBreak && rb && rb.isKinematic)
        {
            rb.MovePosition(worldHand); // keep posing while still held
        }
    }

    public void EndGrab()
    {
        _grabbed = false;

        // If already torn and we kept it kinematic in-hand, hand control to physics now.
        if (_detached && rb)
        {
            rb.isKinematic = false;
            rb.detectCollisions = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            if (holdLifetimeAfterRelease > 0f)
                Destroy(gameObject, holdLifetimeAfterRelease);
        }
    }

    // ===== Detach =====
    void DetachNow()
    {
        if (_detached) return;
        _detached = true;

        // Disable local attachment OR remove just our particle from shared group
        if (attachment.gameObject == gameObject)
        {
            attachment.enabled = false;
        }
        else
        {
            RemoveActorFromAttachmentGroup(attachment, actorIndex);
            if (attachment.particleGroup != null &&
                (attachment.particleGroup.particleIndices == null ||
                 attachment.particleGroup.particleIndices.Count == 0))
                attachment.enabled = false;

            rope.SetConstraintsDirty(Oni.ConstraintType.Pin);
        }

        if (rb)
        {
            if (stayInHandAfterBreak && _grabbed)
            {
                // keep in hand (still kinematic) until EndGrab()
            }
            else
            {
                rb.isKinematic = false;
                rb.detectCollisions = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
            }

            rb.linearVelocity = Vector3.zero;          // safe now (dynamic or will be when released)
            rb.angularVelocity = Vector3.zero;
            rb.AddForce(breakImpulse, ForceMode.VelocityChange);

            var mc = GetComponent<MeshCollider>();
            if (mc && !mc.convex) mc.convex = true;
        }

        if (sapPool)
        {
            Vector3 n = cam ? -cam.transform.forward : Vector3.up;
            sapPool.Play(transform.position, n);
        }

        if (debug) Debug.Log("[LeafBreak] DETACHED", this);
    }

    // ===== Collision sap after detach =====
    void OnCollisionEnter(Collision c)
    {
        if (!_detached || sapPool == null) return;
        float now = Time.time; if (now - _lastCollisionSap < collisionBurstCooldown) return;
        _lastCollisionSap = now;

        if (c.contactCount > 0) sapPool.Play(c.GetContact(0).point, c.GetContact(0).normal);
        else sapPool.Play(transform.position, Vector3.up);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!_detached || sapPool == null) return;
        float now = Time.time; if (now - _lastCollisionSap < collisionBurstCooldown) return;
        _lastCollisionSap = now;

        Vector3 pos = other.ClosestPoint(transform.position);
        Vector3 n = (transform.position - pos);
        sapPool.Play(pos, n.sqrMagnitude > 1e-6f ? n.normalized : Vector3.up);
    }

    // ===== Helpers =====
    void TryArmByProximity()
    {
        if (_armed || !_solverReady || attachment == null || !attachment.enabled) return;
        if (!GroupContains(attachment, actorIndex)) return;

        float d = Vector3.Distance(transform.position, GetParticleWorld(rope, _solverIndex));
        if (d <= armThreshold)
        {
            _armed = true;
            _beyondTimer = 0f;
            if (debug) Debug.Log($"[LeafBreak] ARMED (d={d:F3} <= {armThreshold}).", this);
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
        return (actorIdx < r.solverIndices.count) ? r.solverIndices[actorIdx] : -1;
#else
        // In non-native builds, Obi uses NativeList-like wrappers with `.count`
        return (r.solverIndices != null && actorIdx < r.solverIndices.count) ? r.solverIndices[actorIdx] : -1;
#endif
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
