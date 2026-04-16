using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Central input manager — single source of truth for all gameplay bindings.
/// Supports keyboard+mouse, gamepad (Xbox/PS5/generic), touch, and mouse-only.
/// Auto-creates before first scene load. DontDestroyOnLoad singleton.
///
/// Usage: read actions via IrisInput.Instance.Click, IrisInput.Instance.Scroll, etc.
/// For cursor position (works across all devices): IrisInput.CursorPosition.
/// </summary>
public class IrisInput : MonoBehaviour
{
    public static IrisInput Instance { get; private set; }

    // ── Apartment Actions ───────────────────────────────────────
    public InputAction NavigateLeft { get; private set; }
    public InputAction NavigateRight { get; private set; }
    public InputAction Point { get; private set; }
    public InputAction Click { get; private set; }
    public InputAction Scroll { get; private set; }
    public InputAction PanButton { get; private set; }
    public InputAction PanDelta { get; private set; }
    public InputAction GridToggle { get; private set; }

    // ── Flower Trimming Actions ─────────────────────────────────
    public InputAction FlowerMoveY { get; private set; }
    public InputAction FlowerCut { get; private set; }
    public InputAction FlowerPoint { get; private set; }
    public InputAction FlowerMove { get; private set; }

    // ── Global Actions ──────────────────────────────────────────
    public InputAction Pause { get; private set; }

    // ── Asset & Maps ────────────────────────────────────────────
    public InputActionAsset Asset { get; private set; }
    public InputActionMap ApartmentMap { get; private set; }
    public InputActionMap FlowerMap { get; private set; }
    public InputActionMap GlobalMap { get; private set; }

