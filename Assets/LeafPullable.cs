using Obi;
using System;
using UnityEngine;

/// Add this alongside RopeLeafFollowerSimple on each leaf.
/// - On Start: snaps to nearest LeafPinhole (optionally restricted by rope).
/// - LMB to grab; release to drop.
/// - Tug distance/tension to break; sap bursts on break and on first ground hit.
/// - Despawns after N seconds when dropped (optional).
[DefaultExecutionOrder(32010)]
[DisallowMultipleComponent]
public class LeafPullable : MonoBehaviour
{
    [Header("Scene Refs")]
    public Camera cam;
    public RopeLeafFollowerSimple follower;
    public Rigidbody stemRigidbody;     // small RB on stem for a subtle nudge (optional)
    public SapFxPool sapPool;           // your pooled fluid FX

    [Header("Pick")]
    public LayerMask pickMask = ~0;
    public float pickRadius = 0.02f;
    public float maxPickDistance = 12f;
    public bool includeTriggers = true;

    [Header("Attach (leaf ↔ pinhole)")]
    public float attachSpring = 120f;     // visual “tug” strength while attached
    public float attachDamper = 18f;
    public float yankBreakDistance = 0.09f; // pull this far to break
    public float yankBreakTension = 45f;    // or reach this estimated tension
    public float stemBreakNudge = 0.7f;     // push on stem when it snaps

    [Header("Hold (leaf ↔ hand)")]
    public float handSpring = 240f;
    public float handDamper = 24f;
    public float handSlackAttached = 0.03f; // small slack when still attached
    public float handFollowSpeed = 24f;
    public float handDepthBias = 0f;

    [Header("Drop / FX")]
    public bool sapOnDropImpact = true;
    public float dropImpactMinSpeed = 0.6f;
    public bool destroyAfterDrop = true;
    public float destroyDelay = 10f;

    [Header("Limits")]
    public float maxLinearVelocity = 6f;
    public float maxAngularVelocity = 20f;

    [Header("Debug")]
    public bool logSteps = false;

    public System.Action OnBreak;

    enum State { AttachedIdle, AttachedHeld, DetachedHeld, DetachedFree }
    State _state = State.AttachedIdle;

    Rigidbody _rb;
    Rigidbody _handProxy;
    ConfigurableJoint _jointToHand;
    Transform _pinhole;
    float _grabbedDepthFromCam;
    float _tensionSmoothed;
    bool _broken;
    bool _droppedSplattered;

    const float kEps = 1e-6f;

    void Awake()
    {
        cam = cam ? cam : Camera.main;
        _rb = GetComponent<Rigidbody>() ?? gameObject.AddComponent<Rigidbody>();
        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.maxAngularVelocity = maxAngularVelocity;

        var proxy = new GameObject($"{name}_HandProxy");
        proxy.hideFlags = HideFlags.HideInHierarchy;
        _handProxy = proxy.AddComponent<Rigidbody>();
        _handProxy.isKinematic = true;
        _handProxy.interpolation = RigidbodyInterpolation.Interpolate;

        if (!follower) follower = GetComponent<RopeLeafFollowerSimple>();
    }

    void Start()
    {
        // Choose nearest pinhole (optionally restrict to follower.rope if set)
        _pinhole = FindNearestPinhole(follower ? follower.rope : null, transform.position);
        if (_pinhole)
        {
            if (follower) follower.pinhole = _pinhole;
            if (logSteps) Debug.Log($"{name}: Attached to pinhole '{_pinhole.name}'.");
        }
        else if (logSteps) Debug.LogWarning($"{name}: No LeafPinhole found nearby.");
    }

    void OnDestroy()
    {
        if (_handProxy) Destroy(_handProxy.gameObject);
    }

    void Update()
    {
        // LMB grab/release
        if (Input.GetMouseButtonDown(0)) TryGrab();
        if (Input.GetMouseButtonUp(0)) TryRelease();
    }

    void FixedUpdate()
    {
        // modest velocity clamp so it doesn’t whip around
        if (_rb.linearVelocity.sqrMagnitude > maxLinearVelocity * maxLinearVelocity)
            _rb.linearVelocity = _rb.linearVelocity.normalized * maxLinearVelocity;

        if (_state == State.AttachedHeld || _state == State.DetachedHeld)
            UpdateHandProxy();

        // ★ while held & attached: add gentle spring toward socket and evaluate break
        if (_state == State.AttachedHeld && _pinhole)
        {
            SoftTugTowardPinhole();   // ★ adds mild pull so tension builds without teleporting
            MaybeYankBreak();
        }
    }

    void OnCollisionEnter(Collision c)
    {
        if (_state == State.DetachedFree && !_droppedSplattered && sapOnDropImpact)
        {
            if (c.relativeVelocity.magnitude >= dropImpactMinSpeed)
            {
                _droppedSplattered = true;
                if (sapPool) sapPool.Play(c.GetContact(0).point, Vector3.up);
            }
        }
    }

    // ——— Picking & grabbing ———
    void TryGrab()
    {
        var leaf = RayPickSelf(out RaycastHit hit);
        if (!leaf) return;

        _grabbedDepthFromCam = hit.distance + handDepthBias;
        _handProxy.position = hit.point;

        if (_jointToHand == null)
            _jointToHand = CreateHandJoint(_rb, _handProxy, (_state == State.AttachedIdle ? handSlackAttached : 0f));
        else
            SetHandJointSlack((_state == State.AttachedIdle ? handSlackAttached : 0f));

        // ★ stop visual teleporting while we pull, so stretch can accumulate
        if (follower) follower.enabled = false;  // ★

        _state = (_broken ? State.DetachedHeld : State.AttachedHeld);
    }

