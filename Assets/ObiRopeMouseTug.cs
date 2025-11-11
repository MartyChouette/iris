// File: ObiRopeMouseTug.cs
// Click & drag the nearest Obi rope particle. Release to let go.
// Unity 6 (Input System) + Obi 7.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Obi;

[DefaultExecutionOrder(32000)]
public class ObiRopeMouseTug : MonoBehaviour
{
    [Header("Scene Refs")]
    public Camera cam;
    public ObiRope rope;

    [Header("Input (Unity 6 Input System)")]
    [Tooltip("Vector2 action bound to <Pointer>/position. If null, falls back to Mouse.current.")]
    public InputActionReference pointerPositionAction;
    [Tooltip("Button action bound to <Pointer>/press (or <Mouse>/leftButton).")]
    public InputActionReference pointerPressAction;

    [Header("Picking")]
    [Tooltip("Max world distance from the mouse ray to a particle to allow picking (meters).")]
    public float pickRadius = 0.06f;

    [Tooltip("Optional: raycast mask for the plane we drag on (if none, we drag on a camera-facing plane at the pick point).")]
    public LayerMask dragPlaneMask = 0;   // 0 = use camera-facing plane at pick point

    [Header("Attachment Feel")]
    [Tooltip("Dynamic = 2-way (rope reacts). Static = 1-way (moves particle to handle).")]
    public ObiParticleAttachment.AttachmentType attachmentType = ObiParticleAttachment.AttachmentType.Dynamic;

    [Tooltip("Lower = stiffer (0 = perfectly stiff).")]
    [Min(0)] public float compliance = 0.0001f;

    [Tooltip("If true, the pin enforces orientation (rarely needed for rope points).")]
    public bool constrainOrientation = false;

    [Tooltip("Optional max distance before the attachment snaps (0 = unlimited).")]
    [Min(0)] public float breakDistance = 0f;

    // --- runtime ---
    Transform _handle;                         // invisible target we move with the mouse
    ObiParticleAttachment _attachment;         // temporary pin
    ObiParticleGroup _runtimeGroup;            // holds the selected actor index
    int _pickedActorIndex = -1;
    Vector3 _pickWorld;                        // where we picked on press
    Plane _dragPlane;                          // plane used to convert mouse->world while dragging

    void Awake()
    {
        if (!cam) cam = Camera.main;
        if (!rope) rope = GetComponentInParent<ObiRope>();
    }

    void OnEnable()
    {
        if (pointerPositionAction) pointerPositionAction.action.Enable();
        if (pointerPressAction) pointerPressAction.action.Enable();
    }

    void OnDisable()
    {
        if (pointerPositionAction) pointerPositionAction.action.Disable();
        if (pointerPressAction) pointerPressAction.action.Disable();
        Release(); // cleanup if disabled mid-drag
    }

    void Update()
    {
        if (cam == null || rope == null || rope.solver == null) return;

        Vector2 scr = ReadPointerPosition();
        bool pressed = ReadPointerPressed();

        if (pressed && _attachment == null)
        {
            // Try to pick a particle near the mouse ray
            if (TryPick(scr, out _pickedActorIndex, out _pickWorld))
            {
                // Create (or reuse) a handle
                if (_handle == null)
                {
                    var go = new GameObject("ObiMouseHandle");
                    go.hideFlags = HideFlags.HideInHierarchy;
                    _handle = go.transform;
                }
                _handle.position = _pickWorld;
                _handle.rotation = Quaternion.identity;

                // Build a drag plane:
                if (dragPlaneMask == 0)
                {
                    // Camera-facing plane through the pick point
                    _dragPlane = new Plane(-cam.transform.forward, _pickWorld);
                }
                else
                {
                    // If you prefer, you can raycast to some surface and drag on that.
                    _dragPlane = new Plane(-cam.transform.forward, _pickWorld);
                }

                // Build runtime particle group for the single picked actor index:
                _runtimeGroup = ScriptableObject.CreateInstance<ObiParticleGroup>();
                _runtimeGroup.particleIndices = new List<int>(1) { _pickedActorIndex };

                // NEW: add the attachment to the rope GameObject so it auto-binds to that actor
                _attachment = rope.gameObject.AddComponent<ObiParticleAttachment>();

                // keep the rest:
                _attachment.particleGroup = _runtimeGroup;
                _attachment.target = _handle;
                _attachment.attachmentType = attachmentType;
                //_attachment.stiffness = 1f;                 // harmless legacy field
                _attachment.compliance = compliance;        // Obi 7 softness
                _attachment.constrainOrientation = constrainOrientation;
                _attachment.breakThreshold = breakDistance;
                _attachment.enabled = true;

                // and after creating/changing attachments, mark pins dirty:
                rope.SetConstraintsDirty(Oni.ConstraintType.Pin);
            }
        }
        else if (pressed && _attachment != null)
        {
            // Drag: move the handle to the pointer’s world position on the plane
            if (ScreenToWorldOnPlane(scr, _dragPlane, out var world))
                _handle.position = world;
        }
        else if (!pressed && _attachment != null)
        {
            // Release on mouse up
            Release();
        }
    }

