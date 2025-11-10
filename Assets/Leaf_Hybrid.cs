using UnityEngine;
using Obi;

[DefaultExecutionOrder(32000)]
[DisallowMultipleComponent]
public class LeafHybrid : MonoBehaviour
{
    [Header("Obi / Scene")]
    public ObiRope rope;
    [Tooltip("Actor-space particle index for this leaf. -1 = auto from attachment group (must contain exactly 1 index).")]
    public int actorIndex = -1;
    public ObiParticleAttachment attachment;   // preferably on this leaf
    public Rigidbody rb;
    public SapFxPool sapPool;
    public Camera cam;

    [Header("Break tuning")]
    [Tooltip("Meters of pull beyond the captured anchor required to allow a tear.")]
    [Min(0.001f)] public float breakDistance = 0.12f;

    [Tooltip("Continuous seconds you must remain beyond breakDistance before the pin is removed (\"how long the stem stays connected\").")]
    [Min(0f)] public float breakDwellSeconds = 0.12f;

    [Tooltip("Must come within this distance of the pin once before a break is allowed (prevents instant pop).")]
    public float armThreshold = 0.03f;

    public Vector3 breakImpulse = new(0f, 0.1f, 0f);

    [Header("Hold feel")]
    [Tooltip("Keep the leaf kinematic and in-hand after it tears until you release LMB.")]
    public bool stayInHandAfterBreak = true;

    [Tooltip("If the leaf is already detached when you release LMB, it will be destroyed after this many seconds.")]
    public float postReleaseLifetimeSeconds = 10f;

    [Header("Idle follow (attached & not grabbed)")]
    public bool syncToRopeWhenIdle = true;
    public float idleLerp = 30f;
    public Vector3 idleOffset = Vector3.zero;

    [Header("Collision sap")]
    public float collisionBurstCooldown = 0.15f;

    [Header("Debug")]
    public bool debug = false;

    // runtime
    bool _attached = true;
    bool _armed = false;
    bool _grabbed = false;
    bool _solverReady = false;
    bool _initDone = false;
    int _solverIndex = -1;

    Vector3 _anchorAtGrab;
    Vector3 _hand;
    float _beyondTimer = 0f;
    float _lastCollisionSap = -999f;
    float _destroyAt = -1f;

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

