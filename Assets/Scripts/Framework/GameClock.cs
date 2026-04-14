using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using TMPro;

/// <summary>
/// Scene-scoped singleton that drives the in-game calendar and clock.
/// Ticks game hours at a configurable pace and feeds MoodMachine a "TimeOfDay" source.
/// </summary>
public class GameClock : MonoBehaviour
{
    public static GameClock Instance { get; private set; }

    // ──────────────────────────────────────────────────────────────
    // Configuration
    // ──────────────────────────────────────────────────────────────
    [Header("Calendar")]
    [Tooltip("Total days in the game calendar. 0 = infinite (no cap).")]
    [SerializeField] private int totalDays = 7;

    [Tooltip("Hour the player wakes up each morning.")]
    [SerializeField] private float startHour = 8f;

    [Tooltip("Hour forced bedtime triggers (use >24 for after-midnight, e.g. 26 = 2am).")]
    [SerializeField] private float bedtimeHour = 26f;

    [Header("Pace")]
    [Tooltip("Real-world seconds per one game hour.")]
    [SerializeField] private float realSecondsPerGameHour = 60f;

    [Header("Time-of-Day Mood")]
    [Tooltip("Maps game hour (0-24) to mood value (0-1) for MoodMachine.")]
    [SerializeField] private AnimationCurve timeOfDayMoodCurve = new AnimationCurve(
        new Keyframe(0f, 0.9f),    // midnight — very dark mood
        new Keyframe(6f, 0.4f),    // pre-dawn
        new Keyframe(8f, 0f),      // morning — bright, clear
        new Keyframe(12f, 0.05f),  // noon
        new Keyframe(16f, 0.1f),   // afternoon
        new Keyframe(18f, 0.4f),   // golden hour — mood shifts
        new Keyframe(20f, 0.7f),   // evening — noticeably darker
        new Keyframe(22f, 0.85f),  // late evening — player should want lights
        new Keyframe(24f, 0.9f)    // night
    );

    [Header("References")]
    [Tooltip("DayManager for advancing days.")]
    [SerializeField] private DayManager dayManager;

    [Tooltip("Dream interstitial text shown during sleep transition.")]
    [SerializeField] private TMP_Text _dreamText;

    [Header("Events")]
    public UnityEvent<float> OnHourChanged;
    public UnityEvent OnForcedBedtime;
    public UnityEvent OnDayStarted;
    public UnityEvent OnCalendarComplete;

    // ──────────────────────────────────────────────────────────────
    // Runtime state
    // ──────────────────────────────────────────────────────────────
    private float _currentHour;
    private int _currentDay = 1;
    private bool _isSleeping;

    // Demo timer (real-time countdown)
    private float _demoTimeLimitSeconds;
    private float _demoTimeRemaining;

    // ──────────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────────
    public float CurrentHour => _currentHour;
    public int CurrentDay => _currentDay;
    public bool IsSleeping => _isSleeping;
    public float NormalizedTimeOfDay => Mathf.Repeat(_currentHour, 24f) / 24f;
    public float DemoTimeRemaining => _demoTimeRemaining;
    public bool IsDemoMode => _demoTimeLimitSeconds > 0f;

    /// <summary>Restore day and hour from save data.</summary>
    public void RestoreFromSave(int day, float hour)
    {
        _currentDay = day;
        _currentHour = hour;
        Debug.Log($"[GameClock] Restored to Day {_currentDay} at {_currentHour:F1}");
    }

    // ──────────────────────────────────────────────────────────────
    // Lifecycle
    // ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[GameClock] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _currentHour = startHour;

        // Apply game mode pacing if set from main menu
        if (MainMenuManager.ActiveConfig != null)
        {
            totalDays = MainMenuManager.ActiveConfig.totalDays;
            realSecondsPerGameHour = MainMenuManager.ActiveConfig.realSecondsPerGameHour;
            _demoTimeLimitSeconds = MainMenuManager.ActiveConfig.demoTimeLimitSeconds;
        }

