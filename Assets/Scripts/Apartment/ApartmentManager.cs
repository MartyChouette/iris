using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using Unity.Cinemachine;
using TMPro;
using Iris.Apartment;

public class ApartmentManager : MonoBehaviour
{
    public enum State { Browsing }

    public static ApartmentManager Instance { get; private set; }

    // ──────────────────────────────────────────────────────────────
    // Configuration
    // ──────────────────────────────────────────────────────────────
    [Header("Areas")]
    [Tooltip("Ordered list of apartment areas. A/D cycles through these.")]
    [SerializeField] private ApartmentAreaDefinition[] areas;

    [Tooltip("Which area index to start on (0=Kitchen, 1=Living Room, etc.).")]
    [SerializeField] private int _startAreaIndex = 1;

    [Header("Cameras")]
    [Tooltip("Cinemachine camera used for browsing. Controlled directly by ApartmentManager.")]
    [SerializeField] private CinemachineCamera browseCamera;

    [Tooltip("CinemachineBrain on the main camera. Auto-found if null.")]
    [SerializeField] private CinemachineBrain brain;

    [Header("Transition")]
    [Tooltip("How fast the camera lerps between area positions (higher = faster).")]
    [SerializeField, Range(1f, 15f)] private float transitionSpeed = 5f;

    [Header("Item Discovery")]
    [Tooltip("When true, item discoveries (blanket/book reveals) trigger the moment camera zoom + pause. When false, items just spawn with no camera or pause.")]
    [SerializeField] private bool _itemDiscoveryMoments = true;

    /// <summary>Whether item discovery moments (camera zoom + pause) are enabled.</summary>
    public bool ItemDiscoveryMoments
    {
        get => _itemDiscoveryMoments;
        set => _itemDiscoveryMoments = value;
    }

    [Header("Cursor Parallax")]
    [Tooltip("Maximum camera shift distance toward cursor.")]
    [SerializeField, Range(0f, 0.5f)] private float parallaxMaxOffset = 0.05f;

    [Tooltip("Maximum rotation (degrees) applied instead of translation when in orthographic mode. Rotation avoids geometry clipping that translation causes in ortho.")]
    [SerializeField, Range(0f, 5f)] private float parallaxMaxRotation = 1.5f;

    [Tooltip("Smoothing speed for parallax follow.")]
    [SerializeField, Range(1f, 20f)] private float parallaxSmoothing = 8f;

    [Header("Pan (MMB Drag)")]
    [Tooltip("Pan speed multiplier for middle-mouse drag.")]
    [SerializeField, Range(0.001f, 0.1f)] private float panSpeed = 0.01f;

    [Tooltip("Maximum pan distance from area center.")]
    [SerializeField] private float panMaxDistance = 3f;

    [Tooltip("Custom pan center in world space. Pan clamp is relative to this point instead of the area camera position. Leave at zero to use area camera position.")]
    [SerializeField] private Vector3 _panCenter;

    [Tooltip("Rectangular pan bounds (half-extents in camera-local X and Y). Zero = use circular panMaxDistance instead.")]
    [SerializeField] private Vector2 _panBoundsHalf;

    [Header("Zoom (Stepped)")]
    [Tooltip("Discrete zoom levels (FOV or ortho size). Index 0 = most zoomed out, last = most zoomed in.")]
    [SerializeField] private float[] _zoomSteps = { 100f, 80f, 60f, 45f, 30f };

    [Tooltip("Starting zoom step index (0-based).")]
    [SerializeField] private int _defaultZoomStep = 0;

    [Tooltip("Lerp speed for smooth zoom transitions.")]
    [SerializeField] private float _zoomLerpSpeed = 8f;

    [Header("World Bounds")]
    [Tooltip("Objects outside this box are recovered to the nearest surface.")]
    [SerializeField] private Bounds worldBounds = new Bounds(new Vector3(-3f, 1.5f, 0f), new Vector3(16f, 8f, 20f));

    [Header("Interaction")]
    [Tooltip("ObjectGrabber to enable/disable based on state.")]
    [SerializeField] private ObjectGrabber objectGrabber;

    [Header("Camera Test")]
    [Tooltip("Optional camera preset test controller for A/B/C comparison.")]
    [SerializeField] private CameraTestController cameraTestController;

    [Header("Default Preset")]
    [Tooltip("Camera preset used as the default browse angles (e.g. V1). If set, overrides ApartmentAreaDefinition camera values.")]
    [SerializeField] private CameraPresetDefinition defaultPreset;

    [Header("Audio")]
    [Tooltip("SFX played when navigating between areas (A/D or click-arrows).")]
    [SerializeField] private AudioClip _areaTransitionSFX;

    [Header("UI")]
    [Tooltip("Panel showing the current area name during browsing.")]
    [SerializeField] private GameObject areaNamePanel;

