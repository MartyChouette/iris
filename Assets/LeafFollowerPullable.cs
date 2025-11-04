// File: LeafFollowerPullable.cs
// One-stop happy-leaf: follows rope (left/right), RMB tug to break, sap on break,
// stays in hand until release, then falls, optional sap-on-drop, despawns.

using UnityEngine;
using Obi;
using System;

[DefaultExecutionOrder(32000)]
[DisallowMultipleComponent]
public class LeafFollowerPullable : MonoBehaviour
{
    // ───────── Rope follow (Obi 7 style) ─────────
    [Header("Rope Follow")]
    public ObiRopeBase rope;                         // rope piece this leaf rides
    [Tooltip("Segment start index in actor space (segment = i..i+1).")]
    public int actorIndex = 0;                       // clamped to [0..activeParticleCount-2]
    public Transform pinhole;                        // socket placed on stem surface (optional but recommended)

    [Tooltip("Distance from stem center to leaf base.")]
    public float radialOffset = 0.02f;
    [Tooltip("Azimuth around stem in degrees. +90 = right, -90 = left.")]
    public float azimuthDeg = 90f;
    public Vector3 referenceUp = Vector3.up;         // stabilizes roll

    [Header("Leaf local axes")]
    public Vector3 leafLocalOut = Vector3.right;     // which local axis points out from stem
    public Vector3 leafLocalForward = Vector3.up;    // which local axis runs along stem

    [Header("Smoothing")]
    public float posLerp = 25f;
    public float rotLerp = 25f;

    // add near your other pick fields:
    [Header("Pick Tuning")]
    public bool includeTriggers = true;
    [Tooltip("Log reasons when a pick fails.")]
    public bool verbosePickLogs = true;

  


    // ───────── RMB grab / tug / break ─────────
    [Header("Grab / Tug")]
    public Camera cam;
    public LayerMask leafPickMask = ~0;
    public float pickRadius = 0.02f;
    public float grabMaxDistance = 12f;
    public float handFollowSpeed = 24f;
    public float handDepthBias = 0f;

    [Header("Attachment Spring (leaf↔pinhole)")]
    public float attachSpring = 160f;
    public float attachDamper = 20f;
    public float socketSlack = 0.02f;

    [Header("Hold Spring (leaf↔hand)")]
    public float holdSpring = 260f;
    public float holdDamper = 26f;
    public float handSlackAttached = 0.03f;

    [Header("Break Rules")]
    public bool breakByYank = true;                  // your “pull a bit then break”
    public float yankBreakDistance = 0.09f;          // meters from socket
    public float yankBreakTension = 45f;             // spring-tension approx
    public bool breakByCutNotify = true;             // allows OnStemCutAt(worldPoint)
    public float cutDetachRadius = 0.04f;

    // ───────── FX / physics / despawn ─────────
    [Header("FX / Physics")]
    public SapFxPool sapPool;                        // fires on break (and optionally on drop)
    public bool sapOnDrop = false;
    public float stemBreakNudge = 0.7f;
    public Rigidbody stemRigidbody;

    [Header("After Drop")]
    public bool destroyAfterDrop = true;             // set false if using your own pool
    public float destroyAfterSeconds = 10f;

    [Header("Limits")]
    public float maxLinearVelocity = 6f;
    public float maxAngularVelocity = 20f;

    [Header("Debug")]
    public bool drawDebug = false;
    public bool logBreaks = false;

    public event Action OnBreak;

    enum State { AttachedIdle, AttachedHeld, DetachedHeld, DetachedFree }
    State _state = State.AttachedIdle;

    // internals
    Rigidbody _leafRB;
    ObiSolver _solver;
    LineRenderer _dbgLine;
    Rigidbody _handProxy;
    ConfigurableJoint _jointToHand;
    bool _broken;
    float _grabbedDepthFromCam;
    float _tensionSmoothed;
    Vector3 _lastSegCenter;                          // for fallback when rope changes
    static readonly float kEps = 1e-6f;

    void Awake()
    {
        cam = cam ? cam : Camera.main;
        if (!cam && verbosePickLogs)
            Debug.LogWarning($"{name}: LeafFollowerPullable has no Camera. Assign `cam` or tag your camera MainCamera.");

        _leafRB = GetComponent<Rigidbody>() ?? gameObject.AddComponent<Rigidbody>();
        _leafRB.useGravity = false;
        _leafRB.interpolation = RigidbodyInterpolation.Interpolate;
        _leafRB.maxAngularVelocity = maxAngularVelocity;

        var go = new GameObject($"{name}_GrabProxy");
        go.hideFlags = HideFlags.HideInHierarchy;
        _handProxy = go.AddComponent<Rigidbody>();
        _handProxy.isKinematic = true;
        _handProxy.interpolation = RigidbodyInterpolation.Interpolate;

        CacheSolver();
    }


    void OnEnable() => CacheSolver();
    void OnDestroy() { if (_handProxy) Destroy(_handProxy.gameObject); }

    void CacheSolver() => _solver = rope ? rope.solver : null;

