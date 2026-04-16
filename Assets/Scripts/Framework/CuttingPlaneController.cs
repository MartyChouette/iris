/**
 * @file CuttingPlaneController.cs
 * @brief Moves the plane cutter and triggers cuts (input → scissors → feedback → PlaneBehaviour.Cut()).
 *
 * @details
 * ## Role in Iris
 * This component is the “operator hand” for the plane tool. It does **not** cut meshes itself.
 * Instead it:
 * - Moves the cutting plane up/down (Y height control).
 * - Reads a “cut” input action.
 * - Asks @ref ScissorsVisualController for permission to snip (visual + cooldown gate).
 * - Spawns feedback (audio + fluids) based on what the plane overlaps at the moment of the cut.
 * - Calls @ref DynamicMeshCutter.PlaneBehaviour.Cut() to perform the actual mesh cut.
 *
 * ## Ownership gating (scissors pickup)
 * Iris intent: the player should **not** have cutting control until scissors are equipped from the table.
 * This script therefore supports a hard gate:
 * - @ref SetToolEnabled(bool) enables/disables *movement + cutting input*.
 * - Your pickup system (ScissorStation) should call SetToolEnabled(true/false).
 *
 * ## Anti-accidental cut (pickup click safety)
 * If pickup uses the same input as cutting (e.g., LMB), the pickup click can immediately trigger a cut.
 * This controller provides two layers of defense:
 * 1) A short “arm delay” window after enable (Time.unscaledTime < _cutArmedAtTime).
 * 2) A “release latch” that ignores cut input until it has been released once.
 *
 * ## Mouse-height takeover safety (prevents plane jumping on equip)
 * If mouse vertical position drives height, enabling the tool can cause the plane to “snap” to the mouse.
 * Optional behavior:
 * - Hold the plane at its authored/current Y until the mouse cursor moves near the plane’s current height,
 *   then enable mouse-follow (see preserveAuthoredHeightUntilMouseMatches + mouseHeightTakeoverDeadzonePx).
 *
 * ## Angle integration
 * This controller has **no staging lock**. Height movement is always allowed while the tilt controller
 * (PlaneAngleTiltController) rotates the plane concurrently.
 *
 * ## UI safety
 * Movement and cutting are both blocked when the pointer is over UI
 * (EventSystem.IsPointerOverGameObject()). The check is cached once per frame.
 *
 * ## Performance notes
 * - Update() is hot: avoid allocations.
 * - Physics.OverlapBox allocates the returned array (Unity API). Keep cutSense volume tight and
 *   call only on cut presses (not per-frame).
 *
 * @ingroup tools
 *
 * @section viz_cuttingplanecontroller Visual Relationships
 * @dot
 * digraph CuttingPlaneController {
 *   rankdir=LR;
 *   node [shape=box];
 *   CuttingPlaneController -> "PlaneBehaviour" [label="calls Cut()"];
 *   CuttingPlaneController -> "ScissorsVisualController" [label="AttemptSnip() gate"];
 *   CuttingPlaneController -> "AudioManager" [label="PlaySFX / PlayDualSFX"];
 *   CuttingPlaneController -> "FluidSquirter" [label="Squirt() feedback"];
 * }
 * @enddot
 */

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using DynamicMeshCutter;

[DisallowMultipleComponent]
public class CuttingPlaneController : MonoBehaviour
{
    public enum ControlMode { KeyboardWASD, MouseOnly, MouseAndKeyboard, Gamepad, Touchscreen }
    private enum CutHitKind { None, Stem, Leaf, Petal }

    [Header("Control Mode")]
    public ControlMode controlMode = ControlMode.MouseAndKeyboard;

    [Header("References")]
    [Tooltip("The PlaneBehaviour responsible for actually performing the mesh cut.")]
    public PlaneBehaviour plane;

    [Tooltip("Visual/cooldown gate. Must be asked before a cut fires.")]
    public ScissorsVisualController scissorsVisuals;

    // ─────────────────────────────────────────────────────────────
    // Spline constraint (optional)
    // ─────────────────────────────────────────────────────────────

