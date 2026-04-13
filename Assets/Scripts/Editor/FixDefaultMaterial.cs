using UnityEngine;
using UnityEditor;

/// <summary>One-shot fix: resets the default URP Lit material color to white.</summary>
public class FixDefaultMaterial
{
    [MenuItem("Window/Iris/Fix Red Default Material")]
    public static void Fix()
    {
        // Find all materials in the project and reset any that are the default-lit
        var guids = AssetDatabase.FindAssets("t:Material", new[] { "Packages", "Assets" });
        int fixed_ = 0;

        // The built-in default material isn't an asset — create a fresh primitive
        // and reset its shared material directly.
        var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var rend = temp.GetComponent<Renderer>();
        if (rend != null && rend.sharedMaterial != null)
        {
            rend.sharedMaterial.color = Color.white;
            Debug.Log($"[FixDefaultMaterial] Reset '{rend.sharedMaterial.name}' to white.");
            fixed_++;
        }
        Object.DestroyImmediate(temp);

        // Also check for any Lit materials with suspicious red tint
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;
            if (mat.color == new Color(0.7f, 0.1f, 0.15f) || mat.color == new Color(0.4f, 0.05f, 0.1f, 0.9f))
            {
                mat.color = Color.white;
                EditorUtility.SetDirty(mat);
                Debug.Log($"[FixDefaultMaterial] Reset '{path}' to white.");
                fixed_++;
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[FixDefaultMaterial] Done. Fixed {fixed_} material(s).");
    }
}
