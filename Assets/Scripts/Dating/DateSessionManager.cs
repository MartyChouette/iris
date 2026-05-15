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
    private static Texture2D s_heartAtlas;
    private static int s_heartFrameCount;
    private static bool s_heartAtlasLoaded;

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

    [Tooltip("Prefab spawned on the coffee table after serving. Becomes a dirty dish the next day.")]
    [SerializeField] private GameObject _dirtyGlassPrefab;

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
    [SerializeField] private PhaseCameraFrame _arrivalCamera = new() { label = "Arrival", nearClip = -9f, farClip = 1000f, perspectiveFOV = 60f, panLimit = 0.5f, zoomStep = -1 };
    [SerializeField] private PhaseCameraFrame _kitchenCamera = new() { label = "Kitchen / BackgroundJudging", nearClip = -9f, farClip = 1000f, perspectiveFOV = 60f, panLimit = 0.5f, zoomStep = -1 };
    [SerializeField] private PhaseCameraFrame _couchCamera   = new() { label = "Couch / Reveal", nearClip = -9f, farClip = 1000f, perspectiveFOV = 60f, panLimit = 0.5f, zoomStep = -1 };

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
        [Tooltip("Maximum pan distance during this phase. 0 = locked in place, -1 = use default.")]
        public float panLimit;
        [Tooltip("Which zoom step to force (0 = most zoomed in, -1 = don't override).")]
        public int zoomStep;
        [Tooltip("Minimum zoom step the player can scroll to during this phase (-1 = same as zoomStep).")]
        public int zoomStepMin;
        [Tooltip("Maximum zoom step the player can scroll to during this phase (-1 = same as zoomStep).")]
        public int zoomStepMax;
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
    private readonly HashSet<ReactableTag> _scoredTags = new();

    // Phase 3 cached evaluations — built gradually at phase start to avoid hitch
    private readonly Dictionary<ReactableTag, (ReactionType reaction, int multiplier)> _revealCache = new();
    private bool _revealCacheReady;

    private Coroutine _phase2PulseCoroutine;
    private Color _fridgeOrigColor;
    private Color _drinkOrigColor;
    private Coroutine _phaseCameraLerp;
    private Renderer[] _cachedApartmentRenderers;
    private BottleItem[] _cachedBottles;

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

        StartCoroutine(DateTransitionWrapper(ArrivalTransition()));
    }

    // ──────────────────────────────────────────────────────────────
    // Phase Transitions (fade → teleport → fade)
    // ──────────────────────────────────────────────────────────────

    /// <summary>Wrap date transitions so DayPhaseManager.IsTransitioning blocks input during fades.</summary>
    private static IEnumerator DateTransitionWrapper(IEnumerator inner)
    {
        if (DayPhaseManager.Instance != null)
            DayPhaseManager.Instance.IsTransitioning = true;
        yield return inner;
        if (DayPhaseManager.Instance != null)
            DayPhaseManager.Instance.IsTransitioning = false;
    }

    private IEnumerator ArrivalTransition()
    {
        Debug.Log("[DateSessionManager] P1_DEBUG: ArrivalTransition START");

        // Fade out
        Debug.Log("[DateSessionManager] P1_DEBUG: FadeOut begin");
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeOut(fadeDuration);
        Debug.Log("[DateSessionManager] P1_DEBUG: FadeOut done");

        // Everything between FadeOut and FadeIn is wrapped so a crash
        // at ANY point can't leave the screen stuck white.
        try
        {
            // Phase title shown via PhaseTitleDrop after fade-in (not ScreenFade, to avoid double text)
        }
        catch (System.Exception e) { Debug.LogError($"[DateSessionManager] Phase title failed: {e}"); }

        // Use realtime wait so timeScale=0 can't hang this
        Debug.Log($"[DateSessionManager] P1_DEBUG: phase title hold begin ({phaseTitleHold}s realtime)");
        yield return new WaitForSecondsRealtime(phaseTitleHold);
        Debug.Log("[DateSessionManager] P1_DEBUG: phase title hold done");

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
            _scoredTags.Clear();
            _revealCache.Clear();
            _revealCacheReady = false;

            Debug.Log("[DateSessionManager] P1_DEBUG: SpawnDateCharacter begin");
            SpawnDateCharacter();
            Debug.Log($"[DateSessionManager] P1_DEBUG: SpawnDateCharacter done — GO={_dateCharacterGO?.name ?? "NULL"}");

            // Cache scene queries now so transitions don't hitch later
            _cachedApartmentRenderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            _cachedBottles = Object.FindObjectsByType<BottleItem>(FindObjectsSortMode.None);

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
        Debug.Log("[DateSessionManager] P1_DEBUG: FadeIn begin");
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeIn(fadeDuration);
        Debug.Log("[DateSessionManager] P1_DEBUG: FadeIn done");

        // Unblock input after fade so IsTransitioning clears
        if (DayPhaseManager.Instance != null) DayPhaseManager.Instance.IsTransitioning = false;

        // Cinematic face sweep — close-up pan across the date's face with name
        Debug.Log($"[DateSessionManager] P1_DEBUG: ArrivalFaceSweep check — enabled={_enableArrivalSweep}, GO={_dateCharacterGO?.name ?? "NULL"}, timeScale={Time.timeScale}");
        if (_enableArrivalSweep && _dateCharacterGO != null)
        {
            Debug.Log("[DateSessionManager] P1_DEBUG: ArrivalFaceSweep begin");
            yield return ArrivalFaceSweep();
            Debug.Log("[DateSessionManager] P1_DEBUG: ArrivalFaceSweep done");
        }

        // Epic title drop over the live scene
        Debug.Log($"[DateSessionManager] P1_DEBUG: PhaseTitleDrop check — instance={PhaseTitleDrop.Instance != null}");
        if (PhaseTitleDrop.Instance != null)
        {
            Debug.Log("[DateSessionManager] P1_DEBUG: PhaseTitleDrop.Show begin");
            yield return PhaseTitleDrop.Instance.Show("Impressions");
            Debug.Log("[DateSessionManager] P1_DEBUG: PhaseTitleDrop.Show done");
        }

        Debug.Log($"[DateSessionManager] P1_DEBUG: Phase 1 Arrival — entrance judgments for {_currentDate?.characterName ?? "NULL"}");

        // Run entrance judgments (NPC is already at judgment point)
        if (_entranceJudgments != null && _currentDate != null)
        {
            var reactionUI = _dateCharacterGO?.GetComponent<DateReactionUI>();
            Debug.Log($"[DateSessionManager] P1_DEBUG: RunJudgments begin — reactionUI={reactionUI != null}");
            yield return _entranceJudgments.RunJudgments(reactionUI, _currentDate);
            Debug.Log("[DateSessionManager] P1_DEBUG: RunJudgments done");
        }
        else
        {
            Debug.LogWarning($"[DateSessionManager] P1_DEBUG: SKIPPED RunJudgments — judgments={_entranceJudgments != null}, date={_currentDate != null}");
        }

        // No mid-date fails: the date always plays through all 3 phases.
        // Low affection just means no flower at the end.

        // Wait for player to acknowledge Phase 1 results
        Debug.Log($"[DateSessionManager] P1_DEBUG: PhaseContinueButton check — instance={PhaseContinueButton.Instance != null}");
        if (PhaseContinueButton.Instance != null)
        {
            bool clicked = false;
            PhaseContinueButton.Instance.Show(() => clicked = true);
            Debug.Log("[DateSessionManager] P1_DEBUG: waiting for Continue button click...");
            yield return new WaitUntil(() => clicked || _state != SessionState.DateInProgress);
            Debug.Log($"[DateSessionManager] P1_DEBUG: Continue clicked={clicked}, state={_state}");
            if (_state != SessionState.DateInProgress) yield break;
        }

        Debug.Log("[DateSessionManager] P1_DEBUG: ArrivalTransition COMPLETE — starting Phase 2");
        yield return TransitionToPhase2();
    }

    private IEnumerator TransitionToPhase2()
    {
        // Exit top-down camera if active — phase cameras will fight it
        ApartmentManager.Instance?.ExitTopDown();

        var reactionUI = _dateCharacterGO?.GetComponent<DateReactionUI>();

        // Pre-transition NPC dialogue
        string preLine = s_prePhase2Lines[UnityEngine.Random.Range(0, s_prePhase2Lines.Length)];
        if (reactionUI != null && reactionUI.gameObject.activeInHierarchy) reactionUI.ShowText(preLine, 2.0f);
        yield return s_wait25;

        // Block input during transition
        if (DayPhaseManager.Instance != null) DayPhaseManager.Instance.IsTransitioning = true;

        // Fade out
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeOut(fadeDuration);

        // Phase title shown via PhaseTitleDrop after fade-in (not ScreenFade, to avoid double text)

        yield return new WaitForSecondsRealtime(phaseTitleHold);

        try
        {
            ScreenFade.Instance?.HidePhaseTitle();
            _datePhase = DatePhase.BackgroundJudging;
            NemaController.Instance?.MoveToDatePhase(DatePhase.BackgroundJudging);
            _moodCheckTimer = 0f;

            StartPhase2Pulse();
            // Signal glass choice — DrinkPourManager highlights glasses
            if (DrinkPourManager.Instance != null)
                DrinkPourManager.Instance.BeginGlassChoice();
            else
                HighlightDrinkGlasses(true);
            SetBottleHomes(useCounter: true);

            Debug.Log($"[DateSessionManager] Phase2 model swap: sceneModels={(_activeSceneModels != null ? "OK" : "NULL")}, kitchenModel={(_activeSceneModels?.kitchenModel != null ? _activeSceneModels.kitchenModel.name : "NULL")}");
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
                Debug.LogWarning("[DateSessionManager] Phase2: No kitchen model — warping existing character.");
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

            // Hide everything except drink area, kitchen Nema, kitchen date,
            // all glasses and all bottles (player needs to see + click them)
            // Phase2EnvironmentDim disabled — re-enable when collider handling is added
            // if (Phase2EnvironmentDim.Instance != null)
            //     Phase2EnvironmentDim.Instance.HideEnvironment(...);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[DateSessionManager] TransitionToPhase2 setup failed: {e}");
        }

        // Snap camera into phase framing while screen is still white
        ApplyPhaseCamera(DatePhase.BackgroundJudging);

        // Fade in always runs even if setup threw
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeIn(fadeDuration);

        // Unblock input now that transition is complete
        if (DayPhaseManager.Instance != null) DayPhaseManager.Instance.IsTransitioning = false;

        // Epic title drop over the live scene
        if (PhaseTitleDrop.Instance != null)
            yield return PhaseTitleDrop.Instance.Show("Drinks");

        // Re-fetch reaction UI from the new model (old one is inactive after swap)
        reactionUI = _dateCharacterGO?.GetComponent<DateReactionUI>();

        // Post-transition NPC dialogue
        yield return s_wait05;
        string postLine = s_postPhase2Lines[UnityEngine.Random.Range(0, s_postPhase2Lines.Length)];
        if (reactionUI != null && reactionUI.gameObject.activeInHierarchy)
            reactionUI.ShowText(postLine, 2.0f);

#if UNITY_EDITOR
        Debug.Log("[DateSessionManager] Phase 2: Kitchen — player makes drink, NPC watches.");
#endif

        // ── Step 1: Blink glasses → player clicks one to select ──
        Debug.Log($"[DateSessionManager] P2: IsTransitioning={DayPhaseManager.Instance?.IsTransitioning}, IsInteractionPhase={DayPhaseManager.Instance?.IsInteractionPhase}, DrinkPourMgr={(DrinkPourManager.Instance != null ? "OK" : "NULL")}");
        if (DrinkPourManager.Instance != null)
            DrinkPourManager.Instance.BeginGlassChoice();

        // Wait until player has selected a glass (state moves past ChoosingGlass)
        yield return new WaitUntil(() =>
            DrinkPourManager.Instance == null
            || DrinkPourManager.Instance.CurrentState != DrinkPourManager.State.ChoosingGlass
            || _state != SessionState.DateInProgress);
        if (_state != SessionState.DateInProgress) yield break;

        // ── Step 2: Player pours freely, then clicks "Serve" ──
        // Wait until at least one glass has liquid before showing the Serve button
        yield return new WaitUntil(() =>
        {
            if (_state != SessionState.DateInProgress) return true;
            var glasses = DrinkGlass.All;
            for (int i = 0; i < glasses.Count; i++)
                if (glasses[i] != null && glasses[i].TotalFill > 0f) return true;
            return false;
        });
        if (_state != SessionState.DateInProgress) yield break;

        if (PhaseContinueButton.Instance != null)
        {
            bool serveClicked = false;
            PhaseContinueButton.Instance.Show(() => { serveClicked = true; }, "SERVE");

            yield return new WaitUntil(() => serveClicked || _state != SessionState.DateInProgress);
            if (_state != SessionState.DateInProgress) yield break;
            PhaseContinueButton.Instance.Hide();
        }

        // ── Step 3: Blink glasses → player clicks which one to serve ──
        if (DrinkPourManager.Instance != null)
            DrinkPourManager.Instance.BeginServeChoice();

        // Wait until a glass is served (state goes Idle)
        yield return new WaitUntil(() =>
            DrinkPourManager.Instance == null
            || DrinkPourManager.Instance.CurrentState == DrinkPourManager.State.Idle
            || _state != SessionState.DateInProgress);
        if (_state != SessionState.DateInProgress) yield break;
    }

    private IEnumerator TransitionToPhase3()
    {
        // Exit top-down camera if active — phase cameras will fight it
        ApartmentManager.Instance?.ExitTopDown();

        StopPhase2Pulse();
        HighlightDrinkGlasses(false);
        // Phase2EnvironmentDim.Instance?.RestoreEnvironment();

        // Reset drink minigame — clear all glass contents and state
        // so it's fresh for the next date
        ResetDrinkMinigame();

        // Restore fridge bottles to their original home (fridge shelf)
        SetBottleHomes(useCounter: false);

        // Block input during transition
        if (DayPhaseManager.Instance != null) DayPhaseManager.Instance.IsTransitioning = true;

        // Fade out (instant if already faded from drink verdict)
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeOut(fadeDuration);

        // Phase title shown via PhaseTitleDrop after fade-in (not ScreenFade, to avoid double text)

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

        // Snap directly to Phase 3 framing — ApplyPhaseCamera overwrites all preset
        // fields, so clearing first just causes a redundant projection-mode round-trip
        // (ortho→perspective→ortho) that can hitch the GPU.
        ApplyPhaseCamera(DatePhase.Reveal);

        // Build reveal cache while still faded — spreads GetComponent calls over frames
        yield return StartCoroutine(BuildRevealCache());

        // Fade in always runs even if setup threw
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeIn(fadeDuration);

        // Unblock input now that transition is complete
        if (DayPhaseManager.Instance != null) DayPhaseManager.Instance.IsTransitioning = false;

        // Epic title drop over the live scene
        if (PhaseTitleDrop.Instance != null)
            yield return PhaseTitleDrop.Instance.Show("Warming Up");

        // Re-fetch reaction UI from the new model (old one is inactive after swap)
        var reactionUI = _dateCharacterGO?.GetComponent<DateReactionUI>();

        // Post-transition NPC dialogue
        yield return s_wait05;
        string postLine = s_postPhase3Lines[UnityEngine.Random.Range(0, s_postPhase3Lines.Length)];
        if (reactionUI != null && reactionUI.gameObject.activeInHierarchy) reactionUI.ShowText(postLine, 2.0f);
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
        // Pull out to farthest zoom so the full apartment is visible for judgment
        var zoomSteps = ApartmentManager.Instance?.ZoomSteps;
        if (zoomSteps != null && zoomSteps.Length > 0)
            ApartmentManager.Instance.ForceZoomStep(zoomSteps.Length - 1);
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

        // Separate liked and disliked
        var liked = new List<(ReactableTag tag, ReactionType reaction, int multiplier)>();
        var disliked = new List<(ReactableTag tag, ReactionType reaction, int multiplier)>();
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].reaction == ReactionType.Like)
                liked.Add(items[i]);
            else
                disliked.Add(items[i]);
        }