    [Header("Stem Spline (Optional)")]
    [Tooltip("When assigned, scissors follow this spline instead of the Y-rail.")]
    public StemSplineGenerator stemSpline;

    [Tooltip("Speed at which mouse delta drives the spline t parameter.")]
    [SerializeField] float splineTSpeed = 0.003f;

    // ─────────────────────────────────────────────────────────────
    // Movement / authored pose
    // ─────────────────────────────────────────────────────────────

    [Header("Movement Settings (Y Height)")]
    [Tooltip("Units per second when using axis-based height movement.")]
    public float axisMoveSpeed = 2f;

    [Tooltip("Clamp is relative to the authored start Y. (min offset from start)")]
    public float minYOffset = -1f;

    [Tooltip("Clamp is relative to the authored start Y. (max offset from start)")]
    public float maxYOffset = 1f;

    [Tooltip("If true, mouse vertical delta drives plane height (Y only).")]
    public bool useMouseHeight = true;

    [Header("Mouse Delta Feel (Y Only)")]
    [Tooltip("World units per mouse pixel (delta). Increase this to move up/down faster.")]
    [SerializeField] float mouseYWorldPerPixel = 0.01f;

    [Tooltip("Holding Shift multiplies mouse delta movement.")]
    [SerializeField] float fastMultiplier = 4f;

    [Tooltip("Optional acceleration: bigger drags move more.")]
    [SerializeField] float accelPerPixel = 0.05f;

    [Tooltip("Smooth time for Y glide (smaller = snappier).")]
    [SerializeField] float ySmoothTime = 0.06f;

    // ─────────────────────────────────────────────────────────────
    // Input
    // ─────────────────────────────────────────────────────────────

    [Header("Input Actions")]
    [Tooltip("Axis (float or Vector2.y) controlling plane height.")]
    public InputActionReference moveYAction;

    [Tooltip("Pointer screen position (Vector2). Reserved for future; NOT used for movement in the 'Y-only glide' mode.")]
    public InputActionReference pointerPositionAction;

    [Tooltip("Button action used to trigger cuts.")]
    public InputActionReference cutAction;

    // ─────────────────────────────────────────────────────────────
    // Cut feedback classification volume
    // ─────────────────────────────────────────────────────────────

    [Header("Cut Detection Volume (Feedback Classification Only)")]
    [Tooltip("Half-thickness of the overlap box in Y/Z (radius-like).")]
    public float cutSenseRadius = 0.04f;

    [Tooltip("Length of the overlap box along plane forward.")]
    public float cutSenseLength = 1.0f;

    [Tooltip("Layers considered for cut feedback classification (stem/leaf/petal).")]
    public LayerMask cutDetectionMask = ~0;

    // ─────────────────────────────────────────────────────────────
    // Audio / fluid feedback
    // ─────────────────────────────────────────────────────────────

    [Header("Cut SFX")]
    public AudioClip stemCutPrimary;
    public AudioClip stemCutSecondary;
    public float stemSecondaryDelay = 0.08f;

    public AudioClip leafCutPrimary;
    public AudioClip leafCutSecondary;
    public float leafSecondaryDelay = 0.08f;

    public AudioClip petalCutPrimary;
    public AudioClip petalCutSecondary;
    public float petalSecondaryDelay = 0.08f;

    [Header("Cut Fluids")]
    public FluidSquirter genericFluidPlane;
    public FluidSquirter stemFluidPlane;
    public FluidSquirter leafFluidPlane;
    public FluidSquirter petalFluidPlane;

    [Header("Gore Control")]
    [Range(0f, 1f)]
    [Tooltip("0 disables fluid feedback; 1 is full intensity.")]
    public float goreIntensity = 1f;

    [Header("Advanced (Pose Root)")]
    [Tooltip("If set, height movement is applied to this transform (recommended: VisualAsset). If null, we auto-pick plane.transform.parent if it exists.")]
    public Transform planePoseRootOverride;

    private Transform _poseTransform;   // the thing we actually move in Y (and want to share with tilt)
    private float _authoredStartLocalY;
    private float _lockedLocalX;
    private float _lockedLocalZ;

