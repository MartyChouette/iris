using UnityEngine;
using System;

/// Attach this to each leaf prefab.
/// RMB = grab/preview. Release = drop only if already detached.
/// Detaches only when a CUT is reported near its pinhole (or via optional yank-break).
[DisallowMultipleComponent]
public class LeafPullOff : MonoBehaviour
{
    // ── Scene refs ──
    [Header("Scene Refs")]
    public Camera cam;
    public PinholeLibrary pinholes;     // fallback when explicitPinhole not set by spawner
    public Transform explicitPinhole;   // socket created by spawner at rope particle
    public Rigidbody stemRigidbody;     // small RB on stem for a nudge
    public SapFxPool sapPool;           // pooled spray (ParticleSystem / ObiEmitter)

    // ── Click picking ──
    [Header("Click picking")]
    public LayerMask leafPickMask = 6;
    public float pickRadius = 0.02f;
    public bool includeTriggers = true;
    public bool blockWhenPointerOverUI = false;

    // ── Grab/hand proxy ──
    [Header("Grab (hand)")]
    public float grabMaxDistance = 12f;
    public float handFollowSpeed = 24f;
    public float handDepthBias = 0f;

    // ── Springs ──
    [Header("Attachment spring (leaf ↔ pinhole)")]
    public float attachSpring = 160f;
    public float attachDamper = 20f;
    public float socketSlack = 0.02f;

    [Header("Hold spring (leaf ↔ hand)")]
    public float holdSpring = 260f;
    public float holdDamper = 26f;
    public float handSlackAttached = 0.03f;

    // ── Detach rules ──
    [Header("Detachment")]
    [Tooltip("Detach the leaf when a cut occurs within this radius of the pinhole (world units).")]
    public float cutDetachRadius = 0.035f;

    [Tooltip("If true, yanking can also detach (not your current flow).")]
    public bool allowYankBreak = false;
    public float yankBreakDistance = 0.10f;
    public float yankBreakTension = 45f;

  

    // ── FX / jiggle ──
    [Header("On Break (sap + jiggle)")]
    public float stemBreakNudge = 0.7f;          // impulse magnitude into stem at socket

    // ── After drop ──
    [Header("After Drop")]
    public bool destroyAfterDrop = true;         // false if pooling
    public float destroyAfterSeconds = 10f;

    // ── Limits ──
    [Header("Limits")]
    public float maxLinearVelocity = 6f;
    public float maxAngularVelocity = 20f;

    // ── Tug feel ──
    [Header("Tug Feel (while attached & held)")]
    public float stemTugScale = 0.5f;
    public float stemTugMaxImpulse = 0.8f;
    public ForceMode stemTugMode = ForceMode.Force;

    [Header("Debug")]
    public bool drawDebug = false;
    public bool logBreaks = false;

    // runtime
    public event Action OnBreak;

    enum State { AttachedIdle, AttachedHeld, DetachedHeld, DetachedFree }
    State _state = State.AttachedIdle;

    Rigidbody _leafRB;
    Transform _pinhole;
    ConfigurableJoint _jointToPinhole;
    ConfigurableJoint _jointToHand;
    Rigidbody _handProxy;

    float _tensionSmoothed;
    float _grabbedDepthFromCam;
    bool _broken; // ← guard: fire sap / break-once only


    void Awake()
    {
        _leafRB = GetComponent<Rigidbody>() ?? gameObject.AddComponent<Rigidbody>();
        _leafRB.mass = Mathf.Max(0.01f, _leafRB.mass);
        _leafRB.useGravity = false;
        _leafRB.interpolation = RigidbodyInterpolation.Interpolate;
        _leafRB.maxAngularVelocity = maxAngularVelocity;

        // Ensure there’s a collider and it’s hittable.
        var col = GetComponent<Collider>();
        if (!col) col = gameObject.AddComponent<SphereCollider>();  // simple, robust
        col.enabled = true;

        // If someone put it on Ignore Raycast, put it back on Default.
        if (gameObject.layer == 2) gameObject.layer = 0;

        if (!cam) cam = Camera.main;

        var go = new GameObject($"{name}_GrabProxy");
        go.hideFlags = HideFlags.HideInHierarchy;
        _handProxy = go.AddComponent<Rigidbody>();
        _handProxy.isKinematic = true;
        _handProxy.interpolation = RigidbodyInterpolation.Interpolate;
    }


    void Start()
    {
        _pinhole = explicitPinhole ? explicitPinhole
                                   : (pinholes ? pinholes.GetNearest(transform.position) : null);
        if (_pinhole) _jointToPinhole = CreateSocketJoint(_leafRB, _pinhole, socketSlack);
        _state = State.AttachedIdle;
        _broken = false;
    }

