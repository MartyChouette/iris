using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor window for jumping between game phases/moments at runtime.
/// Open via Iris > Phase Jumper in the menu bar.
/// Only works in Play mode — buttons are disabled otherwise.
/// </summary>
public class PhaseJumperWindow : EditorWindow
{
    private Vector2 _scroll;
    private DatePersonalDefinition _selectedDate;
    private DatePersonalDefinition[] _allDates;
    private string[] _dateNames;
    private int _dateIndex;

    [MenuItem("Iris/Phase Jumper")]
    public static void Open()
    {
        var w = GetWindow<PhaseJumperWindow>("Phase Jumper");
        w.minSize = new Vector2(280, 400);
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        bool playing = Application.isPlaying;

        // ── Current State ──
        EditorGUILayout.LabelField("Current State", EditorStyles.boldLabel);
        if (playing)
        {
            var dpm = DayPhaseManager.Instance;
            var dsm = DateSessionManager.Instance;

            string dayPhase = dpm != null ? dpm.CurrentPhase.ToString() : "—";
            string datePhase = dsm != null ? dsm.CurrentDatePhase.ToString() : "—";
            string sessionState = dsm != null ? dsm.CurrentState.ToString() : "—";

            EditorGUILayout.LabelField($"  Day Phase:      {dayPhase}");
            EditorGUILayout.LabelField($"  Date Phase:     {datePhase}");
            EditorGUILayout.LabelField($"  Session State:  {sessionState}");
        }
        else
        {
            EditorGUILayout.HelpBox("Enter Play mode to use the Phase Jumper.", MessageType.Info);
        }

        EditorGUILayout.Space(10);

        // ── Day Phases ──
        EditorGUILayout.LabelField("Day Phases", EditorStyles.boldLabel);
        GUI.enabled = playing;

        if (Button("Morning", "Newspaper reading"))
            JumpToDayPhase(DayPhaseManager.DayPhase.Morning);

        if (Button("Exploration", "Pre-date cleanup, Nema leaning"))
            JumpToDayPhase(DayPhaseManager.DayPhase.Exploration);

        if (Button("Evening", "Post-date, Go to Bed"))
            JumpToDayPhase(DayPhaseManager.DayPhase.Evening);

        EditorGUILayout.Space(10);

        // ── Schedule Date ──
        EditorGUILayout.LabelField("Schedule Date", EditorStyles.boldLabel);
        GUI.enabled = playing;

        // Refresh date list
        if (_allDates == null || GUILayout.Button("Refresh Date List", GUILayout.Height(20)))
        {
            _allDates = Resources.FindObjectsOfTypeAll<DatePersonalDefinition>();
            _dateNames = new string[_allDates.Length];
            for (int i = 0; i < _allDates.Length; i++)
                _dateNames[i] = _allDates[i].characterName;
            // Try to match current selection
            _dateIndex = 0;
            if (_selectedDate != null)
                for (int i = 0; i < _allDates.Length; i++)
                    if (_allDates[i] == _selectedDate) { _dateIndex = i; break; }
        }

        if (_allDates != null && _allDates.Length > 0)
        {
            _dateIndex = EditorGUILayout.Popup("Date Character", _dateIndex, _dateNames);
            _selectedDate = _allDates[_dateIndex];

            string currentDateName = DateSessionManager.Instance != null && DateSessionManager.Instance.CurrentDate != null
                ? DateSessionManager.Instance.CurrentDate.characterName : "None";
            EditorGUILayout.LabelField($"  Current: {currentDateName}");

            if (GUILayout.Button($"Schedule {_selectedDate.characterName}", GUILayout.Height(26)))
            {
                if (DateSessionManager.Instance != null)
                {
                    DateSessionManager.Instance.ScheduleDate(_selectedDate);
                    Debug.Log($"[PhaseJumper] Scheduled date: {_selectedDate.characterName}");
                }
            }

            if (GUILayout.Button("Schedule + Force Doorbell", GUILayout.Height(26)))
            {
                if (DateSessionManager.Instance != null)
                {
                    DateSessionManager.Instance.ScheduleDate(_selectedDate);
                    DateSessionManager.Instance.OnDateCharacterArrived();
                    Debug.Log($"[PhaseJumper] Scheduled + arrived: {_selectedDate.characterName}");
                }
            }
        }
        else
        {
            EditorGUILayout.HelpBox("No DatePersonalDefinition assets found.", MessageType.Warning);
        }

        EditorGUILayout.Space(10);

        // ── Date Phases ──
        EditorGUILayout.LabelField("Date Phases", EditorStyles.boldLabel);

        bool hasDate = playing && DateSessionManager.Instance != null
            && DateSessionManager.Instance.CurrentDate != null;

        if (!hasDate && playing)
        {
            EditorGUILayout.HelpBox(
                "No date scheduled. Use the dropdown above to schedule one.", MessageType.Warning);
        }

        GUI.enabled = playing && hasDate;

        if (Button("Arrival", "NPC at entrance, outfit judgments"))
            JumpToDatePhase(DateSessionManager.DatePhase.Arrival);

        if (Button("Kitchen / Drinks", "NPC at kitchen, drink mixing"))
            JumpToDatePhase(DateSessionManager.DatePhase.BackgroundJudging);

        if (Button("Couch / Reveal", "NPC on couch, item reactions"))
            JumpToDatePhase(DateSessionManager.DatePhase.Reveal);

        GUI.enabled = playing;

        EditorGUILayout.Space(5);

        if (Button("Skip Forward >>", "Click Continue/Serve, select first glass, advance current step"))
            SkipForward();

        if (Button("End Date Now", "Skip to date end + results"))
        {
            if (DateSessionManager.Instance != null)
                DateSessionManager.Instance.EndDate();
        }

        EditorGUILayout.Space(10);

        // ── Special Moments ──
        EditorGUILayout.LabelField("Special Moments", EditorStyles.boldLabel);

        if (Button("Flower Trimming", "Post-date dream + trimming"))
            JumpToDayPhase(DayPhaseManager.DayPhase.FlowerTrimming);

        EditorGUILayout.Space(10);

        // ── Camera Shortcuts ──
        EditorGUILayout.LabelField("Camera", EditorStyles.boldLabel);

        if (Button("Apply Arrival Camera", "Snap to arrival framing"))
            ApplyDateCamera(DateSessionManager.DatePhase.Arrival);

        if (Button("Apply Kitchen Camera", "Snap to kitchen framing"))
            ApplyDateCamera(DateSessionManager.DatePhase.BackgroundJudging);

        if (Button("Apply Couch Camera", "Snap to couch framing"))
            ApplyDateCamera(DateSessionManager.DatePhase.Reveal);

        if (Button("Release Camera", "Return to free browse"))
        {
            if (DateSessionManager.Instance != null)
                DateSessionManager.Instance.ReleasePhaseCamera();
        }

        EditorGUILayout.Space(10);

        // ── Quick Tools ──
        EditorGUILayout.LabelField("Quick Tools", EditorStyles.boldLabel);

        if (Button("Force Doorbell", "Trigger date arrival now"))
        {
            if (DateSessionManager.Instance != null)
                DateSessionManager.Instance.OnDateCharacterArrived();
        }

        EditorGUILayout.HelpBox("Press F1 in-game for the live debug overlay.", MessageType.None);

        GUI.enabled = true;
        EditorGUILayout.EndScrollView();

        // Repaint while playing so state labels stay current
        if (playing)
            Repaint();
    }

