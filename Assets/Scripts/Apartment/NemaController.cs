using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Scene-scoped singleton that positions Nema's visible model in the apartment.
/// Teleports between predefined spots based on current area and date phase.
/// Nema idles in cool poses, looks at what the player interacts with,
/// and glances at random things in the room when bored.
///
/// Wire the Transform fields in the Inspector to empty GameObjects marking
/// each position.
///
/// Animation setup:
///   - Animator with "IdleIndex" (int) parameter for per-area pose sets
///   - "LookWeight" (float 0-1) for head IK blend
///   - "Bored" (trigger) for bored glance animations
///   - OnAnimatorIK callback drives head look-at toward _lookTarget
/// </summary>
public class NemaController : MonoBehaviour
{
    public static NemaController Instance { get; private set; }

    [Header("Model")]
    [Tooltip("Root transform of Nema's visual model (moved by this controller). Used as the fallback model when no per-location model is configured below.")]
    [SerializeField] private Transform _model;

    [Tooltip("Animator on Nema's fallback model. Optional — pose/look-at features require it. Per-location models carry their own Animators, which are picked up automatically when the model is shown.")]
    [SerializeField] private Animator _animator;

    [Header("Browsing Positions (per area index)")]
    [Tooltip("Where Nema stands in each apartment area. Index-matched to ApartmentManager.areas[]. Ignored when a matching per-area model is set below.")]
    [SerializeField] private Transform[] _areaPositions;

    [Tooltip("Per-browsing-area Nema models with their own idle animators. Index-matched to _areaPositions. Leave empty to use the fallback _model + WarpTo behavior.")]
    [SerializeField] private GameObject[] _areaModels;

    [Header("Date Positions")]
    [Tooltip("Where Nema stands during entrance judgments (Phase 1). Ignored when _arrivalModel is set.")]
    [SerializeField] private Transform _entrancePosition;

    [Tooltip("Where Nema stands during kitchen/drink phase (Phase 2). Ignored when _kitchenModel is set.")]
    [SerializeField] private Transform _kitchenPosition;

    [Tooltip("Where Nema sits during couch/reveal phase (Phase 3). Ignored when _couchModel is set.")]
    [SerializeField] private Transform _couchPosition;

    [Header("Date Phase Models")]
    [Tooltip("Full Nema model (mesh + Animator + looping idle) for Phase 1 Arrival. Pre-positioned at the entrance.")]
    [SerializeField] private GameObject _arrivalModel;

    [Tooltip("Full Nema model for Phase 2 Kitchen. Pre-positioned at the kitchen.")]
    [SerializeField] private GameObject _kitchenModel;

    [Tooltip("Full Nema model for Phase 3 Couch/Reveal. Pre-positioned on the couch.")]
    [SerializeField] private GameObject _couchModel;

    [Header("Newspaper Position")]
    [Tooltip("Where Nema stands while reading the newspaper (morning phase). Ignored when _newspaperModel is set.")]
    [SerializeField] private Transform _newspaperPosition;

    [Tooltip("Full Nema model for the morning newspaper pose. Pre-positioned at the newspaper spot.")]
    [SerializeField] private GameObject _newspaperModel;

    [Header("Exploration (pre-date clean-up)")]
    [Tooltip("Nema model shown during the pre-date Exploration phase — she's leaning against the wall while the player tidies up. Uses the leaning animation / lean location.")]
    [SerializeField] private GameObject _explorationLeanModel;

    [Header("Cleaning Phase (post-date Evening)")]
    [Tooltip("Full Nema model for the post-date Evening phase. Pre-positioned watching the player clean.")]
    [SerializeField] private GameObject _cleaningModel;

    [Header("Secret — Dancing")]
    [Tooltip("Secret dancing Nema model (e.g. Northern Soul Spin). Shown via ShowDancingSecret() — not tied to any phase, toggled externally by whatever trigger unlocks the dance (record player, secret click, etc.).")]
    [SerializeField] private GameObject _dancingModel;

