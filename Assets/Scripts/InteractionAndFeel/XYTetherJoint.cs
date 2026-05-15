/**
 * @file XYTetherJoint.cs
 * @brief XYTetherJoint script.
 * @details
 * - Auto-generated Doxygen header. Expand @details with intent, invariants, and perf notes as needed.
 *
 * @ingroup flowers_runtime
 */

// File: XYTetherJoint.cs
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Rigidbody))]
/**
 * @file XYTetherJoint.cs
 * @brief Custom 2D-ish (XY) tether implemented using a ConfigurableJoint, with authored break logic and “feel” controls.
 *
 * @details
 * XYTetherJoint exists because Unity’s built-in joints do not give you enough *authorial control* over:
 * - “How it feels” as the tether stretches (soft zone, rising tension, pluck/pop behaviors)
 * - “When it breaks” (distance, travel, relative speed, own speed, force) with predictable thresholds
 * - “When it is allowed to break” (engagement gating, cut suppression windows)
 *
 * In Iris, this joint is not just physics: it is a ritual control surface. It is used to make
 * petals/leaves feel *attached* until the player applies enough intent (or physics chaos) to sever them,
 * while keeping results deterministic enough for scoring and session rules.
 *
 * ------------------------------------------------------------
 * What this component actually builds
 * ------------------------------------------------------------
 * At runtime, this script creates a @c ConfigurableJoint that:
 * - Connects this GameObject’s Rigidbody (@ref rb) to @ref connectedBody
 * - Allows linear motion on X/Y (free), locks Z, and locks all angular motion
 * - Uses xDrive/yDrive as a “pull back toward rest” force (spring + damper)
 * - Optionally uses joint projection to prevent explosive separation
 * - Applies @ref breakForce to the joint only if @ref BreakCriteria.Force is enabled and cut suppression is off
 *
 * The “tether” concept here is measured between:
 * - Point A: this object’s joint.anchor in world space
 * - Point B: connectedBody’s joint.connectedAnchor in world space
 *
 * The joint’s authored break logic can break *before* Unity would naturally break a joint,
 * by calling @ref ForceBreak based on stretch/speed/travel conditions.
 *
 * ------------------------------------------------------------
 * Responsibilities
 * ------------------------------------------------------------
 * - Create and own a runtime ConfigurableJoint (see @ref TryCreateJoint / @ref DestroyJoint).
 * - Maintain a rest baseline (rest vector @ref restAB) and compute stretch from it each FixedUpdate.
 * - Compute stable velocity signals (frame velocity or integrated/smoothed velocity).
 * - Track “travel” accumulators (absolute and relative) after an arming delay.
 * - Apply “feel” modifiers (adaptive drive / soft zone tension) by scaling spring/damper each frame.
 * - Decide when to break based on selected @ref BreakCriteria.
 * - Enforce *when breaks are allowed*:
 *    - Static cut suppression (@ref cutBreakSuppressed)
 *    - Optional “only break while engaged” gating via @ref InteractionEngagement
 * - On break, trigger:
 *    - Canonical detachment state write to @ref FlowerPartRuntime (permanent)
 *    - Optional audio responder
 *    - Optional fluid responder or deterministic sap emission
 *    - @ref onBroke UnityEvent
 * - Provide small public API methods for retuning (designer and runtime).
 *
 * ------------------------------------------------------------
 * Non-Responsibilities
 * ------------------------------------------------------------
 * - Does not score the flower.
 * - Does not decide which parts are “critical”; it only reports breaks and writes detach state.
 * - Does not own the cutting system; the cutter may suppress break behavior via @ref SetCutBreakSuppressed.
 * - Does not render UI (but emits events/hooks for UI/audio/haptics).
 *
 * ------------------------------------------------------------
 * Key Data & Invariants
 * ------------------------------------------------------------
 * Invariants that matter for correctness and debuggability:
 * - If @ref connectedBody is null, no joint exists and FixedUpdate exits immediately.
 * - If @ref joint is null, this component is “inactive” as a tether until re-created.
 * - @ref restAB is captured at joint creation and defines “rest distance”.
 * - Stretch is defined as:
 *    stretch = max(0, currentDistance - restDistance)
 * - Normalized stretch is defined as:
 *    stretchNorm = clamp01(stretch / maxDistance)
 * - Travel accumulators only update after arming:
 *    Time.time >= @ref armedAt (armedAt = Time.time + armDelay)
 * - Break callbacks and authored breaks must be suppressible during cutting:
 *    @ref cutBreakSuppressed disables joint break force and ignores break calls.
 *
 * Engagement gating:
 * - If @ref onlyBreakWhenEngaged is true, authored breaks (ForceBreak) are blocked unless:
 *    @c _engagement != null && _engagement.isEngaged
 * - @note Unity’s physical joint breaking (OnJointBreak) is also suppressed by cut suppression,
 *         but not by engagement gating (because physics may break it regardless).
 *
 * Canonical detachment:
 * - On any break that is not suppressed, this component attempts to mark the corresponding
 *   @ref FlowerPartRuntime as detached with a permanent reason.
 * - This prevents “zombie rebinding” where broken parts snap back due to other systems.
 *
 * ------------------------------------------------------------
 * Break Modes (What “break” means here)
 * ------------------------------------------------------------
 * This joint can break in three ways:
 *  1) Unity breaks the underlying ConfigurableJoint (OnJointBreak callback),
 *     typically due to @ref breakForce being exceeded when Force criteria is active.
 *  2) The script calls @ref ForceBreak (authored break) due to thresholds like:
 *     - Distance (stretch)
 *     - RelativeSpeed / OwnSpeed
 *     - AbsoluteTravel / RelativeTravel
 *  3) External code calls @ref ForceBreak intentionally (player rip / scripted break)
 *
 * ------------------------------------------------------------
 * “Feel” System (Nintendo-ish tension, without making logic nondeterministic)
 * ------------------------------------------------------------
 * - Adaptive Drive:
 *   When @ref useAdaptiveDrive is enabled, the joint’s drive spring/damper are scaled each FixedUpdate
 *   based on stretchNorm fed through @ref tensionCurve, then blended into:
 *     - springMultiplier in [minSpringMultiplier, maxSpringMultiplier]
 *     - damperMultiplier in [minDamperMultiplier, maxDamperMultiplier]
 *   This preserves deterministic break thresholds while changing *perceived* tension.
 *
 * - Soft Zone:
 *   @ref softZoneFraction describes the portion of maxDistance that should feel “gentle”.
 *   The curve gives you control over how quickly tension rises within and after the soft zone.
 *
 * - Pluck / Pop:
 *   @ref usePluckDwell can auto-break after staying above a stretch fraction for a duration.
 *   @ref breakOnReleaseFromHighStretch can instead break on the *release* (tension falls below a threshold)
 *   after being pulled high—useful for “pop on let-go” feel.
 *
 * - Tension Event:
 *   @ref onTensionChanged emits normalized tension (0..1) each FixedUpdate when adaptive drive is active.
 *   Use this for audio pitch/volume, subtle haptics, or UI feedback (without per-frame string building).
 *
 * ------------------------------------------------------------
 * Cut Suppression (critical for cutter stability)
 * ------------------------------------------------------------
 * Cutting can momentarily create forces and joint stresses that would incorrectly break tethers.
 * To prevent false breaks, this script provides a *static* suppression switch:
 * - @ref SetCutBreakSuppressed(true) sets joint breakForce to infinity and resets accumulators on all joints.
 * - @ref SetCutBreakSuppressed(false) restores breakForce behavior based on criteria.
 *
 * @note SetCutBreakSuppressed iterates the static s_all registry (O(n) over active joints, no scene scan).
 *
 * ------------------------------------------------------------
 * Performance & Allocation Notes
 * ------------------------------------------------------------
 * - FixedUpdate is the hot path. Avoid allocations and avoid expensive component searches here.
 * - This script intentionally caches:
 *    - @ref rb, @ref joint, previous anchor world positions, integrated velocities,
 *      travel accumulators, base spring/damper, and cached references to FlowerPartRuntime/session.
 * - Debug logs are optional but can be extremely spammy in physics-heavy scenes.
 *   Gate them with @ref debugLogs and avoid string formatting in tight loops unless enabled.
 *
 * ------------------------------------------------------------
 * Integration Points
 * ------------------------------------------------------------
 * - FlowerPartRuntime:
 *   - This script writes authoritative detach state via @ref MarkPartDetachedAuthoritative.
 * - FlowerSessionController:
 *   - Detach state writes respect @c session.suppressDetachEvents for cut/rebind windows.
 * - InteractionEngagement:
 *   - Optional: scales force and gates breaking based on player engagement state.
 * - Feedback responders:
 *   - JointBreakAudioResponder (optional)
 *   - JointBreakFluidResponder (optional)
 *   - Deterministic sap emission (optional) via FlowerSapController + SapOnXYTether
 *
 * ------------------------------------------------------------
 * Visual Maps
 * ------------------------------------------------------------
 * @section viz_relationships_xytether Visual Relationships
 * @dot
 * digraph XYTetherJoint_Relations {
 *   rankdir=LR;
 *   node [shape=box];
 *
 *   "XYTetherJoint" -> "ConfigurableJoint"        [label="creates + owns"];
 *   "XYTetherJoint" -> "Rigidbody (self)"         [label="drives"];
 *   "XYTetherJoint" -> "Rigidbody (connectedBody)"[label="connects"];
 *   "XYTetherJoint" -> "FlowerPartRuntime"        [label="MarkDetached(permanent)"];
 *   "XYTetherJoint" -> "FlowerSessionController"  [label="respects suppressDetachEvents"];
 *   "InteractionEngagement" -> "XYTetherJoint"    [label="engagement scaling + break gating"];
 *   "XYTetherJoint" -> "JointBreakAudioResponder" [label="optional"];
 *   "XYTetherJoint" -> "JointBreakFluidResponder" [label="optional"];
 *   "XYTetherJoint" -> "FlowerSapController"      [label="optional deterministic sap"];
 *   "XYTetherJoint" -> "onBroke (UnityEvent)"     [label="notifies"];
 * }
 * @enddot
 *
 * @ingroup flowers_runtime
 */
