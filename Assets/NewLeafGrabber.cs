using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using System;

/// Drives pulling/tearing by cursor alone (external control).
public class NewLeafGrabber : MonoBehaviour
{
    [Header("Debug")]
    public bool debug = true;

    [Header("Setup")]
    public Camera cam;
    public LayerMask leafMask = ~0;
    public bool blockWhenPointerOverUI = true;

    [Header("Pluck feel")]
    public float spring = 40f;
    public float damping = 10f;
    public float maxDragSpeed = 4f;

    [Header("Tear")]
    public float tearDistance = 0.11f;
    public float tearDwell = 0.0f;

    // Internal
    LeafTarget _leaf;
    Vector3 _anchor;
    float _over;
    Vector3 _vel;

    void Awake()
    {
        if (!cam) cam = Camera.main;
        if (debug)
        {
            Debug.Log($"[Grabber] cam={(cam ? cam.name : "<null>")}  leafMask={LayerMaskToString(leafMask)}  UIBlock={blockWhenPointerOverUI}");
            if (!cam) Warn("No Camera assigned (will not be able to raycast).");
            if (blockWhenPointerOverUI && EventSystem.current == null)
                Warn("blockWhenPointerOverUI is ON but there's no EventSystem in the scene.");
        }
    }

    // fields (add one)
    Vector3 _hand;   // FIX: our own smoothed hand

    void Update()
    {
        var m = Mouse.current; if (m == null || cam == null) return;

        if (_leaf == null)
        {
            if (m.leftButton.wasPressedThisFrame)
            {
                if (blockWhenPointerOverUI && EventSystem.current && EventSystem.current.IsPointerOverGameObject())
                { if (debug) Log("Click ignored: pointer is over UI."); return; }

                if (RaycastLeaf(m.position.ReadValue(), out _leaf, out _anchor, out var hitInfo))
                {
                    _over = 0f; _vel = Vector3.zero;
                    _hand = _anchor;                      // FIX: initialize hand at anchor
                    _leaf.BeginExternalGrab(_anchor, cam.transform.forward);
                    if (debug) Log($"BeginExternalGrab -> leaf={NameOf(_leaf)}  hit={hitInfo.collider.name}  at {hitInfo.point:F3}");
                }
                else if (debug) Log("Click missed all LeafTargets.");
            }
            return;
        }

        if (m.leftButton.isPressed)
        {
            Vector3 target = ScreenToWorldOnPlane(m.position.ReadValue(), _anchor, cam);

            // FIX: spring our own hand toward the target (don’t read leaf pose)
            _hand = Vector3.SmoothDamp(_hand, target, ref _vel, Mathf.Max(0.0001f, damping / spring), maxDragSpeed);
            _leaf.SetExternalHand(_hand);

            // FIX: measure stretch from hand to anchor (socket), not leaf pose
            float stretch = Vector3.Distance(_hand, _anchor);
            if (debug) Log($"Dragging {NameOf(_leaf)}  hand={_hand:F3}  stretch={stretch:F3}/{tearDistance}");

            if (stretch >= tearDistance)
            {
                _over += Time.deltaTime;
                if (_over >= tearDwell)
                {
                    if (debug) Log($"TEAR {NameOf(_leaf)}  dwell={_over:F3}s");
                    _leaf.TearOff();
                    _leaf = null;
                }
            }
            else _over = 0f;
        }
        else
        {
            if (debug && _leaf != null) Log($"Release {NameOf(_leaf)}");
            _leaf?.EndExternalGrab();
            _leaf = null;
        }
    }

    bool RaycastLeaf(Vector2 scr, out LeafTarget leaf, out Vector3 anchor, out RaycastHit hitInfo)
    {
        leaf = null; anchor = default; hitInfo = default;
        Ray r = cam.ScreenPointToRay(scr);
        var hits = Physics.RaycastAll(r, 100f, leafMask, QueryTriggerInteraction.Collide);
        if (debug) { Debug.DrawRay(r.origin, r.direction * 2f, Color.cyan, 0.1f); Log($"RaycastAll hits={hits.Length} (mask={LayerMaskToString(leafMask)})"); }
        if (hits.Length == 0) return false;

        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            var lt = h.collider.GetComponentInParent<LeafTarget>();
            if (debug) Log($" hit {h.collider.name} (layer={LayerMask.LayerToName(h.collider.gameObject.layer)})  lt={(lt != null)}");
            if (lt == null) continue;

            // FIX: prefer the leaf’s attach socket as the anchor
            anchor = h.point;
            var comp = (lt as Component);
            var leafPB = comp ? comp.GetComponent<Leaf3D_PullBreak>() : null;
            //if (leafPB && leafPB.attachSocket) anchor = leafPB.attachSocket.position;

            leaf = lt; hitInfo = h;
            return true;
        }
        return false;
    }


    static Vector3 ScreenToWorldOnPlane(Vector2 scr, Vector3 planePoint, Camera cam)
    {
        Plane p = new Plane(-cam.transform.forward, planePoint);
        Ray r = cam.ScreenPointToRay(scr);
        return p.Raycast(r, out float d) ? r.GetPoint(d) : planePoint;
    }

    string LayerMaskToString(LayerMask m)
    {
        if (m.value == ~0) return "<Everything>";
        System.Text.StringBuilder sb = new();
        for (int i = 0; i < 32; i++) if ((m.value & (1 << i)) != 0) sb.Append(LayerMask.LayerToName(i)).Append("|");
        return sb.Length > 0 ? sb.ToString() : m.value.ToString();
    }

    void Log(string msg) { Debug.Log($"[Grabber] {msg}", this); }
    void Warn(string msg) { Debug.LogWarning($"[Grabber] {msg}", this); }
    string NameOf(LeafTarget lt) => (lt as Component) != null ? (lt as Component).name : "<null>";
}