    // Runtime flag so phase changes respect an active secret dance.
    private bool _dancingSecretActive;
    private GameObject _activeModelGO;

    /// <summary>The currently active Nema model's transform (for dim exclusion, etc).</summary>
    public Transform ActiveModel => _activeModelGO != null ? _activeModelGO.transform : (_model != null ? _model : null);

    [Header("Look-At")]
    [Tooltip("How fast the look-at weight blends in/out (per second).")]
    [SerializeField] private float _lookBlendSpeed = 3f;

    [Tooltip("Maximum head turn angle (degrees) before Nema gives up looking.")]
    [SerializeField] private float _maxLookAngle = 90f;

    [Tooltip("Head bone for manual look-at rotation (used when Animator IK is not available).")]
    [SerializeField] private Transform _headBone;

    [Tooltip("Layer mask for cursor-to-world raycast (look-at target). Exclude UI and IgnoreRaycast.")]
    [SerializeField] private LayerMask _lookAtRaycastMask = ~((1 << 2) | (1 << 5)); // exclude IgnoreRaycast + UI

    [Header("Look-At — Toggles")]
    [Tooltip("Look at whatever the player is carrying.")]
    [SerializeField] private bool _lookAtHeldItem = true;

    [Tooltip("Look at whatever the player is hovering.")]
    [SerializeField] private bool _lookAtHoveredItem = true;

    [Tooltip("Track the cursor's world position on surfaces.")]
    [SerializeField] private bool _lookAtCursor = true;

    [Tooltip("Only track cursor while the player is holding something.")]
    [SerializeField] private bool _cursorOnlyWhileHolding = false;

    [Tooltip("Look directly at the camera (the player).")]
    [SerializeField] private bool _lookAtCamera = true;

    [Tooltip("Glance at random objects in the room when idle.")]
    [SerializeField] private bool _lookAtRandomObjects = true;

    [Header("Look-At — Weights")]
    [Tooltip("IK body weight (how much the torso turns). 0 = head only, 1 = full body turn.")]
    [SerializeField, Range(0f, 1f)] private float _ikBodyWeight = 0.3f;

    [Tooltip("IK head weight.")]
    [SerializeField, Range(0f, 1f)] private float _ikHeadWeight = 0.6f;

    [Tooltip("IK eyes weight.")]
    [SerializeField, Range(0f, 1f)] private float _ikEyesWeight = 0.8f;

    [Tooltip("IK clamp weight (limits extreme head angles).")]
    [SerializeField, Range(0f, 1f)] private float _ikClampWeight = 0.5f;

    [Header("Idle Behavior")]
    [Tooltip("Seconds of no player interaction before Nema gets bored and glances around.")]
    [SerializeField] private float _boredDelay = 6f;

    [Tooltip("How long Nema looks at a random object before picking another or returning to idle.")]
    [SerializeField] private float _boredGlanceDuration = 2.5f;

    [Tooltip("When idle, how often (seconds) Nema picks a new idle target (camera, cursor, random object).")]
    [SerializeField] private float _idleCycleInterval = 4f;

    [Tooltip("Random ± variance on idle cycle interval.")]
    [SerializeField] private float _idleCycleVariance = 1.5f;

    [Tooltip("Chance of looking at the camera during idle cycle (vs cursor or random object).")]
    [SerializeField, Range(0f, 1f)] private float _idleCameraChance = 0.3f;

    [Tooltip("Chance of tracking cursor during idle cycle.")]
    [SerializeField, Range(0f, 1f)] private float _idleCursorChance = 0.3f;

    [Tooltip("Chance of looking at a random object during idle cycle (remainder = stare forward).")]
    [SerializeField, Range(0f, 1f)] private float _idleObjectChance = 0.3f;

    // Body-turn system removed: per-location models have their own idle
    // animations driving full-body orientation; turning the body here would
    // fight the Animator. Head-only look-at is still active via the IK /
    // manual head bone path below.

    // ── Runtime state ──────────────────────────────────────────
    private Camera _cachedCamera;
    private Transform _currentTarget;

