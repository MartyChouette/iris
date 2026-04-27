using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor window that spawns a physical preview camera in the scene.
/// Drag it around to frame each date phase, then click Capture.
/// Live preview shows exactly what the game camera will see.
/// Open via Iris > Phase Camera Preview.
/// </summary>
public class PhaseCameraPreviewWindow : EditorWindow
{
    private const string CameraName = "[PhaseCameraPreview]";
    private const int PreviewWidth = 480;
    private const int PreviewHeight = 270;

    private Camera _previewCam;
    private RenderTexture _rt;
    private int _selectedPhase; // 0=Arrival, 1=Kitchen, 2=Couch
    private readonly string[] _phaseNames = { "Arrival", "Kitchen / Drinks", "Couch / Reveal" };
    private bool _ortho = true;
    private Vector2 _scroll;

    [MenuItem("Iris/Phase Camera Preview")]
    public static void Open()
    {
        var w = GetWindow<PhaseCameraPreviewWindow>("Phase Camera");
        w.minSize = new Vector2(500, 620);
    }

    private void OnEnable()
    {
        // Find or create the preview camera
        FindOrSpawnCamera();
        EnsureRT();
        EditorApplication.update += Repaint;
    }

    private void OnDisable()
    {
        EditorApplication.update -= Repaint;
    }

    private void OnDestroy()
    {
        CleanupRT();
        // Don't destroy the camera here — user might want it to persist while
        // they switch windows. They can click Destroy or it's cleaned on play.
    }

    private void FindOrSpawnCamera()
    {
        if (_previewCam != null) return;

        var existing = GameObject.Find(CameraName);
        if (existing != null)
        {
            _previewCam = existing.GetComponent<Camera>();
            if (_previewCam != null)
            {
                _ortho = _previewCam.orthographic;
                return;
            }
        }

        var go = new GameObject(CameraName);
        go.tag = "EditorOnly";
        _previewCam = go.AddComponent<Camera>();
        _previewCam.enabled = false; // manual render only
        _previewCam.depth = -100;
        _previewCam.orthographic = true;
        _previewCam.orthographicSize = 5f;
        _previewCam.nearClipPlane = -9f;
        _previewCam.farClipPlane = 1000f;
        _previewCam.fieldOfView = 60f;
        _ortho = true;

        // Position at scene view camera if available
        var sv = SceneView.lastActiveSceneView;
        if (sv != null)
        {
            go.transform.position = sv.camera.transform.position;
            go.transform.rotation = sv.camera.transform.rotation;
            if (sv.orthographic)
                _previewCam.orthographicSize = sv.size;
        }

        Selection.activeGameObject = go;
        Debug.Log("[PhaseCameraPreview] Preview camera spawned. Select it in the scene and position it.");
    }

    private void EnsureRT()
    {
        if (_rt != null && _rt.width == PreviewWidth && _rt.height == PreviewHeight) return;
        CleanupRT();
        _rt = new RenderTexture(PreviewWidth, PreviewHeight, 24);
        _rt.antiAliasing = 2;
    }

    private void CleanupRT()
    {
        if (_rt != null)
        {
            _rt.Release();
            DestroyImmediate(_rt);
            _rt = null;
        }
    }

    private void RenderPreview()
    {
        if (_previewCam == null || _rt == null) return;

        // Apply zoom step override so the preview reflects what the player sees
        float savedFOV = _previewCam.fieldOfView;
        float savedOrtho = _previewCam.orthographicSize;
        ApplyZoomToPreview();

        _previewCam.targetTexture = _rt;
        _previewCam.Render();
        _previewCam.targetTexture = null;

        // Restore so the inspector fields stay in sync with the base values
        _previewCam.fieldOfView = savedFOV;
        _previewCam.orthographicSize = savedOrtho;
    }

    private void ApplyZoomToPreview()
    {
        var mgr = FindDSM();
        if (mgr == null) return;

        ref var frame = ref GetFrame(mgr, _selectedPhase);
        if (!frame.captured || frame.zoomStep < 0) return;

        var am = ApartmentManager.Instance;
        if (am == null || am.ZoomSteps == null || am.ZoomSteps.Length == 0) return;

        int step = Mathf.Clamp(frame.zoomStep, 0, am.ZoomSteps.Length - 1);
        float zoomValue = am.ZoomSteps[step];

        if (_previewCam.orthographic)
            _previewCam.orthographicSize = zoomValue;
        else
            _previewCam.fieldOfView = zoomValue;
    }

    private DateSessionManager FindDSM()
    {
        return Object.FindFirstObjectByType<DateSessionManager>();
    }