    [Tooltip("TMP_Text for the area name.")]
    [SerializeField] private TMP_Text areaNameText;

    [Tooltip("Panel showing browse-mode control hints.")]
    [SerializeField] private GameObject browseHintsPanel;

    [Tooltip("Label on the left nav button showing the previous area name.")]
    [SerializeField] private TMP_Text _navLeftLabel;

    [Tooltip("Label on the right nav button showing the next area name.")]
    [SerializeField] private TMP_Text _navRightLabel;

    // ──────────────────────────────────────────────────────────────
    // Constants
    // ──────────────────────────────────────────────────────────────
    private const int PriorityActive = 20;
    private const int PriorityInactive = 0;

    // ──────────────────────────────────────────────────────────────
    // Runtime state
    // ──────────────────────────────────────────────────────────────
    public State CurrentState { get; private set; } = State.Browsing;
    public bool IsTransitioning => _isTransitioning;
    public int CurrentAreaIndex => _currentAreaIndex;

    /// <summary>Current ortho size of the browse camera (for cinematic zoom calculations).</summary>
    public float CurrentOrthoSize => browseCamera != null ? browseCamera.Lens.OrthographicSize : 5f;

    /// <summary>Fired when the player switches apartment areas. Arg = new area index.</summary>
    public event System.Action<int> OnAreaChanged;

    private int _currentAreaIndex;

    // Base camera state (parallax-free, lerped between areas)
    private Vector3 _basePosition;
    private Quaternion _baseRotation;
    private float _baseFOV;

    // Transition targets
    private Vector3 _targetPosition;
    private Quaternion _targetRotation;
    private float _targetFOV;
    private bool _isTransitioning;

    // Parallax offset (applied on top of base, never fed back)
    private Vector3 _currentParallaxOffset;
    private Quaternion _currentParallaxRotation = Quaternion.identity;

    // Browse camera suppression (DayPhaseManager lowers during Morning)
    private bool _browseSuppressed;

    // External camera lock (watering zoom etc.) — skips camera writes but keeps everything else running
    private bool _cameraLocked;
    public void LockCamera() => _cameraLocked = true;
    public void UnlockCamera() => _cameraLocked = false;

    // Preset override (CameraTestController feeds its target here for parallax)
    private bool _presetOverrideActive;

    // Persistent zoom level (written to lens each frame)
    private int _currentZoomStep = -1; // -1 = uninitialized
    private float _currentZoom = -1f; // smoothed zoom value for lerping
    private float _targetZoom = -1f;  // target zoom from step

    // Pan offset (MMB drag, reset on area change)
    private Vector3 _panOffset;

    // Hover highlight tracking
    private InteractableHighlight _hoveredHighlight;
    private readonly HashSet<GameObject> _plantHighlightSet = new();

    /// <summary>The InteractableHighlight currently under the cursor, or null.</summary>
    public InteractableHighlight HoveredHighlight => _hoveredHighlight;
    // _proximityHighlights removed — proximity highlight stage dropped
    private Camera _cachedMainCamera;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[ApartmentManager] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (brain == null)
            brain = FindAnyObjectByType<CinemachineBrain>();