        if (_demoTimeLimitSeconds > 0f)
        {
            _demoTimeRemaining = _demoTimeLimitSeconds;
            Debug.Log($"[GameClock] Demo timer started: {_demoTimeLimitSeconds}s");
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        // Demo timer always ticks (real time) — even during sleep sequences
        if (_demoTimeLimitSeconds > 0f && _demoTimeRemaining > 0f)
        {
            _demoTimeRemaining -= Time.unscaledDeltaTime;
            if (_demoTimeRemaining <= 0f)
            {
                _demoTimeRemaining = 0f;
                _isSleeping = true;
                Debug.Log("[GameClock] Demo timer expired!");
                OnCalendarComplete?.Invoke();
                return;
            }
        }

        if (_isSleeping) return;
        if (DateDebugOverlay.IsTimePaused) return;

        _currentHour += Time.deltaTime / realSecondsPerGameHour;

        // Feed mood machine with time-of-day value
        float displayHour = Mathf.Repeat(_currentHour, 24f);
        MoodMachine.Instance?.SetSource("TimeOfDay", timeOfDayMoodCurve.Evaluate(displayHour));

        OnHourChanged?.Invoke(_currentHour);

        // Don't force bedtime during flower trimming — that path has its own
        // dream screen + day advance in FlowerTrimmingTransition.
        bool inFlowerTrim = DayPhaseManager.Instance != null
            && DayPhaseManager.Instance.CurrentPhase == DayPhaseManager.DayPhase.FlowerTrimming;
        if (_currentHour >= bedtimeHour && !inFlowerTrim)
        {
            OnForcedBedtime?.Invoke();
            GoToBed();
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Public Methods
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Player-initiated sleep. Ends any active date, advances the day.
    /// </summary>
    public void GoToBed()
    {
        if (_isSleeping) return;
        StartCoroutine(SleepSequence());
    }

    /// <summary>
    /// Advance to next day without any fading or coroutines.
    /// Called by DayPhaseManager after flower trimming (which handles its own fade).
    /// </summary>
    public void AdvanceDayDirect()
    {
        _currentDay++;
        _currentHour = startHour;

        if (totalDays > 0 && _currentDay > totalDays)
        {
            _isSleeping = true;
            Debug.Log("[GameClock] Calendar complete!");
            OnCalendarComplete?.Invoke();
            return;
        }

        dayManager?.AdvanceDay();
        _isSleeping = false;
        OnDayStarted?.Invoke();
        Debug.Log($"[GameClock] Day {_currentDay} started at {_currentHour:F0}:00 (direct advance)");
    }

    /// <summary>Debug shortcut to immediately advance to next day.</summary>
    public void ForceNewDay()
    {
        if (_isSleeping) return;
        StartCoroutine(SleepSequence());
    }

    private IEnumerator SleepSequence()
    {
        _isSleeping = true;
        Debug.Log($"[GameClock] Going to bed on day {_currentDay} at hour {_currentHour:F1}");

        // End any active date session
        DateSessionManager.Instance?.EndDate();

        // Reset all UI / station state before fading out
        FridgeController.Instance?.ForceClose();
        DrinkPourManager.Instance?.ForceIdle();
        RecordSlot.Instance?.Stop();
        DateEndScreen.Instance?.Dismiss();

        // Dream interstitial — psychedelic overlay instead of black screen
        if (DreamScreen.Instance != null)
        {
            yield return DreamScreen.Instance.Play();
        }
        else
        {
            // Fallback: simple fade to black
            if (ScreenFade.Instance != null)
                yield return ScreenFade.Instance.FadeOut(1f);
            if (_dreamText != null)
            {
                _dreamText.text = "Nema drifts to sleep...";
                _dreamText.gameObject.SetActive(true);
            }
            yield return new WaitForSecondsRealtime(3f);
            if (_dreamText != null)
                _dreamText.gameObject.SetActive(false);
        }

        // 5. Auto-save before advancing
        AutoSaveController.Instance?.PerformSave("end_of_day");

        // 6. Advance day and reset clock
        _currentDay++;
        _currentHour = startHour;

        // Check calendar completion BEFORE advancing to next day's morning
        if (totalDays > 0 && _currentDay > totalDays)
        {
            _isSleeping = true; // block further ticking
            Debug.Log("[GameClock] Calendar complete!");
            OnCalendarComplete?.Invoke();
            // Skip AdvanceDay — no next morning needed
            yield break;
        }

        // 7. AdvanceDay fires OnNewNewspaper → DayPhaseManager.EnterMorning
        //    which already fades in from black
        dayManager?.AdvanceDay();

        _isSleeping = false;
        OnDayStarted?.Invoke();
        Debug.Log($"[GameClock] Day {_currentDay} started at {_currentHour:F0}:00");
    }
}