    private static bool Button(string label, string tooltip)
    {
        return GUILayout.Button(new GUIContent(label, tooltip), GUILayout.Height(26));
    }

    private static void JumpToDayPhase(DayPhaseManager.DayPhase phase)
    {
        var dpm = DayPhaseManager.Instance;
        if (dpm == null)
        {
            Debug.LogWarning("[PhaseJumper] DayPhaseManager not found.");
            return;
        }

        // Clean up active date if jumping away from DateInProgress
        if (dpm.CurrentPhase == DayPhaseManager.DayPhase.DateInProgress
            && phase != DayPhaseManager.DayPhase.DateInProgress)
        {
            var dsm = DateSessionManager.Instance;
            if (dsm != null && dsm.CurrentState != DateSessionManager.SessionState.Idle)
            {
                dsm.ReleasePhaseCamera();
                // Force state back to idle via reflection (no public ForceIdle)
                SetPrivateField(dsm, "_state", DateSessionManager.SessionState.Idle);
                SetPrivateField(dsm, "_datePhase", DateSessionManager.DatePhase.None);
            }
            WateringManager.Instance?.ForceIdle();
        }

        dpm.SetPhase(phase);
        Debug.Log($"[PhaseJumper] Jumped to {phase}");
    }

    private static void JumpToDatePhase(DateSessionManager.DatePhase datePhase)
    {
        var dsm = DateSessionManager.Instance;
        if (dsm == null)
        {
            Debug.LogWarning("[PhaseJumper] DateSessionManager not found.");
            return;
        }

        // Make sure we're in DateInProgress day phase
        var dpm = DayPhaseManager.Instance;
        if (dpm != null && dpm.CurrentPhase != DayPhaseManager.DayPhase.DateInProgress)
            dpm.SetPhase(DayPhaseManager.DayPhase.DateInProgress);

        // Set private fields directly (no public setters — debug only)
        SetPrivateField(dsm, "_datePhase", datePhase);
        if (dsm.CurrentState == DateSessionManager.SessionState.Idle)
            SetPrivateField(dsm, "_state", DateSessionManager.SessionState.DateInProgress);

        // Move Nema + apply camera
        NemaController.Instance?.MoveToDatePhase(datePhase);
        dsm.ApplyPhaseCamera(datePhase);

        // Unlock camera pan in case it was suppressed
        if (ApartmentManager.Instance != null)
            ApartmentManager.Instance.SuppressPan = false;

        Debug.Log($"[PhaseJumper] Jumped to date phase: {datePhase}");
    }

