using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// Persistent (DDoL) cursor manager that switches the hardware cursor
/// based on what the mouse is hovering over. Auto-spawns on scene load.
///
/// Cursor contexts (highest priority first):
///   1. Watering   — hovering a WaterablePlant (watering pail icon)
///   2. Fridge     — hovering FridgeController (open-fridge icon)
///   3. Phone      — hovering PhoneController (phone icon)
///   4. Drawer     — hovering DrawerController (open/pull icon)
///   5. Drink      — hovering SimpleDrinkManager or drink station (pouring icon)
///   6. Light      — hovering LightSwitch (lightbulb icon)
///   7. Interact   — hovering InteractableHighlight, PlaceableObject, etc. (pinch)
///   8. Default    — OS cursor (null)
/// </summary>
public class GlobalCursorManager : MonoBehaviour
{
    public static GlobalCursorManager Instance { get; private set; }

    private enum CursorType { Default, Interact, Watering, Fridge, Phone, Drawer, Drink, Sponge, Grab, Scissors, Swatter, Light }

    // ── Cursor source textures ──
    private Texture2D _interactCursor;
    private Texture2D _wateringCursor;
    private Texture2D _fridgeCursor;
    private Texture2D _phoneCursor;
    private Texture2D _drawerCursor;
    private Texture2D _drinkCursor;
    private Texture2D _spongeCursor;
    private Texture2D _grabCursor;
    private Texture2D _scissorsCursor;
    private Texture2D _swatterCursor;
    private Texture2D _lightCursor;
    private Texture2D _defaultCursor;

    private Vector2 _interactHotSpot;
    private Vector2 _wateringHotSpot;
    private Vector2 _fridgeHotSpot;
    private Vector2 _phoneHotSpot;
    private Vector2 _drawerHotSpot;
    private Vector2 _drinkHotSpot;
    private Vector2 _spongeHotSpot;
    private Vector2 _scissorsHotSpot;
    private Vector2 _swatterHotSpot;
    private Vector2 _grabHotSpot;
    private Vector2 _lightHotSpot;
    private Vector2 _defaultHotSpot;

    // ── Smooth fade state ──
    // Pre-baked alpha ramp: _alphaBank[cursorType][step] where step 0=transparent, Steps-1=full
    private const int AlphaSteps = 16;
    private Texture2D[][] _alphaBank;

    private CursorType _desiredType = CursorType.Default;   // what raycast wants
    private CursorType _displayedType = CursorType.Default;  // what's currently shown

    [Header("Context Tick")]
    [Tooltip("SFX played when cursor changes from default to a context type.")]
    [SerializeField] private AudioClip _contextTickSFX;
    [SerializeField] private float _contextTickVolume = 0.15f;
    private float _contextTickCooldown;
    private float _currentAlpha;         // 0 = invisible, 1 = full
    private float _targetAlpha;
    private float _hoverTimer;
    private float _fadeProgress;         // 0-1 normalized progress of current fade
    private int _lastStep = -1;          // avoid redundant SetCursor calls

    // Tuning — loaded from Resources or uses defaults
    private CursorFadeSettings _fadeSettings;

    /// <summary>
    /// Lock the cursor to a specific type while an interaction is active (e.g. scrubbing, watering).
    /// While locked, ClassifyHit is skipped and the locked cursor stays visible.
    /// Call with null to unlock.
    /// </summary>
    private static CursorType? s_lockedCursor;
    public static void UnlockCursor() => s_lockedCursor = null;
    public static bool IsCursorLocked => s_lockedCursor.HasValue;

    /// <summary>Lock cursor to a specific context while an interaction is active.</summary>
    private static bool s_hideCursorForSponge;
    public static void HideCursorForSponge(bool hide) => s_hideCursorForSponge = hide;
    public static void LockCursorToSponge()   => s_lockedCursor = CursorType.Sponge;
    public static void LockCursorToWatering() => s_lockedCursor = CursorType.Watering;
    public static void LockCursorToSwatter()  => s_lockedCursor = CursorType.Swatter;
    public static void LockCursorToScissors() => s_lockedCursor = CursorType.Scissors;
    public static void LockCursorToDrink()    => s_lockedCursor = CursorType.Drink;

    /// <summary>Current cursor opacity (0-1). Read by CursorWorldShadow.</summary>
    public float CurrentAlpha => _currentAlpha;

    /// <summary>True when showing a context cursor (not default/grab).</summary>
    public bool IsContextCursor => _displayedType != CursorType.Default && _displayedType != CursorType.Grab;

    /// <summary>Returns the full-opacity source texture for the current cursor type. Used by PourCursorOverlay.</summary>
    public Texture2D GetCurrentCursorTexture()
    {
        return _displayedType switch
        {
            CursorType.Interact => _interactCursor,
            CursorType.Watering => _wateringCursor,
            CursorType.Fridge   => _fridgeCursor,
            CursorType.Phone    => _phoneCursor,
            CursorType.Drawer   => _drawerCursor,
            CursorType.Drink    => _drinkCursor,
            CursorType.Sponge   => _spongeCursor,
            CursorType.Grab     => _grabCursor,
            CursorType.Scissors => _scissorsCursor,
            CursorType.Swatter  => _swatterCursor,
            CursorType.Light    => _lightCursor,
            _ => null
        };
    }

    private Camera _cachedCamera;
    private float _cameraRefetchTimer;

    // Raycast against everything except UI (layer 5) and Ignore Raycast (layer 2)
    private const int RaycastMask = ~((1 << 5) | (1 << 2));

    // Pre-allocated raycast buffer (avoids per-frame allocation from RaycastAll)
    private static readonly RaycastHit[] s_cursorHitBuffer = new RaycastHit[16];
    private static readonly HitDistanceComparer s_hitComparer = new();
    private class HitDistanceComparer : System.Collections.Generic.IComparer<RaycastHit>
    {
        public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoSpawn()
    {
        if (Instance != null) return;
        var go = new GameObject("GlobalCursorManager");
        go.AddComponent<GlobalCursorManager>();
        Debug.Log("[GlobalCursorManager] Auto-spawned via RuntimeInitialize.");
    }

