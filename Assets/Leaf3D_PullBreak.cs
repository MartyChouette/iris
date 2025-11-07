// File: Leaf3D_PullBreak.cs
// Bespoke, hand-placed leaf that sits on a specific socket, breaks off by pull distance,
// bursts fluid on break, and bursts again on collisions. Works with Unity 6.
// Input: uses the new Input System if available, otherwise falls back to old Input.
// Placement: preserves your authoring pose relative to the attachSocket.
// Orientation: asymmetrical-friendly (front/back honored by your authored rotation).

using System;
using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
[DefaultExecutionOrder(32000)]
public class Leaf3D_PullBreak : MonoBehaviour
{
    [Header("Scene Refs")]
    [Tooltip("Camera used for picking and cursor-to-world projection. If null, uses Camera.main.")]
    public Camera cam;

    [Tooltip("Exact socket/empty on the stem/rope where this leaf is anchored while attached.")]
    public Transform attachSocket;

    [Tooltip("Optional: while attached, this is nudged when the leaf breaks (tiny recoil).")]
    public Rigidbody stemRigidbody;

    [Header("Placement / Authoring Pose")]
    [Tooltip("Captured at runtime from your current authored pose relative to attachSocket.")]
    public Vector3 localPosOffset;
    [Tooltip("Captured at runtime from your current authored pose relative to attachSocket.")]
    public Quaternion localRotOffset = Quaternion.identity;

    [Header("Pull-to-Break")]
    [Tooltip("Hold-click and pull beyond this distance (meters from socket) to detach.")]
    [Min(0.001f)] public float breakDistance = 0.12f;

    [Tooltip("If true, keep the leaf in hand after it breaks until the mouse/finger is released.")]
    public bool stayInHandAfterBreak = true;

    [Tooltip("Small launch impulse when the leaf breaks (world space).")]
    public Vector3 breakImpulse = new Vector3(0, 0.8f, 0);

    [Tooltip("Blocks grabbing if pointer over UI (EventSystem required).")]
    public bool blockWhenPointerOverUI = true;

    [Header("Picking")]
    [Tooltip("LayerMask to restrict clicking just this object/layer(s).")]
    public LayerMask pickMask = ~0;
    [Tooltip("Max ray distance when picking the leaf.")]
    public float pickRayDistance = 10f;

    [Header("Collision Burst (after detach)")]
    [Tooltip("Seconds between collision bursts to avoid spamming.")]
    public float collisionBurstCooldown = 0.15f;

    [Header("Fluid / FX Hooks")]
    [Tooltip("Invoked on BREAK: (position, normal). Hook your SapFxPool adapter here.")]
    public Vec3Vec3Event onBreakBurst = new Vec3Vec3Event();

    [Tooltip("Invoked on any post-detach collision: (position, normal).")]
    public Vec3Vec3Event onCollisionBurst = new Vec3Vec3Event();

    [Tooltip("Optional: fire this once right when grabbing begins (preview dribble).")]
    public bool burstOnGrab = false;

    [Serializable]
    public class Vec3Vec3Event : UnityEvent<Vector3, Vector3> { }

    // ───────── runtime ─────────
    Rigidbody _rb;
    Collider _col;
    bool _attached = true;
    bool _grabbed = false;         // grab state (can be attached or detached)
    float _lastCollisionBurstTime = -999f;

    // grab bookkeeping
    Vector3 _grabStartSocketPos;   // socket/world at grab begin
    Vector3 _grabPlaneNormal;      // camera forward at grab begin (for cursor-plane projection)
    Vector3 _handWorld;            // current projected world hand position