public class XYTetherJoint : MonoBehaviour
{
    public enum TestSpace { XYOnly, XYZ }
    public enum VelocityMode { Rigidbody, Integrated }

    [System.Flags]
    public enum BreakCriteria
    {
        None = 0,
        Force = 1 << 0,
        Distance = 1 << 1,   // stretch-from-rest
        RelativeSpeed = 1 << 2,
        OwnSpeed = 1 << 3,
        AbsoluteTravel = 1 << 4,
        RelativeTravel = 1 << 5
    }

    [System.Serializable]
    /**
     * @class FloatEvent
     * @brief FloatEvent component.
     * @details
     * Responsibilities:
     * - (Documented) See fields and methods below.
     *
     * Unity lifecycle:
     * - Awake(): cache references / validate setup.
     * - OnEnable()/OnDisable(): hook/unhook events.
     * - Update(): per-frame behavior (if any).
     *
     * Gotchas:
     * - Keep hot paths allocation-free (Update/cuts/spawns).
     * - Prefer event-driven UI updates over per-frame string building.
     *
     * @ingroup flowers_runtime
     */
    public class FloatEvent : UnityEvent<float> { }

    // ───────────────────────── Connection ─────────────────────────

    [Header("Connection")]
    public Rigidbody connectedBody;

    [Header("Behavior")]
    [Tooltip("Break if STRETCH beyond rest exceeds this.")]
    public float maxDistance = 0.75f;
    public float spring = 1200f;
    public float damper = 60f;

    // ───────────────────────── Break Conditions ─────────────────────────

    [Header("Break Conditions")]
    public BreakCriteria criteria = BreakCriteria.Force | BreakCriteria.Distance;
    public TestSpace testSpace = TestSpace.XYOnly;
    public float armDelay = 0.05f;
    public float breakForce = Mathf.Infinity;
    public float relativeSpeedThreshold = 6f;
    public float ownSpeedThreshold = 8f;
    public float absoluteTravelThreshold = 5f;
    public float relativeTravelThreshold = 5f;

    [Header("Velocity Sampling")]
    public VelocityMode velocityMode = VelocityMode.Integrated;
    public float velocitySmoothing = 0.1f;

    [Header("Drive Cap & Projection")]
    public float driveMaxForce = 500f;
    public bool useJointProjection = true;
    public float projectionDistance = 0.02f;

    [Header("Constraints")]
    public bool enforceXYConstraints = true;

