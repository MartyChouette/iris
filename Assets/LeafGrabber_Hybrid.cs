// File: LeafGrabber_Hybrid.cs
// LMB: pick a LeafHybrid, feed it the hand world position while held.
// The leaf decides when it tears (distance + dwell) and handles sap/physics.

using UnityEngine;
using UnityEngine.InputSystem;

public class LeafGrabber_Hybrid : MonoBehaviour
{
    public Camera cam;
    public LayerMask leafMask = ~0;
    public float maxRayDistance = 60f;

    // optional: visual smoothing
    public float followLerp = 30f;   // 0=snap, 20-40=snappy smooth

    LeafHybrid _leaf;
    Vector3 _hand;       // smoothed hand
    Vector3 _vel;        // for SmoothDamp

    void Awake() { if (!cam) cam = Camera.main; }

    void Update()
    {
        var m = Mouse.current; if (m == null || cam == null) return;

        if (_leaf == null)
        {
            if (m.leftButton.wasPressedThisFrame && RaycastLeaf(m.position.ReadValue(), out _leaf, out var hit))
            {
                _hand = hit;
                _leaf.BeginGrab(hit);
            }
            return;
        }

        if (m.leftButton.isPressed)
        {
            Vector3 target = ScreenToWorldOnPlane(m.position.ReadValue(), _leaf, cam);
            float t = Mathf.Clamp01(1f - Mathf.Exp(-followLerp * Time.deltaTime));
            _hand = Vector3.Lerp(_hand, target, t);

            _leaf.SetHand(_hand);
        }
        else
        {
            _leaf.EndGrab();
            _leaf = null;
        }
    }

    bool RaycastLeaf(Vector2 scr, out LeafHybrid leaf, out Vector3 hitPoint)
    {
        leaf = null; hitPoint = default;
        Ray r = cam.ScreenPointToRay(scr);
        if (Physics.Raycast(r, out var h, maxRayDistance, leafMask, QueryTriggerInteraction.Collide))
        {
            leaf = h.collider.GetComponentInParent<LeafHybrid>();
            if (leaf != null) { hitPoint = h.point; return true; }
        }
        return false;
    }

    static Vector3 ScreenToWorldOnPlane(Vector2 scr, LeafHybrid leaf, Camera cam)
    {
        // Drag on plane through anchor-at-grab facing camera gives consistent "hand" depth.
        Plane p = new Plane(-cam.transform.forward, leaf ? leaf.transform.position : Vector3.zero);
        Ray r = cam.ScreenPointToRay(scr);
        return p.Raycast(r, out float d) ? r.GetPoint(d) : (leaf ? leaf.transform.position : r.origin);
    }
}
