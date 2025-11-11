// File: Leaf3D_PullBreak.cs
// Unity 6 / Obi 7+
// Pluckable leaf that tethers to a rope particle (via ObiParticleAttachment), breaks by distance,
// emits sap on pop/impact (Obi or ParticleSystem or custom pool), and optionally stays-in-hand.
//
// Key fix: Obi fluid now spawns from your override transform (sapEmitOverride/sapSpawn)
// by aligning BOTH the ObiEmitter and its ObiEmitterShape ("nozzle") before bursting.

using System;
using System.Collections;
using UnityEngine;
using Obi;

[DefaultExecutionOrder(32000)]
[DisallowMultipleComponent]
public class Leaf3D_PullBreak : MonoBehaviour
{
    // ────────────────────────── Scene / Obi refs ──────────────────────────
    [Header("Obi refs")]
    public ObiRope rope;
    [Tooltip("Actor-space particle index for this leaf. -1 = auto from attachment group.")]
    public int actorIndex = -1;

    [Tooltip("Attachment that contains this leaf's actorIndex. Can live on the leaf or on the rope.")]
    public ObiParticleAttachment attachment;

    public Rigidbody rb;
    public Camera cam;

    // ────────────────────────── Sap systems ──────────────────────────
    public event Action<Leaf3D_PullBreak> OnDetached;   // raised when the leaf actually pops

    [Header("Sap emit (leaf-local)")]
    [Tooltip("If set, sap will emit from this transform instead of the leaf pivot.")]
    public Transform sapEmitOverride;

    [Tooltip("Optional fallback spawn if override is null.")]
    public Transform sapSpawn;

    [Tooltip("Optional ParticleSystem to play once on POP (place as child on the leaf).")]
    public ParticleSystem sapOnPopPS;

    [Tooltip("Optional ParticleSystem to play on collisions after POP.")]
    public ParticleSystem sapOnImpactPS;

    [Tooltip("Cooldown between impact bursts (seconds).")]
    public float sapImpactCooldown = 0.15f;

    [Header("Obi Fluid (optional)")]
    [Tooltip("Optional Obi Fluid Emitter (one-shot burst on pop/impact).")]
    public ObiEmitter sapObiEmitter;

    [Tooltip("How long to stream when Burst API not available (seconds).")]
    public float obiBurstSeconds = 0.06f;

    [Tooltip("Tiny extra delay before turning emitter fully off, lets particles clear the nozzle.")]
    public float obiStopExtraDelay = 0.02f;

    // Cached nozzle/shape (the actual spawn reference used by Obi)
    private ObiEmitterShape _obiShape;

    [Header("Pool (optional)")]
    public SapFxPool sapPool; // your custom pooled FX (Play(pos, dir))

    // ────────────────────────── Lifecycle / culling ──────────────────────────
    [Header("Auto-cull (optional)")]
    public bool enableAutoCull = false;
    public float autoCullDistance = 20f;
    public Transform autoCullOrigin; // if null, will use Camera.main.transform when available

    [Header("Detach cleanup")]
    [Tooltip("When plucked, remove the ObiParticleAttachment that references this particle.")]
    public bool deleteAttachmentsOnDetach = true;

    // ────────────────────────── Break tuning ──────────────────────────
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
    public float breakDeadZone = 0.08f;
    public bool requireGrabToBreak = true;
    public bool armOnProximity = false;

    [Header("Debug")]
    public bool debug = true;

    // ────────────────────────── Runtime state ──────────────────────────
    Coroutine _releaseDelayCo;
    bool _breakQueued;

    bool _detached, _armed, _grabbed, _initDone, _solverReady;
    int _solverIndex = -1;
    float _beyondTimer, _attachedSince, _lastImpactSap = -999f;
    Vector3 _anchorAtGrab, _hand;

    public bool IsDetached => _detached;
    public Action onBreak;

    void Awake()
    {
        if (!autoCullOrigin && cam) autoCullOrigin = cam.transform;
        if (!rope) rope = GetComponentInParent<ObiRope>();
        if (!attachment) attachment = GetComponent<ObiParticleAttachment>();
        if (!rb) rb = GetComponent<Rigidbody>();
        if (!cam) cam = Camera.main;

        if (sapObiEmitter)
            _obiShape = sapObiEmitter.GetComponentInChildren<ObiEmitterShape>(true);

        if (!rope || !attachment)
        {
            if (debug) Debug.LogError("[LeafBreak] Missing rope or attachment.", this);
            enabled = false;
            return;
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

        // Auto actorIndex from the attachment group if requested:
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

        // grace period
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

    // ────────────────────────── Grab API ──────────────────────────
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

        // Remove/disable attachment cleanly:
        if (attachment)
        {
            if (attachment.gameObject == gameObject)
            {
                // Leaf owns the attachment
                if (deleteAttachmentsOnDetach) attachment.enabled = false;
            }
            else
            {
                // Attachment lives elsewhere (rope). Remove this leaf's index from its group.
                if (deleteAttachmentsOnDetach)
                {
                    RemoveActorFromAttachmentGroup(attachment, actorIndex);
                    if (attachment.particleGroup != null &&
                        (attachment.particleGroup.particleIndices == null ||
                         attachment.particleGroup.particleIndices.Count == 0))
                        attachment.enabled = false;
                    rope.SetConstraintsDirty(Oni.ConstraintType.Pin);
                }
            }
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

            // Non-convex MeshCollider can't be simulated as dynamic:
            var mc = GetComponent<MeshCollider>();
            if (mc && !mc.convex) mc.convex = true;
        }

        // POP FX
        Vector3 pos; Quaternion rot;
        GetSapPose(out pos, out rot);
        FireSap(pos, rot);

        onBreak?.Invoke();
        OnDetached?.Invoke(this);

        if (debug) Debug.Log("[LeafBreak] DETACHED", this);
    }

    public void DetachNow()
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

    // ────────────────────────── Collision → impact sap ──────────────────────────
    void OnCollisionEnter(Collision c)
    {
        if (!_detached) return;

        float now = Time.time;
        if (now - _lastImpactSap < Mathf.Max(sapImpactCooldown, collisionBurstCooldown)) return;
        _lastImpactSap = now;

        Vector3 pos; Quaternion rot;
        if (c.contactCount > 0)
        {
            var ct = c.GetContact(0);
            pos = ct.point;
            rot = Quaternion.LookRotation(ct.normal, Vector3.up);
        }
        else
        {
            pos = transform.position;
            rot = Quaternion.LookRotation(Vector3.up, Vector3.forward);
        }
        FireSap(pos, rot);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!_detached) return;

        float now = Time.time;
        if (now - _lastImpactSap < Mathf.Max(sapImpactCooldown, collisionBurstCooldown)) return;
        _lastImpactSap = now;

        Vector3 p = other.ClosestPoint(transform.position);
        Vector3 n = (transform.position - p);
        if (n.sqrMagnitude < 1e-6f) n = Vector3.up;

        FireSap(p, Quaternion.LookRotation(n.normalized, Vector3.up));
    }