    // ---------------- helpers ----------------

    Vector2 ReadPointerPosition()
    {
        if (pointerPositionAction)
            return pointerPositionAction.action.ReadValue<Vector2>();
        return Mouse.current != null ? Mouse.current.position.ReadValue()
                                     : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
    }

    bool ReadPointerPressed()
    {
        if (pointerPressAction)
            return pointerPressAction.action.ReadValue<float>() > 0.5f;
        return Mouse.current != null && Mouse.current.leftButton.isPressed;
    }

    bool ScreenToWorldOnPlane(Vector2 scr, Plane plane, out Vector3 world)
    {
        Ray r = cam.ScreenPointToRay(scr);
        if (plane.Raycast(r, out float d))
        {
            world = r.GetPoint(d);
            return true;
        }
        world = default;
        return false;
    }

    bool TryPick(Vector2 scr, out int actorIndex, out Vector3 pickWorld)
    {
        actorIndex = -1;
        pickWorld = default;

        // Build the mouse ray:
        Ray ray = cam.ScreenPointToRay(scr);

        // Scan active particles and choose the nearest to the ray within pickRadius
        var s = rope.solver;
        var si = rope.solverIndices;
        var rp = s.renderablePositions;

        int n = rope.activeParticleCount;
        float best = pickRadius * pickRadius;

        for (int i = 0; i < n; i++)
        {
            int svi = si[i];
            if (svi < 0 || svi >= rp.count) continue;

            Vector3 pWorld = s.transform.TransformPoint((Vector3)rp[svi]);

            float d2 = SqrDistancePointToRay(pWorld, ray.origin, ray.direction);
            if (d2 <= best)
            {
                best = d2;
                actorIndex = i;
                pickWorld = ClosestPointOnRay(pWorld, ray.origin, ray.direction);
            }
        }

        return actorIndex >= 0;
    }

    static float SqrDistancePointToRay(Vector3 point, Vector3 ro, Vector3 rd)
    {
        Vector3 v = point - ro;
        float t = Mathf.Max(0f, Vector3.Dot(v, rd.normalized));
        Vector3 closest = ro + rd.normalized * t;
        return (point - closest).sqrMagnitude;
    }

    static Vector3 ClosestPointOnRay(Vector3 point, Vector3 ro, Vector3 rd)
    {
        Vector3 v = point - ro;
        float t = Mathf.Max(0f, Vector3.Dot(v, rd.normalized));
        return ro + rd.normalized * t;
    }

    void Release()
    {
        if (_attachment != null)
        {
            _attachment.enabled = false;
            Destroy(_attachment);
            _attachment = null;
            rope.SetConstraintsDirty(Oni.ConstraintType.Pin);
        }
        if (_runtimeGroup != null)
        {
            Destroy(_runtimeGroup);
            _runtimeGroup = null;
        }
        _pickedActorIndex = -1;
    }


#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (_attachment != null && _handle != null)
        {
            Gizmos.color = new Color(0f, 0.8f, 1f, 0.8f);
            Gizmos.DrawWireSphere(_handle.position, 0.015f);
        }
    }
#endif
}
