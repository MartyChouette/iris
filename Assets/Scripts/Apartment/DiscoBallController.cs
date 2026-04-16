using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Scene-scoped singleton disco ball receiver. Accepts PlaceableObjects with DiscoBallBulb
/// (same pattern as RecordSlot accepts RecordItem). Manages a spotlight with procedural
/// cookie projection, ball rotation, on/off toggle, and MoodMachine feed.
/// Click the disco ball to toggle on/off. Drop a bulb onto it to load a new pattern.
/// </summary>
public class DiscoBallController : MonoBehaviour
{
    public static DiscoBallController Instance { get; private set; }

    [Header("Spotlight")]
    [Tooltip("Child SpotLight that projects the cookie pattern onto walls/ceiling.")]
    [SerializeField] private Light _spotlight;

    [Header("Visuals")]
    [Tooltip("Transform of the mirrored ball mesh (rotated when on).")]
    [SerializeField] private Transform _ballVisual;

    [Tooltip("Rotation speed of the ball visual in degrees/second.")]
    [SerializeField] private float _ballRotationSpeed = 30f;

    [Header("Bulb Placement")]
    [Tooltip("Where the accepted bulb snaps to (child transform).")]
    [SerializeField] private Transform _bulbSnapPoint;

    [Header("Audio")]
    [Tooltip("SFX played when toggling on/off.")]
    [SerializeField] private AudioClip _toggleSFX;

    [Tooltip("SFX played when inserting a bulb.")]
    [SerializeField] private AudioClip _insertSFX;

    // Runtime state
    private DiscoBallBulb _loadedBulb;
    private PlaceableObject _loadedPlaceable;
    private Vector3 _loadedHomePosition;
    private Quaternion _loadedHomeRotation;
    private bool _isOn;
    private Texture2D _cachedCookie;
    private float _colorTimer;
    private float _spotlightRotationAngle;

    // Click detection
    private InputAction _mouseClick;
    private InputAction _mousePosition;
    private Collider _selfCollider;

