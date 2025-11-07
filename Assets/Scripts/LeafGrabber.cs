using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

/// Drives pulling/tearing by cursor alone.
/// Distance-only tear: set tearDwell = 0 for instant tear once threshold is crossed.
public class LeafGrabber : MonoBehaviour
{
    public Camera cam;
    public LayerMask leafMask = ~0;
    public bool blockWhenPointerOverUI = true;

    [Header("Pluck feel")]
    public float spring = 40f;          // higher = snap to cursor faster
    public float damping = 10f;         // approx critically damped when ~2*sqrt(k/m). Tune by feel.
    public float maxDragSpeed = 4f;     // cap while held

    [Header("Tear")]
    public float tearDistance = 0.11f;  // world meters from anchor
    public float tearDwell = 0.0f;      // seconds above distance before tear (0 = pure distance)

    // Internal
    LeafTarget _leaf;                   // any component that implements LeafTarget (below)
    Vector3 _anchor;                    // world anchor at grab begin
    float _over;                        // dwell timer
    Vector3 _vel;                       // SmoothDamp velocity (must persist between frames)

    void Awake() { if (!cam) cam = Camera.main; }

    void Update()
    {
        var m = Mouse.current; if (m == null || cam == null) return;

        if (_leaf == null)
        {
            if (m.leftButton.wasPressedThisFrame)
            {
                if (blockWhenPointerOverUI && EventSystem.current && EventSystem.current.IsPointerOverGameObject())
                    return;

                if (RaycastLeaf(m.position.ReadValue(), out _leaf, out _anchor))
                {
                    _over = 0f;
                    _vel = Vector3.zero;
                    _leaf.BeginExternalGrab(_anchor, cam.transform.forward);
                }
            }
            return;
        }

        if (m.leftButton.isPressed)
        {
            Vector3 target = ScreenToWorldOnPlane(m.position.ReadValue(), _anchor, cam);
            // Spring step toward target (visual stretch)
            Vector3 p = _leaf.GetPosition();
            p = Vector3.SmoothDamp(p, target, ref _vel, Mathf.Max(0.0001f, damping / spring), maxDragSpeed);
            _leaf.SetExternalHand(p);

            // Distance-only tear check
            float stretch = Vector3.Distance(p, _anchor);
            if (stretch >= tearDistance)
            {
                _over += Time.deltaTime;
                if (_over >= tearDwell)
                {
                    _leaf.TearOff();   // will handle sap burst etc.
                    _leaf = null;
                }
            }
            else _over = 0f;
        }
        else
        {
            // release
            _leaf?.EndExternalGrab();
            _leaf = null;
        }
    }

    bool RaycastLeaf(Vector2 scr, out LeafTarget leaf, out Vector3 hit)
    {
        leaf = null; hit = default;
        Ray r = cam.ScreenPointToRay(scr);
        if (Physics.Raycast(r, out var h, 50f, leafMask, QueryTriggerInteraction.Collide))
        {
            // Grab the closest LeafTarget in parents
            leaf = h.collider.GetComponentInParent<LeafTarget>();
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

/// Small interface so any leaf script can be driven by this grabber.
public interface LeafTarget
{
    void BeginExternalGrab(Vector3 anchorWorld, Vector3 planeNormal);
    void SetExternalHand(Vector3 handWorld);
    void EndExternalGrab();
    void TearOff();
    Vector3 GetPosition();
}