        if (rb)
        {
            // while pinned: no physics
            rb.isKinematic = true;
            rb.detectCollisions = false;
            rb.interpolation = RigidbodyInterpolation.None;
        }
    }

    void OnEnable()
    {
        if (!rope) return;
        TryInitFromSolver();
        rope.OnSimulationStart += OnSimulationStart;
    }

    void OnDisable()
    {
        if (rope) rope.OnSimulationStart -= OnSimulationStart;
        _initDone = false; _solverReady = false; _solverIndex = -1;
        _armed = false; _beyondTimer = 0f;
    }

    void Update()
    {
        // post-release lifetime (only counts after we've detached AND released)
        if (_destroyAt > 0f && Time.time >= _destroyAt)
            Destroy(gameObject);
    }

    void LateUpdate()
    {
        if (!_attached || !_solverReady || attachment == null || !attachment.enabled) return;

        // idle rope->leaf follow
        if (!_grabbed && syncToRopeWhenIdle)
        {
            Vector3 pw = GetParticleWorld(rope, _solverIndex) + idleOffset;
            float k = 1f - Mathf.Exp(-idleLerp * Time.deltaTime);
            if (rb && rb.isKinematic) rb.MovePosition(Vector3.Lerp(transform.position, pw, k));
            else transform.position = Vector3.Lerp(transform.position, pw, k);
        }

        // Only allow tearing while actively grabbed
        if (!_grabbed)
        {
            if (!_armed) TryArmByProximity();
            return;
        }
        if (!_armed) return;

        float dist = Vector3.Distance(_hand, _anchorAtGrab);
        if (debug) Debug.Log($"[LeafHybrid] dist={dist:F3}/{breakDistance} armed={_armed} grabbed={_grabbed}", this);

        if (dist >= breakDistance)
        {
            _beyondTimer += Time.deltaTime;
            if (_beyondTimer >= breakDwellSeconds) DetachNow();
        }
        else _beyondTimer = 0f;
    }

    // ---------- driven by the LMB grabber ----------
    public void BeginGrab(Vector3 worldHitPoint)
    {
        if (!_solverReady) return;
        _grabbed = true;
        _hand = worldHitPoint;
        _anchorAtGrab = GetParticleWorld(rope, _solverIndex);

        if (_attached && rb)
        {
            rb.isKinematic = true;
            rb.detectCollisions = false;
        }

        _armed = true;
        _beyondTimer = 0f;
        _destroyAt = -1f; // cancel any pending destroy while held
        if (debug) Debug.Log("[LeafHybrid] BeginGrab -> armed.", this);
    }

    public void SetHand(Vector3 worldHand)
    {
        _hand = worldHand;

        if (_attached || (stayInHandAfterBreak && _grabbed))
        {
            if (rb && rb.isKinematic) rb.MovePosition(worldHand);
            else transform.position = worldHand;

            if (cam)
            {
                var face = Quaternion.LookRotation(-cam.transform.forward, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, face, 0.25f);
            }
        }
    }

    public void EndGrab()
    {
        bool wasHeld = _grabbed;
        _grabbed = false;

        if (!_attached && rb)  // detached already
        {
            // hand over to physics now (we kept it in-hand while held)
            rb.isKinematic = false;
            rb.detectCollisions = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            // start lifetime countdown after release
            if (postReleaseLifetimeSeconds > 0f)
                _destroyAt = Time.time + postReleaseLifetimeSeconds;
        }
    }

    public void ForceTear() { if (_attached) DetachNow(); }

    // ---------- internals ----------
    void OnSimulationStart(ObiActor a, float step, float substep) => TryInitFromSolver();

    void TryInitFromSolver()
    {
        if (_initDone || rope == null || !rope.isLoaded || rope.solver == null) return;

        if (actorIndex < 0 &&
            attachment.particleGroup != null &&
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
    }

    void DetachNow()
    {
        if (!_attached) return;
        _attached = false;

        if (attachment != null && attachment.gameObject == gameObject)
        {
            attachment.enabled = false; // local, safe to disable
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
            // stay in hand (kinematic) until EndGrab(); enable physics there.
            if (!(stayInHandAfterBreak && _grabbed))
            {
                rb.isKinematic = false;
                rb.detectCollisions = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
            }

            //rb.linearVelocity = Vector3.zero;
            //rb.angularVelocity = Vector3.zero;
            rb.AddForce(breakImpulse, ForceMode.VelocityChange);

            var mc = GetComponent<MeshCollider>();
            if (mc && !mc.convex) mc.convex = true;
        }

        if (sapPool)
        {
            Vector3 normal = cam ? -cam.transform.forward : Vector3.up;
            sapPool.Play(transform.position, normal);
        }
    }

    void OnCollisionEnter(Collision c)
    {
        if (_attached || sapPool == null) return;
        float now = Time.time;
        if (now - _lastCollisionSap < collisionBurstCooldown) return;
        _lastCollisionSap = now;

        if (c.contactCount > 0) sapPool.Play(c.GetContact(0).point, c.GetContact(0).normal);
        else sapPool.Play(transform.position, Vector3.up);
    }

    void OnTriggerEnter(Collider other)
    {
        if (_attached || sapPool == null) return;
        float now = Time.time;
        if (now - _lastCollisionSap < collisionBurstCooldown) return;
        _lastCollisionSap = now;

        Vector3 pos = other.ClosestPoint(transform.position);
        Vector3 nrm = (transform.position - pos);
        sapPool.Play(pos, nrm.sqrMagnitude > 1e-6f ? nrm.normalized : Vector3.up);
    }

    void TryArmByProximity()
    {
        if (_armed || !_solverReady || attachment == null || !attachment.enabled) return;
        if (!GroupContains(attachment, actorIndex)) return;

        float d = Vector3.Distance(transform.position, GetParticleWorld(rope, _solverIndex));
        if (d <= armThreshold) { _armed = true; _beyondTimer = 0f; }
    }

    // helpers (Obi safe)
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
        return actorIdx < r.solverIndices.count ? r.solverIndices[actorIdx] : -1;
#else
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
        if (s.renderablePositions != null && solverIdx < s.renderablePositions.count)
            return s.transform.TransformPoint((Vector3)s.renderablePositions[solverIdx]);
        if (s.positions != null && solverIdx < s.positions.count)
            return s.transform.TransformPoint((Vector3)s.positions[solverIdx]);
#endif
        return Vector3.zero;
    }
}