    void Update()
    {
        // RMB grab/preview
        if (Input.GetMouseButtonDown(1)) TryGrab();
        if (Input.GetMouseButtonUp(1)) TryRelease();
    }

    void FixedUpdate()
    {
        // clamp excessive linear speed
        if (_leafRB && _leafRB.linearVelocity.sqrMagnitude > maxLinearVelocity * maxLinearVelocity)
            _leafRB.linearVelocity = _leafRB.linearVelocity.normalized * maxLinearVelocity;

        switch (_state)
        {
            case State.AttachedHeld:
                UpdateHandProxyLockedDepth();
                TugStemWhileHeld();
                if (allowYankBreak && _pinhole) MaybeYankBreak();
                break;

            case State.DetachedHeld:
                UpdateHandProxyLockedDepth();
                break;
        }

        if (drawDebug && _pinhole)
            Debug.DrawLine(transform.position, _pinhole.position,
                           _state == State.AttachedHeld ? Color.cyan : Color.green, 0f, false);
    }

    void OnDestroy()
    {
        if (_handProxy) Destroy(_handProxy.gameObject);
    }

    // ─────────────────────────────────────────────────────────────────────
    // PUBLIC API — call this from your rope cut script:
    // ─────────────────────────────────────────────────────────────────────
    /// <summary>Notify this leaf that a stem cut happened at worldCutPoint.</summary>
    public void OnStemCutAt(Vector3 worldCutPoint)
    {
        if (_broken || _pinhole == null) return;
        if (Vector3.Distance(worldCutPoint, _pinhole.position) <= cutDetachRadius)
            DoBreak();
    }

    /// <summary>Force an immediate break (rarely needed; prefer OnStemCutAt).</summary>
    public void BreakNow()
    {
        if (_broken) return;
        DoBreak();
    }
    // ─────────────────────────────────────────────────────────────────────

    // ── input ──
    void TryGrab()
    {
        if (!cam) { if (drawDebug) Debug.LogWarning("LeafPullOff: no Camera assigned."); return; }

#if UNITY_ENGINE_UI || ENABLE_INPUT_SYSTEM
        if (blockWhenPointerOverUI && UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
        { if (drawDebug) Debug.Log("LeafPullOff: pointer is over UI"); return; }
#endif

        // broaden the pick a bit; use Raycast first, then SphereCast as fallback.
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        bool got = Physics.Raycast(ray, out hit, grabMaxDistance, leafPickMask,
                                   includeTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore);

        if (!got)
        {
            var hits = Physics.SphereCastAll(ray, Mathf.Max(0.01f, pickRadius), grabMaxDistance, leafPickMask,
                                             includeTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore);
            if (hits != null && hits.Length > 0)
            {
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                hit = hits[0];
                got = true;
            }
        }

        if (!got) { if (drawDebug) Debug.Log("LeafPullOff: no collider hit (mask/layer?)"); return; }

        var lpo = hit.collider.GetComponentInParent<LeafPullOff>();
        if (lpo != this) { if (drawDebug) Debug.Log("LeafPullOff: hit something else, not this leaf."); return; }

        _grabbedDepthFromCam = hit.distance + handDepthBias;
        _handProxy.position = hit.point;

        if (_jointToHand == null)
            _jointToHand = CreateHandJoint(_leafRB, _handProxy, (_pinhole ? handSlackAttached : 0f));
        else
            SetHandJointSlack((_pinhole ? handSlackAttached : 0f));

        _state = (_pinhole != null) ? State.AttachedHeld : State.DetachedHeld;
    }

    void TryRelease()
    {
        if (_state == State.AttachedHeld)
        {
            // still attached → just stop holding; stays attached
            DestroyHandJoint();
            _state = State.AttachedIdle;
        }
        else if (_state == State.DetachedHeld)
        {
            DestroyHandJoint();
            _leafRB.useGravity = true;
            _state = State.DetachedFree;
            if (destroyAfterDrop && destroyAfterSeconds > 0f)
                Destroy(gameObject, destroyAfterSeconds);
        }
    }

    // ── hand proxy ──
    void UpdateHandProxyLockedDepth()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        Vector3 target = ray.GetPoint(_grabbedDepthFromCam);
        Vector3 newPos = Vector3.Lerp(_handProxy.position, target, 1f - Mathf.Exp(-handFollowSpeed * Time.fixedDeltaTime));
        _handProxy.MovePosition(newPos);
    }

