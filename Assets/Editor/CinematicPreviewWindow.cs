using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor window for previewing date cinematics without entering Play mode.
/// Drop a date character model, pick a phase, and scrub through the camera
/// moves in the Scene View to dial in timing and framing.
/// Open via Iris > Cinematic Preview.
/// </summary>
public class CinematicPreviewWindow : EditorWindow
{
    // ── Setup ──
    private DatePersonalDefinition _dateDef;
    private GameObject _previewCharacter;
    private bool _characterSpawned;

    // ── Phase selection ──
    private enum PreviewPhase { Arrival, Kitchen, Couch }
    private PreviewPhase _phase = PreviewPhase.Arrival;

    // ── Scrubbing ──
    private enum CinematicType { FaceSweep, VerdictSwirl }
    private CinematicType _cinematic = CinematicType.FaceSweep;
    private float _scrubT; // 0-1 normalized time through the cinematic

    // ── Face sweep params (mirror DateSessionManager) ──
    private float _sweepDistance = 1.2f;
    private float _sweepWidth = 0.6f;
    private float _sweepFOV = 2.5f;
    private float _sweepApproachAngle = 0f;
    private float _sweepStartHeight = 0.85f;
    private float _sweepEndHeight = 0.85f;
    private float _sweepLookAtHeight = 0.85f;

    // ── Verdict swirl params ──
    private float _swirlDistance = 1.5f;
    private float _swirlFOV = 3.0f;
    private float _swirlStartAngle = 0f;
    private float _swirlEndAngle = 540f;
    private float _swirlStartHeight = 2.0f;
    private float _swirlEndHeight = 0.8f;
    private float _swirlLookAtHeight = 0.8f;

    // ── State ──
    private Vector3 _characterPos;
    private Vector2 _scroll;