    // MERGED (from small script intent): allow collisions between connected bodies (SpringJoint.enableCollision equivalent)
    [Header("Joint Collision")]
    [Tooltip("If true, allows collision between the two connected bodies (ConfigurableJoint.enableCollision).")]
    public bool enableJointCollision = true;

    // ───────────────────────── Feel / Nintendo-ish Stuff ─────────────────────────

    [Header("Soft Zone / Adaptive Tension")]
    [Tooltip("If true, spring/damper are scaled based on stretch/maxDistance using tensionCurve.")]
    public bool useAdaptiveDrive = false;

    [Tooltip("Portion of maxDistance that counts as 'soft zone'. 0.6 = first 60% is gentle.")]
    [Range(0f, 1f)] public float softZoneFraction = 0.6f;

    [Tooltip("X: normalized stretch (0..1), Y: tension (0..1).")]
    public AnimationCurve tensionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Spring multiplier at tension=0 (completely slack).")]
    public float minSpringMultiplier = 0.25f;

    [Tooltip("Spring multiplier at tension=1 (max stretch).")]
    public float maxSpringMultiplier = 1.5f;

    [Tooltip("Damper multiplier at tension=0 (completely slack).")]
    public float minDamperMultiplier = 0.25f;

    [Tooltip("Damper multiplier at tension=1 (max stretch).")]
    public float maxDamperMultiplier = 1.5f;

    [Header("Pluck / Pop Feel")]
    [Tooltip("If true, holding past a stretch fraction for dwell time will auto-break (pluck).")]
    public bool usePluckDwell = false;

    [Tooltip("Stretch fraction (0..1 of maxDistance) at which pluck dwell starts counting.")]
    [Range(0f, 1f)] public float pluckThresholdFraction = 0.8f;

    [Tooltip("Time we must stay above pluckThresholdFraction before auto-break.")]
    public float pluckDwellSeconds = 0.08f;

    [Tooltip("If true, break only when tension FALLS back below a threshold after being pulled high (pop on release).")]
    public bool breakOnReleaseFromHighStretch = false;

    [Tooltip("Stretch fraction (0..1) we must drop BELOW after having exceeded pluckThresholdFraction to pop.")]
    [Range(0f, 1f)] public float releasePopThresholdFraction = 0.4f;

    [Header("Feel Events")]
    [Tooltip("Fired with normalized tension (0..1) each FixedUpdate. Use for audio, haptics, etc.")]
    public FloatEvent onTensionChanged;

    [Header("Engagement Scaling")]
    [Tooltip("If true, scale all forces/break checks by an engagement factor.")]
    public bool useEngagementScaling = true;

    [Tooltip("Override: how strong this joint is when directly engaged (if 0, use 1).")]
    [Range(0f, 2f)] public float engagedMultiplier = 1f;

    [Tooltip("Override for passive intensity (if 0, use InteractionEngagement.passiveIntensity).")]
    [Range(0f, 1f)] public float passiveMultiplierOverride = 0f;

    [Tooltip("If true, joint will only be allowed to break while engaged.")]
    public bool onlyBreakWhenEngaged = true;

    private InteractionEngagement _engagement;

    // ───────────────────────── FEEDBACK TOGGLES ─────────────────────────
    [Header("Feedback")]
    [Tooltip("If true, attempts to find and trigger JointBreakAudioResponder.")]
    public bool enableAudio = true;

    [Tooltip("If true, attempts to find and trigger JointBreakFluidResponder.")]
    public bool enableFluid = true;

    // ADDED: optional direct sap emission (keeps responder system intact by default)
    [Tooltip("If true, tear sap is emitted deterministically from this joint break, instead of relying on bleedPoint.forward.")]
    public bool preferDeterministicSapDirection = false;

    // ───────────────────────── Static cut suppression ─────────────────────────

    // PERF: Static registry avoids expensive FindObjectsByType calls in SetCutBreakSuppressed
    private static readonly System.Collections.Generic.List<XYTetherJoint> s_all = new(32);
    public static System.Collections.Generic.IReadOnlyList<XYTetherJoint> All => s_all;

    public static bool cutBreakSuppressed = false;
    public static bool IsCutBreakSuppressed => cutBreakSuppressed;

