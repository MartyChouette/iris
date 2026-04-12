using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.Cinemachine;
using TMPro;

/// <summary>
/// Single authority for daily phase transitions, camera priorities, and screen fades.
/// Phases: Morning (newspaper) → Exploration (free-roam) → DateInProgress → Evening.
///
/// ── Transition quick-reference ──────────────────────────────────────
///
///  MORNING (OnNewNewspaper event)
///    1. Suppress browse camera (priority 0)
///    2. Raise read camera (priority 30)
///    3. Enable NewspaperManager + show newspaper HUD
///    4. Hide apartment UI
///
///  EXPLORATION (OnNewspaperDone event)
///    1. Fade to black               (0.5 s)
///    2. Lower read camera            (priority 0)
///    3. Raise browse camera           (priority 20 via ApartmentManager)
///    4. Toss newspaper to coffee table
///    5. Disable NewspaperManager
///    6. Show apartment UI, hide newspaper HUD
///    7. Spawn daily stains
///    8. Fade in from black           (0.5 s)
///
///  DATE IN PROGRESS (OnDateSessionStarted event)
///    — DateSessionManager / DateCharacterController handle their own flow
///
///  EVENING (OnDateSessionEnded event)
///    — DateEndScreen handles its own flow
/// ─────────────────────────────────────────────────────────────────────
/// </summary>
public class DayPhaseManager : MonoBehaviour
{
    public enum DayPhase { Morning, Exploration, DateInProgress, FlowerTrimming, Evening }

    public static DayPhaseManager Instance { get; private set; }

    [Header("Current Phase")]
    [SerializeField] private DayPhase _currentPhase = DayPhase.Exploration;

    [Header("References")]
    [Tooltip("NewspaperManager to enable/disable at phase transitions.")]
    [SerializeField] private NewspaperManager _newspaperManager;

    [Tooltip("Read camera — raised to priority 30 during Morning, lowered during Exploration.")]
    [SerializeField] private CinemachineCamera _readCamera;

    [Tooltip("CinemachineBrain on the main camera — used for hard-cut during perspective→ortho switch.")]
    [SerializeField] private CinemachineBrain brain;

    [Tooltip("Transform for the tossed newspaper position on the coffee table.")]
    [SerializeField] private Transform _tossedNewspaperPosition;

    [Tooltip("Authored mess spawner triggered at exploration start (stains + objects).")]
    [SerializeField] private AuthoredMessSpawner _authoredMessSpawner;

    [Tooltip("Daily mess spawner for entrance item misplacement.")]
    [SerializeField] private DailyMessSpawner _entranceMessSpawner;

    [Tooltip("Bridge for loading flower trimming scene after successful dates.")]
    [SerializeField] private FlowerTrimmingBridge _flowerTrimmingBridge;

    [Tooltip("Apartment UI canvas root — hidden during Morning, shown during Exploration.")]
    [SerializeField] private GameObject _apartmentUI;

    [Tooltip("Newspaper HUD root — shown during Morning, hidden during Exploration.")]
    [SerializeField] private GameObject _newspaperHUD;

    [Header("Fade Timing")]
    [Tooltip("Duration of fade-to-black and fade-from-black in seconds.")]
    [SerializeField] private float _fadeDuration = 0.5f;

    [Header("Preparation Timer")]
    [Tooltip("Duration of the preparation phase in seconds (hidden from player).")]
    [SerializeField] private float _prepDuration = 900f;

    [Tooltip("TMP_Text displaying the countdown timer.")]
    [SerializeField] private TMP_Text _prepTimerText;

    [Tooltip("Panel root for the prep timer UI.")]
    [SerializeField] private GameObject _prepTimerPanel;

    [Header("Go to Bed")]
    [Tooltip("Panel with Go to Bed button — shown only during Evening phase.")]
    [SerializeField] private GameObject _goToBedPanel;

#if UNITY_EDITOR
    [Header("Editor Quick-Boot (Editor only — stripped from builds)")]
    [Tooltip("If true, pressing Play in the editor skips the normal morning→exploration→date flow and boots straight into the chosen phase with the chosen date pre-selected. Completely absent in built players.")]
    [SerializeField] private bool _editorQuickBoot = true;

    [Tooltip("Which phase to jump to on editor Play. Default is Exploration so you land in the pre-date clean-up phase with Nema already in her lean pose.")]
    [SerializeField] private DayPhase _editorBootPhase = DayPhase.Exploration;

    [Tooltip("Which date to pre-select so phase-based systems know who's coming. Drag the DatePersonalDefinition asset (e.g. Date_Paris).")]
    [SerializeField] private DatePersonalDefinition _editorBootDate;
#endif

    [Header("Audio")]
    [Tooltip("SFX played at the start of a new day (morning transition).")]
    [SerializeField] private AudioClip nextDaySFX;

    [Tooltip("SFX played when date arrival nudge appears.")]
    [SerializeField] private AudioClip timerWarningSFX;

    [Tooltip("Optional ambience loop for the morning newspaper phase. If null, MoodMachine ambient runs.")]
    [SerializeField] private AudioClip _morningAmbienceClip;

    [Tooltip("Optional ambience loop for exploration/prep phase. If null, MoodMachine ambient runs.")]
    [SerializeField] private AudioClip _explorationAmbienceClip;

    [Header("Events")]
    public UnityEvent<int> OnPhaseChanged;

