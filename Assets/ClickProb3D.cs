using UnityEngine;

public class ClickProbe3D : MonoBehaviour
{
    public Camera cam;
    public LayerMask testMask = ~0;        // Everything
    public bool includeTriggers = true;
    public KeyCode probeKey = KeyCode.Mouse1; // RMB

    string _last;
    void Awake() { if (!cam) cam = Camera.main; }

    void Update()
    {
        if (cam == null) return;

        if (Input.GetKeyDown(probeKey))
        {
            Ray r = cam.ScreenPointToRay(Input.mousePosition);

            // What your grabber sees (change to your grabber's mask to verify)
            if (!Physics.Raycast(r, out var hMasked, 100f, testMask,
                    includeTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore))
            {
                _last = $"[Probe] Masked ray MISS. Now listing all hits ignoring mask…";
            }
            else
            {
                _last = $"[Probe] Masked ray HIT {hMasked.collider.name} (layer={LayerMask.LayerToName(hMasked.collider.gameObject.layer)})";
            }

            // Now show everything under the cursor, no mask:
            var all = Physics.RaycastAll(r, 100f, ~0,
                includeTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore);
            System.Array.Sort(all, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < Mathf.Min(8, all.Length); i++)
            {
                var h = all[i];
                Debug.Log($"[Probe] #{i}: {h.collider.name}  layer={h.collider.gameObject.layer} ({LayerMask.LayerToName(h.collider.gameObject.layer)})  dist={h.distance:F2}");
            }

            if (all.Length == 0) Debug.Log("[Probe] No colliders under cursor at all.");
            else Debug.Log(_last);
        }
    }
}