    // ────────────────────────── Sap helpers ──────────────────────────
    void GetSapPose(out Vector3 pos, out Quaternion rot)
    {
        // Priority: sapEmitOverride → sapSpawn → rope pin position
        if (sapEmitOverride)
        {
            pos = sapEmitOverride.position;
            rot = sapEmitOverride.rotation;
            return;
        }

        if (sapSpawn)
        {
            pos = sapSpawn.position;
            rot = sapSpawn.rotation;
            return;
        }

        // Fallback: rope particle position facing camera (or up)
        pos = (_solverReady && _solverIndex >= 0) ? GetParticleWorld(rope, _solverIndex) : transform.position;
        Vector3 fwd = cam ? -cam.transform.forward : Vector3.up;
        rot = Quaternion.LookRotation(fwd, Vector3.up);
    }

    void FireSap(Vector3 worldPos, Quaternion worldRot)
    {
        // 1) Obi Emitter burst (align BOTH emitter and its Shape/nozzle):
        TryPlayObiBurst(worldPos, worldRot);

        // 2) ParticleSystems:
        if (_detached)
        {
            if (sapOnImpactPS)
            {
                sapOnImpactPS.transform.SetPositionAndRotation(worldPos, worldRot);
                sapOnImpactPS.Play(true);
            }
            else if (sapOnPopPS)
            {
                sapOnPopPS.transform.SetPositionAndRotation(worldPos, worldRot);
                sapOnPopPS.Play(true);
            }
        }
        else
        {
            if (sapOnPopPS)
            {
                sapOnPopPS.transform.SetPositionAndRotation(worldPos, worldRot);
                sapOnPopPS.Play(true);
            }
        }

        // 3) Custom pool (dir = forward of rot):
        if (sapPool) sapPool.Play(worldPos, worldRot * Vector3.forward);
    }

    bool TryPlayObiBurst(Vector3 pos, Quaternion rot)
    {
        if (!sapObiEmitter || !sapObiEmitter.isActiveAndEnabled) return false;

        // Move the emitter to desired world pose:
        Transform et = sapObiEmitter.transform;
        et.SetPositionAndRotation(pos, rot);

        // Move the SHAPE (nozzle) to the exact same pose, as Obi samples spawn positions from it:
        if (_obiShape && _obiShape.transform)
            _obiShape.transform.SetPositionAndRotation(pos, rot);

        // Prefer a one-shot "Burst" if present. Otherwise briefly stream:
        var t = sapObiEmitter.GetType();
        var mBurst = t.GetMethod("Burst");        // Obi 7.x
        var mEmitBurst = t.GetMethod("EmitBurst");    // name variant
        var mStart = t.GetMethod("StartEmitting");
        var mStop = t.GetMethod("StopEmitting");

        var prevMethod = sapObiEmitter.emissionMethod;

        if (mBurst != null || mEmitBurst != null)
        {
            (mBurst ?? mEmitBurst).Invoke(sapObiEmitter, null);
            if (gameObject.activeInHierarchy) StartCoroutine(CoRestoreEmitterMethod(prevMethod));
            return true;
        }

        if (mStart != null && mStop != null)
        {
            if (gameObject.activeInHierarchy)
                StartCoroutine(CoStartStopEmit(mStart, mStop, prevMethod));
            else
                sapObiEmitter.emissionMethod = prevMethod;
            return true;
        }

        // Last resort: toggle the component very briefly.
        bool prevEnabled = sapObiEmitter.enabled;
        sapObiEmitter.enabled = false;
        sapObiEmitter.enabled = true;
        if (gameObject.activeInHierarchy) StartCoroutine(CoReenable(prevEnabled, prevMethod));
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

    IEnumerator CoReenable(bool prevEnabled, ObiEmitter.EmissionMethod prev)
    {
        yield return null;
        yield return null;
        sapObiEmitter.enabled = prevEnabled;
        sapObiEmitter.emissionMethod = prev;
    }

    // ────────────────────────── Helpers ──────────────────────────
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

    // Quick configurator for external grabber UI
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