    // ─────────────────────────────────────────────────────────────
    // Ownership gating
    // ─────────────────────────────────────────────────────────────

    [Header("Tool Ownership Gate (Pickup/Putdown)")]
    [Tooltip("When false, plane control + cutting are disabled entirely (no scissors equipped).")]
    [SerializeField] private bool _toolEnabled = false;

    [Tooltip("If true, PlaneBehaviour.enabled is toggled alongside tool ownership.")]
    public bool togglePlaneBehaviourWithTool = true;

    // ─────────────────────────────────────────────────────────────
    // Anti-accidental cut
    // ─────────────────────────────────────────────────────────────

    [Header("Anti-Accidental Cut (On Enable)")]
    [Tooltip("Prevents an immediate cut when the tool is enabled (e.g., same click used to pick up scissors).")]
    public bool preventInstantCutOnEnable = true;

    [Tooltip("How long after enabling before cuts are allowed (seconds). Uses unscaled time.")]
    public float cutArmDelay = 0.12f;

    [Tooltip("If true, require the cut input to be released once after enable before cutting is allowed.")]
    public bool requireReleaseAfterEnable = true;

    // ─────────────────────────────────────────────────────────────
    // Debug
    // ─────────────────────────────────────────────────────────────

    [Header("Debug")]
    public bool debugLogs = false;
    public bool drawDetectionGizmo = true;
    public Color detectionGizmoColor = new Color(1f, 0f, 0f, 0.25f);

    // ─────────────────────────────────────────────────────────────
    // Internal state
    // ─────────────────────────────────────────────────────────────

    private Transform _planeTransform;

    // PERF: Pre-allocated buffer for Physics.OverlapBoxNonAlloc to avoid GC
    private static readonly Collider[] _overlapBuffer = new Collider[32];

    private float _authoredStartY;
    private float _lockedX;
    private float _lockedZ;

    private float _targetY;
    private float _yVel;

    /// <summary>Normalized position along the stem spline (0 = anchor, 1 = tip).</summary>
    private float _splineT = 0.5f;

    private float _cutArmedAtTime = -999f;



    /// <summary>
    /// When true, we ignore cut input until the cut control is released once.
    /// Used to prevent "pickup click" from immediately cutting.
    /// </summary>
    [SerializeField] private bool _suppressCutUntilRelease = false;

    /// <summary>Cached per-frame to avoid redundant EventSystem queries.</summary>
    private bool _isPointerOverUI;

    public bool IsToolEnabled => _toolEnabled;

    private void Reset()
    {
        plane = GetComponentInChildren<PlaneBehaviour>(true);
    }

    private void Awake()
    {
        if (plane == null) plane = GetComponentInChildren<PlaneBehaviour>(true);

        var planeTf = (plane != null) ? plane.transform : transform;

        // ✅ YOU NEED THIS BACK (Update + HandleCutEffects rely on it)
        _planeTransform = planeTf;

        // ✅ Move the shared pose root (VisualAsset) so tilt + height are combined.
        _poseTransform = planePoseRootOverride != null
            ? planePoseRootOverride
            : (planeTf.parent != null ? planeTf.parent : planeTf);

        CaptureAuthoredPoseForClampAndLock();

        if (minYOffset > maxYOffset)
        {
            float tmp = minYOffset;
            minYOffset = maxYOffset;
            maxYOffset = tmp;
        }
    }


    private void Start()
    {
        // Enforce starting state consistently.
        SetToolEnabled(_toolEnabled);
    }

    private void OnEnable()
    {
        if (_toolEnabled)
        {
            EnableAction(moveYAction);
            EnableAction(pointerPositionAction);
            EnableAction(cutAction);
        }
    }

    private void OnDisable()
    {
        DisableAction(moveYAction);
        DisableAction(pointerPositionAction);
        DisableAction(cutAction);
    }

