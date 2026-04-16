using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DayIntroSequence))]
public class DayIntroSequenceEditor : Editor
{
    private bool _isPreviewing;
    private float _previewT;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var seq = (DayIntroSequence)target;
        var wpProp = serializedObject.FindProperty("_waypoints");

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("Track Shot Capture", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Position the Scene View camera, then click Capture to save it as a waypoint. " +
            "Waypoints are traversed in order at constant speed using Catmull-Rom smoothing.",
            MessageType.Info);

        // Show path info
        if (wpProp.arraySize >= 2)
        {
            float totalDist = 0f;
            for (int i = 1; i < wpProp.arraySize; i++)
            {
                var a = wpProp.GetArrayElementAtIndex(i - 1).objectReferenceValue as Transform;
                var b = wpProp.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                if (a != null && b != null)
                    totalDist += Vector3.Distance(a.position, b.position);
            }
            var speedProp = serializedObject.FindProperty("_trackSpeed");
            float speed = speedProp != null ? speedProp.floatValue : 2f;
            float duration = speed > 0.01f ? totalDist / speed : 0f;

            EditorGUILayout.LabelField($"Path: {wpProp.arraySize} waypoints, " +
                $"{totalDist:F1}m, ~{duration:F1}s at {speed:F1} m/s");
        }

        // Capture button
        EditorGUILayout.Space(4);
        if (GUILayout.Button("Capture Scene View → New Waypoint", GUILayout.Height(28)))
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv != null)
            {
                Undo.RecordObject(seq, "Add Intro Waypoint");

                // Create waypoint transform as child
                int idx = wpProp.arraySize;
                var wpGO = new GameObject($"IntroWP_{idx}");
                Undo.RegisterCreatedObjectUndo(wpGO, "Add Intro Waypoint");
                wpGO.transform.SetParent(seq.transform, false);
                wpGO.transform.position = sv.camera.transform.position;
                wpGO.transform.rotation = sv.camera.transform.rotation;

                // Add to array
                serializedObject.Update();
                wpProp.arraySize = idx + 1;
                wpProp.GetArrayElementAtIndex(idx).objectReferenceValue = wpGO.transform;
                serializedObject.ApplyModifiedProperties();

                Debug.Log($"[DayIntroSequence] Captured WP {idx}: pos={wpGO.transform.position}, rot={wpGO.transform.eulerAngles}");
            }
            else
            {
                Debug.LogWarning("[DayIntroSequence] No active Scene View.");
            }
        }

        // Per-waypoint row: Recapture / Preview / Delete
        if (wpProp.arraySize > 0)
        {
            EditorGUILayout.Space(4);
            for (int i = 0; i < wpProp.arraySize; i++)
            {
                var wpRef = wpProp.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                string label = wpRef != null ? wpRef.name : "(missing)";

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"  {i}: {label}", GUILayout.Width(160));

                if (wpRef != null && GUILayout.Button("Recapture", GUILayout.Width(75), GUILayout.Height(20)))
                {
                    var sv = SceneView.lastActiveSceneView;
                    if (sv != null)
                    {
                        Undo.RecordObject(wpRef, $"Recapture WP {i}");
                        wpRef.position = sv.camera.transform.position;
                        wpRef.rotation = sv.camera.transform.rotation;
                        EditorUtility.SetDirty(wpRef);
                        Debug.Log($"[DayIntroSequence] Recaptured WP {i}: pos={wpRef.position}");
                    }
                }

                if (wpRef != null && GUILayout.Button("Preview", GUILayout.Width(60), GUILayout.Height(20)))
                {
                    var sv = SceneView.lastActiveSceneView;
                    if (sv != null)
                    {
                        sv.pivot = wpRef.position + wpRef.rotation * Vector3.forward * 2f;
                        sv.rotation = wpRef.rotation;
                        sv.orthographic = false;
                        sv.Repaint();
                    }
                }

                if (GUILayout.Button("X", GUILayout.Width(24), GUILayout.Height(20)))
                {
                    if (wpRef != null)
                        Undo.DestroyObjectImmediate(wpRef.gameObject);
                    wpProp.DeleteArrayElementAtIndex(i);
                    if (i < wpProp.arraySize && wpProp.GetArrayElementAtIndex(i).objectReferenceValue == null)
                        wpProp.DeleteArrayElementAtIndex(i);
                    serializedObject.ApplyModifiedProperties();
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        // Clear all
        EditorGUILayout.Space(4);
        if (wpProp.arraySize > 0 && GUILayout.Button("Clear All Waypoints", GUILayout.Height(22)))
        {
            Undo.RecordObject(seq, "Clear Intro Waypoints");
            for (int i = wpProp.arraySize - 1; i >= 0; i--)
            {
                var wpRef = wpProp.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                if (wpRef != null)
                    Undo.DestroyObjectImmediate(wpRef.gameObject);
            }
            wpProp.ClearArray();
            serializedObject.ApplyModifiedProperties();
        }
    }

    // Draw the path in the Scene View
    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawPathGizmo(DayIntroSequence seq, GizmoType gizmoType)
    {
        var wpField = typeof(DayIntroSequence).GetField("_waypoints",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (wpField == null) return;
        var waypoints = wpField.GetValue(seq) as Transform[];
        if (waypoints == null || waypoints.Length < 2) return;

        // Draw Catmull-Rom spline
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.8f);
        int steps = 20;
        for (int i = 0; i < waypoints.Length - 1; i++)
        {
            if (waypoints[i] == null || waypoints[i + 1] == null) continue;
            Vector3 prev = CatmullRomGizmo(waypoints, i, 0f);
            for (int s = 1; s <= steps; s++)
            {
                float t = s / (float)steps;
                Vector3 next = CatmullRomGizmo(waypoints, i, t);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }

        // Draw waypoint markers
        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null) continue;
            Gizmos.color = i == 0 ? Color.green : i == waypoints.Length - 1 ? Color.red : Color.yellow;
            Gizmos.DrawWireSphere(waypoints[i].position, 0.08f);

            // Forward direction arrow
            Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.6f);
            Gizmos.DrawRay(waypoints[i].position, waypoints[i].forward * 0.4f);
        }
    }

    private static Vector3 CatmullRomGizmo(Transform[] wp, int seg, float t)
    {
        int n = wp.Length;
        Vector3 p0 = wp[Mathf.Max(seg - 1, 0)].position;
        Vector3 p1 = wp[seg].position;
        Vector3 p2 = wp[Mathf.Min(seg + 1, n - 1)].position;
        Vector3 p3 = wp[Mathf.Min(seg + 2, n - 1)].position;

        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }
}
