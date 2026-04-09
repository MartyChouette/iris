using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DateSessionManager))]
public class DateSessionManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var mgr = (DateSessionManager)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Phase Camera Capture", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Position the Scene View camera to frame Nema + the date character, " +
            "then click a button to capture the framing for that phase. " +
            "The captured pos/rot/fov is pushed into ApartmentManager during phase transitions.",
            MessageType.Info);

        DrawCaptureRow(mgr, "Arrival",  ref mgr.EditorGetArrivalCamera());
        DrawCaptureRow(mgr, "Kitchen",  ref mgr.EditorGetKitchenCamera());
        DrawCaptureRow(mgr, "Couch",    ref mgr.EditorGetCouchCamera());

        EditorGUILayout.Space(5);
        if (GUILayout.Button("Clear All Captures", GUILayout.Height(24)))
        {
            Undo.RecordObject(mgr, "Clear Phase Cameras");
            mgr.EditorGetArrivalCamera().captured = false;
            mgr.EditorGetKitchenCamera().captured = false;
            mgr.EditorGetCouchCamera().captured = false;
            EditorUtility.SetDirty(mgr);
        }
    }

    private void DrawCaptureRow(DateSessionManager mgr, string label, ref DateSessionManager.PhaseCameraFrame frame)
    {
        EditorGUILayout.BeginHorizontal();

        string status = frame.captured ? "captured" : "not set";
        Color statusColor = frame.captured ? new Color(0.3f, 0.8f, 0.3f) : new Color(0.8f, 0.4f, 0.3f);
        var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = statusColor } };

        EditorGUILayout.LabelField($"  {label}", GUILayout.Width(80));
        EditorGUILayout.LabelField($"[{status}]", style, GUILayout.Width(70));

        if (GUILayout.Button($"Capture → {label}", GUILayout.Height(22)))
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv != null)
            {
                Undo.RecordObject(mgr, $"Capture {label} Camera");
                frame.position = sv.camera.transform.position;
                frame.rotation = sv.camera.transform.eulerAngles;
                frame.fov = sv.camera.fieldOfView;
                frame.captured = true;
                EditorUtility.SetDirty(mgr);
                Debug.Log($"[DateSessionManager] Captured {label}: pos={frame.position}, rot={frame.rotation}, fov={frame.fov:F1}");
            }
            else
            {
                Debug.LogWarning("[DateSessionManager] No active Scene View.");
            }
        }

        if (GUILayout.Button("Preview", GUILayout.Width(60), GUILayout.Height(22)))
        {
            if (frame.captured)
            {
                var sv = SceneView.lastActiveSceneView;
                if (sv != null)
                {
                    sv.pivot = frame.position + Quaternion.Euler(frame.rotation) * Vector3.forward * 5f;
                    sv.rotation = Quaternion.Euler(frame.rotation);
                    sv.size = 5f;
                    sv.Repaint();
                }
            }
        }

        EditorGUILayout.EndHorizontal();
    }
}
