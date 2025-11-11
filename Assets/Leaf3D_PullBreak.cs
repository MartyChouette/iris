using Obi;
using System;
using System.Collections;
using UnityEngine;
using static UnityEngine.UI.Image;
using Object = UnityEngine.Object;

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

    public event Action<Leaf3D_PullBreak> OnDetached;   // raised when the leaf actually pops

    [Header("Sap emit (leaf-local)")]
    [Tooltip("If set, sap will emit from this transform instead of the leaf pivot.")]
    public Transform sapEmitOverride;
    [Tooltip("Optional ParticleSystem to play once on POP (place as child on the leaf).")]
    public ParticleSystem sapOnPopPS;
    [Tooltip("Optional ParticleSystem to play on collisions after POP.")]
    public ParticleSystem sapOnImpactPS;
    [Tooltip("Cooldown between impact bursts (seconds).")]
    public float sapImpactCooldown = 0.15f;

    // >>> ADDED: Obi Fluid Emitter support <<<
    [Tooltip("Optional on-leaf Obi Fluid Emitter (fires short burst on pop/impact).")]
    public ObiEmitter sapObiEmitter;
    [Tooltip("How long to keep the Obi emitter 'emitting' to create a burst (seconds).")]
    public float obiBurstSeconds = 0.06f;
    [Tooltip("Tiny extra delay before turning emitter fully off, lets particles clear the nozzle.")]
    public float obiStopExtraDelay = 0.02f;

    [Header("Auto-cull (optional)")]
    public bool enableAutoCull = false;
    public float autoCullDistance = 20f;
    public Transform autoCullOrigin; // if null, will use Camera.main.transform when available

    [Header("Detach cleanup")]
    [Tooltip("When the leaf is plucked, remove any ObiParticleAttachment components that still reference this particle.")]
    public bool deleteAttachmentsOnDetach = true;

    [Header("Break tuning")]
    [Min(0.001f)] public float breakDistance = 0.12f;
    [Min(0f)] public float breakDwellSeconds = 0.05f;
    [Min(0f)] public float minAttachSeconds = 0.10f;
    public Vector3 breakImpulse = new Vector3(0f, 0.8f, 0f);

    [Header("Hold feel")]
    public bool stayInHandAfterBreak = true;
    [Tooltip("Destroy the leaf this many seconds after you release it (only after tear).")]
    public float holdLifetimeAfterRelease = 10f;
    public bool autoDespawnAfterDrop = true;

    [Header("Collision sap")]
    public float collisionBurstCooldown = 0.15f;

    [Header("Arming (prevents instant break on spawn)")]
    [Tooltip("Must come within this distance of the pin once before breaking is allowed.")]
    public float armThreshold = 0.03f;

    [Header("Detach timing (visual tug)")]
    [Tooltip("If ON, keep the Obi attachment alive for a brief moment after break is triggered, so you can see the tug before it releases.")]
    public bool useReleaseDelay = true;
    [Min(0f)] public float releaseDelaySeconds = 0.08f;

    [Header("Distance-only helpers")]
    public float distForBreak = 0.8f;       // (kept, but no longer used for the check)
    public float breakDeadZone = 0.08f;
    public bool requireGrabToBreak = true;
    public bool armOnProximity = false;

    [Header("Debug")]
    public bool debug = true;

    // runtime
    Coroutine _releaseDelayCo;
    bool _breakQueued;

    bool _detached, _armed, _grabbed, _initDone, _solverReady;
    int _solverIndex = -1;
    float _beyondTimer, _attachedSince, _lastCollisionSap = -999f, _lastImpactSap = -999f;
    Vector3 _anchorAtGrab, _hand;

    // optional pinhole spawn point
    public Transform sapSpawn;

    public bool IsDetached => _detached;
    public Action onBreak;

    // >>> ADDED: keep handle to avoid stacking Obi bursts <<<
    Coroutine _obiBurstCo;

    public Vector3 GetPinWorld()
    {
        return (_solverReady && _solverIndex >= 0) ? GetParticleWorld(rope, _solverIndex) : transform.position;
    }

    void Awake()
    {
        if (!autoCullOrigin && cam) autoCullOrigin = cam.transform;

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
            rb.isKinematic = true;
            rb.detectCollisions = false;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.useGravity = false;
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
        if (_releaseDelayCo != null) { StopCoroutine(_releaseDelayCo); _releaseDelayCo = null; }
        if (_obiBurstCo != null) { StopCoroutine(_obiBurstCo); _obiBurstCo = null; }
        _breakQueued = false;

        if (rope != null)
            rope.OnSimulationStart -= Rope_OnSimulationStart;

        _initDone = _solverReady = false;
        _solverIndex = -1;
        _armed = false;
        _beyondTimer = 0f;
    }

    void Rope_OnSimulationStart(ObiActor actor, float step, float substep)
    {
        TryInitFromSolver();
    }

    void TryInitFromSolver()
    {
        if (_initDone || rope == null || !rope.isLoaded || rope.solver == null) return;

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

        if (_solverReady && armOnProximity) TryArmByProximity();
        _initDone = true;

        if (debug) Debug.Log($"[LeafBreak] Init: solverReady={_solverReady} solverIndex={_solverIndex}", this);
    }

    void LateUpdate()
    {
        if (_detached || !_solverReady || attachment == null || !attachment.enabled) return;

        if (Time.time - _attachedSince < minAttachSeconds) return;

        if (requireGrabToBreak && !_grabbed) return;

        if (!_armed)
        {
            if (armOnProximity) TryArmByProximity();
            if (!_armed) return;
        }

        float raw = _grabbed
            ? Vector3.Distance(_hand, _anchorAtGrab)
            : Vector3.Distance(transform.position, GetParticleWorld(rope, _solverIndex));

        // dead-zone
        float dist = Mathf.Max(0f, raw - breakDeadZone);

        if (debug) Debug.Log($"[LeafBreak] dist={dist:F3}/{breakDistance} armed={_armed} grabbed={_grabbed}", this);

        // compare measured 'dist'
        if (dist >= breakDistance)
        {
            _beyondTimer += Time.deltaTime;
            if (_beyondTimer >= breakDwellSeconds)
            {
                if (!_detached && !_breakQueued)
                {
                    if (useReleaseDelay && releaseDelaySeconds > 0f)
                    {
                        _breakQueued = true;
                        _releaseDelayCo = StartCoroutine(ReleaseAfterDelay());
                        if (debug) Debug.Log($"[LeafBreak] Break queued; releasing in {releaseDelaySeconds:0.###}s", this);
                    }
                    else
                    {
                        DoActualDetachNow();
                    }
                }
            }
        }
        else
        {
            _beyondTimer = 0f;
        }

        if (enableAutoCull && _detached && !_grabbed && autoCullOrigin)
        {
            if (Vector3.Distance(autoCullOrigin.position, transform.position) > autoCullDistance)
                Destroy(gameObject);
        }
    }

    public void BeginGrab(Vector3 worldHitPoint)
    {
        if (!_solverReady) return;

        _grabbed = true;
        _hand = worldHitPoint;
        _anchorAtGrab = GetParticleWorld(rope, _solverIndex);

        if (!_detached && rb)
        {
            rb.isKinematic = true;
            rb.detectCollisions = false;
            rb.useGravity = false;
        }

        _armed = true;
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
            rb.MovePosition(worldHand);
        }
    }

    public void EndGrab()
    {
        _grabbed = false;

        if (_detached && rb)
        {
            rb.isKinematic = false;
            rb.detectCollisions = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.useGravity = true;

            if (autoDespawnAfterDrop && holdLifetimeAfterRelease > 0f)
                Destroy(gameObject, holdLifetimeAfterRelease);
        }
    }

    IEnumerator ReleaseAfterDelay()
    {
        float t = 0f;
        while (t < releaseDelaySeconds)
        {
            if (_detached || attachment == null || !attachment.enabled) break;
            t += Time.deltaTime;
            yield return null;
        }
        DoActualDetachNow();
    }

    void DoActualDetachNow()
    {
        if (_detached) return;
        _detached = true;
        _breakQueued = false;
        _releaseDelayCo = null;

        transform.SetParent(null, true);

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
                rb.useGravity = false;
            }
            else
            {
                rb.isKinematic = false;
                rb.detectCollisions = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.useGravity = true;
            }

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.AddForce(breakImpulse, ForceMode.VelocityChange);

            var mc = GetComponent<MeshCollider>();
            if (mc && !mc.convex) mc.convex = true;
        }

        // --- POP EMIT (now goes through Obi/PS/pool in that order) ---
        {
            Vector3 pos = sapEmitOverride ? sapEmitOverride.position :
                           (sapSpawn ? sapSpawn.position : GetPinWorld());
            Vector3 n = (transform.position - pos);
            if (n.sqrMagnitude < 1e-6f) n = (cam ? -cam.transform.forward : Vector3.up);

            FireSap(pos, n.normalized);
        }

        onBreak?.Invoke();
        OnDetached?.Invoke(this);

        if (debug) Debug.Log("[LeafBreak] DETACHED (after delay)", this);
    }

    void DetachNow()
    {
        if (_detached || _breakQueued) return;

        if (useReleaseDelay && releaseDelaySeconds > 0f)
        {
            _breakQueued = true;
            _releaseDelayCo = StartCoroutine(ReleaseAfterDelay());
            if (debug) Debug.Log($"[LeafBreak] Break queued; releasing in {releaseDelaySeconds:0.###}s", this);
        }
        else
        {
            DoActualDetachNow();
        }
    }

    // ===== Collision sap after detach =====
    void OnCollisionEnter(Collision c)
    {
        if (!_detached) return;

        float now = Time.time;
        if (now - _lastImpactSap < Mathf.Max(sapImpactCooldown, collisionBurstCooldown)) return;
        _lastImpactSap = now;

        Vector3 pos, n;
        if (c.contactCount > 0) { var ct = c.GetContact(0); pos = ct.point; n = ct.normal; }
        else { pos = transform.position; n = Vector3.up; }

        FireSap(pos, n);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!_detached) return;

        float now = Time.time;
        if (now - _lastImpactSap < Mathf.Max(sapImpactCooldown, collisionBurstCooldown)) return;
        _lastImpactSap = now;

        Vector3 pos = other.ClosestPoint(transform.position);
        Vector3 n = (transform.position - pos); if (n.sqrMagnitude < 1e-6f) n = Vector3.up;

        FireSap(pos, n.normalized);
    }

    // ===== Sap helpers (ADDED) =====
    Transform GetSapOrigin()
    {
        if (sapEmitOverride) return sapEmitOverride;
        if (sapSpawn) return sapSpawn;
        return transform;
    }

    void FireSap(Vector3 worldPos, Vector3 worldNormal)
    {
        var origin = GetSapOrigin();
        if (origin)
        {
            origin.position = worldPos;
            origin.rotation = Quaternion.LookRotation(
                worldNormal.sqrMagnitude > 1e-6f ? worldNormal.normalized :
                    (cam ? -cam.transform.forward : Vector3.up),
                Vector3.up
            );
        }

        // Obi burst (no assignments here—just call it)
        TryPlayObiBurst(origin.position, origin.forward);

        // ParticleSystems / pool fallbacks...
        if (_detached)
        {
            if (sapOnImpactPS) { sapOnImpactPS.transform.SetPositionAndRotation(origin.position, origin.rotation); sapOnImpactPS.Play(true); }
            else if (sapOnPopPS) { sapOnPopPS.transform.SetPositionAndRotation(origin.position, origin.rotation); sapOnPopPS.Play(true); }
        }
        else
        {
            if (sapOnPopPS) { sapOnPopPS.transform.SetPositionAndRotation(origin.position, origin.rotation); sapOnPopPS.Play(true); }
        }

        if (sapPool) sapPool.Play(origin.position, origin.forward);
    }


    bool TryPlayObiBurst(Vector3 pos, Vector3 dir)
    {
        if (!sapObiEmitter || !sapObiEmitter.isActiveAndEnabled) return false;

        // Move/aim emitter at the spawn point:
        Transform et = sapObiEmitter.transform;
        et.SetPositionAndRotation(pos, Quaternion.LookRotation(dir, Vector3.up));

        // Prefer true "Burst" API, but fall back to brief Start/Stop if needed.
        var t = sapObiEmitter.GetType();
        var mBurst = t.GetMethod("Burst");       // Obi 7.x
        var mEmitBurst = t.GetMethod("EmitBurst");   // some variants
        var mStart = t.GetMethod("StartEmitting");
        var mStop = t.GetMethod("StopEmitting");

        // Temporarily force burst emission method so a one-shot happens:
        var prevMethod = sapObiEmitter.emissionMethod;
        //sapObiEmitter.emissionMethod = TryPlayObiBurst(origin.position, origin.forward);



        if (mBurst != null || mEmitBurst != null)
        {
            (mBurst ?? mEmitBurst).Invoke(sapObiEmitter, null);
            if (gameObject.activeInHierarchy) StartCoroutine(CoRestoreEmitterMethod(prevMethod));
            return true;
        }

        // Fallback: stream briefly, then stop.
        if (mStart != null && mStop != null)
        {
            if (gameObject.activeInHierarchy)
                StartCoroutine(CoStartStopEmit(mStart, mStop, prevMethod));
            else
                sapObiEmitter.emissionMethod = prevMethod;
            return true;
        }

        // Last resort: toggle the component for a frame or two.
        bool prevEnabled = sapObiEmitter.enabled;
        sapObiEmitter.enabled = false;
        sapObiEmitter.enabled = true;
        if (gameObject.activeInHierarchy) StartCoroutine(CoEndable(prevEnabled, prevMethod));
        else { sapObiEmitter.enabled = prevEnabled; sapObiEmitter.emissionMethod = prevMethod; }

        return true;
    }

    IEnumerator CoRestoreEmitterMethod(ObiEmitter.EmissionMethod prev)
    {
        yield return null; // next frame
        sapObiEmitter.emissionMethod = prev;
    }

    IEnumerator CoStartStopEmit(System.Reflection.MethodInfo start,
                                System.Reflection.MethodInfo stop,
                                ObiEmitter.EmissionMethod prev)
    {
        // start streaming for a tiny window (your old obiBurstSeconds)
        start.Invoke(sapObiEmitter, null);

        float t = 0f;
        while (t < Mathf.Max(0f, obiBurstSeconds))
        {
            t += Time.deltaTime;
            yield return null;
        }

        stop.Invoke(sapObiEmitter, null);

        if (obiStopExtraDelay > 0f)
            yield return new WaitForSeconds(obiStopExtraDelay);

        sapObiEmitter.emissionMethod = prev;
    }

    IEnumerator CoEndable(bool prevEnabled, ObiEmitter.EmissionMethod prev)
    {
        yield return null;
        yield return null;
        sapObiEmitter.enabled = prevEnabled;
        sapObiEmitter.emissionMethod = prev;
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

    public void ConfigureFromGrabber(
        float giveDeadZone, float popDistance, float dwellSeconds,
        bool useDelay, float delaySeconds,
        bool stayInHand, float despawnAfterDropSeconds)
    {
        breakDeadZone = Mathf.Max(0f, giveDeadZone);
        breakDistance = Mathf.Max(0.001f, popDistance);
        breakDwellSeconds = Mathf.Max(0f, dwellSeconds);

        useReleaseDelay = useDelay;
        releaseDelaySeconds = Mathf.Max(0f, delaySeconds);

        stayInHandAfterBreak = stayInHand;
        holdLifetimeAfterRelease = Mathf.Max(0f, despawnAfterDropSeconds);

        requireGrabToBreak = true;
        armOnProximity = false;
    }
}