    private ref DateSessionManager.PhaseCameraFrame GetFrame(DateSessionManager mgr, int phase)
    {
        switch (phase)
        {
            case 0: return ref mgr.EditorGetArrivalCamera();
            case 1: return ref mgr.EditorGetKitchenCamera();
            default: return ref mgr.EditorGetCouchCamera();
        }
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        // ── Camera status ──
        if (_previewCam == null)
        {
            EditorGUILayout.HelpBox("Preview camera not found. Click Spawn to create one.", MessageType.Warning);
            if (GUILayout.Button("Spawn Preview Camera", GUILayout.Height(28)))
            {
                FindOrSpawnCamera();
                EnsureRT();
            }
            EditorGUILayout.EndScrollView();
            return;
        }

        // ── Live preview ──
        EnsureRT();
        RenderPreview();

        EditorGUILayout.LabelField("Live Preview", EditorStyles.boldLabel);
        var previewRect = GUILayoutUtility.GetRect(PreviewWidth, PreviewHeight, GUILayout.ExpandWidth(false));
        if (_rt != null)
            GUI.DrawTexture(previewRect, _rt, ScaleMode.ScaleToFit);

        EditorGUILayout.Space(5);

        // ── Camera controls ──
        EditorGUILayout.LabelField("Camera Settings", EditorStyles.boldLabel);

        bool newOrtho = EditorGUILayout.Toggle("Orthographic", _ortho);
        if (newOrtho != _ortho)
        {
            _ortho = newOrtho;
            _previewCam.orthographic = _ortho;
        }

        if (_ortho)
        {
            _previewCam.orthographicSize = EditorGUILayout.FloatField("Ortho Size", _previewCam.orthographicSize);
        }
        else
        {
            _previewCam.fieldOfView = EditorGUILayout.Slider("Field of View", _previewCam.fieldOfView, 10f, 120f);
        }

        _previewCam.nearClipPlane = EditorGUILayout.FloatField("Near Clip", _previewCam.nearClipPlane);
        _previewCam.farClipPlane = EditorGUILayout.FloatField("Far Clip", _previewCam.farClipPlane);

        EditorGUILayout.Space(3);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Select Camera", GUILayout.Height(22)))
            Selection.activeGameObject = _previewCam.gameObject;
        if (GUILayout.Button("Snap Scene View to Camera", GUILayout.Height(22)))
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv != null)
            {
                sv.orthographic = _previewCam.orthographic;
                sv.AlignViewToObject(_previewCam.transform);
                if (_previewCam.orthographic)
                    sv.size = _previewCam.orthographicSize;
                else
                    sv.cameraSettings.fieldOfView = _previewCam.fieldOfView;
                sv.Repaint();
            }
        }
        if (GUILayout.Button("Snap Camera to Scene View", GUILayout.Height(22)))
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv != null)
            {
                Undo.RecordObject(_previewCam.transform, "Snap to Scene View");
                Undo.RecordObject(_previewCam, "Snap to Scene View");
                _previewCam.transform.position = sv.camera.transform.position;
                _previewCam.transform.rotation = sv.camera.transform.rotation;
                _previewCam.orthographic = sv.orthographic;
                _ortho = sv.orthographic;
                if (sv.orthographic)
                    _previewCam.orthographicSize = sv.size;
                else
                    _previewCam.fieldOfView = sv.camera.fieldOfView;
                _previewCam.nearClipPlane = sv.camera.nearClipPlane;
                _previewCam.farClipPlane = sv.camera.farClipPlane;
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);

        // ── Phase selection ──
        EditorGUILayout.LabelField("Phase Cameras", EditorStyles.boldLabel);

        var mgr = FindDSM();
        if (mgr == null)
        {
            EditorGUILayout.HelpBox("DateSessionManager not found in scene.", MessageType.Warning);
            EditorGUILayout.EndScrollView();
            return;
        }

        _selectedPhase = GUILayout.Toolbar(_selectedPhase, _phaseNames, GUILayout.Height(26));

        EditorGUILayout.Space(5);

        ref var frame = ref GetFrame(mgr, _selectedPhase);

        // Show current frame status
        string status = frame.captured ? "Captured" : "Not set";
        Color statusCol = frame.captured ? new Color(0.3f, 0.85f, 0.3f) : new Color(0.9f, 0.4f, 0.3f);
        var statusStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = statusCol } };
        EditorGUILayout.LabelField($"  {_phaseNames[_selectedPhase]}: {status}", statusStyle);

        if (frame.captured)
        {
            EditorGUILayout.LabelField($"  Pos: {frame.position:F2}");
            EditorGUILayout.LabelField($"  Rot: {frame.rotation:F1}");
            EditorGUILayout.LabelField($"  FOV/Size: {frame.fov:F2}  |  Perspective: {frame.perspective}  |  pFOV: {frame.perspectiveFOV:F1}");
            EditorGUILayout.LabelField($"  Near: {frame.nearClip:F1}  |  Far: {frame.farClip:F0}  |  Pan: {frame.panLimit:F2}  |  Zoom: {frame.zoomStep}");
        }

        EditorGUILayout.Space(5);

        // ── Capture / Load / Clear ──
        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
        if (GUILayout.Button($"Capture Preview -> {_phaseNames[_selectedPhase]}", GUILayout.Height(30)))
        {
            Undo.RecordObject(mgr, $"Capture {_phaseNames[_selectedPhase]}");
            frame.position = _previewCam.transform.position;
            frame.rotation = _previewCam.transform.eulerAngles;
            frame.fov = _previewCam.orthographic ? _previewCam.orthographicSize : _previewCam.fieldOfView;
            frame.perspective = !_previewCam.orthographic;
            frame.perspectiveFOV = _previewCam.fieldOfView;
            frame.nearClip = _previewCam.nearClipPlane;
            frame.farClip = _previewCam.farClipPlane;
            frame.captured = true;
            EditorUtility.SetDirty(mgr);
            Debug.Log($"[PhaseCameraPreview] Captured {_phaseNames[_selectedPhase]}: pos={frame.position}, rot={frame.rotation}");
        }
        GUI.backgroundColor = Color.white;

        GUI.enabled = frame.captured;
        if (GUILayout.Button($"Load {_phaseNames[_selectedPhase]} -> Preview", GUILayout.Height(30)))
        {
            Undo.RecordObject(_previewCam.transform, "Load Phase Camera");
            Undo.RecordObject(_previewCam, "Load Phase Camera");
            _previewCam.transform.position = frame.position;
            _previewCam.transform.rotation = Quaternion.Euler(frame.rotation);
            _previewCam.orthographic = !frame.perspective;
            _ortho = _previewCam.orthographic;
            if (frame.perspective)
                _previewCam.fieldOfView = frame.perspectiveFOV;
            else
                _previewCam.orthographicSize = frame.fov;
            _previewCam.nearClipPlane = frame.nearClip;
            _previewCam.farClipPlane = frame.farClip;
        }
        GUI.enabled = true;

        EditorGUILayout.EndHorizontal();

        // ── Per-frame settings ──
        if (frame.captured)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Frame Settings", EditorStyles.boldLabel);

            float newPan = EditorGUILayout.Slider("Pan Limit", frame.panLimit, 0f, 5f);
            int newZoom = EditorGUILayout.IntSlider("Zoom Step (-1 = don't override)", frame.zoomStep, -1, 4);

            if (newPan != frame.panLimit || newZoom != frame.zoomStep)
            {
                Undo.RecordObject(mgr, $"Adjust {_phaseNames[_selectedPhase]}");
                frame.panLimit = newPan;
                frame.zoomStep = newZoom;
                EditorUtility.SetDirty(mgr);
            }
        }

        EditorGUILayout.Space(10);

        // ── Quick actions ──
        EditorGUILayout.LabelField("Quick Actions", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        for (int i = 0; i < 3; i++)
        {
            ref var f = ref GetFrame(mgr, i);
            GUI.enabled = f.captured;
            if (GUILayout.Button($"Go to {_phaseNames[i]}", GUILayout.Height(22)))
            {
                Undo.RecordObject(_previewCam.transform, "Go to Phase");
                Undo.RecordObject(_previewCam, "Go to Phase");
                _previewCam.transform.position = f.position;
                _previewCam.transform.rotation = Quaternion.Euler(f.rotation);
                _previewCam.orthographic = !f.perspective;
                _ortho = _previewCam.orthographic;
                if (f.perspective)
                    _previewCam.fieldOfView = f.perspectiveFOV;
                else
                    _previewCam.orthographicSize = f.fov;
                _previewCam.nearClipPlane = f.nearClip;
                _previewCam.farClipPlane = f.farClip;
                _selectedPhase = i;
            }
            GUI.enabled = true;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Capture ALL from Preview Positions", GUILayout.Height(24)))
        {
            // Only captures the currently selected phase — user should use
            // Load -> adjust -> Capture for each phase individually.
            EditorUtility.DisplayDialog("Capture All",
                "Use 'Go to' to load each phase, adjust the camera, then 'Capture' each one.\n\n" +
                "This ensures each phase gets its own framing.",
                "OK");
        }

        GUI.backgroundColor = new Color(0.9f, 0.4f, 0.3f);
        if (GUILayout.Button("Destroy Preview Camera", GUILayout.Height(24)))
        {
            if (_previewCam != null)
            {
                DestroyImmediate(_previewCam.gameObject);
                _previewCam = null;
            }
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndScrollView();
    }
}