    /// <summary>Call from any scene script's Start() as a safety net if AutoSpawn didn't fire.</summary>
    public static void EnsureExists()
    {
        if (Instance != null) return;
        AutoSpawn();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Load fade settings from Resources (create via Iris > Cursor Fade Settings)
        _fadeSettings = Resources.Load<CursorFadeSettings>("CursorFadeSettings");
        if (_fadeSettings == null)
        {
            _fadeSettings = ScriptableObject.CreateInstance<CursorFadeSettings>();
        }

        LoadCursorTextures();

        // Show our custom default cursor immediately so the OS arrow never flashes.
        ApplyDefaultCursor();

        Debug.Log($"[GlobalCursorManager] Awake — interact={(_interactCursor != null ? "OK" : "NULL")}, " +
                  $"watering={(_wateringCursor != null ? "OK" : "NULL")}, " +
                  $"fridge={(_fridgeCursor != null ? "OK" : "NULL")}, " +
                  $"phone={(_phoneCursor != null ? "OK" : "NULL")}, " +
                  $"drawer={(_drawerCursor != null ? "OK" : "NULL")}, " +
                  $"drink={(_drinkCursor != null ? "OK" : "NULL")}, " +
                  $"sponge={(_spongeCursor != null ? "OK" : "NULL")}, " +
                  $"default={(_defaultCursor != null ? "OK" : "NULL")}");
    }