    // Look-at
    private Vector3 _lookTarget;
    private float _lookWeight;
    private float _targetLookWeight;
    private bool _hasLookTarget;

    // Bored / idle cycle
    private float _interactionTimer; // time since last player interaction
    private float _boredGlanceTimer;
    private bool _isBored;
    private Transform _boredTarget;

    private enum IdleMode { None, Camera, Cursor, RandomObject }
    private IdleMode _currentIdleMode = IdleMode.None;
    private float _idleCycleTimer;


    // Animator hashes
    private static readonly int H_IdleIndex = Animator.StringToHash("IdleIndex");
    private static readonly int H_LookWeight = Animator.StringToHash("LookWeight");
    private static readonly int H_Bored = Animator.StringToHash("Bored");

    // ── Null-safe animator parameter helpers ──────────────────────
    // Per-location Nema models may each have a bespoke Animator Controller
    // that doesn't include every parameter (IdleIndex, LookWeight, Bored).
    // These helpers check the parameter exists with the expected type before
    // calling the underlying Animator API, so missing parameters become a
    // silent no-op instead of a Unity console warning on every frame.

    private static bool AnimatorHasParameter(Animator a, int nameHash, AnimatorControllerParameterType type)
    {
        if (a == null || a.runtimeAnimatorController == null) return false;
        var ps = a.parameters;
        for (int i = 0; i < ps.Length; i++)
        {
            var p = ps[i];
            if (p.nameHash == nameHash && p.type == type) return true;
        }
        return false;
    }

    private void SafeSetFloat(int nameHash, float value)
    {
        if (_animator == null) return;
        if (AnimatorHasParameter(_animator, nameHash, AnimatorControllerParameterType.Float))
            _animator.SetFloat(nameHash, value);
    }

    private void SafeSetInteger(int nameHash, int value)
    {
        if (_animator == null) return;
        if (AnimatorHasParameter(_animator, nameHash, AnimatorControllerParameterType.Int))
            _animator.SetInteger(nameHash, value);
    }

    private void SafeSetTrigger(int nameHash)
    {
        if (_animator == null) return;
        if (AnimatorHasParameter(_animator, nameHash, AnimatorControllerParameterType.Trigger))
            _animator.SetTrigger(nameHash);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (_animator == null && _model != null)
            _animator = _model.GetComponentInChildren<Animator>();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (ApartmentManager.Instance != null)
            ApartmentManager.Instance.OnAreaChanged -= OnAreaChanged;
        if (DayPhaseManager.Instance != null)
            DayPhaseManager.Instance.OnPhaseChanged.RemoveListener(OnPhaseChanged);
    }

    private void Start()
    {
        if (ApartmentManager.Instance != null)
            ApartmentManager.Instance.OnAreaChanged += OnAreaChanged;

        if (DayPhaseManager.Instance != null)
            DayPhaseManager.Instance.OnPhaseChanged.AddListener(OnPhaseChanged);

        // Start at current area position
        if (ApartmentManager.Instance != null)
            OnAreaChanged(ApartmentManager.Instance.CurrentAreaIndex);
    }

    private void Update()
    {
        if (_model == null) return;

        UpdateLookTarget();
        UpdateBoredTimer();

        // Sync animator parameters (null-safe — skipped if the active model's
        // Animator Controller doesn't define LookWeight).
        SafeSetFloat(H_LookWeight, _lookWeight);
    }

    private bool _debugLookLogged;

    private void LateUpdate()
    {
        if (_model == null) return;

        // Manual head look-at (when no Animator IK)
        if (_headBone != null && _lookWeight > 0.01f && _hasLookTarget)
        {
            if (!_debugLookLogged) { Debug.Log($"[Nema] Using MANUAL head bone look-at (bone='{_headBone.name}')"); _debugLookLogged = true; }
            Vector3 toTarget = _lookTarget - _headBone.position;
            if (toTarget.sqrMagnitude > 0.01f)
            {
                Quaternion lookRot = Quaternion.LookRotation(toTarget);
                _headBone.rotation = Quaternion.Slerp(_headBone.rotation, lookRot, _lookWeight * 0.7f);
            }
        }
    }

