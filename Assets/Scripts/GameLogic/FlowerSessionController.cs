using System;
using UnityEngine;
using UnityEngine.Events;
/**
 * @file FlowerSessionController.cs
 * @brief FlowerSessionController script.
 * @details
 * Intent:
 * - Orchestrates a single flower session:
 *   delegates evaluation to @ref FlowerGameBrain,
 *   maps results through @ref FlowerTypeDefinition,
 *   and broadcasts outcomes to UI via UnityEvents.
 *
 * Events:
 * - OnSuccessfulEvaluation: fired when final result is not game over.
 * - OnGameOver: fired when final result is game over.
 * - OnResult: always fired with (EvaluationResult, finalScore, daysAlive) snapshot.
 *
 * Cut grace window:
 * - useCutGraceWindow + cutGraceDuration allow brief post-cut detach events without instant-failing.
 *   This exists to prevent "physics noise" from becoming narrative judgment.
 *
 * Failure containment:
 * - freezeOnGameOver optionally freezes all rigidbodies under brain so the flower does not keep collapsing.
 * - disableCollidersOnGameOver optionally prevents further physics events after failure.
 *
 * Outcome mapping:
 * - If FlowerType is present, score/days are derived from it.
 * - If FlowerType is missing, a fallback mapping is used (0–100 score, 0–7 days).
 *
 * Investigation guardrails (debug safety):
 * - Duplicate session detection: logs if more than one FlowerSessionController is alive.
 * - End-latching:
 *   - "End request" latch prevents multiple end triggers from racing (Evaluate/ForceGameOver/etc.).
 *   - "Result applied" latch prevents ApplyResult from running twice (and captures stack traces).
 * - Time scale safety: if forced slow-mo/pause occurs and this object is disabled mid-flow,
 *   timeScale is restored to avoid poisoning subsequent scenes.
 *
 * Gotchas:
 * - Keep hot paths allocation-free (Update/cuts/spawns).
 * - Prefer event-driven UI updates over per-frame string building.
 *
 * @ingroup flowers_runtime
 *
 * @section viz_flowersessioncontroller Visual Relationships
 * @dot
 * digraph FlowerSessionController {
 *   rankdir=LR;
 *   node [shape=box];
 *   FlowerSessionController -> FlowerGameBrain;
 *   FlowerSessionController -> FlowerTypeDefinition;
 *   FlowerSessionController -> FlowerStemRuntime;
 *   FlowerSessionController -> FlowerHUD;
 * }
 * @enddot
 */



[DisallowMultipleComponent]
public class FlowerSessionController : MonoBehaviour
{
    [Header("Core Refs")]
    public FlowerGameBrain brain;
    public FlowerTypeDefinition FlowerType;

    [Tooltip("Joint rebinder for this flower. If null, auto-found via brain hierarchy.")]
    public FlowerJointRebinder rebinder;

    [Header("Events")]
    public UnityEvent OnGameOver;
    public UnityEvent OnSuccessfulEvaluation;
    public UnityEvent<FlowerGameBrain.EvaluationResult, int, int> OnResult;

    [Header("Debug / Last Result")]
    public bool lastGameOver;
    public string lastGameOverReason;
    [Range(0f, 1f)]
    public float lastNormalizedScore;
    public int lastScore;
    public int lastDays;

    [Header("Cut / Detach Grace Window")]
    [Tooltip("If true, parts can detach during a short grace window after a cut without instantly failing the session.")]
    public bool useCutGraceWindow = true;

    [Tooltip("Duration (seconds) after a cut during which detach events are ignored for instant-fail.")]
    public float cutGraceDuration = 0.15f;

    [HideInInspector]
    public bool suppressDetachEvents = false;

    private float _cutGraceTimer = 0f;

    [Header("Physics Guard Rails")]
    [Tooltip("If true, all rigidbodies under the brain will be frozen (kinematic, zero velocity) on game over so the flower doesn't melt down when you fail.")]
    public bool freezeOnGameOver = true;

    [Tooltip("If true, all colliders under the brain will be disabled on game over (prevents further physics events after failure).")]
    public bool disableCollidersOnGameOver = false;

    [Header("Game Over Slow Motion")]
    [Tooltip("If true, forced game overs (like crown failure) will briefly go slow motion before fully pausing and showing the result UI.")]
    public bool useSlowMoOnForcedGameOver = true;