        if (brain == null)
            Debug.LogError("[ApartmentManager] No CinemachineBrain found in scene.");

    }

    // Input managed by IrisInput singleton — no local enable/disable needed.

    private void Start()
    {
        GlobalCursorManager.EnsureExists();

        if (areas == null || areas.Length == 0)
        {
            Debug.LogError("[ApartmentManager] No areas assigned.");
            return;
        }

        PlaceableObject.SetWorldBounds(worldBounds);

        if (objectGrabber != null)
            objectGrabber.SetEnabled(true);

        _currentAreaIndex = Mathf.Clamp(_startAreaIndex, 0, areas.Length - 1);
        ApplyCameraImmediate(areas[_currentAreaIndex]);

        // Restore smooth blend after the initial hard cut
        if (brain != null)
        {
            brain.DefaultBlend = new CinemachineBlendDefinition(
                CinemachineBlendDefinition.Styles.EaseInOut, 0.8f);
        }

        ResetZoom();
        UpdateUI();
        BuildZoomIndicator();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Called by DayPhaseManager to raise or lower the browse camera.
    /// During Morning phase the browse camera should be suppressed so
    /// the read camera wins without a competing priority-20 camera.
    /// </summary>
    public void SetBrowseCameraActive(bool active)
    {
        _browseSuppressed = !active;
        if (browseCamera != null)
            browseCamera.Priority = active ? PriorityActive : PriorityInactive;

        if (active)
        {
            // Re-apply default preset lens (including ortho mode) when browse camera is raised
            ReapplyDefaultLens();
        }
        else
        {
            // Clear brain ortho override so the read camera can render in perspective
            ApplyBrainOrthoMode(false);
        }
    }

    /// <summary>
    /// Re-applies the default preset lens settings for the current area.
    /// Restores orthographic mode after the read camera (perspective) phase.
    /// </summary>
    public void ReapplyDefaultLens()
    {
        if (browseCamera == null || defaultPreset == null) return;
        if (areas == null || areas.Length == 0) return;

        GetCameraValues(_currentAreaIndex, out _, out _, out _, out LensSettings? presetLens);
        if (presetLens.HasValue)
        {
            var pl = presetLens.Value;
            pl.NearClipPlane = -9f;
            browseCamera.Lens = pl;
            ApplyBrainOrthoMode(pl.ModeOverride == LensSettings.OverrideModes.Orthographic);

            // Reset zoom to default step
            ResetZoom();
        }
    }

    private void Update()
    {
        if (DayPhaseManager.Instance != null && !DayPhaseManager.Instance.IsInteractionPhase)
            return;

        UpdateTransition();
        HandleBrowsingInput();
        HandleZoomInput();
        HandlePanInput();
        ApplyParallax();
        UpdateHoverHighlight();
    }

    // ──────────────────────────────────────────────────────────────
    // Camera Transition (pos/rot/FOV lerp)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads camera position/rotation/lens for an area index.
    /// Prefers defaultPreset SO; falls back to ApartmentAreaDefinition.
    /// </summary>
    private void GetCameraValues(int areaIndex, out Vector3 pos, out Quaternion rot, out float fov)
    {
        GetCameraValues(areaIndex, out pos, out rot, out fov, out _);
    }

    private void GetCameraValues(int areaIndex, out Vector3 pos, out Quaternion rot, out float fov, out LensSettings? lens)
    {
        lens = null;

        if (defaultPreset != null &&
            defaultPreset.areaConfigs != null &&
            areaIndex < defaultPreset.areaConfigs.Length)
        {
            var cfg = defaultPreset.areaConfigs[areaIndex];
            pos = cfg.position;
            rot = Quaternion.Euler(cfg.rotation);
            lens = cfg.lens;
            bool isOrtho = cfg.lens.ModeOverride == LensSettings.OverrideModes.Orthographic;
            fov = isOrtho ? cfg.lens.OrthographicSize : cfg.lens.FieldOfView;
            return;
        }

        // Fallback to area definition
        var area = areas[areaIndex];
        pos = area.cameraPosition;
        rot = Quaternion.Euler(area.cameraRotation);
        fov = area.cameraFOV;
    }

    private void ApplyCameraImmediate(ApartmentAreaDefinition area)
    {
        if (browseCamera == null) return;

        GetCameraValues(_currentAreaIndex, out _basePosition, out _baseRotation, out _baseFOV, out LensSettings? presetLens);

        _targetPosition = _basePosition;
        _targetRotation = _baseRotation;
        _targetFOV = _baseFOV;
        _isTransitioning = false;
        _currentParallaxOffset = Vector3.zero;
        _currentParallaxRotation = Quaternion.identity;

        // Write to transform
        var t = browseCamera.transform;
        t.position = _basePosition;
        t.rotation = _baseRotation;

        // Apply full lens from preset (includes ortho mode, near/far, etc.)
        if (presetLens.HasValue)
        {
            var pl = presetLens.Value;
            pl.NearClipPlane = -9f;
            browseCamera.Lens = pl;
            ApplyBrainOrthoMode(pl.ModeOverride == LensSettings.OverrideModes.Orthographic);
        }
        else
        {
            var lens = browseCamera.Lens;
            lens.FieldOfView = _baseFOV;
            lens.NearClipPlane = -9f;
            browseCamera.Lens = lens;
        }

        if (!_browseSuppressed)
            browseCamera.Priority = PriorityActive;

        // Hard cut for initial position
        if (brain != null)
        {
            brain.DefaultBlend = new CinemachineBlendDefinition(
                CinemachineBlendDefinition.Styles.Cut, 0f);
        }
    }

    private void UpdateTransition()
    {
        if (cameraTestController != null && cameraTestController.ActivePresetIndex >= 0) return;
        if (!_isTransitioning) return;
        if (browseCamera == null) return;

        float step = transitionSpeed * Time.deltaTime;

        _basePosition = Vector3.Lerp(_basePosition, _targetPosition, step);
        _baseRotation = Quaternion.Slerp(_baseRotation, _targetRotation, step);
        _baseFOV = Mathf.Lerp(_baseFOV, _targetFOV, step);

        // Check if close enough to stop
        bool posClose = (_basePosition - _targetPosition).sqrMagnitude < 0.0001f;
        bool rotClose = Quaternion.Angle(_baseRotation, _targetRotation) < 0.05f;
        bool fovClose = Mathf.Abs(_baseFOV - _targetFOV) < 0.05f;

        if (posClose && rotClose && fovClose)
        {
            _basePosition = _targetPosition;
            _baseRotation = _targetRotation;
            _baseFOV = _targetFOV;
            _isTransitioning = false;
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Browsing
    // ──────────────────────────────────────────────────────────────

    [Header("Keyboard Pan")]
    [Tooltip("Pan speed for WASD/arrow keys (world units per second).")]
    [SerializeField] private float _keyPanSpeed = 3f;

    [Header("Edge-Scroll Pan")]
    [Tooltip("Enable RTS-style edge scrolling — pushing the cursor against a screen edge pans the camera in that direction.")]
    [SerializeField] private bool _edgePanEnabled = true;

    [Tooltip("Width of the active edge zone in pixels. Cursor must be within this many pixels of the screen edge for edge-pan to engage.")]
    [SerializeField] private float _edgePanZoneWidth = 60f;

    [Tooltip("Max pan speed when the cursor is all the way at the screen edge (world units per second). Scales linearly from 0 at the inner zone edge to this value at the screen edge.")]
    [SerializeField] private float _edgePanMaxSpeed = 4f;

    [Tooltip("Seconds the cursor must dwell against an edge before edge-pan kicks in. 0 = instant.")]
    [SerializeField] private float _edgePanActivationDelay = 0.1f;

    private float _edgePanHoldTimer;

    /// <summary>When true, all camera panning (WASD, edge scroll, MMB drag) is suppressed.</summary>
    public bool SuppressPan { get; set; }

    private void HandleBrowsingInput()
    {
        if (SuppressPan) return;

        // WASD / arrow keys pan the camera (replaces area cycling)
        float h = 0f, v = 0f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))  h -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) h += 1f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))    v += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))  v -= 1f;

        if (Mathf.Abs(h) > 0.01f || Mathf.Abs(v) > 0.01f)
        {
            Vector3 right = _baseRotation * Vector3.right;
            Vector3 up = _baseRotation * Vector3.up;
            _panOffset += (right * h + up * v) * _keyPanSpeed * Time.deltaTime;
            ClampPanOffset();
        }

        HandleEdgeScrollPan();
    }

    /// <summary>
    /// RTS-style edge scroll: pushing the cursor within <see cref="_edgePanZoneWidth"/>
    /// pixels of any screen edge pans the camera in that direction. Speed is
    /// proportional to how far INTO the edge zone the cursor has traveled (0 at
    /// the inner zone edge, max at the screen edge) — so holding it against
    /// the edge "presses harder" and scrolls faster. Respects a short dwell
    /// delay to avoid jittery accidental activations when moving the cursor
    /// casually across the screen.
    /// </summary>
    private void HandleEdgeScrollPan()
    {
        if (!_edgePanEnabled || SuppressPan) return;

        // Don't edge-pan during the pour drag (watering / drink making) —
        // the pour mechanic deliberately pulls the cursor toward the bottom
        // edge and we don't want the camera to fight that motion.
        if (PourDragHelper.IsDragging)
        {
            _edgePanHoldTimer = 0f;
            return;
        }

        // Don't edge-pan when right-click dragging (user is already panning manually).
        if (IrisInput.Instance != null && IrisInput.Instance.PanButton.IsPressed())
        {
            _edgePanHoldTimer = 0f;
            return;
        }

        // Don't edge-pan when hovering a UI element (buttons, panels, HUDs).
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            _edgePanHoldTimer = 0f;
            return;
        }

        Vector2 cursor = Input.mousePosition;
        float w = Screen.width;
        float h = Screen.height;
        float zone = Mathf.Max(1f, _edgePanZoneWidth);

        // Compute a -1..+1 push factor per axis. Each axis scales from 0 at the
        // inner zone edge to ±1 at the screen edge (or past it), so cursors
        // deep in the edge zone scroll faster than ones just nudging into it.
        float hFactor = 0f;
        float vFactor = 0f;

        if (cursor.x < zone)
            hFactor = -Mathf.Clamp01(1f - cursor.x / zone);
        else if (cursor.x > w - zone)
            hFactor = Mathf.Clamp01((cursor.x - (w - zone)) / zone);

        if (cursor.y < zone)
            vFactor = -Mathf.Clamp01(1f - cursor.y / zone);
        else if (cursor.y > h - zone)
            vFactor = Mathf.Clamp01((cursor.y - (h - zone)) / zone);

        // Ignore cursor positions outside the window bounds entirely — Unity
        // can report negative or greater-than-screen-size values in windowed
        // mode when the cursor leaves the game view, which would otherwise
        // keep edge-pan firing after you've alt-tabbed away.
        if (cursor.x < 0f || cursor.x > w || cursor.y < 0f || cursor.y > h)
            hFactor = vFactor = 0f;

        if (Mathf.Abs(hFactor) < 0.01f && Mathf.Abs(vFactor) < 0.01f)
        {
            _edgePanHoldTimer = 0f;
            return;
        }

        // Dwell-delay gate: require the cursor to be in the edge zone for a
        // short moment before engaging, so flicks across the screen don't trip.
        _edgePanHoldTimer += Time.deltaTime;
        if (_edgePanHoldTimer < _edgePanActivationDelay) return;

        Vector3 right = _baseRotation * Vector3.right;
        Vector3 up = _baseRotation * Vector3.up;
        _panOffset += (right * hFactor + up * vFactor) * _edgePanMaxSpeed * Time.deltaTime;
        ClampPanOffset();
    }

    private void ResetZoom()
    {
        if (_zoomSteps == null || _zoomSteps.Length == 0) return;
        _currentZoomStep = Mathf.Clamp(_defaultZoomStep, 0, _zoomSteps.Length - 1);
        _targetZoom = _zoomSteps[_currentZoomStep];
        _currentZoom = _targetZoom;
    }

    private void HandleZoomInput()
    {
        if (browseCamera == null) return;
        if (_zoomSteps == null || _zoomSteps.Length == 0) return;

        // Scroll always zooms — rotation moved to R key

        // Initialize zoom step on first use
        if (_currentZoomStep < 0)
        {
            _currentZoomStep = Mathf.Clamp(_defaultZoomStep, 0, _zoomSteps.Length - 1);
            _targetZoom = _zoomSteps[_currentZoomStep];
            _currentZoom = _targetZoom;
        }

        float scroll = IrisInput.Instance != null ? IrisInput.Instance.Scroll.ReadValue<float>() : 0f;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            // Scroll up = zoom in. Respect invert scroll setting.
            int raw = scroll > 0f ? -1 : 1;
            int direction = AccessibilitySettings.InvertScroll ? -raw : raw;
            int newStep = Mathf.Clamp(_currentZoomStep + direction, 0, _zoomSteps.Length - 1);
            if (newStep != _currentZoomStep)
            {
                _currentZoomStep = newStep;
                _targetZoom = _zoomSteps[_currentZoomStep];

                // Re-clamp pan so it doesn't exceed the new zoom's limit
                ClampPanOffset();
                UpdateZoomIndicator();
            }
        }

        // Smooth lerp toward target
        if (_targetZoom > 0f)
            _currentZoom = Mathf.Lerp(_currentZoom, _targetZoom, Time.unscaledDeltaTime * _zoomLerpSpeed);
    }

    private void HandlePanInput()
    {
        if (SuppressPan) return;
        if (IrisInput.Instance == null || !IrisInput.Instance.PanButton.IsPressed()) return;

        Vector2 delta = IrisInput.Instance.PanDelta.ReadValue<Vector2>();
        if (delta.sqrMagnitude < 0.01f) return;

        // Move along camera-local right/up axes
        Vector3 right = _baseRotation * Vector3.right;
        Vector3 up = _baseRotation * Vector3.up;
        _panOffset -= (right * delta.x + up * delta.y) * panSpeed;
        ClampPanOffset();
    }

    private void ClampPanOffset()
    {
        float zoomFactor = 1f;
        if (_zoomSteps != null && _zoomSteps.Length > 0 && _currentZoom > 0f)
        {
            float baseZoom = _zoomSteps[0];
            zoomFactor = baseZoom / Mathf.Max(_currentZoom, 1f);
        }
        float scale = Mathf.Max(zoomFactor, 1f);

        // If custom pan center is set, offset the pan relative to it
        Vector3 centerOffset = Vector3.zero;
        if (_panCenter != Vector3.zero)
            centerOffset = _panCenter - _basePosition;

        Vector3 adjusted = _panOffset - centerOffset;

        if (_panBoundsHalf.x > 0f && _panBoundsHalf.y > 0f)
        {
            // Rectangular clamp in camera-local space
            Vector3 right = _baseRotation * Vector3.right;
            Vector3 up = _baseRotation * Vector3.up;

            float dotR = Vector3.Dot(adjusted, right);
            float dotU = Vector3.Dot(adjusted, up);

            dotR = Mathf.Clamp(dotR, -_panBoundsHalf.x * scale, _panBoundsHalf.x * scale);
            dotU = Mathf.Clamp(dotU, -_panBoundsHalf.y * scale, _panBoundsHalf.y * scale);

            _panOffset = centerOffset + right * dotR + up * dotU;
        }
        else
        {
            // Circular clamp
            float effectiveMaxPan = panMaxDistance * scale;
            if (adjusted.magnitude > effectiveMaxPan)
                adjusted = adjusted.normalized * effectiveMaxPan;
            _panOffset = centerOffset + adjusted;
        }
    }

    private void CycleArea(int direction)
    {
        if (areas == null || areas.Length == 0) return;

        _currentAreaIndex = (_currentAreaIndex + direction + areas.Length) % areas.Length;
        var area = areas[_currentAreaIndex];
        _panOffset = Vector3.zero;

        AudioManager.Instance?.PlaySFX(_areaTransitionSFX);

        if (cameraTestController != null && cameraTestController.ActivePresetIndex >= 0)
        {
            cameraTestController.OnAreaChanged(_currentAreaIndex);
            if (!_browseSuppressed)
                browseCamera.Priority = PriorityActive;
            UpdateUI();
            Debug.Log($"[ApartmentManager] Browsing: {area.areaName}");
            return;
        }

        GetCameraValues(_currentAreaIndex, out _targetPosition, out _targetRotation, out _targetFOV);
        _isTransitioning = true;

        if (!_browseSuppressed)
            browseCamera.Priority = PriorityActive;

        UpdateUI();
        OnAreaChanged?.Invoke(_currentAreaIndex);
        Debug.Log($"[ApartmentManager] Browsing: {area.areaName}");
    }

    /// <summary>Navigate to the previous area. Called by UI button. Disabled for now.</summary>
    public void NavigateLeft() { }

    /// <summary>Navigate to the next area. Called by UI button. Disabled for now.</summary>
    public void NavigateRight() { }

    // ──────────────────────────────────────────────────────────────
    // UI
    // ──────────────────────────────────────────────────────────────

    private void UpdateUI()
    {
        // Area name and nav buttons hidden — single camera angle for now
        if (areaNamePanel != null)
            areaNamePanel.SetActive(false);

        if (browseHintsPanel != null)
            browseHintsPanel.SetActive(false);

        // Hide nav arrow buttons
        if (_navLeftLabel != null)
            _navLeftLabel.transform.parent.gameObject.SetActive(false);
        if (_navRightLabel != null)
            _navRightLabel.transform.parent.gameObject.SetActive(false);
    }

    // ── Zoom Indicator (minimal UI) ──────────────────────────────────

    private UnityEngine.UI.Image[] _zoomTicks;
    private static readonly Color TickActive = new Color(1f, 1f, 1f, 0.9f);
    private static readonly Color TickInactive = new Color(1f, 1f, 1f, 0.2f);

    private void BuildZoomIndicator()
    {
        if (_zoomSteps == null || _zoomSteps.Length == 0) return;

        var canvasGO = new GameObject("ZoomIndicator");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        int count = _zoomSteps.Length;
        _zoomTicks = new UnityEngine.UI.Image[count];
        float tickH = 60f;
        float tickW = 15f;
        float gap = 10f;
        float totalH = count * tickH + (count - 1) * gap;
        float startY = -totalH * 0.5f;

        for (int i = 0; i < count; i++)
        {
            var tickGO = new GameObject($"Tick_{i}");
            tickGO.transform.SetParent(canvasGO.transform, false);
            var rt = tickGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-30f, startY + i * (tickH + gap));
            rt.sizeDelta = new Vector2(tickW, tickH);

            var img = tickGO.AddComponent<UnityEngine.UI.Image>();
            img.color = TickInactive;
            img.raycastTarget = false;
            _zoomTicks[i] = img;
        }

        UpdateZoomIndicator();
    }

    private void UpdateZoomIndicator()
    {
        if (_zoomTicks == null) return;
        for (int i = 0; i < _zoomTicks.Length; i++)
        {
            if (_zoomTicks[i] == null) continue;
            // Invert so bottom = zoomed out, top = zoomed in
            int visualIdx = _zoomTicks.Length - 1 - i;
            _zoomTicks[i].color = (visualIdx == _currentZoomStep) ? TickActive : TickInactive;

            // Active tick is slightly wider
            var rt = _zoomTicks[i].rectTransform;
            rt.sizeDelta = (visualIdx == _currentZoomStep)
                ? new Vector2(24f, 60f)
                : new Vector2(15f, 60f);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Preset Override (CameraTestController feeds values here)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by CameraTestController to set the parallax base from a preset.
    /// ApartmentManager applies mouse parallax on top of these values.
    /// </summary>
    private float _presetNearClip = -9f;
    private float _presetFarClip = 1000f;

    public void SetPresetBase(Vector3 pos, Quaternion rot, float fov)
    {
        SetPresetBase(pos, rot, fov, -9f, 1000f);
    }

    public void SetPresetBase(Vector3 pos, Quaternion rot, float fov, float nearClip, float farClip)
    {
        _basePosition = pos;
        _baseRotation = rot;
        _baseFOV = fov;
        _presetNearClip = nearClip;
        _presetFarClip = farClip;
        _presetOverrideActive = true;
    }

    /// <summary>Called by CameraTestController when preset is cleared.</summary>
    public void ClearPresetBase()
    {
        _presetOverrideActive = false;
        // Snap back to current area (reads from defaultPreset if set)
        if (areas != null && areas.Length > 0)
        {
            GetCameraValues(_currentAreaIndex, out _basePosition, out _baseRotation, out _baseFOV);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Cursor Parallax (offset on top of base — never feeds back)
    // ──────────────────────────────────────────────────────────────

    private void ApplyParallax()
    {
        if (browseCamera == null) return;
        if (_cameraLocked) return; // external system controlling camera

        // Always write base + offset to transform, even with parallax disabled
        var t = browseCamera.transform;

        bool isOrtho = browseCamera.Lens.ModeOverride == Unity.Cinemachine.LensSettings.OverrideModes.Orthographic;

        if ((parallaxMaxOffset > 0f || parallaxMaxRotation > 0f) && !AccessibilitySettings.ReduceMotion)
        {
            Vector2 mousePos = IrisInput.CursorPosition;
            float nx = (mousePos.x / Screen.width - 0.5f) * 2f;
            float ny = (mousePos.y / Screen.height - 0.5f) * 2f;
            nx = Mathf.Clamp(nx, -1f, 1f);
            ny = Mathf.Clamp(ny, -1f, 1f);

            if (isOrtho && parallaxMaxRotation > 0f)
            {
                // Ortho: rotate instead of translate to avoid geometry clipping.
                // Small yaw/pitch pivots the view without shifting the frustum.
                float effectiveRot = parallaxMaxRotation * AccessibilitySettings.ScreenShakeScale;
                Quaternion targetRot = Quaternion.Euler(-ny * effectiveRot, nx * effectiveRot, 0f);
                _currentParallaxRotation = Quaternion.Slerp(_currentParallaxRotation, targetRot,
                    Time.deltaTime * parallaxSmoothing);
                _currentParallaxOffset = Vector3.Lerp(_currentParallaxOffset, Vector3.zero,
                    Time.deltaTime * parallaxSmoothing);
            }
            else
            {
                // Perspective: translate as before
                Vector3 right = _baseRotation * Vector3.right;
                Vector3 up = _baseRotation * Vector3.up;
                float effectiveOffset = parallaxMaxOffset * AccessibilitySettings.ScreenShakeScale;
                Vector3 targetOffset = (right * -nx + up * -ny) * effectiveOffset;

                _currentParallaxOffset = Vector3.Lerp(_currentParallaxOffset, targetOffset,
                    Time.deltaTime * parallaxSmoothing);
                _currentParallaxRotation = Quaternion.Slerp(_currentParallaxRotation, Quaternion.identity,
                    Time.deltaTime * parallaxSmoothing);
            }
        }
        else if (AccessibilitySettings.ReduceMotion)
        {
            _currentParallaxOffset = Vector3.Lerp(_currentParallaxOffset, Vector3.zero,
                Time.deltaTime * parallaxSmoothing);
            _currentParallaxRotation = Quaternion.Slerp(_currentParallaxRotation, Quaternion.identity,
                Time.deltaTime * parallaxSmoothing);
        }

        // Write final position = base + parallax + pan
        t.position = _basePosition + _currentParallaxOffset + _panOffset;
        t.rotation = _baseRotation * _currentParallaxRotation;

        // Write lens — preset or area default, then layer zoom on top
        {
            var lens = browseCamera.Lens;
            bool lensOrtho = lens.ModeOverride == LensSettings.OverrideModes.Orthographic;

            // Base lens: preset owns it when active, otherwise use area FOV
            if (!_presetOverrideActive)
                lens.FieldOfView = _baseFOV;

            // Layer zoom on top (works with both preset and default lens)
            if (_currentZoom >= 0f)
            {
                if (lensOrtho)
                    lens.OrthographicSize = _currentZoom;
                else
                    lens.FieldOfView = _currentZoom;
            }

            lens.NearClipPlane = _presetOverrideActive ? _presetNearClip : -9f;
            if (_presetOverrideActive)
                lens.FarClipPlane = _presetFarClip;
            browseCamera.Lens = lens;
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Brain Ortho Override
    // ──────────────────────────────────────────────────────────────

    private void ApplyBrainOrthoMode(bool ortho)
    {
        if (brain == null) return;
        var lmo = brain.LensModeOverride;
        lmo.Enabled = ortho;
        if (ortho)
            lmo.DefaultMode = LensSettings.OverrideModes.Orthographic;
        brain.LensModeOverride = lmo;

        var cam = brain.GetComponent<UnityEngine.Camera>();
        if (cam != null)
            cam.orthographic = ortho;
    }

    // ──────────────────────────────────────────────────────────────
    // Hover Highlight
    // ──────────────────────────────────────────────────────────────

    [Tooltip("Angular radius in degrees for hover detection. Scales naturally with distance.")]
    private const float HoverAngle = 1.5f;

    /// <summary>
    /// Angular distance (degrees) between the ray direction and the direction to the point.
    /// Scales naturally with distance — far objects are just as easy to hover as near ones.
    /// </summary>
    private static float AngleToRay(Ray ray, Vector3 point)
    {
        Vector3 toPoint = point - ray.origin;
        if (toPoint.sqrMagnitude < 0.0001f) return 0f;
        return Vector3.Angle(ray.direction, toPoint);
    }

    private void UpdateHoverHighlight()
    {
        if (_cachedMainCamera == null) _cachedMainCamera = UnityEngine.Camera.main;
        var cam = _cachedMainCamera;
        if (cam == null) return;

        Vector2 mousePos = IrisInput.CursorPosition;
        Ray ray = cam.ScreenPointToRay(mousePos);

        // ── Find nearest InteractableHighlight to the cursor ray ──
        // Scans all registered highlights (placeables, plants, etc.).
        // When holding a watering can, only plants are valid hover targets.
        bool wateringCanHeld = ObjectGrabber.HeldObject != null
            && ObjectGrabber.HeldObject.GetComponent<WateringCan>() != null;

        // Pre-build a quick set of plant GameObjects to avoid GetComponent per highlight per frame
        _plantHighlightSet.Clear();
        if (wateringCanHeld)
        {
            var plants = WaterablePlant.All;
            for (int i = 0; i < plants.Count; i++)
                if (plants[i] != null) _plantHighlightSet.Add(plants[i].gameObject);
        }

        InteractableHighlight hit = null;
        {
            float bestAngle = HoverAngle;
            var allHighlights = InteractableHighlight.All;
            for (int i = 0; i < allHighlights.Count; i++)
            {
                var hl = allHighlights[i];
                if (hl == null) continue;

                // Watering can only interacts with plants — skip everything else
                if (wateringCanHeld && !_plantHighlightSet.Contains(hl.gameObject))
                    continue;

                float angle = AngleToRay(ray, hl.transform.position);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    hit = hl;
                }
            }
        }

        // ── Direct hover (strongest highlight) ──
        if (hit != _hoveredHighlight)
        {
            // Unhighlight previous + its pair partner
            if (_hoveredHighlight != null)
            {
                _hoveredHighlight.SetHighlighted(false);
                SetPartnerHighlight(_hoveredHighlight, false);
            }

            _hoveredHighlight = hit;

            // Highlight new + its pair partner
            if (_hoveredHighlight != null)
            {
                _hoveredHighlight.SetHighlighted(true);
                SetPartnerHighlight(_hoveredHighlight, true);
            }
        }
    }

    /// <summary>If the object has a PairableItem with a specific partner, highlight/unhighlight it too.</summary>
    private static void SetPartnerHighlight(InteractableHighlight hl, bool on)
    {
        var pairable = hl.GetComponent<PairableItem>();
        if (pairable == null) return;

        // For SpecificPartner: highlight the partner shoe
        if (pairable.Mode == PairableItem.PairMode.SpecificPartner && pairable.SpecificPartner != null)
        {
            var partnerHL = pairable.SpecificPartner.GetComponent<InteractableHighlight>();
            if (partnerHL != null)
                partnerHL.SetHighlighted(on);
        }

        // For AnyOfCategory when paired: highlight the paired child
        if (pairable.PairedChild != null)
        {
            var childHL = pairable.PairedChild.GetComponent<InteractableHighlight>();
            if (childHL != null)
                childHL.SetHighlighted(on);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Scene View Gizmos
    // ──────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (areas == null || areas.Length == 0) return;

        // World bounds box
        Gizmos.color = new Color(0f, 1f, 0f, 0.15f);
        Gizmos.DrawWireCube(worldBounds.center, worldBounds.size);

        for (int i = 0; i < areas.Length; i++)
        {
            var area = areas[i];
            if (area == null) continue;

            Vector3 pos = area.cameraPosition;
            Quaternion rot = Quaternion.Euler(area.cameraRotation);

            // Sphere at camera position
            bool isCurrent = Application.isPlaying && i == _currentAreaIndex;
            Gizmos.color = isCurrent ? Color.yellow : Color.cyan;
            Gizmos.DrawSphere(pos, 0.15f);

            // Forward direction ray
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
            Gizmos.DrawRay(pos, rot * Vector3.forward * 1.5f);

            // Label
            var style = new GUIStyle(UnityEditor.EditorStyles.boldLabel)
            {
                normal = { textColor = isCurrent ? Color.yellow : Color.white }
            };
            UnityEditor.Handles.Label(pos + Vector3.up * 0.3f, area.areaName, style);
        }
    }
#endif
}