#if UNITY_EDITOR
        Debug.Log($"[DateSessionManager] Sweep: {liked.Count} liked, {disliked.Count} disliked remaining.");
#endif

        // Fire all liked items at once — particles burst simultaneously
        if (liked.Count > 0)
        {
            for (int i = 0; i < liked.Count; i++)
            {
                _scoredTags.Add(liked[i].tag);
                ApplyReaction(liked[i].reaction, liked[i].multiplier);
                SpawnReactionParticles(liked[i].tag.transform.position, liked[i].reaction);
                SpawnMultiplierPopup(liked[i].tag.transform.position + Vector3.up * 0.22f, liked[i].multiplier, liked[i].reaction);

                var hl = liked[i].tag.GetComponent<ItemHighlight>()
                      ?? liked[i].tag.GetComponentInParent<ItemHighlight>();
                if (hl != null) hl.SetPrepLikedHighlighted(true);
            }

            AffectionBar.Instance?.ShowPopup($"{liked.Count} things loved! \u2665", true);
            yield return new WaitForSeconds(2f);

            // Clear liked highlights
            for (int i = 0; i < liked.Count; i++)
            {
                var hl = liked[i].tag.GetComponent<ItemHighlight>()
                      ?? liked[i].tag.GetComponentInParent<ItemHighlight>();
                if (hl != null) hl.SetPrepLikedHighlighted(false);
            }
        }

        // Then fire disliked one by one (staggered for impact)
        if (disliked.Count > 0)
        {
            yield return new WaitForSeconds(0.5f);
            yield return StartCoroutine(RunRevealWave(disliked));
        }

    }

    /// <summary>
    /// Gather all qualifying ReactableTags into a sorted list.
    /// When <paramref name="skipInspected"/> is true, tags already handled
    /// by DateInspectSystem are excluded (for the Phase 3 remainder sweep).
    /// </summary>
    /// <summary>
    /// Build the reveal cache gradually over multiple frames to avoid a hitch.
    /// Call at the start of Phase 3 while the screen is still fading in.
    /// </summary>
    private IEnumerator BuildRevealCache()
    {
        _revealCache.Clear();
        _revealCacheReady = false;

        if (_currentDate == null || _currentDate.preferences == null)
        {
            _revealCacheReady = true;
            yield break;
        }

        var prefs = _currentDate.preferences;
        var apartmentScene = gameObject.scene;
        var allTags = ReactableTag.All;
        int batchSize = 10; // evaluate 10 per frame

        for (int i = 0; i < allTags.Count; i++)
        {
            var tag = allTags[i];
            if (tag == null || !tag.IsActive || tag.IsPrivate) continue;
            if (tag.gameObject.scene != apartmentScene) continue;

            var reaction = ReactionEvaluator.EvaluateReactable(tag, prefs);
            int multiplier = GetTagEffectMultiplier(tag);
            _revealCache[tag] = (reaction, multiplier);

            if ((i + 1) % batchSize == 0)
                yield return null; // spread across frames
        }

        _revealCacheReady = true;
        Debug.Log($"[DateSessionManager] Reveal cache built: {_revealCache.Count} items");
    }

    /// <summary>Look up a tag's cached reaction. Returns Neutral if not cached.</summary>
    public ReactionType GetCachedReaction(ReactableTag tag)
    {
        return _revealCache.TryGetValue(tag, out var entry) ? entry.reaction : ReactionType.Neutral;
    }

    private List<(ReactableTag tag, ReactionType reaction, int multiplier)> GatherRevealItems(bool skipInspected)
    {
        var inspectSystem = DateInspectSystem.Instance;

        var list = new List<(ReactableTag tag, ReactionType reaction, int multiplier)>();
        foreach (var kvp in _revealCache)
        {
            var tag = kvp.Key;
            if (tag == null || !tag.IsActive) continue;

            if (skipInspected && inspectSystem != null && inspectSystem.IsInspected(tag))
                continue;

            // Skip items already scored earlier in this date
            if (_scoredTags.Contains(tag))
                continue;

            var reaction = kvp.Value.reaction;
            if (reaction == ReactionType.Neutral) continue;

            list.Add((tag, reaction, kvp.Value.multiplier));
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

        ItemHighlight activeHL = null;
        bool activeHLLiked = false;

        for (int i = 0; i < items.Count; i++)
        {
            var tag = items[i].tag;
            var reaction = items[i].reaction;
            int multiplier = items[i].multiplier;

            // Mark as scored (should already be filtered, but belt-and-suspenders)
            _scoredTags.Add(tag);

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

            var highlight = tag.GetComponent<ItemHighlight>()
                         ?? tag.GetComponentInParent<ItemHighlight>()
                         ?? tag.GetComponentInChildren<ItemHighlight>();
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
        var glasses = DrinkGlass.All;
        for (int i = 0; i < glasses.Count; i++)
        {
            if (glasses[i] == null) continue;
            var hl = glasses[i].GetComponent<ItemHighlight>();
            if (hl == null && on)
                hl = glasses[i].gameObject.AddComponent<ItemHighlight>();
            if (hl != null) hl.SetHighlighted(on);
        }
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

        // Use cached renderer list (populated at date start) to avoid FindObjectsByType hitch
        var renderers = _cachedApartmentRenderers ?? Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        foreach (var r in renderers)
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
    private void SetBottleHomes(bool useCounter)
    {
        var bottles = _cachedBottles ?? Object.FindObjectsByType<BottleItem>(FindObjectsSortMode.None);
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
        string sign = reaction == ReactionType.Like ? "+" : "-";
        tm.text = multiplier > 1 ? $"{sign}{multiplier}" : sign;
        tm.fontSize = 64;

        // Scale size by multiplier: ×1 = base, ×5 = 2× base
        float t = Mathf.Clamp01((multiplier - 1f) / 4f);
        tm.characterSize = Mathf.Lerp(_popupCharSize, _popupCharSize * 2f, t);

        // Green for positive, red for negative
        Color likeColor = new Color(0.2f, 0.9f, 0.3f, 1f);
        Color dislikeColor = new Color(0.95f, 0.2f, 0.15f, 1f);
        Color baseColor = reaction == ReactionType.Like ? likeColor : dislikeColor;
        tm.color = Color.Lerp(baseColor * 0.85f, baseColor, t);

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
                t.rotation = Quaternion.LookRotation(cam.transform.forward, Vector3.up);

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

        float heartScale = VisualScaleSettings.Instance.GetHeartScale();

        if (reaction == ReactionType.Like)
        {
            main.duration = 2.5f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f * heartScale, 0.14f * heartScale);
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

        // Velocity — slight random spread (all axes must use the same curve mode)
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);

        // Material
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        InitCachedShaders();
        var shader = s_particleShader;
        if (shader != null)
        {
            var mat = new Material(shader);
            // Transparent alpha-blended so PNG transparency works
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHABLEND_ON");
            renderer.material = mat;

            // Heart flipbook for Like reactions
            if (reaction == ReactionType.Like)
            {
                if (!s_heartAtlasLoaded)
                {
                    s_heartAtlasLoaded = true;
                    var frames = FlipbookAtlas.LoadFrames("Particles", "heart_explode_", 3);
                    if (frames != null && frames.Length > 0)
                    {
                        s_heartAtlas = FlipbookAtlas.Build(frames);
                        s_heartFrameCount = frames.Length;
                    }
                }
                if (s_heartAtlas != null)
                {
                    mat.mainTexture = s_heartAtlas;
                    var tsa = ps.textureSheetAnimation;
                    tsa.enabled = true;
                    tsa.mode = ParticleSystemAnimationMode.Grid;
                    tsa.numTilesX = s_heartFrameCount;
                    tsa.numTilesY = 1;
                    tsa.animation = ParticleSystemAnimationType.WholeSheet;
                    tsa.cycleCount = 1;
                    tsa.timeMode = ParticleSystemAnimationTimeMode.Lifetime;
                }
            }
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
    public void ReceiveDrink(DrinkRecipeDefinition recipe, int score, DrinkGlass servedGlass = null)
    {
        if (_state != SessionState.DateInProgress || _currentDate == null) return;
        if (_drinkVerdictRunning) return;

        // Hide the original glass at the drink station
        if (servedGlass != null)
            servedGlass.gameObject.SetActive(false);

        // Spawn a dirty glass on the coffee table (persists as next-day dish)
        SpawnDirtyGlass();

        StartCoroutine(DrinkVerdictSequence(recipe, score));
    }

    private GameObject _spawnedDirtyGlass;

    private void SpawnDirtyGlass()
    {
        if (_dirtyGlassPrefab == null || coffeeTableDeliveryPoint == null) return;

        // Destroy any previously spawned glass (only one served drink per date)
        if (_spawnedDirtyGlass != null)
            Destroy(_spawnedDirtyGlass);

        _spawnedDirtyGlass = Instantiate(
            _dirtyGlassPrefab,
            coffeeTableDeliveryPoint.position,
            coffeeTableDeliveryPoint.rotation);

        // Configure as a dirty dish that won't smell until the next day
        var po = _spawnedDirtyGlass.GetComponent<PlaceableObject>();
        if (po != null)
            po.ConfigureHome("CoffeeTable");
    }

    /// <summary>
    /// Reset all drink minigame state at end of Phase 2 so it's clean for the next date.
    /// Clears glass contents, re-enables hidden glasses, and idles the pour manager.
    /// </summary>
    private void ResetDrinkMinigame()
    {
        // Force pour manager back to idle
        DrinkPourManager.Instance?.ForceIdle();

        // Clear contents from all glasses and re-enable any that were hidden
        var glasses = DrinkGlass.All;
        for (int i = glasses.Count - 1; i >= 0; i--)
        {
            if (glasses[i] == null) continue;
            glasses[i].Clear();
            if (!glasses[i].gameObject.activeSelf)
                glasses[i].gameObject.SetActive(true);
        }
    }

    [Header("Arrival Cinematic — Face Sweep")]
    [Tooltip("Enable the close-up face sweep when the date arrives.")]
    [SerializeField] private bool _enableArrivalSweep = true;

    [Tooltip("How close to the character's head the camera pushes (world units).")]
    [SerializeField] private float _sweepDistance = 1.2f;

    [Tooltip("How far the camera pans horizontally across the face (world units).")]
    [SerializeField] private float _sweepWidth = 0.6f;

    [Tooltip("FOV/ortho size during the face sweep.")]
    [SerializeField] private float _sweepFOV = 2.5f;

    [Tooltip("Duration of the sweep pan (seconds).")]
    [SerializeField] private float _sweepDuration = 2.5f;

    [Tooltip("Hold on the face after sweep before pulling back (seconds).")]
    [SerializeField] private float _sweepHold = 0.8f;

    [Tooltip("Duration of the pull-back to normal framing (seconds).")]
    [SerializeField] private float _sweepReturnDuration = 0.8f;

    [Tooltip("Angle in degrees the camera approaches from (0 = right, 90 = behind, 180 = left, 270 = front).")]
    [SerializeField] private float _sweepApproachAngle = 0f;

    [Tooltip("Camera height offset from character at the start of the sweep.")]
    [SerializeField] private float _sweepStartHeight = 0.85f;

    [Tooltip("Camera height offset from character at the end of the sweep.")]
    [SerializeField] private float _sweepEndHeight = 0.85f;

    [Tooltip("Height on the character the camera looks at (0 = feet, 0.85 = head).")]
    [SerializeField] private float _sweepLookAtHeight = 0.85f;

    [Header("Drink Verdict Cinematic")]
    [Tooltip("How long the camera takes to zoom toward the date character.")]
    [SerializeField] private float _verdictZoomDuration = 2.0f;

    [Tooltip("How close to the date character the camera pushes (world units from character center).")]
    [SerializeField] private float _verdictZoomDistance = 1.5f;

    [Tooltip("FOV/ortho size for the verdict close-up.")]
    [SerializeField] private float _verdictZoomFOV = 3.0f;

    [Tooltip("Enable the orbit swirl around the date character during drink tasting.")]
    [SerializeField] private bool _enableVerdictSwirl = true;

    [Tooltip("Angle in degrees where the orbit ends (e.g. start=0 end=540 = 1.5 orbits).")]
    [SerializeField] private float _swirlEndAngle = 540f;

    [Tooltip("Duration of the swirl orbit (seconds).")]
    [SerializeField] private float _swirlDuration = 2.0f;

    [Tooltip("Starting angle of the orbit in degrees (0 = right, 90 = forward, etc.).")]
    [SerializeField] private float _swirlStartAngle = 0f;

    [Tooltip("Vertical offset from the character at the start of the swirl (world units).")]
    [SerializeField] private float _swirlStartHeight = 2.0f;

    [Tooltip("Vertical offset from the character at the end of the swirl (world units).")]
    [SerializeField] private float _swirlEndHeight = 0.8f;

    [Tooltip("Height offset on the character that the camera looks at during the swirl (0 = feet, 0.8 = chest, 1.5 = head).")]
    [SerializeField] private float _swirlLookAtHeight = 0.8f;

    // ──────────────────────────────────────────────────────────────
    // Arrival Face Sweep
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Close-up camera sweep across the date character's face.
    /// Camera pushes in, pans horizontally, holds, then returns to phase framing.
    /// Character name slides across the screen during the sweep.
    /// </summary>
    private IEnumerator ArrivalFaceSweep()
    {
        var am = ApartmentManager.Instance;
        var mainCam = Camera.main;
        if (am == null || mainCam == null || _dateCharacterGO == null) yield break;

        // Save starting camera state (parallax-free base) to return to
        Vector3 restorePos = am.CurrentBasePosition;
        Quaternion restoreRot = am.CurrentBaseRotation;
        float restoreFOV = am.CurrentBaseFOV;

        Vector3 charRoot = _dateCharacterGO.transform.position;
        Vector3 lookTarget = charRoot + Vector3.up * _sweepLookAtHeight;

        // Camera approach direction from configurable angle
        float approachRad = _sweepApproachAngle * Mathf.Deg2Rad;
        Vector3 camDir = new Vector3(Mathf.Cos(approachRad), 0f, Mathf.Sin(approachRad));

        // Close position along approach direction
        Vector3 closePos = charRoot + camDir * _sweepDistance;

        // Sweep direction: perpendicular to approach, in world XZ plane
        Vector3 sweepDir = Vector3.Cross(camDir, Vector3.up).normalized;

        // Start and end positions for the horizontal pan
        Vector3 sweepStartXZ = closePos - sweepDir * (_sweepWidth * 0.5f);
        Vector3 sweepEndXZ = closePos + sweepDir * (_sweepWidth * 0.5f);
        Vector3 sweepStart = new Vector3(sweepStartXZ.x, charRoot.y + _sweepStartHeight, sweepStartXZ.z);
        Vector3 sweepEnd = new Vector3(sweepEndXZ.x, charRoot.y + _sweepEndHeight, sweepEndXZ.z);

        // Character name title removed — "Impressions" title drop follows
        // immediately and was overwriting this, causing a double-flash.

        // Phase 1: Push in from current position to sweep start
        bool skipped = false;
        float pushDuration = 0.6f;
        float elapsed = 0f;
        while (elapsed < pushDuration && !skipped)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / pushDuration);
            Vector3 pos = Vector3.Lerp(restorePos, sweepStart, t);
            float fov = Mathf.Lerp(restoreFOV, _sweepFOV, t);
            Quaternion rot = Quaternion.Slerp(restoreRot,
                Quaternion.LookRotation(lookTarget - sweepStart, Vector3.up), t);
            am.SetPresetBase(pos, rot, fov);
            yield return null;
            if (CinematicSkipRequested()) skipped = true;
        }

        // Phase 2: Sweep horizontally across the face
        if (!skipped)
        {
            elapsed = 0f;
            while (elapsed < _sweepDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / _sweepDuration);
                Vector3 pos = Vector3.Lerp(sweepStart, sweepEnd, t);
                Quaternion rot = Quaternion.LookRotation(lookTarget - pos, Vector3.up);
                am.SetPresetBase(pos, rot, _sweepFOV);
                yield return null;
                if (CinematicSkipRequested()) { skipped = true; break; }
            }
        }

        // Phase 3: Hold on face (skip bypasses this)
        if (!skipped)
            yield return new WaitForSeconds(_sweepHold);

        // Phase 4: Pull back to original framing
        if (!skipped)
        {
            elapsed = 0f;
            while (elapsed < _sweepReturnDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / _sweepReturnDuration);
                Vector3 pos = Vector3.Lerp(sweepEnd, restorePos, t);
                float fov = Mathf.Lerp(_sweepFOV, restoreFOV, t);
                Quaternion rot = Quaternion.Slerp(
                    Quaternion.LookRotation(lookTarget - sweepEnd, Vector3.up),
                    restoreRot, t);
                am.SetPresetBase(pos, rot, fov);
                yield return null;
                if (CinematicSkipRequested()) break;
            }
        }

        // Smooth glide back to phase camera (no hard snap)
        LerpPhaseCamera(DatePhase.Arrival, 0.3f);
    }

    /// <summary>Returns true when the player wants to skip a cinematic animation (click or Space).</summary>
    private static bool CinematicSkipRequested()
    {
        return Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
    }

    // ──────────────────────────────────────────────────────────────

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
            ? _dateCharacterGO.transform.position + Vector3.up * _swirlLookAtHeight
            : Vector3.zero;
        var am = ApartmentManager.Instance;
        // Use parallax-free base state to avoid double-counting parallax offset
        Vector3 camStartPos = am != null ? am.CurrentBasePosition : Vector3.zero;
        Quaternion camStartRot = am != null ? am.CurrentBaseRotation : Quaternion.identity;
        float camStartFOV = am != null ? am.CurrentBaseFOV : 5f;

        // Compute a close-up position looking at the date character
        Vector3 camDir = (camStartPos - zoomTarget).normalized;
        Vector3 camEndPos = zoomTarget + camDir * _verdictZoomDistance;

        // 4. Fade from white → characters floating in the sky
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeIn(0.5f);

        // 5. Zoom in + optional orbit swirl around the date (skippable)
        Vector3 finalCamPos;
        Quaternion finalCamRot;

        if (_enableVerdictSwirl)
        {
            // Orbit swirl: camera spirals around the character while zooming in
            float swirlElapsed = 0f;
            float totalDuration = Mathf.Max(_verdictZoomDuration, _swirlDuration);
            float startAngle = _swirlStartAngle * Mathf.Deg2Rad;
            float endAngle = _swirlEndAngle * Mathf.Deg2Rad;
            float totalAngle = endAngle - startAngle;
            float startDist = Vector3.Distance(camStartPos, zoomTarget);

            // Compute final position for skip
            Vector3 endOrbitPos = zoomTarget + new Vector3(Mathf.Cos(endAngle), 0f, Mathf.Sin(endAngle)) * _verdictZoomDistance;
            endOrbitPos.y = zoomTarget.y + _swirlEndHeight;
            finalCamPos = endOrbitPos;
            finalCamRot = Quaternion.LookRotation(zoomTarget - endOrbitPos, Vector3.up);

            while (swirlElapsed < totalDuration)
            {
                swirlElapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, swirlElapsed / totalDuration);

                // Spiral in: distance decreases, angle increases
                float dist = Mathf.Lerp(startDist, _verdictZoomDistance, t);
                float angle = startAngle + totalAngle * t;
                float height = Mathf.Lerp(zoomTarget.y + _swirlStartHeight, zoomTarget.y + _swirlEndHeight, t);

                Vector3 orbitPos = zoomTarget + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist;
                orbitPos.y = height;

                // Camera always looks at the character
                Quaternion lookRot = Quaternion.LookRotation(zoomTarget - orbitPos, Vector3.up);
                float fov = Mathf.Lerp(camStartFOV, _verdictZoomFOV, t);

                am?.SetPresetBase(orbitPos, lookRot, fov);
                yield return null;
                if (CinematicSkipRequested()) break;
            }
        }
        else
        {
            // Simple straight zoom (fallback)
            finalCamPos = camEndPos;
            finalCamRot = camStartRot;

            float zoomElapsed = 0f;
            while (zoomElapsed < _verdictZoomDuration)
            {
                zoomElapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, zoomElapsed / _verdictZoomDuration);

                Vector3 pos = Vector3.Lerp(camStartPos, camEndPos, t);
                float fov = Mathf.Lerp(camStartFOV, _verdictZoomFOV, t);
                am?.SetPresetBase(pos, camStartRot, fov);

                yield return null;
                if (CinematicSkipRequested()) break;
            }
        }

        // Snap to final close-up (in case we skipped mid-animation)
        am?.SetPresetBase(finalCamPos, finalCamRot, _verdictZoomFOV);

        // 6. Suspense — thinking face
        if (reactionUI != null && reactionUI.gameObject.activeInHierarchy) reactionUI.ShowText("Hmm...", _drinkTastingHold);
        yield return CacheDrinkTastingWait();

        // 7. Verdict reaction
        reactionUI?.ShowLabeledReaction(reactionType, drinkName);
        ApplyReaction(reactionType, magnitude);

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