    // ── Animator IK callback (if Animator has IK pass enabled) ──

    private bool _debugIKLogged;

    private void OnAnimatorIK(int layerIndex)
    {
        if (_animator == null) return;

        if (!_debugIKLogged) { Debug.Log($"[Nema] Using ANIMATOR IK look-at (animator='{_animator.name}')"); _debugIKLogged = true; }

        if (_hasLookTarget && _lookWeight > 0.01f)
        {
            _animator.SetLookAtPosition(_lookTarget);
            _animator.SetLookAtWeight(_lookWeight, _ikBodyWeight, _ikHeadWeight, _ikEyesWeight, _ikClampWeight);
        }
        else
        {
            _animator.SetLookAtWeight(0f);
        }
    }

    // ── Look-at system ─────────────────────────────────────────

    private void UpdateLookTarget()
    {
        // Priority 0: Hold spacebar → force look at camera
        if (Keyboard.current != null && Keyboard.current.spaceKey.isPressed)
        {
            if (_cachedCamera == null) _cachedCamera = Camera.main;
            if (_cachedCamera != null)
            {
                SetLookTarget(_cachedCamera.transform.position);
                _interactionTimer = 0f;
                _isBored = false;
                _currentIdleMode = IdleMode.None;
                return;
            }
        }

        bool isHolding = ObjectGrabber.IsHoldingObject && ObjectGrabber.HeldObject != null;

        // Priority 1: Look at held item
        if (_lookAtHeldItem && isHolding)
        {
            SetLookTarget(ObjectGrabber.HeldObject.transform.position);
            _interactionTimer = 0f;
            _isBored = false;
            _currentIdleMode = IdleMode.None;
            return;
        }

        // Priority 2: Look at hovered item
        if (_lookAtHoveredItem && ApartmentManager.Instance != null)
        {
            var hovered = ApartmentManager.Instance.HoveredHighlight;
            if (hovered != null)
            {
                SetLookTarget(hovered.transform.position);
                _interactionTimer = 0f;
                _isBored = false;
                _currentIdleMode = IdleMode.None;
                return;
            }
        }

        // Revalidate: if current idle mode's toggle was disabled, repick
        if ((_currentIdleMode == IdleMode.Cursor && (!_lookAtCursor || (_cursorOnlyWhileHolding && !isHolding)))
            || (_currentIdleMode == IdleMode.Camera && !_lookAtCamera)
            || (_currentIdleMode == IdleMode.RandomObject && !_lookAtRandomObjects))
        {
            PickIdleMode();
        }

        // Priority 3: Cursor tracking (always, or only while holding)
        if (_lookAtCursor && (!_cursorOnlyWhileHolding || isHolding)
            && _currentIdleMode == IdleMode.Cursor && TryCursorWorldPosition(out Vector3 cursorPos))
        {
            SetLookTarget(cursorPos);
            return;
        }

        // Priority 4: Camera
        if (_lookAtCamera && _currentIdleMode == IdleMode.Camera)
        {
            if (_cachedCamera == null) _cachedCamera = Camera.main;
            if (_cachedCamera != null)
            {
                SetLookTarget(_cachedCamera.transform.position);
                return;
            }
        }

        // Priority 5: Random object (bored glance)
        if (_lookAtRandomObjects && _currentIdleMode == IdleMode.RandomObject
            && _isBored && _boredTarget != null)
        {
            SetLookTarget(_boredTarget.position);
            return;
        }

        // No active idle mode picked yet or current mode can't resolve — clear
        ClearLookTarget();
    }