    // ─────────────────────────────────────────────────────────────
    // Public: allow your cutter to notify a nearby cut
    public void OnStemCutAt(Vector3 worldCutPoint)
    {
        if (!_broken && breakByCutNotify && pinhole &&
            Vector3.Distance(worldCutPoint, pinhole.position) <= cutDetachRadius)
            DoBreak();
    }
    // ─────────────────────────────────────────────────────────────

    void Update()
    {
        // RMB grab + release
        if (Input.GetMouseButtonDown(1)) TryGrab();
        if (Input.GetMouseButtonUp(1)) TryRelease();
    }

    void FixedUpdate()
    {
        // clamp linear speed
        if (_leafRB && _leafRB.linearVelocity.sqrMagnitude > maxLinearVelocity * maxLinearVelocity)
            _leafRB.linearVelocity = _leafRB.linearVelocity.normalized * maxLinearVelocity;

        if (_state == State.AttachedHeld || _state == State.DetachedHeld)
            UpdateHandProxyLockedDepth();

        if (_state == State.AttachedHeld && breakByYank && pinhole)
            MaybeYankBreak();
    }

    void LateUpdate()
    {
        // While attached (idle or held), follow the rope segment:
        if (_state == State.AttachedIdle || _state == State.AttachedHeld)
            FollowRopeSegment();
    }

    // ───────── Rope follow math ─────────
    void FollowRopeSegment()
    {
        if (rope == null || _solver == null) return;

        var siL = rope.solverIndices;
        var rp = _solver.renderablePositions;
        int n = rope.activeParticleCount;

        if (siL == null || rp == null || n < 2) return;

        actorIndex = Mathf.Clamp(actorIndex, 0, n - 2);
        if (siL.count <= actorIndex + 1) return;

        int s0 = siL[actorIndex];
        int s1 = siL[actorIndex + 1];
        if (!ValidSi(s0, rp.count) || !ValidSi(s1, rp.count)) return;

        Vector3 p0 = _solver.transform.TransformPoint((Vector3)rp[s0]);
        Vector3 p1 = _solver.transform.TransformPoint((Vector3)rp[s1]);
        Vector3 center = (p0 + p1) * 0.5f;
        _lastSegCenter = center;

        Vector3 tangent = (p1 - p0);
        if (tangent.sqrMagnitude < kEps) tangent = Vector3.up;
        tangent.Normalize();

        Vector3 upRef = referenceUp.normalized;
        if (Mathf.Abs(Vector3.Dot(upRef, tangent)) > 0.98f)
        {
            upRef = Vector3.Cross(tangent, Vector3.right).normalized;
            if (upRef.sqrMagnitude < kEps) upRef = Vector3.Cross(tangent, Vector3.forward).normalized;
        }

        Vector3 radial0 = Vector3.Cross(upRef, tangent).normalized;
        Quaternion roll = Quaternion.AngleAxis(azimuthDeg, tangent);
        Vector3 radial = (roll * radial0).normalized;

        Vector3 basePos = pinhole ? pinhole.position : center;
        Vector3 targetPos = basePos + radial * radialOffset;

        Quaternion alignF = Quaternion.FromToRotation(leafLocalForward.normalized, tangent);
        Vector3 outAfterF = alignF * leafLocalOut.normalized;
        Quaternion alignO = Quaternion.FromToRotation(outAfterF, radial);
        Quaternion targetRot = alignO * alignF;

        float kPos = 1f - Mathf.Exp(-posLerp * Time.deltaTime);
        float kRot = 1f - Mathf.Exp(-rotLerp * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, targetPos, kPos);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, kRot);