    private const int PriorityActive = 30;
    private const int PriorityInactive = 0;

    private float _prepTimer;
    private bool _prepTimerActive;
    private bool _nudgeShown;
    private Coroutine _nudgeHideCoroutine;

    public DayPhase CurrentPhase => _currentPhase;
    public float PrepTimer => _prepTimer;
    public bool PrepTimerActive => _prepTimerActive;

    /// <summary>True during Exploration, DateInProgress, or Evening — stations can accept input.</summary>
    public bool IsInteractionPhase => _currentPhase == DayPhase.Exploration
                                   || _currentPhase == DayPhase.DateInProgress
                                   || _currentPhase == DayPhase.Evening;

    /// <summary>True only during DateInProgress — drink making is allowed.</summary>
    public bool IsDrinkPhase => _currentPhase == DayPhase.DateInProgress;

    // ─── Lifecycle ──────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[DayPhaseManager] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (brain == null)
            brain = FindAnyObjectByType<CinemachineBrain>();
    }

    private void Start()
    {
        // Runtime subscription for DateSessionManager events
        // (multi-param UnityEvents can't be wired via UnityEventTools in editor)
        if (DateSessionManager.Instance != null)
        {
            DateSessionManager.Instance.OnDateSessionStarted.AddListener(EnterDateInProgress);
            DateSessionManager.Instance.OnDateSessionEnded.AddListener(EnterEvening);
        }

        // Apply game mode prep duration if set from main menu
        if (MainMenuManager.ActiveConfig != null)
            _prepDuration = MainMenuManager.ActiveConfig.prepDuration;

        // Subscribe to calendar completion
        if (GameClock.Instance != null)
            GameClock.Instance.OnCalendarComplete.AddListener(OnCalendarComplete);

#if UNITY_EDITOR
        // Editor quick-boot: skip the normal day flow and jump straight to a
        // chosen phase for fast iteration. Entirely stripped from builds via
        // the #if, so the demo build still follows the normal morning →
        // exploration → date session → evening flow.
        if (_editorQuickBoot)
            TryEditorQuickBoot();
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// Editor-only shortcut: pre-select the chosen date on DateSessionManager
    /// (so phase-based systems that read CurrentDate behave as if a session
    /// is set up) and jump straight to the target phase. Does not run a real
    /// date session — it just fakes enough state for phase testing.
    /// Default target is Exploration (pre-date clean-up) so Nema appears in
    /// her lean pose and the editor tester can iterate on the clean-up UX
    /// without walking through morning → newspaper → call-date flow.
    /// </summary>
    private void TryEditorQuickBoot()
    {
        if (_editorBootDate != null && DateSessionManager.Instance != null)
        {
            // Use reflection so we don't need a new public setter on DSM — this
            // is editor-only so the perf cost is irrelevant.
            var field = typeof(DateSessionManager).GetField("_currentDate",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
                field.SetValue(DateSessionManager.Instance, _editorBootDate);

            Debug.Log($"[DayPhaseManager] Editor quick-boot: date set to '{_editorBootDate.characterName}'.");
        }

        // Call the right entry point for the target phase so all side effects
        // (UI toggles, fade-in, state machine) run normally.
        switch (_editorBootPhase)
        {
            case DayPhase.Morning:
                EnterMorning();
                break;
            case DayPhase.Exploration:
                EnterExploration();
                break;
            case DayPhase.Evening:
                EnterEvening(_editorBootDate, 100f);
                break;
            case DayPhase.FlowerTrimming:
                SetPhase(DayPhase.FlowerTrimming);
                break;
            case DayPhase.DateInProgress:
                SetPhase(DayPhase.DateInProgress);
                break;
        }

        Debug.Log($"[DayPhaseManager] Editor quick-boot: jumped to {_editorBootPhase}.");
    }
#endif

    private void OnDestroy()
    {
        if (DateSessionManager.Instance != null)
        {
            DateSessionManager.Instance.OnDateSessionStarted.RemoveListener(EnterDateInProgress);
            DateSessionManager.Instance.OnDateSessionEnded.RemoveListener(EnterEvening);
        }
        if (GameClock.Instance != null)
            GameClock.Instance.OnCalendarComplete.RemoveListener(OnCalendarComplete);
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!_prepTimerActive) return;
        if (DateDebugOverlay.IsTimePaused) return;

        _prepTimer -= Time.deltaTime;

        // Nudge at 60 seconds remaining — "your date will arrive soon"
        if (!_nudgeShown && _prepTimer <= 60f && _prepTimer > 0f)
        {
            _nudgeShown = true;
            ShowArrivalNudge();
        }

        if (_prepTimer <= 0f)
        {
            _prepTimerActive = false;
            OnPrepTimerExpired();
        }
    }

    // ─── Prep Timer ──────────────────────────────────────────────────

    private void StartPrepTimer()
    {
        float multiplier = AccessibilitySettings.TimerMultiplier;

        // 0 = unlimited — no timer
        if (multiplier <= 0f)
        {
            _prepTimerActive = false;
            if (_prepTimerPanel != null) _prepTimerPanel.SetActive(false);
            Debug.Log("[DayPhaseManager] Prep timer disabled (unlimited mode).");
            return;
        }

        _prepTimer = _prepDuration * multiplier;
        _prepTimerActive = true;
        _nudgeShown = false;

        // Timer is hidden — player doesn't see a countdown
        if (_prepTimerPanel != null) _prepTimerPanel.SetActive(false);
        Debug.Log($"[DayPhaseManager] Prep timer started (hidden): {_prepTimer}s (base {_prepDuration} x {multiplier}).");
    }

    private void StopPrepTimer()
    {
        _prepTimerActive = false;
        if (_prepTimerPanel != null) _prepTimerPanel.SetActive(false);
        if (_nudgeHideCoroutine != null)
        {
            StopCoroutine(_nudgeHideCoroutine);
            _nudgeHideCoroutine = null;
        }
    }

    private void ShowArrivalNudge()
    {
        if (timerWarningSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(timerWarningSFX);

        // Show the nudge — top-third of screen, large and prominent
        if (_prepTimerText != null)
        {
            _prepTimerText.text = "Your date will arrive soon!";
            _prepTimerText.fontSize = 44f;
        }
        if (_prepTimerPanel != null)
        {
            var rt = _prepTimerPanel.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(650f, 90f);
                rt.anchoredPosition = new Vector2(0f, -80f);
            }

            // Ensure panel background is visible
            var panelImg = _prepTimerPanel.GetComponent<UnityEngine.UI.Image>();
            if (panelImg != null)
                panelImg.color = new Color(0.08f, 0.07f, 0.06f, 0.85f);

            _prepTimerPanel.SetActive(true);

            // Click anywhere on the panel to dismiss early
            var btn = _prepTimerPanel.GetComponent<UnityEngine.UI.Button>();
            if (btn == null) btn = _prepTimerPanel.AddComponent<UnityEngine.UI.Button>();
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(DismissNudge);
        }

        _nudgeHideCoroutine = StartCoroutine(HideNudgeAfterDelay(5f));

        // If the menu music is still playing (player hasn't put on a record),
        // fade it out as a subtle hint to choose something.
        if (MusicDirector.Instance != null && MusicDirector.Instance.IsMenuMusicPlaying)
            MusicDirector.Instance.FadeOutMenuMusic();

        Debug.Log("[DayPhaseManager] Date arrival nudge shown.");
    }

    private void DismissNudge()
    {
        if (_nudgeHideCoroutine != null)
        {
            StopCoroutine(_nudgeHideCoroutine);
            _nudgeHideCoroutine = null;
        }
        if (_prepTimerPanel != null) _prepTimerPanel.SetActive(false);
    }

    private IEnumerator HideNudgeAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (_prepTimerPanel != null) _prepTimerPanel.SetActive(false);
        _nudgeHideCoroutine = null;
    }

    private void OnPrepTimerExpired()
    {
        StopPrepTimer();
        Debug.Log("[DayPhaseManager] Prep timer expired — doorbell!");

        // Date arrives via doorbell
        PhoneController.Instance?.PlayDoorbell();
    }

    /// <summary>Called by PhoneController when player clicks phone to end prep early.</summary>
    public void EndPrepEarly()
    {
        StopPrepTimer();
        Debug.Log("[DayPhaseManager] Prep ended early by player.");
    }

    // ─── Public entry points (called by events) ─────────────────────

    /// <summary>Called by DayManager.OnNewNewspaper event.</summary>
    public void EnterMorning()
    {
        bool isDemo = MainMenuManager.ActiveConfig != null;
        int day = GameClock.Instance != null ? GameClock.Instance.CurrentDay : 1;

        // Demo day 2+: skip newspaper, go straight to cleanup exploration
        if (isDemo && day >= 2)
        {
            Debug.Log("[DayPhaseManager] Demo cleanup day — skipping newspaper.");
            StartCoroutine(DemoCleanupTransition());
            return;
        }

        // Demo day 1: skip newspaper, show info card, auto-select Paris
        if (isDemo && day == 1)
        {
            Debug.Log("[DayPhaseManager] Demo day 1 — showing date info card instead of newspaper.");
            StartCoroutine(DemoDay1Transition());
            return;
        }

        SetPhase(DayPhase.Morning);
    }

    /// <summary>Called by NewspaperManager.OnNewspaperDone event.</summary>
    public void EnterExploration()
    {
        SetPhase(DayPhase.Exploration);
    }

    /// <summary>Called by DateSessionManager.OnDateSessionStarted event.</summary>
    public void EnterDateInProgress(DatePersonalDefinition _)
    {
        SetPhase(DayPhase.DateInProgress);
    }

    /// <summary>Called by DateSessionManager.OnDateSessionEnded event.</summary>
    public void EnterEvening(DatePersonalDefinition _, float __)
    {
        // If there's a pending flower trim from a successful date, do it first
        if (DateSessionManager.PendingFlowerTrim)
        {
            SetPhase(DayPhase.FlowerTrimming);
            return;
        }

        SetPhase(DayPhase.Evening);
    }

    // ─── Save/Load ─────────────────────────────────────────────────

    /// <summary>
    /// Instantly restore to a saved phase without transition coroutines.
    /// Sets cameras, UI, and manager states to match the target phase.
    /// </summary>
    public void RestoreToPhase(DayPhase phase)
    {
        _currentPhase = phase;
        StopPrepTimer();

        // Go to Bed panel
        if (_goToBedPanel != null)
            _goToBedPanel.SetActive(phase == DayPhase.Evening);

        switch (phase)
        {
            case DayPhase.Morning:
                // Suspend ortho preset so read camera displays correctly
                CameraTestController.Instance?.SuspendPreset();
                // Newspaper is showing — raise read camera, suppress browse
                if (ApartmentManager.Instance != null)
                    ApartmentManager.Instance.SetBrowseCameraActive(false);
                if (_readCamera != null)
                    _readCamera.Priority = PriorityActive;
                if (_newspaperManager != null)
                    _newspaperManager.enabled = true;
                if (_apartmentUI != null) _apartmentUI.SetActive(false);
                if (_newspaperHUD != null) _newspaperHUD.SetActive(true);
                break;

            case DayPhase.Exploration:
            case DayPhase.DateInProgress:
            case DayPhase.FlowerTrimming:
            case DayPhase.Evening:
                // Free-roam — browse camera active, newspaper off
                if (_readCamera != null)
                    _readCamera.Priority = PriorityInactive;
                if (ApartmentManager.Instance != null)
                    ApartmentManager.Instance.SetBrowseCameraActive(true);
                if (_newspaperManager != null)
                    _newspaperManager.enabled = false;
                if (_apartmentUI != null) _apartmentUI.SetActive(true);
                if (_newspaperHUD != null) _newspaperHUD.SetActive(false);

                // Toss newspaper to coffee table
                if (_tossedNewspaperPosition != null && _newspaperManager != null
                    && _newspaperManager.NewspaperTransform != null)
                {
                    _newspaperManager.NewspaperTransform.position = _tossedNewspaperPosition.position;
                    _newspaperManager.NewspaperTransform.rotation = _tossedNewspaperPosition.rotation;
                }
                break;
        }

        // Fade in immediately
        ScreenFade.Instance?.FadeIn(_fadeDuration);

        Debug.Log($"[DayPhaseManager] Restored to phase {phase}.");
        OnPhaseChanged?.Invoke((int)phase);
    }

    // ─── Phase dispatch ─────────────────────────────────────────────

    public void SetPhase(DayPhase phase)
    {
        if (_currentPhase == phase) return;

        _currentPhase = phase;
        Debug.Log($"[DayPhaseManager] Phase → {phase}");

        // Close all station UIs/HUDs on any phase change
        DismissAllStationUI();

        // Go to Bed panel is only visible during Evening
        if (_goToBedPanel != null)
            _goToBedPanel.SetActive(phase == DayPhase.Evening);
        switch (phase)
        {
            case DayPhase.Morning:
                RecordSlot.Instance?.Stop(); // full stop between days
                AudioManager.Instance?.SetNonMusicMix(1f, 0.5f);
                StartCoroutine(MorningTransition());
                break;
            case DayPhase.Exploration:
                AudioManager.Instance?.UnduckMusic(1f);
                AudioManager.Instance?.SetNonMusicMix(1f, 0.5f);
                StartCoroutine(ExplorationTransition());
                break;
            case DayPhase.DateInProgress:
                AudioManager.Instance?.DuckMusic(0.15f, 0.5f);
                AudioManager.Instance?.SetNonMusicMix(0.85f, 0.5f); // non-music -15%
                StopPrepTimer();
                break;
            case DayPhase.FlowerTrimming:
                AudioManager.Instance?.DuckMusic(0.1f, 0.5f);
                AudioManager.Instance?.SetNonMusicMix(0.85f, 0.5f); // match apartment levels
                StartCoroutine(FlowerTrimmingTransition());
                break;
            case DayPhase.Evening:
                AudioManager.Instance?.UnduckMusic(1.5f);
                AudioManager.Instance?.SetNonMusicMix(1f, 1f);
                break;
        }

        OnPhaseChanged?.Invoke((int)phase);
    }

    // ═══════════════════════════════════════════════════════════════
    // MORNING TRANSITION
    // ═══════════════════════════════════════════════════════════════

    private IEnumerator MorningTransition()
    {
        if (nextDaySFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(nextDaySFX);

        // Cross-fade menu music out as game ambience comes in
        MusicDirector.Instance?.FadeOutMenuMusic();

        // Phase ambience override (gentle newspaper-reading tone)
        if (_morningAmbienceClip != null && AudioManager.Instance != null)
            AudioManager.Instance.PlayAmbience(_morningAmbienceClip, 0.5f);

        // 0. Suspend ortho preset so Cinemachine can blend to perspective read camera
        CameraTestController.Instance?.SuspendPreset();

        // 1. Suppress browse camera so it doesn't compete with read camera
        if (ApartmentManager.Instance != null)
            ApartmentManager.Instance.SetBrowseCameraActive(false);

        // 2. Raise read camera
        if (_readCamera != null)
            _readCamera.Priority = PriorityActive;

        // 3. Enable newspaper manager so it can populate ads
        if (_newspaperManager != null)
            _newspaperManager.enabled = true;

        // 4. UI: hide apartment browse, show newspaper HUD
        if (_apartmentUI != null)
            _apartmentUI.SetActive(false);
        if (_newspaperHUD != null)
            _newspaperHUD.SetActive(true);

        // 5. Fade in from black (scene started fully black)
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeIn(_fadeDuration);
    }

    // ═══════════════════════════════════════════════════════════════
    // EXPLORATION TRANSITION
    // ═══════════════════════════════════════════════════════════════

    private IEnumerator ExplorationTransition()
    {
        // Fade to black → switch cameras (perspective→ortho) while hidden → fade in.
        // Cinemachine can't cleanly blend between perspective and orthographic,
        // so we hide the switch behind a black screen.

        // 1. Fade to black
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeOut(_fadeDuration);

        // ── While screen is black ────────────────────────────────────

        // 2. Lower read camera → browse camera wins via priority
        if (_readCamera != null)
            _readCamera.Priority = PriorityInactive;

        // 3. Raise browse camera + restore ortho lens from default preset
        if (ApartmentManager.Instance != null)
            ApartmentManager.Instance.SetBrowseCameraActive(true);

        // 4. Restore suspended camera preset (ortho mode, volume profile, light overrides)
        CameraTestController.Instance?.RestorePreset();

        // 5. Force a hard cut so Cinemachine doesn't try to blend
        if (brain != null)
        {
            var savedBlend = brain.DefaultBlend;
            brain.DefaultBlend = new CinemachineBlendDefinition(
                CinemachineBlendDefinition.Styles.Cut, 0f);
            // Let one frame pass so the cut takes effect
            yield return null;
            brain.DefaultBlend = savedBlend;
        }

        // 6. Toss newspaper to coffee table
        if (_tossedNewspaperPosition != null)
        {
            var surface = _newspaperManager != null ? _newspaperManager.NewspaperTransform : null;
            if (surface != null)
            {
                surface.position = _tossedNewspaperPosition.position;
                surface.rotation = _tossedNewspaperPosition.rotation;
            }
        }

        // 7. Disable newspaper manager (done for the day)
        if (_newspaperManager != null)
            _newspaperManager.enabled = false;

        // 8. UI: show apartment browse, hide newspaper HUD
        if (_apartmentUI != null)
            _apartmentUI.SetActive(true);
        if (_newspaperHUD != null)
            _newspaperHUD.SetActive(false);

        // 9. Spawn authored messes (stains + objects) + misplace entrance items
        if (_authoredMessSpawner != null)
            _authoredMessSpawner.SpawnDailyMess();
        if (_entranceMessSpawner != null)
            _entranceMessSpawner.SpawnDailyMess();

        // 10. Swap to exploration ambience (or let MoodMachine take over if null)
        if (_explorationAmbienceClip != null && AudioManager.Instance != null)
            AudioManager.Instance.PlayAmbience(_explorationAmbienceClip, 0.5f);

        // ── Reveal ───────────────────────────────────────────────────

        // 11. Fade in from black (ortho browse camera is now active)
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeIn(_fadeDuration);

        // 12. Epic phase title drop over the live scene
        string dayLabel = GameClock.Instance != null
            ? $"Day {GameClock.Instance.CurrentDay}"
            : "Exploration";
        if (PhaseTitleDrop.Instance != null)
            yield return PhaseTitleDrop.Instance.Show(dayLabel);

        // 13. Flash visibility eyes on all items so the player sees what the date can notice
        VisibilityEyeIndicator.Instance?.FlashAllItems();

        // 14. Start preparation countdown
        StartPrepTimer();
    }

    // ═══════════════════════════════════════════════════════════════
    // DEMO DAY 1 (info card → auto-select date → exploration)
    // ═══════════════════════════════════════════════════════════════

    private IEnumerator DemoDay1Transition()
    {
        // 1. Get the tutorial date definition from DayManager's pool
        var tutorialDate = DayManager.Instance != null && DayManager.Instance.Pool != null
            ? DayManager.Instance.Pool.tutorialDate
            : null;

        if (tutorialDate == null)
        {
            Debug.LogWarning("[DayPhaseManager] No tutorial date found — falling back to normal morning.");
            SetPhase(DayPhase.Morning);
            yield break;
        }

        // 2. Set up browse camera (skip newspaper read camera entirely)
        if (_readCamera != null)
            _readCamera.Priority = PriorityInactive;
        if (ApartmentManager.Instance != null)
            ApartmentManager.Instance.SetBrowseCameraActive(true);
        CameraTestController.Instance?.RestorePreset();

        // Force hard cut
        if (brain != null)
        {
            var savedBlend = brain.DefaultBlend;
            brain.DefaultBlend = new CinemachineBlendDefinition(
                CinemachineBlendDefinition.Styles.Cut, 0f);
            yield return null;
            brain.DefaultBlend = savedBlend;
        }

        // 3. Disable newspaper, fade out menu music
        if (_newspaperManager != null)
            _newspaperManager.enabled = false;
        if (_newspaperHUD != null) _newspaperHUD.SetActive(false);
        if (_apartmentUI != null) _apartmentUI.SetActive(true);
        MusicDirector.Instance?.FadeOutMenuMusic();

        // 4. Fade in from black so the apartment is visible behind the card
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeIn(_fadeDuration);

        // 5. Build and show the info card overlay
        bool cardDismissed = false;
        var cardRoot = BuildDemoInfoCard(tutorialDate, () => cardDismissed = true);

        // 6. Wait for player to dismiss the card
        while (!cardDismissed)
            yield return null;

        // 7. Clean up the card
        if (cardRoot != null)
            Destroy(cardRoot);

        // 8. Auto-schedule the tutorial date
        DateSessionManager.Instance?.ScheduleDate(tutorialDate);
        PhoneController.Instance?.SetPendingDate(tutorialDate);

        // 9. Spawn day 1 messes + start exploration (with prep timer)
        _currentPhase = DayPhase.Exploration;
        Debug.Log("[DayPhaseManager] Phase → Exploration (demo day 1)");
        DismissAllStationUI();

        if (_authoredMessSpawner != null)
            _authoredMessSpawner.SpawnDailyMess();
        if (_entranceMessSpawner != null)
            _entranceMessSpawner.SpawnDailyMess();

        if (_explorationAmbienceClip != null && AudioManager.Instance != null)
            AudioManager.Instance.PlayAmbience(_explorationAmbienceClip, 0.5f);

        if (_goToBedPanel != null) _goToBedPanel.SetActive(false);

        OnPhaseChanged?.Invoke((int)DayPhase.Exploration);

        // 10. Start prep timer (date will arrive when it expires or player calls)
        StartPrepTimer();
    }

    private GameObject BuildDemoInfoCard(DatePersonalDefinition date, System.Action onDismiss)
    {
        // ── Canvas ──
        var canvasGO = new GameObject("DemoInfoCardCanvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasGO.AddComponent<GraphicRaycaster>();

        // ── Dim backdrop ──
        var dimGO = new GameObject("Dim");
        dimGO.transform.SetParent(canvasGO.transform, false);
        var dimRT = dimGO.AddComponent<RectTransform>();
        dimRT.anchorMin = Vector2.zero;
        dimRT.anchorMax = Vector2.one;
        dimRT.sizeDelta = Vector2.zero;
        var dimImg = dimGO.AddComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.5f);

        // ── Card panel ──
        var panelGO = new GameObject("CardPanel");
        panelGO.transform.SetParent(canvasGO.transform, false);
        var panelRT = panelGO.AddComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta = new Vector2(800f, 500f);
        var panelImg = panelGO.AddComponent<Image>();
        panelImg.color = new Color(0.95f, 0.92f, 0.85f, 1f); // eggshell/newspaper

        // ── Title (character name) ──
        var titleGO = new GameObject("Title");
        titleGO.transform.SetParent(panelGO.transform, false);
        var titleRT = titleGO.AddComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0f, 0.75f);
        titleRT.anchorMax = new Vector2(1f, 0.95f);
        titleRT.sizeDelta = Vector2.zero;
        titleRT.offsetMin = new Vector2(40f, 0f);
        titleRT.offsetMax = new Vector2(-40f, 0f);
        var titleTMP = titleGO.AddComponent<TextMeshProUGUI>();
        titleTMP.text = date.characterName;
        titleTMP.fontSize = 48f;
        titleTMP.fontStyle = FontStyles.Bold;
        titleTMP.alignment = TextAlignmentOptions.Center;
        titleTMP.color = new Color(0.15f, 0.12f, 0.1f);

        // ── Ad text ──
        var bodyGO = new GameObject("Body");
        bodyGO.transform.SetParent(panelGO.transform, false);
        var bodyRT = bodyGO.AddComponent<RectTransform>();
        bodyRT.anchorMin = new Vector2(0f, 0.25f);
        bodyRT.anchorMax = new Vector2(1f, 0.75f);
        bodyRT.sizeDelta = Vector2.zero;
        bodyRT.offsetMin = new Vector2(50f, 0f);
        bodyRT.offsetMax = new Vector2(-50f, 0f);
        var bodyTMP = bodyGO.AddComponent<TextMeshProUGUI>();
        bodyTMP.text = date.adText;
        bodyTMP.fontSize = 32f;
        bodyTMP.fontStyle = FontStyles.Italic;
        bodyTMP.alignment = TextAlignmentOptions.Center;
        bodyTMP.color = new Color(0.25f, 0.22f, 0.2f);
        bodyTMP.textWrappingMode = TextWrappingModes.Normal;

        // ── Start button ──
        var btnGO = new GameObject("StartButton");
        btnGO.transform.SetParent(panelGO.transform, false);
        var btnRT = btnGO.AddComponent<RectTransform>();
        btnRT.anchorMin = new Vector2(0.5f, 0.05f);
        btnRT.anchorMax = new Vector2(0.5f, 0.05f);
        btnRT.anchoredPosition = new Vector2(0f, 40f);
        btnRT.sizeDelta = new Vector2(250f, 60f);
        var btnImg = btnGO.AddComponent<Image>();
        btnImg.color = new Color(0.25f, 0.55f, 0.35f);
        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        btn.onClick.AddListener(() => onDismiss?.Invoke());

        var btnLabelGO = new GameObject("Label");
        btnLabelGO.transform.SetParent(btnGO.transform, false);
        var btnLabelRT = btnLabelGO.AddComponent<RectTransform>();
        btnLabelRT.anchorMin = Vector2.zero;
        btnLabelRT.anchorMax = Vector2.one;
        btnLabelRT.sizeDelta = Vector2.zero;
        var btnLabel = btnLabelGO.AddComponent<TextMeshProUGUI>();
        btnLabel.text = "BEGIN";
        btnLabel.fontSize = 28f;
        btnLabel.alignment = TextAlignmentOptions.Center;
        btnLabel.color = Color.white;

        return canvasGO;
    }

    // ═══════════════════════════════════════════════════════════════
    // DEMO CLEANUP (day 2+ in demo — explore aftermath, no date)
    // ═══════════════════════════════════════════════════════════════

    private IEnumerator DemoCleanupTransition()
    {
        // Set phase to Exploration but skip newspaper and prep timer entirely
        _currentPhase = DayPhase.Exploration;
        Debug.Log("[DayPhaseManager] Phase → Exploration (demo cleanup)");
        DismissAllStationUI();

        // 1. Fade to black (we're coming from sleep which already faded out, but be safe)
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeOut(_fadeDuration);

        // 2. Set up browse camera (skip newspaper read camera)
        if (_readCamera != null)
            _readCamera.Priority = PriorityInactive;
        if (ApartmentManager.Instance != null)
            ApartmentManager.Instance.SetBrowseCameraActive(true);
        CameraTestController.Instance?.RestorePreset();

        // 3. Force hard cut (no Cinemachine blend)
        if (brain != null)
        {
            var savedBlend = brain.DefaultBlend;
            brain.DefaultBlend = new CinemachineBlendDefinition(
                CinemachineBlendDefinition.Styles.Cut, 0f);
            yield return null;
            brain.DefaultBlend = savedBlend;
        }

        // 4. Disable newspaper
        if (_newspaperManager != null)
            _newspaperManager.enabled = false;

        // 5. UI: apartment browse on, newspaper off
        if (_apartmentUI != null) _apartmentUI.SetActive(true);
        if (_newspaperHUD != null) _newspaperHUD.SetActive(false);
        if (_goToBedPanel != null) _goToBedPanel.SetActive(false);

        // 6. Spawn date aftermath messes
        if (_authoredMessSpawner != null)
            _authoredMessSpawner.SpawnDailyMess();
        if (_entranceMessSpawner != null)
            _entranceMessSpawner.SpawnDailyMess();

        // 7. Restore audio (may still be ducked from flower trimming / date)
        AudioManager.Instance?.UnduckMusic(0.5f);
        AudioManager.Instance?.SetNonMusicMix(1f, 0.5f);
        RecordSlot.Instance?.Stop(); // clean slate for new day

        // 8. Ambience
        if (_explorationAmbienceClip != null && AudioManager.Instance != null)
            AudioManager.Instance.PlayAmbience(_explorationAmbienceClip, 0.5f);

        // 9. Fade in
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeIn(_fadeDuration);

        OnPhaseChanged?.Invoke((int)DayPhase.Exploration);

        // 10. Poll for all messes resolved, then show end screen
        StartCoroutine(PollDemoCleanupComplete());
    }

    private IEnumerator PollDemoCleanupComplete()
    {
        // Wait at least a few seconds before checking (let messes spawn)
        yield return new WaitForSeconds(3f);

        while (true)
        {
            bool stainsClean = CleaningManager.Instance == null || CleaningManager.Instance.AllClean;
            bool objectsClean = AuthoredMessSpawner.Instance == null
                             || AuthoredMessSpawner.Instance.ActiveMessObjectCount == 0;

            if (stainsClean && objectsClean)
            {
                Debug.Log("[DayPhaseManager] Demo cleanup complete — all messes resolved!");
                yield return new WaitForSeconds(1.5f); // brief pause before end screen
                OnCalendarComplete();
                yield break;
            }

            yield return new WaitForSeconds(0.5f);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // FLOWER TRIMMING TRANSITION
    // ═══════════════════════════════════════════════════════════════

    private IEnumerator FlowerTrimmingTransition()
    {
        if (!DateSessionManager.PendingFlowerTrim)
        {
            Debug.LogWarning("[DayPhaseManager] FlowerTrimming phase but no pending trim. Skipping to Evening.");
            SetPhase(DayPhase.Evening);
            yield break;
        }

        var bridge = _flowerTrimmingBridge != null ? _flowerTrimmingBridge : FlowerTrimmingBridge.Instance;
        if (bridge == null)
        {
            Debug.LogWarning("[DayPhaseManager] No FlowerTrimmingBridge found. Skipping to Evening.");
            DateSessionManager.PendingFlowerTrim = false;
            SetPhase(DayPhase.Evening);
            yield break;
        }

        // 0. Force-drop any held item and disable grabber — prevents soft-lock
        //    if the player is holding something when the scene transitions.
        var grabber = Object.FindAnyObjectByType<ObjectGrabber>();
        if (grabber != null) grabber.SetEnabled(false);

        // Also end any active watering session
        WateringManager.Instance?.ForceIdle();

        // 1. Fade to black
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeOut(_fadeDuration);

        // 2. Show phase title
        if (ScreenFade.Instance != null)
            ScreenFade.Instance.ShowPhaseTitle("Trimming");

        // 3. Begin scene load while still black — camera will activate off-screen
        bool trimmingComplete = false;
        bridge.BeginTrimming((score, days, gameOver) =>
        {
            trimmingComplete = true;
        });

        // 4. Wait for the flower scene to finish loading
        while (!bridge.IsSceneReady)
            yield return null;

        // 5. Hold title so the player can read it (3 seconds total)
        yield return new WaitForSeconds(3.0f);
        if (ScreenFade.Instance != null)
            ScreenFade.Instance.HidePhaseTitle();

        // 6. Brief pause after title fades before revealing the scene
        yield return new WaitForSeconds(0.3f);

        // 7. Fade in — the flower scene's own Camera is now active (apartment camera disabled)
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeIn(_fadeDuration * 2f);

        // 7b. Fade music back up now that the scene is visible
        AudioManager.Instance?.UnduckMusic(1.5f);

        // 8. Wait for the player to finish trimming
        while (!trimmingComplete)
            yield return null;

        // 9. Fade music down before leaving flower scene
        AudioManager.Instance?.DuckMusic(0f, 0.8f);
        yield return new WaitForSecondsRealtime(0.3f);

        // 10. Fade to black
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeOut(_fadeDuration);

        // 8b. Wait for flower scene to fully unload before showing apartment
        while (bridge != null && bridge.IsSceneReady)
            yield return null;

        // Clean up any stray trimming debris that fell into the apartment
        // during scene unload (they're at flower-scene scale = giant).
        foreach (var debris in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
        {
            if (debris == null) continue;
            // Trimming debris has no PlaceableObject — it's raw physics chunks
            if (debris.GetComponent<PlaceableObject>() == null
                && debris.gameObject.scene == gameObject.scene
                && debris.transform.lossyScale.x > 1f)
            {
                Object.Destroy(debris.gameObject);
            }
        }

        // Extra frames for cleanup + physics settle
        yield return null;
        yield return null;
        yield return new WaitForSeconds(0.2f);

        // 9. Restore apartment camera while screen is fully black
        if (bridge != null)
            bridge.RestoreApartmentCamera();

        // 10. Clean up state while still black (skip GoToBed to avoid redundant fades)
        _currentPhase = DayPhase.Evening;

        // Re-enable grabber after trimming scene
        if (grabber != null) grabber.SetEnabled(true);

        // Restore audio levels — FlowerTrimming ducked music to 10%
        AudioManager.Instance?.UnduckMusic(0.5f);
        AudioManager.Instance?.SetNonMusicMix(1f, 0.5f);
        DateSessionManager.Instance?.EndDate();
        FridgeController.Instance?.ForceClose();
        DrinkPourManager.Instance?.ForceIdle();
        RecordSlot.Instance?.Stop();
        DateEndScreen.Instance?.Dismiss();

        // 11. Dream interstitial
        if (ScreenFade.Instance != null)
            ScreenFade.Instance.ShowPhaseTitle("Nema drifts to sleep...");
        yield return new WaitForSecondsRealtime(3f);
        if (ScreenFade.Instance != null)
            ScreenFade.Instance.HidePhaseTitle();

        // 12. Auto-save + advance day (still black — no flicker)
        AutoSaveController.Instance?.PerformSave("end_of_day");

        if (GameClock.Instance != null)
        {
            // Advance day directly without SleepSequence's redundant fades
            GameClock.Instance.AdvanceDayDirect();
        }
        else
        {
            Debug.LogWarning("[DayPhaseManager] No GameClock — cannot advance day after flower trimming.");
        }

        // 13. EnterMorning will be called by DayManager.OnNewNewspaper
        //     It handles its own fade-in, so screen stays black until then.
    }

    // ═══════════════════════════════════════════════════════════════
    // CALENDAR COMPLETE
    // ═══════════════════════════════════════════════════════════════

    private void OnCalendarComplete()
    {
        StartCoroutine(CalendarCompleteSequence());
    }

    private IEnumerator CalendarCompleteSequence()
    {
        Debug.Log("[DayPhaseManager] Calendar complete — showing end screen.");

        // Ensure time scale is normal so fades and waits work
        TimeScaleManager.ClearAll();

        // 1. Fade to black (ScreenFade uses unscaledDeltaTime internally)
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeOut(_fadeDuration);

        // 2. Show phase title (dynamic mode name)
        string modeName = MainMenuManager.ActiveConfig != null
            ? MainMenuManager.ActiveConfig.modeName
            : "Game";
        if (ScreenFade.Instance != null)
            ScreenFade.Instance.ShowPhaseTitle($"{modeName} Complete");

        // 3. Hold for the player to read (real time — immune to time scale)
        yield return new WaitForSecondsRealtime(3f);

        // 4. Hide phase title
        if (ScreenFade.Instance != null)
            ScreenFade.Instance.HidePhaseTitle();

        // 5. Show summary screen if available, otherwise fall back to menu
        if (GameEndSummaryScreen.Instance != null)
        {
            // Fade in so the summary screen is visible
            if (ScreenFade.Instance != null)
                yield return ScreenFade.Instance.FadeIn(_fadeDuration);

            GameEndSummaryScreen.Instance.Show();
            // Summary screen handles feedback form → return to menu
        }
        else
        {
            // Fallback: return to main menu directly
            TimeScaleManager.ClearAll();
            MusicDirector.Instance?.PlayMenuSong();
            if (SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/mainmenu_nemahead.unity") >= 0)
            {
                SceneManager.LoadScene("mainmenu_nemahead");
            }
            else if (SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/mainmenu.unity") >= 0)
            {
                SceneManager.LoadScene("mainmenu");
            }
            else
            {
                Debug.Log("[DayPhaseManager] Loading menu via build index 0.");
                SceneManager.LoadScene(0);
            }
        }
    }

    // ─── UI Cleanup ─────────────────────────────────────────────────

    /// <summary>
    /// Force-close all station UIs and HUDs. Called on every phase transition
    /// so no stale UI persists across phases or into the next day.
    /// </summary>
    private void DismissAllStationUI()
    {
        WateringManager.Instance?.ForceIdle();
        // Record music continues across phases — duck/unduck handles volume
        DrinkPourManager.Instance?.ForceIdle();
        FridgeController.Instance?.CloseDoor();

        var calendar = Object.FindAnyObjectByType<ApartmentCalendar>();
        if (calendar != null) calendar.CloseCalendar();

        var pause = Object.FindAnyObjectByType<PauseMenuController>();
        if (pause != null) pause.ClosePauseMenu();
    }
}