    /// <summary>
    /// Called by ScissorStation (or any pickup/putdown system).
    /// When disabled, this controller stops moving the plane and stops cutting.
    /// </summary>
    public void SetToolEnabled(bool enabled)
    {
        _toolEnabled = enabled;

        if (togglePlaneBehaviourWithTool && plane != null)
            plane.enabled = enabled;

        if (enabled)
        {
            // Re-capture start pose at equip time so whatever you authored *now* becomes the clamp anchor.
            CaptureAuthoredPoseForClampAndLock();

            EnableAction(moveYAction);
            EnableAction(pointerPositionAction);
            EnableAction(cutAction);

            if (preventInstantCutOnEnable)
                _cutArmedAtTime = Time.unscaledTime + Mathf.Max(0f, cutArmDelay);

            if (requireReleaseAfterEnable)
                _suppressCutUntilRelease = true;

            // Optional: reset scissors visuals/cooldown so we don't snap shut on pickup.
            if (scissorsVisuals != null)
                scissorsVisuals.ResetCooldown();

            if (debugLogs)
                Debug.Log($"[CuttingPlaneController] ToolEnabled → true (startY={_authoredStartY:0.###}, lockXZ=({_lockedX:0.###},{_lockedZ:0.###}), armedAt={_cutArmedAtTime:0.###})", this);
        }
        else
        {
            DisableAction(moveYAction);
            DisableAction(pointerPositionAction);
            DisableAction(cutAction);

            _suppressCutUntilRelease = false;

            if (debugLogs)
                Debug.Log("[CuttingPlaneController] ToolEnabled → false", this);
        }
    }

    /// <summary>
    /// Call immediately after enabling tool ownership to prevent the pickup click from cutting.
    /// Cutting is blocked until the cut input is released once.
    /// </summary>
    public void SuppressCutUntilReleased()
    {
        _suppressCutUntilRelease = true;

        if (debugLogs)
            Debug.Log("[CuttingPlaneController] SuppressCutUntilReleased()", this);
    }

