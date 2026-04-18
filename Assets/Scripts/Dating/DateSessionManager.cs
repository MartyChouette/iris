using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Scene-scoped singleton orchestrating the date lifecycle.
/// Phases use fade-to-black + teleport (no NPC walking).
///   Phase 1: NPC at entrance — entrance judgments
///   Phase 2: NPC at kitchen — player makes drink, NPC judges
///   Phase 3: NPC on couch — seated excursions evaluate apartment items
/// </summary>
public class DateSessionManager : MonoBehaviour
{
    public static DateSessionManager Instance { get; private set; }

    // ── Cached WaitForSeconds to avoid per-yield allocations ──
    private static readonly WaitForSeconds s_wait03 = new WaitForSeconds(0.3f);
    private static readonly WaitForSeconds s_wait05 = new WaitForSeconds(0.5f);
    private static readonly WaitForSeconds s_wait1  = new WaitForSeconds(1f);
    private static readonly WaitForSeconds s_wait2  = new WaitForSeconds(2f);
    private static readonly WaitForSeconds s_wait25 = new WaitForSeconds(2.5f);
    private static readonly WaitForSeconds s_wait3  = new WaitForSeconds(3f);
    private static readonly WaitForSeconds s_wait35 = new WaitForSeconds(3.5f);
    private static readonly WaitForSeconds s_waitRevealStep = new WaitForSeconds(0.6f);

    // Lazily-cached instance-scoped WaitForSeconds for inspector-driven durations
    private WaitForSeconds _waitPhaseTitle;
    private float _waitPhaseTitleCachedValue = -1f;
    private WaitForSeconds _waitDrinkTasting;
    private float _waitDrinkTastingCachedValue = -1f;

    private WaitForSeconds CachePhaseTitleWait()
    {
        if (_waitPhaseTitle == null || _waitPhaseTitleCachedValue != phaseTitleHold)
        {
            _waitPhaseTitle = new WaitForSeconds(phaseTitleHold);
            _waitPhaseTitleCachedValue = phaseTitleHold;
        }
        return _waitPhaseTitle;
    }

    private WaitForSeconds CacheDrinkTastingWait()
    {
        if (_waitDrinkTasting == null || _waitDrinkTastingCachedValue != _drinkTastingHold)
        {
            _waitDrinkTasting = new WaitForSeconds(_drinkTastingHold);
            _waitDrinkTastingCachedValue = _drinkTastingHold;
        }
        return _waitDrinkTasting;
    }

    // Cached shader lookups (avoid per-call Shader.Find which scans all loaded shaders)
    private static Shader s_overlaySpriteShader;
    private static Shader s_particleShader;
    private static bool s_shadersInitialized;

    private static void InitCachedShaders()
    {
        if (s_shadersInitialized) return;
        s_overlaySpriteShader = Shader.Find("Iris/OverlaySprite");
        s_particleShader = Shader.Find("Particles/Standard Unlit")
                        ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");
        s_shadersInitialized = true;
    }

    public enum SessionState { Idle, WaitingForArrival, DateInProgress, DateEnding }

    /// <summary>
    /// Sub-phases within DateInProgress:
    ///   Arrival           — NPC at entrance, entrance judgments
    ///   BackgroundJudging — NPC at kitchen, player makes drink
    ///   Reveal            — NPC on couch, seated excursions
    /// </summary>
    public enum DatePhase { None, Arrival, BackgroundJudging, Reveal }

    // ──────────────────────────────────────────────────────────────
    // Configuration
    // ──────────────────────────────────────────────────────────────
    [Header("Affection")]
    [Tooltip("Starting affection value (0-100 scale).")]
    [SerializeField] private float startingAffection = 50f;

    [Tooltip("Affection multiplier when mood matches date's preferences.")]
    [SerializeField] private float moodMatchMultiplier = 1.5f;

    [Tooltip("Affection multiplier when mood is outside date's preferences.")]
    [SerializeField] private float moodMismatchMultiplier = 0.5f;

    [Header("Multiplier Popup")]
    [Tooltip("Character size of the floating ×N text (world units per character).")]
    [SerializeField] private float _popupCharSize = 0.035f;

    [Tooltip("Color for positive (Like) multiplier popups.")]
    [SerializeField] private Color _popupLikeColor = new Color(1f, 0.55f, 0.75f, 1f);

    [Tooltip("Color for negative (Dislike) multiplier popups.")]
    [SerializeField] private Color _popupDislikeColor = new Color(0.55f, 0.55f, 0.6f, 1f);

    [Tooltip("How far the popup floats upward during its animation.")]
    [SerializeField] private float _popupRiseHeight = 0.35f;

    [Tooltip("How long the popup is visible (seconds).")]
    [SerializeField] private float _popupDuration = 1.6f;

    [Header("Reaction Values")]
    [Tooltip("Affection gained from a Like reaction.")]
    [SerializeField] private float likeAffection = 5f;

    [Tooltip("Affection gained from a Neutral reaction.")]
    [SerializeField] private float neutralAffection = 0.5f;

    [Tooltip("Affection lost from a Dislike reaction.")]
    [SerializeField] private float dislikeAffection = -4f;

    [Header("Fail Thresholds")]
    [Tooltip("Affection below this after Arrival → NPC leaves.")]
    [SerializeField] private float _arrivalFailThreshold = 25f;

    [Tooltip("Affection below this after drink delivery → NPC leaves.")]
    [SerializeField] private float _bgJudgingFailThreshold = 20f;

    [Tooltip("Affection below this after Phase 3 → NPC leaves without flower.")]
    [SerializeField] private float _revealFailThreshold = 30f;

    [Tooltip("If affection drops below this at ANY point, date immediately fails. 0 = disabled.")]
    [SerializeField] private float _bailOutThreshold = 10f;

    [Tooltip("Minimum affection required for the date to give you a flower (and trigger flower trimming).")]
    [SerializeField] private float _flowerAffectionThreshold = 30f;

    [Header("Ambient Check")]
    [Tooltip("Seconds between ambient mood evaluations.")]
    [SerializeField] private float moodCheckInterval = 15f;

    [Tooltip("Affection drift per check when mood matches.")]
    [SerializeField] private float ambientMoodDrift = 0.5f;

    [Header("Phase 3 Timing")]
#pragma warning disable 0414
    [Tooltip("Duration of Phase 3 (couch judging) in seconds before the date ends.")]
    [SerializeField] private float phase3Duration = 40f;
#pragma warning restore 0414

    [Header("Drink Verdict")]
    [Tooltip("Suspense pause (seconds) before the drink verdict is revealed.")]
    [SerializeField] private float _drinkTastingHold = 1.5f;

    [Header("Fade Timing")]
    [Tooltip("Fade duration for phase transitions (seconds).")]
    [SerializeField] private float fadeDuration = 0.3f;

    [Tooltip("Seconds to show phase title on black screen.")]
    [SerializeField] private float phaseTitleHold = 2.0f;

    [Header("Audio")]
    [Tooltip("SFX played when the date character arrives.")]
    [SerializeField] private AudioClip dateArrivedSFX;

    [Tooltip("SFX played on a Like reaction.")]
    [SerializeField] private AudioClip likeSFX;

    [Tooltip("SFX played on a Dislike reaction.")]
    [SerializeField] private AudioClip dislikeSFX;

    [Tooltip("SFX played when transitioning to a new date phase.")]
    [SerializeField] private AudioClip phaseTransitionSFX;

    [Header("References")]
    [Tooltip("Where the date character spawns (apartment entrance).")]
    [SerializeField] private Transform dateSpawnPoint;

    [Tooltip("Where the date character sits (couch seat target).")]
    [SerializeField] private Transform couchSeatTarget;

    [Tooltip("Where drinks are delivered (coffee table).")]
    [SerializeField] private Transform coffeeTableDeliveryPoint;

    [Tooltip("Where the NPC stands for entrance judgments.")]
    [SerializeField] private Transform judgmentStopPoint;

    [Tooltip("Where the NPC stands during the kitchen/drink phase.")]
    [SerializeField] private Transform kitchenStandPoint;


    [Tooltip("Runs the entrance judgments (music, perfume, outfit, cleanliness).")]
    [SerializeField] private EntranceJudgmentSequence _entranceJudgments;

    [Header("Phase Cameras")]
    [Tooltip("Camera framing snapped to during each date phase. Capture from the Scene View via the inspector buttons.")]
    [SerializeField] private PhaseCameraFrame _arrivalCamera = new() { label = "Arrival", nearClip = -9f, farClip = 1000f, perspectiveFOV = 60f };
    [SerializeField] private PhaseCameraFrame _kitchenCamera = new() { label = "Kitchen / BackgroundJudging", nearClip = -9f, farClip = 1000f, perspectiveFOV = 60f };
    [SerializeField] private PhaseCameraFrame _couchCamera   = new() { label = "Couch / Reveal", nearClip = -9f, farClip = 1000f, perspectiveFOV = 60f };

