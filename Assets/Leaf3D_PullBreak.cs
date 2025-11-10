using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
[DefaultExecutionOrder(32000)]
[RequireComponent(typeof(Rigidbody))]
public class Leaf3D_PullBreak : MonoBehaviour, LeafTarget
{
    [Header("Refs")]
    [SerializeField] RopeLeafFollower_Bespoke follower;     // keeps us riding a rope particle/segment
    public Camera cam;
    public Transform attachSocket;                          // little empty on the stem
    public Rigidbody stemRigidbody;                         // optional: small recoil/tug receiver

    [Header("Authoring Pose (captured at Awake if socket present)")]
    public Vector3 localPosOffset;
    public Quaternion localRotOffset = Quaternion.identity;

    [Header("Pull-to-Break")]
    [Min(0.01f)] public float breakDistance = 0.12f;        // meters from socket to tear
    public bool stayInHandAfterBreak = true;
    public Vector3 breakImpulse = new(0, 0.8f, 0);          // initial kick on tear

    [Header("Tug physics (while still attached)")]
    [Tooltip("Newton per meter of stretch applied to stem at the socket while grabbed + attached.")]
    public float tugForcePerMeter = 18f;
    public ForceMode tugMode = ForceMode.Force;

    [Header("Picking (internal mode only)")]
    public bool blockWhenPointerOverUI = true;
    public LayerMask pickMask = ~0;
    public float pickRayDistance = 100f;

    [Header("FX")]
    public float collisionBurstCooldown = 0.15f;
    [Serializable] public class Vec3Vec3Event : UnityEvent<Vector3, Vector3> { }
    public Vec3Vec3Event onBreakBurst = new();
    public Vec3Vec3Event onCollisionBurst = new();

    [Header("Control Mode")]
    [Tooltip("TRUE if driven by NewLeafGrabber. FALSE = this script reads mouse/touch.")]
    public bool externalControl = true;

    // ── runtime ──
    Rigidbody _rb;
    Collider[] _cols;
    bool _attached = true;              // remains true until we actually tear
    bool _grabbed = false;
    float _lastCollisionBurstTime = -999f;

    Vector3 _grabStartSocketPos;
    Vector3 _grabPlaneNormal;
    Vector3 _handWorld;

    public Vector3 GetPosition() => transform.position;

    void OnValidate() { CacheRefs(); }
    void Awake()
    {
        CacheRefs();

        // attached state config
        _rb.isKinematic = true;
        _rb.detectCollisions = false;
        _rb.interpolation = RigidbodyInterpolation.None;

        if (attachSocket != null)
        {
            localPosOffset = attachSocket.InverseTransformPoint(transform.position);
            localRotOffset = Quaternion.Inverse(attachSocket.rotation) * transform.rotation;
            ApplyAttachedPose(); // collider will move with MovePosition/MoveRotation (no drift)
        }
    }

    void CacheRefs()
    {
        if (!follower) follower = GetComponent<RopeLeafFollower_Bespoke>();
        if (!cam) cam = Camera.main;
        if (!_rb) _rb = GetComponent<Rigidbody>();
        if (_cols == null || _cols.Length == 0) _cols = GetComponentsInChildren<Collider>(true);
    }

    void Update()
    {
        // when attached & not grabbed, stay snapped to socket (mesh + colliders stay together)
        if (_attached && !_grabbed && attachSocket) ApplyAttachedPose();

        if (!externalControl) HandleInputInternal();

        if (!_grabbed) return;

        if (!externalControl) UpdateHandWorldFromDevice();

        // while attached, we "stretch" toward the hand, but still snap each frame so it visually follows rope
        if (_attached)
        {
            if (_rb.isKinematic) _rb.MovePosition(_handWorld);
            else transform.position = _handWorld;

            // apply tug to stem so stem reacts (before breaking)
            if (stemRigidbody && attachSocket && tugForcePerMeter > 0f)
            {
                Vector3 socket = attachSocket.position;
                Vector3 delta = _handWorld - socket;
                Vector3 tug = delta.normalized * (delta.magnitude * tugForcePerMeter);
                stemRigidbody.AddForceAtPosition(tug, socket, tugMode);
            }

            Vector3 anchor = attachSocket ? attachSocket.position : _grabStartSocketPos;
            float dist = Vector3.Distance(anchor, _handWorld);
            if (dist >= breakDistance) BreakOff();
        }
        else if (stayInHandAfterBreak)
        {
            PoseLeafAtHand();
        }
    }

    // ───────────────────── external control (NewLeafGrabber) ─────────────────────
    public void BeginExternalGrab(Vector3 anchorWorld, Vector3 planeNormal)
    {
        // pause follower only while we’re actively stretching; re-enable if we release without tearing
        if (_attached && follower) follower.enabled = false;

        _rb.isKinematic = true;
        _rb.detectCollisions = false;

        _grabbed = true;
        _grabPlaneNormal = planeNormal;
        _grabStartSocketPos = anchorWorld;
    }

    public void SetExternalHand(Vector3 handWorld)
    {
        _handWorld = handWorld;

        if (_attached)
        {
            if (_rb.isKinematic) _rb.MovePosition(_handWorld);
            else transform.position = _handWorld;

            Vector3 anchor = attachSocket ? attachSocket.position : _grabStartSocketPos;
            float dist = Vector3.Distance(anchor, _handWorld);
            if (dist >= breakDistance) BreakOff();
        }
        else if (stayInHandAfterBreak) PoseLeafAtHand();
    }

