using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class PlaceableObject : MonoBehaviour
{
    // ── Static registry (avoids FindObjectsByType) ──────────────────
    private static readonly List<PlaceableObject> s_all = new();
    public static IReadOnlyList<PlaceableObject> All => s_all;

    public enum State { Resting, Held, Placed }

    [Header("Visual Feedback")]
#pragma warning disable 0414
    [Tooltip("Color multiplier applied to the material when held.")]
    [SerializeField] private float heldBrightness = 1.4f;
#pragma warning restore 0414

    [Header("Respawn")]
    [Tooltip("Seconds after leaving world bounds before recovery (prevents flicker).")]
    [SerializeField] private float respawnDelay = 0.5f;

    [Header("Item Classification")]
    [Tooltip("What kind of apartment item this is (for tidiness scoring).")]
    [SerializeField] private ItemCategory _itemCategory = ItemCategory.General;

    [Tooltip("Name of the DropZone this item belongs to (empty = no home zone).")]
    [SerializeField] private string _homeZoneName = "";

    [Tooltip("Secondary zone name (e.g. 'CoffeeTable'). Item counts as home on either zone.")]
    [SerializeField] private string _altHomeZoneName = "";

    [Tooltip("Short description shown when picked up (leave empty to use object name).")]
    [SerializeField] private string _itemDescription = "";

    [Header("Smell / Aging")]
    [Tooltip("Base smell per day left out. Milk=0.5, Trash=0.2, Dish=0.1, General=0.")]
    [SerializeField] private float _smellPerDay;

    [Tooltip("Days this item has been left out (not at home). Increases each morning.")]
    [SerializeField] private int _daysLeftOut;

    [Header("Surface Restrictions")]
    [Tooltip("If true, this object can be placed on vertical (wall) surfaces.")]
    [SerializeField] private bool canWallMount;

    [Tooltip("If true, this object can ONLY be placed on walls (rejects tables/shelves).")]
    [SerializeField] private bool wallOnly;

    [Tooltip("Random rotation range (degrees) applied when spawned on a wall.")]
    [SerializeField] private float crookedAngleRange = 12f;

    public enum RotateMode { Y, X, Z, None }

    [Header("Rotate Input")]
    [Tooltip("Which axis RMB rotates this item around. None = rotation locked.")]
    [SerializeField] private RotateMode _rotateMode = RotateMode.Y;

    [Tooltip("Optional child transform used as the rotation pivot. Leave empty to rotate around the object's own origin.")]
    [SerializeField] private Transform _rotatePivot;

    [Tooltip("Allow pickup to auto-straighten (zero X/Z tilt). Uncheck for items that should keep their orientation when grabbed.")]
    [SerializeField] private bool _allowAutoStraighten = true;

    public RotateMode Rotation => _rotateMode;

    /// <summary>World axis for the rotate input. Zero = rotation locked.</summary>
    public Vector3 RotateAxis => _rotateMode switch
    {
        RotateMode.X => Vector3.right,
        RotateMode.Y => Vector3.up,
        RotateMode.Z => Vector3.forward,
        _ => Vector3.zero
    };

    private Collider _cachedRotatePivotCol;
    private bool _cachedRotatePivotColResolved;

    /// <summary>World-space pivot point for rotation. Uses explicit pivot, then collider center, then transform origin.</summary>
    public Vector3 RotatePivotWorld
    {
        get
        {
            if (_rotatePivot != null) return _rotatePivot.position;
            if (!_cachedRotatePivotColResolved)
            {
                _cachedRotatePivotCol = GetComponent<Collider>();
                _cachedRotatePivotColResolved = true;
            }
            if (_cachedRotatePivotCol != null) return _cachedRotatePivotCol.bounds.center;
            return transform.position;
        }
    }

    public bool AllowAutoStraighten => _allowAutoStraighten;

    [Header("Disheveled Pose")]
    [Tooltip("Captured askew rotation. Use the inspector 'Capture Disheveled Rotation' button to set.")]
    [SerializeField] private Quaternion _disheveledRotation = Quaternion.identity;

    [Tooltip("If true, starts the scene in the disheveled pose.")]
    [SerializeField] private bool _startDishelved;

    [Header("Audio Overrides")]
    [Tooltip("Pickup sound override. If set, plays instead of ObjectGrabber's default.")]
    [SerializeField] private AudioClip _pickupSFXOverride;

    [Tooltip("Place sound override. If set, plays instead of ObjectGrabber's default.")]
    [SerializeField] private AudioClip _placeSFXOverride;

    [Header("Home Position")]
#pragma warning disable 0414
    [Tooltip("If true, captures spawn position as home on Awake (when _homePosition is unset).")]
    [SerializeField] private bool _useSpawnAsHome;
#pragma warning restore 0414

    [Tooltip("World-space home position. Set manually or auto-captured via _useSpawnAsHome.")]
    [SerializeField] private Vector3 _homePosition;

    [Tooltip("Distance threshold — object counts as 'at home' when within this range.")]
    [SerializeField] private float _homeTolerance = 0.2f;

    [Tooltip("World-space home rotation. Auto-captured alongside home position.")]
    [SerializeField] private Quaternion _homeRotation = Quaternion.identity;

    // ── Static world bounds (set by ApartmentManager) ─────────────────
    private static Bounds s_worldBounds = new Bounds(Vector3.zero, Vector3.one * 1000f);

    /// <summary>Set the world bounding box. Objects outside this are recovered to the nearest surface.</summary>
    public static void SetWorldBounds(Bounds bounds) { s_worldBounds = bounds; }

    public State CurrentState { get; private set; } = State.Resting;

    /// <summary>Scale captured at Awake — the true base scale unaffected by animations.</summary>
    public Vector3 OriginalScale { get; set; }
    public ItemCategory Category => _itemCategory;
    public string HomeZoneName => _homeZoneName;
    public string AltHomeZoneName => _altHomeZoneName;
    public string ItemDescription
    {
        get => !string.IsNullOrEmpty(_itemDescription) ? _itemDescription : name;
        set => _itemDescription = value;
    }
    public AudioClip PickupSFXOverride => _pickupSFXOverride;
    public AudioClip PlaceSFXOverride => _placeSFXOverride;
    private bool _isAtHomeOverride;
    public bool IsAtHome
    {
        get => _isAtHomeOverride || IsNearHome;
        set => _isAtHomeOverride = value;
    }

    /// <summary>
    /// True when this object is near its home position (within tolerance).
    /// Excludes held objects and requires a non-zero home position.
    /// </summary>
    public bool IsNearHome => _homePosition != Vector3.zero
        && CurrentState != State.Held
        && Vector3.Distance(transform.position, _homePosition) <= _homeTolerance;
    public bool CanWallMount => canWallMount;
    public bool WallOnly => wallOnly;
    public PlacementSurface LastPlacedSurface => _lastPlacedSurface;

    /// <summary>
    /// True when the item is inside a closed cubby/drawer — signaled by its
    /// ReactableTag.IsPrivate flag. Hidden items are fully excluded from
    /// tidiness scoring (dirty mug in a closed drawer = zero visible mess).
    /// Smell is deliberately NOT gated by this (ReactableTag comment: "Smell
    /// travels through drawers.") so smells still accumulate but aren't amplified.
    /// </summary>
    public bool IsHiddenInCubby
    {
        get
        {
            var tag = GetComponent<ReactableTag>();
            return tag != null && tag.IsPrivate;
        }
    }

    /// <summary>
    /// The effect multiplier from the current placement surface (1-5),
    /// plus bonuses for paired items and stacked dishes.
    /// Returns 1x if:
    ///   - The item isn't on a surface (held, on the floor, etc.).
    ///   - The item is hidden in a closed cubby. Interior surface multipliers
    ///     don't amplify effects for things the player has hidden. The
    ///     reveal/affection path already skips private items via tag.IsPrivate,
    ///     but smell and NemaController don't, so the gate lives here.
    /// Consumers: DateSessionManager (reveal affection), smell accumulation,
    /// NemaController (bored glance weight). TidyScorer skips hidden items
    /// entirely via IsHiddenInCubby rather than weighting them at 1.
    /// </summary>
    public int CurrentEffectMultiplier
    {
        get
        {
            if (_lastPlacedSurface == null) return 1;
            if (IsHiddenInCubby) return 1;
            return _lastPlacedSurface.EffectMultiplier;
        }
    }
    public Vector3 HomePosition => _homePosition;
    public Quaternion HomeRotation => _homeRotation;
    public float HomeTolerance => _homeTolerance;
    public bool HasHome => _homePosition != Vector3.zero;

    /// <summary>
    /// Assign the surface this item is sitting on. Used by DrawerController
    /// to claim editor-placed items that didn't go through OnPlaced().
    /// </summary>
    public void SetLastPlacedSurface(PlacementSurface surface) => _lastPlacedSurface = surface;

    /// <summary>
    /// Configure home settings at runtime (e.g. from BookItem).
    /// Captures current position as home if useSpawnAsHome is true and no home is set yet.
    /// </summary>
    public void ConfigureHome(string homeZone = null, string altHomeZone = null, bool useSpawnAsHome = false)
    {
        if (homeZone != null) _homeZoneName = homeZone;
        if (altHomeZone != null) _altHomeZoneName = altHomeZone;
        if (useSpawnAsHome && _homePosition == Vector3.zero)
        {
            _homePosition = transform.position;
            _homeRotation = transform.rotation;
        }
    }

    /// <summary>
    /// Apply the captured askew pose. No-op if no rotation was captured.
    /// Also swaps the item to the PSX glitch shader (matching the Dead Letter
    /// Love books) so disheveled items visibly jitter with the same PSX feel
    /// as other glitched scene elements. The shader swap uses _GlitchIntensity=0
    /// — identical to the books' serialized material — so the jitter level
    /// comes from the shader's vertex-snap/affine/dither passes, not extra
    /// screen-space displacement.
    /// </summary>
    public void Dishevel()
    {
        if (_disheveledRotation == Quaternion.identity) return;

        transform.rotation = _disheveledRotation;

        if (_rb != null)
        {
            _rb.rotation = _disheveledRotation;
            _rb.angularVelocity = Vector3.zero;
            _rb.linearVelocity = Vector3.zero;
        }

        ApplyGlitch();

#if UNITY_EDITOR
        Debug.Log($"[PlaceableObject] {name} disheveled.");
#endif
    }

    /// <summary>Check if a shader belongs to the PSX family and can be swapped to glitch.</summary>
    private static bool IsPSXShader(Shader shader)
    {
        if (shader == null) return false;
        var n = shader.name;
        return n.StartsWith("Iris/PSX") || n == "Iris/PSXLit" || n == "Iris/PSXInteractable"
            || n == "Iris/PSXLitDissolvable" || n == "Iris/PSXLitGlitch";
    }

    // Pre-glitch saved materials — for non-PSX slots we swap in a temp glitch
    // material and stash the original here so it can be restored perfectly.
    private Material[] _preGlitchMats;

    private void ApplyGlitch()
    {
        if (_instanceMats == null || _instanceMats.Length == 0)
        {
            Debug.LogWarning($"[PlaceableObject] ApplyGlitch skipped on '{name}' — no instance materials");
            return;
        }
        if (_isGlitched) return;
        if (!IsFullyPSX)
        {
            Debug.Log($"[PlaceableObject] ApplyGlitch skipped on '{name}' — has non-PSX materials");
            return;
        }

        if (!s_glitchShaderCached)
        {
            s_glitchShaderCached = true;
            s_glitchShader = Shader.Find("Iris/PSXLitGlitch");
        }
        if (s_glitchShader == null)
        {
            Debug.LogWarning($"[PlaceableObject] ApplyGlitch skipped on '{name}' — Iris/PSXLitGlitch shader not found");
            return;
        }

        // Save every slot's current material so we can restore on RemoveGlitch.
        _preGlitchMats = new Material[_instanceMats.Length];
        for (int i = 0; i < _instanceMats.Length; i++)
            _preGlitchMats[i] = _instanceMats[i];

        for (int i = 0; i < _instanceMats.Length; i++)
        {
            if (_instanceMats[i] == null) continue;

            if (IsPSXShader(_instanceMats[i].shader))
            {
                // PSX slot: swap shader in-place (safe, same property set)
                _instanceMats[i].shader = s_glitchShader;
                _instanceMats[i].SetFloat("_GlitchIntensity", 0f);
            }
            else
            {
                // Non-PSX slot: create a temp PSXLitGlitch material using
                // the original's color so the glitch shimmer is consistent.
                var temp = new Material(s_glitchShader);
                temp.SetFloat("_GlitchIntensity", 0f);
                temp.color = _instanceMats[i].HasProperty("_BaseColor")
                    ? _instanceMats[i].GetColor("_BaseColor")
                    : _instanceMats[i].color;
                // Copy main texture if available
                if (_instanceMats[i].HasProperty("_BaseMap"))
                    temp.SetTexture("_MainTex", _instanceMats[i].GetTexture("_BaseMap"));
                else if (_instanceMats[i].HasProperty("_MainTex"))
                    temp.SetTexture("_MainTex", _instanceMats[i].GetTexture("_MainTex"));
                _instanceMats[i] = temp;
            }
        }

        // Push swapped materials to renderer and refresh highlight cache
        if (_renderer != null) _renderer.materials = _instanceMats;
        var hl = GetComponent<ItemHighlight>();
        if (hl != null) hl.RefreshBaseMaterials();
        _isGlitched = true;
#if UNITY_EDITOR
        Debug.Log($"[PlaceableObject] ApplyGlitch on '{name}': ({_instanceMats.Length} slots)");
#endif
    }

    private void RemoveGlitch()
    {
        if (!_isGlitched) return;
        if (_preGlitchMats == null || _instanceMats == null) return;

        for (int i = 0; i < _instanceMats.Length; i++)
        {
            if (i >= _preGlitchMats.Length) continue;

            if (_instanceMats[i] != _preGlitchMats[i])
            {
                // Non-PSX slot: destroy temp, restore original
                if (_instanceMats[i] != null) Destroy(_instanceMats[i]);
                _instanceMats[i] = _preGlitchMats[i];
            }
            else if (_instanceMats[i] != null && _originalShaders != null
                     && i < _originalShaders.Length && _originalShaders[i] != null)
            {
                // PSX slot: restore original shader
                _instanceMats[i].shader = _originalShaders[i];
            }
        }

        if (_renderer != null) _renderer.materials = _instanceMats;
        var hl = GetComponent<ItemHighlight>();
        if (hl != null) hl.RefreshBaseMaterials();
        _preGlitchMats = null;
        _isGlitched = false;
#if UNITY_EDITOR
        Debug.Log($"[PlaceableObject] RemoveGlitch on '{name}' ({_instanceMats.Length} slots)");
#endif
    }

    /// <summary>Public toggle for the PSX glitch shader swap (used by PairableItem for unpaired-state hint).</summary>
    public void SetGlitched(bool active)
    {
        if (active) ApplyGlitch();
        else RemoveGlitch();
    }

    /// <summary>True if the glitch shader is currently applied.</summary>
    public bool IsGlitched => _isGlitched;

    /// <summary>True if ALL material slots use PSX shaders. Non-PSX materials break when swapped to glitch.</summary>
    public bool IsFullyPSX
    {
        get
        {
            if (_instanceMats == null || _instanceMats.Length == 0) return false;
            for (int i = 0; i < _instanceMats.Length; i++)
            {
                if (_instanceMats[i] == null) continue;
                if (!IsPSXShader(_instanceMats[i].shader)) return false;
            }
            return true;
        }
    }

    /// <summary>Force the instance material to a specific shader, bypassing glitch state tracking.</summary>
    public void ForceShader(Shader shader)
    {
        if (_instanceMats == null || shader == null) return;
        for (int i = 0; i < _instanceMats.Length; i++)
            if (_instanceMats[i] != null) _instanceMats[i].shader = shader;
        _isGlitched = false;
    }

    /// <summary>Capture current rotation as the disheveled pose (rotation only, position untouched).</summary>
    [ContextMenu("Capture Disheveled Rotation")]
    private void CaptureDisheveledPose()
    {
        _disheveledRotation = transform.rotation;
#if UNITY_EDITOR
        Debug.Log($"[PlaceableObject] {name} disheveled rotation captured: {transform.eulerAngles}");
#endif
    }

    /// <summary>Capture current rotation as the home/normal rotation (rotation only, position untouched).</summary>
    [ContextMenu("Capture Home Rotation")]
    private void CaptureHomeRotation()
    {
        _homeRotation = transform.rotation;
#if UNITY_EDITOR
        Debug.Log($"[PlaceableObject] {name} home rotation captured: {transform.eulerAngles}");
#endif
    }

    /// <summary>Capture current position + rotation as the full home pose.</summary>
    [ContextMenu("Capture Home Position + Rotation")]
    private void CaptureNormalPose()
    {
        _homePosition = transform.position;
        _homeRotation = transform.rotation;
#if UNITY_EDITOR
        Debug.Log($"[PlaceableObject] {name} home pose captured: pos={_homePosition}, rot={transform.eulerAngles}");
#endif
    }

    /// <summary>Clear the home position and rotation. Object will have no home.</summary>
    [ContextMenu("Clear Home Position")]
    private void ClearHomePose()
    {
        _homePosition = Vector3.zero;
        _homeRotation = Quaternion.identity;
        _useSpawnAsHome = false;
#if UNITY_EDITOR
        Debug.Log($"[PlaceableObject] {name} home position cleared.");
#endif
    }

    /// <summary>Clear the captured disheveled pose.</summary>
    [ContextMenu("Clear Disheveled Rotation")]
    private void ClearDisheveledPose()
    {
        _disheveledRotation = Quaternion.identity;
#if UNITY_EDITOR
        Debug.Log($"[PlaceableObject] {name} disheveled rotation cleared.");
#endif
    }

    /// <summary>
    /// True when the object is resting on the floor (not held, not on a surface/DropZone).
    /// Items on PlacementSurfaces or in DropZones are NOT considered clutter.
    /// </summary>
    public bool IsOnFloor => CurrentState != State.Held
                          && transform.position.y < 0.15f
                          && _lastPlacedSurface == null;

    private Renderer _renderer;
    private Material[] _instanceMats;   // cloned per-slot materials (all slots)
    private Color _originalColor;       // color of slot 0 (for flash reference)
    private Color[] _originalColors;    // per-slot original colors for restore

    // Silhouette overlay (see-through when occluded)
    private Material _silhouetteMat;
    private GameObject _silhouetteGO;

    private Vector3 _lastValidPosition;
    private Quaternion _lastValidRotation;
    private PlacementSurface _lastPlacedSurface;
    private Rigidbody _rb;
    private float _fallTimer;

    private Collider[] _colliders;
    private Coroutine _validationCoroutine;
    private Coroutine _flashCoroutine;
    private Coroutine _snapBounceCoroutine;

    // Settle immunity: prevents accidental re-grab immediately after placement.
    private float _settleTimer;
    /// <summary>True for ~0.05 s after OnPlaced so the grabber ignores this item.</summary>
    public bool IsSettling => _settleTimer > 0f;

    private void OnEnable() => s_all.Add(this);
    private void OnDisable() => s_all.Remove(this);

    // Cached shader lookups (avoid per-object Shader.Find)
    private static Shader s_silhouetteShader;
    private static bool s_silhouetteShaderCached;
    private static Shader s_glitchShader;
    private static bool s_glitchShaderCached;
    private Shader _originalShader;
    private Shader[] _originalShaders;
    private bool _isGlitched;

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        if (_renderer == null) _renderer = GetComponentInChildren<Renderer>();
        // Don't clone materials — leave renderer untouched.
        // All color changes use MaterialPropertyBlock (non-destructive).
        if (_renderer != null && _renderer.sharedMaterials.Length > 0)
        {
            var shared = _renderer.sharedMaterials;
            _instanceMats = shared;
            _originalColor = shared[0] != null
                ? (shared[0].HasProperty("_BaseColor") ? shared[0].GetColor("_BaseColor") : shared[0].color)
                : Color.white;
            _originalColors = new Color[shared.Length];
            for (int i = 0; i < shared.Length; i++)
                _originalColors[i] = shared[i] != null
                    ? (shared[i].HasProperty("_BaseColor") ? shared[i].GetColor("_BaseColor") : shared[i].color)
                    : Color.white;
            _originalShader = shared[0] != null ? shared[0].shader : null;
            _originalShaders = new Shader[shared.Length];
            for (int i = 0; i < shared.Length; i++)
                if (shared[i] != null) _originalShaders[i] = shared[i].shader;
        }

        OriginalScale = transform.localScale;
        _lastValidPosition = transform.position;
        _lastValidRotation = transform.rotation;
        _rb = GetComponent<Rigidbody>();
        _colliders = GetComponentsInChildren<Collider>();

        // ItemHighlight auto-add disabled — its material cache
        // conflicts with multi-material objects and glitch swaps.
        // TODO: refactor highlight system to use MaterialPropertyBlock only.
        // if (GetComponent<ItemHighlight>() == null && GetComponent<WaterablePlant>() == null)
        //     gameObject.AddComponent<ItemHighlight>();

        // Auto-capture spawn position/rotation as home whenever unset
        if (_homePosition == Vector3.zero)
        {
            _homePosition = transform.position;
            // Wall-mountable items: store a clean wall-aligned rotation as home,
            // not the baked crooked offset from the scene builder.
            if (canWallMount)
                _homeRotation = CleanWallRotation(transform);
            else
                _homeRotation = transform.rotation;
        }
    }

    private void Start()
    {
        // Snap scene-placed items to the same grid the player uses, so editor
        // placement (which is rarely on-grid) doesn't conflict with the runtime
        // occupancy check. Disheveled items snap *first*, then take their askew
        // rotation. Wall items keep their wall-aligned position.
        SnapToGridOnSpawn();

        if (_startDishelved)
            Dishevel();

        // Trash items always get the PSX glitch shader as a "needs attention"
        // hint until they're thrown away. Spawned messes already set this via
        // AuthoredMessSpawner; this covers scene-placed trash too.
        if (_itemCategory == ItemCategory.Trash)
            ApplyGlitch();

        // Subscribe to day-start for smell aging
        if (GameClock.Instance != null)
            GameClock.Instance.OnDayStarted.AddListener(OnNewDay);

        // Auto-set smell per day based on category if not manually set
        if (_smellPerDay <= 0f)
        {
            switch (_itemCategory)
            {
                case ItemCategory.Trash: _smellPerDay = 0.2f; break;
                case ItemCategory.Dish:  _smellPerDay = 0.1f; break;
                default: _smellPerDay = 0f; break;
            }
            // Milk cartons (HomeZoneName = "Fridge") smell fast
            if (_homeZoneName == "Fridge") _smellPerDay = 0.5f;
        }
    }

    /// <summary>
    /// Find the placement surface this item is sitting on (raycast down for
    /// horizontal items, use last placed surface for wall items) and snap the
    /// position to the global grid. Updates _homePosition to match.
    /// </summary>
    private void SnapToGridOnSpawn()
    {
        if (canWallMount) return; // wall items use their crooked spawn pose
        if (_itemCategory == ItemCategory.Trash) return; // trash spawns wherever it falls
        if (_itemCategory == ItemCategory.Record) return; // records live in dedicated record slots, not on the grid
        // Belt-and-suspenders: also bail if a RecordItem component is present,
        // in case a record isn't flagged with the Record category in the inspector.
        if (GetComponent<RecordItem>() != null) return;

        var surface = PlacementSurface.FindNearest(transform.position, skipVertical: true);
        if (surface == null) return;

        // Only snap if we're actually over this surface — otherwise we'd teleport
        // floor items onto the nearest table.
        Vector3 clamped = surface.ClampToSurface(transform.position);
        if ((clamped - transform.position).sqrMagnitude > 0.25f) return; // > 0.5m off

        var hit = surface.ProjectOntoSurface(transform.position);
        Vector3 snapped = surface.SnapToGrid(hit.worldPosition, ObjectGrabber.GlobalGridSize);

        // Preserve the item's resting offset above the surface (half-height along surface normal).
        float halfExtent = GetHalfExtentAlongNormal(hit.surfaceNormal);
        Vector3 finalPos = snapped + hit.surfaceNormal * halfExtent;

        transform.position = finalPos;
        _lastValidPosition = finalPos;
        if (_homePosition != Vector3.zero)
            _homePosition = finalPos;
        _lastPlacedSurface = surface;
    }

    /// <summary>Half-extent of the renderer bounds along a world-space normal.</summary>
    private float GetHalfExtentAlongNormal(Vector3 normal)
    {
        if (_renderer == null) return 0f;
        Vector3 ext = _renderer.bounds.extents;
        return Mathf.Abs(Vector3.Dot(ext, normal.normalized));
    }

    private void OnNewDay()
    {
        if (this == null) return; // destroyed

        // Only age items NOT at home
        if (IsAtHome) return;

        // Skip items that don't smell
        if (_smellPerDay <= 0f) return;

        _daysLeftOut++;
        // Surface multiplier amplifies displayed stink — prominent trash stinks 3x.
        float totalSmell = _smellPerDay * _daysLeftOut * CurrentEffectMultiplier;

        // Update ReactableTag smell
        var tag = GetComponent<ReactableTag>();
        if (tag != null)
            tag.SmellAmount = totalSmell;

        // Show/update stink lines when smell is noticeable
        if (totalSmell >= 0.3f)
            EnsureStinkLines(totalSmell);
    }

    /// <summary>Reset aging when item is put away (placed at home or deposited).</summary>
    public void ResetSmellAging()
    {
        _daysLeftOut = 0;
        var tag = GetComponent<ReactableTag>();
        if (tag != null) tag.SmellAmount = 0f;
        DestroyStinkLines();
    }

    // ── Stink Lines (wavy particles rising from smelly items) ──

    private GameObject _stinkLinesGO;

    private void EnsureStinkLines(float intensity)
    {
        if (_stinkLinesGO == null)
        {
            _stinkLinesGO = new GameObject("StinkLines");
            _stinkLinesGO.transform.SetParent(transform);
            _stinkLinesGO.transform.localPosition = Vector3.up * 0.1f;

            var ps = _stinkLinesGO.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 1.5f;
            main.startSpeed = 0.15f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            main.gravityModifier = -0.1f;
            main.maxParticles = 15;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new Color(0.6f, 0.7f, 0.3f, 0.4f); // sickly green

            var emission = ps.emission;
            emission.rateOverTime = 3f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.5f),
                new Keyframe(0.5f, 1f),
                new Keyframe(1f, 0.3f)
            ));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(0.6f, 0.7f, 0.3f), 0f), new GradientColorKey(new Color(0.5f, 0.6f, 0.2f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.5f, 0.3f), new GradientAlphaKey(0f, 1f) }
            );
            col.color = gradient;

            // Wavy motion
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);

            var renderer = _stinkLinesGO.GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Particles/Standard Unlit")
                      ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader != null)
            {
                renderer.material = new Material(shader);
                renderer.material.SetFloat("_Surface", 1f);
            }
        }

        // Scale intensity with smell
        var stinkPS = _stinkLinesGO.GetComponent<ParticleSystem>();
        if (stinkPS != null)
        {
            var em = stinkPS.emission;
            em.rateOverTime = Mathf.Lerp(2f, 10f, Mathf.Clamp01(intensity / 1.5f));

            var m = stinkPS.main;
            m.startSize = new ParticleSystem.MinMaxCurve(
                Mathf.Lerp(0.02f, 0.04f, intensity),
                Mathf.Lerp(0.04f, 0.08f, intensity));
        }
    }

    private void DestroyStinkLines()
    {
        if (_stinkLinesGO != null)
        {
            Destroy(_stinkLinesGO);
            _stinkLinesGO = null;
        }
    }

    /// <summary>
    /// Lazy-init silhouette material and mesh on first pickup (not Awake).
    /// Avoids 2 material allocations + Shader.Find + GO creation per object at scene load.
    /// </summary>
    public void EnsureSilhouette()
    {
        if (_silhouetteMat != null) return;

        if (!s_silhouetteShaderCached)
        {
            s_silhouetteShaderCached = true;
            s_silhouetteShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (s_silhouetteShader == null) s_silhouetteShader = Shader.Find("Sprites/Default");
        }

        if (s_silhouetteShader != null)
        {
            _silhouetteMat = new Material(s_silhouetteShader);
            _silhouetteMat.SetFloat("_Surface", 1f);
            _silhouetteMat.SetFloat("_Blend", 0f);
            _silhouetteMat.SetFloat("_ZWrite", 0f);
            _silhouetteMat.SetFloat("_ZTest", (float)CompareFunction.Always);
            _silhouetteMat.SetOverrideTag("RenderType", "Transparent");
            _silhouetteMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _silhouetteMat.renderQueue = 3100;
        }

        BuildSilhouette();
    }

    private void Update()
    {
        if (CurrentState == State.Held) return;

        // Tick down settle immunity (set by OnPlaced to block accidental re-grab)
        if (_settleTimer > 0f)
            _settleTimer -= Time.deltaTime;

        if (!s_worldBounds.Contains(transform.position))
        {
            _fallTimer += Time.deltaTime;
            if (_fallTimer >= respawnDelay)
            {
                RecoverToNearestSurface();
                _fallTimer = 0f;
            }
        }
        else
        {
            _fallTimer = 0f;
        }
    }

    private void RecoverToNearestSurface()
    {
        // If last valid position is ALSO out of bounds, try to find any surface.
        // Otherwise we'd loop forever respawning to an invalid spot.
        Vector3 target = _lastValidPosition;
        if (!s_worldBounds.Contains(target))
        {
            var nearest = FindNearestSurface(s_worldBounds.center);
            if (nearest != null)
            {
                var hit = nearest.ProjectOntoSurface(s_worldBounds.center);
                target = hit.worldPosition + hit.surfaceNormal * 0.1f;
            }
            else
            {
                // No surface found — park at world center and go kinematic to prevent falling
                target = s_worldBounds.center;
            }
            _lastValidPosition = target;
            Debug.LogWarning($"[PlaceableObject] {name} had invalid recovery pos — placed at {target}.");
        }

        transform.position = target;
        transform.rotation = _lastValidRotation;
        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;

            if (_lastPlacedSurface != null && _lastPlacedSurface.IsVertical)
            {
                _rb.isKinematic = true;
                _rb.useGravity = false;
            }
            else
            {
                _rb.isKinematic = false;
                _rb.useGravity = true;
            }
        }
        CurrentState = State.Resting;
        StartFlash();
        Debug.Log($"[PlaceableObject] {name} out of bounds — respawned at {target}.");
    }

    private void OnDestroy()
    {
        // Don't destroy _instanceMats — they're shared assets, not clones.
        if (_silhouetteMat != null) Destroy(_silhouetteMat);
        if (_silhouetteGO != null) Destroy(_silhouetteGO);
        DestroyStinkLines();
        if (GameClock.Instance != null)
            GameClock.Instance.OnDayStarted.RemoveListener(OnNewDay);
    }

    // ── Grabbed / Released ────────────────────────────────────────────

    /// <summary>
    /// Called by ObjectGrabber when this object is picked up.
    /// </summary>
    public void OnPickedUp()
    {
        CurrentState = State.Held;
        _lastValidPosition = transform.position;
        _lastValidRotation = transform.rotation;
        IsAtHome = false;

        // Silhouette overlay disabled — SetRenderOnTop (called from the actual
        // pickup path) swaps the held mesh's shader to Iris/HeldOnTop, which
        // already renders the real item on top of everything. The old
        // see-through silhouette duplicate was visual noise on top of that.

        // If we were stored in a drawer, notify the drawer
        var drawer = GetComponentInParent<DrawerController>();
        if (drawer != null)
            drawer.RemoveStoredItem(this);

        // Disable colliders FIRST so switching to dynamic Rigidbody doesn't
        // cause a physics impulse from overlapping the surface we were sitting on.
        SetCollidersEnabled(false);

        if (_rb != null)
            _rb.isKinematic = false;

        // Always remove glitch on pickup — the held item should show its
        // real materials. Glitch is re-applied on drop if still needed
        // (e.g. unpaired PairableItem).
        if (_isGlitched)
            RemoveGlitch();

        if (_allowAutoStraighten && !canWallMount)
        {
            // Skip auto-straighten for pairable items (shoes) — player controls rotation
            var pairable = GetComponent<PairableItem>();
            bool skipStraighten = pairable != null;

            if (!skipStraighten)
            {
                // Auto-straighten to home rotation on pickup (if home was captured)
                // Skip wall-mountable items — their rotation is intentional
                if (_homeRotation != Quaternion.identity)
                {
                    transform.rotation = _homeRotation;
                    if (_rb != null) _rb.angularVelocity = Vector3.zero;
                }
                else
                {
                    // No home rotation — just zero X/Z, keep Y
                    Vector3 euler = transform.eulerAngles;
                    float xTilt = Mathf.Abs(Mathf.DeltaAngle(euler.x, 0f));
                    float zTilt = Mathf.Abs(Mathf.DeltaAngle(euler.z, 0f));
                    if (xTilt > 1f || zTilt > 1f)
                    {
                        transform.rotation = Quaternion.Euler(0f, euler.y, 0f);
                        if (_rb != null) _rb.angularVelocity = Vector3.zero;
                    }
                }
            }
        }

        // Render held object on top of all scene geometry so it's always visible
        SetRenderOnTop(true);

        // Held-item feedback handled by grab cursor — no material color change

        if (_validationCoroutine != null)
        {
            StopCoroutine(_validationCoroutine);
            _validationCoroutine = null;
        }

        Debug.Log($"[PlaceableObject] {name} picked up.");
    }

    /// <summary>
    /// Called by ObjectGrabber when this object is placed on a surface.
    /// </summary>
    public void OnPlaced(PlacementSurface surface, bool gridSnapped, Vector3 position, Quaternion rotation)
    {
        CurrentState = State.Placed;
        _lastValidPosition = position;
        _lastValidRotation = rotation;
        _lastPlacedSurface = surface;

        // Check if placed on a matching DropZone (home or alt zone)
        if (surface != null)
        {
            var zone = surface.GetComponent<DropZone>();
            if (zone != null)
            {
                if ((!string.IsNullOrEmpty(_homeZoneName) && zone.ZoneName == _homeZoneName)
                    || (!string.IsNullOrEmpty(_altHomeZoneName) && zone.ZoneName == _altHomeZoneName))
                {
                    IsAtHome = true;
                    ResetSmellAging();
                    // Item is home — clear glitch indicator
                    // But keep glitch on unpaired pairables (glitch = "find my partner")
                    var pairable = GetComponent<PairableItem>();
                    bool needsPairing = pairable != null
                        && pairable.Mode == PairableItem.PairMode.SpecificPartner
                        && !pairable.IsPaired;
                    if (_isGlitched && !needsPairing) RemoveGlitch();
                }
            }
        }

        transform.position = position;
        transform.rotation = rotation;

        SetCollidersEnabled(true);
        SetRenderOnTop(false);
        RestoreMaterial();
        DestroySilhouette();

        // Force-clear any MaterialPropertyBlock color overrides left by pulse systems
        if (_renderer != null)
            _renderer.SetPropertyBlock(null);

        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;

            if (surface != null && (surface.IsVertical || surface.IsFloor))
            {
                // Wall and floor items stay put — no physics needed
                _rb.isKinematic = true;
                _rb.useGravity = false;
            }
            else
            {
                _rb.isKinematic = false;
                _rb.useGravity = true;
                // Freeze X/Z rotation to prevent toppling; Y rotation preserved for scroll-rotate
                _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            }
        }

        if (surface != null && !surface.IsVertical)
        {
            if (_validationCoroutine != null) StopCoroutine(_validationCoroutine);
            _validationCoroutine = StartCoroutine(PlacementValidation());
        }

        // Wall overlap: if two wall-mounted items overlap, apply crooked offset to both
        if (canWallMount && surface != null && surface.IsVertical)
            CheckWallOverlap();

        Debug.Log($"[PlaceableObject] {name} placed at {position} (grid={gridSnapped}, wall={surface != null && surface.IsVertical}).");

        // Block accidental re-grab for ~3 frames after placement
        _settleTimer = 0.05f;

        // Satisfying scale bounce on placement
        if (_snapBounceCoroutine != null) StopCoroutine(_snapBounceCoroutine);
        _snapBounceCoroutine = StartCoroutine(PlacementSnapBounce());
    }

    /// <summary>
    /// Called by ObjectGrabber if placement is cancelled (e.g. area exit without a surface).
    /// </summary>
    public void OnDropped()
    {
        CurrentState = State.Resting;
        SetCollidersEnabled(true);
        SetRenderOnTop(false);
        RestoreMaterial();
        DestroySilhouette();

        if (_renderer != null)
            _renderer.SetPropertyBlock(null);

        // Check if we ended up somewhere the player can't see
        StartCoroutine(CheckVisibilityAfterDrop());

        Debug.Log($"[PlaceableObject] {name} dropped.");
    }

    // ── Collider toggle ──────────────────────────────────────────────

    private void SetCollidersEnabled(bool enabled)
    {
        if (_colliders == null) return;
        foreach (var col in _colliders)
        {
            if (col != null)
                col.enabled = enabled;
        }
    }

    // ── Wall alignment ────────────────────────────────────────────────

    public void AlignToWall(Vector3 wallNormal, float rotationAngle)
    {
        // Face the wall normal, then apply user yaw. Works cleanly when the
        // root has (0,0,0) rotation and the mesh visual is on a child.
        transform.rotation = Quaternion.LookRotation(wallNormal, Vector3.up)
            * Quaternion.AngleAxis(rotationAngle, Vector3.forward);
    }

    public void ApplyCrookedOffset(Vector3 wallNormal)
    {
        float angle = Random.Range(-crookedAngleRange, crookedAngleRange);
        transform.rotation *= Quaternion.AngleAxis(angle, -wallNormal);
    }

    /// <summary>
    /// Strip the crooked offset from a wall-mounted item's rotation,
    /// returning a clean LookRotation along the wall normal with upright orientation.
    /// </summary>
    private static Quaternion CleanWallRotation(Transform t)
    {
        Vector3 forward = t.forward;
        return Quaternion.LookRotation(forward, Vector3.up);
    }

    /// <summary>
    /// Check if this wall item overlaps another wall item. If so, apply a crooked
    /// offset to both — paintings placed on top of each other go askew.
    /// </summary>
    private void CheckWallOverlap()
    {
        var col = GetComponent<Collider>();
        if (col == null) return;

        Bounds myBounds = col.bounds;
        const float overlapThreshold = 0.1f; // minimum overlap to trigger

        for (int i = 0; i < s_all.Count; i++)
        {
            var other = s_all[i];
            if (other == this || other == null) continue;
            if (!other.canWallMount) continue;
            if (other.CurrentState == State.Held) continue;

            var otherCol = other.GetComponent<Collider>()
                        ?? other.GetComponentInChildren<Collider>();
            if (otherCol == null) continue;

            Bounds otherBounds = otherCol.bounds;
            if (!myBounds.Intersects(otherBounds)) continue;

            // Verify meaningful overlap (not just touching edges)
            Vector3 overlap = Vector3.Min(myBounds.max, otherBounds.max) - Vector3.Max(myBounds.min, otherBounds.min);
            if (overlap.x < overlapThreshold || overlap.y < overlapThreshold) continue;

            // Both go crooked
            ApplyCrookedOffset(transform.forward);
            other.ApplyCrookedOffset(other.transform.forward);

            Debug.Log($"[PlaceableObject] Wall overlap: {name} + {other.name} — both go crooked.");
            return; // one overlap is enough
        }
    }

    // ── Silhouette overlay (see-through furniture) ────────────────────

    private void BuildSilhouette()
    {
        if (_silhouetteMat == null) return;
        if (_silhouetteGO != null) return; // Already built

        var mf = GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;

        // Tint: washed-out version of object color at 35% opacity
        _silhouetteMat.color = new Color(
            _originalColor.r * 0.5f + 0.5f,
            _originalColor.g * 0.5f + 0.5f,
            _originalColor.b * 0.5f + 0.5f,
            0.35f);

        _silhouetteGO = new GameObject("Silhouette");
        _silhouetteGO.transform.SetParent(transform, false);

        var silMF = _silhouetteGO.AddComponent<MeshFilter>();
        silMF.sharedMesh = mf.sharedMesh;

        var silRend = _silhouetteGO.AddComponent<MeshRenderer>();
        silRend.sharedMaterial = _silhouetteMat;
        silRend.shadowCastingMode = ShadowCastingMode.Off;
        silRend.receiveShadows = false;
    }

    /// <summary>Remove the silhouette overlay on this item and all children. Called on place, drop, and pair.</summary>
    public void ForceDestroySilhouette()
    {
        DestroySilhouette();

        // Also destroy silhouettes on paired/stacked children
        foreach (Transform child in transform)
        {
            var childPlaceable = child.GetComponent<PlaceableObject>();
            if (childPlaceable != null)
                childPlaceable.ForceDestroySilhouette();
        }
    }

    private void DestroySilhouette()
    {
        if (_silhouetteGO != null)
        {
            Destroy(_silhouetteGO);
            _silhouetteGO = null;
        }
        if (_silhouetteMat != null)
        {
            Destroy(_silhouetteMat);
            _silhouetteMat = null;
        }
    }

    // ── Safety: drop visibility check ─────────────────────────────────

    private IEnumerator CheckVisibilityAfterDrop()
    {
        yield return new WaitForSeconds(2f);

        if (CurrentState != State.Resting) yield break;

        var cam = Camera.main;
        if (cam == null) yield break;

        Vector3 vp = cam.WorldToViewportPoint(transform.position);
        bool inView = vp.z > 0f && vp.x > -0.1f && vp.x < 1.1f && vp.y > -0.1f && vp.y < 1.1f;

        if (!inView)
        {
            // Place on nearest surface instead of raw respawn
            var nearest = FindNearestSurface(transform.position);
            if (nearest != null)
            {
                var hit = nearest.ProjectOntoSurface(transform.position);
                float halfY = 0f;
                var col = GetComponent<Collider>();
                if (col != null)
                    halfY = Mathf.Abs(Vector3.Dot(col.bounds.extents, hit.surfaceNormal.normalized));

                Vector3 pos = hit.worldPosition + hit.surfaceNormal * halfY;
                Quaternion rot = nearest.IsVertical
                    ? Quaternion.LookRotation(hit.surfaceNormal, Vector3.up)
                    : transform.rotation;

                Debug.Log($"[PlaceableObject] {name} out of view after drop — placed on {nearest.name}.");
                OnPlaced(nearest, false, pos, rot);
            }
            else
            {
                Debug.Log($"[PlaceableObject] {name} out of view, no surface found — recovering.");
                RecoverToNearestSurface();
            }
        }
    }

    private PlacementSurface FindNearestSurface(Vector3 worldPos)
    {
        return PlacementSurface.FindNearest(worldPos,
            skipVertical: !canWallMount,
            skipHorizontal: wallOnly);
    }

    // ── Safety: Placement validation timer ────────────────────────────

    private IEnumerator PlacementValidation()
    {
        yield return new WaitForSeconds(1.5f);

        if (CurrentState != State.Placed || _lastPlacedSurface == null)
            yield break;

        bool onSurface = _lastPlacedSurface.ContainsWorldPoint(transform.position);
        bool nearValid = Vector3.Distance(transform.position, _lastValidPosition) < 0.5f;

        if (!onSurface && !nearValid)
        {
            Debug.Log($"[PlaceableObject] {name} drifted from surface — snapping back.");
            transform.position = _lastValidPosition;
            transform.rotation = _lastValidRotation;
            if (_rb != null)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            StartFlash();
        }

        _validationCoroutine = null;
    }

    // ── Visual feedback flash ─────────────────────────────────────────

    private void StartFlash()
    {
        if (_instanceMats == null || _instanceMats.Length == 0) return;
        if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
        _flashCoroutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        Color bright = _originalColor * 2f;
        for (int i = 0; i < 2; i++)
        {
            SetInstanceColor(bright);
            yield return new WaitForSeconds(0.1f);
            SetInstanceColor(_originalColor);
            yield return new WaitForSeconds(0.1f);
        }
        _flashCoroutine = null;
    }

    // ── Placement snap bounce ─────────────────────────────────────────

    private IEnumerator PlacementSnapBounce()
    {
        if (AccessibilitySettings.ReduceMotion)
        {
            _snapBounceCoroutine = null;
            yield break;
        }

        Vector3 baseScale = transform.localScale;
        Vector3 basePos = transform.position;

        // Squish down (0.04s)
        yield return ScaleLerp(baseScale, baseScale * 0.92f, basePos, basePos + Vector3.down * 0.01f, 0.04f);
        // Bounce up (0.06s)
        yield return ScaleLerp(baseScale * 0.92f, baseScale * 1.04f, basePos + Vector3.down * 0.01f, basePos, 0.06f);
        // Settle (0.04s)
        yield return ScaleLerp(baseScale * 1.04f, baseScale, basePos, basePos, 0.04f);

        transform.localScale = baseScale;
        transform.position = basePos;
        _snapBounceCoroutine = null;
    }

    private IEnumerator ScaleLerp(Vector3 fromScale, Vector3 toScale, Vector3 fromPos, Vector3 toPos, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            transform.localScale = Vector3.Lerp(fromScale, toScale, t);
            transform.position = Vector3.Lerp(fromPos, toPos, t);
            yield return null;
        }
    }

    // ── Render on top (held items always visible) ─────────────────────

    // Per-renderer state so we can restore each material on drop.
    private struct RenderOnTopState
    {
        public Renderer renderer;
        public int matIndex;
        public Material originalMat;   // the real material (never modified for non-PSX)
        public Material tempMat;       // disposable AlwaysOnTop copy (null for PSX)
        public int savedQueue;
        public float savedZTest;
    }
    private readonly List<RenderOnTopState> _renderOnTopStates = new();

    private static Shader s_heldOnTopShader;
    private static bool s_heldOnTopShaderCached;

    private static int s_heldItemLayer = -1;
    private int _savedLayer;

    public void SetRenderOnTop(bool onTop)
    {
        // Layer-based approach: move to HeldItem layer (rendered with ZTest Always
        // via URP Render Objects feature). Works for ANY shader.
        if (s_heldItemLayer < 0)
        {
            s_heldItemLayer = LayerMask.NameToLayer("HeldItem");
            if (s_heldItemLayer < 0) s_heldItemLayer = 18; // fallback (was StickerPad)
        }

        if (onTop)
        {
            _savedLayer = gameObject.layer;
            SetLayerRecursive(gameObject, s_heldItemLayer);
        }
        else
        {
            // Always restore to Placeable — children added mid-hold (paired
            // books, stacked plates) may have _savedLayer as HeldItem or
            // Default(0), neither of which is correct after placement.
            int placeableLayer = LayerMask.NameToLayer("Placeable");
            int restoreLayer = (_savedLayer != s_heldItemLayer && _savedLayer != 0)
                ? _savedLayer : placeableLayer;
            SetLayerRecursive(gameObject, restoreLayer);
        }

        // Cascade to paired/stacked child PlaceableObjects (each owns its own state)
        foreach (Transform child in transform)
        {
            var childPlaceable = child.GetComponent<PlaceableObject>();
            if (childPlaceable != null)
                childPlaceable.SetRenderOnTop(onTop);
        }
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        for (int i = 0; i < go.transform.childCount; i++)
            SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
    }

    // ── Material restore ──────────────────────────────────────────────

    private void RestoreMaterial()
    {
        // Color tinting now uses MaterialPropertyBlock (never modifies
        // instance materials), so there's nothing to restore here.
    }

    /// <summary>Public version for external systems (PairableItem) to force color restore.</summary>
    public void ForceRestoreMaterial()
    {
        RestoreMaterial();
    }

    /// <summary>Set all instance material colors via MaterialPropertyBlock (safe for PSX). Returns false if no renderer.</summary>
    public bool SetInstanceColor(Color color)
    {
        if (_renderer == null) return false;
        // Fresh MPB — never GetPropertyBlock first.
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", color);
        _renderer.SetPropertyBlock(mpb);
        return true;
    }

    /// <summary>The original base color captured in Awake.</summary>
    public Color OriginalColor => _originalColor;

    /// <summary>
    /// Override the instance material's shader and properties from a source material.
    private System.Collections.IEnumerator DebugMatsNextFrame()
    {
        yield return null;
        if (_renderer == null) yield break;
        var mats = _renderer.sharedMaterials;
        for (int d = 0; d < mats.Length; d++)
            Debug.Log($"[PO-DROP+1] {name} slot {d}: {(mats[d] != null ? mats[d].name + " shader=" + mats[d].shader.name : "NULL")}");
    }

    /// Call AFTER Awake (which creates _instanceMats) and BEFORE ItemHighlight
    /// caches base materials. Preserves the material reference so pickup/place still works.
    /// </summary>
    public void ApplyMaterialOverride(Material source, Color color)
    {
        if (source == null) return;

        if (_instanceMats == null || _instanceMats.Length == 0)
        {
            // Awake hasn't run yet — set on the renderer directly
            _renderer = GetComponent<Renderer>();
            if (_renderer == null) return;
            var mat = new Material(source);
            mat.color = color;
            _instanceMats = new[] { mat };
            _renderer.material = mat;
        }
        else
        {
            // Override slot 0 with source properties
            _instanceMats[0].shader = source.shader;
            _instanceMats[0].CopyPropertiesFromMaterial(source);
            _instanceMats[0].color = color;
        }
        _originalColor = color;
    }
}