        if (drawDebug)
        {
            Debug.DrawLine(center, center + tangent * 0.06f, Color.yellow);
            Debug.DrawLine(center, center + radial * 0.06f, Color.cyan);
        }
    }

    static bool ValidSi(int si, int count) => (si >= 0 && si < count);

    // ───────── Input: RMB grab / release ─────────
    void TryGrab()
    {
        if (!cam) { if (verbosePickLogs) Debug.Log($"{name}: no camera set; cannot grab."); return; }

        // quick mask sanity
        int myLayer = gameObject.layer;
        if (((leafPickMask.value >> myLayer) & 1) == 0 && verbosePickLogs)
            Debug.Log($"{name}: layer '{LayerMask.LayerToName(myLayer)}' not in leafPickMask.");

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        QueryTriggerInteraction qti = includeTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;

        // 1) spherecast (best UX)
        LeafFollowerPullable picked = null; RaycastHit best = default;
        var hits = Physics.SphereCastAll(ray, Mathf.Max(0.001f, pickRadius), grabMaxDistance, leafPickMask, qti);
        if (hits != null && hits.Length > 0)
        {
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                var l = h.collider ? h.collider.GetComponentInParent<LeafFollowerPullable>() : null;
                if (l == this) { picked = l; best = h; break; }
            }
        }

        // 2) fallback: thin raycast
        if (picked == null && Physics.Raycast(ray, out var rh, grabMaxDistance, leafPickMask, qti))
        {
            var l = rh.collider ? rh.collider.GetComponentInParent<LeafFollowerPullable>() : null;
            if (l == this) { picked = l; best = rh; }
        }

        // 3) last-chance: proximity to this leaf's bounds center
        if (picked == null)
        {
            // pick a representative point for this leaf (renderer bounds center if possible)
            Vector3 leafPoint = transform.position;
            var r = GetComponentInChildren<Renderer>();
            if (r) leafPoint = r.bounds.center;

            // distance from ray to that point (world)
            Vector3 ro = ray.origin, rd = ray.direction;
            Vector3 w = leafPoint - ro;
            float t = Mathf.Clamp(Vector3.Dot(w, rd), 0f, grabMaxDistance);
            Vector3 closest = ro + rd * t;
            float worldRadius = Mathf.Max(0.06f, pickRadius * 2.0f); // generous fallback
            float d = Vector3.Distance(closest, leafPoint);

            if (d <= worldRadius)
            {
                picked = this;
                best.point = closest;
                best.distance = t;
                if (verbosePickLogs) Debug.Log($"{name}: proximity-grab (no collider hit). d={d:F3}, r={worldRadius:F3}");
            }
        }

        if (picked == null)
        {
            if (verbosePickLogs)
                Debug.Log($"{name}: pick failed. Check collider covers mesh & is enabled, layer not 'Ignore Raycast', " +
                          $"mask includes layer, includeTriggers={(includeTriggers ? "ON" : "OFF")}, " +
                          $"pickRadius={pickRadius}, maxDist={grabMaxDistance}.");
            return;
        }

        // arm grab
        _grabbedDepthFromCam = best.distance + handDepthBias;
        _handProxy.position = best.point;

        if (_jointToHand == null)
            _jointToHand = CreateHandJoint(_leafRB, _handProxy, (_state == State.AttachedIdle ? handSlackAttached : 0f));
        else
            SetHandJointSlack((_state == State.AttachedIdle ? handSlackAttached : 0f));

        _state = (_broken ? State.DetachedHeld : State.AttachedHeld);
    }



    void TryRelease()
    {
        if (_state == State.AttachedHeld)
        {
            // still attached → stop holding; remain attached
            DestroyHandJoint();
            _state = State.AttachedIdle;
        }
        else if (_state == State.DetachedHeld)
        {
            DestroyHandJoint();
            _leafRB.useGravity = true;
            _state = State.DetachedFree;

            if (sapOnDrop && sapPool)
                sapPool.Play(transform.position, Vector3.up);

            if (destroyAfterDrop && destroyAfterSeconds > 0f)
                Destroy(gameObject, destroyAfterSeconds);
        }
    }

    // ───────── Yank-to-break ─────────
    void MaybeYankBreak()
    {
        Vector3 socket = pinhole ? pinhole.position : _lastSegCenter;
        float stretch = Vector3.Distance(transform.position, socket);
        float estTension = attachSpring * stretch;
        _tensionSmoothed = Mathf.Lerp(_tensionSmoothed, estTension, 1f - Mathf.Exp(-8f * Time.fixedDeltaTime));

        if (stretch >= yankBreakDistance || _tensionSmoothed >= yankBreakTension)
            DoBreak();
    }

    // ───────── Break routine ─────────
    void DoBreak()
    {
        if (_broken) return;
        _broken = true;
        if (logBreaks) Debug.Log($"Leaf '{name}' broke @ {Time.time:F3}");

        // Sap burst from socket, push stem a bit:
        if (pinhole)
        {
            Vector3 dir = (transform.position - pinhole.position);
            if (dir.sqrMagnitude < kEps) dir = transform.forward;

            if (sapPool) sapPool.Play(pinhole.position, dir, pinhole, null);
            if (stemRigidbody && !stemRigidbody.isKinematic)
                stemRigidbody.AddForceAtPosition(dir.normalized * stemBreakNudge, pinhole.position, ForceMode.Impulse);
        }

        // Stop following; stay held if currently held
        pinhole = null;
        _leafRB.useGravity = false;         // still in hand
        _state = (_jointToHand ? State.DetachedHeld : State.DetachedFree);
        if (_state == State.DetachedFree) _leafRB.useGravity = true;

        OnBreak?.Invoke();
    }

    // ───────── Joint helpers ─────────
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

    void DestroyHandJoint()
    {
        if (_jointToHand)
        {
            Destroy(_jointToHand);
            _jointToHand = null;
        }
    }

    // Keep the hand proxy at a fixed depth from the camera and smooth toward the cursor.
    void UpdateHandProxyLockedDepth()
    {
        if (!cam || _handProxy == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        Vector3 target = ray.GetPoint(_grabbedDepthFromCam);
        Vector3 newPos = Vector3.Lerp(
            _handProxy.position,
            target,
            1f - Mathf.Exp(-handFollowSpeed * Time.fixedDeltaTime)
        );

        _handProxy.MovePosition(newPos);
    }

}