    /// <summary>
    /// Reset cut suppression on domain reload (Enter Play Mode Options safety)
    /// and on every scene load so a stuck flag can never persist across scenes.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        s_all.Clear();
        cutBreakSuppressed = false;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoadedResetSuppression;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoadedResetSuppression;
    }

    private static void OnSceneLoadedResetSuppression(
        UnityEngine.SceneManagement.Scene scene,
        UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (mode == UnityEngine.SceneManagement.LoadSceneMode.Single)
            cutBreakSuppressed = false;
    }

    public static void SetCutBreakSuppressed(bool on)
    {
        cutBreakSuppressed = on;

        for (int i = s_all.Count - 1; i >= 0; i--)
        {
            var t = s_all[i];
            if (t == null) continue;

            // Reset accumulators in both directions: entering suppression
            // clears stale data, leaving suppression prevents accumulated
            // drift from triggering instant breaks and re-arms the grace window.
            t.ResetBreakAccumulators();

            t.ApplyBreakForceToJoint();
        }
    }

    // ───────────────────────── Events / Debug ─────────────────────────

    [Header("Events")]
    public UnityEvent onBroke;

    [Header("Debug / Viz")]
    public bool debugLogs = true;
    public bool drawGizmos = true;
    public bool logLiveDistance = false;
    public Color lineColor = new Color(0f, 1f, 1f, 0.9f);
    public Color limitColor = new Color(1f, 0.3f, 0f, 0.6f);

    // ───────────────────────── Internals ─────────────────────────

    private Rigidbody rb;
    private ConfigurableJoint joint;
    private float armedAt = -999f;
    private float logTimer;

    private Vector3 prevA, prevB;
    private float absoluteTravel, relativeTravel;
    private Vector3 restAB;
    private Vector3 vA_int, vB_int;

    private float baseSpring;
    private float baseDamper;

    private float pluckTimer;
    private bool wasAbovePluckThreshold;
    private bool _breakForceArmed;

    // Startup grace: track creation time and retry count to suppress false physics breaks
    // during initial settling (large scale objects, collider overlaps, etc.)
    private float _jointCreatedAt;
    private int _startupRetries;
    private const int MAX_STARTUP_RETRIES = 5;
    private const float STARTUP_GRACE_SECONDS = 0.25f;

    private float lastTension;

    // ADDED: cache part runtime so we can mark permanent detaches (stops zombie rebinding)
    private FlowerPartRuntime _partRuntime;

    // ADDED: cache session for suppression check and clarity
    private FlowerSessionController _session;

    // PERF: cached feedback responders (avoids GetComponent on every break)
    private JointBreakAudioResponder _cachedAudio;
    private JointBreakFluidResponder _cachedFluid;
    private SapOnXYTether _cachedSapKind;

    /**
     * @brief Unity lifecycle setup for the tether’s Rigidbody constraints and cached references.
     *
     * @details
     * Configures this object’s Rigidbody for stable tether behavior:
     * - Ensures non-kinematic, interpolated motion with continuous collision.
     * - Optionally enforces XY-only constraints by freezing Z translation and all rotations.
     *
     * Caches references used in hot paths:
     * - Interaction engagement state (optional)
     * - FlowerPartRuntime (optional, used for authoritative detach)
     * - FlowerSessionController (optional, used for suppression windows)
     *
     * @note This method does not create the joint; joint creation is deferred to Start/OnEnable.
     *
     * MERGED FIX (from the smaller script):
     * - Do NOT forcibly set rb.isKinematic=false on stem pieces. Preserve kinematic state if StemPieceMarker exists.
     */
    void Awake()
    {
        rb = GetComponent<Rigidbody>();

        // MERGED FIX: Preserve stem piece kinematic state.
        bool isStemPiece = (GetComponent<StemPieceMarker>() != null);
        if (!isStemPiece)
        {
            rb.isKinematic = false;
        }
        // If it's a stem piece, preserve its existing kinematic state.

        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        if (enforceXYConstraints)
        {
            // NOTE: Do NOT freeze world-Z position here. The ConfigurableJoint's
            // zMotion=Locked already handles Z-locking in joint-local space.
            // Adding world-Z freeze on top conflicts on rotated parts (e.g. leaves)
            // because world-Z != joint-local-Z, pinning the part to a 1D line.
            rb.constraints |= RigidbodyConstraints.FreezeRotationX
                            | RigidbodyConstraints.FreezeRotationY
                            | RigidbodyConstraints.FreezeRotationZ;
        }

        _engagement = GetComponent<InteractionEngagement>()
                   ?? GetComponentInParent<InteractionEngagement>();

        _partRuntime = GetComponent<FlowerPartRuntime>();
        _session = GetComponentInParent<FlowerSessionController>();

        // PERF: cache feedback responders once instead of GetComponent on every break
        _cachedAudio = GetComponent<JointBreakAudioResponder>();
        _cachedFluid = GetComponent<JointBreakFluidResponder>();
        _cachedSapKind = GetComponent<SapOnXYTether>();
    }

    /**
     * @brief Unity lifecycle entry point that attempts to create the underlying ConfigurableJoint.
     *
     * @details
     * Calls @ref TryCreateJoint once at startup. This is separated from Awake() so that other
     * components can assign @ref connectedBody during initialization before joint creation.
     */
    void Start() { _startupRetries = 0; TryCreateJoint(); }

    /**
     * @brief Unity lifecycle hook to restore the joint if the component is re-enabled.
     *
     * @details
     * If a joint does not exist and a @ref connectedBody is assigned, calls @ref TryCreateJoint.
     * This supports pooling and enable/disable flows without requiring scene reload.
     */
    void OnEnable()
    {
        s_all.Add(this);
        if (!joint && connectedBody) { _startupRetries = 0; TryCreateJoint(); }
    }

    /**
     * @brief Unity lifecycle hook to teardown the joint when disabled.
     *
     * @details
     * Calls @ref DestroyJoint to prevent orphaned joints from persisting while the component is inactive.
     */
    void OnDisable()
    {
        s_all.Remove(this);
        DestroyJoint();
    }

    /**
     * @brief Physics-step update that computes stretch/velocity/travel and triggers authored breaks.
     *
     * @details
     * FixedUpdate is the hot path for this component. It:
     * - Computes world-space anchor points A/B.
     * - Samples or integrates smoothed velocities (depending on @ref velocityMode).
     * - Updates travel accumulators after the arming delay (@ref armedAt):
     *     - @ref absoluteTravel: total movement of A in chosen test space
     *     - @ref relativeTravel: total change in (A-B) in chosen test space
     * - Computes rest distance and current distance, producing:
     *     - stretch = max(0, currentDistance - restDistance)
     *     - stretchNorm = clamp01(stretch / maxDistance)
     * - Applies adaptive drive (optional) to scale spring/damper based on tension curve and engagement.
     * - Evaluates “feel” break behaviors (optional):
     *     - pluck dwell
     *     - release pop
     * - Evaluates configured @ref BreakCriteria thresholds and calls @ref ForceBreak when exceeded.
     *
     * @note Break evaluation is skipped until Time.time >= @ref armedAt to avoid breaking
     *       immediately during initialization or teleports.
     *
     * @warning This method should remain allocation-free. Avoid adding GetComponent calls or
     *          string formatting unless strictly gated behind debug flags.
     */
    void FixedUpdate()
    {
        if (!joint || !connectedBody) return;

        Vector3 a = transform.TransformPoint(joint.anchor);
        Vector3 b = connectedBody.transform.TransformPoint(joint.connectedAnchor);

        float dt = Mathf.Max(Time.fixedDeltaTime, 1e-5f);
        Vector3 vA_frame = (a - prevA) / dt;
        Vector3 vB_frame = (b - prevB) / dt;
        float alpha = Mathf.Clamp01(dt / Mathf.Max(velocitySmoothing, dt));
        vA_int = Vector3.Lerp(vA_int, vA_frame, alpha);
        vB_int = Vector3.Lerp(vB_int, vB_frame, alpha);

        if (Time.time >= armedAt)
        {
            absoluteTravel += Dist(ApplySpace(a - prevA));
            relativeTravel += Dist(ApplySpace((a - b) - (prevA - prevB)));
        }

        prevA = a;
        prevB = b;

        float restDistance = Dist(restAB);
        float currentDistance = Dist(ApplySpace(a - b));
        float stretch = Mathf.Max(0f, currentDistance - restDistance);
        float stretchNorm = 0f;
        if (maxDistance > 0.0001f)
            stretchNorm = Mathf.Clamp01(stretch / maxDistance);

        if (useAdaptiveDrive && joint != null)
        {
            float tension = tensionCurve != null ? tensionCurve.Evaluate(stretchNorm) : stretchNorm;
            tension = Mathf.Clamp01(tension);

            float springMult = Mathf.Lerp(minSpringMultiplier, maxSpringMultiplier, tension);
            float damperMult = Mathf.Lerp(minDamperMultiplier, maxDamperMultiplier, tension);

            float engageFactor = GetEngagementFactor();

            var drive = joint.xDrive;
            drive.positionSpring = baseSpring * springMult * engageFactor;
            drive.positionDamper = baseDamper * damperMult * engageFactor;
            joint.xDrive = drive;
            joint.yDrive = drive;

            onTensionChanged?.Invoke(tension);
            lastTension = tension;
        }

        if (Time.time >= armedAt)
        {
            if (usePluckDwell)
            {
                if (stretchNorm >= pluckThresholdFraction)
                {
                    pluckTimer += dt;
                    if (pluckTimer >= pluckDwellSeconds)
                    {
                        ForceBreak(debugLogs ? $"Pluck dwell (stretchNorm={stretchNorm:F2})" : "Pluck dwell", isAuthoredPhysics: true);
                        return;
                    }
                }
                else
                {
                    pluckTimer = 0f;
                }
            }

            if (breakOnReleaseFromHighStretch)
            {
                if (stretchNorm >= pluckThresholdFraction)
                    wasAbovePluckThreshold = true;
                else if (wasAbovePluckThreshold && stretchNorm <= releasePopThresholdFraction)
                {
                    ForceBreak(debugLogs ? $"Release pop (stretchNorm={stretchNorm:F2})" : "Release pop", isAuthoredPhysics: true);
                    return;
                }
            }
        }

        if (logLiveDistance)
        {
            logTimer += dt;
            if (logTimer >= 0.2f)
            {
                if (debugLogs)
                    Debug.Log($"[XYTetherJoint] stretch={stretch:F3}  | absTravel={absoluteTravel:F2}  relTravel={relativeTravel:F2}", this);
                logTimer = 0f;
            }
        }

        if (Time.time < armedAt) return;

        // Startup grace: keep breakForce at Infinity AND skip all authored break
        // criteria (distance, speed, travel, pluck) during the settling window.
        // Without this, ForceBreak fires on the first FixedUpdate after arming
        // when initial drift exceeds maxDistance — the root cause of leaves
        // detaching on frame 1 despite arm-delay protection.
        if (Time.time - _jointCreatedAt < STARTUP_GRACE_SECONDS)
            return;

        // Arm the physics breakForce now that settling is over
        if (!_breakForceArmed)
        {
            _breakForceArmed = true;
            ApplyBreakForceToJoint();
        }

        Vector3 vA = velocityMode == VelocityMode.Rigidbody ? rb.linearVelocity : vA_int;
        Vector3 vB = velocityMode == VelocityMode.Rigidbody ? connectedBody.linearVelocity : vB_int;

        if ((criteria & BreakCriteria.Distance) != 0)
        {
            if (stretch > Mathf.Max(0.0001f, maxDistance))
            {
                ForceBreak(debugLogs ? $"Stretch {stretch:F3} > {maxDistance:F3}" : "Stretch", isAuthoredPhysics: true);
                return;
            }
        }

        if ((criteria & BreakCriteria.RelativeSpeed) != 0)
        {
            float relSpeed = Dist(ApplySpace(vA - vB));
            if (relSpeed > relativeSpeedThreshold)
            {
                ForceBreak(debugLogs ? $"RelativeSpeed {relSpeed:F2} > {relativeSpeedThreshold:F2}" : "RelativeSpeed", isAuthoredPhysics: true);
                return;
            }
        }

        if ((criteria & BreakCriteria.OwnSpeed) != 0)
        {
            float ownSpeed = Dist(ApplySpace(vA));
            if (ownSpeed > ownSpeedThreshold)
            {
                ForceBreak(debugLogs ? $"OwnSpeed {ownSpeed:F2} > {ownSpeedThreshold:F2}" : "OwnSpeed", isAuthoredPhysics: true);
                return;
            }
        }

        if ((criteria & BreakCriteria.AbsoluteTravel) != 0)
        {
            if (absoluteTravel >= absoluteTravelThreshold)
            {
                ForceBreak(debugLogs ? $"AbsoluteTravel {absoluteTravel:F2} >= {absoluteTravelThreshold:F2}" : "AbsoluteTravel", isAuthoredPhysics: true);
                return;
            }
        }

        if ((criteria & BreakCriteria.RelativeTravel) != 0)
        {
            if (relativeTravel >= relativeTravelThreshold)
            {
                ForceBreak(debugLogs ? $"RelativeTravel {relativeTravel:F2} >= {relativeTravelThreshold:F2}" : "RelativeTravel", isAuthoredPhysics: true);
                return;
            }
        }
    }

    /**
     * @brief Computes the current engagement scaling multiplier for forces/break checks.
     *
     * @details
     * If @ref useEngagementScaling is false, returns 1.
     * Otherwise, returns either an “engaged” multiplier or a “passive” multiplier depending on:
     * - whether @ref _engagement exists
     * - whether @c _engagement.isEngaged is true
     *
     * Engaged/passive values come from:
     * - @ref engagedMultiplier (if > 0)
     * - @ref passiveMultiplierOverride (if > 0), otherwise InteractionEngagement.passiveIntensity
     *
     * @return A multiplier typically in [0..2] used to scale spring/damper (and can be used by callers).
     */
    float GetEngagementFactor()
    {
        if (!useEngagementScaling)
            return 1f;

        float engaged = 1f;
        float passive = 0.25f;

        if (_engagement != null)
            passive = _engagement.passiveIntensity;

        if (engagedMultiplier > 0f) engaged = engagedMultiplier;
        if (passiveMultiplierOverride > 0f) passive = passiveMultiplierOverride;

        bool isEngaged = _engagement != null && _engagement.isEngaged;
        return isEngaged ? engaged : passive;
    }

    // ───────────────────────── Break callbacks ─────────────────────────

    /**
     * @brief Unity physics callback invoked when the underlying ConfigurableJoint breaks.
     *
     * @param force The break force reported by Unity.
     *
     * @details
     * This is the “physics decided it broke” pathway. The method:
     * - Exits immediately if static cut suppression is enabled (@ref cutBreakSuppressed).
     * - Optionally logs the break if force-based breaking is enabled.
     * - Writes authoritative detach state to @ref FlowerPartRuntime (permanent).
     * - Triggers optional audio and fluid/sap feedback.
     * - Clears local joint reference and invokes @ref onBroke.
     *
     * @warning This can be called during intense physics events; it must remain fast and allocation-free.
     */
    void OnJointBreak(float force)
    {
        if (cutBreakSuppressed)
        {
            if (debugLogs)
                Debug.Log($"[XYTetherJoint] OnJointBreak suppressed (force={force:F1}) due to cutBreakSuppressed.", this);
            return;
        }

        // Startup grace: physics settling can exceed low breakForce values on the first
        // few frames (large scale objects, collider overlaps, etc.). If we're still within
        // the grace window and haven't exhausted retries, recreate the joint instead of
        // accepting a false break.
        if (Time.time - _jointCreatedAt < STARTUP_GRACE_SECONDS && _startupRetries < MAX_STARTUP_RETRIES)
        {
            _startupRetries++;
            if (debugLogs)
                Debug.Log($"[XYTetherJoint] OnJointBreak suppressed during startup grace (force={force:F1}, retry {_startupRetries}/{MAX_STARTUP_RETRIES}). Recreating joint.", this);
            joint = null;
            TryCreateJoint();
            return;
        }

        if ((criteria & BreakCriteria.Force) != 0 && debugLogs)
            Debug.Log($"[XYTetherJoint] Joint broke by physics force = {force:F1}.", this);

        // Authoritative permanent detach state (prevents rebinder snapping back later)
        MarkPartDetachedAuthoritative(isPlayerAction: false, reasonText: debugLogs ? $"Physics JointBreak force={force:F1}" : "Physics JointBreak");

        TriggerBreakAudio();
        // Only emit fluid here for parts WITHOUT FlowerPartRuntime — parts with it
        // already get sap from FlowerPartRuntime.MarkDetached → EmitTearWithFollow.
        if (_partRuntime == null)
            TriggerBreakFluidOrDeterministicSap();

        joint = null;
        onBroke?.Invoke();
    }

    /**
     * @brief Forces an authored break of the tether (script-driven detachment).
     *
     * @param reason Human-readable reason for debug/logging and detach annotation.
     *
     * @details
     * This is the “authorial break” pathway used for deterministic rules such as:
     * - stretch threshold
     * - speed threshold
     * - travel threshold
     * - pluck dwell / release pop
     * - explicit external calls (e.g., player rip)
     *
     * Behavior:
     * - Exits if static cut suppression is enabled (@ref cutBreakSuppressed).
     * - If @ref onlyBreakWhenEngaged is true, exits unless currently engaged.
     * - Destroys the joint immediately via @ref DestroyJoint.
     * - Writes authoritative detach state to @ref FlowerPartRuntime (permanent).
     * - Triggers optional audio and fluid/sap feedback.
     * - Invokes @ref onBroke.
     */
    public void ForceBreak(string reason = "Forced", bool isAuthoredPhysics = false)
    {
        if (cutBreakSuppressed)
        {
            if (debugLogs)
                Debug.Log($"[XYTetherJoint] ForceBreak \"{reason}\" suppressed due to cutBreakSuppressed.", this);
            return;
        }

        // Engagement gating only applies to player-initiated breaks, not
        // authored physics breaks (distance / pluck / release-pop). After a
        // stem cut the separated piece can stretch the joint past limits while
        // the player is not actively grabbing the leaf.
        if (!isAuthoredPhysics && onlyBreakWhenEngaged && _engagement != null)
        {
            if (!_engagement.isEngaged)
            {
                if (debugLogs)
                    Debug.Log($"[XYTetherJoint] Suppressed break \"{reason}\" because not engaged.", this);
                return;
            }
        }

        if (debugLogs) Debug.Log($"[XYTetherJoint] Break → {reason}", this);

        DestroyJoint();

        MarkPartDetachedAuthoritative(isPlayerAction: !isAuthoredPhysics, reasonText: reason);

        TriggerBreakAudio();
        // Only emit fluid here for parts WITHOUT FlowerPartRuntime — parts with it
        // already get sap from FlowerPartRuntime.MarkDetached → EmitTearWithFollow.
        if (_partRuntime == null)
            TriggerBreakFluidOrDeterministicSap();

        onBroke?.Invoke();
    }

    /**
     * @brief Centralized canonical detach state write into FlowerPartRuntime.
     *
     * @param isPlayerAction If true, detachment is labeled as player-driven (PlayerRipped); otherwise PhysicsBreak.
     * @param reasonText Human-readable annotation for debugging and traceability.
     *
     * @details
     * This method exists to prevent inconsistent “broken but reattached” states by ensuring that
     * a joint break always results in:
     * - @ref FlowerPartRuntime.isAttached = false
     * - @ref FlowerPartRuntime.permanentlyDetached = true
     * - @ref FlowerPartRuntime.lastDetachReason set appropriately
     *
     * Suppression behavior:
     * - If a @ref FlowerSessionController exists and @c suppressDetachEvents is true,
     *   the state write is skipped. This mirrors the project-wide philosophy that cut/rebind
     *   grace windows must not produce accidental detach events.
     *
     * @note If @ref FlowerPartRuntime is not present on the same GameObject, this becomes a no-op.
     */
    private void MarkPartDetachedAuthoritative(bool isPlayerAction, string reasonText)
    {
        // Respect session suppression here too (keeps your existing philosophy)
        if (_session == null) _session = GetComponentInParent<FlowerSessionController>();
        if (_session != null && _session.suppressDetachEvents)
        {
            if (debugLogs)
                Debug.Log($"[XYTetherJoint] Detach state write skipped due to session.suppressDetachEvents: {reasonText}", this);
            return;
        }

        if (_partRuntime == null) _partRuntime = GetComponent<FlowerPartRuntime>();
        if (_partRuntime == null) return;

        var reason = isPlayerAction
            ? FlowerPartRuntime.DetachReason.PlayerRipped
            : FlowerPartRuntime.DetachReason.PhysicsBreak;

        // Permanent detach = true in both cases (prevents rebinding snapped leaves)
        _partRuntime.MarkDetached($"XYTether broke: {reasonText}", reason, permanent: true);
    }

    /**
     * @brief Triggers break fluid feedback, optionally using deterministic sap direction.
     *
     * @details
     * If @ref enableFluid is false, does nothing.
     * Otherwise:
     * - If @ref preferDeterministicSapDirection is false, calls @ref TriggerBreakFluid (responder-based).
     * - If true, attempts deterministic sap emission using:
     *     - FlowerSapController.Instance
     *     - optional SapOnXYTether component for kind/offset
     *   Direction is computed from connectedBody (source) toward this joint (impact point).
     *
     * @note If required dependencies are missing, falls back to responder-based behavior.
     */
    private void TriggerBreakFluidOrDeterministicSap()
    {
        if (!enableFluid) return;

        if (!preferDeterministicSapDirection)
        {
            TriggerBreakFluid(); // existing behavior
            return;
        }

        // Deterministic sap emission (optional): connectedBody -> this object
        var sap = FlowerSapController.Instance;
        if (sap == null) { TriggerBreakFluid(); return; }

        // Use cached SapOnXYTether to decide kind and offset.
        Vector3 pos = transform.position;
        if (_cachedSapKind != null) pos = transform.TransformPoint(_cachedSapKind.localOffset);

        Vector3 from = connectedBody ? connectedBody.worldCenterOfMass : (pos - transform.up);
        Vector3 dir = (pos - from);
        if (dir.sqrMagnitude < 0.0001f) dir = transform.up;
        dir.Normalize();

        // Pass transform so particles follow the detaching part
        if (_cachedSapKind != null)
        {
            if (_cachedSapKind.partKind == SapOnXYTether.PartKind.Leaf) sap.EmitLeafTear(pos, dir, transform);
            else sap.EmitPetalTear(pos, dir, transform);
        }
        else
        {
            sap.EmitLeafTear(pos, dir, transform);
        }
    }

    // ───────────────────────── FEEDBACK HELPERS ─────────────────────────

    /**
     * @brief Triggers audio feedback for joint break if enabled.
     *
     * @details
     * If @ref enableAudio is false, does nothing.
     * Otherwise attempts to find a JointBreakAudioResponder on this GameObject and calls it.
     *
     * @note This is intentionally “best-effort” to keep XYTetherJoint decoupled from specific audio implementations.
     */
    private void TriggerBreakAudio()
    {
        if (!enableAudio) return;

        if (_cachedAudio != null)
            _cachedAudio.OnJointBroken();
    }

    private void TriggerBreakFluid()
    {
        if (_cachedFluid != null)
            _cachedFluid.OnJointBroken();
    }

    // ───────────────────────── Public API ─────────────────────────

    public void SetConnectedBody(Rigidbody body)
    {
        connectedBody = body;
        _startupRetries = 0;
        TryCreateJoint();
    }

    /**
     * MERGED (from the small script):
     * Convenience method that matches the callsite you already had: UpdateConnectedBody(newBody).
     *
     * Behavior:
     * - If newBody is null, we tear down the joint (tether disabled).
     * - Otherwise we set connectedBody and rebuild the ConfigurableJoint so rest baseline re-captures.
     *
     * @note This keeps the "attach to new parent after cut" workflow without introducing SpringJoint.
     */
    public void UpdateConnectedBody(Rigidbody newBody)
    {
        connectedBody = newBody;

        if (newBody == null)
        {
            DestroyJoint();
            return;
        }

        _startupRetries = 0;
        TryCreateJoint();
    }


    /**
     * @brief Updates key tuning parameters and rebuilds the joint.
     *
     * @param newMaxDist New max stretch distance (used for stretch-based breaking).
     * @param newSpring New spring value for xDrive/yDrive.
     * @param newDamper New damper value for xDrive/yDrive.
     * @param newDriveMax Optional maximum drive force (if > 0).
     *
     * @details
     * Writes the new tuning values, optionally updates drive cap, then calls @ref TryCreateJoint
     * to apply them to a new ConfigurableJoint.
     *
     * @note Rebuilding resets arming/travel accumulators and rest baseline.
     */
    public void Retune(float newMaxDist, float newSpring, float newDamper, float newDriveMax = -1f)
    {
        maxDistance = newMaxDist;
        spring = newSpring;
        damper = newDamper;
        if (newDriveMax > 0f) driveMaxForce = newDriveMax;
        _startupRetries = 0;
        TryCreateJoint();
    }

    /**
     * @brief Convenience preset for making the tether more fragile.
     *
     * @details
     * Sets a set of parameters (maxDistance, breakForce, spring, damper, driveMaxForce)
     * intended to yield a joint that breaks with less stretch and less force.
     * Then rebuilds the joint via @ref TryCreateJoint.
     *
     * @note This is a designer-friendly shortcut and may be used for difficulty scaling,
     *       tutorialization, or “degrading integrity” mechanics.
     */
    public void MakeEasierToBreak(
        float newMaxDistance = 0.35f,
        float newBreakForce = 100f,
        float newDriveMax = 300f,
        float newSpring = 800f,
        float newDamper = 40f)
    {
        maxDistance = newMaxDistance;
        breakForce = newBreakForce;
        spring = newSpring;
        damper = newDamper;
        driveMaxForce = newDriveMax;
        _startupRetries = 0;
        TryCreateJoint();
    }

    // ───────────────────────── Joint Setup ─────────────────────────

    /**
     * @brief Creates and configures the underlying ConfigurableJoint for XY tether behavior.
     *
     * @details
     * This is the core setup method. It:
     * - Destroys any existing joint.
     * - Validates @ref connectedBody and clamps key parameters to safe ranges.
     * - Caches base spring/damper for adaptive drive scaling.
     * - Adds a new ConfigurableJoint with:
     *     - linear motion free in X/Y, locked in Z
     *     - angular motion locked
     *     - xDrive/yDrive configured with spring/damper and capped by @ref driveMaxForce
     *     - connectedAnchor set to the connectedBody’s local position corresponding to this transform
     * - Configures optional projection settings.
     * - Applies breakForce behavior via @ref ApplyBreakForceToJoint.
     * - Captures rest baseline and resets sampling accumulators.
     * - Arms break evaluation after @ref armDelay (sets @ref armedAt).
     *
     * @warning This uses AddComponent at runtime and therefore allocates; it should be called
     *          sparingly (setup, tuning changes, connect changes), not per-frame.
     */
    void TryCreateJoint()
    {
        DestroyJoint();

        if (!connectedBody)
        {
            if (debugLogs) Debug.LogWarning("[XYTetherJoint] No connectedBody assigned.", this);
            return;
        }

        maxDistance = Mathf.Max(0.0001f, maxDistance);
        spring = Mathf.Max(0f, spring);
        damper = Mathf.Max(0f, damper);
        driveMaxForce = Mathf.Max(0f, driveMaxForce);

        baseSpring = spring;
        baseDamper = damper;

        joint = gameObject.AddComponent<ConfigurableJoint>();
        joint.connectedBody = connectedBody;
        joint.autoConfigureConnectedAnchor = false;

        // MERGED: mirror SpringJoint.enableCollision intent
        joint.enableCollision = enableJointCollision;

        joint.anchor = Vector3.zero;
        joint.connectedAnchor = connectedBody.transform.InverseTransformPoint(transform.position);

        joint.xMotion = ConfigurableJointMotion.Free;
        joint.yMotion = ConfigurableJointMotion.Free;
        joint.zMotion = ConfigurableJointMotion.Locked;

        joint.angularXMotion = ConfigurableJointMotion.Locked;
        joint.angularYMotion = ConfigurableJointMotion.Locked;
        joint.angularZMotion = ConfigurableJointMotion.Locked;

        JointDrive drive = new JointDrive
        {
            positionSpring = baseSpring,
            positionDamper = baseDamper,
            maximumForce = driveMaxForce
        };
        joint.xDrive = drive;
        joint.yDrive = drive;
        joint.zDrive = new JointDrive();

        joint.targetPosition = Vector3.zero;

        if (useJointProjection)
        {
            joint.projectionMode = JointProjectionMode.PositionAndRotation;
            joint.projectionDistance = projectionDistance;
        }
        else
        {
            joint.projectionMode = JointProjectionMode.None;
        }

        ApplyBreakForceToJoint();

        Vector3 a = transform.TransformPoint(joint.anchor);
        Vector3 b = connectedBody.transform.TransformPoint(joint.connectedAnchor);
        restAB = ApplySpace(a - b);
        prevA = a; prevB = b;
        absoluteTravel = 0f; relativeTravel = 0f;
        vA_int = vB_int = Vector3.zero;

        pluckTimer = 0f;
        wasAbovePluckThreshold = false;
        _breakForceArmed = false;
        _jointCreatedAt = Time.time;

        armedAt = Time.time + Mathf.Max(0f, armDelay);

        if (debugLogs)
        {
            string bf = float.IsInfinity(joint.breakForce) ? "∞" : joint.breakForce.ToString("F0");
            Debug.Log($"[XYTetherJoint] Created → Spring={spring}, Damper={damper}, StretchMax={maxDistance}, DriveMax={driveMaxForce}, BreakForce={bf}, Criteria={criteria}, VelMode={velocityMode}, Projection={(useJointProjection ? "On" : "Off")}", this);
        }
    }

    /**
     * @brief Applies break force settings to the underlying ConfigurableJoint based on criteria, suppression, and arm state.
     *
     * @details
     * breakForce is set to infinity (suppressed) when any of these are true:
     * - @ref cutBreakSuppressed (cutting grace window)
     * - @ref _breakForceArmed is false (arm delay window — prevents settling forces from
     *   snapping joints on the first physics frames after creation)
     *
     * Otherwise:
     * - joint.breakForce is set to @ref breakForce if @ref BreakCriteria.Force is enabled,
     *   else infinity.
     * - joint.breakTorque remains infinity (rotational breaking is not used).
     */
    void ApplyBreakForceToJoint()
    {
        if (!joint) return;

        // Suppress physics breakForce during cut suppression AND during the arm delay
        // window. Settling forces on the first few physics frames can exceed low
        // breakForce values and falsely snap joints before authored logic even runs.
        if (cutBreakSuppressed || !_breakForceArmed)
        {
            joint.breakForce = Mathf.Infinity;
            joint.breakTorque = Mathf.Infinity;
        }
        else
        {
            joint.breakForce = ((criteria & BreakCriteria.Force) != 0) ? breakForce : Mathf.Infinity;
            joint.breakTorque = Mathf.Infinity;
        }
    }

    void ResetBreakAccumulators()
    {
        absoluteTravel = 0f;
        relativeTravel = 0f;
        pluckTimer = 0f;
        wasAbovePluckThreshold = false;
        _breakForceArmed = false;
        armedAt = Time.time + Mathf.Max(0f, armDelay);
    }

    /**
     * @brief Returns whether a ConfigurableJoint currently exists and is owned by this component.
     *
     * @return True if @ref joint is non-null; otherwise false.
     */
    public bool HasActiveJoint() => joint != null;

    /**
     * @brief Destroys the owned ConfigurableJoint if it exists.
     *
     * @details
     * This removes the runtime joint component, clears the cached reference, and (optionally)
     * logs when debug logging is enabled.
     *
     * @note This does not alter @ref connectedBody. It only removes the active tether.
     */
    void DestroyJoint()
    {
        if (joint)
        {
            if (debugLogs) Debug.Log("[XYTetherJoint] Destroying joint.", this);
            Destroy(joint);
            joint = null;
        }
    }

    /**
     * @brief Applies the configured test space (XY-only vs XYZ) to a vector.
     *
     * @param v Input vector (usually a delta or velocity).
     * @return Vector in the selected test space.
     *
     * @details
     * When @ref testSpace is XYOnly, Z is zeroed so that stretch/speed/travel calculations
     * ignore Z entirely. This keeps break behavior stable for 2.5D setups.
     */
    Vector3 ApplySpace(Vector3 v) => (testSpace == TestSpace.XYOnly) ? new Vector3(v.x, v.y, 0f) : v;
    static float Dist(Vector3 v) => v.magnitude;

    /**
     * @brief Editor-only visualization of tether line and break radius.
     *
     * @details
     * Draws a line between this object and the connected anchor, and a wire sphere indicating
     * @ref maxDistance at the connected anchor. This provides immediate feedback for tuning.
     *
     * @note This does not run in builds. It should remain lightweight.
     */
    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Vector3 a = transform.position;
        Vector3 b;

        if (joint && connectedBody)
            b = connectedBody.transform.TransformPoint(joint.connectedAnchor);
        else if (connectedBody)
            b = connectedBody.transform.position;
        else
            return;

        Gizmos.color = lineColor; Gizmos.DrawLine(a, b);
        Gizmos.color = limitColor; Gizmos.DrawWireSphere(b, Mathf.Max(0.0001f, maxDistance));
    }
}
