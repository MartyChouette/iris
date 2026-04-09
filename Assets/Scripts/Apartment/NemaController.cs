using System.Collections.Generic;
using UnityEngine;

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

    [Header("Look-At")]
    [Tooltip("How fast the look-at weight blends in/out (per second).")]
    [SerializeField] private float _lookBlendSpeed = 3f;

    [Tooltip("Maximum head turn angle (degrees) before Nema gives up looking.")]
    [SerializeField] private float _maxLookAngle = 90f;

    [Tooltip("Head bone for manual look-at rotation (used when Animator IK is not available).")]
    [SerializeField] private Transform _headBone;

    [Header("Bored Behavior")]
    [Tooltip("Seconds of no player interaction before Nema gets bored and glances around.")]
    [SerializeField] private float _boredDelay = 6f;

    [Tooltip("How long Nema looks at a random object before picking another or returning to idle.")]
    [SerializeField] private float _boredGlanceDuration = 2.5f;

    [Tooltip("Chance (0-1) of glancing at a random object vs just shifting pose.")]
    [SerializeField, Range(0f, 1f)] private float _glanceChance = 0.7f;

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

    // Bored timer
    private float _interactionTimer; // time since last player interaction
    private float _boredGlanceTimer;
    private bool _isBored;
    private Transform _boredTarget;


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

    private void LateUpdate()
    {
        if (_model == null) return;

        // Manual head look-at (when no Animator IK)
        if (_headBone != null && _lookWeight > 0.01f && _hasLookTarget)
        {
            Vector3 toTarget = _lookTarget - _headBone.position;
            if (toTarget.sqrMagnitude > 0.01f)
            {
                Quaternion lookRot = Quaternion.LookRotation(toTarget);
                _headBone.rotation = Quaternion.Slerp(_headBone.rotation, lookRot, _lookWeight * 0.7f);
            }
        }
    }

    // ── Animator IK callback (if Animator has IK pass enabled) ──

    private void OnAnimatorIK(int layerIndex)
    {
        if (_animator == null) return;

        if (_hasLookTarget && _lookWeight > 0.01f)
        {
            _animator.SetLookAtPosition(_lookTarget);
            _animator.SetLookAtWeight(_lookWeight, 0.3f, 0.6f, 0.8f, 0.5f);
            // body weight, head weight, eyes weight, clamp weight
        }
        else
        {
            _animator.SetLookAtWeight(0f);
        }
    }

    // ── Look-at system ─────────────────────────────────────────

    private void UpdateLookTarget()
    {
        // Priority 1: Player is holding something — look at it
        if (ObjectGrabber.IsHoldingObject && ObjectGrabber.HeldObject != null)
        {
            SetLookTarget(ObjectGrabber.HeldObject.transform.position);
            _interactionTimer = 0f; // reset bored timer
            _isBored = false;
            return;
        }

        // Priority 2: Player is hovering something (check ApartmentManager highlight)
        if (ApartmentManager.Instance != null)
        {
            var hovered = ApartmentManager.Instance.HoveredHighlight;
            if (hovered != null)
            {
                SetLookTarget(hovered.transform.position);
                _interactionTimer = 0f;
                _isBored = false;
                return;
            }
        }

        // Priority 3: Bored — look at random thing
        if (_isBored && _boredTarget != null)
        {
            SetLookTarget(_boredTarget.position);
            return;
        }

        // Nothing interesting — clear look target
        ClearLookTarget();
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
        if (_isBored)
        {
            _boredGlanceTimer -= Time.deltaTime;
            if (_boredGlanceTimer <= 0f)
            {
                // Done glancing — either pick a new target or stop being bored
                if (Random.value < _glanceChance)
                    StartBoredGlance();
                else
                    _isBored = false;
            }
            return;
        }

        _interactionTimer += Time.deltaTime;
        if (_interactionTimer >= _boredDelay)
        {
            StartBoredGlance();
        }
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

        if (target == null) return;

        target.SetActive(true);
        // Re-bind look-at/bored to the active model's animator so the head
        // tracking keeps working after a phase switch. If the target doesn't
        // have its own animator we fall back to the serialized one.
        var animOnTarget = target.GetComponentInChildren<Animator>();
        if (animOnTarget != null) _animator = animOnTarget;
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
                // during dates. Don't touch _model — it's the fallback
                // when per-phase date models aren't wired.
                if (_explorationLeanModel != null) _explorationLeanModel.SetActive(false);
                if (_cleaningModel != null) _cleaningModel.SetActive(false);
                if (_newspaperModel != null) _newspaperModel.SetActive(false);
                if (_areaModels != null)
                {
                    for (int i = 0; i < _areaModels.Length; i++)
                        if (_areaModels[i] != null) _areaModels[i].SetActive(false);
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
