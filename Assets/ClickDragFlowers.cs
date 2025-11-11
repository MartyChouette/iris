using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class ClickDragFlowers : MonoBehaviour
{// Add near the top of the class:
    [Header("Debug HUD")]
    [Range(0f, 1f)] public float hudAnchorX = 0.02f;   // 0 = left, 1 = right
    [Range(0f, 1f)] public float hudAnchorY = 0.50f;   // 0.5 = vertical middle
    public int hudWidth = 820;
    public int hudLine = 18;
    public int hudPad = 8;

    [Header("Setup")]
    public Camera cam;
    [Tooltip("Only objects on these layers are draggable. Set to Everything to test.")]
    public LayerMask dragMask = ~0;
    [Tooltip("Count trigger colliders as hits.")]
    public bool includeTriggers = true;
    [Tooltip("Use a small sphere for hits to avoid missing thin meshes.")]
    public float rayRadius = 0.01f;
    [Tooltip("Ignore clicks when pointer is over UI.")]
    public bool blockWhenOverUI = true;

    [Header("Feel")]
    [Tooltip("How quickly the object chases the cursor. 0 = snap.")]
    public float followLerp = 30f;

    // runtime
    Transform _grabbed;
    Rigidbody _grabbedRb;
    Vector3 _grabLocalHitOffset; // world offset from hit point to object position at grab time
    Vector3 _planePoint;
    Vector3 _planeNormal;
    string _lastWhy = "";
    string _lastHitName = "";
    string _lastLayer = "";
    Vector3 _lastHitPoint;

    void Awake()
    {
        if (!cam) cam = Camera.main;
        if (!cam) _lastWhy = "No camera found (assign Cam on the script).";
    }

    void Update()
    {
        if (!cam) return;

        // ----- input snapshot (RMB) -----
        bool down, held, up;
        Vector2 screen;
#if ENABLE_INPUT_SYSTEM
        var m = Mouse.current; if (m == null) return;
        down = m.leftButton.wasPressedThisFrame;
        held = m.leftButton.isPressed;
        up = m.leftButton.wasReleasedThisFrame;
        screen = m.position.ReadValue();
#else
        down = Input.GetMouseButtonDown(0);
        held = Input.GetMouseButton(0);
        up   = Input.GetMouseButtonUp(0);
        screen = Input.mousePosition;
#endif

        if (down)
        {
            _lastWhy = "";
            if (blockWhenOverUI && EventSystem.current && EventSystem.current.IsPointerOverGameObject())
            {
                _lastWhy = "Blocked by UI under pointer.";
            }
            else
            {
                if (TryHit(screen, out var hit))
                {
                    _grabbed = hit.collider.transform;
                    _grabbedRb = _grabbed.GetComponent<Rigidbody>();
                    _lastHitName = _grabbed.name;
                    _lastLayer = LayerMask.LayerToName(hit.collider.gameObject.layer);
                    _lastHitPoint = hit.point;

                    // drag plane: through hit, facing camera
                    _planePoint = hit.point;
                    _planeNormal = cam.transform.forward;

                    // remember offset so we keep the same “grabbed spot”
                    _grabLocalHitOffset = _grabbed.position - hit.point;
                }
                else if (string.IsNullOrEmpty(_lastWhy))
                {
                    _lastWhy = "Ray missed all colliders in mask.";
                }
            }
        }

        if (_grabbed && held)
        {
            Vector3 target = ScreenToWorldOnPlane(screen, _planePoint, _planeNormal, cam) + _grabLocalHitOffset;
            float k = 1f - Mathf.Exp(-followLerp * Time.deltaTime);

            if (_grabbedRb && !_grabbedRb.isKinematic)
                _grabbedRb.MovePosition(Vector3.Lerp(_grabbed.position, target, k));
            else
                _grabbed.position = Vector3.Lerp(_grabbed.position, target, k);
        }

        if (up)
        {
            _grabbed = null;
            _grabbedRb = null;
        }
    }

    bool TryHit(Vector2 screen, out RaycastHit best)
    {
        best = default;
        Ray ray = cam.ScreenPointToRay(screen);
        Debug.DrawRay(ray.origin, ray.direction * 5f, Color.cyan, 0.1f);

        // choose trigger behavior
        QueryTriggerInteraction qti = includeTriggers ? QueryTriggerInteraction.Collide
                                                      : QueryTriggerInteraction.Ignore;

        RaycastHit[] hits;
        if (rayRadius > 0f)
            hits = Physics.SphereCastAll(ray, rayRadius, 500f, dragMask, qti);
        else
            hits = Physics.RaycastAll(ray, 500f, dragMask, qti);

        if (hits.Length == 0)
        {
            // second chance: Everything, for diagnosis
            var diag = Physics.RaycastAll(ray, 500f, ~0, qti);
            if (diag.Length > 0)
            {
                var closest = Closest(diag);
                _lastWhy = $"Hit '{closest.collider.name}' on layer '{LayerMask.LayerToName(closest.collider.gameObject.layer)}' but it is filtered out by dragMask.";
            }
            return false;
        }

        best = Closest(hits);
        return true;
    }

    static RaycastHit Closest(RaycastHit[] hits)
    {
        int bestI = 0;
        float bestD = hits[0].distance;
        for (int i = 1; i < hits.Length; i++)
            if (hits[i].distance < bestD) { bestD = hits[i].distance; bestI = i; }
        return hits[bestI];
    }

    static Vector3 ScreenToWorldOnPlane(Vector2 screen, Vector3 planePoint, Vector3 planeNormal, Camera cam)
    {
        Ray ray = cam.ScreenPointToRay(screen);
        float denom = Vector3.Dot(planeNormal, ray.direction);
        if (Mathf.Abs(denom) < 1e-6f) return planePoint;
        float t = Vector3.Dot(planePoint - ray.origin, planeNormal) / denom;
        return ray.origin + ray.direction * Mathf.Max(t, 0f);
    }

    // simple on-screen debug HUD
    // Replace your OnGUI() with this:
    //void OnGUI()
    //{
    //    int startX = Mathf.RoundToInt(Screen.width * hudAnchorX) + hudPad;
    //    int startY = Mathf.RoundToInt(Screen.height * hudAnchorY) + hudPad;

    //    int y = startY;
    //    int line = hudLine;

    //    // faint backdrop
    //    var rect = new Rect(startX - hudPad, startY - hudPad, hudWidth + hudPad * 2, line * 6 + hudPad * 2);
    //    Color old = GUI.color;
    //    GUI.color = new Color(0, 0, 0, 0.35f);
    //    GUI.DrawTexture(rect, Texture2D.whiteTexture);
    //    GUI.color = old;

    //    GUI.Label(new Rect(startX, y, hudWidth, line), $"[RMB_Drag3D] cam={(cam ? cam.name : "<null>")}"); y += line;
    //    GUI.Label(new Rect(startX, y, hudWidth, line), _grabbed ? $"Dragging: {_grabbed.name}" : "Dragging: <none>"); y += line;
    //    GUI.Label(new Rect(startX, y, hudWidth, line),
    //        string.IsNullOrEmpty(_lastHitName) ? "Last Hit: <none>"
    //            : $"Last Hit: {_lastHitName} (layer {_lastLayer}) @ {_lastHitPoint}");
    //    y += line;

    //    if (!string.IsNullOrEmpty(_lastWhy))
    //    {
    //        GUI.color = Color.yellow;
    //        GUI.Label(new Rect(startX, y, hudWidth, line), $"Why no grab: {_lastWhy}");
    //        GUI.color = old;
    //        y += line;
    //    }

    //    GUI.Label(new Rect(startX, y, hudWidth, line),
    //        $"Mask: {dragMask.value}  | includeTriggers={includeTriggers}  | rayRadius={rayRadius}");
    //}

}