    [Tooltip("Default seconds the camera takes to glide into a phase frame when LerpPhaseCamera is used.")]
    [SerializeField] private float _phaseCameraLerpDuration = 1.6f;

    [System.Serializable]
    public struct PhaseCameraFrame
    {
        public string label;
        public Vector3 position;
        public Vector3 rotation;
        public float fov;
        [Tooltip("Near clip plane. Push forward to clip through walls/geometry in front of the camera.")]
        public float nearClip;
        [Tooltip("Far clip plane. Pull back to hide distant geometry.")]
        public float farClip;
        [Tooltip("Use perspective projection instead of orthographic for this phase.")]
        public bool perspective;
        [Tooltip("Field of view in degrees (only used in perspective mode).")]
        public float perspectiveFOV;
        public bool captured;
    }

    // Editor-only access to the frames so the custom inspector can mutate them.
#if UNITY_EDITOR
    public ref PhaseCameraFrame EditorGetArrivalCamera() => ref _arrivalCamera;
    public ref PhaseCameraFrame EditorGetKitchenCamera() => ref _kitchenCamera;
    public ref PhaseCameraFrame EditorGetCouchCamera()   => ref _couchCamera;
#endif

    [Header("Phase 2 Highlights")]
    [Tooltip("Renderer on the fridge to pulse during drink phase.")]
    [SerializeField] private Renderer _fridgeHighlightRenderer;

    [Tooltip("Renderer on the drink station/counter to pulse during drink phase.")]
    [SerializeField] private Renderer _drinkStationHighlightRenderer;

    [Tooltip("Pulse color for Phase 2 interactive objects.")]
    [SerializeField] private Color _phase2PulseColor = new Color(1f, 0.9f, 0.6f, 0.5f);

    [Tooltip("Pulse speed for Phase 2 highlights.")]
    [SerializeField] private float _phase2PulseSpeed = 1.5f;

    [Header("Events")]
    public UnityEvent<DatePersonalDefinition> OnDateSessionStarted;
    public UnityEvent<float> OnAffectionChanged;
    public UnityEvent<DatePersonalDefinition, float> OnDateSessionEnded;

    // ──────────────────────────────────────────────────────────────
    // Accumulated reactions
    // ──────────────────────────────────────────────────────────────
    public struct AccumulatedReaction
    {
        public string itemName;
        public ReactionType type;
    }

    /// <summary>True when a successful date should trigger flower trimming before evening.</summary>
    public static bool PendingFlowerTrim { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        PendingFlowerTrim = false;
    }

    /// <summary>Fired for each reaction (HUD display).</summary>
    public event System.Action<AccumulatedReaction> OnRevealReaction;

    // ──────────────────────────────────────────────────────────────
    // Phase transition dialogue
    // ──────────────────────────────────────────────────────────────
    private static readonly string[] s_prePhase2Lines = { "Why don't we go to the kitchen?", "I could use a drink..." };
    private static readonly string[] s_postPhase2Lines = { "Make me something good!", "What are you pouring?" };
    private static readonly string[] s_prePhase3Lines = { "Let's sit down for a bit.", "Show me the living room!" };
    private static readonly string[] s_postPhase3Lines = { "Nice place you've got here...", "Let me look around." };

    // ──────────────────────────────────────────────────────────────
    // Runtime state
    // ──────────────────────────────────────────────────────────────
    private SessionState _state = SessionState.Idle;
    private DatePhase _datePhase = DatePhase.None;
    private DatePersonalDefinition _currentDate;
    private float _affection;
    private bool _drinkVerdictRunning;
    private float _moodCheckTimer;
    private DateCharacterController _dateCharacter;
    private GameObject _dateCharacterGO;
    private DateSceneModels _activeSceneModels; // non-null when using scene-placed per-phase models
    private float _arrivalTimer;
    private bool _arrivalTimerActive;
    private readonly List<AccumulatedReaction> _accumulatedReactions = new();
    private Coroutine _phase2PulseCoroutine;
    private Color _fridgeOrigColor;
    private Color _drinkOrigColor;
    private Coroutine _phaseCameraLerp;

    // ──────────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────────
    public SessionState CurrentState => _state;
    public DatePhase CurrentDatePhase => _datePhase;
    public DatePersonalDefinition CurrentDate => _currentDate;
    public float Affection => _affection;
    public bool IsDateActive => _state == SessionState.DateInProgress;
    public DateCharacterController DateCharacter => _dateCharacter;

    // Debug read-only accessors
    public float StartingAffection => startingAffection;
    public float MoodMatchMultiplier => moodMatchMultiplier;
    public float MoodMismatchMultiplier => moodMismatchMultiplier;
    public float ArrivalFailThreshold => _arrivalFailThreshold;
    public float BgJudgingFailThreshold => _bgJudgingFailThreshold;
    public float RevealFailThreshold => _revealFailThreshold;
    public IReadOnlyList<AccumulatedReaction> AccumulatedReactions => _accumulatedReactions;
    public float ArrivalTimer => _arrivalTimer;
    public bool ArrivalTimerActive => _arrivalTimerActive;

    // ──────────────────────────────────────────────────────────────
    // Lifecycle
    // ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[DateSessionManager] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (_dateCharacter != null)
            _dateCharacter.OnReaction -= HandleCharacterReaction;

        StopPhase2Pulse();
        StopPhaseCameraLerp();

        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        // Arrival timer — ticks during WaitingForArrival
        if (_state == SessionState.WaitingForArrival && _arrivalTimerActive && !DateDebugOverlay.IsTimePaused)
        {
            _arrivalTimer -= Time.deltaTime;
            if (_arrivalTimer <= 0f)
            {
                _arrivalTimer = 0f;
                _arrivalTimerActive = false;
                TriggerDateArrival();
            }
        }

        if (_state != SessionState.DateInProgress) return;