    // ── Device Detection ────────────────────────────────────────
    public enum ActiveControlScheme { KeyboardMouse, Gamepad, Touch }
    public static ActiveControlScheme Scheme { get; private set; } = ActiveControlScheme.KeyboardMouse;
    public static bool IsGamepad => Scheme == ActiveControlScheme.Gamepad;
    public static bool IsTouch => Scheme == ActiveControlScheme.Touch;
    public static event System.Action<ActiveControlScheme> OnSchemeChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // Clear subscribers from prior editor play sessions (Unity does not reset
        // static event delegates on scene reload or with domain-reload off)
        OnSchemeChanged = null;
    }

    // ── Gamepad Cursor ──────────────────────────────────────────
    private Vector2 _gamepadCursorPos;
    private const float GamepadCursorSpeed = 800f;

    /// <summary>
    /// Unified cursor position — works for mouse, touch, and gamepad.
    /// Use this instead of reading mouse position directly.
    /// </summary>
    public static Vector2 CursorPosition
    {
        get
        {
            if (Instance == null) return Vector2.zero;
            if (IsGamepad) return Instance._gamepadCursorPos;
            return Instance.Point.ReadValue<Vector2>();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Auto-creation (runs before any scene loads)
    // ─────────────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("[IrisInput]");
        go.AddComponent<IrisInput>();
    }

    // ─────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        BuildAsset();
        ApartmentMap.Enable();
        FlowerMap.Enable();
        GlobalMap.Enable();

        _gamepadCursorPos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        // Wire up rebind persistence
        InputRebindManager.Initialize(Asset);
        InputRebindManager.LoadOverrides();

        // Listen for device changes
        InputSystem.onActionChange += OnActionChange;

        // Create gamepad cursor visual
        var cursorGO = new GameObject("[GamepadCursor]");
        cursorGO.transform.SetParent(transform);
        cursorGO.AddComponent<GamepadCursor>();

        Debug.Log("[IrisInput] Initialized — keyboard+mouse, gamepad, touch supported.");
    }

    private void OnDestroy()
    {
        InputSystem.onActionChange -= OnActionChange;
        if (Instance == this) Instance = null;
        if (Asset != null) Destroy(Asset);
    }

    private void Update()
    {
        if (IsGamepad)
            UpdateGamepadCursor();
    }

    // ─────────────────────────────────────────────────────────────
    // Build InputActionAsset programmatically
    // ─────────────────────────────────────────────────────────────

    private void BuildAsset()
    {
        Asset = ScriptableObject.CreateInstance<InputActionAsset>();

        // ── Control Schemes ──
        Asset.AddControlScheme("KeyboardMouse")
            .WithRequiredDevice("<Keyboard>")
            .WithRequiredDevice("<Mouse>");
        Asset.AddControlScheme("Gamepad")
            .WithRequiredDevice("<Gamepad>");
        Asset.AddControlScheme("Touch")
            .WithRequiredDevice("<Touchscreen>");

        BuildApartmentMap();
        BuildFlowerMap();
        BuildGlobalMap();
    }

    private void BuildApartmentMap()
    {
        ApartmentMap = Asset.AddActionMap("Apartment");

        // ── Area navigation ──
        NavigateLeft = ApartmentMap.AddAction("NavigateLeft", InputActionType.Button);
        NavigateLeft.AddBinding("<Keyboard>/a", groups: "KeyboardMouse");
        NavigateLeft.AddBinding("<Keyboard>/leftArrow", groups: "KeyboardMouse");
        NavigateLeft.AddBinding("<Gamepad>/dpad/left", groups: "Gamepad");

        NavigateRight = ApartmentMap.AddAction("NavigateRight", InputActionType.Button);
        NavigateRight.AddBinding("<Keyboard>/d", groups: "KeyboardMouse");
        NavigateRight.AddBinding("<Keyboard>/rightArrow", groups: "KeyboardMouse");
        NavigateRight.AddBinding("<Gamepad>/dpad/right", groups: "Gamepad");

        // ── Cursor position ──
        Point = ApartmentMap.AddAction("Point", InputActionType.PassThrough);
        Point.expectedControlType = "Vector2";
        Point.AddBinding("<Mouse>/position", groups: "KeyboardMouse");
        Point.AddBinding("<Touchscreen>/primaryTouch/position", groups: "Touch");
        // Gamepad cursor is software-driven — read via IrisInput.CursorPosition

        // ── Primary interaction ──
        Click = ApartmentMap.AddAction("Click", InputActionType.Button);
        Click.AddBinding("<Mouse>/leftButton", groups: "KeyboardMouse");
        Click.AddBinding("<Touchscreen>/primaryTouch/press", groups: "Touch");
        Click.AddBinding("<Gamepad>/buttonSouth", groups: "Gamepad"); // A / Cross

        // ── Scroll / zoom ──
        Scroll = ApartmentMap.AddAction("Scroll", InputActionType.PassThrough);
        Scroll.expectedControlType = "Axis";
        Scroll.AddBinding("<Mouse>/scroll/y", groups: "KeyboardMouse");
        // Gamepad zoom: DPad up/down mapped as discrete axis
        // (bumpers reserved for pan/rotate)
        Scroll.AddCompositeBinding("1DAxis")
            .With("Negative", "<Gamepad>/rightShoulder", groups: "Gamepad")
            .With("Positive", "<Gamepad>/leftShoulder", groups: "Gamepad");

        // ── Camera pan ──
        PanButton = ApartmentMap.AddAction("PanButton", InputActionType.Button);
        PanButton.AddBinding("<Mouse>/middleButton", groups: "KeyboardMouse");
        PanButton.AddBinding("<Gamepad>/leftTrigger", groups: "Gamepad");

        PanDelta = ApartmentMap.AddAction("PanDelta", InputActionType.PassThrough);
        PanDelta.expectedControlType = "Vector2";
        PanDelta.AddBinding("<Mouse>/delta", groups: "KeyboardMouse");
        PanDelta.AddBinding("<Gamepad>/leftStick", groups: "Gamepad");

        // ── Interaction toggles ──
        GridToggle = ApartmentMap.AddAction("GridToggle", InputActionType.Button);
        GridToggle.AddBinding("<Keyboard>/g", groups: "KeyboardMouse");
        GridToggle.AddBinding("<Gamepad>/buttonNorth", groups: "Gamepad"); // Y / Triangle
    }

    private void BuildFlowerMap()
    {
        FlowerMap = Asset.AddActionMap("Flower");

        // Height movement (W/S or left stick Y)
        FlowerMoveY = FlowerMap.AddAction("MoveY", InputActionType.Value);
        FlowerMoveY.expectedControlType = "Axis";
        FlowerMoveY.AddCompositeBinding("1DAxis")
            .With("Negative", "<Keyboard>/s", groups: "KeyboardMouse")
            .With("Positive", "<Keyboard>/w", groups: "KeyboardMouse");
        FlowerMoveY.AddBinding("<Gamepad>/leftStick/y", groups: "Gamepad");

        // Cut action
        FlowerCut = FlowerMap.AddAction("Cut", InputActionType.Button);
        FlowerCut.AddBinding("<Keyboard>/space", groups: "KeyboardMouse");
        FlowerCut.AddBinding("<Mouse>/leftButton", groups: "KeyboardMouse");
        FlowerCut.AddBinding("<Touchscreen>/primaryTouch/press", groups: "Touch");
        FlowerCut.AddBinding("<Gamepad>/buttonSouth", groups: "Gamepad");

        // Pointer position
        FlowerPoint = FlowerMap.AddAction("Point", InputActionType.PassThrough);
        FlowerPoint.expectedControlType = "Vector2";
        FlowerPoint.AddBinding("<Mouse>/position", groups: "KeyboardMouse");
        FlowerPoint.AddBinding("<Touchscreen>/primaryTouch/position", groups: "Touch");

        // 2D movement (WASD or left stick)
        FlowerMove = FlowerMap.AddAction("Move", InputActionType.Value);
        FlowerMove.expectedControlType = "Vector2";
        FlowerMove.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w", groups: "KeyboardMouse")
            .With("Down", "<Keyboard>/s", groups: "KeyboardMouse")
            .With("Left", "<Keyboard>/a", groups: "KeyboardMouse")
            .With("Right", "<Keyboard>/d", groups: "KeyboardMouse");
        FlowerMove.AddBinding("<Gamepad>/leftStick", groups: "Gamepad");
    }

    private void BuildGlobalMap()
    {
        GlobalMap = Asset.AddActionMap("Global");

        Pause = GlobalMap.AddAction("Pause", InputActionType.Button);
        Pause.AddBinding("<Keyboard>/escape", groups: "KeyboardMouse");
        Pause.AddBinding("<Gamepad>/start", groups: "Gamepad");
    }

    // ─────────────────────────────────────────────────────────────
    // Gamepad Virtual Cursor
    // ─────────────────────────────────────────────────────────────

    private void UpdateGamepadCursor()
    {
        var gp = Gamepad.current;
        if (gp == null) return;

        Vector2 stick = gp.rightStick.ReadValue();
        if (stick.sqrMagnitude > 0.04f) // dead zone
        {
            _gamepadCursorPos += stick * GamepadCursorSpeed * Time.unscaledDeltaTime;
            _gamepadCursorPos.x = Mathf.Clamp(_gamepadCursorPos.x, 0f, Screen.width);
            _gamepadCursorPos.y = Mathf.Clamp(_gamepadCursorPos.y, 0f, Screen.height);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Device Auto-Detection
    // ─────────────────────────────────────────────────────────────

    private void OnActionChange(object obj, InputActionChange change)
    {
        if (change != InputActionChange.ActionPerformed) return;
        if (obj is not InputAction action) return;

        var device = action.activeControl?.device;
        if (device == null) return;

        ActiveControlScheme newScheme;
        if (device is Gamepad)
            newScheme = ActiveControlScheme.Gamepad;
        else if (device is Touchscreen)
            newScheme = ActiveControlScheme.Touch;
        else
            newScheme = ActiveControlScheme.KeyboardMouse;

        if (newScheme != Scheme)
        {
            Scheme = newScheme;
            OnSchemeChanged?.Invoke(newScheme);
            Debug.Log($"[IrisInput] Control scheme changed: {newScheme}");

            // Reset gamepad cursor to screen center on gamepad activation
            if (newScheme == ActiveControlScheme.Gamepad)
                _gamepadCursorPos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }
    }
}