    void Awake()
    {
        if (!cam) cam = Camera.main;
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();

        if (_rb != null)
        {
            // While attached, we want the leaf to be kinematic and not collide-push things.
            _rb.isKinematic = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        // Capture your authored pose relative to attachSocket.
        if (attachSocket != null)
        {
            localPosOffset = attachSocket.InverseTransformPoint(transform.position);
            localRotOffset = Quaternion.Inverse(attachSocket.rotation) * transform.rotation;

            // Snap to socket on load (so tiny drift doesn’t accumulate).
            ApplyAttachedPose();
        }
    }

    void Update()
    {
        // 1) While attached and not grabbed, continuously enforce the authored socket pose
        if (_attached && !_grabbed && attachSocket != null)
            ApplyAttachedPose();

        // 2) Input: handle grab / hold / release
        HandleInput();

        // 3) While grabbed, update hand world position and check break condition if attached
        if (_grabbed)
        {
            UpdateHandWorld();

            if (_attached)
            {
                float dist = Vector3.Distance(attachSocket.position, _handWorld);
                if (dist >= breakDistance)
                    BreakOff();
            }
            else if (stayInHandAfterBreak)
            {
                // Keep leaf following hand while still grabbed (detached)
                PoseLeafAtHand();
            }
        }
    }

    void ApplyAttachedPose()
    {
        transform.position = attachSocket.TransformPoint(localPosOffset);
        transform.rotation = attachSocket.rotation * localRotOffset;
        if (_rb) { _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }
    }

    void PoseLeafAtHand()
    {
        // Pose the leaf at hand position, maintaining its current rotation (or orient gently toward camera)
        transform.position = _handWorld;

        // Optional: face a little toward camera to feel “in hand”.
        if (cam)
        {
            Vector3 toCam = cam.transform.position - transform.position;
            if (toCam.sqrMagnitude > 1e-4f)
            {
                Quaternion face = Quaternion.LookRotation(-cam.transform.forward, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, face, 0.25f);
            }
        }
    }

    void BreakOff()
    {
        _attached = false;

        // Re-enable physics
        if (_rb)
        {
            _rb.isKinematic = false;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.AddForce(breakImpulse, ForceMode.VelocityChange);
        }

        // Gentle recoil on stem
        if (stemRigidbody)
            stemRigidbody.AddForceAtPosition(-breakImpulse, attachSocket.position, ForceMode.VelocityChange);

        // Fluid burst at the socket (outward normal = away from camera or socket up)
        Vector3 pos = attachSocket ? attachSocket.position : transform.position;
        Vector3 normal = cam ? -cam.transform.forward : (attachSocket ? attachSocket.up : Vector3.up);
        onBreakBurst.Invoke(pos, normal);
    }

    void HandleInput()
    {
        bool down = false;
        bool held = false;
        bool up = false;
        Vector2 screenPos = default;

        // ── New Input System (if compiled in) ──
#if ENABLE_INPUT_SYSTEM
        var mouse = Mouse.current;
        if (mouse != null)
        {
            down = mouse.leftButton.wasPressedThisFrame;
            held = mouse.leftButton.isPressed;
            up = mouse.leftButton.wasReleasedThisFrame;
            screenPos = mouse.position.ReadValue();
        }
        else if (Touchscreen.current != null)
        {
            var t = GetPrimaryTouch();
            if (t.isValid)
            {
                down = t.began;
                up = t.ended || t.canceled;
                held = !up;
                screenPos = t.position;
            }
        }
#else
        // ── Legacy Input fallback ──
        down = Input.GetMouseButtonDown(0);
        held = Input.GetMouseButton(0);
        up   = Input.GetMouseButtonUp(0);
        screenPos = Input.mousePosition;
#endif

        // Guard: ignore UI when requested
        if (blockWhenPointerOverUI && UnityEngine.EventSystems.EventSystem.current != null)
        {
            if (UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            {
                // If pointer over UI, only process release to end any stray grab.
                if (up) _grabbed = false;
                return;
            }
        }

        if (down)
        {
            // Only start grab if we actually clicked THIS leaf (precise picking)
            if (HitThisLeaf(screenPos))
                BeginGrab(screenPos);
        }
        else if (held)
        {
            if (_grabbed)
                _ = true; // noop; Update() handles continuous logic
        }
        else if (up)
        {
            if (_grabbed)
                EndGrab();
        }
    }

    bool HitThisLeaf(Vector2 screenPos)
    {
        if (cam == null || _col == null) return false;

        Ray ray = cam.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out var hit, pickRayDistance, pickMask, QueryTriggerInteraction.Collide))
            return hit.collider == _col;

        return false;
    }

    void BeginGrab(Vector2 screenPos)
    {
        _grabbed = true;

        // Define a flat plane for hand movement at grab time (through socket, facing camera)
        _grabPlaneNormal = cam ? cam.transform.forward : Vector3.forward;
        _grabStartSocketPos = attachSocket ? attachSocket.position : transform.position;

        UpdateHandWorld(screenPos);

        if (burstOnGrab)
        {
            Vector3 pos = attachSocket ? attachSocket.position : transform.position;
            Vector3 normal = cam ? -cam.transform.forward : Vector3.up;
            onBreakBurst.Invoke(pos, normal); // tiny preview dribble if you hook it that way
        }

        // If still attached, we won't move the leaf yet; if already detached, we’ll start posing at hand in Update().
    }

