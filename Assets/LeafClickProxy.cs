using UnityEngine;

[DisallowMultipleComponent]
public class LeafClickProxy : MonoBehaviour
{
    [Tooltip("Set to a layer that your grabber mask includes (e.g. 'Leaf').")]
    public string layerName = "Leaf";
    public Vector3 localCenter = Vector3.zero;
    public Vector3 localSize = new Vector3(0.03f, 0.03f, 0.01f);

    void Reset() { Ensure(); }
    void OnValidate() { if (!Application.isPlaying) Ensure(); }

    void Ensure()
    {
        var t = transform.Find("__PickProxy");
        if (!t)
        {
            var go = new GameObject("__PickProxy");
            go.transform.SetParent(transform, false);
            t = go.transform;
        }
        t.localPosition = localCenter;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;

        var col = t.GetComponent<BoxCollider>();
        if (!col) col = t.gameObject.AddComponent<BoxCollider>();
        col.isTrigger = false;
        col.size = localSize;

        if (!string.IsNullOrEmpty(layerName))
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer == -1)
                Debug.LogWarning($"[LeafClickProxy] Layer '{layerName}' does not exist. Create it and reassign.");
            else
                t.gameObject.layer = layer;
        }

        Debug.Log($"[LeafClickProxy] Ensured non-trigger BoxCollider on {name} (layer='{layerName}').");
    }
}