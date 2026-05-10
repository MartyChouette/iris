using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor window that finds all renderers using duplicate materials and
/// batch-swaps them to a single target material.
/// Tools → Iris → Material Consolidator
/// </summary>
public class MaterialConsolidator : EditorWindow
{
    private Material _targetMaterial;
    private string _searchPattern = "metal";
    private Vector2 _scroll;
    private List<FoundEntry> _results = new();
    private bool _scanned;

    private struct FoundEntry
    {
        public Renderer renderer;
        public int slotIndex;
        public Material currentMat;
        public string objectPath;
        public bool selected;
    }

    [MenuItem("Tools/Iris/Material Consolidator")]
    public static void ShowWindow()
    {
        GetWindow<MaterialConsolidator>("Material Consolidator");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Material Consolidator", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        _targetMaterial = (Material)EditorGUILayout.ObjectField(
            "Target Material (keep)", _targetMaterial, typeof(Material), false);

        _searchPattern = EditorGUILayout.TextField("Search Pattern (name contains)", _searchPattern);

        EditorGUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Scan Scene", GUILayout.Height(28)))
            Scan();
        if (GUILayout.Button("Scan Project Assets", GUILayout.Height(28)))
            ScanAssets();
        EditorGUILayout.EndHorizontal();

        if (!_scanned || _results.Count == 0)
        {
            if (_scanned)
                EditorGUILayout.HelpBox("No matches found.", MessageType.Info);
            return;
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"Found {_results.Count} material slots matching \"{_searchPattern}\"");

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Select All"))
            for (int i = 0; i < _results.Count; i++)
            { var e = _results[i]; e.selected = true; _results[i] = e; }
        if (GUILayout.Button("Select None"))
            for (int i = 0; i < _results.Count; i++)
            { var e = _results[i]; e.selected = false; _results[i] = e; }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        for (int i = 0; i < _results.Count; i++)
        {
            var entry = _results[i];
            EditorGUILayout.BeginHorizontal();

            entry.selected = EditorGUILayout.Toggle(entry.selected, GUILayout.Width(20));

            bool isSame = entry.currentMat == _targetMaterial;
            GUI.color = isSame ? Color.gray : Color.white;

            if (GUILayout.Button(entry.renderer != null ? entry.renderer.name : "(null)",
                EditorStyles.miniButtonLeft, GUILayout.Width(200)))
            {
                if (entry.renderer != null)
                    Selection.activeGameObject = entry.renderer.gameObject;
            }

            EditorGUILayout.LabelField(
                $"[{entry.slotIndex}] {(entry.currentMat != null ? entry.currentMat.name : "NULL")}" +
                (isSame ? " (already target)" : ""),
                GUILayout.Width(300));

            GUI.color = Color.white;
            EditorGUILayout.LabelField(entry.objectPath);

            EditorGUILayout.EndHorizontal();
            _results[i] = entry;
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(8);

        int selectedCount = 0;
        for (int i = 0; i < _results.Count; i++)
            if (_results[i].selected && _results[i].currentMat != _targetMaterial) selectedCount++;

        GUI.enabled = _targetMaterial != null && selectedCount > 0;
        GUI.color = new Color(0.9f, 0.4f, 0.4f);
        if (GUILayout.Button($"Replace {selectedCount} selected → {(_targetMaterial != null ? _targetMaterial.name : "?")}",
            GUILayout.Height(32)))
        {
            if (EditorUtility.DisplayDialog("Replace Materials",
                $"Replace {selectedCount} material slots with '{_targetMaterial.name}'?\n\nThis modifies the scene. Undo is available.",
                "Replace", "Cancel"))
            {
                Replace();
            }
        }
        GUI.color = Color.white;
        GUI.enabled = true;
    }

    private void Scan()
    {
        _results.Clear();
        _scanned = true;

        if (string.IsNullOrEmpty(_searchPattern)) return;
        string pattern = _searchPattern.ToLowerInvariant();

        foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            var mats = renderer.sharedMaterials;
            for (int s = 0; s < mats.Length; s++)
            {
                if (mats[s] == null) continue;
                if (!mats[s].name.ToLowerInvariant().Contains(pattern)) continue;

                _results.Add(new FoundEntry
                {
                    renderer = renderer,
                    slotIndex = s,
                    currentMat = mats[s],
                    objectPath = GetPath(renderer.transform),
                    selected = mats[s] != _targetMaterial
                });
            }
        }

        // Sort by material name then object path
        _results.Sort((a, b) =>
        {
            int c = string.Compare(a.currentMat?.name, b.currentMat?.name);
            return c != 0 ? c : string.Compare(a.objectPath, b.objectPath);
        });

        Debug.Log($"[MaterialConsolidator] Found {_results.Count} slots matching '{_searchPattern}'");
    }

    private void ScanAssets()
    {
        _results.Clear();
        _scanned = true;

        if (string.IsNullOrEmpty(_searchPattern)) return;
        string pattern = _searchPattern.ToLowerInvariant();

        // Find all material assets matching the pattern
        var guids = AssetDatabase.FindAssets("t:Material");
        var duplicates = new List<Material>();

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith("Packages/") || path.StartsWith("Library/")) continue;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;
            if (!mat.name.ToLowerInvariant().Contains(pattern)) continue;
            if (mat == _targetMaterial) continue;
            duplicates.Add(mat);
        }

        EditorGUILayout.HelpBox($"Found {duplicates.Count} material assets matching pattern. Use 'Scan Scene' to find renderers using them.", MessageType.Info);

        // Now scan scene for renderers using any of these duplicates
        foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            var mats = renderer.sharedMaterials;
            for (int s = 0; s < mats.Length; s++)
            {
                if (mats[s] == null) continue;
                if (!duplicates.Contains(mats[s]) && !mats[s].name.ToLowerInvariant().Contains(pattern)) continue;

                _results.Add(new FoundEntry
                {
                    renderer = renderer,
                    slotIndex = s,
                    currentMat = mats[s],
                    objectPath = GetPath(renderer.transform),
                    selected = mats[s] != _targetMaterial
                });
            }
        }

        _results.Sort((a, b) =>
        {
            int c = string.Compare(a.currentMat?.name, b.currentMat?.name);
            return c != 0 ? c : string.Compare(a.objectPath, b.objectPath);
        });

        Debug.Log($"[MaterialConsolidator] Found {_results.Count} renderer slots using {duplicates.Count} duplicate materials");
    }

    private void Replace()
    {
        if (_targetMaterial == null) return;

        int replaced = 0;
        foreach (var entry in _results)
        {
            if (!entry.selected) continue;
            if (entry.renderer == null) continue;
            if (entry.currentMat == _targetMaterial) continue;

            Undo.RecordObject(entry.renderer, "Consolidate Material");

            var mats = entry.renderer.sharedMaterials;
            if (entry.slotIndex < mats.Length)
            {
                mats[entry.slotIndex] = _targetMaterial;
                entry.renderer.sharedMaterials = mats;
                EditorUtility.SetDirty(entry.renderer);
                replaced++;
            }
        }

        Debug.Log($"[MaterialConsolidator] Replaced {replaced} material slots with '{_targetMaterial.name}'");
        Scan(); // refresh
    }

    private static string GetPath(Transform t)
    {
        var parts = new List<string>();
        while (t != null) { parts.Add(t.name); t = t.parent; }
        parts.Reverse();
        return string.Join("/", parts);
    }
}