        // Periodic mood check during BackgroundJudging and Reveal
        if (_datePhase == DatePhase.BackgroundJudging || _datePhase == DatePhase.Reveal)
        {
            _moodCheckTimer += Time.deltaTime;
            if (_moodCheckTimer >= moodCheckInterval)
            {
                _moodCheckTimer = 0f;
                EvaluateAmbientMood();
            }
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Session Flow
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Called after newspaper ad is selected — date is pending.
    /// Arrival is triggered externally by DayPhaseManager (prep timer expired)
    /// or PhoneController (player clicks phone to end prep early).
    /// </summary>
    public void ScheduleDate(DatePersonalDefinition date)
    {
        _currentDate = date;
        _state = SessionState.WaitingForArrival;
        _arrivalTimerActive = false;

        // Reset affection to 0 immediately so the HUD shows fresh for this date
        _affection = 0f;
        OnAffectionChanged?.Invoke(_affection);

#if UNITY_EDITOR
        Debug.Log($"[DateSessionManager] Scheduled date with {date.characterName}. Waiting for prep phase to end.");
#endif
    }

    /// <summary>Called when the arrival timer expires — triggers phone ring or direct arrival.</summary>
    private void TriggerDateArrival()
    {
#if UNITY_EDITOR
        Debug.Log($"[DateSessionManager] {_currentDate?.characterName} is arriving!");
#endif

        if (PhoneController.Instance != null)
            PhoneController.Instance.StartRinging();
        else
            OnDateCharacterArrived();
    }

    /// <summary>Called when the player answers the door. Starts the date.</summary>
    public void OnDateCharacterArrived()
    {
        if (_currentDate == null)
        {
            Debug.LogWarning("[DateSessionManager] No current date set.");
            return;
        }

        StartCoroutine(ArrivalTransition());
    }

    // ──────────────────────────────────────────────────────────────
    // Phase Transitions (fade → teleport → fade)
    // ──────────────────────────────────────────────────────────────

    private IEnumerator ArrivalTransition()
    {
        // Fade out
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeOut(fadeDuration);

        // Everything between FadeOut and FadeIn is wrapped so a crash
        // at ANY point can't leave the screen stuck white.
        try
        {
            ScreenFade.Instance?.ShowPhaseTitle("Impressions");
        }
        catch (System.Exception e) { Debug.LogError($"[DateSessionManager] Phase title failed: {e}"); }

        // Use realtime wait so timeScale=0 can't hang this
        yield return new WaitForSecondsRealtime(phaseTitleHold);

        try
        {
            ScreenFade.Instance?.HidePhaseTitle();

            const float sunsetHour = 18f;
            if (GameClock.Instance != null && GameClock.Instance.CurrentHour < sunsetHour)
                GameClock.Instance.RestoreFromSave(GameClock.Instance.CurrentDay, sunsetHour);

            _state = SessionState.DateInProgress;
            _datePhase = DatePhase.Arrival;
            NemaController.Instance?.MoveToDatePhase(DatePhase.Arrival);
            _affection = startingAffection;
            DateInspectSystem.Instance?.ResetForNewDate();
            _moodCheckTimer = 0f;
            _accumulatedReactions.Clear();

            SpawnDateCharacter();

            if (dateArrivedSFX != null && AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX(dateArrivedSFX);

            OnDateSessionStarted?.Invoke(_currentDate);
            OnAffectionChanged?.Invoke(_affection);

            ApplyPhaseCamera(DatePhase.Arrival);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[DateSessionManager] ArrivalTransition setup failed: {e}");
        }

        // Fade in ALWAYS runs
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeIn(fadeDuration);

        // Phase camera already positioned by ApplyPhaseCamera above.
        // No MomentCamera here — it fights with the phase camera and
        // causes a jarring snap-back when it returns to normal.

        // Epic title drop over the live scene
        if (PhaseTitleDrop.Instance != null)
            yield return PhaseTitleDrop.Instance.Show("Impressions");

#if UNITY_EDITOR
        Debug.Log($"[DateSessionManager] Phase 1: Arrival — entrance judgments for {_currentDate.characterName}.");
#endif

        // Run entrance judgments (NPC is already at judgment point)
        if (_entranceJudgments != null && _currentDate != null)
        {
            var reactionUI = _dateCharacterGO?.GetComponent<DateReactionUI>();
            yield return _entranceJudgments.RunJudgments(reactionUI, _currentDate);
        }

        // No mid-date fails: the date always plays through all 3 phases.
        // Low affection just means no flower at the end.

        // Wait for player to acknowledge Phase 1 results
        if (PhaseContinueButton.Instance != null)
        {
            bool clicked = false;
            PhaseContinueButton.Instance.Show(() => clicked = true);
            yield return new WaitUntil(() => clicked || _state != SessionState.DateInProgress);
            if (_state != SessionState.DateInProgress) yield break;
        }

        yield return TransitionToPhase2();
    }

    private IEnumerator TransitionToPhase2()
    {
        var reactionUI = _dateCharacterGO?.GetComponent<DateReactionUI>();

        // Pre-transition NPC dialogue
        string preLine = s_prePhase2Lines[UnityEngine.Random.Range(0, s_prePhase2Lines.Length)];
        reactionUI?.ShowText(preLine, 2.0f);
        yield return s_wait25;

        // Fade out
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeOut(fadeDuration);

        try { ScreenFade.Instance?.ShowPhaseTitle("Drinks"); }
        catch (System.Exception e) { Debug.LogError($"[DateSessionManager] Phase title failed: {e}"); }

        yield return new WaitForSecondsRealtime(phaseTitleHold);

        try
        {
            ScreenFade.Instance?.HidePhaseTitle();
            _datePhase = DatePhase.BackgroundJudging;
            NemaController.Instance?.MoveToDatePhase(DatePhase.BackgroundJudging);
            _moodCheckTimer = 0f;

            StartPhase2Pulse();
            HighlightDrinkGlasses(true);
            SetBottleHomes(useCounter: true);

            if (_activeSceneModels != null && _activeSceneModels.kitchenModel != null)
            {
                if (_dateCharacter != null)
                    _dateCharacter.OnReaction -= HandleCharacterReaction;
                _activeSceneModels.ShowOnly(_activeSceneModels.kitchenModel);
                _dateCharacterGO = _activeSceneModels.kitchenModel;
                EnsureDateComponents(_dateCharacterGO);
                _dateCharacter.SetSitting();
                _dateCharacter.OnReaction += HandleCharacterReaction;
            }
            else
            {
                Vector3 kitchenPos = kitchenStandPoint != null ? kitchenStandPoint.position
                    : new Vector3(-4f, 0f, -4.5f);
                if (_dateCharacter != null)
                {
                    _dateCharacter.WarpTo(kitchenPos);
                    _dateCharacter.SetSitting();
                }
            }

            if (phaseTransitionSFX != null && AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX(phaseTransitionSFX);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[DateSessionManager] TransitionToPhase2 setup failed: {e}");
        }

        // Fade in always runs even if setup threw
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeIn(fadeDuration);

        // Now glide the camera over to the kitchen while the player watches.
        LerpPhaseCamera(DatePhase.BackgroundJudging);

        // Epic title drop over the live scene
        if (PhaseTitleDrop.Instance != null)
            yield return PhaseTitleDrop.Instance.Show("Drinks");

        // Post-transition NPC dialogue
        yield return s_wait05;
        string postLine = s_postPhase2Lines[UnityEngine.Random.Range(0, s_postPhase2Lines.Length)];
        reactionUI?.ShowText(postLine, 2.0f);

#if UNITY_EDITOR
        Debug.Log("[DateSessionManager] Phase 2: Kitchen — player makes drink, NPC watches.");
#endif

        // Show "Serve" button — player clicks when they're done mixing.
        // This replaces the old pick-up-glass + walk-to-date delivery flow.
        if (PhaseContinueButton.Instance != null)
        {
            bool served = false;
            PhaseContinueButton.Instance.Show(() =>
            {
                // Auto-serve the drink via DrinkPourManager
                if (DrinkPourManager.Instance != null
                    && DrinkPourManager.Instance.CurrentState != DrinkPourManager.State.Idle)
                {
                    DrinkPourManager.Instance.ServeDrink();
                }
                served = true;
            }, "Serve \u2192");
            yield return new WaitUntil(() => served || _state != SessionState.DateInProgress);
            if (_state != SessionState.DateInProgress) yield break;
        }
    }

    private IEnumerator TransitionToPhase3()
    {
        StopPhase2Pulse();
        HighlightDrinkGlasses(false);

        // Restore fridge bottles to their original home (fridge shelf)
        SetBottleHomes(useCounter: false);

        var reactionUI = _dateCharacterGO?.GetComponent<DateReactionUI>();

        // Pre-transition NPC dialogue
        string preLine = s_prePhase3Lines[UnityEngine.Random.Range(0, s_prePhase3Lines.Length)];
        reactionUI?.ShowText(preLine, 2.0f);
        yield return s_wait25;

        // Fade out
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeOut(fadeDuration);

        try { ScreenFade.Instance?.ShowPhaseTitle("Warming Up"); }
        catch (System.Exception e) { Debug.LogError($"[DateSessionManager] Phase title failed: {e}"); }

        yield return new WaitForSecondsRealtime(phaseTitleHold);

        try
        {
            ScreenFade.Instance?.HidePhaseTitle();
            _datePhase = DatePhase.Reveal;
            NemaController.Instance?.MoveToDatePhase(DatePhase.Reveal);

            if (_activeSceneModels != null && _activeSceneModels.couchModel != null)
            {
                if (_dateCharacter != null)
                    _dateCharacter.OnReaction -= HandleCharacterReaction;
                _activeSceneModels.ShowOnly(_activeSceneModels.couchModel);
                _dateCharacterGO = _activeSceneModels.couchModel;
                EnsureDateComponents(_dateCharacterGO);
                _dateCharacter.SetSitting();
                _dateCharacter.OnReaction += HandleCharacterReaction;
            }
            else
            {
                Vector3 couchPos = couchSeatTarget != null ? couchSeatTarget.position : Vector3.zero;
                if (_dateCharacter != null)
                {
                    _dateCharacter.WarpTo(couchPos);
                    _dateCharacter.SetSitting();
                }
            }

            if (phaseTransitionSFX != null && AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX(phaseTransitionSFX);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[DateSessionManager] TransitionToPhase3 setup failed: {e}");
        }

        // Fade in always runs even if setup threw
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeIn(fadeDuration);

        // Glide camera over to the couch while the player watches.
        LerpPhaseCamera(DatePhase.Reveal);

        // Epic title drop over the live scene
        if (PhaseTitleDrop.Instance != null)
            yield return PhaseTitleDrop.Instance.Show("Warming Up");

        // Post-transition NPC dialogue
        yield return s_wait05;
        string postLine = s_postPhase3Lines[UnityEngine.Random.Range(0, s_postPhase3Lines.Length)];
        reactionUI?.ShowText(postLine, 2.0f);
        yield return s_wait25;

#if UNITY_EDITOR
        Debug.Log("[DateSessionManager] Phase 3: Player-driven item inspection.");
#endif

        // Phase 3 is player-driven — player clicks items to show the date.
        DialoguePortraitBox.Instance?.Say("Show me what you've got!", 2.5f);
        yield return s_wait25;

        // Show Continue button — player explores at their own pace
        if (PhaseContinueButton.Instance != null)
        {
            bool clicked = false;
            PhaseContinueButton.Instance.Show(() => clicked = true);
            yield return new WaitUntil(() => clicked || _state != SessionState.DateInProgress);
            if (_state != SessionState.DateInProgress) yield break;
        }

        // Release phase camera back to original apartment angle for the sweep
        ReleasePhaseCamera();
        yield return s_wait05;

        // Sweep remaining un-inspected items as a wave (from the wide OG angle)
        yield return StartCoroutine(SweepRemainingItems());

        // Post-reveal commentary based on affection
        if (_affection >= 0.7f)
            DialoguePortraitBox.Instance?.Say("I love what you've done here.", 3f);
        else if (_affection >= 0.4f)
            DialoguePortraitBox.Instance?.Say("Not bad... there's potential.", 3f);
        else
            DialoguePortraitBox.Instance?.Say("We can work on this...", 3f);

        yield return s_wait2;

        // Final continue before flower gift / farewell
        if (PhaseContinueButton.Instance != null)
        {
            bool clicked = false;
            PhaseContinueButton.Instance.Show(() => clicked = true);
            yield return new WaitUntil(() => clicked || _state != SessionState.DateInProgress);
            if (_state != SessionState.DateInProgress) yield break;
        }

        yield return StartCoroutine(RunEndSequence());
    }

    /// <summary>
    /// Instantly evaluate all active ReactableTags against the date's preferences.
    /// Liked items emit heart particles; disliked emit a grey puff.
    /// Staggered with a short delay between each for visual readability.
    /// </summary>
    private IEnumerator RevealAllReactions()
    {
        if (_currentDate == null || _currentDate.preferences == null) yield break;

        var items = GatherRevealItems(skipInspected: false);
        yield return StartCoroutine(RunRevealWave(items));
    }

    /// <summary>
    /// Sweep only the items the player didn't manually inspect in Phase 3.
    /// Same visual wave as RevealAllReactions but filtered.
    /// </summary>
    private IEnumerator SweepRemainingItems()
    {
        if (_currentDate == null || _currentDate.preferences == null) yield break;

        var items = GatherRevealItems(skipInspected: true);

#if UNITY_EDITOR
        Debug.Log($"[DateSessionManager] Sweep: {items.Count} un-inspected items remaining.");
#endif

        yield return StartCoroutine(RunRevealWave(items));
    }

    /// <summary>
    /// Gather all qualifying ReactableTags into a sorted list.
    /// When <paramref name="skipInspected"/> is true, tags already handled
    /// by DateInspectSystem are excluded (for the Phase 3 remainder sweep).
    /// </summary>
    private List<(ReactableTag tag, ReactionType reaction, int multiplier)> GatherRevealItems(bool skipInspected)
    {
        var prefs = _currentDate.preferences;
        var apartmentScene = gameObject.scene;
        var inspectSystem = DateInspectSystem.Instance;

        var list = new List<(ReactableTag tag, ReactionType reaction, int multiplier)>();
        foreach (var tag in ReactableTag.All)
        {
            if (!tag.IsActive) continue;
            if (tag.IsPrivate) continue;
            if (tag.gameObject.scene != apartmentScene) continue;

            if (skipInspected && inspectSystem != null && inspectSystem.IsInspected(tag))
                continue;

            var reaction = ReactionEvaluator.EvaluateReactable(tag, prefs);
            if (reaction == ReactionType.Neutral) continue;

            int multiplier = GetTagEffectMultiplier(tag);
            list.Add((tag, reaction, multiplier));
        }
        // Descending by multiplier so 3× items go first, then 2×, then 1×.
        list.Sort((a, b) => b.multiplier.CompareTo(a.multiplier));
        return list;
    }

    /// <summary>
    /// The shared reveal wave — plays particles, popups, highlights, and
    /// affection changes for each item with a 0.6s stagger. Used by both
    /// RevealAllReactions (full scan) and SweepRemainingItems (filtered).
    /// </summary>
    private IEnumerator RunRevealWave(List<(ReactableTag tag, ReactionType reaction, int multiplier)> items)
    {
        var reactionUI = _dateCharacterGO?.GetComponent<DateReactionUI>();

        InteractableHighlight activeHL = null;
        bool activeHLLiked = false;

        for (int i = 0; i < items.Count; i++)
        {
            var tag = items[i].tag;
            var reaction = items[i].reaction;
            int multiplier = items[i].multiplier;

            // Apply affection with the surface multiplier baked into magnitude.
            ApplyReaction(reaction, multiplier);

            // Pop the item name + reaction above the flower gauge
            string popText = reaction == ReactionType.Like
                ? $"{tag.DisplayName} \u2665"
                : reaction == ReactionType.Dislike
                    ? $"{tag.DisplayName} \u2639"
                    : tag.DisplayName;
            if (multiplier > 1) popText += $" {multiplier}\u00d7";
            AffectionBar.Instance?.ShowPopup(popText, reaction == ReactionType.Like);

            // Fire reveal event for HUD
            OnRevealReaction?.Invoke(new AccumulatedReaction
            {
                itemName = tag.DisplayName,
                type = reaction
            });

            // Clear any previously-lit item so only the current item glows.
            if (activeHL != null)
            {
                if (activeHLLiked) activeHL.SetPrepLikedHighlighted(false);
                else activeHL.SetPrepDislikedHighlighted(false);
                activeHL = null;
            }

            var highlight = tag.GetComponent<InteractableHighlight>()
                         ?? tag.GetComponentInParent<InteractableHighlight>()
                         ?? tag.GetComponentInChildren<InteractableHighlight>();
            if (highlight != null)
            {
                if (reaction == ReactionType.Like)
                {
                    highlight.SetPrepLikedHighlighted(true);
                    activeHLLiked = true;
                }
                else
                {
                    highlight.SetPrepDislikedHighlighted(true);
                    activeHLLiked = false;
                }
                activeHL = highlight;
            }

            Vector3 itemPos = tag.transform.position;

#if UNITY_EDITOR
            Debug.Log($"[DateSessionManager] Reveal: '{tag.DisplayName}' \u2192 {reaction} \u00d7{multiplier} | pos={itemPos:F3}");
#endif

            SpawnReactionParticles(itemPos, reaction);

            if (multiplier > 1)
                SpawnMultiplierPopup(itemPos + Vector3.up * 0.22f, multiplier, reaction);

            yield return s_waitRevealStep;
        }

        // Clear the last item's highlight.
        if (activeHL != null)
        {
            if (activeHLLiked) activeHL.SetPrepLikedHighlighted(false);
            else activeHL.SetPrepDislikedHighlighted(false);
        }

        // Evaluate cleanliness as a whole-room judgment
        if (TidyScorer.Instance != null)
        {
            var cleanReaction = ReactionEvaluator.EvaluateCleanliness(TidyScorer.Instance.OverallTidiness);
            if (cleanReaction != ReactionType.Neutral)
            {
                ApplyReaction(cleanReaction);
                if (reactionUI != null)
                {
                    string cleanText = cleanReaction == ReactionType.Like
                        ? "So clean and tidy!"
                        : "It's a bit messy...";
                    reactionUI.ShowText(cleanText, 2f);
                }
                yield return s_wait1;
            }
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Reaction Particles (runtime-built, no prefab needed)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Get the visual center of an object using renderer bounds.
    /// Falls back to transform.position if no renderer found.
    /// </summary>
    // ── Phase 2 highlight pulse ──────────────────────────────────

    // Shared MaterialPropertyBlock for pulse (no material instancing, no leaks)
    private static readonly int s_colorPropertyId = Shader.PropertyToID("_Color");
    private static readonly int s_baseColorPropertyId = Shader.PropertyToID("_BaseColor");
    private MaterialPropertyBlock _pulseMPB;

    private void StartPhase2Pulse()
    {
        if (_phase2PulseCoroutine != null) StopCoroutine(_phase2PulseCoroutine);

        if (_pulseMPB == null) _pulseMPB = new MaterialPropertyBlock();

        // Read original colors from sharedMaterial (no instancing)
        if (_fridgeHighlightRenderer != null && _fridgeHighlightRenderer.sharedMaterial != null)
            _fridgeOrigColor = _fridgeHighlightRenderer.sharedMaterial.HasProperty(s_baseColorPropertyId)
                ? _fridgeHighlightRenderer.sharedMaterial.GetColor(s_baseColorPropertyId)
                : _fridgeHighlightRenderer.sharedMaterial.color;
        if (_drinkStationHighlightRenderer != null && _drinkStationHighlightRenderer.sharedMaterial != null)
            _drinkOrigColor = _drinkStationHighlightRenderer.sharedMaterial.HasProperty(s_baseColorPropertyId)
                ? _drinkStationHighlightRenderer.sharedMaterial.GetColor(s_baseColorPropertyId)
                : _drinkStationHighlightRenderer.sharedMaterial.color;

        _phase2PulseCoroutine = StartCoroutine(Phase2PulseLoop());
    }

    private void StopPhase2Pulse()
    {
        if (_phase2PulseCoroutine != null)
        {
            StopCoroutine(_phase2PulseCoroutine);
            _phase2PulseCoroutine = null;
        }

        // Clear MPB to restore original shared material color
        if (_fridgeHighlightRenderer != null)
            _fridgeHighlightRenderer.SetPropertyBlock(null);
        if (_drinkStationHighlightRenderer != null)
            _drinkStationHighlightRenderer.SetPropertyBlock(null);
    }

    private void HighlightDrinkGlasses(bool on)
    {
        if (on) InteractableHighlight.SuppressVisuals = false;

        var glasses = DrinkGlass.All;
        for (int i = 0; i < glasses.Count; i++)
        {
            if (glasses[i] == null) continue;
            var hl = glasses[i].GetComponent<InteractableHighlight>();
            if (hl == null && on)
                hl = glasses[i].gameObject.AddComponent<InteractableHighlight>();
            if (hl != null) hl.SetHighlighted(on);
        }

        if (!on) InteractableHighlight.SuppressVisuals = true;
    }

    // ── Drink verdict cinematic: apartment show/hide ─────────────────

    /// <summary>
    /// Disable all Renderers in the apartment scene EXCEPT the date character,
    /// Nema, and the NatureBox skybox. Returns the list of disabled renderers
    /// so they can be re-enabled later.
    /// </summary>
    private List<Renderer> DisableApartmentRenderers()
    {
        var hidden = new List<Renderer>(128);
        var apartmentScene = gameObject.scene;

        // Collect GOs to preserve
        var preserve = new HashSet<GameObject>();
        if (_dateCharacterGO != null)
            foreach (var r in _dateCharacterGO.GetComponentsInChildren<Renderer>(true))
                preserve.Add(r.gameObject);
        if (NemaController.Instance != null)
            foreach (var r in NemaController.Instance.GetComponentsInChildren<Renderer>(true))
                preserve.Add(r.gameObject);
        // NatureBoxController lives on the skybox cube — preserve it
        if (NatureBoxController.Instance != null)
            foreach (var r in NatureBoxController.Instance.GetComponentsInChildren<Renderer>(true))
                preserve.Add(r.gameObject);

        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r == null || !r.enabled) continue;
            if (r.gameObject.scene != apartmentScene) continue;
            if (preserve.Contains(r.gameObject)) continue;

            r.enabled = false;
            hidden.Add(r);
        }

        return hidden;
    }

    /// <summary>Re-enable all renderers that were hidden by DisableApartmentRenderers.</summary>
    private static void RestoreApartmentRenderers(List<Renderer> hidden)
    {
        if (hidden == null) return;
        for (int i = 0; i < hidden.Count; i++)
        {
            if (hidden[i] != null)
                hidden[i].enabled = true;
        }
        hidden.Clear();
    }

    /// <summary>Switch all BottleItem homes between counter (Phase 2) and original (fridge).</summary>
    private static void SetBottleHomes(bool useCounter)
    {
        var bottles = Object.FindObjectsByType<BottleItem>(FindObjectsSortMode.None);
        for (int i = 0; i < bottles.Length; i++)
        {
            if (bottles[i] == null) continue;
            if (useCounter)
                bottles[i].UseCounterHome();
            else
                bottles[i].UseOriginalHome();
        }
    }

    private IEnumerator Phase2PulseLoop()
    {
        while (true)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * _phase2PulseSpeed * Mathf.PI * 2f);

            if (_fridgeHighlightRenderer != null)
                ApplyPulseColor(_fridgeHighlightRenderer, _fridgeOrigColor, pulse);
            if (_drinkStationHighlightRenderer != null)
                ApplyPulseColor(_drinkStationHighlightRenderer, _drinkOrigColor, pulse);

            yield return null;
        }
    }

    private void ApplyPulseColor(Renderer r, Color baseColor, float pulse)
    {
        Color target = Color.Lerp(baseColor, _phase2PulseColor, pulse);
        r.GetPropertyBlock(_pulseMPB);
        // Set both URP (_BaseColor) and built-in (_Color) so it works regardless of shader
        _pulseMPB.SetColor(s_colorPropertyId, target);
        _pulseMPB.SetColor(s_baseColorPropertyId, target);
        r.SetPropertyBlock(_pulseMPB);
    }

    // ── Helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Compute the world-space visual center of a ReactableTag's item by
    /// encapsulating the bounds of EVERY active renderer on the item and its
    /// children. The old version used `GetComponentInChildren<Renderer>()`
    /// which returns only the first match in depth-first order — for
    /// multi-mesh items (Gunpla, paired shoes, flowers with petals +
    /// leaves + stem) that was the first child mesh found, not the centroid
    /// of the whole item, so particles would spawn on an arm instead of the
    /// torso, on a petal instead of the flower crown, etc. Walking all
    /// renderers and calling Bounds.Encapsulate gives the true visual
    /// centroid. Skips renderers with invalid / zero-extent bounds (common
    /// when the mesh hasn't been rendered yet) and falls back to the
    /// transform's world position if nothing usable is found.
    /// </summary>
    /// <summary>
    /// Walks a ReactableTag's hierarchy to find the PlaceableObject and
    /// returns its current surface effect multiplier (1-5). Defaults to 1
    /// if the tag has no PlaceableObject or the item isn't on a surface.
    /// </summary>
    public static int GetTagEffectMultiplier(ReactableTag tag)
    {
        if (tag == null) return 1;
        var po = tag.GetComponent<PlaceableObject>();
        if (po == null) po = tag.GetComponentInParent<PlaceableObject>();
        if (po == null) po = tag.GetComponentInChildren<PlaceableObject>();
        return po != null ? po.CurrentEffectMultiplier : 1;
    }

    /// <summary>
    /// Floating "×N" label that rises and fades above each revealed item
    /// during the Phase 3 wave. Uses a runtime-built TextMesh so no prefab
    /// wiring is required. Color matches the reaction (pink for Like, grey
    /// for Dislike). Animates via a coroutine on DateSessionManager itself.
    /// </summary>
    public void SpawnMultiplierPopup(Vector3 worldPos, int multiplier, ReactionType reaction)
    {
        var go = new GameObject($"MultiplierPopup_x{multiplier}");
        go.transform.position = worldPos;

        var tm = go.AddComponent<TextMesh>();
        tm.text = $"×{multiplier}";
        tm.fontSize = 64;

        // Scale size and color intensity by multiplier value:
        // ×1 = base size + like/dislike color
        // ×5 = 2× base size + deep red
        float t = Mathf.Clamp01((multiplier - 1f) / 4f); // 0 at ×1, 1 at ×5
        tm.characterSize = Mathf.Lerp(_popupCharSize, _popupCharSize * 2f, t);

        Color baseColor = reaction == ReactionType.Like ? _popupLikeColor : _popupDislikeColor;
        Color hotColor = new Color(1f, 0.15f, 0.1f, 1f); // deep red
        tm.color = Color.Lerp(baseColor, hotColor, t);

        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;

        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            // Swap the default TextMesh material (which uses GUI/Text Shader
            // with ZTest LEqual — occluded by scene geometry) for our custom
            // Iris/OverlaySprite shader which hard-codes ZTest Always +
            // Overlay queue. Copy the font atlas from the original material
            // so the glyphs still render. If the overlay shader isn't found
            // in the build, fall back to the default and bump the queue so
            // at least render ordering helps.
            InitCachedShaders();
            var overlayShader = s_overlaySpriteShader;
            if (overlayShader != null && tm.font != null && tm.font.material != null)
            {
                var overlayMat = new Material(overlayShader);
                overlayMat.mainTexture = tm.font.material.mainTexture;
                overlayMat.color = tm.color;
                overlayMat.renderQueue = 4500;
                mr.sharedMaterial = overlayMat;
            }
            else if (mr.sharedMaterial != null)
            {
                // IMPORTANT: instance the material so we don't mutate the shared
                // font material globally (which affects every TextMesh using it).
                var fallbackMat = new Material(mr.sharedMaterial);
                fallbackMat.renderQueue = 4500;
                mr.sharedMaterial = fallbackMat;
            }
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        StartCoroutine(AnimateMultiplierPopup(go.transform, _popupDuration));
    }

    private IEnumerator AnimateMultiplierPopup(Transform t, float duration)
    {
        if (t == null) yield break;
        Vector3 startPos = t.position;
        Vector3 endPos = startPos + Vector3.up * _popupRiseHeight;

        var tm = t.GetComponent<TextMesh>();
        var mr = t.GetComponent<MeshRenderer>();
        Color baseColor = tm != null ? tm.color : Color.white;

        // Cache Camera.main once — it's an O(n) scan internally
        var cam = Camera.main;

        float elapsed = 0f;
        while (elapsed < duration && t != null)
        {
            elapsed += Time.deltaTime;
            float u = Mathf.Clamp01(elapsed / duration);

            // Rise smoothly
            t.position = Vector3.Lerp(startPos, endPos, Mathf.SmoothStep(0f, 1f, u));

            // Always face the camera (billboard) — re-fetch if null in case it became valid
            if (cam == null) cam = Camera.main;
            if (cam != null)
                t.rotation = Quaternion.LookRotation(t.position - cam.transform.position, Vector3.up);

            // Fade: pop in fast, hold, fade out
            float alpha;
            if (u < 0.15f) alpha = u / 0.15f;            // pop in
            else if (u < 0.65f) alpha = 1f;               // hold
            else alpha = Mathf.Lerp(1f, 0f, (u - 0.65f) / 0.35f); // fade out

            // Scale punch on pop-in for juiciness
            float scale = u < 0.2f
                ? Mathf.Lerp(0.5f, 1.15f, u / 0.2f)
                : u < 0.3f
                    ? Mathf.Lerp(1.15f, 1f, (u - 0.2f) / 0.1f)
                    : 1f;
            t.localScale = Vector3.one * scale;

            var c = baseColor;
            c.a *= alpha;

            // Drive color through BOTH the TextMesh (in case the overlay
            // shader isn't present) and the MeshRenderer's overlay material
            // (which is what actually draws when the shader swap succeeded).
            if (tm != null) tm.color = c;
            if (mr != null && mr.sharedMaterial != null)
                mr.sharedMaterial.color = c;

            yield return null;
        }

        if (t != null) Destroy(t.gameObject);
    }

    private static Vector3 GetVisualCenter(Transform t)
    {
        if (t == null) return Vector3.zero;

        var renderers = t.GetComponentsInChildren<Renderer>(includeInactive: false);
        bool any = false;
        Bounds combined = new Bounds();
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null || !r.enabled) continue;
            // Particle systems and skinned meshes sometimes report
            // zero-extent bounds until the first frame of rendering.
            Bounds b = r.bounds;
            if (b.extents.sqrMagnitude < 0.0000001f) continue;
            if (!any) { combined = b; any = true; }
            else combined.Encapsulate(b);
        }

        if (any) return combined.center;
        return t.position;
    }

