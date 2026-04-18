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

        // ── Date Phases ──
        EditorGUILayout.LabelField("Date Phases", EditorStyles.boldLabel);

        bool hasDate = playing && DateSessionManager.Instance != null
            && DateSessionManager.Instance.CurrentDate != null;

        if (!hasDate && playing)
        {
            EditorGUILayout.HelpBox(
                "No date scheduled. Start Exploration first, then schedule a date " +
                "or use Quick-Boot with a date pre-selected.", MessageType.Warning);
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

    private static void SetPrivateField<T>(object target, string fieldName, T value)
    {
        var field = target.GetType().GetField(fieldName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null)
            field.SetValue(target, value);
    }
}