    void TryRelease()
    {
        if (_state == State.AttachedHeld)
        {
            DestroyHandJoint();

            // ★ still attached -> resume following visuals
            if (follower && !_broken) follower.enabled = true; // ★

            _state = State.AttachedIdle;
        }
        else if (_state == State.DetachedHeld)
        {
            DestroyHandJoint();
            _rb.useGravity = true;
            _state = State.DetachedFree;

            if (destroyAfterDrop && destroyDelay > 0f)
                Destroy(gameObject, destroyDelay);
        }
    }

    // ——— Break mechanics ———
    void MaybeYankBreak()
    {
        Vector3 socket = _pinhole ? _pinhole.position : transform.position;
        float stretch = Vector3.Distance(transform.position, socket);
        float estTension = attachSpring * stretch;

        // smooth so brief spikes don’t insta-break
        _tensionSmoothed = Mathf.Lerp(_tensionSmoothed, estTension, 1f - Mathf.Exp(-8f * Time.fixedDeltaTime));

        if (stretch >= yankBreakDistance || _tensionSmoothed >= yankBreakTension)
            DoBreak();
    }

    public void DoBreak()
    {
        if (_broken) return;
        _broken = true;
        if (logSteps) Debug.Log($"{name}: BREAK at t={Time.time:F2}");

        // sap burst from socket & nudge stem
        if (_pinhole)
        {
            Vector3 dir = (transform.position - _pinhole.position);
            if (dir.sqrMagnitude < kEps) dir = transform.forward;
            if (sapPool) sapPool.Play(_pinhole.position, dir.normalized, _pinhole, null);
            if (stemRigidbody && !stemRigidbody.isKinematic)
                stemRigidbody.AddForceAtPosition(dir.normalized * stemBreakNudge, _pinhole.position, ForceMode.Impulse);
        }

        // stop following visuals; enter held/free states as appropriate
        if (follower) follower.enabled = false;
        _pinhole = null;

        _rb.useGravity = _jointToHand ? false : true;
        _state = _jointToHand ? State.DetachedHeld : State.DetachedFree;

        OnBreak?.Invoke();
    }

    // ——— Helpers ———
    Transform FindNearestPinhole(ObiRopeBase restrictToRope, Vector3 from)
    {
        LeafPinhole[] all = GameObject.FindObjectsOfType<LeafPinhole>();
        Transform best = null; float bestD2 = float.PositiveInfinity;

        foreach (var ph in all)
        {
            if (restrictToRope && ph.rope && ph.rope != restrictToRope) continue;
            float d2 = (ph.transform.position - from).sqrMagnitude;
            if (d2 < bestD2) { bestD2 = d2; best = ph.transform; }
        }
        return best;
    }

    bool RayPickSelf(out RaycastHit best)
    {
        best = default;
        if (!cam) return false;

        var ray = cam.ScreenPointToRay(Input.mousePosition);
        var qti = includeTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;

        // 1) SphereCast first (nicer UX), only accept hits on THIS leaf
        var hits = Physics.SphereCastAll(ray, Mathf.Max(0.001f, pickRadius), maxPickDistance, pickMask, qti);
        if (hits != null && hits.Length > 0)
        {
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider && h.collider.GetComponentInParent<LeafPullable>() == this)
                { best = h; return true; }
            }
        }
        // 2) Thin ray fallback
        if (Physics.Raycast(ray, out var rh, maxPickDistance, pickMask, qti))
        {
            if (rh.collider && rh.collider.GetComponentInParent<LeafPullable>() == this)
            { best = rh; return true; }
        }
        // 3) Bounds proximity (very forgiving)
        var r = GetComponentInChildren<Renderer>();
        Vector3 center = r ? r.bounds.center : transform.position;
        Vector3 ro = ray.origin, rd = ray.direction;
        float t = Mathf.Clamp(Vector3.Dot(center - ro, rd), 0f, maxPickDistance);
        Vector3 closest = ro + rd * t;
        float worldRadius = Mathf.Max(0.06f, pickRadius * 2f);
        if (Vector3.Distance(center, closest) <= worldRadius)
        {
            best.point = closest; best.distance = t;
            return true;
        }
        return false;
    }

    ConfigurableJoint CreateHandJoint(Rigidbody leaf, Rigidbody hand, float slack)
    {
        var j = leaf.gameObject.AddComponent<ConfigurableJoint>();
        j.connectedBody = hand;
        j.autoConfigureConnectedAnchor = false;
        j.anchor = Vector3.zero;
        j.connectedAnchor = Vector3.zero;

        j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Limited;
        j.linearLimit = new SoftJointLimit { limit = Mathf.Max(0f, slack) };
        j.linearLimitSpring = new SoftJointLimitSpring { spring = handSpring, damper = handDamper };

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

    void DestroyHandJoint()
    {
        if (_jointToHand)
        {
            Destroy(_jointToHand);
            _jointToHand = null;
        }
    }

    void UpdateHandProxy()
    {
        var ray = cam.ScreenPointToRay(Input.mousePosition);
        Vector3 target = ray.GetPoint(_grabbedDepthFromCam);
        Vector3 newPos = Vector3.Lerp(_handProxy.position, target, 1f - Mathf.Exp(-handFollowSpeed * Time.fixedDeltaTime));
        _handProxy.MovePosition(newPos);
    }

    // ★ gentle spring toward socket while held (no teleporting)
    void SoftTugTowardPinhole()
    {
        if (!_rb || !_pinhole) return;
        Vector3 toSocket = _pinhole.position - _rb.position;
        float k = attachSpring;   // ~120–200
        float c = attachDamper;   // ~15–30
        _rb.AddForce(k * toSocket - c * _rb.linearVelocity, ForceMode.Acceleration);
    }
}