    [MenuItem("Iris/Cinematic Preview")]
    public static void Open()
    {
        var w = GetWindow<CinematicPreviewWindow>("Cinematic Preview");
        w.minSize = new Vector2(320, 500);
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.LabelField("Character Setup", EditorStyles.boldLabel);

        var newDef = (DatePersonalDefinition)EditorGUILayout.ObjectField(
            "Date Definition", _dateDef, typeof(DatePersonalDefinition), false);
        if (newDef != _dateDef)
        {
            _dateDef = newDef;
            DespawnPreview();
        }

        _previewCharacter = (GameObject)EditorGUILayout.ObjectField(
            "Preview Model", _previewCharacter, typeof(GameObject), true);

        EditorGUILayout.Space(5);

        // Spawn/despawn
        EditorGUILayout.BeginHorizontal();
        if (!_characterSpawned)
        {
            if (GUILayout.Button("Spawn Character", GUILayout.Height(28)))
                SpawnPreview();
        }
        else
        {
            if (GUILayout.Button("Despawn", GUILayout.Height(28)))
                DespawnPreview();

            if (GUILayout.Button("Reposition to Scene View", GUILayout.Height(28)))
                RepositionToSceneView();
        }
        EditorGUILayout.EndHorizontal();

        if (_characterSpawned && _previewCharacter != null)
            _characterPos = _previewCharacter.transform.position;

        EditorGUILayout.Space(10);

        // ── Phase & Cinematic ──
        EditorGUILayout.LabelField("Cinematic", EditorStyles.boldLabel);
        _phase = (PreviewPhase)EditorGUILayout.EnumPopup("Phase", _phase);
        _cinematic = (CinematicType)EditorGUILayout.EnumPopup("Type", _cinematic);

        EditorGUILayout.Space(5);

        // ── Params ──
        if (_cinematic == CinematicType.FaceSweep)
        {
            EditorGUILayout.LabelField("Face Sweep Settings", EditorStyles.boldLabel);
            _sweepDistance = EditorGUILayout.Slider("Distance", _sweepDistance, 0.3f, 5f);
            _sweepWidth = EditorGUILayout.Slider("Sweep Width", _sweepWidth, 0.1f, 3f);
            _sweepFOV = EditorGUILayout.Slider("FOV / Ortho Size", _sweepFOV, 0.5f, 10f);

            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField("Direction", EditorStyles.miniLabel);
            _sweepApproachAngle = EditorGUILayout.Slider("Approach Angle (deg)", _sweepApproachAngle, -360f, 360f);

            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField("Height", EditorStyles.miniLabel);
            _sweepStartHeight = EditorGUILayout.Slider("Start Height", _sweepStartHeight, 0f, 3f);
            _sweepEndHeight = EditorGUILayout.Slider("End Height", _sweepEndHeight, 0f, 3f);
            _sweepLookAtHeight = EditorGUILayout.Slider("Look-At Height", _sweepLookAtHeight, 0f, 3f);
        }
        else
        {
            EditorGUILayout.LabelField("Verdict Swirl Settings", EditorStyles.boldLabel);
            _swirlDistance = EditorGUILayout.Slider("End Distance", _swirlDistance, 0.5f, 5f);
            _swirlFOV = EditorGUILayout.Slider("FOV / Ortho Size", _swirlFOV, 0.5f, 10f);

            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField("Orbit", EditorStyles.miniLabel);
            _swirlStartAngle = EditorGUILayout.Slider("Start Angle (deg)", _swirlStartAngle, -360f, 360f);
            _swirlEndAngle = EditorGUILayout.Slider("End Angle (deg)", _swirlEndAngle, -720f, 1080f);
            float orbits = (_swirlEndAngle - _swirlStartAngle) / 360f;
            EditorGUILayout.LabelField($"  = {orbits:F2} orbits", EditorStyles.miniLabel);

            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField("Height", EditorStyles.miniLabel);
            _swirlStartHeight = EditorGUILayout.Slider("Start Height", _swirlStartHeight, -2f, 5f);
            _swirlEndHeight = EditorGUILayout.Slider("End Height", _swirlEndHeight, -2f, 5f);
            _swirlLookAtHeight = EditorGUILayout.Slider("Look-At Height", _swirlLookAtHeight, 0f, 3f);
        }

        EditorGUILayout.Space(10);

        // ── Scrub bar ──
        EditorGUILayout.LabelField("Scrub Timeline", EditorStyles.boldLabel);
        float newT = EditorGUILayout.Slider("Time", _scrubT, 0f, 1f);
        if (newT != _scrubT)
        {
            _scrubT = newT;
            ApplyScrub();
        }

        EditorGUILayout.Space(5);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("|< Start", GUILayout.Height(24))) { _scrubT = 0f; ApplyScrub(); }
        if (GUILayout.Button("Mid", GUILayout.Height(24))) { _scrubT = 0.5f; ApplyScrub(); }
        if (GUILayout.Button("End >|", GUILayout.Height(24))) { _scrubT = 1f; ApplyScrub(); }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);

        if (GUILayout.Button("Copy Settings to DateSessionManager", GUILayout.Height(28)))
            CopyToDateSessionManager();

        EditorGUILayout.Space(5);

        EditorGUILayout.HelpBox(
            "Drag the Time slider to scrub through the cinematic in the Scene View.\n" +
            "Spawn a character model or select one already in the scene.\n" +
            "Use 'Copy Settings' to push your tweaked values into DateSessionManager.",
            MessageType.Info);

        EditorGUILayout.EndScrollView();
    }

    private void SpawnPreview()
    {
        if (_previewCharacter != null && !_characterSpawned)
        {
            // Already have a scene reference — just use it
            _characterSpawned = true;
            _characterPos = _previewCharacter.transform.position;
            return;
        }

        // Try to spawn from definition
        GameObject prefab = null;
        if (_dateDef != null)
            prefab = _dateDef.characterModelPrefab ?? _dateDef.characterPrefab;

        if (prefab == null && _previewCharacter == null)
        {
            Debug.LogWarning("[CinematicPreview] No model to spawn. Assign a Date Definition with a model prefab, or drag a model into Preview Model.");
            return;
        }

        if (prefab != null)
        {
            // Spawn at scene view pivot
            var sv = SceneView.lastActiveSceneView;
            Vector3 pos = sv != null ? sv.pivot : Vector3.zero;
            pos.y = 0f;

            _previewCharacter = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            _previewCharacter.transform.position = pos;
            _previewCharacter.name = $"[CinematicPreview] {(_dateDef != null ? _dateDef.characterName : "Character")}";
            Undo.RegisterCreatedObjectUndo(_previewCharacter, "Spawn Cinematic Preview");
        }

        _characterSpawned = true;
        _characterPos = _previewCharacter.transform.position;
    }

    private void DespawnPreview()
    {
        if (_previewCharacter != null && _previewCharacter.name.StartsWith("[CinematicPreview]"))
        {
            Undo.DestroyObjectImmediate(_previewCharacter);
        }
        _previewCharacter = null;
        _characterSpawned = false;
    }

    private void RepositionToSceneView()
    {
        if (_previewCharacter == null) return;
        var sv = SceneView.lastActiveSceneView;
        if (sv == null) return;

        Vector3 pos = sv.pivot;
        pos.y = 0f;
        Undo.RecordObject(_previewCharacter.transform, "Reposition Preview Character");
        _previewCharacter.transform.position = pos;
        _characterPos = pos;
    }

    private void ApplyScrub()
    {
        var sv = SceneView.lastActiveSceneView;
        if (sv == null) return;

        // Use character position or scene view pivot as target
        Vector3 targetRoot = _characterSpawned && _previewCharacter != null
            ? _previewCharacter.transform.position
            : sv.pivot;

        if (_cinematic == CinematicType.FaceSweep)
            ApplyFaceSweepScrub(sv, targetRoot);
        else
            ApplyVerdictSwirlScrub(sv, targetRoot);
    }

    private void ApplyFaceSweepScrub(SceneView sv, Vector3 root)
    {
        Vector3 lookTarget = root + Vector3.up * _sweepLookAtHeight;

        // Approach direction from configurable angle
        float approachRad = _sweepApproachAngle * Mathf.Deg2Rad;
        Vector3 camDir = new Vector3(Mathf.Cos(approachRad), 0f, Mathf.Sin(approachRad));
        Vector3 closePos = root + camDir * _sweepDistance;

        // Sweep direction: perpendicular to approach
        Vector3 sweepDir = Vector3.Cross(camDir, Vector3.up).normalized;

        Vector3 sweepStartXZ = closePos - sweepDir * (_sweepWidth * 0.5f);
        Vector3 sweepEndXZ = closePos + sweepDir * (_sweepWidth * 0.5f);
        Vector3 sweepStart = new Vector3(sweepStartXZ.x, root.y + _sweepStartHeight, sweepStartXZ.z);
        Vector3 sweepEnd = new Vector3(sweepEndXZ.x, root.y + _sweepEndHeight, sweepEndXZ.z);

        // Far position for push-in / pull-back
        float midHeight = root.y + (_sweepStartHeight + _sweepEndHeight) * 0.5f;
        Vector3 farPos = closePos + camDir * (_sweepDistance * 2f);
        farPos.y = midHeight;

        // Map scrub 0-1 to: push-in (0-0.15), sweep (0.15-0.75), hold (0.75-0.85), pull-back (0.85-1)
        Vector3 pos;
        if (_scrubT < 0.15f)
        {
            float t = Mathf.SmoothStep(0f, 1f, _scrubT / 0.15f);
            pos = Vector3.Lerp(farPos, sweepStart, t);
        }
        else if (_scrubT < 0.75f)
        {
            float t = Mathf.SmoothStep(0f, 1f, (_scrubT - 0.15f) / 0.6f);
            pos = Vector3.Lerp(sweepStart, sweepEnd, t);
        }
        else if (_scrubT < 0.85f)
        {
            pos = sweepEnd;
        }
        else
        {
            float t = Mathf.SmoothStep(0f, 1f, (_scrubT - 0.85f) / 0.15f);
            pos = Vector3.Lerp(sweepEnd, farPos, t);
        }

        Quaternion lookRot = Quaternion.LookRotation(lookTarget - pos, Vector3.up);
        float fov = _scrubT < 0.15f
            ? Mathf.Lerp(sv.size, _sweepFOV, _scrubT / 0.15f)
            : _scrubT > 0.85f
                ? Mathf.Lerp(_sweepFOV, sv.size, (_scrubT - 0.85f) / 0.15f)
                : _sweepFOV;

        sv.pivot = pos + lookRot * Vector3.forward * sv.cameraDistance;
        sv.rotation = lookRot;
        sv.size = fov;
        sv.Repaint();
    }

    private void ApplyVerdictSwirlScrub(SceneView sv, Vector3 root)
    {
        Vector3 target = root + Vector3.up * _swirlLookAtHeight;
        float startAngle = _swirlStartAngle * Mathf.Deg2Rad;
        float endAngle = _swirlEndAngle * Mathf.Deg2Rad;
        float totalAngle = endAngle - startAngle;
        float t = Mathf.SmoothStep(0f, 1f, _scrubT);
        float angle = startAngle + totalAngle * t;

        // Start distance: 3x end distance (matches runtime startDist)
        float startDist = _swirlDistance * 3f;
        float dist = Mathf.Lerp(startDist, _swirlDistance, t);
        float height = Mathf.Lerp(target.y + _swirlStartHeight, target.y + _swirlEndHeight, t);

        Vector3 orbitPos = target + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist;
        orbitPos.y = height;

        Quaternion lookRot = Quaternion.LookRotation(target - orbitPos, Vector3.up);
        float fov = Mathf.Lerp(sv.size, _swirlFOV, t);

        sv.pivot = orbitPos + lookRot * Vector3.forward * sv.cameraDistance;
        sv.rotation = lookRot;
        sv.size = fov;
        sv.Repaint();
    }

    private void CopyToDateSessionManager()
    {
        var dsm = Object.FindFirstObjectByType<DateSessionManager>();
        if (dsm == null)
        {
            Debug.LogWarning("[CinematicPreview] No DateSessionManager found in scene.");
            return;
        }

        Undo.RecordObject(dsm, "Copy Cinematic Settings");
        var so = new SerializedObject(dsm);

        if (_cinematic == CinematicType.FaceSweep)
        {
            so.FindProperty("_sweepDistance").floatValue = _sweepDistance;
            so.FindProperty("_sweepWidth").floatValue = _sweepWidth;
            so.FindProperty("_sweepFOV").floatValue = _sweepFOV;
            so.FindProperty("_sweepApproachAngle").floatValue = _sweepApproachAngle;
            so.FindProperty("_sweepStartHeight").floatValue = _sweepStartHeight;
            so.FindProperty("_sweepEndHeight").floatValue = _sweepEndHeight;
            so.FindProperty("_sweepLookAtHeight").floatValue = _sweepLookAtHeight;
        }
        else
        {
            so.FindProperty("_verdictZoomDistance").floatValue = _swirlDistance;
            so.FindProperty("_swirlEndAngle").floatValue = _swirlEndAngle;
            so.FindProperty("_verdictZoomFOV").floatValue = _swirlFOV;
            so.FindProperty("_swirlStartAngle").floatValue = _swirlStartAngle;
            so.FindProperty("_swirlStartHeight").floatValue = _swirlStartHeight;
            so.FindProperty("_swirlEndHeight").floatValue = _swirlEndHeight;
            so.FindProperty("_swirlLookAtHeight").floatValue = _swirlLookAtHeight;
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(dsm);
        Debug.Log($"[CinematicPreview] Copied {_cinematic} settings to DateSessionManager.");
    }

    private void OnDestroy()
    {
        DespawnPreview();
    }
}
