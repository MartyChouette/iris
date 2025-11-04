// ClickDragFlowers.cs
// Click LMB to pick any collider on layer "Flower" and drag it around.
// Works with meshes or prefabs; uses a camera-parallel drag plane at grab depth.

using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public class ClickDragFlowers : MonoBehaviour
{
    [Header("Setup")]
    public Camera cam;                          // assign your main camera
    [Tooltip("Only objects on this Unity layer are draggable.")]
    public string draggableLayerName = "Flower";
    [Tooltip("Ignore clicks when pointer is over UI.")]
    public bool blockWhenPointerOverUI = true;

    [Header("Feel")]
    [Tooltip("Optional smoothing for motion while dragging.")]
    [Range(0f, 60f)] public float moveLerp = 30f;

    // runtime
    int _layerMask;
    Transform _grabbed;
    Rigidbody _grabbedRB;
    bool _madeKinematic;
    float _grabDepth;               // signed distance along camera forward
    Vector3 _grabOffsetWS;          // world-space offset from cursor plane hit to object pivot
    Vector3 _targetPos;             // for smoothing

    void Awake()
    {
        if (cam == null) cam = Camera.main;
        _layerMask = LayerMask.GetMask(draggableLayerName);
        if (_layerMask == 0)
        {
            Debug.LogWarning($"ClickDragFlowers: Layer \"{draggableLayerName}\" not found. " +
                             $"Create it and assign your objects to that layer.");
        }
    }

    void Update()
    {
        // start drag
        if (Input.GetMouseButtonDown(0))
            TryBeginGrab();

        // update drag
        if (_grabbed != null)
            UpdateGrab();

        // end drag
        if (Input.GetMouseButtonUp(0))
            EndGrab();
    }

    void TryBeginGrab()
    {
        if (blockWhenPointerOverUI && EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject())
            return;

        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out var hit, float.PositiveInfinity, _layerMask, QueryTriggerInteraction.Ignore))
        {
            // choose the rigidbody root if available so we move the whole thing
            _grabbedRB = hit.rigidbody;
            _grabbed = _grabbedRB ? _grabbedRB.transform : hit.transform;

            // camera-parallel plane depth where we grabbed
            _grabDepth = Vector3.Dot(cam.transform.forward, _grabbed.position - cam.transform.position);

            // compute point on that plane under the cursor
            if (TryRayToCameraDepth(ray, _grabDepth, out Vector3 cursorAtDepth))
            {
                _grabOffsetWS = _grabbed.position - cursorAtDepth;
                _targetPos = _grabbed.position;

                // make RB kinematic while dragging (so we can place it precisely)
                if (_grabbedRB != null)
                {
                    _madeKinematic = !_grabbedRB.isKinematic;
                    _grabbedRB.isKinematic = true;
                }
            }
            else
            {
                // failed to compute a stable plane hit (degenerate case)
                _grabbed = null;
                _grabbedRB = null;
            }
        }
    }

    void UpdateGrab()
    {
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!TryRayToCameraDepth(ray, _grabDepth, out Vector3 cursorAtDepth))
            return;

        Vector3 desired = cursorAtDepth + _grabOffsetWS;

        // smooth move (set moveLerp=0 for perfectly crisp motion)
        float t = moveLerp > 0f ? 1f - Mathf.Exp(-moveLerp * Time.deltaTime) : 1f;
        _targetPos = Vector3.Lerp(_targetPos, desired, t);

        if (_grabbedRB != null)
        {
            // if you prefer physics interpolation:
            _grabbedRB.MovePosition(_targetPos);
        }
        else
        {
            _grabbed.position = _targetPos;
        }
    }

    void EndGrab()
    {
        if (_grabbedRB != null && _madeKinematic)
        {
            _grabbedRB.isKinematic = false;
        }
        _grabbed = null;
        _grabbedRB = null;
        _madeKinematic = false;
    }

    /// <summary>
    /// Intersect a screen ray with a plane parallel to the camera plane,
    /// at a signed distance (along cam.forward) from the camera position.
    /// </summary>
    static bool TryRayToCameraDepth(Ray ray, float camForwardDepth, out Vector3 point)
    {
        // We need t such that dot(camFwd, ray.origin + t*ray.dir - camPos) == camForwardDepth.
        // Let camFwd be ray2? We don't have camera here, so derive from ray: we need an external camFwd.
        // Instead: reconstruct from current main camera each call… but to keep this static, we pass via Camera.main.
        var cam = Camera.main;
        if (cam == null) { point = default; return false; }

        Vector3 camFwd = cam.transform.forward;
        Vector3 camPos = cam.transform.position;

        float denom = Vector3.Dot(camFwd, ray.direction);
        if (Mathf.Abs(denom) < 1e-4f)
        {
            point = default;
            return false;
        }

        float numer = camForwardDepth - Vector3.Dot(camFwd, ray.origin - camPos);
        float t = numer / denom;
        if (t < 0f)
        {
            point = default;
            return false;
        }

        point = ray.origin + ray.direction * t;
        return true;
    }
}