    public static void SpawnReactionParticles(Vector3 position, ReactionType reaction)
    {
        Vector3 spawnPos = position + Vector3.up * 0.15f;
        var go = new GameObject("ReactionParticles");
        // Position BEFORE adding the ParticleSystem, otherwise the PS Awake
        // runs with the GameObject at (0,0,0) and any initial emission that
        // happens before Play() is called later in this function spawns at
        // the wrong spot when the system uses World simulation space.
        go.transform.position = spawnPos;

        var ps = go.AddComponent<ParticleSystem>();

        // Critical: stop any default-config playback that Unity kicked off
        // when AddComponent<ParticleSystem>() ran with the default playOnAwake=true.
        // Without this, a handful of default-cone particles fire BEFORE our
        // configuration is applied, which can spawn particles at whatever the
        // simulation state was when the GameObject first existed.
        ps.Stop(withChildren: true, stopBehavior: ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.Clear(withChildren: true);

        var main = ps.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

#if UNITY_EDITOR
        Debug.Log($"[DateSessionManager] SpawnReactionParticles: reaction={reaction} spawnPos={spawnPos:F3} goPos={go.transform.position:F3}");
#endif

        if (reaction == ReactionType.Like)
        {
            main.duration = 2.5f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            main.gravityModifier = -0.4f; // float upward
            main.maxParticles = 30;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.45f, 0.55f),    // hot pink
                new Color(1f, 0.7f, 0.75f));     // soft pink
        }
        else if (reaction == ReactionType.Dislike)
        {
            main.duration = 1.5f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
            main.gravityModifier = 0.1f; // sink slightly
            main.maxParticles = 8;
            main.startColor = new Color(0.4f, 0.4f, 0.45f, 0.5f);
        }
        else
        {
            Object.Destroy(go);
            return;
        }

