using DynamicMeshCutter;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

/**
 * @file ScissorStation.cs
 * @brief World interaction point for equipping and returning scissors.
 *
 * @details
 * ## AI FAST CONTEXT (READ THIS FIRST)
 * **Project:** Iris
 * **This script’s job:** The central authority on whether the user is holding the scissors or if they are on the table.
 * **Non-goals:** * - Does NOT handle the cutting logic itself (handled by PlaneBehaviour).
 * - Does NOT handle the input for the cut (handled by CuttingPlaneController).
 * **Authority / source of truth:** This script is the master state machine for "Equipped" status.
 * **Safe to refactor:** Yes, specifically for adding SplineController logic in the `UpdatePosition` stub.
 *
 * ## DESIGN INTENT (PLAYER-FACING)
 * - **Seamless Transition:** The swap between "Table Visuals" and "Hand Visuals" must happen instantly.
 * - **Offset Control:** Allows fine-tuning the visual "grip" position via `toolPositionOffset`.
 * - **Future Pathing:** Built to support Spline-based movement (e.g., following a stem) instead of free-hand mouse movement.
 *
 * ## SYSTEM CONTRACTS (HARD RULES)
 * - The `returnMatCollider` MUST be a different object than the `scissorsClickableCollider`.
 * - `PlaneAngleTiltController` must only be enabled while equipped.
 * - **ANCHOR RULE:** You must hook into `OnEquipScissors` to Kinematically Lock the stem, otherwise it falls before the cut.
 *
 * ## OWNERSHIP / STATE MODEL
 * **States:** * - `Idle` (Scissors on table)
 * - `Equipped` (Scissors in hand/screen space)
 * - `Busy` (Transitioning/Animating)
 * **Who transitions state:** Player Input (Raycast Click) -> This Script.
 *
 * ## SCENE / PREFAB AUTHORING REQUIREMENTS
 * - **Hierarchy:** `activeScissorsRoot` must be the PARENT of the `PlaneBehaviour` to ensure offsets apply to the cut.
 * - **Splines:** If `useSplinePath` is true, `targetStemSpline` must be assigned.
 *
 * ## HIERARCHY + TRANSFORM TRUTH (MOST COMMON BUG SOURCE)
 * **Transform of Truth:** The Station is the anchor.
 * **Tilt Bug Fix:** Ensure `angleTiltController.planeTransform` targets the **Pivot**, and that Pivot is the parent of `planeCutter`.
 *
 * ## EVENT / HOOK POINTS (INTENTIONAL EXTENSION)
 * - `OnEquipScissors`: **HOOK ANCHOR SYSTEM HERE** (Freeze the stem's Rigidbody).
 * - `OnUnequipScissors`: Release the stem or reset state.
 *
 * @ingroup tools
 *
 * @section viz_scissorstation Visual Relationships
 * @dot
 * digraph ScissorStation {
 * rankdir=LR;
 * node [shape=box];
 * "Player Click" -> "ScissorStation" [label="Raycast"];
 * "ScissorStation" -> "activeScissorsRoot" [label="Applies Offset"];
 * "ScissorStation" -> "CuttingPlaneController" [label="Enable Input"];
 * "ScissorStation" -> "PlaneAngleTiltController" [label="Enable Tilt"];
 * }
 * @enddot
 */
[DisallowMultipleComponent]
public class ScissorStation : MonoBehaviour
{
    // ─────────────────────────────────────────
    // STATION VISUALS
    // ─────────────────────────────────────────

    [Header("Station Visuals (World Objects)")]

    [Tooltip("Scissors visible on the table when NOT equipped.")]
    public GameObject inactiveScissorsRoot;

    [Tooltip("Scissors visible at the cutting rig when equipped.")]
    public GameObject activeScissorsRoot;

    [Tooltip("Transform used to detect scissors clicks (hit collider may be on a child).")]
    public Transform scissorsClickableRoot;

    [Tooltip("Optional explicit collider for the scissors click target (recommended).")]
    public Collider scissorsClickableCollider;