    public void EndExternalGrab()
    {
        _grabbed = false;

        if (_attached)
        {
            // resume following the rope so if the stem falls, we go with that segment
            if (follower) follower.enabled = true;

            _rb.isKinematic = true;
            _rb.detectCollisions = false;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
        else
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
    }

    public void TearOff()
    {
        if (_attached) BreakOff();
    }

    // ───────────────────────── helpers ─────────────────────────
    void ApplyAttachedPose()
    {
        if (!attachSocket) return;
        Vector3 pos = attachSocket.TransformPoint(localPosOffset);
        Quaternion rot = attachSocket.rotation * localRotOffset;

        if (_rb.isKinematic)
        {
            _rb.MovePosition(pos);
            _rb.MoveRotation(rot);
        }
        else
        {
            transform.SetPositionAndRotation(pos, rot);
        }
    }

    void PoseLeafAtHand()
    {
        if (_rb.isKinematic) _rb.MovePosition(_handWorld);
        else transform.position = _handWorld;

        if (cam)
        {
            Quaternion face = Quaternion.LookRotation(-cam.transform.forward, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, face, 0.25f);
        }
    }

    void BreakOff()
    {
        _attached = false;

        // once torn, we no longer follow the rope; physics takes over:
        if (follower) follower.enabled = false;

        _rb.isKinematic = false;
        _rb.detectCollisions = true;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _rb.AddForce(breakImpulse, ForceMode.VelocityChange);

        if (TryGetComponent(out MeshCollider mc) && !mc.convex) mc.convex = true;

        if (stemRigidbody && attachSocket)
            stemRigidbody.AddForceAtPosition(-breakImpulse, attachSocket.position, ForceMode.VelocityChange);

        Vector3 pos = attachSocket ? attachSocket.position : transform.position;
        Vector3 normal = cam ? -cam.transform.forward : (attachSocket ? attachSocket.up : Vector3.up);
        onBreakBurst.Invoke(pos, normal);
    }

    // ───────────────────── optional internal input ─────────────────────
    void HandleInputInternal()
    {
        bool down = false, up = false;
        Vector2 scr = default;

#if ENABLE_INPUT_SYSTEM
        var m = Mouse.current;
        if (m != null) { down = m.leftButton.wasPressedThisFrame; up = m.leftButton.wasReleasedThisFrame; scr = m.position.ReadValue(); }
#else
        down = Input.GetMouseButtonDown(0);
        up   = Input.GetMouseButtonUp(0);
        scr  = Input.mousePosition;
#endif
        if (blockWhenPointerOverUI && EventSystem.current && EventSystem.current.IsPointerOverGameObject())
        { if (up) _grabbed = false; return; }

        if (down)
        {
            if (HitThisLeaf(scr))
            {
                _grabbed = true;
                _grabPlaneNormal = cam ? cam.transform.forward : Vector3.forward;
                _grabStartSocketPos = attachSocket ? attachSocket.position : transform.position;
                UpdateHandWorld(scr);

                _rb.isKinematic = true;
                _rb.detectCollisions = false;
                if (_attached && follower) follower.enabled = false; // pause while stretching
            }
        }
        else if (up && _grabbed)
        {
            _grabbed = false;
            if (_attached && follower) follower.enabled = true; // resume following
        }
    }

    bool HitThisLeaf(Vector2 scr)
    {
        if (cam == null || _cols == null || _cols.Length == 0) return false;
        Ray ray = cam.ScreenPointToRay(scr);

        // prefer non-triggers for picking (Ignore triggers)
        if (Physics.Raycast(ray, out var hit, pickRayDistance, pickMask, QueryTriggerInteraction.Ignore))
            return hit.collider && hit.collider.transform.IsChildOf(transform);

        return false;
    }

    void UpdateHandWorldFromDevice()
    {
#if ENABLE_INPUT_SYSTEM
        var m = Mouse.current;
        Vector2 scr = m != null ? m.position.ReadValue() : new Vector2(Screen.width * .5f, Screen.height * .5f);
#else
        Vector2 scr = (Vector2)Input.mousePosition;
#endif
        UpdateHandWorld(scr);
    }

    void UpdateHandWorld(Vector2 scr)
    {
        if (!cam) { _handWorld = transform.position; return; }
        Ray r = cam.ScreenPointToRay(scr);
        Vector3 n = _grabPlaneNormal.sqrMagnitude > 1e-6f ? _grabPlaneNormal : (cam ? cam.transform.forward : Vector3.forward);
        Vector3 p = _grabStartSocketPos;

        _handWorld = RayPlane(r, p, n, out float t) ? r.origin + r.direction * t
                                                    : (attachSocket ? attachSocket.position : transform.position);
    }

    static bool RayPlane(Ray r, Vector3 planePoint, Vector3 planeNormal, out float t)
    {
        float d = Vector3.Dot(planeNormal, r.direction);
        if (Mathf.Abs(d) > 1e-6f) { t = Vector3.Dot(planePoint - r.origin, planeNormal) / d; return t >= 0f; }
        t = 0f; return false;
    }

    // FX after detach
    void OnCollisionEnter(Collision c)
    {
        if (_attached) return;
        float now = Time.time;
        if (now - _lastCollisionBurstTime < collisionBurstCooldown) return;
        _lastCollisionBurstTime = now;

        if (c.contactCount > 0) { var cp = c.GetContact(0); onCollisionBurst.Invoke(cp.point, cp.normal); }
        else onCollisionBurst.Invoke(transform.position, Vector3.up);
    }

    void OnTriggerEnter(Collider other)
    {
        if (_attached) return;
        float now = Time.time;
        if (now - _lastCollisionBurstTime < collisionBurstCooldown) return;
        _lastCollisionBurstTime = now;

        Vector3 pos = other.ClosestPoint(transform.position);
        Vector3 normal = (transform.position - pos).sqrMagnitude > 1e-6f ? (transform.position - pos).normalized : Vector3.up;
        onCollisionBurst.Invoke(pos, normal);
    }
}
