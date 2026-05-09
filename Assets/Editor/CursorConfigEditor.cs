using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom editor for CursorConfig — shows each cursor texture with a
/// draggable crosshair to set the hotspot visually.
/// </summary>
[CustomEditor(typeof(CursorConfig))]
public class CursorConfigEditor : Editor
{
    private const float PreviewSize = 128f;
    private const float CrosshairSize = 10f;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawEntry("Default", "defaultCursor");
        DrawEntry("Interact", "interact");
        DrawEntry("Watering", "watering");
        DrawEntry("Fridge", "fridge");
        DrawEntry("Phone", "phone");
        DrawEntry("Drawer", "drawer");
        DrawEntry("Drink", "drink");
        DrawEntry("Sponge", "sponge");
        DrawEntry("Grab", "grab");
        DrawEntry("Scissors", "scissors");
        DrawEntry("Swatter", "swatter");
        DrawEntry("Light", "light");

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawEntry(string label, string propName)
    {
        var prop = serializedObject.FindProperty(propName);
        var texProp = prop.FindPropertyRelative("texture");
        var hsProp = prop.FindPropertyRelative("hotspot");

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

        EditorGUILayout.PropertyField(texProp, new GUIContent("Texture"));

        var tex = texProp.objectReferenceValue as Texture2D;
        if (tex == null)
        {
            EditorGUILayout.PropertyField(hsProp, new GUIContent("Hotspot"));
            return;
        }

        // Draw preview with crosshair
        var previewRect = GUILayoutUtility.GetRect(PreviewSize, PreviewSize, GUILayout.ExpandWidth(false));

        // Checkerboard background
        EditorGUI.DrawRect(previewRect, new Color(0.2f, 0.2f, 0.2f));
        for (int x = 0; x < PreviewSize; x += 8)
        {
            for (int y = 0; y < PreviewSize; y += 8)
            {
                if ((x / 8 + y / 8) % 2 == 0)
                {
                    EditorGUI.DrawRect(new Rect(previewRect.x + x, previewRect.y + y, 8, 8),
                        new Color(0.3f, 0.3f, 0.3f));
                }
            }
        }

        GUI.DrawTexture(previewRect, tex, ScaleMode.ScaleToFit, true);

        // Map hotspot to preview position
        Vector2 hotspot = hsProp.vector2Value;
        float scaleX = PreviewSize / tex.width;
        float scaleY = PreviewSize / tex.height;
        float scale = Mathf.Min(scaleX, scaleY);
        float drawW = tex.width * scale;
        float drawH = tex.height * scale;
        float offsetX = previewRect.x + (PreviewSize - drawW) * 0.5f;
        float offsetY = previewRect.y + (PreviewSize - drawH) * 0.5f;

        Vector2 crossPos = new Vector2(
            offsetX + hotspot.x * scale,
            offsetY + hotspot.y * scale
        );

        // Draw crosshair
        Handles.color = Color.red;
        Handles.DrawLine(
            new Vector3(crossPos.x - CrosshairSize, crossPos.y, 0),
            new Vector3(crossPos.x + CrosshairSize, crossPos.y, 0));
        Handles.DrawLine(
            new Vector3(crossPos.x, crossPos.y - CrosshairSize, 0),
            new Vector3(crossPos.x, crossPos.y + CrosshairSize, 0));

        // Draw circle
        Handles.DrawWireDisc(new Vector3(crossPos.x, crossPos.y, 0), Vector3.forward, 4f);

        // Handle click/drag to set hotspot
        var e = Event.current;
        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag)
            && e.button == 0 && previewRect.Contains(e.mousePosition))
        {
            float px = (e.mousePosition.x - offsetX) / scale;
            float py = (e.mousePosition.y - offsetY) / scale;
            px = Mathf.Clamp(px, 0, tex.width);
            py = Mathf.Clamp(py, 0, tex.height);
            hsProp.vector2Value = new Vector2(Mathf.Round(px), Mathf.Round(py));
            e.Use();
            Repaint();
        }

        // Show numeric fields too
        EditorGUILayout.PropertyField(hsProp, new GUIContent("Hotspot (px)"));
    }
}