    // Track procedurally generated textures so we only destroy those (not Resources assets)
    private readonly System.Collections.Generic.HashSet<Texture2D> _proceduralTextures = new();

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            Instance = null;
        }
        foreach (var tex in _proceduralTextures)
        {
            if (tex != null) Destroy(tex);
        }
        _proceduralTextures.Clear();
    }

    /// <summary>
    /// Create a CPU-readable RGBA32 copy of a texture. Works regardless of
    /// the source texture's compression or import settings.
    /// </summary>
    private Texture2D MakeCursorCopy(Texture2D source)
    {
        // Render the source to a temporary RenderTexture, then read it back as RGBA32
        var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(source, rt);

        var prev = RenderTexture.active;
        RenderTexture.active = rt;

        var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        copy.filterMode = FilterMode.Point;
        copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        copy.Apply();

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        _proceduralTextures.Add(copy); // track for cleanup
        return copy;
    }

    private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;
    private void OnSceneLoaded(Scene s, LoadSceneMode m) { _cachedCamera = null; _cameraRefetchTimer = 0f; s_lockedCursor = null; s_hideCursorForSponge = false; }

    // ══════════════════════════════════════════════════════════════
    // Texture loading
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Loads each cursor from Resources/Cursors/ by name.
    /// If an art asset exists it's used; otherwise falls back to a procedural placeholder.
    /// To replace a cursor: drop a 32x32 PNG (Read/Write enabled, Cursor texture type)
    /// into Assets/Resources/Cursors/ with the matching name:
    ///   pinch.png, watering.png, fridge.png, phone.png, drawer.png, drink.png
    /// </summary>
    private void LoadCursorTextures()
    {
        const int S = 32;
        Vector2 center = new Vector2(S / 2f, S / 2f);

        _interactCursor = LoadOrGenerate("pinch", null); // pinch.png already exists
        _interactHotSpot = Vector2.zero; // top-left (matches flower scene CursorContext)

        _wateringCursor = LoadOrGenerate("watering", GenWateringPail(S));
        _wateringHotSpot = new Vector2(6f, 2f);

        _fridgeCursor = LoadOrGenerate("fridge", GenFridge(S));
        _fridgeHotSpot = center;

        _phoneCursor = LoadOrGenerate("phone", GenPhone(S));
        _phoneHotSpot = center;

        _drawerCursor = LoadOrGenerate("drawer", GenDrawer(S));
        _drawerHotSpot = center;

        _drinkCursor = LoadOrGenerate("drink", GenDrinkPour(S));
        _drinkHotSpot = center;

        _spongeCursor = LoadOrGenerate("sponge", GenSponge(S));
        _spongeHotSpot = center;

        _grabCursor = LoadOrGenerate("grab", GenGrab(S));
        _grabHotSpot = center;

        _scissorsCursor = LoadOrGenerate("scissors", GenScissors(S));
        _scissorsHotSpot = center;

        _swatterCursor = LoadOrGenerate("swatter", GenSwatter(S));
        _swatterHotSpot = center;

        _lightCursor = LoadOrGenerate("light", GenLightBulb(S));
        _lightHotSpot = center;

        _defaultCursor = LoadOrGenerate("default", GenArrow(S));
        _defaultHotSpot = Vector2.zero; // top-left tip of the arrow

        // Pre-bake alpha ramp for each cursor type
        int typeCount = System.Enum.GetValues(typeof(CursorType)).Length;
        _alphaBank = new Texture2D[typeCount][];
        BakeAlphaRamp(CursorType.Interact, _interactCursor);
        BakeAlphaRamp(CursorType.Watering, _wateringCursor);
        BakeAlphaRamp(CursorType.Fridge,   _fridgeCursor);
        BakeAlphaRamp(CursorType.Phone,    _phoneCursor);
        BakeAlphaRamp(CursorType.Drawer,   _drawerCursor);
        BakeAlphaRamp(CursorType.Drink,    _drinkCursor);
        BakeAlphaRamp(CursorType.Sponge,   _spongeCursor);
        BakeAlphaRamp(CursorType.Scissors, _scissorsCursor);
        BakeAlphaRamp(CursorType.Swatter,  _swatterCursor);
        BakeAlphaRamp(CursorType.Light,    _lightCursor);
        // Grab doesn't fade — no bank needed
    }

    private void BakeAlphaRamp(CursorType type, Texture2D source)
    {
        if (source == null) return;
        var srcPx = source.GetPixels32();
        int w = source.width, h = source.height;
        var ramp = new Texture2D[AlphaSteps];

        for (int step = 0; step < AlphaSteps; step++)
        {
            float alpha = (float)step / (AlphaSteps - 1);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            var px = new Color32[srcPx.Length];
            for (int i = 0; i < srcPx.Length; i++)
            {
                px[i] = srcPx[i];
                px[i].a = (byte)(srcPx[i].a * alpha);
            }
            tex.SetPixels32(px);
            tex.Apply();
            _proceduralTextures.Add(tex);
            ramp[step] = tex;
        }
        _alphaBank[(int)type] = ramp;
    }

    /// <summary>
    /// Try loading Resources/Cursors/{name}. If found, discard the fallback and return the loaded texture.
    /// If not found, return the procedural fallback (may be null for pinch which has no procedural).
    /// </summary>
    private Texture2D LoadOrGenerate(string name, Texture2D proceduralFallback)
    {
        var loaded = Resources.Load<Texture2D>($"Cursors/{name}");
        if (loaded != null)
        {
            // Always make a RGBA32 copy — works regardless of import settings
            var copy = MakeCursorCopy(loaded);
            if (proceduralFallback != null)
                Destroy(proceduralFallback);
            return copy;
        }

        if (proceduralFallback == null)
            Debug.LogWarning($"[GlobalCursorManager] Cursors/{name} not found and no procedural fallback.");

        // Track procedural texture so we can clean it up on destroy
        if (proceduralFallback != null)
            _proceduralTextures.Add(proceduralFallback);

        return proceduralFallback;
    }

    // ══════════════════════════════════════════════════════════════
    // Update — raycast and pick cursor
    // ══════════════════════════════════════════════════════════════

    private void Update()
    {
        if (_contextTickCooldown > 0f) _contextTickCooldown -= Time.unscaledDeltaTime;

        // Re-fetch Camera.main periodically (every 0.5s) or when null
        _cameraRefetchTimer -= Time.unscaledDeltaTime;
        if (_cachedCamera == null || _cameraRefetchTimer <= 0f)
        {
            _cachedCamera = Camera.main;
            _cameraRefetchTimer = 0.5f;
        }
        if (_cachedCamera == null) { ApplyCursor(CursorType.Default); return; }

        // F7 debug: log grab state + wall placement info
        if (Input.GetKeyDown(KeyCode.F7))
        {
            Debug.Log($"[GlobalCursorManager] HeldObject={ObjectGrabber.HeldObject?.name ?? "null"} IsHolding={ObjectGrabber.IsHoldingObject}");
            Debug.Log($"[GlobalCursorManager] CurrentSurface={ObjectGrabber.CurrentSurface?.name ?? "null"} " +
                      $"IsVertical={ObjectGrabber.CurrentSurface?.IsVertical}");

            // Log all wall-mountable items and their last placed surface
            foreach (var p in PlaceableObject.All)
            {
                if (p.CanWallMount)
                    Debug.Log($"[WallDebug] '{p.name}' state={p.CurrentState} lastSurface={p.LastPlacedSurface?.name ?? "none"} pos={p.transform.position}");
            }

            // Log what the surface raycast is hitting right now
            var debugCam = Camera.main;
            if (debugCam != null)
            {
                Vector2 mp = IrisInput.CursorPosition;
                Ray debugRay = debugCam.ScreenPointToRay(mp);
                int surfLayer = LayerMask.GetMask("Surfaces");
                var debugHits = Physics.RaycastAll(debugRay, 100f, surfLayer);
                Debug.Log($"[WallDebug] Surface raycast hits: {debugHits.Length} (layer mask={surfLayer})");
                for (int i = 0; i < debugHits.Length; i++)
                {
                    var h = debugHits[i];
                    var surf = h.collider.GetComponentInParent<PlacementSurface>();
                    Debug.Log($"[WallDebug]   hit[{i}]: '{h.collider.name}' layer={h.collider.gameObject.layer} " +
                              $"dist={h.distance:F2} isTrigger={h.collider.isTrigger} " +
                              $"surface={surf?.name ?? "null"} isVertical={surf?.IsVertical}");
                }
            }
        }

        // Hide cursor entirely while sponge visual is active — must be checked
        // FIRST so nothing below can re-show or SetCursor over it.
        if (s_hideCursorForSponge)
        {
            Cursor.visible = false;
            return;
        }

        // Force cursor visible when paused — menus need the cursor
        if ((SimplePauseMenu.Instance != null && SimplePauseMenu.Instance.IsPaused)
            || Time.timeScale == 0f)
        {
            if (!Cursor.visible) Cursor.visible = true;
            ApplyCursor(CursorType.Default);
            return;
        }

        // While holding an item, hide the cursor entirely — the held object is the feedback
        if (ObjectGrabber.IsHoldingObject)
        {
            if (_displayedType != CursorType.Grab)
            {
                _displayedType = CursorType.Grab;
                _currentAlpha = 0f;
                _targetAlpha = 0f;
                _hoverTimer = 0f;
                _lastStep = -1;
                Cursor.visible = false;
            }
            return;
        }
        else if (_displayedType == CursorType.Grab)
        {
            // Just released — restore cursor visibility
            Cursor.visible = true;
            _displayedType = CursorType.Default;
            _lastStep = -1;
        }

        // Cursor lock — active interaction holds the cursor type steady
        if (s_lockedCursor.HasValue)
        {
            ApplyCursor(s_lockedCursor.Value);
            return;
        }

        // If cursor is over UI (buttons, panels, etc.), use default cursor
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            ApplyCursor(CursorType.Default);
            return;
        }

        Vector2 cursorPos = IrisInput.CursorPosition;
        // Use ApartmentManager's manual ray (builds from Camera.main's current
        // transform + orthoSize, avoiding stale projection matrices).
        Ray ray;
        if (ApartmentManager.Instance != null)
            ray = ApartmentManager.Instance.ScreenPointToRay(cursorPos);
        else
            ray = _cachedCamera.ScreenPointToRay(cursorPos);

        CursorType desired = CursorType.Default;

        // RaycastNonAlloc so we can see through PlacementSurface triggers to the items behind them.
        // Sort by distance so the nearest interactable wins (prevents stain behind plant confusion).
        int hitCount = Physics.RaycastNonAlloc(ray, s_cursorHitBuffer, 100f, RaycastMask);
        if (hitCount > 1)
            System.Array.Sort(s_cursorHitBuffer, 0, hitCount, s_hitComparer);
        for (int i = 0; i < hitCount; i++)
        {
            var go = s_cursorHitBuffer[i].collider.gameObject;
            var type = ClassifyHit(go);
            if (type != CursorType.Default)
            {
                desired = type;
                break;
            }
        }

        // During pour drag, the PourCursorOverlay handles the visual cursor
        if (PourDragHelper.IsDragging)
        {
            _desiredType = desired;
            return;
        }

        ApplyCursor(desired);
    }

    private static CursorType ClassifyHit(GameObject go)
    {
        // During dates, restrict context cursors by phase
        var dsm = DateSessionManager.Instance;
        if (dsm != null && dsm.IsDateActive)
        {
            var phase = dsm.CurrentDatePhase;

            // Phase 3 (Reveal): only show Interact cursor for inspectable items
            if (phase == DateSessionManager.DatePhase.Reveal)
            {
                if (Has<InteractableHighlight>(go)
                 || Has<PlaceableObject>(go)
                 || HasFlowerTag(go))           return CursorType.Interact;
                return CursorType.Default;
            }

            // Phase 2 (BackgroundJudging): drink interactions + interact
            if (phase == DateSessionManager.DatePhase.BackgroundJudging)
            {
                if (Has<DrinkGlass>(go))        return CursorType.Interact;
                if (Has<BottleItem>(go))        return CursorType.Interact;
                if (Has<InteractableHighlight>(go)
                 || Has<PlaceableObject>(go))   return CursorType.Interact;
                return CursorType.Default;
            }

            // Phase 1 (Arrival): interact cursor only, no special contexts
            if (Has<InteractableHighlight>(go)
             || Has<PlaceableObject>(go)
             || HasFlowerTag(go))               return CursorType.Interact;
            return CursorType.Default;
        }

        // Normal (non-date) cursor classification
        if (Has<WaterablePlant>(go))       return CursorType.Interact;
        if (Has<FridgeController>(go))     return CursorType.Interact;
        if (Has<PhoneController>(go))      return CursorType.Interact;
        if (Has<InteractableHighlight>(go)
         || Has<PlaceableObject>(go)
         || Has<RecordSlot>(go)
         || HasFlowerTag(go))              return CursorType.Interact;
        { // Only show drawer cursor when the drawer is closed; when open, let the ray
          // fall through so items inside the cubby get their own cursor.
            var dc = go.GetComponent<DrawerController>() ?? go.GetComponentInParent<DrawerController>();
            if (dc != null && dc.CurrentState == DrawerController.State.Closed)
                return CursorType.Drawer;
        }
        if (Has<DrinkGlass>(go))            return CursorType.Interact;
        if (Has<BottleItem>(go))           return CursorType.Interact;
        if (Has<FlyController>(go))         return CursorType.Interact;
        if (Has<CleanableSurface>(go))     return CursorType.Interact;
        if (Has<ScissorStation>(go))      return CursorType.Scissors;
        if (Has<LightSwitch>(go))          return CursorType.Interact;
        return CursorType.Default;
    }

    private static bool Has<T>(GameObject go) where T : Component
    {
        return go.GetComponent<T>() != null || go.GetComponentInParent<T>() != null;
    }

    private static bool HasFlowerTag(GameObject go)
    {
        return go.CompareTag("Petal")
            || go.CompareTag("Leaf")
            || go.CompareTag("Crown");
    }

    private void ApplyCursor(CursorType type)
    {
        _desiredType = type;

        if (type == CursorType.Grab) return;

        // Same type as last frame — nothing to do
        if (type == _displayedType) return;

        // Audio tick when entering a context cursor from default
        CursorType prev = _displayedType;
        _displayedType = type;
        _currentAlpha = 1f;

        if (prev == CursorType.Default && type != CursorType.Default
            && _contextTickSFX != null && _contextTickCooldown <= 0f)
        {
            AudioManager.Instance?.PlaySFX(_contextTickSFX, _contextTickVolume);
            _contextTickCooldown = 0.15f;
        }

        if (type == CursorType.Default)
        {
            ApplyDefaultCursor();
            return;
        }

        // Swap to the full-opacity context cursor texture immediately
        var source = GetSourceTexture(type);
        if (source != null)
        {
            Vector2 hotSpot = GetHotSpot(type);
            Cursor.SetCursor(source, hotSpot, CursorMode.Auto);
        }
    }

    private Texture2D GetSourceTexture(CursorType type)
    {
        return type switch
        {
            CursorType.Interact => _interactCursor,
            CursorType.Watering => _wateringCursor,
            CursorType.Fridge   => _fridgeCursor,
            CursorType.Phone    => _phoneCursor,
            CursorType.Drawer   => _drawerCursor,
            CursorType.Drink    => _drinkCursor,
            CursorType.Sponge   => _spongeCursor,
            CursorType.Grab     => _grabCursor,
            CursorType.Scissors => _scissorsCursor,
            CursorType.Swatter  => _swatterCursor,
            CursorType.Light    => _lightCursor,
            _ => null
        };
    }

    /// <summary>
    /// Apply our custom default cursor texture (replaces the OS arrow).
    /// Falls back to OS cursor if the default texture failed to load.
    /// </summary>
    private void ApplyDefaultCursor()
    {
        if (_defaultCursor != null)
            Cursor.SetCursor(_defaultCursor, _defaultHotSpot, CursorMode.Auto);
        else
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }

    private Vector2 GetHotSpot(CursorType type)
    {
        return type switch
        {
            CursorType.Interact => _interactHotSpot,
            CursorType.Watering => _wateringHotSpot,
            CursorType.Fridge   => _fridgeHotSpot,
            CursorType.Phone    => _phoneHotSpot,
            CursorType.Drawer   => _drawerHotSpot,
            CursorType.Drink    => _drinkHotSpot,
            CursorType.Sponge   => _spongeHotSpot,
            CursorType.Grab     => _grabHotSpot,
            CursorType.Scissors => _scissorsHotSpot,
            CursorType.Swatter  => _swatterHotSpot,
            CursorType.Light    => _lightHotSpot,
            _ => Vector2.zero
        };
    }

    // ══════════════════════════════════════════════════════════════
    // Procedural cursor generators (32x32 pixel art)
    // ══════════════════════════════════════════════════════════════

    private static Texture2D MakeTex(int s)
    {
        var t = new Texture2D(s, s, TextureFormat.RGBA32, false);
        t.filterMode = FilterMode.Point;
        return t;
    }

    private static void Set(Color32[] px, int w, int x, int y, Color32 c)
    {
        if (x >= 0 && x < w && y >= 0 && y < w)
            px[y * w + x] = c;
    }

    private static void FillRect(Color32[] px, int w, int x0, int y0, int x1, int y1, Color32 c)
    {
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                Set(px, w, x, y, c);
    }

    private static void DrawLine(Color32[] px, int w, int x0, int y0, int x1, int y1, Color32 c)
    {
        int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            Set(px, w, x0, y0, c);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    // ── Watering pail ──────────────────────────────────────────
    private static Texture2D GenWateringPail(int s)
    {
        var tex = MakeTex(s);
        var px = new Color32[s * s];

        var body = new Color32(110, 160, 200, 255);
        var dark = new Color32(70, 110, 150, 255);
        var hndl = new Color32(90, 140, 180, 255);
        var drop = new Color32(100, 180, 230, 180);
        var rim  = new Color32(130, 180, 210, 255);

        // Bucket body — tapered
        for (int y = 10; y <= 24; y++)
        {
            float t = (y - 10f) / 14f;
            int l = (int)Mathf.Lerp(10, 12, t);
            int r = (int)Mathf.Lerp(24, 22, t);
            for (int x = l; x <= r; x++)
                Set(px, s, x, y, (y == 24 || x == l || x == r) ? dark : body);
        }
        // Rim
        for (int x = 9; x <= 25; x++) { Set(px, s, x, 24, rim); Set(px, s, x, 25, rim); }
        // Handle arc
        for (int y = 26; y <= 30; y++)
        {
            int o = y - 25;
            Set(px, s, 13 - o, y, hndl); Set(px, s, 14 - o, y, hndl);
            Set(px, s, 21 + o, y, hndl); Set(px, s, 20 + o, y, hndl);
        }
        for (int x = 8; x <= 26; x++) Set(px, s, x, 30, hndl);
        // Spout
        for (int y = 16; y <= 22; y++)
        {
            int sx = 10 - (y - 16);
            Set(px, s, sx, y, dark); Set(px, s, sx + 1, y, body);
        }
        // Water drops
        Set(px, s, 4, 6, drop); Set(px, s, 3, 4, drop); Set(px, s, 5, 2, drop);

        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    // ── Fridge (open door icon) ────────────────────────────────
    private static Texture2D GenFridge(int s)
    {
        var tex = MakeTex(s);
        var px = new Color32[s * s];

        var body = new Color32(220, 225, 230, 255);   // white/light grey
        var edge = new Color32(160, 165, 170, 255);    // darker edge
        var door = new Color32(200, 210, 215, 255);    // slightly off-white door
        var hndl = new Color32(130, 135, 140, 255);    // handle
        var cold = new Color32(170, 210, 240, 200);    // cold air wisps
        var line = new Color32(140, 145, 150, 255);    // divider line

        // Fridge body
        FillRect(px, s, 8, 4, 24, 28, body);
        // Edges
        for (int y = 4; y <= 28; y++) { Set(px, s, 8, y, edge); Set(px, s, 24, y, edge); }
        for (int x = 8; x <= 24; x++) { Set(px, s, x, 4, edge); Set(px, s, x, 28, edge); }
        // Middle divider (freezer/fridge split)
        for (int x = 8; x <= 24; x++) Set(px, s, x, 18, line);
        // Handle (right side)
        FillRect(px, s, 22, 20, 23, 24, hndl);
        FillRect(px, s, 22, 8, 23, 12, hndl);
        // Open door hint — partial door ajar on right side
        DrawLine(px, s, 25, 5, 28, 8, door);
        DrawLine(px, s, 25, 27, 28, 24, door);
        for (int y = 9; y <= 23; y++) Set(px, s, 25 + (y < 16 ? 1 : 1), y, door);
        // Cold air wisps
        Set(px, s, 26, 14, cold); Set(px, s, 27, 16, cold); Set(px, s, 28, 12, cold);
        Set(px, s, 26, 20, cold); Set(px, s, 27, 22, cold);

        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    // ── Phone (handset icon) ───────────────────────────────────
    private static Texture2D GenPhone(int s)
    {
        var tex = MakeTex(s);
        var px = new Color32[s * s];

        var body = new Color32(60, 60, 65, 255);      // dark handset
        var ear  = new Color32(80, 80, 85, 255);      // earpiece/mic
        var ring = new Color32(180, 200, 140, 255);    // ringing indicator
        var cord = new Color32(50, 50, 55, 255);       // cord

        // Handset — classic phone shape (vertical, ear at top, mic at bottom)
        // Earpiece (top)
        FillRect(px, s, 12, 24, 20, 27, ear);
        FillRect(px, s, 11, 25, 21, 26, ear);
        // Handle (middle bar)
        FillRect(px, s, 14, 10, 18, 24, body);
        // Mouthpiece (bottom)
        FillRect(px, s, 12, 6, 20, 10, ear);
        FillRect(px, s, 11, 7, 21, 9, ear);
        // Ring indicators (sound waves)
        Set(px, s, 23, 26, ring); Set(px, s, 25, 27, ring); Set(px, s, 27, 26, ring);
        Set(px, s, 24, 28, ring); Set(px, s, 26, 29, ring);
        Set(px, s, 9, 26, ring); Set(px, s, 7, 27, ring); Set(px, s, 5, 26, ring);
        Set(px, s, 8, 28, ring); Set(px, s, 6, 29, ring);

        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    // ── Drawer (open/pull arrow icon) ──────────────────────────
    private static Texture2D GenDrawer(int s)
    {
        var tex = MakeTex(s);
        var px = new Color32[s * s];

        var wood = new Color32(170, 130, 90, 255);     // warm wood
        var dark = new Color32(130, 95, 65, 255);       // wood edge
        var hndl = new Color32(200, 180, 140, 255);     // brass handle
        var arrw = new Color32(240, 230, 200, 255);     // arrow (pull indicator)

        // Drawer front face
        FillRect(px, s, 6, 12, 26, 24, wood);
        // Edges
        for (int y = 12; y <= 24; y++) { Set(px, s, 6, y, dark); Set(px, s, 26, y, dark); }
        for (int x = 6; x <= 26; x++) { Set(px, s, x, 12, dark); Set(px, s, x, 24, dark); }
        // Inner panel bevel
        FillRect(px, s, 8, 14, 24, 22, new Color32(180, 140, 100, 255));
        // Handle (horizontal bar in center)
        FillRect(px, s, 13, 17, 19, 19, hndl);
        // Pull arrow pointing down (open direction)
        DrawLine(px, s, 16, 6, 16, 11, arrw);
        DrawLine(px, s, 16, 6, 13, 9, arrw);
        DrawLine(px, s, 16, 6, 19, 9, arrw);
        // Second arrow line for thickness
        DrawLine(px, s, 15, 6, 15, 11, arrw);
        DrawLine(px, s, 17, 6, 17, 11, arrw);

        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    // ── Drink pour (bottle pouring icon) ───────────────────────
    private static Texture2D GenDrinkPour(int s)
    {
        var tex = MakeTex(s);
        var px = new Color32[s * s];

        var glass = new Color32(180, 200, 220, 220);   // glass/bottle
        var dark  = new Color32(120, 140, 160, 255);   // glass edge
        var liquid = new Color32(200, 140, 80, 255);   // amber liquid
        var drops = new Color32(210, 160, 100, 200);   // pour stream

        // Bottle (tilted ~30deg, top-right to bottom-left)
        // Bottle body — angled rectangle
        for (int i = 0; i < 14; i++)
        {
            int bx = 18 + i / 2;
            int by = 14 + i;
            FillRect(px, s, bx - 2, by, bx + 2, by, glass);
            Set(px, s, bx - 3, by, dark);
            Set(px, s, bx + 3, by, dark);
        }
        // Bottle neck (narrower, towards pour point)
        for (int i = 0; i < 5; i++)
        {
            int nx = 17 + i / 3;
            int ny = 10 + i;
            Set(px, s, nx - 1, ny, dark);
            Set(px, s, nx, ny, glass);
            Set(px, s, nx + 1, ny, dark);
        }
        // Liquid inside bottle
        for (int i = 6; i < 12; i++)
        {
            int lx = 18 + i / 2;
            int ly = 14 + i;
            FillRect(px, s, lx - 1, ly, lx + 1, ly, liquid);
        }
        // Pour stream (drops falling from neck)
        Set(px, s, 16, 9, drops);
        Set(px, s, 15, 7, drops);
        Set(px, s, 14, 5, drops);
        Set(px, s, 13, 3, drops);
        Set(px, s, 15, 4, drops);
        Set(px, s, 14, 6, drops);
        // Glass at bottom-left receiving the pour
        FillRect(px, s, 6, 2, 14, 3, dark);
        FillRect(px, s, 7, 4, 13, 8, glass);
        for (int y = 4; y <= 8; y++) { Set(px, s, 6, y, dark); Set(px, s, 14, y, dark); }
        for (int x = 6; x <= 14; x++) Set(px, s, x, 2, dark);
        // Liquid in glass
        FillRect(px, s, 7, 4, 13, 6, liquid);

        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    // ── Sponge (rounded rectangle with pores) ──────────────────
    private static Texture2D GenSponge(int s)
    {
        var tex = MakeTex(s);
        var px = new Color32[s * s];

        var body = new Color32(230, 210, 120, 255);    // yellow sponge
        var dark = new Color32(200, 180, 90, 255);      // edge/shadow
        var pore = new Color32(210, 190, 100, 220);     // pore dots
        var foam = new Color32(240, 245, 250, 200);     // soap bubbles

        // Sponge body — rounded rectangle
        FillRect(px, s, 8, 8, 24, 22, body);
        // Rounded corners — clip
        Set(px, s, 8, 8, new Color32(0,0,0,0)); Set(px, s, 24, 8, new Color32(0,0,0,0));
        Set(px, s, 8, 22, new Color32(0,0,0,0)); Set(px, s, 24, 22, new Color32(0,0,0,0));
        // Edges
        for (int x = 9; x <= 23; x++) { Set(px, s, x, 8, dark); Set(px, s, x, 22, dark); }
        for (int y = 9; y <= 21; y++) { Set(px, s, 8, y, dark); Set(px, s, 24, y, dark); }
        // Pores (scattered dots inside)
        Set(px, s, 11, 11, pore); Set(px, s, 15, 12, pore); Set(px, s, 20, 10, pore);
        Set(px, s, 13, 16, pore); Set(px, s, 18, 14, pore); Set(px, s, 22, 18, pore);
        Set(px, s, 10, 19, pore); Set(px, s, 16, 20, pore); Set(px, s, 21, 16, pore);
        Set(px, s, 12, 13, pore); Set(px, s, 19, 19, pore);
        // Soap bubbles (top-right, floating above)
        Set(px, s, 22, 24, foam); Set(px, s, 23, 25, foam);
        Set(px, s, 24, 24, foam); Set(px, s, 25, 26, foam);
        Set(px, s, 20, 25, foam); Set(px, s, 26, 24, foam);

        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    // ── Default arrow (classic top-left pixel cursor) ──────────
    private static Texture2D GenArrow(int s)
    {
        var tex = MakeTex(s);
        var px = new Color32[s * s];

        var fill    = new Color32(245, 245, 250, 255);  // bright off-white
        var outline = new Color32(20, 20, 25, 255);     // hard black outline
        var shadow  = new Color32(0, 0, 0, 90);         // soft drop shadow

        // Pixel art arrow — top-left tip at (1, 30), tail extends to ~(12, 18).
        // Y axis is bottom-up in Unity's Texture2D.
        // We sketch the white fill first, then trace the outline around it.

        // Triangular blade (rows from top tip down)
        // Row 30 (top tip): single pixel
        Set(px, s, 1, 30, fill);
        // Rows 29..20: blade widens to the right
        for (int i = 0; i < 11; i++)
        {
            int y = 29 - i;
            int x0 = 1;
            int x1 = 2 + i;
            for (int x = x0; x <= x1; x++) Set(px, s, x, y, fill);
        }
        // Notch at row 19 — split into "leg" on left and "stem" on right
        Set(px, s, 1, 19, fill); Set(px, s, 2, 19, fill); Set(px, s, 3, 19, fill);
        Set(px, s, 6, 19, fill); Set(px, s, 7, 19, fill); Set(px, s, 8, 19, fill);
        // Left leg (rows 18..15)
        for (int y = 18; y >= 15; y--)
        {
            Set(px, s, 1, y, fill); Set(px, s, 2, y, fill); Set(px, s, 3, y, fill);
        }
        // Right stem (rows 18..14)
        for (int y = 18; y >= 14; y--)
        {
            Set(px, s, 6, y, fill); Set(px, s, 7, y, fill); Set(px, s, 8, y, fill);
        }

        // Trace outline: any black pixel where a fill pixel borders empty.
        // We do a second pass that places outline around the fill silhouette.
        var fillCopy = new bool[s * s];
        for (int i = 0; i < px.Length; i++) fillCopy[i] = px[i].a > 0;
        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                if (fillCopy[y * s + x]) continue;
                // Check 4-neighborhood for fill
                bool n = (y + 1 < s && fillCopy[(y + 1) * s + x]);
                bool sN = (y - 1 >= 0 && fillCopy[(y - 1) * s + x]);
                bool e = (x + 1 < s && fillCopy[y * s + (x + 1)]);
                bool w = (x - 1 >= 0 && fillCopy[y * s + (x - 1)]);
                if (n || sN || e || w) Set(px, s, x, y, outline);
            }
        }

        // Drop shadow — one pixel down-right of the silhouette where empty
        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                if (!fillCopy[y * s + x]) continue;
                int sx = x + 1, sy = y - 1;
                if (sx < s && sy >= 0)
                {
                    int idx = sy * s + sx;
                    if (!fillCopy[idx] && px[idx].a == 0)
                        Set(px, s, sx, sy, shadow);
                }
            }
        }

        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    // ── Lightbulb (classic incandescent shape) ─────────────────
    private static Texture2D GenLightBulb(int s)
    {
        var tex = MakeTex(s);
        var px = new Color32[s * s];

        var glass    = new Color32(255, 240, 170, 255);  // warm bulb glass
        var hilite   = new Color32(255, 250, 220, 255);  // bright highlight
        var dark     = new Color32(180, 150, 60, 255);   // glass outline
        var base_    = new Color32(160, 160, 165, 255);  // grey metal base
        var threads  = new Color32(110, 110, 115, 255);  // base threads
        var glow     = new Color32(255, 230, 120, 80);   // soft outer glow

        int cx = 16;

        // Outer glow halo (subtle, large)
        for (int y = 12; y <= 30; y++)
            for (int x = 6; x <= 26; x++)
            {
                int dx = x - cx, dy = y - 22;
                int rsq = dx * dx + dy * dy;
                if (rsq >= 49 && rsq <= 100) Set(px, s, x, y, glow);
            }

        // Bulb glass (round top)
        for (int y = 14; y <= 28; y++)
            for (int x = 9; x <= 23; x++)
            {
                int dx = x - cx, dy = y - 22;
                int rsq = dx * dx + dy * dy;
                if (rsq <= 49) Set(px, s, x, y, glass);
                else if (rsq <= 64) Set(px, s, x, y, dark);
            }

        // Bright highlight on upper-left of bulb
        Set(px, s, 13, 25, hilite); Set(px, s, 14, 26, hilite);
        Set(px, s, 13, 26, hilite); Set(px, s, 12, 24, hilite);
        Set(px, s, 14, 24, hilite);

        // Filament hint inside bulb
        DrawLine(px, s, 14, 21, 16, 23, dark);
        DrawLine(px, s, 16, 23, 18, 21, dark);
        DrawLine(px, s, 18, 21, 16, 19, dark);
        DrawLine(px, s, 16, 19, 14, 21, dark);

        // Neck transition (bulb to base)
        FillRect(px, s, 13, 12, 19, 14, dark);
        FillRect(px, s, 14, 12, 18, 13, base_);

        // Metal screw base (3 thread bands)
        FillRect(px, s, 13, 8, 19, 12, base_);
        for (int x = 13; x <= 19; x++)
        {
            Set(px, s, x, 11, threads);
            Set(px, s, x, 9,  threads);
        }
        // Base outline
        for (int y = 8; y <= 12; y++) { Set(px, s, 13, y, dark); Set(px, s, 19, y, dark); }
        // Contact tip (bottom)
        FillRect(px, s, 15, 6, 17, 8, base_);
        Set(px, s, 15, 6, dark); Set(px, s, 17, 6, dark);

        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    // ── Grab (closed fist / gripping hand) ─────────────────────
    private static Texture2D GenGrab(int s)
    {
        var tex = MakeTex(s);
        var px = new Color32[s * s];

        var skin = new Color32(220, 190, 160, 255);    // skin tone
        var dark = new Color32(180, 150, 120, 255);     // shadow/outline
        var nail = new Color32(240, 210, 190, 255);     // lighter nail/knuckle

        // Closed fist — 4 curled fingers (rows 12-22)
        // Finger 1 (index)
        FillRect(px, s, 8, 18, 12, 24, skin);
        FillRect(px, s, 8, 24, 12, 25, dark);   // tip curl
        Set(px, s, 8, 22, dark); Set(px, s, 12, 22, dark);
        // Finger 2 (middle)
        FillRect(px, s, 13, 19, 17, 25, skin);
        FillRect(px, s, 13, 25, 17, 26, dark);
        Set(px, s, 13, 23, dark); Set(px, s, 17, 23, dark);
        // Finger 3 (ring)
        FillRect(px, s, 18, 18, 22, 24, skin);
        FillRect(px, s, 18, 24, 22, 25, dark);
        Set(px, s, 18, 22, dark); Set(px, s, 22, 22, dark);
        // Finger 4 (pinky)
        FillRect(px, s, 23, 17, 26, 23, skin);
        FillRect(px, s, 23, 23, 26, 24, dark);
        Set(px, s, 23, 21, dark); Set(px, s, 26, 21, dark);

        // Palm (connects fingers)
        FillRect(px, s, 8, 12, 26, 18, skin);
        for (int x = 8; x <= 26; x++) Set(px, s, x, 12, dark);
        for (int y = 12; y <= 18; y++) { Set(px, s, 7, y, dark); Set(px, s, 27, y, dark); }

        // Thumb (curled across front, lower-left)
        FillRect(px, s, 5, 10, 9, 16, skin);
        Set(px, s, 5, 10, dark); Set(px, s, 9, 10, dark);
        for (int y = 10; y <= 16; y++) Set(px, s, 4, y, dark);
        FillRect(px, s, 5, 16, 8, 17, dark); // thumb tip
        // Thumb nail
        Set(px, s, 6, 11, nail); Set(px, s, 7, 11, nail);

        // Knuckle highlights
        Set(px, s, 10, 19, nail); Set(px, s, 15, 20, nail);
        Set(px, s, 20, 19, nail); Set(px, s, 24, 18, nail);

        // Wrist hint (bottom)
        FillRect(px, s, 10, 6, 24, 12, skin);
        for (int x = 10; x <= 24; x++) Set(px, s, x, 6, dark);
        for (int y = 6; y <= 12; y++) { Set(px, s, 9, y, dark); Set(px, s, 25, y, dark); }

        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    // ── Scissors (open scissors icon) ──────────────────────────
    private static Texture2D GenScissors(int s)
    {
        var tex = MakeTex(s);
        var px = new Color32[s * s];

        var blade = new Color32(190, 195, 200, 255);   // steel
        var dark  = new Color32(130, 135, 140, 255);    // edge
        var hndl  = new Color32(80, 60, 50, 255);       // handle
        var pivot = new Color32(160, 165, 170, 255);    // pivot screw

        // Top blade (angled upper-right)
        DrawLine(px, s, 16, 16, 26, 28, blade);
        DrawLine(px, s, 15, 16, 25, 28, blade);
        DrawLine(px, s, 17, 16, 27, 28, dark);

        // Bottom blade (angled lower-right)
        DrawLine(px, s, 16, 16, 26, 4, blade);
        DrawLine(px, s, 15, 16, 25, 4, blade);
        DrawLine(px, s, 17, 16, 27, 4, dark);

        // Pivot point
        FillRect(px, s, 14, 14, 18, 18, pivot);

        // Top handle (loop upper-left)
        for (int y = 20; y <= 28; y++)
        {
            int xl = 4 + (28 - y) / 2;
            int xr = 14;
            Set(px, s, xl, y, hndl);
            Set(px, s, xr, y, hndl);
        }
        for (int x = 4; x <= 14; x++) { Set(px, s, x, 28, hndl); Set(px, s, x, 20, hndl); }

        // Bottom handle (loop lower-left)
        for (int y = 4; y <= 12; y++)
        {
            int xl = 4 + (y - 4) / 2;
            int xr = 14;
            Set(px, s, xl, y, hndl);
            Set(px, s, xr, y, hndl);
        }
        for (int x = 4; x <= 14; x++) { Set(px, s, x, 4, hndl); Set(px, s, x, 12, hndl); }

        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    // ── Swatter (fly swatter — mesh grid on a handle) ───────
    private static Texture2D GenSwatter(int s)
    {
        var tex = MakeTex(s);
        var px = new Color32[s * s];

        var mesh  = new Color32(200, 180, 160, 255);   // beige mesh
        var frame = new Color32(140, 120, 100, 255);    // frame edge
        var hndl  = new Color32(100, 70, 50, 255);      // wood handle

        // Swatter head — rounded rectangle (top portion)
        FillRect(px, s, 8, 14, 24, 28, mesh);
        // Frame border
        for (int x = 8; x <= 24; x++) { Set(px, s, x, 14, frame); Set(px, s, x, 28, frame); }
        for (int y = 14; y <= 28; y++) { Set(px, s, 8, y, frame); Set(px, s, 24, y, frame); }
        // Mesh grid lines
        for (int x = 10; x <= 22; x += 3) for (int y = 16; y <= 26; y++) Set(px, s, x, y, frame);
        for (int y = 16; y <= 26; y += 3) for (int x = 10; x <= 22; x++) Set(px, s, x, y, frame);
        // Rounded corners
        Set(px, s, 8, 14, new Color32(0,0,0,0)); Set(px, s, 24, 14, new Color32(0,0,0,0));
        Set(px, s, 8, 28, new Color32(0,0,0,0)); Set(px, s, 24, 28, new Color32(0,0,0,0));

        // Handle (below the head, center)
        FillRect(px, s, 15, 4, 17, 14, hndl);
        // Handle grip
        Set(px, s, 14, 4, hndl); Set(px, s, 18, 4, hndl);
        Set(px, s, 14, 5, hndl); Set(px, s, 18, 5, hndl);

        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }
}
