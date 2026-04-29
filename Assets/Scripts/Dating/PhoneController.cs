using System.Collections;
using UnityEngine;

/// <summary>
/// Phone that can be clicked from any camera (ambient) or used as a station.
/// When a date has been called, the phone rings. Clicking or entering the station
/// answers it, triggering the date's arrival.
/// </summary>
public class PhoneController : MonoBehaviour, IStationManager
{
    public static PhoneController Instance { get; private set; }

    public enum PhoneState { Idle, Ringing, InCall }

    // ──────────────────────────────────────────────────────────────
    // Configuration
    // ──────────────────────────────────────────────────────────────
    [Header("Audio")]
    [SerializeField] private AudioClip ringingSFX;
    [SerializeField] private AudioClip pickupSFX;
    [SerializeField] private AudioClip doorbellSFX;

    [Tooltip("SFX played when the player clicks the phone to call the date early.")]
    [SerializeField] private AudioClip callOutgoingSFX;

    [Header("Visuals")]
    [Tooltip("Pulsing indicator that shows the phone is ringing.")]
    [SerializeField] private GameObject ringVisual;

    [Header("Timing")]
    [Tooltip("Seconds between ring SFX repeats.")]
    [SerializeField] private float ringInterval = 3f;

    [Header("Interaction")]
    [Tooltip("Layer for ambient click detection on the phone collider.")]
    [SerializeField] private LayerMask phoneLayer;

    // ──────────────────────────────────────────────────────────────
    // Runtime state
    // ──────────────────────────────────────────────────────────────
    private PhoneState _state = PhoneState.Idle;
    private DatePersonalDefinition _pendingDate;
    private float _ringTimer;
    private Camera _mainCamera;
    private static readonly WaitForSeconds s_waitCallSequence = new WaitForSeconds(0.5f);

    // ──────────────────────────────────────────────────────────────
    // IStationManager
    // ──────────────────────────────────────────────────────────────
    public bool IsAtIdleState => _state == PhoneState.Idle;

    public PhoneState CurrentPhoneState => _state;

    // ──────────────────────────────────────────────────────────────
    // Lifecycle
    // ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[PhoneController] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (ringVisual != null)
            ringVisual.SetActive(false);

        _mainCamera = Camera.main;
    }

    // Input managed by IrisInput singleton — no local enable/disable needed.

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (ObjectGrabber.IsHoldingObject) return;
        if (ObjectGrabber.ClickConsumedThisFrame) return;

        // During Exploration (Idle state), clicking phone ends prep early and triggers arrival
        if (_state == PhoneState.Idle && _pendingDate != null)
        {
            if (DayPhaseManager.Instance != null
                && DayPhaseManager.Instance.CurrentPhase == DayPhaseManager.DayPhase.Exploration
                && IrisInput.Instance != null && IrisInput.Instance.Click.WasPressedThisFrame())
            {
                if (CheckPhoneRaycast())
                {
                    if (callOutgoingSFX != null && AudioManager.Instance != null)
                        AudioManager.Instance.PlaySFX(callOutgoingSFX);
                    Debug.Log("[PhoneController] Phone clicked during prep — ending prep early.");
                    DayPhaseManager.Instance.EndPrepEarly();
                    if (DoorGreetingController.Instance != null)
                    {
                        DoorGreetingController.Instance.TriggerKnock();
                    }
                    else
                    {
                        Debug.LogWarning("[PhoneController] DoorGreetingController missing — arriving directly.");
                        DateSessionManager.Instance?.OnDateCharacterArrived();
                    }
                    _pendingDate = null;
                    return;
                }
            }
        }

        if (_state == PhoneState.Ringing)
        {
            // Pulse visual
            if (ringVisual != null)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 8f);
                ringVisual.transform.localScale = Vector3.one * (0.8f + pulse * 0.4f);
            }

            // Periodic ring SFX
            _ringTimer += Time.deltaTime;
            if (_ringTimer >= ringInterval)
            {
                _ringTimer = 0f;
                if (ringingSFX != null && AudioManager.Instance != null)
                    AudioManager.Instance.PlaySFX(ringingSFX);
            }

            // Ambient click detection
            if (IrisInput.Instance != null && IrisInput.Instance.Click.WasPressedThisFrame() && CheckPhoneRaycast())
            {
                AnswerPhone();
            }
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────────

    /// <summary>Set which date is pending (called after newspaper ad is cut).</summary>
    public void SetPendingDate(DatePersonalDefinition date)
    {
        _pendingDate = date;
    }

    /// <summary>Start the phone ringing (called when the waiting timer triggers).</summary>
    public void StartRinging()
    {
        if (_state != PhoneState.Idle) return;

        _state = PhoneState.Ringing;
        _ringTimer = ringInterval; // ring immediately on first frame

        if (ringVisual != null)
            ringVisual.SetActive(true);

        Debug.Log("[PhoneController] Phone is ringing!");
    }

    /// <summary>Answer the phone — triggers date arrival.</summary>
    public void AnswerPhone()
    {
        if (_state != PhoneState.Ringing) return;

        _state = PhoneState.InCall;

        if (ringVisual != null)
            ringVisual.SetActive(false);

        if (pickupSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(pickupSFX);

        Debug.Log("[PhoneController] Phone answered!");
        StartCoroutine(CallSequence());
    }

    /// <summary>Play doorbell SFX (date arrives without phone answer).</summary>
    public void PlayDoorbell()
    {
        if (doorbellSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(doorbellSFX);

        Debug.Log("[PhoneController] Doorbell rang!");

        // Stop ringing if still ringing
        _state = PhoneState.Idle;
        if (ringVisual != null)
            ringVisual.SetActive(false);

        if (DoorGreetingController.Instance != null)
        {
            DoorGreetingController.Instance.TriggerKnock();
        }
        else
        {
            Debug.LogWarning("[PhoneController] DoorGreetingController missing — skipping door knock, arriving directly.");
            DateSessionManager.Instance?.OnDateCharacterArrived();
        }
    }

    private bool CheckPhoneRaycast()
    {
        if (_mainCamera == null) _mainCamera = Camera.main;
        if (_mainCamera == null) return false;

        Vector2 screenPos = IrisInput.CursorPosition;
        Ray ray = ApartmentManager.Instance != null ? ApartmentManager.Instance.ScreenPointToRay(screenPos) : _mainCamera.ScreenPointToRay(screenPos);

        if (Physics.Raycast(ray, out RaycastHit hit, 100f, phoneLayer))
        {
            return hit.collider.gameObject == gameObject ||
                   hit.collider.transform.IsChildOf(transform);
        }
        return false;
    }

    private IEnumerator CallSequence()
    {
        yield return s_waitCallSequence;

        if (DoorGreetingController.Instance != null)
        {
            DoorGreetingController.Instance.TriggerKnock();
        }
        else
        {
            Debug.LogWarning("[PhoneController] DoorGreetingController missing — arriving directly.");
            DateSessionManager.Instance?.OnDateCharacterArrived();
        }

        _state = PhoneState.Idle;
    }
}