    void EndGrab()
    {
        _grabbed = false;

        if (!_attached && _rb)
        {
            // Release into physics
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
    }

    // Overload: Update hand from current device state
    void UpdateHandWorld()
    {
        Vector2 screenPos;
#if ENABLE_INPUT_SYSTEM
        var mouse = Mouse.current;
        if (mouse != null)
            screenPos = mouse.position.ReadValue();
        else if (Touchscreen.current != null)
            screenPos = GetPrimaryTouch().position;
        else
            screenPos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
#else
        screenPos = Input.mousePosition;
#endif

        UpdateHandWorld(screenPos);
    }

    void UpdateHandWorld(Vector2 screenPos)
    {
        if (cam == null)
        {
            _handWorld = transform.position;
            return;
        }

        // Project the pointer ray onto a plane through the socket, facing the camera at grab time.
        Ray ray = cam.ScreenPointToRay(screenPos);
        Vector3 planeNormal = _grabPlaneNormal.sqrMagnitude > 1e-6f ? _grabPlaneNormal : (cam ? cam.transform.forward : Vector3.forward);
        Vector3 planePoint = _grabStartSocketPos;

        float t;
        if (RayPlane(ray, planePoint, planeNormal, out t))
            _handWorld = ray.origin + ray.direction * t;
        else
            _handWorld = attachSocket ? attachSocket.position : transform.position;
    }

    static bool RayPlane(Ray ray, Vector3 planePoint, Vector3 planeNormal, out float t)
    {
        float denom = Vector3.Dot(planeNormal, ray.direction);
        if (Mathf.Abs(denom) > 1e-6f)
        {
            t = Vector3.Dot(planePoint - ray.origin, planeNormal) / denom;
            return t >= 0f;
        }
        t = 0f;
        return false;
    }

#if ENABLE_INPUT_SYSTEM
    struct TouchSnapshot { public bool isValid; public bool began; public bool ended; public bool canceled; public Vector2 position; }

    TouchSnapshot GetPrimaryTouch()
    {
        var ts = Touchscreen.current;
        if (ts == null || ts.touches.Count == 0)
            return default;

        var t = ts.primaryTouch;
        return new TouchSnapshot
        {
            isValid = t.press.isPressed || t.press.wasPressedThisFrame || t.press.wasReleasedThisFrame,
            began = t.press.wasPressedThisFrame,
            ended = t.press.wasReleasedThisFrame,
            canceled = t.touchId.ReadValue() < 0,
            position = t.position.ReadValue()
        };
    }
#endif

    // ───────── Collision FX after detach ─────────
    void OnCollisionEnter(Collision c)
    {
        if (_attached) return;

        float now = Time.time;
        if (now - _lastCollisionBurstTime < collisionBurstCooldown) return;
        _lastCollisionBurstTime = now;

        // Use first contact for position/normal
        if (c.contactCount > 0)
        {
            var cp = c.GetContact(0);
            onCollisionBurst.Invoke(cp.point, cp.normal);
        }
        else
        {
            onCollisionBurst.Invoke(transform.position, Vector3.up);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (_attached) return;

        float now = Time.time;
        if (now - _lastCollisionBurstTime < collisionBurstCooldown) return;
        _lastCollisionBurstTime = now;

        // Approximate trigger impact location with closest point on bounds
        Vector3 pos = other.ClosestPoint(transform.position);
        Vector3 normal = (transform.position - pos).sqrMagnitude > 1e-6f ? (transform.position - pos).normalized : Vector3.up;
        onCollisionBurst.Invoke(pos, normal);
    }

    // ───────── Utilities / Editor ─────────
    [ContextMenu("Snap To Socket Now")]
    public void EditorSnapToSocket()
    {
        if (attachSocket == null) return;
        ApplyAttachedPose();
    }

    void OnDrawGizmosSelected()
    {
        if (attachSocket)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(attachSocket.position, 0.01f);
            Gizmos.DrawLine(attachSocket.position, attachSocket.position + attachSocket.up * 0.05f);
        }

        if (_attached)
        {
            Gizmos.color = new Color(1, 0.6f, 0.1f, 0.6f);
            if (attachSocket) Gizmos.DrawWireSphere(attachSocket.position, breakDistance);
        }
    }
}
