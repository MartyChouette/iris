using UnityEngine;
using UnityEngine.EventSystems;
using System.Linq;

public class RaycastClickDebugger : MonoBehaviour
{
    public Camera cam;
    public bool respectUIBlock = true;
    public float maxDistance = 100f;
    public bool includeTriggers = true;

    // visual
    public float markerSize = 0.02f;
    public float markerSeconds = 1.0f;

    void Awake()
    {
        if (!cam) cam = Camera.main;
        Debug.Log($"[RayDbg] Camera={(cam ? cam.name : "<null>")}. " +
                  $"QueriesHitTriggers(ProjectSettings/Physics)={Physics.queriesHitTriggers}");
    }

    void Update()
    {
        if (!cam) return;
        if (!Input.GetMouseButtonDown(0)) return;

        if (respectUIBlock && EventSystem.current && EventSystem.current.IsPointerOverGameObject())
        {
            Debug.LogWarning("[RayDbg] Click ignored: pointer over UI.");
            return;
        }

        Ray r = cam.ScreenPointToRay(Input.mousePosition);
        var hits = Physics.RaycastAll(
            r,
            maxDistance,
            ~0, // Everything
            includeTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore
        ).OrderBy(h => h.distance).ToArray();

        Debug.DrawRay(r.origin, r.direction * 2f, Color.cyan, 0.25f);
        Debug.Log($"[RayDbg] Hits={hits.Length} (includeTriggers={includeTriggers})");

        if (hits.Length == 0) return;

        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            var lt = h.collider.GetComponentInParent<LeafTarget>();
            string layerName = LayerMask.LayerToName(h.collider.gameObject.layer);
            Debug.Log($"[RayDbg]  #{i} {h.collider.name}  layer={layerName}  dist={h.distance:F3}  LeafTarget={(lt != null)}");
        }

        // drop a small marker at first hit
        SpawnMarker(hits[0].point, Color.magenta);
    }

    void SpawnMarker(Vector3 p, Color c)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "__RayDbgMarker";
        go.hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor;
        go.GetComponent<Collider>().enabled = false;
        go.transform.position = p;
        go.transform.localScale = Vector3.one * markerSize;
        var mr = go.GetComponent<MeshRenderer>(); if (mr) mr.material.color = c;
        Destroy(go, markerSeconds);
    }
}
