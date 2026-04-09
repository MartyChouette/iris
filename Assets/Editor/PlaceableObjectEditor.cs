using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlaceableObject))]
[CanEditMultipleObjects]
public class PlaceableObjectEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Pose Capture", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Capture Home Rotation", GUILayout.Height(28)))
        {
            foreach (var t in targets)
            {
                var po = (PlaceableObject)t;
                Undo.RecordObject(po, "Capture Home Rotation");
                var so = new SerializedObject(po);
                so.FindProperty("_homeRotation").quaternionValue = po.transform.rotation;
                so.ApplyModifiedProperties();
                Debug.Log($"[PlaceableObject] {po.name} home rotation captured: {po.transform.eulerAngles}");
            }
        }

        if (GUILayout.Button("Capture Disheveled Rotation", GUILayout.Height(28)))
        {
            foreach (var t in targets)
            {
                var po = (PlaceableObject)t;
                Undo.RecordObject(po, "Capture Disheveled Rotation");
                var so = new SerializedObject(po);
                so.FindProperty("_disheveledRotation").quaternionValue = po.transform.rotation;
                so.ApplyModifiedProperties();
                Debug.Log($"[PlaceableObject] {po.name} disheveled rotation captured: {po.transform.eulerAngles}");
            }
        }

        EditorGUILayout.EndHorizontal();
    }
}