    public bool IsOn => _isOn;
    public DiscoBulbDefinition CurrentBulb => _loadedBulb != null ? _loadedBulb.Definition : null;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[DiscoBallController] Duplicate instance destroyed.");
            Destroy(this);
            return;
        }
        Instance = this;

        _selfCollider = GetComponent<Collider>();

        _mouseClick = new InputAction("DiscoBallClick", InputActionType.Button, "<Mouse>/leftButton");
        _mousePosition = new InputAction("DiscoBallMousePos", InputActionType.Value, "<Mouse>/position");

        // Start with spotlight off
        if (_spotlight != null)
            _spotlight.enabled = false;
    }

    private void OnEnable()
    {
        _mouseClick.Enable();
        _mousePosition.Enable();
    }

    private void OnDisable()
    {
        _mouseClick.Disable();
        _mousePosition.Disable();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;

        if (_cachedCookie != null)
            Object.Destroy(_cachedCookie);

        _mouseClick?.Dispose();
        _mousePosition?.Dispose();
        _mouseClick = null;
        _mousePosition = null;
    }

    private void Update()
    {
        // Click-to-toggle (only when not holding an object and click not consumed)
        if (_mouseClick.WasPressedThisFrame()
            && !ObjectGrabber.IsHoldingObject
            && !ObjectGrabber.ClickConsumedThisFrame
            && _selfCollider != null)
        {
            var cam = Camera.main;
            if (cam != null)
            {
                Vector2 screenPos = _mousePosition.ReadValue<Vector2>();
                Ray ray = cam.ScreenPointToRay(screenPos);
                if (Physics.Raycast(ray, out RaycastHit hit, 100f)
                    && (hit.collider == _selfCollider || hit.collider.transform.IsChildOf(transform)))
                {
                    Toggle();
                }
            }
        }

        if (!_isOn || _loadedBulb == null) return;

        var def = _loadedBulb.Definition;

        // Rotate ball visual
        if (_ballVisual != null)
            _ballVisual.Rotate(Vector3.up, _ballRotationSpeed * Time.deltaTime, Space.Self);

        // Rotate spotlight around a tilted axis so the projection sweeps the room
        if (_spotlight != null)
        {
            _spotlightRotationAngle += def.rotationSpeed * Time.deltaTime;
            if (_spotlightRotationAngle > 360f) _spotlightRotationAngle -= 360f;

            // Tilt 45° from vertical, then spin around local Y
            _spotlight.transform.localRotation = Quaternion.AngleAxis(_spotlightRotationAngle, Vector3.up)
                * Quaternion.AngleAxis(45f, Vector3.right);
        }

        // Color cycling
        if (def.cycleColors && def.colorGradient != null && _spotlight != null)
        {
            _colorTimer += def.colorCycleSpeed * Time.deltaTime;
            if (_colorTimer > 1f) _colorTimer -= 1f;
            _spotlight.color = def.colorGradient.Evaluate(_colorTimer);
        }
    }

    /// <summary>
    /// Attempts to accept a held PlaceableObject as a disco bulb.
    /// Returns true if the bulb was accepted (has DiscoBallBulb component).
    /// </summary>
    public bool TryAcceptBulb(PlaceableObject held)
    {
        if (held == null) return false;

        var bulbItem = held.GetComponent<DiscoBallBulb>();
        if (bulbItem == null || bulbItem.Definition == null) return false;

        // Eject current bulb if one is loaded
        if (_loadedPlaceable != null)
            EjectBulb();

        // Load the new bulb
        _loadedPlaceable = held;
        _loadedBulb = bulbItem;
        _loadedHomePosition = bulbItem.HomePosition;
        _loadedHomeRotation = bulbItem.HomeRotation;

        // Disable physics
        var rb = held.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Disable colliders
        foreach (var col in held.GetComponents<Collider>())
            col.enabled = false;

        // Snap to bulb point
        if (_bulbSnapPoint != null)
        {
            held.transform.SetParent(_bulbSnapPoint);
            held.transform.localPosition = Vector3.zero;
            held.transform.localRotation = Quaternion.identity;
        }
        else
        {
            held.transform.SetParent(transform);
            held.transform.localPosition = Vector3.up * 0.05f;
            held.transform.localRotation = Quaternion.identity;
        }

        // Generate cookie texture
        var def = bulbItem.Definition;
        if (_cachedCookie != null)
            Object.Destroy(_cachedCookie);

        _cachedCookie = DiscoBallCookieGenerator.Generate(def.pattern, def.cookieResolution);

        // Configure spotlight
        if (_spotlight != null)
        {
            _spotlight.cookie = _cachedCookie;
            _spotlight.intensity = def.lightIntensity;
            _spotlight.spotAngle = def.spotAngle;
            _spotlight.color = def.lightColor;
        }

        // Auto-turn on
        if (!_isOn)
            TurnOn();

        AudioManager.Instance?.PlaySFX(_insertSFX);
        Debug.Log($"[DiscoBallController] Loaded bulb: {def.bulbName} ({def.pattern})");
        return true;
    }

    /// <summary>
    /// Ejects the current bulb, returning it to its home position.
    /// </summary>
    public void EjectBulb()
    {
        if (_loadedPlaceable == null) return;

        TurnOff();

        _loadedPlaceable.transform.SetParent(null);

        // Return to home position
        _loadedPlaceable.transform.position = _loadedHomePosition;
        _loadedPlaceable.transform.rotation = _loadedHomeRotation;

        var rb = _loadedPlaceable.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Re-enable colliders
        foreach (var col in _loadedPlaceable.GetComponents<Collider>())
            col.enabled = true;

        _loadedPlaceable.IsAtHome = true;

        Debug.Log($"[DiscoBallController] Ejected bulb: {_loadedBulb?.Definition?.bulbName}");

        _loadedPlaceable = null;
        _loadedBulb = null;

        // Clear cookie
        if (_spotlight != null)
            _spotlight.cookie = null;
    }

    /// <summary>Toggle on/off. Called by click detection or external code.</summary>
    public void Toggle()
    {
        if (_isOn)
            TurnOff();
        else
            TurnOn();
    }

    /// <summary>
    /// Stops the disco ball without ejecting the bulb. Called by DayPhaseManager
    /// during phase transitions.
    /// </summary>
    public void Stop()
    {
        if (_isOn)
            TurnOff();
    }

    private void TurnOn()
    {
        if (_loadedBulb == null) return;

        _isOn = true;
        _colorTimer = 0f;
        _spotlightRotationAngle = 0f;

        if (_spotlight != null)
            _spotlight.enabled = true;

        // Feed mood machine
        MoodMachine.Instance?.SetSource("DiscoBall", _loadedBulb.Definition.moodValue);

        // Activate reactable tag
        var reactable = GetComponent<ReactableTag>();
        if (reactable != null) reactable.IsActive = true;

        AudioManager.Instance?.PlaySFX(_toggleSFX);
        Debug.Log("[DiscoBallController] Turned ON.");
    }

    private void TurnOff()
    {
        _isOn = false;

        if (_spotlight != null)
            _spotlight.enabled = false;

        MoodMachine.Instance?.RemoveSource("DiscoBall");

        var reactable = GetComponent<ReactableTag>();
        if (reactable != null) reactable.IsActive = false;

        AudioManager.Instance?.PlaySFX(_toggleSFX);
        Debug.Log("[DiscoBallController] Turned OFF.");
    }
}
