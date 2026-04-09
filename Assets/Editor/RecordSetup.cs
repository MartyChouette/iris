using UnityEngine;
using UnityEditor;

/// <summary>
/// Editor utility: Window > Iris > Setup Records & Turntable.
/// Finds all "record N" GameObjects in the scene, adds PlaceableObject + RecordItem
/// components, assigns RecordDefinition SOs, and sets up a turntable with RecordSlot.
/// </summary>
public static class RecordSetup
{
    private const int PlaceablesLayer = 15;
    private const string SOFolder = "Assets/ScriptableObjects/RecordPlayer";

    [MenuItem("Window/Iris/Setup Records && Turntable")]
    public static void Run()
    {
        // ── Load RecordDefinition SOs ────────────────────────────────
        var soGuids = AssetDatabase.FindAssets("t:RecordDefinition", new[] { SOFolder });
        var definitions = new RecordDefinition[soGuids.Length];
        for (int i = 0; i < soGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(soGuids[i]);
            definitions[i] = AssetDatabase.LoadAssetAtPath<RecordDefinition>(path);
        }

        Debug.Log($"[RecordSetup] Found {definitions.Length} RecordDefinition SOs.");

        // ── Find record GameObjects ──────────────────────────────────
        int wired = 0;
        for (int n = 1; n <= 6; n++)
        {
            var go = GameObject.Find($"record {n}");
            if (go == null)
            {
                Debug.LogWarning($"[RecordSetup] 'record {n}' not found in scene.");
                continue;
            }

            SetupRecord(go, n, definitions);
            wired++;
        }

        // ── Find or create turntable ─────────────────────────────────
        SetupTurntable();

        Debug.Log($"[RecordSetup] Done — wired {wired} records.");
    }

    private static void SetupRecord(GameObject go, int index, RecordDefinition[] definitions)
    {
        // Remove static flags so it can move
        GameObjectUtility.SetStaticEditorFlags(go, 0);

        // Set layer
        go.layer = PlaceablesLayer;
        foreach (Transform child in go.transform)
            child.gameObject.layer = PlaceablesLayer;

        // Unparent from FBX hierarchy so it can be a standalone rigidbody
        go.transform.SetParent(null);

        // Add BoxCollider if missing (fitted to mesh bounds)
        if (go.GetComponent<Collider>() == null)
        {
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null) mf = go.GetComponentInChildren<MeshFilter>();

            var box = go.AddComponent<BoxCollider>();
            if (mf != null && mf.sharedMesh != null)
            {
                box.center = mf.sharedMesh.bounds.center;
                box.size = mf.sharedMesh.bounds.size;
            }
        }

        // Add Rigidbody if missing
        var rb = go.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.3f;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        // Add PlaceableObject if missing
        var placeable = go.GetComponent<PlaceableObject>();
        if (placeable == null)
            placeable = go.AddComponent<PlaceableObject>();

        // Configure PlaceableObject via SerializedObject
        var so = new SerializedObject(placeable);
        so.FindProperty("_useSpawnAsHome").boolValue = true;
        so.FindProperty("_itemCategory").enumValueIndex = (int)ItemCategory.Record;

        string recordName = index <= definitions.Length ? definitions[index - 1]?.title ?? $"Record {index}" : $"Record {index}";
        so.FindProperty("_itemDescription").stringValue = recordName;
        so.ApplyModifiedProperties();

        // Add RecordItem if missing
        var recordItem = go.GetComponent<RecordItem>();
        if (recordItem == null)
            recordItem = go.AddComponent<RecordItem>();

        // Assign RecordDefinition SO
        if (index - 1 < definitions.Length && definitions[index - 1] != null)
        {
            var riSO = new SerializedObject(recordItem);
            riSO.FindProperty("_definition").objectReferenceValue = definitions[index - 1];
            riSO.ApplyModifiedProperties();
        }

        // Add ReactableTag if missing
        var tag = go.GetComponent<ReactableTag>();
        if (tag == null)
            tag = go.AddComponent<ReactableTag>();

        // Add InteractableHighlight if missing
        if (go.GetComponent<InteractableHighlight>() == null)
            go.AddComponent<InteractableHighlight>();