    [Tooltip("Collider representing where scissors are returned (MUST be different from the scissors collider).")]
    public Collider returnMatCollider;

    // ─────────────────────────────────────────
    // VISUAL OFFSETS & SPLINES (NEW)
    // ─────────────────────────────────────────
    [Header("Visual Offsets")]
    [Tooltip("Position offset applied to the active scissors when equipped.")]
    public Vector3 toolPositionOffset = Vector3.zero;

    [Tooltip("Rotation offset applied to the active scissors when equipped.")]
    public Vector3 toolRotationOffset = Vector3.zero;

    [Header("Spline Pathing")]
    [Tooltip("If true, scissors will constrain to the referenced spline instead of free mouse movement.")]
    public bool useSplinePath = false;

    [Tooltip("The stem spline generator the scissors should follow.")]
    public StemSplineGenerator targetStemSpline;

    // ─────────────────────────────────────────
    // TOOL REFERENCES (WHAT GETS ENABLED)
    // ─────────────────────────────────────────

    [Header("Tool References (Enabled When Equipped)")]

    [Tooltip("Mesh cutter. Enabled while equipped. (Optional: CuttingPlaneController can gate too.)")]
    public PlaneBehaviour planeCutter;

    [Tooltip("Moves the plane + triggers cuts. Enabled via SetToolEnabled.")]
    public CuttingPlaneController planeController;

    [Tooltip("Tilts the plane (mouse X + scroll). Enable this if you want angle control.")]
    public PlaneAngleTiltController angleTiltController;

    // ─────────────────────────────────────────
    // INPUT / RAYCAST
    // ─────────────────────────────────────────

    [Header("Click Detection")]

    [Tooltip("Camera used for raycasting clicks. If null, uses Camera.main.")]
    public Camera raycastCamera;

    [Tooltip("Maximum click distance (increase if your table is far from the camera).")]
    public float maxClickDistance = 100f;

    [Tooltip("Which layers are clickable.")]
    public LayerMask clickMask = ~0;

    [Tooltip("Mouse button index (0 = LMB).")]
    public int mouseButton = 0;

    [Tooltip("Whether raycast considers triggers. Usually Ignore.")]
    public QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    // ─────────────────────────────────────────
    // SAFETY
    // ─────────────────────────────────────────

    [Header("Safety")]
    [Tooltip("Delay to prevent pickup click from instantly cutting AND from instantly tilting.")]
    [Min(0f)]
    public float equipDelay = 0.15f;

    [Tooltip("Require cut input to be released once after pickup.")]
    public bool suppressCutUntilReleased = true;

    // ─────────────────────────────────────────
    // EVENTS
    // ─────────────────────────────────────────

    [Header("Events")]
    [Tooltip("Trigger your 'Hold Stem' or 'Freeze Rigidbody' logic here.")]
    public UnityEvent OnEquipScissors;
    public UnityEvent OnUnequipScissors;

    // ─────────────────────────────────────────
    // DEBUG
    // ─────────────────────────────────────────

    [Header("Debug")]
    public bool debugLogs = true;
    public bool debugDrawRay = true;
    [Min(0f)] public float debugRayDuration = 1f;
    public bool debugLogUIBlockedClicks = true;

    // ─────────────────────────────────────────
    // STATE
    // ─────────────────────────────────────────

    [Header("Runtime State (Read Only)")]
    [SerializeField] private bool isEquipped = false;
    [SerializeField] private bool isBusy = false;

    private Coroutine _equipRoutine;

    // Cached base transform so offset application is idempotent (no compounding on re-equip)
    private Vector3 _baseLocalPosition;
    private Vector3 _baseLocalEulerAngles;
    private bool _baseCached;

    public bool IsEquipped => isEquipped;
    public bool IsBusy => isBusy;

