using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Scene-scoped singleton that handles the door knock → answer → phase transition sequence.
/// When TriggerKnock() is called, shows a screen-space "knock knock" notification that
/// slides toward the door's screen position (visible from any camera angle).
/// Player clicks the door to answer, triggering the date arrival.
/// </summary>
public class DoorGreetingController : MonoBehaviour
{
    public static DoorGreetingController Instance { get; private set; }

    [Header("References")]
    [Tooltip("Layer mask for the door collider.")]
    [SerializeField] private LayerMask _doorLayer;

    [Tooltip("Main camera for raycasting.")]
    [SerializeField] private Camera _mainCamera;

    [Tooltip("World-space TMP text shown above the door (secondary visual).")]
    [SerializeField] private TMP_Text _knockText;

    [Header("Audio")]
    [Tooltip("SFX played when the knock occurs.")]
    [SerializeField] private AudioClip _knockSFX;

    [Tooltip("SFX played when the door is answered.")]
    [SerializeField] private AudioClip _doorOpenSFX;

    private bool _knockActive;

    // Screen-space overlay
    private GameObject _overlayCanvasGO;
    private RectTransform _notifRT;
    private TMP_Text _notifText;
    private CanvasGroup _notifCanvasGroup;
    private float _pulseTimer;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[DoorGreetingController] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (_mainCamera == null)
            _mainCamera = Camera.main;

        if (_knockText != null)
            _knockText.gameObject.SetActive(false);

        BuildOverlay();
    }

    // Input managed by IrisInput singleton — no local enable/disable needed.

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_overlayCanvasGO != null) Destroy(_overlayCanvasGO);
    }

    private void Update()
    {
        if (!_knockActive) return;
        if (ObjectGrabber.IsHoldingObject) return;
        if (ObjectGrabber.ClickConsumedThisFrame) return;

        if (IrisInput.Instance != null && IrisInput.Instance.Click.WasPressedThisFrame() && CheckDoorRaycast())
        {
            _knockActive = false;
            StartCoroutine(DoorAnsweredSequence());
        }
    }

    private void LateUpdate()
    {
        if (!_knockActive || _notifRT == null) return;

        PositionOverlayTowardDoor();

        // Gentle pulse
        _pulseTimer += Time.deltaTime;
        if (_notifCanvasGroup != null)
            _notifCanvasGroup.alpha = 0.8f + 0.2f * Mathf.Sin(_pulseTimer * 2.5f);
    }

    /// <summary>Show "knock knock" notification and enable door click detection.</summary>
    public void TriggerKnock()
    {
        _knockActive = true;
        _pulseTimer = 0f;

        // World-space text (secondary — visible when near the door)
        if (_knockText != null)
        {
            _knockText.gameObject.SetActive(true);
            _knockText.text = "knock knock";
        }

        // Screen-space overlay (always visible)
        if (_overlayCanvasGO != null)
            _overlayCanvasGO.SetActive(true);

        if (_knockSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(_knockSFX);

        Debug.Log("[DoorGreetingController] Knock triggered — waiting for player to click door.");
    }

    private IEnumerator DoorAnsweredSequence()
    {
        // Hide all knock visuals
        if (_knockText != null)
            _knockText.gameObject.SetActive(false);
        if (_overlayCanvasGO != null)
            _overlayCanvasGO.SetActive(false);

        // Play door open SFX
        if (_doorOpenSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(_doorOpenSFX);

        // Delegate to DateSessionManager — it owns the Phase 1 fade transition
        DateSessionManager.Instance?.OnDateCharacterArrived();
        yield break;
    }

    private bool CheckDoorRaycast()
    {
        if (_mainCamera == null) _mainCamera = Camera.main;
        if (_mainCamera == null) return false;

        Vector2 screenPos = IrisInput.CursorPosition;
        Ray ray = ApartmentManager.Instance != null ? ApartmentManager.Instance.ScreenPointToRay(screenPos) : _mainCamera.ScreenPointToRay(screenPos);

        return Physics.Raycast(ray, out _, 100f, _doorLayer);
    }

    // ── Screen-space overlay ──────────────────────────────────────────

    private void BuildOverlay()
    {
        _overlayCanvasGO = new GameObject("KnockOverlay");
        _overlayCanvasGO.transform.SetParent(transform);

        var canvas = _overlayCanvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        var scaler = _overlayCanvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        _overlayCanvasGO.AddComponent<GraphicRaycaster>();

        // Notification panel
        var panelGO = new GameObject("NotifPanel");
        panelGO.transform.SetParent(_overlayCanvasGO.transform, false);
        _notifRT = panelGO.AddComponent<RectTransform>();
        _notifRT.sizeDelta = new Vector2(400f, 100f);

        // Background image
        var bg = panelGO.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.6f);

        // Canvas group for pulse
        _notifCanvasGroup = panelGO.AddComponent<CanvasGroup>();

        // Text
        var textGO = new GameObject("NotifText");
        textGO.transform.SetParent(panelGO.transform, false);
        var textRT = textGO.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(10f, 5f);
        textRT.offsetMax = new Vector2(-10f, -5f);

        _notifText = textGO.AddComponent<TextMeshProUGUI>();
        _notifText.text = "knock knock\n<size=70%>click the door to answer</size>";
        _notifText.fontSize = 36f;
        _notifText.alignment = TextAlignmentOptions.Center;
        _notifText.color = new Color(1f, 0.9f, 0.3f);

        _overlayCanvasGO.SetActive(false);
    }

    private void PositionOverlayTowardDoor()
    {
        if (_mainCamera == null) _mainCamera = Camera.main;
        if (_mainCamera == null || _notifRT == null) return;

        // Door position is this GO's transform (DoorGreetingController lives on the door)
        Vector3 doorWorld = transform.position + Vector3.up * 1.1f;
        Vector3 vp = _mainCamera.WorldToViewportPoint(doorWorld);

        // If door is behind camera, project direction onto screen edge
        bool behind = vp.z < 0f;
        if (behind)
        {
            vp.x = 1f - vp.x;
            vp.y = 1f - vp.y;
        }

        // Clamp to screen with margin so the notif stays readable
        const float margin = 0.15f;
        float tx = Mathf.Clamp(vp.x, margin, 1f - margin);
        float ty = Mathf.Clamp(vp.y, margin + 0.1f, 1f - margin);

        // Bias: lerp between center and door direction (60% toward door)
        float cx = Mathf.Lerp(0.5f, tx, 0.6f);
        float cy = Mathf.Lerp(0.5f, ty, 0.6f);

        // Nudge up slightly so it doesn't overlap center HUD elements
        cy = Mathf.Max(cy, 0.55f);

        _notifRT.anchorMin = new Vector2(cx, cy);
        _notifRT.anchorMax = new Vector2(cx, cy);
        _notifRT.anchoredPosition = Vector2.zero;
    }
}