        // Emission — multiple bursts for juiciness
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        if (reaction == ReactionType.Like)
        {
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, 12),
                new ParticleSystem.Burst(0.3f, 8),
                new ParticleSystem.Burst(0.6f, 6),
            });
        }
        else
        {
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 5) });
        }

        // Shape — spread around the item
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = reaction == ReactionType.Like ? 0.2f : 0.1f;

        // Size over lifetime — pop in, hold, fade out
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.2f),
            new Keyframe(0.15f, 1.2f),  // pop!
            new Keyframe(0.4f, 1f),     // hold
            new Keyframe(1f, 0f)        // fade
        ));

        // Color over lifetime — bright start, gentle fade
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.1f),
                new GradientAlphaKey(1f, 0.5f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        col.color = gradient;

        // Rotation for visual variety
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-1f, 1f);

        // Velocity — slight random spread
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);

        // Material
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        InitCachedShaders();
        var shader = s_particleShader;
        if (shader != null)
        {
            var mat = new Material(shader);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            renderer.material = mat;
        }

        ps.Play();
    }

    // ──────────────────────────────────────────────────────────────
    // Reactions
    // ──────────────────────────────────────────────────────────────

    /// <summary>Apply a reaction to affection (called by DateCharacterController or drink delivery).</summary>
    public void ApplyReaction(ReactionType type, float magnitude = 1f)
    {
        if (_state != SessionState.DateInProgress || _currentDate == null) return;

        float delta = type switch
        {
            ReactionType.Like => likeAffection,
            ReactionType.Neutral => neutralAffection,
            ReactionType.Dislike => dislikeAffection,
            _ => 0f
        };

        delta *= magnitude * GetMoodMultiplier() * _currentDate.preferences.reactionStrength;
        _affection = Mathf.Clamp(_affection + delta, 0f, 100f);

        // Reaction SFX
        if (type == ReactionType.Like && likeSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(likeSFX);
        else if (type == ReactionType.Dislike && dislikeSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(dislikeSFX);

        OnAffectionChanged?.Invoke(_affection);
#if UNITY_EDITOR
        Debug.Log($"[DateSessionManager] Reaction: {type} (delta={delta:+0.0;-0.0}) → Affection: {_affection:F1}");
#endif

        // No bail-out — the date always plays through all phases.
        // Low affection just means no flower gift at the end.
    }

    /// <summary>Called when a drink is delivered to the coffee table.</summary>
    public void ReceiveDrink(DrinkRecipeDefinition recipe, int score)
    {
        if (_state != SessionState.DateInProgress || _currentDate == null) return;
        if (_drinkVerdictRunning) return;
        StartCoroutine(DrinkVerdictSequence(recipe, score));
    }

    [Header("Drink Verdict Cinematic")]
    [Tooltip("How long the camera takes to zoom toward the date character.")]
    [SerializeField] private float _verdictZoomDuration = 2.0f;

    [Tooltip("How close to the date character the camera pushes (world units from character center).")]
    [SerializeField] private float _verdictZoomDistance = 1.5f;

    [Tooltip("FOV/ortho size for the verdict close-up.")]
    [SerializeField] private float _verdictZoomFOV = 3.0f;

    /// <summary>Dramatic drink tasting beat → verdict → continue button → Phase 3.</summary>
    private IEnumerator DrinkVerdictSequence(DrinkRecipeDefinition recipe, int score)
    {
        _drinkVerdictRunning = true;

        var reactionType = ReactionEvaluator.EvaluateDrink(recipe, score, _currentDate.preferences);
        float magnitude = score / 100f;
        var reactionUI = _dateCharacterGO?.GetComponent<DateReactionUI>();
        string drinkName = recipe != null ? recipe.drinkName : "Drink";

        // ── Cinematic: fade to white, strip apartment, reveal characters in nature ──

        // 1. Fade to white
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeOut(0.5f);

        // 2. Disable apartment renderers (keep date, Nema, NatureBox, UI)
        var hiddenRenderers = DisableApartmentRenderers();

        // 3. Push camera toward the date character
        Vector3 zoomTarget = _dateCharacterGO != null
            ? _dateCharacterGO.transform.position + Vector3.up * 0.8f
            : Vector3.zero;
        var am = ApartmentManager.Instance;
        var mainCam = Camera.main;
        Vector3 camStartPos = mainCam != null ? mainCam.transform.position : Vector3.zero;
        Quaternion camStartRot = mainCam != null ? mainCam.transform.rotation : Quaternion.identity;
        float camStartFOV = am != null ? am.CurrentOrthoSize : 5f;

        // Compute a close-up position looking at the date character
        Vector3 camDir = (camStartPos - zoomTarget).normalized;
        Vector3 camEndPos = zoomTarget + camDir * _verdictZoomDistance;

        // 4. Fade from white → characters floating in the sky
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeIn(0.5f);

        // 5. Smoothly zoom camera toward the date
        float zoomElapsed = 0f;
        while (zoomElapsed < _verdictZoomDuration)
        {
            zoomElapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, zoomElapsed / _verdictZoomDuration);

            Vector3 pos = Vector3.Lerp(camStartPos, camEndPos, t);
            float fov = Mathf.Lerp(camStartFOV, _verdictZoomFOV, t);
            am?.SetPresetBase(pos, camStartRot, fov);

            yield return null;
        }

        // 6. Suspense — thinking face
        reactionUI?.ShowText("Hmm...", _drinkTastingHold);
        yield return CacheDrinkTastingWait();

        // 7. Verdict reaction
        reactionUI?.ShowLabeledReaction(reactionType, drinkName);
        ApplyReaction(reactionType, magnitude);

        if (_state != SessionState.DateInProgress)
        {
            RestoreApartmentRenderers(hiddenRenderers);
            am?.ClearPresetBase();
            _drinkVerdictRunning = false;
            yield break;
        }

        // 8. Flower popup + particles
        if (reactionType != ReactionType.Neutral)
        {
            string sym = reactionType == ReactionType.Like ? " \u2665" : " \u2639";
            AffectionBar.Instance?.ShowPopup(drinkName + sym, reactionType == ReactionType.Like);

            if (_dateCharacterGO != null)
                SpawnReactionParticles(_dateCharacterGO.transform.position + Vector3.up * 0.5f, reactionType);
        }

        // Hold for flower animation + let the moment breathe
        yield return s_wait2;

        if (_state != SessionState.DateInProgress)
        {
            RestoreApartmentRenderers(hiddenRenderers);
            am?.ClearPresetBase();
            _drinkVerdictRunning = false;
            yield break;
        }

#if UNITY_EDITOR
        Debug.Log($"[DateSessionManager] Drink verdict: {drinkName} (score={score}) \u2192 {reactionType}");
#endif

        // 9. Wait for player to acknowledge (still in the cinematic close-up)
        if (PhaseContinueButton.Instance != null)
        {
            bool clicked = false;
            PhaseContinueButton.Instance.Show(() => clicked = true);
            yield return new WaitUntil(() => clicked || _state != SessionState.DateInProgress);
            if (_state != SessionState.DateInProgress)
            {
                RestoreApartmentRenderers(hiddenRenderers);
                am?.ClearPresetBase();
                _drinkVerdictRunning = false;
                yield break;
            }
        }

        // 10. Fade to white, restore apartment, release camera
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeOut(0.5f);

        RestoreApartmentRenderers(hiddenRenderers);
        am?.ClearPresetBase();

        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeIn(0.3f);

        // 6. Transition to Phase 3
        yield return TransitionToPhase3();
        _drinkVerdictRunning = false;
    }

    // ──────────────────────────────────────────────────────────────
    // End of Date
    // ──────────────────────────────────────────────────────────────

    /// <summary>Public safety fallback (only called from flower-trim cleanup now). Always routes to SucceedDate — flower threshold decides the outcome.</summary>
    public void EndDate()
    {
        if (_state == SessionState.Idle || _state == SessionState.DateEnding) return;

        // Release date camera framing back to normal browsing
        ReleasePhaseCamera();

        SucceedDate();
    }

    // ──────────────────────────────────────────────────────────────
    // Phase Camera Framing
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Apply the captured camera framing for the given phase as an instant snap.
    /// Pushes pos/rot/fov into ApartmentManager as a preset override (parallax
    /// still layers on top). No-op if the frame hasn't been captured yet.
    /// Use this during a fade-to-black so the player never sees the cut.
    /// </summary>
    public void ApplyPhaseCamera(DatePhase phase)
    {
        var frame = GetPhaseFrame(phase);
        if (!frame.captured) return;
        if (ApartmentManager.Instance == null) return;

        StopPhaseCameraLerp();
        ApartmentManager.Instance.SetPresetBase(
            frame.position,
            Quaternion.Euler(frame.rotation),
            frame.fov,
            frame.nearClip,
            frame.farClip,
            frame.perspective,
            frame.perspectiveFOV);
    }

    /// <summary>
    /// Smoothly glide the camera from its current pose to the captured frame for
    /// <paramref name="phase"/>. Pass <paramref name="duration"/> &lt; 0 to use
    /// the inspector default. Use this AFTER fade-in so the player sees the
    /// camera move into the new framing.
    /// </summary>
    public void LerpPhaseCamera(DatePhase phase, float duration = -1f)
    {
        var frame = GetPhaseFrame(phase);
        if (!frame.captured) return;
        if (ApartmentManager.Instance == null) return;

        if (duration < 0f) duration = _phaseCameraLerpDuration;

        StopPhaseCameraLerp();
        _phaseCameraLerp = StartCoroutine(PhaseCameraLerpRoutine(frame, duration));
    }

    /// <summary>Release the date camera override and return to normal apartment browsing.</summary>
    public void ReleasePhaseCamera()
    {
        StopPhaseCameraLerp();
        ApartmentManager.Instance?.ClearPresetBase();
    }

    private void StopPhaseCameraLerp()
    {
        if (_phaseCameraLerp != null)
        {
            StopCoroutine(_phaseCameraLerp);
            _phaseCameraLerp = null;
        }
    }

    private IEnumerator PhaseCameraLerpRoutine(PhaseCameraFrame frame, float duration)
    {
        var am = ApartmentManager.Instance;
        var cam = Camera.main;
        if (am == null || cam == null) yield break;

        // Capture starting pose from the live camera (mouse parallax included
        // — it's small enough that the lerp absorbs it cleanly).
        Vector3 startPos = cam.transform.position;
        Quaternion startRot = cam.transform.rotation;
        float startFov = cam.fieldOfView;

        Vector3 endPos = frame.position;
        Quaternion endRot = Quaternion.Euler(frame.rotation);
        float endFov = frame.fov;
        float startNear = cam.nearClipPlane;
        float startFar = cam.farClipPlane;
        // Guard zero values from old serialized data (fields didn't exist before)
        float endNear = frame.nearClip != 0f ? frame.nearClip : -9f;
        float endFar = frame.farClip > 0.1f ? frame.farClip : 1000f;

        // Projection mode applies immediately (no lerp — instant cut)
        bool usePerspective = frame.perspective;
        float endPFOV = Mathf.Max(frame.perspectiveFOV, 1f);

        if (duration <= 0f)
        {
            am.SetPresetBase(endPos, endRot, endFov, endNear, endFar, usePerspective, endPFOV);
            _phaseCameraLerp = null;
            yield break;
        }

        // Apply projection mode at start of lerp so FOV interpolation is coherent
        float startPFOV = cam.fieldOfView;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);

            am.SetPresetBase(
                Vector3.Lerp(startPos, endPos, t),
                Quaternion.Slerp(startRot, endRot, t),
                Mathf.Lerp(startFov, endFov, t),
                Mathf.Lerp(startNear, endNear, t),
                Mathf.Lerp(startFar, endFar, t),
                usePerspective,
                Mathf.Lerp(startPFOV, endPFOV, t));

            yield return null;
        }

        am.SetPresetBase(endPos, endRot, endFov, endNear, endFar, usePerspective, endPFOV);
        _phaseCameraLerp = null;
    }

    private PhaseCameraFrame GetPhaseFrame(DatePhase phase) => phase switch
    {
        DatePhase.Arrival           => _arrivalCamera,
        DatePhase.BackgroundJudging => _kitchenCamera,
        DatePhase.Reveal            => _couchCamera,
        _                           => default,
    };

    private IEnumerator RunEndSequence()
    {
        var reactionUI = _dateCharacterGO?.GetComponent<DateReactionUI>();

        yield return s_wait1;

        // The date always completes — affection only determines the farewell
        // dialogue and whether a flower is given. There is no "fail" exit path.
        if (reactionUI != null)
        {
            if (_affection >= _flowerAffectionThreshold)
            {
                reactionUI.ShowText("I had a wonderful time...", 3f);
                yield return s_wait35;
                reactionUI.ShowText("Here... I brought you something.", 3f);
                yield return s_wait35;
            }
            else
            {
                reactionUI.ShowText("Well... goodnight.", 3f);
                yield return s_wait35;
            }
        }

        SucceedDate();
    }

    private void FailDate()
    {
        if (_state == SessionState.Idle || _state == SessionState.DateEnding) return;

        string failedPhaseName = _datePhase.ToString();
        _state = SessionState.DateEnding;
        _datePhase = DatePhase.None;
#if UNITY_EDITOR
        Debug.Log($"[DateSessionManager] Date FAILED at {failedPhaseName} with {_currentDate?.characterName}. Affection: {_affection:F1}");
#endif

        DateOutcomeCapture.Capture(_currentDate, _affection, false, _accumulatedReactions);

        var failEntry = new DateHistory.DateHistoryEntry
        {
            name = _currentDate?.characterName ?? "Unknown",
            day = GameClock.Instance != null ? GameClock.Instance.CurrentDay : 0,
            affection = _affection,
            grade = "F",
            succeeded = false,
            failedPhase = failedPhaseName
        };
        PopulateLearnedPreferences(failEntry);
        DateHistory.Record(failEntry);

        DismissCharacter();
        OnDateSessionEnded?.Invoke(_currentDate, _affection);
        DateEndScreen.Instance?.Show(_currentDate, _affection, failed: true);
        AutoSaveController.Instance?.PerformSave("date_failed");
        _state = SessionState.Idle;
    }

    private void SucceedDate()
    {
        if (_state == SessionState.Idle || _state == SessionState.DateEnding) return;

        _state = SessionState.DateEnding;
        _datePhase = DatePhase.None;
        StartCoroutine(SucceedDateSequence());
    }

    private IEnumerator SucceedDateSequence()
    {
#if UNITY_EDITOR
        Debug.Log($"[DateSessionManager] Date SUCCEEDED with {_currentDate?.characterName}. Affection: {_affection:F1}");
#endif

        DateOutcomeCapture.Capture(_currentDate, _affection, true, _accumulatedReactions);

        var successEntry = new DateHistory.DateHistoryEntry
        {
            name = _currentDate?.characterName ?? "Unknown",
            day = GameClock.Instance != null ? GameClock.Instance.CurrentDay : 0,
            affection = _affection,
            grade = DateEndScreen.ComputeGrade(_affection),
            succeeded = true
        };
        PopulateLearnedPreferences(successEntry);
        DateHistory.Record(successEntry);

        // Award flower if affection is high enough, OR if the date guarantees flower success (tutorial)
        bool guaranteeFlower = _currentDate != null && _currentDate.guaranteeFlowerSuccess;
        bool earnedFlower = guaranteeFlower || _affection >= _flowerAffectionThreshold;

        // Signal flower trimming if this date has a flower scene configured AND player earned it
        if (earnedFlower && _currentDate != null && !string.IsNullOrEmpty(_currentDate.flowerSceneName))
            PendingFlowerTrim = true;

        // 1. Zelda-style flower gift presentation (only if earned)
        if (earnedFlower && _currentDate != null && _currentDate.flowerPrefab != null
            && FlowerGiftPresenter.Instance != null)
        {
            yield return FlowerGiftPresenter.Instance.Present(
                _currentDate.flowerPrefab, _currentDate.characterName);
        }

        // 2. Dismiss NPC
        DismissCharacter();

        // 3. Show date grade screen and wait for Continue click
        if (DateEndScreen.Instance != null)
        {
            bool dismissed = false;
            DateEndScreen.Instance.OnDismissed += OnEndScreenDismissed;
            DateEndScreen.Instance.Show(_currentDate, _affection, failed: false);

            void OnEndScreenDismissed()
            {
                dismissed = true;
                DateEndScreen.Instance.OnDismissed -= OnEndScreenDismissed;
            }

            // Wait with safety timeout — if the end screen is destroyed without firing the event,
            // we shouldn't hang forever.
            float endScreenTimeout = 120f;
            float endScreenStart = Time.realtimeSinceStartup;
            while (!dismissed)
            {
                if (DateEndScreen.Instance == null ||
                    Time.realtimeSinceStartup - endScreenStart > endScreenTimeout)
                {
                    Debug.LogWarning("[DateSessionManager] DateEndScreen dismissal timed out or instance lost — proceeding.");
                    break;
                }
                yield return null;
            }
        }

        // 4. Now fire event → DayPhaseManager routes to FlowerTrimming (if pending) or Evening
        AutoSaveController.Instance?.PerformSave("date_succeeded");
        _state = SessionState.Idle;
        OnDateSessionEnded?.Invoke(_currentDate, _affection);
    }

    private void DismissCharacter()
    {
        if (_dateCharacter != null)
        {
            _dateCharacter.OnReaction -= HandleCharacterReaction;
            _dateCharacter.Dismiss();
        }

        if (_activeSceneModels != null)
        {
            // Hide all scene-placed models — don't destroy them
            _activeSceneModels.HideAll();
            _activeSceneModels = null;
        }
        else if (_dateCharacterGO != null)
        {
            Destroy(_dateCharacterGO);
        }

        _dateCharacterGO = null;
        _dateCharacter = null;
    }

    // ──────────────────────────────────────────────────────────────
    // Internal
    // ──────────────────────────────────────────────────────────────

    private void SpawnDateCharacter()
    {
        // ── Scene-placed per-phase models (preferred) ──
        // Look up scene models matching this date's SO via DateSceneModels registry.
        _activeSceneModels = DateSceneModels.FindForDate(_currentDate);

        if (_activeSceneModels != null && _activeSceneModels.arrivalModel != null)
        {
            _activeSceneModels.ShowOnly(_activeSceneModels.arrivalModel);
            _dateCharacterGO = _activeSceneModels.arrivalModel;
        }
        else
        {
            // Fallback: instantiate from SO prefab
            Vector3 spawnPos = judgmentStopPoint != null ? judgmentStopPoint.position
                : new Vector3(-1.0f, 0f, 5.5f);

            if (_currentDate.characterModelPrefab != null)
            {
                _dateCharacterGO = Instantiate(_currentDate.characterModelPrefab, spawnPos, Quaternion.identity);
            }
            else
            {
                _dateCharacterGO = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                _dateCharacterGO.name = $"Date_{_currentDate.characterName}";
                _dateCharacterGO.transform.position = spawnPos;
            }
        }

        EnsureDateComponents(_dateCharacterGO);

        // Initialize and set to sitting (idle, no walking)
        Vector3 initPos = _activeSceneModels != null
            ? _dateCharacterGO.transform.position
            : (judgmentStopPoint != null ? judgmentStopPoint.position : new Vector3(-1.0f, 0f, 5.5f));
        _dateCharacter.Initialize(initPos);
        _dateCharacter.SetSitting();

        // Subscribe to reactions
        _dateCharacter.OnReaction += HandleCharacterReaction;
    }

    /// <summary>Ensure required components exist on the active date model.</summary>
    private void EnsureDateComponents(GameObject go)
    {
        _dateCharacter = go.GetComponent<DateCharacterController>();
        if (_dateCharacter == null)
            _dateCharacter = go.AddComponent<DateCharacterController>();

        if (go.GetComponent<DateReactionUI>() == null)
            go.AddComponent<DateReactionUI>();

        if (go.GetComponent<NPCGazeHighlight>() == null)
            go.AddComponent<NPCGazeHighlight>();

        if (go.GetComponent<OccludedSilhouette>() == null)
            go.AddComponent<OccludedSilhouette>();
    }

    private void HandleCharacterReaction(ReactableTag tag, ReactionType type, string displayName)
    {
        ApplyReaction(type);

        // Pop item name above the flower gauge during live reactions
        if (type != ReactionType.Neutral)
        {
            string sym = type == ReactionType.Like ? " \u2665" : " \u2639";
            AffectionBar.Instance?.ShowPopup(displayName + sym, type == ReactionType.Like);
        }

        // Show labeled reaction bubble on the character (with item icon if available)
        var reactionUI = _dateCharacterGO?.GetComponent<DateReactionUI>();
        Sprite itemIcon = tag != null ? tag.ReactionIcon : null;
        reactionUI?.ShowLabeledReaction(type, displayName, itemIcon);

        // Accumulate during all date phases (reactions shown live)
        if (tag != null)
        {
            var reaction = new AccumulatedReaction
            {
                itemName = displayName,
                type = type
            };
            _accumulatedReactions.Add(reaction);
            OnRevealReaction?.Invoke(reaction);
        }

        // Debug overlay logging
        DateDebugOverlay.Instance?.LogReaction($"{displayName} → {type}");
    }

    private void EvaluateAmbientMood()
    {
        if (_currentDate == null) return;

        float mood = MoodMachine.Instance?.Mood ?? 0f;
        var moodReaction = ReactionEvaluator.EvaluateMood(mood, _currentDate.preferences);

        if (moodReaction == ReactionType.Like)
        {
            _affection = Mathf.Clamp(_affection + ambientMoodDrift, 0f, 100f);
            OnAffectionChanged?.Invoke(_affection);
        }
        else if (moodReaction == ReactionType.Dislike)
        {
            _affection = Mathf.Clamp(_affection - ambientMoodDrift * 0.5f, 0f, 100f);
            OnAffectionChanged?.Invoke(_affection);
        }
    }

    private float GetMoodMultiplier()
    {
        if (_currentDate == null) return 1f;

        float mood = MoodMachine.Instance?.Mood ?? 0f;
        var prefs = _currentDate.preferences;

        if (mood >= prefs.preferredMoodMin && mood <= prefs.preferredMoodMax)
            return moodMatchMultiplier;

        return moodMismatchMultiplier;
    }

    private void PopulateLearnedPreferences(DateHistory.DateHistoryEntry entry)
    {
        foreach (var reaction in _accumulatedReactions)
        {
            if (reaction.type == ReactionType.Like)
                entry.learnedLikes.Add(reaction.itemName);
            else if (reaction.type == ReactionType.Dislike)
                entry.learnedDislikes.Add(reaction.itemName);
        }
    }
}