        EditorUtility.SetDirty(go);
        Debug.Log($"[RecordSetup] Wired '{go.name}' → {(index - 1 < definitions.Length ? definitions[index - 1]?.title : "no SO")}");
    }

    private static void SetupTurntable()
    {
        // Look for existing turntable
        var existing = Object.FindFirstObjectByType<RecordSlot>();
        if (existing != null)
        {
            Debug.Log($"[RecordSetup] RecordSlot already exists on '{existing.name}'.");
            return;
        }

        // Create a simple turntable placeholder
        var turntable = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        turntable.name = "Turntable";
        turntable.transform.localScale = new Vector3(0.25f, 0.02f, 0.25f);

        // Position near the record shelf (try to find it)
        var shelf = GameObject.Find("record shelf mid");
        if (shelf != null)
        {
            turntable.transform.position = shelf.transform.position + Vector3.up * 0.15f + Vector3.right * 0.3f;
        }
        else
        {
            turntable.transform.position = new Vector3(0f, 0.8f, 0f);
        }

        // Set layer
        turntable.layer = PlaceablesLayer;

        // Remove default collider — we'll add a PlacementSurface trigger instead
        var defaultCol = turntable.GetComponent<Collider>();
        if (defaultCol != null) Object.DestroyImmediate(defaultCol);

        // Material
        var rend = turntable.GetComponent<Renderer>();
        if (rend != null)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (mat.shader == null) mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(0.15f, 0.12f, 0.1f); // Dark wood/vinyl
            rend.sharedMaterial = mat;
        }

        // Disc visual (spinning platter)
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = "DiscVisual";
        disc.transform.SetParent(turntable.transform, false);
        disc.transform.localScale = new Vector3(0.85f, 0.3f, 0.85f);
        disc.transform.localPosition = Vector3.up * 0.3f;

        var discCol = disc.GetComponent<Collider>();
        if (discCol != null) Object.DestroyImmediate(discCol);

        var discRend = disc.GetComponent<Renderer>();
        if (discRend != null)
        {
            Material discMat;
            if (rend != null && rend.sharedMaterial != null)
                discMat = new Material(rend.sharedMaterial);
            else
                discMat = new Material(Shader.Find("Standard"));
            discMat.color = new Color(0.05f, 0.05f, 0.05f); // Black vinyl
            discRend.sharedMaterial = discMat;
        }

        // Record snap point
        var snapPoint = new GameObject("RecordSnapPoint");
        snapPoint.transform.SetParent(turntable.transform, false);
        snapPoint.transform.localPosition = Vector3.up * 0.6f;

        // Add RecordSlot
        var slot = turntable.AddComponent<RecordSlot>();
        var slotSO = new SerializedObject(slot);
        slotSO.FindProperty("_discVisual").objectReferenceValue = disc.transform;
        slotSO.FindProperty("_discRenderer").objectReferenceValue = discRend;
        slotSO.FindProperty("_recordSnapPoint").objectReferenceValue = snapPoint.transform;
        slotSO.ApplyModifiedProperties();

        // Add ReactableTag for date reactions
        var tag = turntable.AddComponent<ReactableTag>();
        var tagSO = new SerializedObject(tag);
        tagSO.FindProperty("tags").arraySize = 2;
        tagSO.FindProperty("tags").GetArrayElementAtIndex(0).stringValue = "vinyl";
        tagSO.FindProperty("tags").GetArrayElementAtIndex(1).stringValue = "music";
        tagSO.FindProperty("displayName").stringValue = "Record Player";
        tagSO.FindProperty("isActive").boolValue = false; // Active only when playing
        tagSO.ApplyModifiedProperties();

        // Add PlacementSurface on turntable so ObjectGrabber detects it as a surface
        var surface = turntable.AddComponent<PlacementSurface>();
        var surfSO = new SerializedObject(surface);
        surfSO.FindProperty("localBounds").boundsValue = new Bounds(
            Vector3.up * 0.02f,
            new Vector3(0.2f, 0.05f, 0.2f));
        surfSO.FindProperty("surfaceLayerIndex").intValue = LayerMask.NameToLayer("Surfaces");
        surfSO.ApplyModifiedProperties();

        EditorUtility.SetDirty(turntable);
        Debug.Log("[RecordSetup] Created turntable with RecordSlot.");
    }
}
