using UnityEngine;
using UnityEngine.InputSystem;

public class LeafGrabber : MonoBehaviour
{
    public Camera cam;
    public LayerMask leafMask = ~0;

    [Header("Pluck feel")]
    public float spring = 40f;         // pull strength toward cursor
    public float damping = 10f;        // critically damped-ish
    public float maxDragSpeed = 4f;    // cap motion while held

    public float tearDistance = 0.11f; // how far past anchor to tear
    public float tearDwell = 0.12f;    // how long stretched before tearing

    Leaf _leaf;
    Vector3 _anchor;                   // world anchor on first grab
    float _over;

    void Awake() { if (!cam) cam = Camera.main; }

    void Update()
    {
        var m = Mouse.current; if (m == null || cam == null) return;

        if (_leaf == null)
        {
            if (m.leftButton.wasPressedThisFrame && RaycastLeaf(m.position.ReadValue(), out _leaf, out _anchor))
                _over = 0f;
            return;
        }

        if (m.leftButton.isPressed)
        {
            // target on plane through anchor facing camera
            Vector3 target = ScreenToWorldOnPlane(m.position.ReadValue(), _anchor, cam);

            // springy pull of the leaf’s transform (visual + attachment target)
            Transform t = _leaf.transform;
            Vector3 toTarget = target - t.position;

            // critically-damped spring step
            Vector3 vel = Vector3.zero;
            t.position = Vector3.SmoothDamp(t.position, target, ref vel, Mathf.Max(0.0001f, damping / spring), maxDragSpeed);

            // stretch test (how far from the original anchor)
            float stretch = Vector3.Distance(t.position, _anchor);
            if (stretch >= tearDistance)
            {
                _over += Time.deltaTime;
                if (_over >= tearDwell)
                {
                    _leaf.TearOff();
                    _leaf = null;
                }
            }
            else _over = 0f;
        }
        else
        {
            _leaf = null;
        }
    }

    bool RaycastLeaf(Vector2 scr, out Leaf leaf, out Vector3 hit)
    {
        leaf = null; hit = default;
        Ray r = cam.ScreenPointToRay(scr);
        if (Physics.Raycast(r, out var h, 50f, leafMask, QueryTriggerInteraction.Ignore))
        {
            leaf = h.collider.GetComponentInParent<Leaf>();
            if (leaf != null) { hit = h.point; return true; }
        }
        return false;
    }

    static Vector3 ScreenToWorldOnPlane(Vector2 scr, Vector3 planePoint, Camera cam)
    {
        Plane p = new Plane(-cam.transform.forward, planePoint);
        Ray r = cam.ScreenPointToRay(scr);
        return p.Raycast(r, out float d) ? r.GetPoint(d) : planePoint;
    }
}