    [Tooltip("Time scale to use during the slow-motion window for forced game overs (0.01–1).")]
    [Range(0.01f, 1f)]
    public float forcedGameOverSlowMoScale = 0.1f;

    [Tooltip("Real-time duration (seconds) to stay in slow motion before fully pausing (Time.timeScale = 0).")]
    public float forcedGameOverSlowMoDuration = 0.5f;
    
    [Header("Runtime Flags")]
    [Tooltip("If true, session has already processed a final result (prevents double end).")]
    public bool sessionEnded = false;

    [Tooltip("If true, an end has been initiated (prevents double end during slow-mo delay).")]
    public bool endRequested = false;

    public bool allowKeyboardEvaluate = false;

    // ─────────────────────────────────────────────
    // Investigation guardrails
    // ─────────────────────────────────────────────

    private static int s_liveSessions = 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_liveSessions = 0;
    }

    private bool _endRequested = false;
    private string _endRequestedStack = null;

    private bool _resultApplied = false;
    private string _resultAppliedStack = null;

    // Time-scale requests are now managed by TimeScaleManager (priority-based).
    // No local save/restore needed; the manager handles conflicts and scene-transition cleanup.

    private void Awake()
    {
        // Duplicate session detection (scene-authored + runtime-spawned overlap, additive loads, etc.)
        s_liveSessions++;
        if (s_liveSessions > 1)
            Debug.LogError($"[FlowerSessionController] DUPLICATE SESSION INSTANCE. live={s_liveSessions} this={name}", this);

        // Autofill brain for stability (avoids silent null wiring when prefabs drift).
        if (brain == null)
            brain = GetComponentInChildren<FlowerGameBrain>(true);

        if (rebinder == null && brain != null)
            rebinder = brain.GetComponentInParent<FlowerJointRebinder>();
        if (rebinder == null)
            rebinder = GetComponentInChildren<FlowerJointRebinder>(true);
    }

    private void OnDisable()
    {
        // Safety: if we got disabled mid slow-mo/pause, release our time-scale request
        // so it doesn't poison other scenes.
        TimeScaleManager.Clear(TimeScaleManager.PRIORITY_GAME_OVER);
    }

    private void OnDestroy()
    {
        s_liveSessions = Mathf.Max(0, s_liveSessions - 1);
    }

    private void Update()
    {
        // 1) Manage cut grace window timer
        if (useCutGraceWindow && suppressDetachEvents)
        {
            _cutGraceTimer -= Time.unscaledDeltaTime;
            if (_cutGraceTimer <= 0f)
            {
                suppressDetachEvents = false;
            }
        }

        // 2) Evaluate key (E to finish trimming)
        if (allowKeyboardEvaluate && !sessionEnded && Input.GetKeyDown(KeyCode.E))
        {
            EvaluateCurrentFlower();
        }
    }

    // ─────────────────────────────────────────────
    // End-latching helpers (investigation / anti-ghost)
    // ─────────────────────────────────────────────

    private bool TryRequestEndOnce(string reason)
    {
        if (sessionEnded)
            return false;

        // Unified end-latch: both the public endRequested flag and the private
        // _endRequested latch must agree. Previously the two were out of sync.
        if (_endRequested || endRequested)
        {
            Debug.LogError(
                $"[FlowerSessionController] END REQUESTED TWICE. reason={reason}\nfirst=\n{_endRequestedStack}\nsecond=\n{Environment.StackTrace}",
                this);
            return false;
        }

        _endRequested = true;
        endRequested = true;
        _endRequestedStack = Environment.StackTrace;
        return true;
    }

    private bool TryApplyResultOnce(string reason)
    {
        if (_resultApplied)
        {
            Debug.LogError(
                $"[FlowerSessionController] APPLY RESULT TWICE. reason={reason}\nfirst=\n{_resultAppliedStack}\nsecond=\n{Environment.StackTrace}",
                this);
            return false;
        }

        _resultApplied = true;
        _resultAppliedStack = Environment.StackTrace;
        return true;
    }

    /// <summary>
    /// Call this right after performing a stem cut so that brief detach flutters don't insta-fail.
    /// </summary>
    public void StartCutGraceWindow()
    {
        if (!useCutGraceWindow)
            return;

        suppressDetachEvents = true;
        _cutGraceTimer = cutGraceDuration;
    }

    /// <summary>
    /// Force a hard-fail of the session immediately (e.g., crown ripped off, stem cut way too high).
    /// This pushes a result through the scoring pipeline so HUD + grading UI can show it.
    /// </summary>
    public void ForceGameOver(string reason)
    {
        if (sessionEnded || endRequested)
        {
            Debug.LogWarning($"[FlowerSessionController] END REQUESTED TWICE. reason={reason}", this);
            return;
        }

        endRequested = true; // latch immediately so nothing else can request end during slow-mo

        if (brain == null)
        {
            lastGameOver = true;
            lastGameOverReason = reason;
            lastScore = 0;
            lastDays = 0;
            lastNormalizedScore = 0f;

            if (freezeOnGameOver)
                FreezeAllRigidbodies();

#if UNITY_EDITOR
            Debug.Log($"[FlowerSessionController] GAME OVER (no brain): {reason}", this);
#endif
            OnGameOver?.Invoke();
            OnResult?.Invoke(new FlowerGameBrain.EvaluationResult
            {
                isGameOver = true,
                gameOverReason = reason,
                scoreNormalized = 0f
            }, lastScore, lastDays);

            sessionEnded = true;
            return;
        }

        var result = brain.EvaluateFlower();
        result.isGameOver = true;
        result.gameOverReason = reason;

        if (useSlowMoOnForcedGameOver && gameObject.activeInHierarchy)
            StartCoroutine(CoHandleForcedGameOver(result));
        else
            ApplyResult(result);
    }


    private System.Collections.IEnumerator CoHandleForcedGameOver(FlowerGameBrain.EvaluationResult result)
    {
        // Enter slow motion if requested.
        if (forcedGameOverSlowMoScale > 0f)
        {
            TimeScaleManager.Set(TimeScaleManager.PRIORITY_GAME_OVER, forcedGameOverSlowMoScale);
        }

        // Wait in real-time so the slow-mo scale doesn't affect the delay.
        if (forcedGameOverSlowMoDuration > 0f)
        {
            yield return new WaitForSecondsRealtime(forcedGameOverSlowMoDuration);
        }

        // Fully pause gameplay while the grading screen is visible.
        TimeScaleManager.Set(TimeScaleManager.PRIORITY_GAME_OVER, 0f);

        ApplyResult(result);
    }

    /// <summary>
    /// Call this to evaluate the current flower state (e.g. when the player confirms they're done).
    /// </summary>
    public void EvaluateCurrentFlower()
    {
        if (sessionEnded || endRequested)
            return;

        if (brain == null)
            return;

        endRequested = true;

        var result = brain.EvaluateFlower();
        ApplyResult(result);
    }


    /// <summary>
    /// Call this right after a stem cut to see if we've cut "too high / too short"
    /// and should instantly game over.
    /// </summary>
    public void CheckStemCutImmediate()
    {
        if (brain == null || brain.ideal == null || brain.stem == null)
            return;

        float currentLen = brain.stem.CurrentLength;
        float signedDelta = currentLen - brain.ideal.idealStemLength;
        float absDelta = Mathf.Abs(signedDelta);

        // We only treat "too short" as instant fail: cut up into the crown area.
        if (brain.ideal.stemCanCauseGameOver
            && signedDelta < 0f
            && absDelta > brain.ideal.stemHardFailDelta)
        {
            ReleaseCrownAndFall("Stem cut too short (cut too high towards the crown).");
        }
    }

    // ─────────────────────────────────────────────
    // CROWN RELEASE + FALL (dramatic stem-fail)
    // ─────────────────────────────────────────────

    [Header("Crown Fall Settings")]
    [Tooltip("Seconds to wait for crownFailY to trigger game over before the safety-net timeout fires.")]
    public float crownFallTimeout = 3f;

    /// <summary>
    /// Releases the crown/held piece so it falls under gravity, then waits for the
    /// crownFailY failsafe (on FlowerPartRuntime) to trigger game over.
    /// If the crown lands on geometry and crownFailY never fires, a safety-net
    /// timeout calls ForceGameOver instead.
    /// </summary>
    public void ReleaseCrownAndFall(string reason, float fallTimeout = -1f)
    {
        if (sessionEnded || endRequested)
            return;

        if (fallTimeout < 0f)
            fallTimeout = crownFallTimeout;

#if UNITY_EDITOR
        Debug.Log($"[FlowerSessionController] ReleaseCrownAndFall: {reason} (timeout={fallTimeout}s)", this);
#endif

        // Release the held piece so it falls
        if (rebinder != null)
            rebinder.ReleaseHeldPieceForFall();
        else
            Debug.LogWarning("[FlowerSessionController] ReleaseCrownAndFall: no rebinder found, crown may not fall.", this);

        // Start safety-net coroutine
        if (gameObject.activeInHierarchy)
            StartCoroutine(CoCrownFallTimeout(reason, fallTimeout));
        else
            ForceGameOver(reason); // can't start coroutine, fall back to immediate
    }

    private System.Collections.IEnumerator CoCrownFallTimeout(string reason, float timeout)
    {
        // Wait in real-time so slow-mo doesn't stretch the timeout
        float elapsed = 0f;
        while (elapsed < timeout)
        {
            // If crownFailY (or anything else) already ended the session, bail out
            if (sessionEnded || endRequested)
                yield break;

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // Safety net: crown landed on something and crownFailY never fired
        if (!sessionEnded && !endRequested)
        {
#if UNITY_EDITOR
            Debug.Log($"[FlowerSessionController] CoCrownFallTimeout: safety-net triggered after {timeout}s. Forcing game over.", this);
#endif
            ForceGameOver(reason);
        }
    }

    // ─────────────────────────────────────────────
    // INTERNAL: unified result handling
    // ─────────────────────────────────────────────

    private void ApplyResult(FlowerGameBrain.EvaluationResult result)
    {
        if (sessionEnded)
            return;

        endRequested = true; // keep it true no matter how we got here

      

        if (!TryApplyResultOnce("ApplyResult"))
            return;

        if (brain != null)
        {
            brain.lastWasGameOver = result.isGameOver;
            brain.lastGameOverReason = result.gameOverReason;
            brain.lastScoreNormalized = result.scoreNormalized;
        }

        bool finalIsGameOver = result.isGameOver;
        string finalReason = result.gameOverReason;

        // Soft-fail mode: this flower type doesn't want true "hard game over",
        // so we treat even fatal violations like just a really bad score.
        if (FlowerType != null && !FlowerType.allowGameOver && result.isGameOver)
        {
            finalIsGameOver = false;
        }

        lastGameOver = finalIsGameOver;
        lastGameOverReason = finalReason;
        lastNormalizedScore = result.scoreNormalized;

        int score = 0;
        int days = 0;

        // ALWAYS compute score + days from normalized score, even on fail.
        if (FlowerType != null)
        {
            score = FlowerType.GetFinalScoreFromNormalized(result.scoreNormalized);
            days = FlowerType.GetDaysFromNormalized(result.scoreNormalized);
        }
        else
        {
            // Simple fallback: 0–100 score, 0–7 days.
            score = Mathf.RoundToInt(result.scoreNormalized * 100f);
            days = Mathf.RoundToInt(result.scoreNormalized * 7f);
        }

        lastScore = score;
        lastDays = days;

        if (!finalIsGameOver)
        {
#if UNITY_EDITOR
            Debug.Log($"[FlowerSessionController] EVALUATE OK → score={score}, days={days}, norm={result.scoreNormalized:0.###}", this);
#endif
            OnSuccessfulEvaluation?.Invoke();
        }
        else
        {
            if (freezeOnGameOver)
                FreezeAllRigidbodies();

#if UNITY_EDITOR
            Debug.Log($"[FlowerSessionController] GAME OVER → {finalReason} (score={score}, days={days}, norm={result.scoreNormalized:0.###})", this);
#endif
            OnGameOver?.Invoke();
        }

        // Always broadcast result + the final score/days snapshot to HUD / grading UI.
        OnResult?.Invoke(result, lastScore, lastDays);

        sessionEnded = true;
    }

    private void FreezeAllRigidbodies()
    {
        if (brain == null)
            return;

        Transform root = brain.transform;

        // Freeze all rigidbodies so they stop flopping when the player fails.
        var bodies = root.GetComponentsInChildren<Rigidbody>();
        foreach (var rb in bodies)
        {
            if (rb == null) continue;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        if (disableCollidersOnGameOver)
        {
            var cols = root.GetComponentsInChildren<Collider>();
            foreach (var c in cols)
            {
                if (c == null) continue;
                c.enabled = false;
            }
        }
    }
}

