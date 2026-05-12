using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PerfumeBottle))]
public class PerfumeBottleEditor : Editor
{
    private SerializedProperty _definition;
    private SerializedProperty _sprayLayers;

    private void OnEnable()
    {
        _definition = serializedObject.FindProperty("definition");
        _sprayLayers = serializedObject.FindProperty("sprayLayers");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(_definition);
        EditorGUILayout.Space(8);

        EditorGUILayout.LabelField("Spray Layers", EditorStyles.boldLabel);

        for (int i = 0; i < _sprayLayers.arraySize; i++)
        {
            var layer = _sprayLayers.GetArrayElementAtIndex(i);

            EditorGUILayout.BeginVertical("box");

            // Header row with label + remove button
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Layer {i}", EditorStyles.boldLabel);
            if (GUILayout.Button("\u2212", GUILayout.Width(24), GUILayout.Height(18)))
            {
                _sprayLayers.DeleteArrayElementAtIndex(i);
                serializedObject.ApplyModifiedProperties();
                return;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel++;

            EditorGUILayout.PropertyField(layer.FindPropertyRelative("particles"));
            EditorGUILayout.PropertyField(layer.FindPropertyRelative("delay"),
                new GUIContent("Delay (sec)", "Time offset from spray start."));

            var duration = layer.FindPropertyRelative("duration");
            EditorGUILayout.PropertyField(duration,
                new GUIContent("Duration", "0 = instant burst. > 0 = sustained emission."));

            if (duration.floatValue <= 0f)
            {
                // Burst mode
                EditorGUILayout.PropertyField(layer.FindPropertyRelative("burstCount"),
                    new GUIContent("Burst Count"));
            }
            else
            {
                // Sustained mode
                EditorGUILayout.PropertyField(layer.FindPropertyRelative("emissionRate"),
                    new GUIContent("Emission Rate", "Particles/sec at peak."));
                EditorGUILayout.PropertyField(layer.FindPropertyRelative("fadeIn"),
                    new GUIContent("Fade In (sec)"));
                EditorGUILayout.PropertyField(layer.FindPropertyRelative("fadeOut"),
                    new GUIContent("Fade Out (sec)"));
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        // Add layer button
        if (GUILayout.Button("+ Add Spray Layer", GUILayout.Height(26)))
        {
            _sprayLayers.InsertArrayElementAtIndex(_sprayLayers.arraySize);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