    private void Update()
    {
        if (!_toolEnabled) return;
        if (_planeTransform == null) return;

        _isPointerOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        // ─────────────────────────────────────────────────────────────
        // MOVEMENT (blocked when pointer is over UI)
        // ─────────────────────────────────────────────────────────────

        if (!_isPointerOverUI)
        {
            bool useAxis = false;
            bool useMouseDelta = false;

            switch (controlMode)
            {
                case ControlMode.KeyboardWASD: useAxis = true; break;
                case ControlMode.MouseOnly: useMouseDelta = true; break;
                case ControlMode.MouseAndKeyboard: useAxis = true; useMouseDelta = true; break;
                case ControlMode.Gamepad: useAxis = true; break;
                case ControlMode.Touchscreen: useMouseDelta = true; break;
            }

            // ── Spline-following mode ──
            if (stemSpline != null)
            {
                float delta = 0f;

                if (useAxis)
                {
                    float axis = ReadAxis(moveYAction);
                    if (Mathf.Abs(axis) > 0.0001f)
                        delta += axis * axisMoveSpeed * Time.deltaTime * splineTSpeed * 10f;
                }

                if (useMouseDelta && useMouseHeight)
                {
                    float dy = 0f;
                    if (Mouse.current != null)
                        dy = Mouse.current.delta.ReadValue().y;
                    else
                        dy = Input.GetAxisRaw("Mouse Y");

                    float mult = (Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed) ? fastMultiplier : 1f;
                    float accel = 1f + Mathf.Abs(dy) * accelPerPixel;

                    delta += dy * splineTSpeed * mult * accel;
                }

                _splineT = Mathf.Clamp01(_splineT + delta);

                Vector3 worldPos = stemSpline.EvaluateWorld(_splineT);
                _poseTransform.position = worldPos;
            }
            // ── Fallback: Y-rail mode (original behavior) ──
            else
            {
                if (useAxis)
                {
                    float axis = ReadAxis(moveYAction);
                    if (Mathf.Abs(axis) > 0.0001f)
                        _targetY += axis * axisMoveSpeed * Time.deltaTime;
                }

                if (useMouseDelta && useMouseHeight)
                {
                    float dy = 0f;
                    if (Mouse.current != null)
                        dy = Mouse.current.delta.ReadValue().y;
                    else
                        dy = Input.GetAxisRaw("Mouse Y");

                    float mult = (Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed) ? fastMultiplier : 1f;
                    float accel = 1f + Mathf.Abs(dy) * accelPerPixel;

                    _targetY += dy * mouseYWorldPerPixel * mult * accel;
                }

                float minY = _authoredStartLocalY + minYOffset;
                float maxY = _authoredStartLocalY + maxYOffset;
                _targetY = Mathf.Clamp(_targetY, minY, maxY);

                Vector3 lp = _poseTransform.localPosition;
                lp.x = _lockedLocalX;
                lp.z = _lockedLocalZ;
                lp.y = Mathf.SmoothDamp(lp.y, _targetY, ref _yVel, ySmoothTime);
                _poseTransform.localPosition = lp;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // CUT LOGIC
        // ─────────────────────────────────────────────────────────────

        if (cutAction?.action == null || !cutAction.action.enabled)
            return;

        // UI block (uses per-frame cached value)
        if (_isPointerOverUI)
            return;

        // (1) Arm delay
        if (preventInstantCutOnEnable && Time.unscaledTime < _cutArmedAtTime)
            return;

        // (2) Release latch
        if (_suppressCutUntilRelease)
        {
            bool pressed = cutAction.action.IsPressed();
            if (!pressed)
            {
                _suppressCutUntilRelease = false;
                if (debugLogs) Debug.Log("[CuttingPlaneController] Cut released → cuts re-enabled", this);
            }
            return;
        }

        // Only cut on a fresh press this frame.
        if (!cutAction.action.WasPerformedThisFrame())
            return;

        if (plane == null || !plane.enabled)
        {
            if (debugLogs) Debug.LogWarning("[CuttingPlaneController] Cut ignored: PlaneBehaviour missing/disabled.", this);
            return;
        }

        // Scissors cooldown/animation gate
        if (scissorsVisuals != null && scissorsVisuals.AttemptSnip() == false)
            return;

        HandleCutEffects();
        plane.Cut();
    }

    private void CaptureAuthoredPoseForClampAndLock()
    {
        if (_poseTransform == null) return;

        Vector3 lp = _poseTransform.localPosition;

        _authoredStartLocalY = lp.y;
        _lockedLocalX = lp.x;
        _lockedLocalZ = lp.z;

        _targetY = lp.y;
        _yVel = 0f;
    }


    // ─────────────────────────────────────────────────────────────
    // Input helpers
    // ─────────────────────────────────────────────────────────────

    private void EnableAction(InputActionReference actionRef)
    {
        if (actionRef?.action != null && !actionRef.action.enabled)
            actionRef.action.Enable();
    }

    private void DisableAction(InputActionReference actionRef)
    {
        if (actionRef?.action != null && actionRef.action.enabled)
            actionRef.action.Disable();
    }

    private float ReadAxis(InputActionReference actionRef)
    {
        if (actionRef?.action == null || !actionRef.action.enabled) return 0f;

        var action = actionRef.action;
        if (action.activeValueType == typeof(float)) return action.ReadValue<float>();
        if (action.activeValueType == typeof(Vector2)) return action.ReadValue<Vector2>().y;
        return 0f;
    }

    // ─────────────────────────────────────────────────────────────
    // Cut feedback
    // ─────────────────────────────────────────────────────────────

    private void HandleCutEffects()
    {
        if (_planeTransform == null) return;

        bool hasAnySfx =
            stemCutPrimary || stemCutSecondary ||
            leafCutPrimary || leafCutSecondary ||
            petalCutPrimary || petalCutSecondary;

        if (!hasAnySfx && goreIntensity <= 0f) return;

        Vector3 center = _planeTransform.position;
        Vector3 halfExtents = new Vector3(cutSenseLength * 0.5f, cutSenseRadius, cutSenseRadius);
        Quaternion rotation = _planeTransform.rotation;

        // PERF: Use NonAlloc version to avoid GC allocation
        int hitCount = Physics.OverlapBoxNonAlloc(center, halfExtents, _overlapBuffer, rotation, cutDetectionMask, QueryTriggerInteraction.Ignore);

        if (hitCount == 0)
        {
            TriggerFluid(genericFluidPlane, null);
            return;
        }

        Collider leafCol = null, petalCol = null, stemCol = null;

        for (int i = 0; i < hitCount; i++)
        {
            var col = _overlapBuffer[i];
            if (col == null) continue;

            var part = col.GetComponentInParent<FlowerPartRuntime>();
            var stem = col.GetComponentInParent<FlowerStemRuntime>();

            if (part != null)
            {
                if (part.kind == FlowerPartKind.Leaf) leafCol = col;
                else if (part.kind == FlowerPartKind.Petal) petalCol = col;
            }
            else if (stem != null)
            {
                if (stemCol == null) stemCol = col;
            }
            else
            {
                // Legacy tag fallback if needed
                if (stemCol == null && col.CompareTag("Stem")) stemCol = col;
                else if (leafCol == null && col.CompareTag("Leaf")) leafCol = col;
                else if (petalCol == null && col.CompareTag("Petal")) petalCol = col;
            }

            // PERF: Early exit if we found all types
            if (stemCol != null && leafCol != null && petalCol != null) break;
        }

        CutHitKind kind = CutHitKind.None;
        Collider chosen = null;

        if (stemCol != null) { kind = CutHitKind.Stem; chosen = stemCol; }
        else if (leafCol != null) { kind = CutHitKind.Leaf; chosen = leafCol; }
        else if (petalCol != null) { kind = CutHitKind.Petal; chosen = petalCol; }

        switch (kind)
        {
            case CutHitKind.Leaf:
                PlayCutDual(leafCutPrimary, leafCutSecondary, leafSecondaryDelay);
                TriggerFluid(leafFluidPlane, chosen);
                break;

            case CutHitKind.Petal:
                PlayCutDual(petalCutPrimary, petalCutSecondary, petalSecondaryDelay);
                TriggerFluid(petalFluidPlane, chosen);
                break;

            case CutHitKind.Stem:
                PlayCutDual(stemCutPrimary, stemCutSecondary, stemSecondaryDelay);
                TriggerFluid(stemFluidPlane, chosen);
                break;

            default:
                PlayCutDual(stemCutPrimary, stemCutSecondary, stemSecondaryDelay);
                TriggerFluid(genericFluidPlane, chosen);
                break;
        }
    }

    private void PlayCutDual(AudioClip first, AudioClip second, float delay)
    {
        if (AudioManager.Instance == null || (first == null && second == null)) return;

        const float trimVolume = 0.8f;
        if (second != null || delay > 0f)
            AudioManager.Instance.PlayDualSFX(first, second, delay, trimVolume);
        else
            AudioManager.Instance.PlaySFX(first, trimVolume);
    }

    private void TriggerFluid(FluidSquirter planeSquirter, Collider exampleCol)
    {
        float intensity = Mathf.Clamp01(goreIntensity);
        if (intensity <= 0f) return;

        if (planeSquirter != null)
            planeSquirter.Squirt(intensity, _planeTransform.position, _planeTransform.forward);

        if (exampleCol != null)
        {
            var squirters = exampleCol.GetComponentsInParent<FluidSquirter>();
            if (squirters != null && squirters.Length > 0)
            {
                Vector3 hitPoint = exampleCol.ClosestPoint(_planeTransform.position);
                Vector3 hitNormal = exampleCol.transform.up;

                foreach (var fs in squirters)
                    if (fs != null && fs != planeSquirter)
                        fs.Squirt(intensity, hitPoint, hitNormal);
            }
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!drawDetectionGizmo) return;

        Transform t = Application.isPlaying && plane != null ? plane.transform : transform;
        Gizmos.color = detectionGizmoColor;

        Matrix4x4 prev = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
        Gizmos.DrawCube(Vector3.zero, new Vector3(cutSenseLength, cutSenseRadius * 2f, cutSenseRadius * 2f));
        Gizmos.matrix = prev;
    }
#endif
}