#if UNITY_EDITOR
        Debug.Log($"[DateSessionManager] Drink verdict: {drinkName} (score={score}) \u2192 {reactionType}");
#endif

        // 9. Wait for player to acknowledge (still in the cinematic close-up)
        if (PhaseContinueButton.Instance != null)
        {
            bool clicked = false;
            PhaseContinueButton.Instance.Show(() => clicked = true);
            yield return new WaitUntil(() => clicked);
        }

        // 10. Fade out, restore apartment, go straight to Phase 3
        //     (skip returning to Phase 2 camera — transition handles its own fade-in)
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeOut(0.5f);

        RestoreApartmentRenderers(hiddenRenderers);
        // Don't ClearPresetBase here — that would flash the kitchen camera.
        // TransitionToPhase3 snaps the Phase 3 camera while still faded.

        // Transition to Phase 3 while still faded — it does its own fade-in
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

        // Reset pan so the phase framing is exact (no leftover player pan)
        ApartmentManager.Instance.ResetPanOffset();

        ApartmentManager.Instance.SetPresetBase(
            frame.position,
            Quaternion.Euler(frame.rotation),
            frame.fov,
            frame.nearClip,
            frame.farClip,
            frame.perspective,
            frame.perspectiveFOV);

        // Apply per-phase pan limit (0 = locked, small value = tight bounds)
        float panLim = frame.panLimit == 0f ? 0.5f : frame.panLimit; // guard uninitialized 0
        ApartmentManager.Instance.SetPresetPanLimit(panLim);

        // Force zoom step if configured (-1 = don't override)
        ApartmentManager.Instance.ForceZoomStep(frame.zoomStep);

        // Allow zoom within a range if configured
        int minStep = frame.zoomStepMin >= 0 ? frame.zoomStepMin : frame.zoomStep;
        int maxStep = frame.zoomStepMax >= 0 ? frame.zoomStepMax : frame.zoomStep;
        if (minStep != maxStep && minStep >= 0 && maxStep >= 0)
            ApartmentManager.Instance.SetPresetZoomRange(minStep, maxStep);
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

        // Reset pan so start/end positions are consistent with the phase framing
        ApartmentManager.Instance.ResetPanOffset();

        // Apply pan limit and zoom for the target phase immediately
        float panLim = frame.panLimit == 0f ? 0.5f : frame.panLimit;
        ApartmentManager.Instance.SetPresetPanLimit(panLim);
        ApartmentManager.Instance.ForceZoomStep(frame.zoomStep);

        // Allow zoom within a range if configured
        int minStep = frame.zoomStepMin >= 0 ? frame.zoomStepMin : frame.zoomStep;
        int maxStep = frame.zoomStepMax >= 0 ? frame.zoomStepMax : frame.zoomStep;
        if (minStep != maxStep && minStep >= 0 && maxStep >= 0)
            ApartmentManager.Instance.SetPresetZoomRange(minStep, maxStep);

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
            elapsed += Time.unscaledDeltaTime;
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
        _cachedApartmentRenderers = null;
        _cachedBottles = null;
        PerfumeBottle.ClearLastSprayed();
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
        _cachedApartmentRenderers = null;
        _cachedBottles = null;
        PerfumeBottle.ClearLastSprayed();
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

    /// <summary>Returns true if this tag has already been scored this date. Marks it as scored if not.</summary>
    public bool TryMarkScored(ReactableTag tag)
    {
        if (tag == null) return false;
        return !_scoredTags.Add(tag); // Add returns false if already present
    }

    private void HandleCharacterReaction(ReactableTag tag, ReactionType type, string displayName)
    {
        // Each item only affects the score once per date
        if (tag != null && !_scoredTags.Add(tag)) return;

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