    // ── tug into stem while held & attached ──
    void TugStemWhileHeld()
    {
        if (_pinhole == null || stemRigidbody == null || stemRigidbody.isKinematic) return;

        Vector3 desiredDelta = _handProxy.position - transform.position;
        Vector3 pinToLeaf = transform.position - _pinhole.position;
        Vector3 axis = pinToLeaf.sqrMagnitude > 1e-6f ? pinToLeaf.normalized : Vector3.up;

        float along = Vector3.Dot(desiredDelta, axis);
        Vector3 tug = axis * (along * stemTugScale);
        if (tug.magnitude > stemTugMaxImpulse) tug = tug.normalized * stemTugMaxImpulse;

        stemRigidbody.AddForceAtPosition(tug, _pinhole.position, stemTugMode);
    }

    // ── internal break ──
    void DoBreak()
    {
        if (_broken) return;
        _broken = true;

        if (logBreaks) Debug.Log($"Leaf '{name}' broke at {Time.time:F3}");

        // spray/nudge at socket
        if (_pinhole != null)
        {
            Vector3 dir = (transform.position - _pinhole.position);
            if (dir.sqrMagnitude < 1e-6f) dir = transform.forward;

            if (sapPool) sapPool.Play(_pinhole.position, dir, _pinhole, null);

            if (stemRigidbody && !stemRigidbody.isKinematic)
                stemRigidbody.AddForceAtPosition(dir.normalized * stemBreakNudge, _pinhole.position, ForceMode.Impulse);
        }

        // sever socket joint
        DestroySocketJoint();
        _pinhole = null;

        // force drop, even if held
        DestroyHandJoint();
        _leafRB.useGravity = true;
        _state = State.DetachedFree;

        if (destroyAfterDrop && destroyAfterSeconds > 0f)
            Destroy(gameObject, destroyAfterSeconds);

        OnBreak?.Invoke();
    }

    // ── yank option ──
    void MaybeYankBreak()
    {
        if (_pinhole == null) return;

        float stretch = Vector3.Distance(transform.position, _pinhole.position);
        float estTension = attachSpring * stretch;
        _tensionSmoothed = Mathf.Lerp(_tensionSmoothed, estTension, 1f - Mathf.Exp(-8f * Time.fixedDeltaTime));
        if (stretch >= yankBreakDistance || _tensionSmoothed >= yankBreakTension)
            DoBreak();
    }

    // ── joints ──
    ConfigurableJoint CreateSocketJoint(Rigidbody leaf, Transform pinhole, float slack)
    {
        var j = leaf.gameObject.AddComponent<ConfigurableJoint>();
        var pinParentRB = pinhole.GetComponentInParent<Rigidbody>();
        j.connectedBody = pinParentRB;
        j.autoConfigureConnectedAnchor = false;
        j.anchor = Vector3.zero;

        j.connectedAnchor = (j.connectedBody != null)
            ? j.connectedBody.transform.InverseTransformPoint(pinhole.position)
            : pinhole.position;

        j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Limited;
        var lm = new SoftJointLimit { limit = Mathf.Max(0f, slack) };
        j.linearLimit = lm;
        j.linearLimitSpring = new SoftJointLimitSpring { spring = attachSpring, damper = attachDamper };
        j.enableCollision = false;
        j.projectionMode = JointProjectionMode.PositionAndRotation;
        j.projectionDistance = 0.005f;
        return j;
    }

    ConfigurableJoint CreateHandJoint(Rigidbody leaf, Rigidbody hand, float slack)
    {
        var j = leaf.gameObject.AddComponent<ConfigurableJoint>();
        j.connectedBody = hand;
        j.autoConfigureConnectedAnchor = false;
        j.anchor = Vector3.zero;
        j.connectedAnchor = Vector3.zero;

        j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Limited;
        var lm = new SoftJointLimit { limit = Mathf.Max(0f, slack) };
        j.linearLimit = lm;
        j.linearLimitSpring = new SoftJointLimitSpring { spring = holdSpring, damper = holdDamper };
        j.enableCollision = false;
        j.projectionMode = JointProjectionMode.PositionAndRotation;
        j.projectionDistance = 0.01f;
        return j;
    }

    void SetHandJointSlack(float slack)
    {
        if (_jointToHand == null) return;
        var lm = _jointToHand.linearLimit; lm.limit = Mathf.Max(0f, slack);
        _jointToHand.linearLimit = lm;
    }

    void DestroySocketJoint() { if (_jointToPinhole) { Destroy(_jointToPinhole); _jointToPinhole = null; } }
    void DestroyHandJoint() { if (_jointToHand) { Destroy(_jointToHand); _jointToHand = null; } }
}