    private void Awake()
    {
        if (raycastCamera == null) raycastCamera = Camera.main;

        // Convenience default: if root not set, try to use the inactive scissors transform.
        if (scissorsClickableRoot == null && inactiveScissorsRoot != null)
            scissorsClickableRoot = inactiveScissorsRoot.transform;

        // Cache base local transform before any offsets are applied
        if (activeScissorsRoot != null && !_baseCached)
        {
            _baseLocalPosition = activeScissorsRoot.transform.localPosition;
            _baseLocalEulerAngles = activeScissorsRoot.transform.localEulerAngles;
            _baseCached = true;
        }

        // HARD deterministic start (prevents "scissors already out" + prevents tilt changing rotation on frame 0)
        ForceUnequip(silentEvents: true);

        ValidateSetup();
    }

    private void ValidateSetup()
    {
        if (!debugLogs) return;

        if (raycastCamera == null)
            Debug.LogWarning("[ScissorStation] RaycastCamera is null and Camera.main was not found.", this);

        if (scissorsClickableCollider == null && scissorsClickableRoot == null)
            Debug.LogWarning("[ScissorStation] No scissors click target set (Collider or Root). Equip will never trigger.", this);

        if (returnMatCollider == null)
            Debug.LogWarning("[ScissorStation] ReturnMatCollider is null. Unequip will never trigger.", this);

        if (returnMatCollider != null && scissorsClickableCollider != null && returnMatCollider == scissorsClickableCollider)
            Debug.LogWarning("[ScissorStation] ReturnMatCollider == ScissorsClickableCollider (same collider). Create a separate return mat collider.", this);

        if (planeController == null)
            Debug.LogWarning("[ScissorStation] planeController is null. Equipping won’t enable tool input.", this);

        if (planeCutter == null)
            Debug.LogWarning("[ScissorStation] planeCutter is null. Equipping won’t enable cutting (if you rely on toggling it).", this);

        if (angleTiltController == null)
            Debug.LogWarning("[ScissorStation] angleTiltController is null. Angle cutting will not be possible (unless you tilt elsewhere).", this);
    }

    private void Update()
    {
        if (isBusy) return;

        // RMB while equipped → put scissors back
        if (isEquipped && Input.GetMouseButtonDown(1))
        {
            if (debugLogs) Debug.Log("[ScissorStation] -> Unequip via RMB", this);
            Unequip();
            return;
        }

        if (!Input.GetMouseButtonDown(mouseButton)) return;

        // Block through UI (but log so you know WHY)
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            if (debugLogs && debugLogUIBlockedClicks)
                Debug.Log("[ScissorStation] Click ignored (pointer over UI).", this);
            return;
        }