    /// <summary>Raycast the cursor into the scene to get a world-space look point.</summary>
    private bool TryCursorWorldPosition(out Vector3 worldPos)
    {
        worldPos = Vector3.zero;
        if (_cachedCamera == null) _cachedCamera = Camera.main;
        if (_cachedCamera == null) return false;

        Ray ray = ApartmentManager.Instance != null ? ApartmentManager.Instance.ScreenPointToRay(IrisInput.CursorPosition) : _cachedCamera.ScreenPointToRay(IrisInput.CursorPosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, _lookAtRaycastMask))
        {
            worldPos = hit.point;
            return true;
        }
        return false;
    }

    private void SetLookTarget(Vector3 worldPos)
    {
        if (_model == null) return;

        // Check angle — don't look behind Nema
        Vector3 toTarget = worldPos - _model.position;
        toTarget.y = 0f;
        float angle = Vector3.Angle(_model.forward, toTarget);
        if (angle > _maxLookAngle)
        {
            ClearLookTarget();
            return;
        }

        _lookTarget = worldPos;
        _hasLookTarget = true;
        _targetLookWeight = 1f;
        _lookWeight = Mathf.MoveTowards(_lookWeight, _targetLookWeight, _lookBlendSpeed * Time.deltaTime);
    }

    private void ClearLookTarget()
    {
        _targetLookWeight = 0f;
        _lookWeight = Mathf.MoveTowards(_lookWeight, 0f, _lookBlendSpeed * Time.deltaTime);
        if (_lookWeight < 0.01f)
            _hasLookTarget = false;
    }

    // ── Bored behavior ─────────────────────────────────────────

    private void UpdateBoredTimer()
    {
        // While the player is actively interacting, reset timers
        bool interacting = (ObjectGrabber.IsHoldingObject && ObjectGrabber.HeldObject != null)
            || (ApartmentManager.Instance != null && ApartmentManager.Instance.HoveredHighlight != null);

        if (interacting)
        {
            _interactionTimer = 0f;
            _isBored = false;
            _currentIdleMode = IdleMode.None;
            _idleCycleTimer = 0f;
            return;
        }

        // Count up to bored threshold
        _interactionTimer += Time.deltaTime;
        if (_interactionTimer < _boredDelay)
        {
            // Not bored yet — default idle mode
            if (_currentIdleMode == IdleMode.None)
                PickIdleMode();
            return;
        }

        // Bored — cycle through idle targets on a timer
        _idleCycleTimer -= Time.deltaTime;
        if (_idleCycleTimer <= 0f)
        {
            PickIdleMode();
            _idleCycleTimer = _idleCycleInterval + Random.Range(-_idleCycleVariance, _idleCycleVariance);
        }

        // If current mode is random object, tick the glance timer
        if (_currentIdleMode == IdleMode.RandomObject && _isBored)
        {
            _boredGlanceTimer -= Time.deltaTime;
            if (_boredGlanceTimer <= 0f)
                PickIdleMode(); // cycle to next
        }
    }

    /// <summary>Roll dice to pick what Nema looks at during idle.</summary>
    private void PickIdleMode()
    {
        // Build a weighted roll from enabled options
        float roll = Random.value;
        float camWeight = _lookAtCamera ? _idleCameraChance : 0f;
        float curWeight = _lookAtCursor && (!_cursorOnlyWhileHolding || ObjectGrabber.IsHoldingObject) ? _idleCursorChance : 0f;
        float objWeight = _lookAtRandomObjects ? _idleObjectChance : 0f;
        float total = camWeight + curWeight + objWeight;

        if (total <= 0f)
        {
            _currentIdleMode = IdleMode.None;
            _isBored = false;
            return;
        }

        // Normalize and pick
        roll *= total;
        if (roll < camWeight)
        {
            _currentIdleMode = IdleMode.Camera;
            _isBored = false;
        }
        else if (roll < camWeight + curWeight)
        {
            _currentIdleMode = IdleMode.Cursor;
            _isBored = false;
        }
        else
        {
            _currentIdleMode = IdleMode.RandomObject;
            StartBoredGlance();
        }

        _idleCycleTimer = _idleCycleInterval + Random.Range(-_idleCycleVariance, _idleCycleVariance);
    }

    private void StartBoredGlance()
    {
        // Pick a random active ReactableTag in the scene, weighted by the
        // surface effect multiplier of the item's PlaceableObject. An item on
        // a 3× centerpiece surface is 3× as likely to be picked as one on a
        // normal shelf, so Nema looks at prominent things more often.
        var candidates = ReactableTag.All;
        if (candidates.Count == 0)
        {
            _isBored = false;
            return;
        }

        // Try a few times to find one that's in front of Nema
        for (int attempt = 0; attempt < 5; attempt++)
        {
            var pick = PickWeightedRandomTag(candidates);
            if (pick == null || !pick.IsActive) continue;

            Vector3 toItem = pick.transform.position - _model.position;
            toItem.y = 0f;
            if (Vector3.Angle(_model.forward, toItem) <= _maxLookAngle)
            {
                _boredTarget = pick.transform;
                _isBored = true;
                _boredGlanceTimer = _boredGlanceDuration + Random.Range(-0.5f, 0.5f);

                // Fire animator trigger for a pose shift (null-safe).
                SafeSetTrigger(H_Bored);

                return;
            }
        }

        // Couldn't find a valid target — just shift pose without looking
        _isBored = false;
        SafeSetTrigger(H_Bored);
    }

    /// <summary>
    /// Pick a random ReactableTag weighted by its PlaceableObject's current
    /// effect multiplier. Items on prominent surfaces (3×) get picked 3× as
    /// often as items on normal surfaces (1×). Falls back to uniform random
    /// if no candidates have a PlaceableObject.
    /// </summary>
    private static ReactableTag PickWeightedRandomTag(IReadOnlyList<ReactableTag> candidates)
    {
        if (candidates == null || candidates.Count == 0) return null;

        // Compute total weight
        int totalWeight = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            if (c == null || !c.IsActive) continue;
            totalWeight += GetTagWeight(c);
        }

        if (totalWeight <= 0) return candidates[Random.Range(0, candidates.Count)];

        // Pick a random point in the weight range and walk until we land in a bucket
        int roll = Random.Range(0, totalWeight);
        int cursor = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            if (c == null || !c.IsActive) continue;
            cursor += GetTagWeight(c);
            if (roll < cursor) return c;
        }
        return candidates[candidates.Count - 1];
    }

    private static int GetTagWeight(ReactableTag tag)
    {
        var po = tag.GetComponent<PlaceableObject>();
        if (po == null) po = tag.GetComponentInParent<PlaceableObject>();
        return po != null ? po.CurrentEffectMultiplier : 1;
    }

    // ── Public API ──────────────────────────────────────────────

    /// <summary>Teleport Nema to a specific Transform position.</summary>
    public void WarpTo(Transform target)
    {
        if (_model == null || target == null) return;
        _currentTarget = target;
        _model.position = target.position;
        _model.rotation = target.rotation;

        // Reset look/bored state on teleport
        ClearLookTarget();
        _isBored = false;
        _interactionTimer = 0f;
        _boredTarget = null;
    }

    /// <summary>Teleport Nema to a world position.</summary>
    public void WarpTo(Vector3 position)
    {
        if (_model == null) return;
        _model.position = position;
    }

    /// <summary>Show or hide Nema's model.</summary>
    public void SetVisible(bool visible)
    {
        if (_model != null)
            _model.gameObject.SetActive(visible);
    }

    /// <summary>
    /// Deactivate every known per-location Nema model and activate
    /// <paramref name="target"/>. Pass null to hide every model.
    /// Each model has its own looping idle Animator which plays on enable,
    /// so phase transitions become "toggle the right GameObject". The
    /// active model's Animator becomes the look-at / bored target so head
    /// tracking keeps working without per-model wiring.
    /// </summary>
    private void ShowOnlyModel(GameObject target)
    {
        // Deactivate every known model, including the fallback single-model root.
        if (_model != null && (target == null || _model.gameObject != target))
            _model.gameObject.SetActive(false);
        if (_arrivalModel   != null && _arrivalModel   != target) _arrivalModel.SetActive(false);
        if (_kitchenModel   != null && _kitchenModel   != target) _kitchenModel.SetActive(false);
        if (_couchModel     != null && _couchModel     != target) _couchModel.SetActive(false);
        if (_newspaperModel != null && _newspaperModel != target) _newspaperModel.SetActive(false);
        if (_explorationLeanModel != null && _explorationLeanModel != target) _explorationLeanModel.SetActive(false);
        if (_cleaningModel  != null && _cleaningModel  != target) _cleaningModel.SetActive(false);
        if (_dancingModel   != null && _dancingModel   != target) _dancingModel.SetActive(false);
        if (_areaModels != null)
        {
            for (int i = 0; i < _areaModels.Length; i++)
                if (_areaModels[i] != null && _areaModels[i] != target)
                    _areaModels[i].SetActive(false);
        }

        if (target == null) { _activeModelGO = null; return; }

        target.SetActive(true);
        _activeModelGO = target;

        // Models that were inactive during PSXRenderController's startup scan
        // still have their original URP/Standard shaders, which may be stripped
        // in builds. Swap them to PSXLit now that they're active.
        if (PSXRenderController.Instance != null)
            PSXRenderController.Instance.EnsureSwapped(target);

        // Re-bind look-at/bored to the active model's animator so the head
        // tracking keeps working after a phase switch. If the target doesn't
        // have its own animator we fall back to the serialized one.
        var animOnTarget = target.GetComponentInChildren<Animator>();
        if (animOnTarget != null)
        {
            _animator = animOnTarget;
            // Snap to default pose immediately so there's no T-pose flash
            _animator.Update(0f);
            _animator.Update(0f);
        }

        // Re-find head bone on the new model for manual look-at fallback
        if (_animator != null)
        {
            var headT = _animator.GetBoneTransform(HumanBodyBones.Head);
            if (headT != null) _headBone = headT;
        }

        // Reset debug flags so we log which system the new model uses
        _debugLookLogged = false;
        _debugIKLogged = false;
    }

    // ── Public API for the secret dancing overlay ───────────────────

    /// <summary>
    /// Activate the secret dancing Nema model (Northern Soul Spin etc.).
    /// Overrides whatever the current phase would show until HideDancingSecret
    /// is called. Trigger from wherever makes sense (record player, secret
    /// click, debug hotkey).
    /// </summary>
    public void ShowDancingSecret()
    {
        if (_dancingModel == null)
        {
            Debug.LogWarning("[NemaController] ShowDancingSecret called but _dancingModel is not assigned.");
            return;
        }
        _dancingSecretActive = true;
        ShowOnlyModel(_dancingModel);
    }

    /// <summary>
    /// Deactivate the secret dance and restore Nema to whatever the current
    /// phase / area would normally show. Re-triggers the phase handler.
    /// </summary>
    public void HideDancingSecret()
    {
        if (!_dancingSecretActive) return;
        _dancingSecretActive = false;

        // Re-evaluate the current phase so she snaps back to the right model.
        if (DayPhaseManager.Instance != null)
            OnPhaseChanged((int)DayPhaseManager.Instance.CurrentPhase);
    }

    /// <summary>Notify Nema that the player just did something interesting (resets bored timer).</summary>
    public void NotifyInteraction()
    {
        _interactionTimer = 0f;
        _isBored = false;
    }

    // ── Event handlers ──────────────────────────────────────────

    private void OnAreaChanged(int areaIndex)
    {
        // Only move for area changes during browsing phases (not during date)
        if (DayPhaseManager.Instance != null)
        {
            var phase = DayPhaseManager.Instance.CurrentPhase;
            if (phase == DayPhaseManager.DayPhase.DateInProgress
                || phase == DayPhaseManager.DayPhase.FlowerTrimming)
                return;
        }

        // Prefer per-area model if wired, otherwise fall back to WarpTo.
        if (_areaModels != null && areaIndex >= 0 && areaIndex < _areaModels.Length
            && _areaModels[areaIndex] != null)
        {
            ShowOnlyModel(_areaModels[areaIndex]);
            return;
        }

        if (_areaPositions != null && areaIndex >= 0 && areaIndex < _areaPositions.Length
            && _areaPositions[areaIndex] != null)
        {
            WarpTo(_areaPositions[areaIndex]);

            // Set idle pose for this area (null-safe).
            SafeSetInteger(H_IdleIndex, areaIndex);
        }
    }

    private void OnPhaseChanged(int phaseInt)
    {
        // If the dancing secret is currently active, phase changes leave her
        // dancing — the secret state wins until explicitly cleared.
        if (_dancingSecretActive) return;

        var phase = (DayPhaseManager.DayPhase)phaseInt;
        switch (phase)
        {
            case DayPhaseManager.DayPhase.Morning:
                SetVisible(true);
                if (_newspaperModel != null)
                    ShowOnlyModel(_newspaperModel);
                else if (_newspaperPosition != null)
                    WarpTo(_newspaperPosition);
                break;

            case DayPhaseManager.DayPhase.Exploration:
                // Pre-date clean-up phase — prefer the lean model if wired,
                // else fall back to per-area browsing models.
                SetVisible(true);
                if (_explorationLeanModel != null)
                    ShowOnlyModel(_explorationLeanModel);
                else if (ApartmentManager.Instance != null)
                    OnAreaChanged(ApartmentManager.Instance.CurrentAreaIndex);
                break;

            case DayPhaseManager.DayPhase.Evening:
                // Post-date cleanup phase — prefer the dedicated cleaning model
                // if wired, else fall back to the normal area-driven behavior.
                SetVisible(true);
                if (_cleaningModel != null)
                    ShowOnlyModel(_cleaningModel);
                else if (ApartmentManager.Instance != null)
                    OnAreaChanged(ApartmentManager.Instance.CurrentAreaIndex);
                break;

            case DayPhaseManager.DayPhase.FlowerTrimming:
                // Hide all models — Nema is off-screen during flower trimming
                ShowOnlyModel(null);
                break;

            case DayPhaseManager.DayPhase.DateInProgress:
                // MoveToDatePhase() has already set up the correct model.
                // Only deactivate phase-specific models that aren't used
                // during dates. Skip any model that's also a date phase model
                // so we don't immediately undo MoveToDatePhase.
                if (_explorationLeanModel != null) _explorationLeanModel.SetActive(false);
                if (_cleaningModel != null) _cleaningModel.SetActive(false);
                if (_newspaperModel != null) _newspaperModel.SetActive(false);
                if (_areaModels != null)
                {
                    for (int i = 0; i < _areaModels.Length; i++)
                    {
                        if (_areaModels[i] == null) continue;
                        // Don't disable area models that double as date phase models
                        if (_areaModels[i] == _arrivalModel) continue;
                        if (_areaModels[i] == _kitchenModel) continue;
                        if (_areaModels[i] == _couchModel) continue;
                        _areaModels[i].SetActive(false);
                    }
                }
                break;
        }
    }

    /// <summary>Called by DateSessionManager during date phase transitions.</summary>
    public void MoveToDatePhase(DateSessionManager.DatePhase datePhase)
    {
        SetVisible(true);
        switch (datePhase)
        {
            case DateSessionManager.DatePhase.Arrival:
                if (_arrivalModel != null)
                    ShowOnlyModel(_arrivalModel);
                else if (_entrancePosition != null)
                    WarpTo(_entrancePosition);
                break;
            case DateSessionManager.DatePhase.BackgroundJudging:
                if (_kitchenModel != null)
                    ShowOnlyModel(_kitchenModel);
                else if (_kitchenPosition != null)
                    WarpTo(_kitchenPosition);
                break;
            case DateSessionManager.DatePhase.Reveal:
                if (_couchModel != null)
                    ShowOnlyModel(_couchModel);
                else if (_couchPosition != null)
                    WarpTo(_couchPosition);
                break;
        }
    }
}
