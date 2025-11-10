using UnityEngine;

public class RMBDrag3D_Min : MonoBehaviour
{
    public Camera cam;
    public LayerMask mask = ~0;      // Everything (change to Leaf after it works)
    public float planeZBias = 0f;    // 0 = plane through hit point
    public bool includeTriggers = true;

    Transform _drag; float _dist; Vector3 _planeN; Vector3 _planePt;

    void Awake() { if (!cam) cam = Camera.main; }

    void Update()
    {
        if (cam == null) return;
        Ray r = cam.ScreenPointToRay(Input.mousePosition);

        if (_drag == null && Input.GetMouseButtonDown(1))
        {
            if (Physics.Raycast(r, out var h, 1000f, mask,
                    includeTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore))
            {
                _drag = h.collider.transform; // grabs whatever we hit
                _planeN = cam.transform.forward;
                _planePt = h.point + _planeN * planeZBias;
            }
        }
        else if (_drag != null && Input.GetMouseButton(1))
        {
            if (RayPlane(r, _planePt, _planeN, out float t))
                _drag.position = r.origin + r.direction * t;
        }
        else if (_drag != null && Input.GetMouseButtonUp(1))
        {
            _drag = null;
        }
    }

    static bool RayPlane(Ray ray, Vector3 p0, Vector3 n, out float t)
    {
        float d = Vector3.Dot(n, ray.direction);
        if (Mathf.Abs(d) < 1e-6f) { t = 0; return false; }
        t = Vector3.Dot(p0 - ray.origin, n) / d;
        return t >= 0f;
    }
}