        TryHandleClick();
    }

    private void TryHandleClick()
    {
        if (raycastCamera == null) raycastCamera = Camera.main;
        if (raycastCamera == null)
        {
            if (debugLogs) Debug.LogWarning("[ScissorStation] No camera available for raycast.", this);
            return;
        }

        Ray ray = ApartmentManager.Instance != null ? ApartmentManager.Instance.ScreenPointToRay(Input.mousePosition) : raycastCamera.ScreenPointToRay(Input.mousePosition);

        if (debugDrawRay)
            Debug.DrawRay(ray.origin, ray.direction * maxClickDistance, Color.yellow, debugRayDuration);

        if (!Physics.Raycast(ray, out RaycastHit hit, maxClickDistance, clickMask, triggerInteraction))
        {
            if (debugLogs)
                Debug.Log($"[ScissorStation] Raycast MISS. cam='{raycastCamera.name}', mask={clickMask.value}, maxDist={maxClickDistance}", this);
            return;
        }

        if (debugLogs)
            Debug.Log($"[ScissorStation] Raycast HIT '{hit.collider.name}' (layer='{LayerMask.LayerToName(hit.collider.gameObject.layer)}')", hit.collider);

        // Equip
        if (!isEquipped && HitIsScissors(hit))
        {
            if (debugLogs) Debug.Log("[ScissorStation] -> Equip requested", this);
            BeginEquip();
            return;
        }

        // Unequip
        if (isEquipped && returnMatCollider != null && hit.collider == returnMatCollider)
        {
            if (debugLogs) Debug.Log("[ScissorStation] -> Unequip requested", this);
            Unequip();
            return;
        }

        if (debugLogs)
            Debug.Log("[ScissorStation] Hit something, but it was not a valid equip/return target.", hit.collider);
    }

    private bool HitIsScissors(RaycastHit hit)
    {
        if (hit.collider == null) return false;

        // Best: explicit collider
        if (scissorsClickableCollider != null)
            return hit.collider == scissorsClickableCollider;

        // Fallback: root + children
        if (scissorsClickableRoot == null) return false;

        return hit.collider.transform == scissorsClickableRoot
                || hit.collider.transform.IsChildOf(scissorsClickableRoot);
    }

    private void BeginEquip()
    {
        if (isBusy) return;

        if (_equipRoutine != null)
            StopCoroutine(_equipRoutine);

        _equipRoutine = StartCoroutine(EquipRoutine());
    }

    private IEnumerator EquipRoutine()
    {
        isBusy = true;

        // Visual swap first (instant feedback)
        if (inactiveScissorsRoot != null) inactiveScissorsRoot.SetActive(false);
        if (activeScissorsRoot != null)
        {
            activeScissorsRoot.SetActive(true);

            // APPLY OFFSET: Uses cached base + offset so re-equipping never compounds
            activeScissorsRoot.transform.localPosition = _baseLocalPosition + toolPositionOffset;
            activeScissorsRoot.transform.localEulerAngles = _baseLocalEulerAngles + toolRotationOffset;
        }

        // Safety delay prevents:
        // - pickup click from cutting
        // - pickup click / MouseX jitter from instantly changing angle on enable
        if (equipDelay > 0f)
            yield return new WaitForSeconds(equipDelay);

        isEquipped = true;

        // Enable tool components (null-safe)
        if (planeCutter != null) planeCutter.enabled = true;

        // IMPORTANT: tilt must be enabled ONLY while equipped; otherwise rotation can change on scene start.
        if (angleTiltController != null)
            angleTiltController.SetEnabled(true);

        if (planeController != null)
        {
            // Wire spline reference so scissors follow the stem curve
            if (useSplinePath && targetStemSpline != null)
                planeController.stemSpline = targetStemSpline;
            else
                planeController.stemSpline = null;

            planeController.SetToolEnabled(true);

            if (suppressCutUntilReleased)
                planeController.SuppressCutUntilReleased();
        }

        // ANCHOR HOOK: Connect your "Hold Stem" logic here so it doesn't fall.
        OnEquipScissors?.Invoke();

        isBusy = false;
        _equipRoutine = null;
    }

    public void Unequip()
    {
        if (isBusy) return;

        if (_equipRoutine != null)
        {
            StopCoroutine(_equipRoutine);
            _equipRoutine = null;
        }

        isEquipped = false;

        if (planeController != null) planeController.SetToolEnabled(false);
        if (planeCutter != null) planeCutter.enabled = false;

        // Disable tilt
        if (angleTiltController != null) angleTiltController.SetEnabled(false);

        if (activeScissorsRoot != null) activeScissorsRoot.SetActive(false);
        if (inactiveScissorsRoot != null) inactiveScissorsRoot.SetActive(true);

        OnUnequipScissors?.Invoke();
    }

    /// <summary>
    /// Hard reset to the unequipped state. Used on Awake to enforce deterministic starts.
    /// </summary>
    public void ForceUnequip(bool silentEvents)
    {
        isBusy = false;
        isEquipped = false;

        if (_equipRoutine != null)
        {
            StopCoroutine(_equipRoutine);
            _equipRoutine = null;
        }

        if (planeController != null) planeController.SetToolEnabled(false);
        if (planeCutter != null) planeCutter.enabled = false;

        // THIS is the missing line that prevents “rotation changes when it starts”.
        if (angleTiltController != null) angleTiltController.SetEnabled(false);

        if (activeScissorsRoot != null) activeScissorsRoot.SetActive(false);
        if (inactiveScissorsRoot != null) inactiveScissorsRoot.SetActive(true);

        if (!silentEvents)
            OnUnequipScissors?.Invoke();
    }
}