    private static void ApplyDateCamera(DateSessionManager.DatePhase phase)
    {
        if (DateSessionManager.Instance != null)
            DateSessionManager.Instance.ApplyPhaseCamera(phase);
    }

    /// <summary>
    /// Skip forward through the current date blocker:
    /// - If Continue/Serve button is showing → click it
    /// - If choosing a glass → select the first one
    /// - If choosing serve glass → serve the first filled one
    /// </summary>
    private static void SkipForward()
    {
        // Click Continue/Serve button if visible
        if (PhaseContinueButton.Instance != null
            && PhaseContinueButton.Instance.gameObject.activeInHierarchy)
        {
            // Invoke the button's click via reflection (OnButtonClicked is private)
            var method = typeof(PhaseContinueButton).GetMethod("OnButtonClicked",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (method != null)
            {
                method.Invoke(PhaseContinueButton.Instance, null);
                Debug.Log("[PhaseJumper] Skipped: clicked Continue/Serve button.");
                return;
            }
        }

        // DrinkPourManager states
        var dpm = DrinkPourManager.Instance;
        if (dpm != null)
        {
            if (dpm.CurrentState == DrinkPourManager.State.ChoosingGlass)
            {
                // Select the first glass
                var glasses = DrinkGlass.All;
                for (int i = 0; i < glasses.Count; i++)
                {
                    if (glasses[i] != null)
                    {
                        dpm.SelectGlass(glasses[i]);
                        Debug.Log($"[PhaseJumper] Skipped: selected glass {glasses[i].name}.");
                        return;
                    }
                }
            }

            if (dpm.CurrentState == DrinkPourManager.State.ChoosingServeGlass)
            {
                // Serve the first glass that has liquid
                var glasses = DrinkGlass.All;
                for (int i = 0; i < glasses.Count; i++)
                {
                    if (glasses[i] != null && glasses[i].TotalFill > 0f)
                    {
                        dpm.ServeGlass(glasses[i]);
                        Debug.Log($"[PhaseJumper] Skipped: served glass {glasses[i].name}.");
                        return;
                    }
                }
                // No filled glass — serve first anyway
                if (glasses.Count > 0 && glasses[0] != null)
                {
                    dpm.ServeGlass(glasses[0]);
                    Debug.Log("[PhaseJumper] Skipped: force-served empty glass.");
                    return;
                }
            }
        }

        Debug.Log("[PhaseJumper] Skip: nothing to skip right now.");
    }

    private static void SetPrivateField<T>(object target, string fieldName, T value)
    {
        var field = target.GetType().GetField(fieldName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null)
            field.SetValue(target, value);
    }
}
