using UnityEngine;
using System;

[DisallowMultipleComponent]
public class LeafPullOff : MonoBehaviour
{
    // ── Scene refs ──
    [Header("Scene Refs")]
    public Camera cam;
    public PinholeLibrary pinholes;     // optional
    public Transform explicitPinhole;   // assigned by spawner
    public Rigidbody stemRigidbody;     // small RB for jiggle/tug
    public SapFxPool sapPool;           // <-- pooled spray (ParticleSystem or ObiEmitter)

    // ── Grab / hand proxy ──
    [Header("Grab (hand)")]
    public float grabMaxDistance = 12f;
    public float handFollowSpeed = 24f;
    public float handDepthBias = 0f;

    // ── Springs ──
    [Header("Attachment spring (leaf ↔ pinhole)")]
    public float attachSpring = 160f;
    public float attachDamper = 20f;
    public float socketSlack = 0.02f;   // visible give at socket

    [Header("Hold spring (leaf ↔ hand)")]
    public float holdSpring = 260f;
    public float holdDamper = 26f;
    public float handSlackAttached = 0.03f; // slack only while attached

    // ── Detach rules ──
    [Header("Detachment")]
    public bool onlyBreakByExternalCut = true;
    public bool allowYankBreak = false;
    public float yankBreakDistance = 0.10f;
    public float yankBreakTension = 45f;

    // ── FX / jiggle ──
    [Header("On Break (sap + jiggle)")]
    public float stemBreakNudge = 0.7f;       // impulse magnitude into stem

    // ── After drop ──
    [Header("After Drop")]
    public bool destroyAfterDrop = true;      // if you want pooling, set this false and add a ReturnToPool script
    public float destroyAfterSeconds = 10f;

    // ── Physics niceties ──
    [Header("Limits")]
    public float maxLinearVelocity = 6f;
    public float maxAngularVelocity = 20f;

    // ── Tug feel ──
    [Header("Tug Feel (while attached & held)")]
    public float stemTugScale = 0.5f;
    public float stemTugMaxImpulse = 0.8f;
    public ForceMode stemTugMode = ForceMode.Force;

    [Header("Debug")]
    public bool drawDebug;

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

    void Awake()
    {
        _leafRB = GetComponent<Rigidbody>() ?? gameObject.AddComponent<Rigidbody>();
        _leafRB.mass = Mathf.Max(0.01f, _leafRB.mass);
        _leafRB.useGravity = false;
        _leafRB.interpolation = RigidbodyInterpolation.Interpolate;
        _leafRB.maxAngularVelocity = maxAngularVelocity;

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
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0)) TryGrab();
        if (Input.GetMouseButtonUp(0)) TryRelease();
    }

    void FixedUpdate()
    {
        if (_leafRB && _leafRB.linearVelocity.sqrMagnitude > maxLinearVelocity * maxLinearVelocity)
            _leafRB.linearVelocity = _leafRB.linearVelocity.normalized * maxLinearVelocity;

        switch (_state)
        {
            case State.AttachedHeld:
                UpdateHandProxyLockedDepth();
                TugStemWhileHeld();
                if (!onlyBreakByExternalCut && allowYankBreak && _pinhole) MaybeYankBreak();
                break;
            case State.DetachedHeld:
                UpdateHandProxyLockedDepth();
                break;
        }

        if (drawDebug && _pinhole)
            Debug.DrawLine(transform.position, _pinhole.position,
                           _state == State.AttachedHeld ? Color.cyan : Color.green, 0f, false);
    }

    void OnDestroy() { if (_handProxy) Destroy(_handProxy.gameObject); }

    // --- input ---
    void TryGrab()
    {
        if (!cam) return;
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out var hit, grabMaxDistance, ~0, QueryTriggerInteraction.Ignore)) return;
        if (!hit.collider || !hit.collider.transform.IsChildOf(transform)) return;

        _grabbedDepthFromCam = Vector3.Dot(transform.position - cam.transform.position, cam.transform.forward);
        _grabbedDepthFromCam = Mathf.Max(0.1f, _grabbedDepthFromCam) + handDepthBias;

        _handProxy.position = transform.position;

        if (_jointToHand == null)
            _jointToHand = CreateHandJoint(_leafRB, _handProxy, (_pinhole ? handSlackAttached : 0f));
        else
            SetHandJointSlack((_pinhole ? handSlackAttached : 0f));

        _state = (_pinhole ? State.AttachedHeld : State.DetachedHeld);
    }

    void TryRelease()
    {
        if (_state == State.AttachedHeld)
        {
            DestroyHandJoint();
            _state = State.AttachedIdle;
        }
        else if (_state == State.DetachedHeld)
        {
            DestroyHandJoint();
            _leafRB.useGravity = true;
            _state = State.DetachedFree;

            // pooled leaves? comment this and call your own ReturnToPool()
            if (destroyAfterDrop && destroyAfterSeconds > 0f)
                Destroy(gameObject, destroyAfterSeconds);
        }
    }

    // --- hand proxy ---
    void UpdateHandProxyLockedDepth()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        Vector3 target = ray.GetPoint(_grabbedDepthFromCam);
        Vector3 newPos = Vector3.Lerp(_handProxy.position, target, 1f - Mathf.Exp(-handFollowSpeed * Time.fixedDeltaTime));
        _handProxy.MovePosition(newPos);
    }

    // --- tug into stem while attached ---
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

    // --- external break (from scissors) ---
    public void BreakNow()
    {
        if (_pinhole == null) return;
        DoBreak();
    }

    void MaybeYankBreak()
    {
        float stretch = Vector3.Distance(transform.position, _pinhole.position);
        float estTension = attachSpring * stretch;
        _tensionSmoothed = Mathf.Lerp(_tensionSmoothed, estTension, 1f - Mathf.Exp(-8f * Time.fixedDeltaTime));
        if (stretch >= yankBreakDistance || _tensionSmoothed >= yankBreakTension) DoBreak();
    }

    void DoBreak()
    {
        // spray burst at the socket (direction from socket to leaf)
        Vector3 dir = (transform.position - _pinhole.position);
        if (dir.sqrMagnitude < 1e-6f) dir = transform.forward;

        if (sapPool)                       // <-- pooled spray
            sapPool.Play(_pinhole.position, dir, _pinhole, null);
        else
            Debug.LogWarning("[LeafPullOff] No SapFxPool assigned, skipping spray.");

        // one-shot nudge into stem
        if (stemRigidbody && !stemRigidbody.isKinematic)
            stemRigidbody.AddForceAtPosition(dir.normalized * stemBreakNudge, _pinhole.position, ForceMode.Impulse);

        // sever socket
        DestroySocketJoint();

        // keep holding: tighten hand slack to 0 so it hugs cursor
        SetHandJointSlack(0f);

        _pinhole = null;

        if (_state == State.AttachedHeld)
        {
            _state = State.DetachedHeld;
            _leafRB.useGravity = false;
        }
        else
        {
            _state = State.DetachedFree;
            _leafRB.useGravity = true;

            if (destroyAfterDrop && destroyAfterSeconds > 0f)
                Destroy(gameObject, destroyAfterSeconds);
        }

        OnBreak?.Invoke();
    }

    // --- joints ---
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
