using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

public class ObjectGrabber : MonoBehaviour
{
    [Header("Physics")]
#pragma warning disable 0414
    [Tooltip("Spring strength pulling the object toward the cursor.")]
    [SerializeField] private float grabSpring = 120f;

    [Tooltip("Damper opposing overshoot.")]
    [SerializeField] private float grabDamper = 18f;

    [Tooltip("Maximum acceleration applied to the grabbed object.")]
    [SerializeField] private float maxAccel = 60f;

    [Tooltip("Maximum speed cap to prevent tunneling.")]
    [SerializeField] private float maxSpeed = 12f;
#pragma warning restore 0414

    // ── Grab feel presets (switchable at runtime via F3 debug panel) ──
    public enum GrabFeel
    {
        Default,   // current — snappy, direct
        Plucky,    // resistant tug, bouncy release — leaf-pulling feel
        Floaty,    // slow, dreamy, drifty — underwater feel
        Snappy,    // instant, no lag — arcade feel
        Heavy,     // sluggish, weighty — moving furniture feel
    }

    private static GrabFeel s_currentFeel = GrabFeel.Default;
    public static GrabFeel CurrentFeel
    {
        get => s_currentFeel;
        set
        {
            s_currentFeel = value;
            Debug.Log($"[ObjectGrabber] Grab feel: {value}");
        }
    }

    public static void CycleGrabFeel()
    {
        var values = System.Enum.GetValues(typeof(GrabFeel));
        int next = ((int)s_currentFeel + 1) % values.Length;
        CurrentFeel = (GrabFeel)next;
    }

    // Preset tuning values: spring, damper, maxAccel, maxSpeed
    private static readonly (float spring, float damper, float accel, float speed)[] s_feelPresets =
    {
        (120f, 18f, 60f,  12f),  // Default — snappy, direct
        (35f,  6f,  20f,  5f),   // Plucky — low spring, low damper = stretchy tug + overshoot bounce
        (25f,  12f, 15f,  4f),   // Floaty — low spring, high damper = slow drift, no bounce
        (500f, 40f, 200f, 25f),  // Snappy — very stiff, instant follow
        (50f,  25f, 25f,  3f),   // Heavy — medium spring, high damper, low speed cap
    };

    // Runtime overrides (driven by debug sliders, -1 = use preset)
    private static float s_overrideSpring = -1f;
    private static float s_overrideDamper = -1f;
    private static float s_overrideAccel = -1f;
    private static float s_overrideSpeed = -1f;

    public static void SetGrabOverrides(float spring, float damper, float accel, float speed)
    {
        s_overrideSpring = spring;
        s_overrideDamper = damper;
        s_overrideAccel = accel;
        s_overrideSpeed = speed;
    }

    public static void GetCurrentGrabParams(out float spring, out float damper, out float accel, out float speed)
    {
        if (s_overrideSpring > 0f)
        {
            spring = s_overrideSpring; damper = s_overrideDamper;
            accel = s_overrideAccel; speed = s_overrideSpeed;
            return;
        }
        var p = s_feelPresets[(int)s_currentFeel];
        spring = p.spring; damper = p.damper; accel = p.accel; speed = p.speed;
    }

    private void GetActiveGrabParams(out float spring, out float damper, out float accel, out float speed)
    {
        if (s_overrideSpring > 0f)
        {
            spring = s_overrideSpring;
            damper = s_overrideDamper;
            accel = s_overrideAccel;
            speed = s_overrideSpeed;
            return;
        }
        var p = s_feelPresets[(int)s_currentFeel];
        spring = p.spring;
        damper = p.damper;
        accel = p.accel;
        speed = p.speed;
    }

    [Header("Grid Snap")]
    [Tooltip("Grid cell size in world units. Placement is always grid-snapped — no toggle.")]
    [SerializeField] private float gridSize = 0.06f;

    [Tooltip("Multiplier for wall grid size. Walls feel better with a coarser grid (2-3x). 1 = same as tables.")]
    [SerializeField] private float _wallGridMultiplier = 1f;

    /// <summary>Grid size adjusted for wall surfaces.</summary>
    private float GetGridSize(PlacementSurface surface)
    {
        return surface != null && surface.IsVertical ? gridSize * _wallGridMultiplier : gridSize;
    }

    public float GridSize
    {
        get => gridSize;
        set => gridSize = Mathf.Max(0.01f, value);
    }

    /// <summary>Global grid cell size, set in Awake. PlaceableObject reads this on Start to snap scene-placed items.</summary>
    public static float GlobalGridSize { get; private set; } = 0.03f;

    [Header("Scroll Behavior")]
    [Tooltip("Degrees rotated per scroll tick around Y axis.")]
    [SerializeField] private float scrollRotateStep = 45f;

    // Pre-allocated raycast buffers (avoid per-frame allocation from RaycastAll)
    private static readonly RaycastHit[] s_surfaceHitBuffer = new RaycastHit[16];
    private static readonly RaycastHit[] s_deliveryHitBuffer = new RaycastHit[8];
    private static readonly RaycastHit[] s_pairHitBuffer = new RaycastHit[8];
    private static readonly HitDistComparer s_surfaceHitComparer = new();
    private class HitDistComparer : System.Collections.Generic.IComparer<RaycastHit>
    {
        public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
    }

    [Header("Raycast")]
    [Tooltip("Layer mask for placeable objects.")]
    [SerializeField] private LayerMask placeableLayer = ~0;

    [Tooltip("Layer mask for surface triggers (auto-generated by PlacementSurface).")]
    [SerializeField] private LayerMask surfaceLayer;

    [Tooltip("Camera used for raycasting. Auto-finds MainCamera if null.")]
    [SerializeField] private Camera cam;

    [Tooltip("Layer mask for wall/ceiling/floor geometry that held items cannot pass through.")]
    [SerializeField] private LayerMask _wallLayer = 1; // Default layer

    [Tooltip("Additional layer mask for barriers (closed cubbies, custom blockers). Merged with _wallLayer for clamping.")]
    [SerializeField] private LayerMask _barrierLayer;

    [Header("Audio")]
    [Tooltip("SFX played when picking up an object.")]
    [SerializeField] private AudioClip _pickupSFX;

    [Tooltip("SFX played when placing an object on a surface.")]
    [SerializeField] private AudioClip _placeSFX;

    [Header("Surface Reaction Sounds")]
    [Tooltip("Reaction SFX per surface material. Played alongside the main place SFX.")]
    [SerializeField] private AudioClip _surfaceWood;
    [SerializeField] private AudioClip _surfaceGlass;
    [SerializeField] private AudioClip _surfaceMetal;
    [SerializeField] private AudioClip _surfaceStone;
    [SerializeField] private AudioClip _surfaceFabric;
    [SerializeField] private AudioClip _surfacePlastic;

    [Header("Tether")]
    [Tooltip("Max distance between grab target and held object before teleporting to catch up.")]
    [SerializeField] private float maxTetherDistance = 3f;

    // Input managed by IrisInput singleton

    private bool _isEnabled;

    /// <summary>Fired after an object is placed on a surface. Arg: the placed object.</summary>
    public static event System.Action<PlaceableObject> OnObjectPlaced;

    private static ObjectGrabber s_instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // Clear static state that would otherwise persist across editor play sessions
        // (Unity does not reset statics on scene reload or domain reload in some configs)
        s_instance = null;
        s_lastConsumedFrame = -1;
        ClickConsumedThisFrame = false;
        OnObjectPlaced = null; // clear subscribers from previous sessions
    }

    /// <summary>The active ObjectGrabber in the scene, or null.</summary>
    public static ObjectGrabber Instance => s_instance;

    /// <summary>True when any ObjectGrabber is holding an object. Check this to block other interactions.</summary>
    public static bool IsHoldingObject => s_instance != null && s_instance._held != null;

    /// <summary>The currently held PlaceableObject, or null if nothing is held.</summary>
    public static PlaceableObject HeldObject => s_instance != null ? s_instance._held : null;

    /// <summary>The surface the held object is currently hovering over, or null.</summary>
    public static PlacementSurface CurrentSurface => s_instance != null ? s_instance._currentSurface : null;

    /// <summary>
    /// True when ObjectGrabber consumed a click this frame (picked up, placed, toggled drawer/switch).
    /// Other click-driven systems should check this to avoid processing the same click.
    /// </summary>
    public static bool ClickConsumedThisFrame { get; private set; }
    private static int s_lastConsumedFrame = -1;

    /// <summary>Force-release the held item without placing. Used by FridgeController deposit.</summary>
    public static void ForceReleaseHeld()
    {
        if (s_instance != null && s_instance._held != null)
            s_instance.ClearHeld();
    }

    private PlaceableObject _held;
    private Rigidbody _heldRb;
    private Vector3 _grabTarget;
    private float _fallbackDepth;
    private RigidbodyConstraints _originalConstraints;
    private float _heldShadowDiameter;
    private Vector3 _heldBoundsExtents;
    private Vector3 _heldBoundsCenterOffset; // collider center relative to pivot

    // Pickup feel: plucky spring on pickup, then ramp to stiff carry
    private float _pickupTimer;
    private Coroutine _squishCoroutine;
    private const float PickupFeelDuration = 0.25f; // seconds of plucky feel after pickup

    // Surface tracking
    private PlacementSurface _currentSurface;
    private PlacementSurface _lastValidSurface;
    private float _wallRotation;
    private bool _isOnWall;
    private Vector3 _heldOriginalForward; // which way the item faced when picked up
    private bool _isPourTilted;

    // Double-click detection for plate unstacking
    private float _lastClickTime;
    private const float DoubleClickThreshold = 0.3f;

    // Shadow preview
    private GameObject _shadowGO;
    private MeshRenderer _shadowRenderer;
    private Material _shadowMat;

    // Ghost preview (translucent copy of the held item at the snapped grid cell)
    private GameObject _ghostPreviewGO;
    private readonly List<Material> _ghostPreviewMats = new();

    private void Awake()
    {
        s_instance = this;
        GlobalGridSize = gridSize;

        if (cam == null)
            cam = Camera.main;

        // Load default click SFX from Resources if none assigned in Inspector
        if (_pickupSFX == null)
            _pickupSFX = Resources.Load<AudioClip>("Audio/default_click");
        if (_placeSFX == null)
            _placeSFX = Resources.Load<AudioClip>("Audio/default_click");

        BuildShadow();
    }

    // Input managed by IrisInput singleton — no local enable/disable needed.

    /// <summary>
    /// Build a ray from screen position. Uses ApartmentManager's browse camera
    /// directly to avoid Cinemachine's stale projection matrix on Camera.main.
    /// Falls back to Camera.main when ApartmentManager isn't available.
    /// </summary>
    private Ray ScreenRay(Vector2 screenPos)
    {
        var am = ApartmentManager.Instance;
        if (am != null)
            return am.ScreenPointToRay(screenPos);
        return cam != null ? cam.ScreenPointToRay(screenPos) : default;
    }

    private void OnDestroy()
    {
        if (s_instance == this) s_instance = null;
        if (_shadowMat != null) Destroy(_shadowMat);
        DestroyGhostPreview();
    }

    private void Update()
    {
        // RAW DEBUG — fires before any gate, uses old Input API as fallback
        if (Input.GetMouseButtonDown(0))
        {
            var c = Camera.main;
            string hitMain = "MISS";
            Ray rMain = default;
            if (c != null)
            {
                rMain = c.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(rMain, out RaycastHit h, 100f))
                    hitMain = $"{h.collider.gameObject.name} (layer={h.collider.gameObject.layer})";
            }

            // Fresh camera test — copy browseCamera transform, no Cinemachine contamination
            string hitFresh = "NO_AM";
            Ray rFresh = default;
            var am = ApartmentManager.Instance;
            if (am != null)
            {
                rFresh = am.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(rFresh, out RaycastHit h2, 100f))
                    hitFresh = $"{h2.collider.gameObject.name} (layer={h2.collider.gameObject.layer})";
                else
                    hitFresh = "MISS";
            }

            bool presetActive = false;
            if (am != null)
            {
                var pf = am.GetType().GetField("_presetOverrideActive", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (pf != null) presetActive = (bool)pf.GetValue(am);
            }
            Debug.LogWarning($"[OG-RAW] CMain={hitMain} ray={rMain.origin:F1}→{rMain.direction:F2} | Fresh={hitFresh} ray={rFresh.origin:F1}→{rFresh.direction:F2} | preset={presetActive} phase={DayPhaseManager.Instance?.CurrentPhase}");
        }

        // Only block input during actual fades, not during gameplay waits
        if (DayPhaseManager.Instance != null && DayPhaseManager.Instance.IsTransitioning
            && ScreenFade.Instance != null && ScreenFade.Instance.IsFading)
            return;

        // Always use the current main camera (Cinemachine brain may switch)
        var mainCam = Camera.main;
        if (mainCam != null && mainCam != cam)
            cam = mainCam;

        // Reset click consumption flag at start of each frame
        if (Time.frameCount != s_lastConsumedFrame)
            ClickConsumedThisFrame = false;

        if (IrisInput.Instance != null && IrisInput.Instance.Click.WasPressedThisFrame())
        {
            // Gunpla attach: check before UI blocker so clicking the body
            // while holding a part always works, even if UI overlaps.
            // RaycastAll so the floor/table surface doesn't swallow the hit.
            if (_held != null)
            {
                var heldPart = _held.GetComponent<GunplaPart>();
                if (heldPart != null && !heldPart.IsAttached)
                {
                    Ray ray = ScreenRay(IrisInput.CursorPosition);
                    var hits = Physics.RaycastAll(ray, 50f);
                    for (int i = 0; i < hits.Length; i++)
                    {
                        var clickedBody = hits[i].collider.GetComponent<GunplaFigure>()
                            ?? hits[i].collider.GetComponentInParent<GunplaFigure>();
                        if (clickedBody != null && clickedBody.CanAccept(heldPart))
                        {
                            clickedBody.AttachPart(heldPart);
                            ClearHeld();
                            ConsumeClick();
                            return;
                        }
                    }
                }
            }

            // Block game-world clicks when pointer is over UI
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                // Find which UI element is blocking
                var ped = new UnityEngine.EventSystems.PointerEventData(EventSystem.current);
                ped.position = IrisInput.CursorPosition;
                var results = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
                EventSystem.current.RaycastAll(ped, results);
                string blocker = "unknown";
                for (int i = 0; i < results.Count; i++)
                {
                    blocker = $"{results[i].gameObject.name} (parent={results[i].gameObject.transform.parent?.name ?? "root"})";
                    break;
                }
                Debug.LogWarning($"[ObjectGrabber] Click blocked by UI: {blocker}");
                return;
            }

            if (_held == null)
            {
                TryPickUp();
            }
            else
            {
                // Double-click while holding a plate stack → unstack top plate
                float now = Time.unscaledTime;
                bool isDoubleClick = (now - _lastClickTime) < DoubleClickThreshold;
                _lastClickTime = now;

                if (isDoubleClick && TryUnstackHeldPlate())
                {
                    ConsumeClick();
                    return;
                }

                // If holding a pairable item, check if clicking its match → snap pair.
                // This is the ONLY path for placing one item on top of another —
                // non-pairable items can't be stacked.
                if (TryPairWithClicked())
                {
                    ConsumeClick();
                    return;
                }

                Place();
                ConsumeClick();
            }
        }

        if (_held != null)
        {
            UpdateGrabTarget();
            ClampGrabTargetToWalls();
            UpdateScrollInput();
            UpdateShadow();
        }
        else
        {
            UpdateAlbumHover();
        }
    }

    // ── Album sleeve hover detection ────────────────────────────────

    private AlbumSleeve _lastHoveredSleeve;

    private void UpdateAlbumHover()
    {
        AlbumSleeve hovered = null;

        Ray ray = ScreenRay(IrisInput.CursorPosition);
        // Check both placeable layer and RecordSleeves layer (30) so all
        // sleeves are hoverable regardless of which layer they're on.
        int sleeveMask = placeableLayer | (1 << 30);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, sleeveMask))
        {
            hovered = hit.collider.GetComponent<AlbumSleeve>();
            if (hovered == null) hovered = hit.collider.GetComponentInParent<AlbumSleeve>();
        }

        if (hovered != _lastHoveredSleeve)
        {
            if (_lastHoveredSleeve != null)
                _lastHoveredSleeve.SetHovered(false);

            if (hovered != null)
                hovered.SetHovered(true);

            _lastHoveredSleeve = hovered;
        }
    }

    private void FixedUpdate()
    {
        if (_held == null || _heldRb == null) return;

        // Tether snap-back
        if (Vector3.Distance(_heldRb.position, _grabTarget) > maxTetherDistance)
        {
            _heldRb.position = _grabTarget;
            _heldRb.linearVelocity = Vector3.zero;
        }

        // Pickup feel: plucky spring for the first fraction of a second,
        // then ramp to stiff carry so the object follows smoothly.
        _pickupTimer = Mathf.Max(_pickupTimer - Time.fixedDeltaTime, 0f);
        float pickupT = Mathf.Clamp01(_pickupTimer / PickupFeelDuration); // 1 = just picked up, 0 = carrying

        GetActiveGrabParams(out float presetSpring, out float presetDamper,
            out float presetAccel, out float presetSpeed);

        // Carry params: slightly springy for a toylike lag feel
        const float carrySpring = 250f;
        const float carryDamper = 22f;
        const float carryAccel = 120f;
        const float carrySpeed = 16f;

        // Blend from preset (plucky) to carry (stiff)
        float activeSpring = Mathf.Lerp(carrySpring, presetSpring, pickupT);
        float activeDamper = Mathf.Lerp(carryDamper, presetDamper, pickupT);
        float activeMaxAccel = Mathf.Lerp(carryAccel, presetAccel, pickupT);
        float activeMaxSpeed = Mathf.Lerp(carrySpeed, presetSpeed, pickupT);

        Vector3 toTarget = _grabTarget - _heldRb.worldCenterOfMass;
        Vector3 force = toTarget * activeSpring - _heldRb.linearVelocity * activeDamper;

        if (force.sqrMagnitude > activeMaxAccel * activeMaxAccel)
            force = force.normalized * activeMaxAccel;

        _heldRb.AddForce(force, ForceMode.Acceleration);

        // Speed cap
        if (_heldRb.linearVelocity.sqrMagnitude > activeMaxSpeed * activeMaxSpeed)
            _heldRb.linearVelocity = _heldRb.linearVelocity.normalized * activeMaxSpeed;

        // Wall rejection: cancel velocity into walls so the item slides along
        // room geometry instead of tunneling through it. Uses Walls layer (31)
        // only — not surfaces/furniture, which would cause snagging.
        RejectVelocityIntoWalls();
    }

    private void RejectVelocityIntoWalls()
    {
        Vector3 vel = _heldRb.linearVelocity;
        float speed = vel.magnitude;
        if (speed < 0.01f) return;

        // SphereCast along velocity to detect imminent wall collision
        float castRadius = 0.05f;
        float lookAhead = speed * Time.fixedDeltaTime + castRadius;
        int wallMask = _wallLayer | _barrierLayer;
        if (wallMask == 0) return;

        if (Physics.SphereCast(_heldRb.worldCenterOfMass, castRadius, vel.normalized,
                out RaycastHit hit, lookAhead, wallMask, QueryTriggerInteraction.Ignore))
        {
            // Remove velocity component going into the wall
            float into = Vector3.Dot(vel, -hit.normal);
            if (into > 0f)
                _heldRb.linearVelocity = vel + hit.normal * into;
        }
    }

    // ── Pick up ──────────────────────────────────────────────────────

    private static void ConsumeClick()
    {
        ClickConsumedThisFrame = true;
        s_lastConsumedFrame = Time.frameCount;
    }

    /// <summary>Allow other systems (WateringManager, CleaningManager) to consume the click.</summary>
    public static void ConsumeClickExternal() => ConsumeClick();

    private void TryPickUp()
    {
        Vector2 screenPos = IrisInput.CursorPosition;
        Ray ray = ScreenRay(screenPos);

        // QueryTriggerInteraction.Collide so mess items (petals, trimmings)
        // with trigger colliders can be clicked. Also check RecordSleeves layer (30).
        int pickupMask = placeableLayer | (1 << 30);
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f, pickupMask, QueryTriggerInteraction.Collide))
            return;

        var placeable = hit.collider.GetComponent<PlaceableObject>();

        // Walk up the hierarchy to find the first ENABLED PlaceableObject.
        // Handles: paired children (disabled PlaceableObject), compound colliders,
        // and any nested structure.
        if (placeable == null || !placeable.enabled)
        {
            placeable = null;
            Transform walk = hit.collider.transform;
            while (walk != null)
            {
                var p = walk.GetComponent<PlaceableObject>();
                if (p != null && p.enabled) { placeable = p; break; }
                walk = walk.parent;
            }
        }

        // Standard parent walk — skip disabled PlaceableObjects (paired children)
        if (placeable == null)
        {
            var candidates = hit.collider.GetComponentsInParent<PlaceableObject>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i].enabled) { placeable = candidates[i]; break; }
            }
        }

        // Perfume bottle — click to spray in place (no pickup)
        var perfumeHit = hit.collider.GetComponent<PerfumeBottle>();
        if (perfumeHit == null) perfumeHit = hit.collider.GetComponentInParent<PerfumeBottle>();
        if (perfumeHit != null)
        {
            perfumeHit.SprayOnce();
            ConsumeClick();
            return;
        }

        // Light switch on the clicked collider — toggle even if parent is a PlaceableObject
        // (e.g. lamp shade toggles light, lamp base picks up the whole lamp)
        var directSwitch = hit.collider.GetComponent<LightSwitch>();
        if (directSwitch != null)
        {
            directSwitch.Toggle();
            ConsumeClick();
            return;
        }

        // No placeable hit — check for cubby/drawer door click
        if (placeable == null)
        {
            // DrinkGlass without PlaceableObject — handle during date Phase 2
            var noPlaceableGlass = hit.collider.GetComponent<DrinkGlass>();
            if (noPlaceableGlass == null) noPlaceableGlass = hit.collider.GetComponentInParent<DrinkGlass>();
            if (noPlaceableGlass != null && DrinkPourManager.Instance != null)
            {
                var pourState = DrinkPourManager.Instance.CurrentState;
                if (pourState == DrinkPourManager.State.ChoosingGlass
                    || pourState == DrinkPourManager.State.Pouring)
                {
                    DrinkPourManager.Instance.SelectGlass(noPlaceableGlass);
                    ConsumeClick();
                    return;
                }
                if (pourState == DrinkPourManager.State.ChoosingServeGlass)
                {
                    DrinkPourManager.Instance.ServeGlass(noPlaceableGlass);
                    ConsumeClick();
                    return;
                }
            }

            // Demo-locked doors jiggle instead of opening
            var demoLock = hit.collider.GetComponent<DemoLock>();
            if (demoLock == null) demoLock = hit.collider.GetComponentInParent<DemoLock>();
            if (demoLock != null)
            {
                demoLock.OnClicked();
                ConsumeClick();
                return;
            }

            // Cabinets/cubbies/drawers are blocked during all date phases
            bool dateActiveForDrawer = DateSessionManager.Instance != null && DateSessionManager.Instance.IsDateActive;
            if (!dateActiveForDrawer)
            {
                var clickedDrawer = hit.collider.GetComponent<DrawerController>();
                if (clickedDrawer == null)
                    clickedDrawer = hit.collider.GetComponentInParent<DrawerController>();

                if (clickedDrawer != null)
                {
                    if (clickedDrawer.CurrentState == DrawerController.State.Closed)
                        clickedDrawer.Open();
                    else if (clickedDrawer.CurrentState == DrawerController.State.Open)
                        clickedDrawer.Close();
                    ConsumeClick();
                    return;
                }
            }

            // Check for Nema click — open personality/wellbeing panel (non-date only)
            var nema = hit.collider.GetComponent<NemaController>();
            if (nema == null) nema = hit.collider.GetComponentInParent<NemaController>();
            if (nema != null)
            {
                if (DayPhaseManager.Instance == null
                    || DayPhaseManager.Instance.CurrentPhase != DayPhaseManager.DayPhase.DateInProgress)
                {
                    NemaProfilePanel.Instance?.Open();
                    ConsumeClick();
                    return;
                }
            }

            // Check for light switch click
            var lightSwitch = hit.collider.GetComponent<LightSwitch>();
            if (lightSwitch == null)
                lightSwitch = hit.collider.GetComponentInParent<LightSwitch>();
            if (lightSwitch != null)
            {
                lightSwitch.Toggle();
                ConsumeClick();
                return;
            }

            // Click on album sleeve — extract vinyl if hovered/peeking (fully blocked during dates)
            bool dateActiveForSleeve = DateSessionManager.Instance != null && DateSessionManager.Instance.IsDateActive;
            if (!dateActiveForSleeve)
            {
                var clickedSleeve = hit.collider.GetComponent<AlbumSleeve>();
                if (clickedSleeve == null)
                    clickedSleeve = hit.collider.GetComponentInParent<AlbumSleeve>();
                if (clickedSleeve != null && clickedSleeve.IsHovered)
                {
                    if (TryExtractAndGrab(clickedSleeve)) return;
                }
            }

            // Click on a drink glass while not holding anything
            var clickedGlass = hit.collider.GetComponent<DrinkGlass>();
            if (clickedGlass == null)
                clickedGlass = hit.collider.GetComponentInParent<DrinkGlass>();
            if (clickedGlass != null && DrinkPourManager.Instance != null)
            {
                var pourState = DrinkPourManager.Instance.CurrentState;
                if (pourState == DrinkPourManager.State.ChoosingGlass)
                {
                    // Step 1: player picks which glass to pour into
                    DrinkPourManager.Instance.SelectGlass(clickedGlass);
                    ConsumeClick();
                    return;
                }
                if (pourState == DrinkPourManager.State.ChoosingServeGlass)
                {
                    // Step 3: player picks which glass to serve the date
                    DrinkPourManager.Instance.ServeGlass(clickedGlass);
                    ConsumeClick();
                    return;
                }
            }

            // Click on vinyl sitting on platter:
            //   If playing → stop (pause + lift needle)
            //   If stopped → pick up into hand
            var clickedVinyl = hit.collider.GetComponent<VinylDisc>();
            if (clickedVinyl != null && RecordSlot.Instance != null && RecordSlot.Instance.IsLoaded)
            {
                if (RecordSlot.Instance.IsPlaying)
                {
                    RecordSlot.Instance.Pause();
                }
                else
                {
                    var ejected = RecordSlot.Instance.EjectVinyl();
                    if (ejected != null)
                    {
                        _held = ejected;
                        _pickupTimer = PickupFeelDuration;
                        InitGrab();
                    }
                }
                ConsumeClick();
                return;
            }

            // Click on turntable lid — toggle open/close
            if (RecordSlot.Instance != null && RecordSlot.Instance.IsLidTarget(hit.collider))
            {
                RecordSlot.Instance.ToggleLid();
                ConsumeClick();
                return;
            }

            // Click on turntable body while vinyl is loaded — toggle play/pause
            var clickedSlot = hit.collider.GetComponent<RecordSlot>();
            if (clickedSlot == null)
                clickedSlot = hit.collider.GetComponentInParent<RecordSlot>();
            if (clickedSlot != null && clickedSlot.IsLoaded)
            {
                clickedSlot.TogglePlayPause();
                ConsumeClick();
                return;
            }

            return;
        }

        if (placeable.CurrentState == PlaceableObject.State.Held)
            return;

        // Settle immunity: item was just placed — skip pickup for ~3 frames to prevent re-grab
        if (placeable.IsSettling)
            return;

        // Album sleeves are static — never grab them
        if (placeable.GetComponent<AlbumSleeve>() != null
            || placeable.GetComponentInParent<AlbumSleeve>() != null)
        {
            // During dates, don't allow vinyl extraction at all
            bool dateActive = DateSessionManager.Instance != null && DateSessionManager.Instance.IsDateActive;
            if (!dateActive)
            {
                // But if the click hit the peeking vinyl, extract it
                var vinylOnSleeve = placeable.GetComponent<VinylDisc>();
                if (vinylOnSleeve == null) vinylOnSleeve = placeable.GetComponentInChildren<VinylDisc>();
                var parentSleeve = vinylOnSleeve != null ? vinylOnSleeve.HomeSleeve : null;
                if (parentSleeve != null && parentSleeve.IsHovered)
                    TryExtractAndGrab(parentSleeve);
            }
            return; // never grab the sleeve itself
        }

        // Vinyl in album sleeve — extract on click (when sleeve is hovered / peeking)
        var vinylDisc = placeable.GetComponent<VinylDisc>();
        if (vinylDisc != null)
        {
            bool dateActive = DateSessionManager.Instance != null && DateSessionManager.Instance.IsDateActive;
            var sleeve = vinylDisc.HomeSleeve;
            if (!dateActive && sleeve != null && sleeve.IsHovered)
            {
                if (TryExtractAndGrab(sleeve)) return;
            }
        }


        // During date phases — block pickup, allow only phase-appropriate interactions
        if (DateSessionManager.Instance != null && DateSessionManager.Instance.IsDateActive)
        {
            var datePhase = DateSessionManager.Instance.CurrentDatePhase;

            // Phase 3 (Reveal): observation only — click to inspect, no pickup at all
            if (datePhase == DateSessionManager.DatePhase.Reveal)
            {
                if (DateInspectSystem.Instance != null && DateInspectSystem.Instance.TryInspect())
                    ConsumeClick();
                return;
            }

            // Phase 2 (BackgroundJudging): glasses and bottles allowed for drink making
            if (datePhase == DateSessionManager.DatePhase.BackgroundJudging)
            {
                var clickedGlass = placeable.GetComponent<DrinkGlass>();
                if (clickedGlass == null) clickedGlass = placeable.GetComponentInParent<DrinkGlass>();
                if (clickedGlass == null) clickedGlass = placeable.GetComponentInChildren<DrinkGlass>();
                if (clickedGlass != null && DrinkPourManager.Instance != null)
                {
                    var pourState = DrinkPourManager.Instance.CurrentState;
                    if (pourState == DrinkPourManager.State.ChoosingGlass
                        || pourState == DrinkPourManager.State.Pouring)
                    {
                        // Select or switch glass — shows cutaway + recipe card
                        DrinkPourManager.Instance.SelectGlass(clickedGlass);
                        ConsumeClick();
                        return;
                    }
                    if (pourState == DrinkPourManager.State.ChoosingServeGlass)
                    {
                        DrinkPourManager.Instance.ServeGlass(clickedGlass);
                        ConsumeClick();
                        return;
                    }
                }

                // Bottles allowed — fall through to normal pickup
                bool isBottle = placeable.GetComponent<BottleItem>() != null;
                if (isBottle)
                {
                    // Fall through to normal pickup below
                }
                else
                {
                    // Non-glass, non-bottle clicks during Phase 2 — ignore silently
                    return;
                }
            }
            else
            {
                // Phase 1 or anything else — inspect reaction, no pickup
                if (DateInspectSystem.Instance != null && DateInspectSystem.Instance.TryInspect())
                    ConsumeClick();
                return;
            }
        }

        ConsumeClick();
        _held = placeable;
        _pickupTimer = PickupFeelDuration;
        InitGrab();
    }

    /// <summary>
    /// Shared grab initialization. Assumes _held and _pickupTimer are already set.
    /// Sets up physics, bounds cache, shadow, ghost preview, and description HUD.
    /// </summary>
    private void InitGrab()
    {
        var placeable = _held;

        // Clear all highlight layers on the item being picked up
        var heldHL = placeable.GetComponent<InteractableHighlight>();
        if (heldHL != null)
        {
            heldHL.SetHighlighted(false);
            heldHL.SetInteractHighlighted(false);
            heldHL.SetGazeHighlighted(false);
            heldHL.SetDisplayHighlighted(false);
            heldHL.SetPrepLikedHighlighted(false);
            heldHL.SetPrepDislikedHighlighted(false);
        }

        // Flash partner highlight for pairable items (shoes)
        var pairable = placeable.GetComponent<PairableItem>();
        if (pairable != null)
        {
            InteractableHighlight.SuppressVisuals = false;
            pairable.OnPickedUp();
        }

        // Cache collider bounds BEFORE disabling — used for shadow size + surface offset
        // Check children too: compound objects (e.g. plant pot) have colliders on child meshes
        var pickupCol = placeable.GetComponent<Collider>()
                     ?? placeable.GetComponentInChildren<Collider>();
        if (pickupCol != null)
        {
            _heldBoundsExtents = pickupCol.bounds.extents;
            _heldBoundsCenterOffset = pickupCol.bounds.center - placeable.transform.position;
            _heldShadowDiameter = Mathf.Max(_heldBoundsExtents.x, _heldBoundsExtents.z) * 2f * 1.3f;
        }
        else
        {
            _heldBoundsExtents = Vector3.one * 0.1f;
            _heldBoundsCenterOffset = Vector3.zero;
            _heldShadowDiameter = 0.3f;
        }

        // Detach from plate stack before pickup
        var stackable = placeable.GetComponent<StackablePlate>();
        if (stackable != null)
            stackable.PrepareForGrab();

        _heldRb = placeable.GetComponent<Rigidbody>();
        if (_heldRb == null)
        {
            // PlaceableObject missing its Rigidbody — abort pickup cleanly
            Debug.LogWarning($"[ObjectGrabber] Cannot grab '{placeable.name}' — no Rigidbody component.");
            _held = null;
            _pickupTimer = 0f;
            return;
        }
        _heldRb.useGravity = false;
        _heldRb.isKinematic = false;
        _heldRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _grabTarget = _heldRb.worldCenterOfMass;

        // Store depth for floating fallback when cursor isn't over a surface
        _fallbackDepth = Vector3.Dot(
            _heldRb.worldCenterOfMass - cam.transform.position,
            cam.transform.forward);

        // Lock rotation so collisions don't spin the object
        _originalConstraints = _heldRb.constraints;
        _heldRb.constraints = _originalConstraints | RigidbodyConstraints.FreezeRotation;

        _currentSurface = placeable.LastPlacedSurface;
        _lastValidSurface = _currentSurface;
        _isOnWall = _currentSurface != null && _currentSurface.IsVertical;

        // Preserve wall rotation so pick-up + put-down keeps the same facing
        _heldOriginalForward = placeable.transform.forward;
        if (_isOnWall)
        {
            // Use the item's own forward to determine which side of the wall it was on
            Vector3 wallNormal = Vector3.Dot(_heldOriginalForward, _currentSurface.SurfaceNormal) >= 0f
                ? _currentSurface.SurfaceNormal
                : -_currentSurface.SurfaceNormal;
            Quaternion faceWall = Quaternion.LookRotation(wallNormal, Vector3.up);
            Quaternion delta = Quaternion.Inverse(faceWall) * placeable.transform.rotation;
            _wallRotation = delta.eulerAngles.z;
            if (_wallRotation > 180f) _wallRotation -= 360f;
        }
        else
        {
            _wallRotation = 0f;
        }

        placeable.OnPickedUp();

        // When picking up the watering can, highlight all plants so
        // the player knows where to water (same steady highlight as shoes).
        if (placeable.GetComponent<WateringCan>() != null)
            SetPlantHighlights(true);

        // When picking up a vinyl record, highlight the turntable until placed
        if (placeable.GetComponent<VinylDisc>() != null && RecordSlot.Instance != null)
        {
            InteractableHighlight.SuppressVisuals = false;
            var slotHL = RecordSlot.Instance.GetComponent<InteractableHighlight>();
            if (slotHL == null) slotHL = RecordSlot.Instance.gameObject.AddComponent<InteractableHighlight>();
            slotHL.SetHighlighted(true);
        }

        // Book: hidden item check + grab books above in stack
        var bookItem = placeable.GetComponent<BookItem>();
        if (bookItem != null)
        {
            bookItem.OnBookPickedUp();
            bookItem.GrabBooksAbove();
        }

        // Blanket fold + hidden item reveal
        var blanket = placeable.GetComponent<BlanketItem>();
        if (blanket != null) blanket.OnBlanketPickedUp();

        AudioManager.Instance?.PlaySFX(placeable.PickupSFXOverride != null ? placeable.PickupSFXOverride : _pickupSFX);
        ShowShadow(true);
        BuildGhostPreview(placeable);

        // Show pickup description
        if (PickupDescriptionHUD.Instance != null)
            PickupDescriptionHUD.Instance.Show(placeable.ItemDescription);

        // Pickup squish-pop feel
        if (_squishCoroutine != null) StopCoroutine(_squishCoroutine);
        _squishCoroutine = StartCoroutine(SquishPop(placeable.transform, 0.85f, 1.08f));
    }

    // ── Place ────────────────────────────────────────────────────────

    private void Place()
    {
        if (_held == null) return;

        // ── Watering can ──
        // Near a plant: LMB starts watering. Away from plants: LMB places
        // the can on the surface like any other item.
        if (_held.GetComponent<WateringCan>() != null)
        {
            var wm = WateringManager.Instance;
            WaterablePlant clickPlant = _nearestWaterablePlant;

            if (clickPlant != null && wm != null)
            {
                if (wm.CurrentState != WateringManager.State.Pouring
                    || wm.ActivePlant != clickPlant)
                {
                    wm.StartWatering(clickPlant);
                }
                ConsumeClick();
                return;
            }

            // Not near a plant — fall through to normal surface placement below
        }

        // ── Gunpla part near body → attach (proximity snap fallback) ──
        if (_nearestGunplaBody != null)
        {
            var heldPart = _held.GetComponent<GunplaPart>();
            if (heldPart != null && _nearestGunplaBody.CanAccept(heldPart))
            {
                _nearestGunplaBody.AttachPart(heldPart);
                ClearHeld();
                ConsumeClick();
                return;
            }
        }

        // ── Bottle near glass → start pouring instead of placing ──
        // Only during BackgroundJudging date phase — bottles are normal items otherwise.
        bool isDrinkPhase = DateSessionManager.Instance != null
            && DateSessionManager.Instance.CurrentDatePhase == DateSessionManager.DatePhase.BackgroundJudging;
        var heldBottle = _held.GetComponent<BottleItem>();
        if (isDrinkPhase && heldBottle != null && heldBottle.Ingredient != null && _nearestDrinkGlass != null)
        {
            if (DrinkPourManager.Instance != null)
            {
                DrinkPourManager.Instance.StartPouring(_nearestDrinkGlass, heldBottle.Ingredient);
                ConsumeClick();
                return; // keep holding the bottle
            }
        }

        // ── Bottle during drink phase — always return to home ──
        if (isDrinkPhase && heldBottle != null && heldBottle.HasHome)
        {
            heldBottle.ReturnHome();
            ClearHeld();
            ConsumeClick();
            return;
        }

        // Drink delivery is now click-to-serve (no pickup needed) — handled
        // in the non-holding click path above via DrinkPourManager.ServeGlass().

        // ── DiscoBall check ──
        if (_currentSurface != null)
        {
            var disco = _currentSurface.GetComponentInParent<DiscoBallController>();
            if (disco != null && _held.GetComponent<DiscoBallBulb>() != null)
            {
                _heldRb.constraints = _originalConstraints;
                _heldRb.linearVelocity = Vector3.zero;
                if (disco.TryAcceptBulb(_held))
                {
                    PlayPlaceSFX(_held);
                    ClearHeld();
                    return;
                }
            }
        }

        // ── Vinyl placement ──
        var heldVinyl = _held.GetComponent<VinylDisc>();
        if (heldVinyl != null)
        {
            // Try placing on turntable
            if (RecordSlot.Instance != null && RecordSlot.Instance.TryAcceptVinyl(_held))
            {
                PlayPlaceSFX(_held);
                ClearHeld();
                ConsumeClick();
                return;
            }

            // Anywhere else — return to home sleeve
            if (heldVinyl.HomeSleeve != null)
            {
                heldVinyl.HomeSleeve.ReturnVinyl(heldVinyl);
                ClearHeld();
                ConsumeClick();
                return;
            }
        }

        // Check if clicking directly on a DropZone (trash can body, fridge, etc.)
        // This runs regardless of surface state so clicking anywhere on the
        // trash can (body or lid) deposits trash.
        if (TryDepositAtNearbyZone())
        {
            ConsumeClick();
            return;
        }

        // Proximity deposit: if the item was magnetically pulled close to a
        // destroy zone (trash can, sink), deposit it even if the click missed
        // the zone's collider (hit the counter behind it instead).
        if (_held.Category == ItemCategory.Trash || _held.Category == ItemCategory.Dish)
        {
            var allZones = DropZone.All;
            for (int i = 0; i < allZones.Count; i++)
            {
                var z = allZones[i];
                if (z == null || !z.DestroyOnDeposit) continue;

                bool matches = (_held.Category == ItemCategory.Trash && z.ZoneName == "TrashCan")
                            || (_held.Category == ItemCategory.Dish && z.ZoneName == "Sink")
                            || z.ZoneName == _held.HomeZoneName
                            || z.ZoneName == _held.AltHomeZoneName;
                if (!matches) continue;

                float dist = Vector3.Distance(z.transform.position, _grabTarget);
                if (dist < _trashSnapRadius)
                {
                    z.RegisterDeposit(_held);
                    ClearHeld();
                    ConsumeClick();
                    return;
                }
            }
        }

        // No surface under cursor — snap to the nearest surface as a fallback
        // so items don't free-fall through the floor. BUT only if there's no
        // barrier geometry nearby — if the occlusion check rejected surfaces
        // because of a barrier (inside furniture), don't bypass it with fallback.
        if (_currentSurface == null)
        {
            int blockMask = _wallLayer | _barrierLayer;
            bool nearBarrier = Physics.CheckSphere(_grabTarget, 0.15f, blockMask);
            if (nearBarrier) return; // inside furniture — don't place here

            var fallbackSurface = PlacementSurface.FindNearest(_grabTarget, skipVertical: true);
            if (fallbackSurface == null) return;
            _currentSurface = fallbackSurface;
        }

        // ── DropZone check ──
        // Only match the DropZone directly on this surface — parent/child
        // traversal was causing the kitchen counter to match the nearby sink.
        {
            DropZone zone = _currentSurface.GetComponent<DropZone>();
            bool zoneIsDirect = zone != null; // true = zone is on this surface, not found via proximity

            // No proximity fallback — items must be placed directly on a DropZone's
            // surface to match. The player should bring the item to the blinking
            // area and place it there, not have it snatched from nearby surfaces.

            // Block closed fridge shelves + closed cubbies from accepting
            // home-zone deposits too (bottles-in-fridge, books-in-cubby).
            if (zone != null && FridgeController.Instance != null)
            {
                var zoneSurface = zone.GetComponent<PlacementSurface>()
                    ?? zone.GetComponentInParent<PlacementSurface>();
                if (zoneSurface != null && FridgeController.Instance.IsShelfAndClosed(zoneSurface))
                {
                    Debug.Log($"[ObjectGrabber] BLOCKED: home-zone deposit on closed fridge shelf '{zone.ZoneName}'");
                    zone = null;
                }
            }
            if (zone != null)
            {
                var zoneSurface = zone.GetComponent<PlacementSurface>()
                    ?? zone.GetComponentInParent<PlacementSurface>();
                if (zoneSurface != null && IsCubbyAndClosed(zoneSurface))
                {
                    Debug.Log($"[ObjectGrabber] BLOCKED: home-zone deposit in closed cubby '{zone.ZoneName}'");
                    zone = null;
                }
            }

            if (zone != null)
            {
                bool matchesHome = (!string.IsNullOrEmpty(_held.HomeZoneName) && zone.ZoneName == _held.HomeZoneName)
                    || (!string.IsNullOrEmpty(_held.AltHomeZoneName) && zone.ZoneName == _held.AltHomeZoneName);
                // Category-specific destroy zones (trash→TrashCan, dishes→Sink, milk→Fridge)
                if (!matchesHome && zone.DestroyOnDeposit)
                {
                    if (_held.Category == ItemCategory.Trash && zone.ZoneName == "TrashCan")
                        matchesHome = true;
                    else if (_held.Category == ItemCategory.Dish && zone.ZoneName == "Sink")
                        matchesHome = true;
                }
                // Proximity-found zone with unpaired pairable — skip the zone,
                // fall through to normal surface placement instead.
                if (matchesHome && !zoneIsDirect)
                {
                    var pairable = _held.GetComponent<PairableItem>();
                    if (pairable != null && pairable.Mode == PairableItem.PairMode.SpecificPartner && !pairable.IsPaired)
                    {
                        zone = null;
                        matchesHome = false;
                    }
                }

                if (matchesHome)
                {
                    _heldRb.constraints = _originalConstraints;
                    _heldRb.linearVelocity = Vector3.zero;

                    if (zone.DestroyOnDeposit)
                    {
                        // Trash can — shrink and destroy
                        zone.RegisterDeposit(_held);
                        ClearHeld();
                        return;
                    }

                    // Auto-slot deposit (shoe rack, etc.) — snap to the nearest
                    // free slot to where the player is pointing.
                    if (zone.UseSlotting
                        && zone.TryGetNearestFreeSlot(_grabTarget, out var slotPos, out var slotRot))
                    {
                        // Pairable items (shoes) always keep the player's rotation;
                        // other items respect the zone's PreserveItemRotation flag.
                        var heldPairable = _held.GetComponent<PairableItem>();
                        if (heldPairable != null || zone.PreserveItemRotation)
                            slotRot = _held.transform.rotation;
                        _held.OnPlaced(_currentSurface, true, slotPos, slotRot);

                        // Lock slotted items in place — they shouldn't fall off
                        // their slot or roll around. Picking the shoe back up
                        // re-enables physics via PlaceableObject.OnPickedUp.
                        LockRigidbodyAtSlot(_held);

                        zone.ClaimSlotFor(_held);
                        zone.RegisterDeposit(_held);
                        OnObjectPlaced?.Invoke(_held);
                        PlayPlaceSFX(_held);
                        _held.gameObject.AddComponent<SettleBounce>().Play(_held.transform.localScale);
                        ClearHeld();
                        return;
                    }

                    // Normal drop zone — place normally, zone registers deposit
                    // Fall through to standard placement, OnPlaced will set IsAtHome
                }
            }
        }

        // Wall items only on walls, table items only on tables
        if (_currentSurface.IsVertical && !_held.CanWallMount)
        {
            Debug.Log($"[ObjectGrabber] BLOCKED: surface '{_currentSurface.name}' IsVertical={_currentSurface.IsVertical} but item CanWallMount=false");
            return;
        }
        if (!_currentSurface.IsVertical && _held.WallOnly)
        {
            Debug.Log($"[ObjectGrabber] BLOCKED: surface '{_currentSurface.name}' is horizontal but item is WallOnly");
            return;
        }

        // Fridge shelves blocked when doors are closed
        if (FridgeController.Instance != null && FridgeController.Instance.IsShelfAndClosed(_currentSurface))
        {
            Debug.Log($"[ObjectGrabber] BLOCKED: fridge shelf '{_currentSurface.name}' — doors are closed");
            return;
        }

        // Cubby interiors blocked when drawer/door is closed
        var placeCubby = DrawerController.FindByInteriorSurface(_currentSurface);
        if (placeCubby != null && placeCubby.IsInteriorAndClosed(_currentSurface))
        {
            Debug.Log($"[ObjectGrabber] BLOCKED: cubby '{_currentSurface.name}' — door is closed");
            return;
        }

        // Trash only on floor or trash cans
        if (_held.Category == ItemCategory.Trash)
        {
            var zone = _currentSurface.GetComponent<DropZone>();
            bool isTrashCan = zone != null && zone.DestroyOnDeposit;
            if (!_currentSurface.IsFloor && !isTrashCan)
            {
                Debug.Log($"[ObjectGrabber] BLOCKED TRASH: surface '{_currentSurface.name}' IsFloor={_currentSurface.IsFloor} isTrashCan={isTrashCan}");
                return;
            }
        }

        // Use _grabTarget (cursor-tracked) instead of _heldRb.position for wall face
        // detection — the rigidbody can overshoot through thin wall triggers.
        var hitResult = _currentSurface.ProjectOntoSurface(_grabTarget, cam.transform.position);
        Vector3 pos = _currentSurface.SnapToGrid(hitResult.worldPosition, GetGridSize(_currentSurface), cam.transform.position);

        float halfExtent = GetHeldHalfExtentAlongNormal(hitResult.surfaceNormal);
        pos += hitResult.surfaceNormal * halfExtent;

        Quaternion rot;
        bool bookIsFlat = false;
        if (_currentSurface.IsVertical)
        {
            // Use a consistent normal: if the item's original forward agrees with
            // the hit normal, use it as-is; otherwise flip. This prevents the
            // camera-dependent face detection from flipping the painting around.
            Vector3 wallNormal = hitResult.surfaceNormal;
            if (_held.CanWallMount && Vector3.Dot(_heldOriginalForward, wallNormal) < 0f)
                wallNormal = -wallNormal;
            _held.AlignToWall(wallNormal, _wallRotation);
            rot = _held.transform.rotation;
        }
        else
        {
            // Watering can always goes upright; other items keep held rotation
            // unless they were pour-tilted (bottles near glasses)
            if (_held.GetComponent<WateringCan>() != null)
                rot = Quaternion.Euler(0f, _held.transform.eulerAngles.y, 0f);
            else
                rot = _isPourTilted ? Quaternion.identity : _held.transform.rotation;

        }
        _isPourTilted = false;

        // Home snap: if placing very close to the item's home position, use exact home pos/rot.
        // Use a tight radius so wall items can be placed freely on the wall without
        // constantly snapping back to their spawn point.
        // Paired items (shoes) keep the player's chosen rotation so they can be
        // arranged freely on the shoe rack.
        if (_held.HasHome && Vector3.Distance(pos, _held.HomePosition) < _held.HomeTolerance)
        {
            pos = _held.HomePosition;
            var pairable = _held.GetComponent<PairableItem>();
            if (pairable == null || !pairable.IsPaired)
                rot = _held.HomeRotation;
        }

        // Barrier check — reject if placement position overlaps a barrier volume.
        // Only the sphere overlap is used; the old downward ray was hitting barrier
        // colliders inside counter/table bodies directly below valid surfaces.
        if (_barrierLayer != 0 && !_currentSurface.IsVertical)
        {
            if (Physics.CheckSphere(pos, 0.009f, _barrierLayer))
            {
                Debug.Log($"[ObjectGrabber] BLOCKED: placement at {pos} is inside barrier geometry.");
                return;
            }
        }

        // Exclusion zones
        if (PlacementExclusionZone.IsExcluded(pos))
            return;

        // Bounds check: reject if item footprint hangs off the surface
        if (!_currentSurface.IsFloor && !_currentSurface.IsVertical)
        {
            float halfX = _heldBoundsExtents.x;
            float halfZ = _heldBoundsExtents.z;
            Vector3 surfNormal = _currentSurface.SurfaceNormal;
            Vector3 fwd = Vector3.ProjectOnPlane(rot * Vector3.forward, surfNormal).normalized;
            Vector3 rgt = Vector3.Cross(surfNormal, fwd).normalized;
            if (!_currentSurface.ContainsWorldPoint(pos + rgt * halfX + fwd * halfZ)
             || !_currentSurface.ContainsWorldPoint(pos - rgt * halfX + fwd * halfZ)
             || !_currentSurface.ContainsWorldPoint(pos + rgt * halfX - fwd * halfZ)
             || !_currentSurface.ContainsWorldPoint(pos - rgt * halfX - fwd * halfZ))
            {
                Debug.Log($"[ObjectGrabber] BLOCKED: item footprint hangs off surface edge at {pos}");
                return;
            }
        }

        // Cubby capacity check — reject placement if cubby is full
        var capacityCubby = DrawerController.FindByInteriorSurface(_currentSurface);
        if (capacityCubby != null && !capacityCubby.HasInteriorCapacity)
        {
            if (DialoguePortraitBox.Instance != null)
                DialoguePortraitBox.Instance.Say("No room in here.", 2f);
            else
                PickupDescriptionHUD.Instance?.Show("No room in here.");
            return;
        }

        // Occupancy check — block placement if another item already occupies the
        // resolved cell. Pairables stack via the click-on-partner path
        // (TryPairWithClicked above); plain placement here is never allowed to
        // overlap a sibling.
        var occupant = FindOccupant(pos);
        if (occupant != null)
        {
            Debug.Log($"[ObjectGrabber] BLOCKED: cell occupied at {pos}");
            return;
        }

        // Restore constraints before placement configures the rigidbody
        _heldRb.constraints = _originalConstraints;
        _heldRb.linearVelocity = Vector3.zero;

        _held.OnPlaced(_currentSurface, true, pos, rot);

        // Book: place stacked books above, then toggle privacy
        var bookItem = _held.GetComponent<BookItem>();
        if (bookItem != null)
        {
            bookItem.OnBookPlaced(_currentSurface, bookIsFlat);
            if (bookItem.HasStackedBooks)
                bookItem.ReleaseBooksAbove(_currentSurface, pos, rot);
        }

        // Cubby privacy: items placed on a closed cubby's interior surface become private.
        // Items placed anywhere else become public (clears stale privacy from previous location).
        var cubbyDrawer = DrawerController.FindByInteriorSurface(_currentSurface);
        bool inClosedCubby = cubbyDrawer != null && cubbyDrawer.IsInteriorAndClosed(_currentSurface);
        var privTag = _held.GetComponent<ReactableTag>();
        if (privTag != null)
            privTag.IsPrivate = inClosedCubby;

        OnObjectPlaced?.Invoke(_held);
        PlayPlaceSFX(_held);

        // DropZone deposit (non-destroy — item stays placed on surface)
        {
            var zone = _currentSurface.GetComponent<DropZone>();
            if (zone != null)
            {
                bool match = (!string.IsNullOrEmpty(_held.HomeZoneName) && zone.ZoneName == _held.HomeZoneName)
                    || (!string.IsNullOrEmpty(_held.AltHomeZoneName) && zone.ZoneName == _held.AltHomeZoneName);
                if (match)
                    zone.RegisterDeposit(_held);
            }
        }

        // Stack-aware plate hooks. No more auto-stack-on-place: stacking only
        // happens through explicit pairable click (TryPairWithClicked above).
        var stackable = _held.GetComponent<StackablePlate>();
        if (stackable != null)
        {
            var dishZone = _currentSurface != null
                ? _currentSurface.GetComponent<DishDropZone>() : null;
            if (dishZone != null)
            {
                // Deposit the held plate plus any child plates in the stack
                dishZone.RegisterDeposit(stackable);
                var childPlates = stackable.GetComponentsInChildren<StackablePlate>();
                foreach (var child in childPlates)
                {
                    if (child != stackable)
                        dishZone.RegisterDeposit(child);
                }
            }
        }

        // Settle bounce feel on the placed item
        if (_held != null)
        {
            _held.gameObject.AddComponent<SettleBounce>().Play(_held.transform.localScale);

            // Dust motes on heavy items (bounds volume > threshold)
            float vol = _heldBoundsExtents.x * _heldBoundsExtents.y * _heldBoundsExtents.z * 8f;
            if (vol > 0.005f)
                SmokePoof.Spawn(_held.transform.position, 0.08f, new Color(0.75f, 0.7f, 0.65f, 0.3f));
        }

        ClearHeld();
    }

    // ── Pair / stacking helpers ─────────────────────────────────────

    // Forgiving radius for pairing/stacking — SphereCast instead of Raycast
    private const float PairStackRadius = 0.08f;

    [Header("Pair Snap")]
    [Tooltip("World-space radius at which the held item magnetically snaps toward its pair partner while carrying.")]
    [SerializeField] private float _pairSnapRadius = 0.4f;

    [Header("Trash Can Snap")]
    [Tooltip("World-space radius at which trash items start being pulled toward the trash can lid.")]
    [SerializeField] private float _trashSnapRadius = 0.8f;

    [Tooltip("How strongly the item is pulled toward the lid (0 = no pull, 1 = full lock). Interpolated by proximity.")]
    [SerializeField, Range(0f, 1f)] private float _trashSnapStrength = 0.45f;

    [Header("Turntable Snap")]
    [Tooltip("World-space radius at which vinyl items start being pulled toward the turntable platter.")]
    [SerializeField] private float _turntableSnapRadius = 0.6f;

    [Tooltip("How strongly the vinyl is pulled toward the platter (0 = no pull, 1 = full lock).")]
    [SerializeField, Range(0f, 1f)] private float _turntableSnapStrength = 0.5f;

    [Header("Bottle Snap")]
    [Tooltip("World-space radius at which a held bottle snaps toward a glass.")]
    [SerializeField] private float _bottleSnapRadius = 1.2f;

    [Tooltip("How strongly the bottle is pulled toward the glass pour position.")]
    [SerializeField, Range(0f, 1f)] private float _bottleSnapStrength = 0.9f;

    [Header("Watering Can Snap")]
    [Tooltip("World-space radius at which the watering can snaps toward a plant.")]
    [SerializeField] private float _wateringSnapRadius = 1.5f;

    [Tooltip("How strongly the can is pulled toward the plant pour position.")]
    [SerializeField, Range(0f, 1f)] private float _wateringSnapStrength = 0.9f;

    private bool TryPairWithClicked()
    {
        if (_held == null) return false;

        var heldPairable = _held.GetComponent<PairableItem>();
        if (heldPairable == null) return false;
        // SpecificPartner (shoes) can't re-pair, but AnyOfCategory (plates) can stack freely
        if (heldPairable.IsPaired && heldPairable.Mode == PairableItem.PairMode.SpecificPartner) return false;

        Vector2 screenPos = IrisInput.CursorPosition;
        Ray ray = ScreenRay(screenPos);

        // Find a PairableItem along the ray, skipping the held item (its collider
        // sits at the cursor and would block the ray to the partner behind it).
        PairableItem clickedPairable = null;
        float bestDist = float.MaxValue;
        int hitCount = Physics.RaycastNonAlloc(ray, s_pairHitBuffer, 100f, placeableLayer);
        for (int i = 0; i < hitCount; i++)
        {
            var comp = s_pairHitBuffer[i].collider.GetComponent<PairableItem>();
            if (comp == null) comp = s_pairHitBuffer[i].collider.GetComponentInParent<PairableItem>();
            if (comp == null || comp == heldPairable) continue;
            if (s_pairHitBuffer[i].distance < bestDist)
            {
                bestDist = s_pairHitBuffer[i].distance;
                clickedPairable = comp;
            }
        }
        // SphereCast fallback for forgiving click area
        if (clickedPairable == null
            && Physics.SphereCast(ray, PairStackRadius, out RaycastHit sphereHit, 100f, placeableLayer))
        {
            var comp = sphereHit.collider.GetComponent<PairableItem>();
            if (comp == null) comp = sphereHit.collider.GetComponentInParent<PairableItem>();
            if (comp != null && comp != heldPairable)
                clickedPairable = comp;
        }

        if (clickedPairable == null) return false;
        if (!clickedPairable.CanPairWith(heldPairable)) return false;

        // Release held item from grabber
        _heldRb.constraints = _originalConstraints;
        _heldRb.linearVelocity = Vector3.zero;

        // Clean up held item visuals before pairing
        _held.ForceRestoreMaterial();
        _held.ForceDestroySilhouette();

        // Snap pair
        clickedPairable.SnapPair(heldPairable);

        PlayPlaceSFX(_held);
        ClearHeld();
        return true;
    }

    // ── Pair snap (magnetic carry bias) ──────────────────────────────

    private PairableItem _pairSnapTarget; // currently snapped-to partner (null = no snap)

    /// <summary>
    /// When holding a pairable item near its partner, snap the grab target
    /// to the pair position so the item magnetically locks in place.
    /// Called from UpdateGrabTarget after the normal position is calculated.
    /// </summary>
    private void UpdatePairSnap()
    {
        _pairSnapTarget = null;
        if (_held == null) return;

        var heldPairable = _held.GetComponent<PairableItem>();
        if (heldPairable == null) return;
        if (heldPairable.IsPaired && heldPairable.Mode == PairableItem.PairMode.SpecificPartner) return;

        // Find the nearest valid partner within snap radius
        PairableItem best = null;
        float bestDist = _pairSnapRadius;

        var allItems = PlaceableObject.All;
        for (int i = 0; i < allItems.Count; i++)
        {
            var po = allItems[i];
            if (po == null || po == _held) continue;
            if (po.CurrentState == PlaceableObject.State.Held) continue;

            var pairable = po.GetComponent<PairableItem>();
            if (pairable == null || pairable == heldPairable) continue;
            if (!pairable.CanPairWith(heldPairable)) continue;

            float dist = Vector3.Distance(po.transform.position, _grabTarget);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = pairable;
            }
        }

        if (best == null) return;

        _pairSnapTarget = best;

        // Calculate the position the held item would land at when paired
        Vector3 snapPos = best.GetPairSnapPosition(heldPairable);

        // If on a surface, grid-snap the pair position so items still
        // "click" into the surface grid instead of floating freely.
        if (_currentSurface != null)
            snapPos = _currentSurface.SnapToGrid(snapPos, GetGridSize(_currentSurface), cam.transform.position);

        _grabTarget = snapPos;

        // Hard-teleport so the item locks instantly (no spring lag)
        if (_heldRb != null)
        {
            _heldRb.position = snapPos;
            _heldRb.linearVelocity = Vector3.zero;
            _heldRb.angularVelocity = Vector3.zero;
        }
    }

    // ── Trash can magnetic snap ─────────────────────────────────────

    /// <summary>
    /// When holding a trash item near a trash can, softly pull the grab
    /// target toward the lid to signal "you can drop this here."
    /// </summary>
    private void UpdateTrashCanSnap()
    {
        if (_held == null) return;

        // Only snap items the trash can would accept
        bool isTrash = _held.Category == ItemCategory.Trash
                    || _held.HomeZoneName == "TrashCan"
                    || _held.AltHomeZoneName == "TrashCan";
        if (!isTrash) return;

        // Find nearest trash can within snap radius
        DropZone bestZone = null;
        float bestDist = _trashSnapRadius;

        var allZones = DropZone.All;
        for (int i = 0; i < allZones.Count; i++)
        {
            var zone = allZones[i];
            if (zone == null || !zone.DestroyOnDeposit) continue;
            if (zone.ZoneName != "TrashCan") continue;

            float dist = Vector3.Distance(zone.transform.position, _grabTarget);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestZone = zone;
            }
        }

        if (bestZone == null) return;

        // Pull toward the lid (top of the trash can)
        Vector3 lidPos = bestZone.transform.position + bestZone.transform.up * 0.15f;
        float proximity = 1f - (bestDist / _trashSnapRadius); // 0 at edge, 1 at center
        float strength = proximity * proximity * _trashSnapStrength; // quadratic ease-in
        _grabTarget = Vector3.Lerp(_grabTarget, lidPos, strength);
    }

    // ── Turntable magnetic snap ─────────────────────────────────────

    /// <summary>
    /// When holding a vinyl disc near the turntable, softly pull the grab
    /// target toward the platter to signal "you can place this here."
    /// </summary>
    private bool _turntableLidOpen;

    private void UpdateTurntableSnap()
    {
        if (_held == null) return;
        if (_held.GetComponent<VinylDisc>() == null) return;
        if (RecordSlot.Instance == null) return;

        Vector3 snapPos = RecordSlot.Instance.SnapPoint;
        float dist = Vector3.Distance(snapPos, _grabTarget);

        bool inRange = dist <= _turntableSnapRadius && dist > 0.01f;

        // Auto-open lid on proximity (player closes it manually)
        if (inRange && !_turntableLidOpen)
        {
            RecordSlot.Instance.OpenLidExternal();
            _turntableLidOpen = true;
        }
        else if (!inRange && _turntableLidOpen)
        {
            _turntableLidOpen = false; // reset tracking flag, but don't auto-close
        }

        if (!inRange) return;

        float proximity = 1f - (dist / _turntableSnapRadius);
        float strength = proximity * proximity * _turntableSnapStrength;
        _grabTarget = Vector3.Lerp(_grabTarget, snapPos, strength);
    }

    // ── Watering can magnetic snap + pour trigger ────────────────────

    private WaterablePlant _nearestWaterablePlant;
    private WaterablePlant _wateringUIEngagedPlant; // which plant the cutaway UI is currently showing for

    /// <summary>
    /// When holding a watering can near a plant, pull toward pour position.
    /// If the player clicks while snapped, start the watering session.
    /// </summary>
    // ── Bottle magnetic snap ────────────────────────────────────────

    private DrinkGlass _nearestDrinkGlass;
    private DrinkGlass _magnetizedGlass; // tracks highlight state

    private void UpdateBottleSnap()
    {
        _nearestDrinkGlass = null;
        if (_held == null) return;

        // Only snap bottles toward glasses during the drink-making date phase
        if (DateSessionManager.Instance == null
            || DateSessionManager.Instance.CurrentDatePhase != DateSessionManager.DatePhase.BackgroundJudging)
            return;

        var bottle = _held.GetComponent<BottleItem>();
        if (bottle == null) return;

        // Find nearest DrinkGlass
        DrinkGlass bestGlass = null;
        float bestDist = _bottleSnapRadius;

        foreach (var glass in DrinkGlass.All)
        {
            if (glass == null) continue;
            float dist = Vector3.Distance(glass.transform.position, _grabTarget);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestGlass = glass;
            }
        }

        // While actively pouring (LMB held), lock to the current glass
        // even if the cursor drifts away — same feel as watering can.
        bool wasPouring = DrinkPourManager.Instance != null
            && DrinkPourManager.Instance.CurrentState == DrinkPourManager.State.Pouring;
        var mouse = UnityEngine.InputSystem.Mouse.current;
        bool lmbHeld = mouse != null && mouse.leftButton.isPressed;

        // RMB while pouring → stop pouring and return bottle to home
        if (wasPouring && mouse != null && mouse.rightButton.wasPressedThisFrame)
        {
            DrinkPourManager.Instance.StopPouring();
            var heldBottle = _held != null ? _held.GetComponent<BottleItem>() : null;
            if (heldBottle != null && heldBottle.HasHome)
            {
                heldBottle.ReturnHome();
                ClearHeld();
            }
            return;
        }

        if (bestGlass == null && wasPouring && lmbHeld)
        {
            bestGlass = DrinkPourManager.Instance.ActiveGlass;
            if (bestGlass != null)
                bestDist = 0f;
        }

        if (bestGlass == null)
        {
            if (_magnetizedGlass != null)
            {
                SetGlassMagnetHighlight(_magnetizedGlass, false);
                _magnetizedGlass = null;
            }
            if (wasPouring)
                DrinkPourManager.Instance.StopPouring();
            return;
        }

        // Highlight the glass we're snapping to + show its cutaway/recipe
        if (bestGlass != _magnetizedGlass)
        {
            SetGlassMagnetHighlight(_magnetizedGlass, false);
            SetGlassMagnetHighlight(bestGlass, true);
            _magnetizedGlass = bestGlass;

            // Show this glass's recipe cutaway so the player sees what they're making
            if (DrinkPourManager.Instance != null && bestGlass != null)
            {
                var recipe = DrinkPourManager.Instance.ActiveRecipe;
                // Switch active glass if it changed
                if (bestGlass != DrinkPourManager.Instance.ActiveGlass)
                    DrinkPourManager.Instance.SelectGlass(bestGlass);
            }
        }

        _nearestDrinkGlass = bestGlass;

        // Snap to pour position above the glass rim
        Vector3 glassTop = bestGlass.transform.position + Vector3.up * 0.15f;
        Vector3 toCursor = _grabTarget - bestGlass.transform.position;
        toCursor.y = 0f;
        if (toCursor.sqrMagnitude < 0.001f) toCursor = Vector3.forward;
        toCursor.Normalize();

        Vector3 pourPos = bestGlass.transform.position + toCursor * 0.08f + Vector3.up * 0.2f;

        float proximity = 1f - (bestDist / _bottleSnapRadius);
        float strength = proximity > 0.4f
            ? Mathf.Lerp(0.5f, 1f, (proximity - 0.4f) / 0.6f)
            : proximity * proximity * _bottleSnapStrength;

        _grabTarget = Vector3.Lerp(_grabTarget, pourPos, strength);

        // Tilt bottle toward glass
        if (proximity > 0.5f)
        {
            Vector3 tiltDir = (bestGlass.transform.position - _held.transform.position).normalized;
            tiltDir.y = 0f;
            float tiltAngle = Mathf.Lerp(0f, 45f, (proximity - 0.5f) / 0.5f);
            Quaternion tiltRot = Quaternion.LookRotation(tiltDir, Vector3.up)
                               * Quaternion.Euler(tiltAngle, 0f, 0f);
            _held.transform.rotation = Quaternion.Slerp(_held.transform.rotation, tiltRot,
                                                         Time.deltaTime * 8f);
            _isPourTilted = true;
        }
        else if (_held != null && _isPourTilted)
        {
            _held.transform.rotation = Quaternion.Slerp(_held.transform.rotation,
                Quaternion.identity, Time.deltaTime * 8f);
            if (Quaternion.Angle(_held.transform.rotation, Quaternion.identity) < 1f)
            {
                _held.transform.rotation = Quaternion.identity;
                _isPourTilted = false;
            }
        }
    }

    private void UpdateWateringCanSnap()
    {
        _nearestWaterablePlant = null;
        if (_held == null) return;
        if (_held.GetComponent<WateringCan>() == null) return;

        var wm = WateringManager.Instance;
        bool wasWatering = wm != null
            && wm.CurrentState == WateringManager.State.Pouring;

        // RMB while holding watering can → put it down (or stop watering)
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null && mouse.rightButton.wasPressedThisFrame)
        {
            if (wasWatering && wm != null)
                wm.EndWatering();

            // Dismiss cutaway UI
            _wateringUIEngagedPlant = null;
            PotCrossSectionUI.Instance?.Hide();

            // Place the can upright on the nearest surface
            Quaternion uprightRot = Quaternion.Euler(0f, _held.transform.eulerAngles.y, 0f);
            var nearest = FindNearestSurfaceForHeld(_heldRb != null ? _heldRb.position : _held.transform.position);
            if (nearest != null)
            {
                var hitResult = nearest.ProjectOntoSurface(_held.transform.position, cam != null ? cam.transform.position : (Vector3?)null);
                float halfExtent = GetHeldHalfExtentAlongNormal(hitResult.surfaceNormal);
                Vector3 pos = hitResult.worldPosition + hitResult.surfaceNormal * halfExtent;
                _held.OnPlaced(nearest, false, pos, uprightRot);
            }
            else
            {
                _held.transform.rotation = uprightRot;
                _held.OnDropped();
            }
            _isPourTilted = false;
            ClearHeld();
            return;
        }

        bool lmbHeld = mouse != null && mouse.leftButton.isPressed;

        // LMB LOCK: while holding LMB and we have an engaged plant,
        // skip the plant search entirely — locked to this plant, no switching.
        WaterablePlant bestPlant = null;
        float bestDist = _wateringSnapRadius;

        if (lmbHeld && _wateringUIEngagedPlant != null
            && _wateringUIEngagedPlant.gameObject != null) // guard against destroyed plant
        {
            bestPlant = _wateringUIEngagedPlant;
            bestDist = 0f;
            // Freeze grab target at the plant so cursor can't drag the can elsewhere
            _grabTarget = bestPlant.transform.position + Vector3.up * 0.3f;
        }
        else
        {
            // Find nearest WaterablePlant
            foreach (var plant in WaterablePlant.All)
            {
                if (plant == null) continue;
                float dist = Vector3.Distance(plant.transform.position, _grabTarget);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestPlant = plant;
                }
            }
        }

        // No plant in range (and LMB not locking us) → disengage
        if (bestPlant == null)
        {
            if (wasWatering && wm != null)
                wm.EndWatering();

            // Dismiss cutaway UI when leaving snap range
            if (_wateringUIEngagedPlant != null)
            {
                PotCrossSectionUI.Instance?.Hide();
                _wateringUIEngagedPlant = null;
            }
            return;
        }

        _nearestWaterablePlant = bestPlant;

        // Show cutaway UI the moment we snap-engage with a plant
        if (bestPlant != _wateringUIEngagedPlant)
        {
            _wateringUIEngagedPlant = bestPlant;
            if (PotCrossSectionUI.Instance != null && bestPlant.definition != null)
                PotCrossSectionUI.Instance.Show(bestPlant.definition, bestPlant.WaterLevel);
        }

        // Snap the can to orbit the plant rim. Mouse left/right rotates
        // the can around the pot. Hard lock when close — the can clicks
        // into position on the rim and waits for the player to hold-click.
        Vector3 plantCenter = bestPlant.transform.position;
        float rimRadius = 0.15f; // distance from plant center to rim edge
        float rimHeight = 0.3f;  // height above plant base (spout above rim)

        // Direction from plant to cursor in XZ
        Vector3 toCursor = _grabTarget - plantCenter;
        toCursor.y = 0f;
        if (toCursor.sqrMagnitude < 0.001f)
            toCursor = Vector3.forward;
        toCursor.Normalize();

        // Pour position orbits the rim
        Vector3 pourPos = plantCenter + toCursor * rimRadius + Vector3.up * rimHeight;

        float proximity = 1f - (bestDist / _wateringSnapRadius);

        // Hard magnetic lock — snaps firmly to the rim, not a soft suggestion
        float strength;
        if (proximity > 0.4f)
            strength = Mathf.Lerp(0.5f, 1f, (proximity - 0.4f) / 0.6f); // strong ramp to full lock
        else
            strength = proximity * proximity * _wateringSnapStrength;

        _grabTarget = Vector3.Lerp(_grabTarget, pourPos, strength);

        // Tilt the watering can toward the plant for pouring.
        // Spout faces along local X — rotate so spout aims at plant, then pitch to pour.
        if (_held != null && proximity > 0.5f)
        {
            Vector3 toPlant = (plantCenter - _held.transform.position);
            toPlant.y = 0f;
            if (toPlant.sqrMagnitude < 0.001f) toPlant = Vector3.forward;
            toPlant.Normalize();

            // Yaw: face the spout toward the plant
            float yaw = Mathf.Atan2(toPlant.x, toPlant.z) * Mathf.Rad2Deg;
            float tiltAngle = Mathf.Lerp(0f, 20f, (proximity - 0.5f) / 0.5f);
            Quaternion tiltRot = Quaternion.Euler(tiltAngle, yaw, 0f);
            _held.transform.rotation = Quaternion.Slerp(_held.transform.rotation, tiltRot,
                                                         Time.deltaTime * 6f);
            _isPourTilted = true;
        }
        else if (_held != null && _isPourTilted)
        {
            // Restore upright (keep yaw, zero pitch/roll)
            Quaternion upright = Quaternion.Euler(0f, _held.transform.eulerAngles.y, 0f);
            _held.transform.rotation = Quaternion.Slerp(_held.transform.rotation,
                upright, Time.deltaTime * 8f);
            if (Quaternion.Angle(_held.transform.rotation, upright) < 1f)
            {
                _held.transform.rotation = upright;
                _isPourTilted = false;
            }
        }
    }

    /// <summary>
    /// Forgiving component search along a ray. Tries SphereCast first (wider hit area),
    /// then falls back to RaycastNonAlloc for objects the sphere might miss.
    /// </summary>
    private static T FindComponentAlongRay<T>(Ray ray, LayerMask layer) where T : Component
    {
        // 1. SphereCast — generous hit radius
        if (Physics.SphereCast(ray, PairStackRadius, out RaycastHit sphereHit, 100f, layer))
        {
            var comp = sphereHit.collider.GetComponent<T>();
            if (comp == null) comp = sphereHit.collider.GetComponentInParent<T>();
            if (comp != null) return comp;
        }

        // 2. RaycastNonAlloc — check all hits along the exact ray
        int pairHitCount = Physics.RaycastNonAlloc(ray, s_pairHitBuffer, 100f, layer);
        float bestDist = float.MaxValue;
        T best = null;
        for (int i = 0; i < pairHitCount; i++)
        {
            var comp = s_pairHitBuffer[i].collider.GetComponent<T>();
            if (comp == null) comp = s_pairHitBuffer[i].collider.GetComponentInParent<T>();
            if (comp != null && s_pairHitBuffer[i].distance < bestDist)
            {
                bestDist = s_pairHitBuffer[i].distance;
                best = comp;
            }
        }
        return best;
    }

    /// Double-click while holding a plate with children → detach the topmost child
    /// and place/drop the remaining stack.
    /// </summary>
    private bool TryUnstackHeldPlate()
    {
        var heldStack = _held.GetComponent<StackablePlate>();
        if (heldStack == null) return false;

        // Find topmost child plate
        StackablePlate topChild = null;
        foreach (Transform child in _held.transform)
        {
            var sp = child.GetComponent<StackablePlate>();
            if (sp != null)
                topChild = sp;
        }
        if (topChild == null) return false;

        var topPlaceable = topChild.GetComponent<PlaceableObject>();
        if (topPlaceable == null) return false;

        // Detach the top plate
        topChild.PrepareForGrab();

        // Drop the current stack onto the nearest surface
        _heldRb.constraints = _originalConstraints;
        _heldRb.linearVelocity = Vector3.zero;
        _heldRb.useGravity = true;
        _held.OnDropped();

        // Pick up the detached top plate via InitGrab (full setup: bounds, shadow, ghost, etc.)
        _held = topPlaceable;
        _pickupTimer = PickupFeelDuration;
        InitGrab();

        Debug.Log($"[ObjectGrabber] Unstacked {topChild.name}");
        return true;
    }

    /// <summary>
    /// Try to deposit the held item at the nearest DropZone when not over a PlacementSurface.
    /// Used for fridge, trash can, etc. that don't have placement surfaces.
    /// </summary>
    private bool TryDepositAtNearbyZone()
    {
        if (_held == null) return false;

        // Raycast to see what we clicked on
        Vector2 screenPos = IrisInput.CursorPosition;
        Ray ray = ScreenRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f)) return false;

        // Check the hit object and its parents for a DropZone
        var zone = hit.collider.GetComponent<DropZone>();
        if (zone == null) zone = hit.collider.GetComponentInParent<DropZone>();
        if (zone == null) return false;

        // Check if the held item matches this zone (name match or category match)
        bool matches = (!string.IsNullOrEmpty(_held.HomeZoneName) && zone.ZoneName == _held.HomeZoneName)
                    || (!string.IsNullOrEmpty(_held.AltHomeZoneName) && zone.ZoneName == _held.AltHomeZoneName);
        // Category-specific destroy zones: trash→TrashCan, dishes→Sink
        if (!matches && zone.DestroyOnDeposit)
        {
            if (_held.Category == ItemCategory.Trash && zone.ZoneName == "TrashCan")
                matches = true;
            else if (_held.Category == ItemCategory.Dish && zone.ZoneName == "Sink")
                matches = true;
        }
        if (!matches) return false;

        // Slotted zone (shoe rack) — snap to nearest free slot
        if (zone.UseSlotting && zone.TryGetNearestFreeSlot(_grabTarget, out var slotPos, out var slotRot))
        {
            var heldPairable = _held.GetComponent<PairableItem>();
            if (heldPairable != null || zone.PreserveItemRotation)
                slotRot = _held.transform.rotation;
            var surface = zone.GetComponent<PlacementSurface>()
                ?? zone.GetComponentInParent<PlacementSurface>();
            _held.OnPlaced(surface, true, slotPos, slotRot);
            LockRigidbodyAtSlot(_held);
            zone.ClaimSlotFor(_held);
            zone.RegisterDeposit(_held);
            OnObjectPlaced?.Invoke(_held);
            PlayPlaceSFX(_held);
            Debug.Log($"[ObjectGrabber] Slotted {_held.name} at zone '{zone.ZoneName}'");
            ClearHeld();
            return true;
        }

        // Non-slotted deposit (trash can, sink, etc.)
        _held.ForceRestoreMaterial();
        _held.ForceDestroySilhouette();
        var item = _held;
        ClearHeld();
        zone.RegisterDeposit(item);
        PlayPlaceSFX(item);
        Debug.Log($"[ObjectGrabber] Deposited {item.name} at zone '{zone.ZoneName}'");
        return true;
    }

    private void PlayPlaceSFX(PlaceableObject obj)
    {
        var clip = obj != null && obj.PlaceSFXOverride != null ? obj.PlaceSFXOverride : _placeSFX;
        AudioManager.Instance?.PlaySFX(clip);

        // Surface reaction sound (layered on top of the main placement SFX)
        if (_currentSurface != null)
        {
            var reaction = GetSurfaceReactionClip(_currentSurface.Material);
            if (reaction != null)
                AudioManager.Instance?.PlaySFX(reaction, 0.5f);
        }

        // Camera nudge — small positional shake for juicy placement feedback
        CameraNudge.Nudge(0.3f);
    }

    private AudioClip GetSurfaceReactionClip(PlacementSurface.SurfaceMaterial mat)
    {
        return mat switch
        {
            PlacementSurface.SurfaceMaterial.Wood    => _surfaceWood,
            PlacementSurface.SurfaceMaterial.Glass   => _surfaceGlass,
            PlacementSurface.SurfaceMaterial.Metal   => _surfaceMetal,
            PlacementSurface.SurfaceMaterial.Stone   => _surfaceStone,
            PlacementSurface.SurfaceMaterial.Fabric  => _surfaceFabric,
            PlacementSurface.SurfaceMaterial.Plastic => _surfacePlastic,
            _ => null
        };
    }

    private void ClearHeld()
    {
        // Restore discrete collision detection
        if (_heldRb != null)
            _heldRb.collisionDetectionMode = CollisionDetectionMode.Discrete;

        if (_held != null)
        {
            // Restore normal rendering (undo HeldOnTop shader swap from pickup)
            _held.SetRenderOnTop(false);

            // Always clean up held item visuals
            _held.ForceRestoreMaterial();
            _held.ForceDestroySilhouette();

            // Stop partner highlight flash + restore partner color
            var pairable = _held.GetComponent<PairableItem>();
            if (pairable != null) pairable.OnPutDown();

            // Clear any lingering highlight
            var hl = _held.GetComponent<InteractableHighlight>();
            if (hl != null)
            {
                hl.SetHighlighted(false);
                hl.SetInteractHighlighted(false);
            }

            // Release any books stacked above on drop
            var dropBook = _held.GetComponent<BookItem>();
            if (dropBook != null && dropBook.HasStackedBooks)
                dropBook.ReleaseBooksAbove(null, _held.transform.position, _held.transform.rotation);
        }

        _held = null;
        _heldRb = null;
        _currentSurface = null;
        _lastValidSurface = null;
        _isOnWall = false;
        _pairSnapTarget = null;

        // Dismiss watering cutaway UI if it was showing
        if (_wateringUIEngagedPlant != null)
        {
            PotCrossSectionUI.Instance?.Hide();
            _wateringUIEngagedPlant = null;
        }

        // Clear glass magnetize highlight
        if (_magnetizedGlass != null)
        {
            SetGlassMagnetHighlight(_magnetizedGlass, false);
            _magnetizedGlass = null;
        }

        _isPourTilted = false;
        _nearestGunplaBody = null;

        ShowShadow(false);
        DestroyGhostPreview();
        ClearPlantHighlights();

        // Reset proximity tracking (lid stays open — player closes manually)
        _turntableLidOpen = false;

        // Clear turntable highlight if we were holding a record
        if (RecordSlot.Instance != null)
        {
            var slotHL = RecordSlot.Instance.GetComponent<InteractableHighlight>();
            if (slotHL != null) slotHL.SetHighlighted(false);
            InteractableHighlight.SuppressVisuals = true;
        }

        // Safety: unlock cursor in case an interaction lock was active
        GlobalCursorManager.UnlockCursor();

        if (PickupDescriptionHUD.Instance != null)
            PickupDescriptionHUD.Instance.Hide();
    }

    // ── Target highlight blink (generic + plant-specific) ────────────

    /// <summary>Blink a single object's InteractableHighlight N times.</summary>
    private static System.Collections.IEnumerator BlinkHighlight(GameObject target, int blinks)
    {
        if (target == null) yield break;
        var hl = target.GetComponent<InteractableHighlight>();
        if (hl == null) hl = target.AddComponent<InteractableHighlight>();

        for (int b = 0; b < blinks; b++)
        {
            hl.SetHighlighted(true);
            yield return new WaitForSeconds(0.25f);
            hl.SetHighlighted(false);
            yield return new WaitForSeconds(0.2f);
        }
    }


    /// <summary>Turn plant highlights on or off (steady, same style as shoe partner highlight).</summary>
    private void SetPlantHighlights(bool on)
    {
        // Unsuppress so the highlight shader actually renders
        InteractableHighlight.SuppressVisuals = !on;

        var plants = WaterablePlant.All;
        for (int i = 0; i < plants.Count; i++)
        {
            if (plants[i] == null) continue;
            var hl = plants[i].GetComponent<InteractableHighlight>();
            if (hl == null && on)
                hl = plants[i].gameObject.AddComponent<InteractableHighlight>();
            if (hl != null) hl.SetHighlighted(on);
        }
    }

    private void ClearPlantHighlights()
    {
        SetPlantHighlights(false);
    }

    /// <summary>Extract vinyl from a sleeve and grab it. Returns true if successful.</summary>
    private bool TryExtractAndGrab(AlbumSleeve sleeve)
    {
        if (sleeve == null) return false;
        var vinylPlaceable = sleeve.ExtractVinyl();
        if (vinylPlaceable == null) return false;
        ConsumeClick();
        _held = vinylPlaceable;
        _pickupTimer = PickupFeelDuration;
        InitGrab();
        return true;
    }

    private void SetGlassMagnetHighlight(DrinkGlass glass, bool on)
    {
        if (glass == null) return;
        if (on) InteractableHighlight.SuppressVisuals = false;
        var hl = glass.GetComponent<InteractableHighlight>();
        if (hl == null && on)
            hl = glass.gameObject.AddComponent<InteractableHighlight>();
        if (hl != null) hl.SetHighlighted(on);
    }

    // ── Grab target (surface raycast with depth-plane fallback) ──────

    private void UpdateGrabTarget()
    {
        Vector2 screenPos = IrisInput.CursorPosition;
        Ray ray = ScreenRay(screenPos);

        bool foundSurface = false;

        // RaycastNonAlloc so the ray can pass through nearer surfaces to reach
        // ones further away (e.g. coffee table in living room from kitchen camera).
        int hitCount = Physics.RaycastNonAlloc(ray, s_surfaceHitBuffer, 100f, surfaceLayer);
        if (hitCount == 0 && _held != null)
            Debug.Log($"[UpdateGrabTarget] No surface hits at all. surfaceLayer={surfaceLayer.value}");
        if (hitCount > 0)
        {
            // Sort nearest-first so we try the camera-closest surface first
            System.Array.Sort(s_surfaceHitBuffer, 0, hitCount, s_surfaceHitComparer);

            int occlusionMask = _wallLayer | _barrierLayer;
            Vector3 camPos = cam.transform.position;

            for (int h = 0; h < hitCount; h++)
            {
                var surface = s_surfaceHitBuffer[h].collider.GetComponentInParent<PlacementSurface>();
                bool surfaceValid = surface != null
                    && (!surface.IsVertical || _held.CanWallMount)
                    && (surface.IsVertical || !_held.WallOnly)
                    && (FridgeController.Instance == null || !FridgeController.Instance.IsShelfAndClosed(surface))
                    && !IsCubbyAndClosed(surface);
                if (!surfaceValid)
                {
                    Debug.Log($"[UpdateGrabTarget] Skipped {s_surfaceHitBuffer[h].collider.name}: surface={surface?.name}, isVertical={surface?.IsVertical}, canWallMount={_held?.CanWallMount}, wallOnly={_held?.WallOnly}");
                    continue;
                }

                // Project onto the actual surface face to get the real placement point
                var hitResult = surface.ProjectOntoSurface(s_surfaceHitBuffer[h].point, camPos);
                Vector3 projectedPos = hitResult.worldPosition;

                // Occlusion check: reject surfaces whose placement point is
                // hidden behind solid geometry (counter, bookshelf, etc.).
                // Skip for floor surfaces — floor placement should never be
                // blocked by furniture the camera looks through.
                if (!surface.IsFloor)
                {
                    Vector3 toProj = projectedPos - camPos;
                    float projDist = toProj.magnitude;
                    if (projDist > 0.1f)
                    {
                        Vector3 projDir = toProj / projDist;
                        if (Physics.Raycast(camPos, projDir, out RaycastHit occHit,
                                projDist - 0.05f, occlusionMask))
                        {
                            // Allow if the blocker is the surface's own furniture
                            bool sameFurniture = occHit.transform.IsChildOf(surface.transform)
                                              || surface.transform.IsChildOf(occHit.transform);
                            // Allow if blocker shares a parent with the surface
                            // (e.g. barrier box inside a counter whose top is the surface)
                            if (!sameFurniture && surface.transform.parent != null
                                && occHit.transform.parent == surface.transform.parent)
                                sameFurniture = true;
                            // Allow if the hit is very close to the surface — it's
                            // the surface's own supporting geometry (floor slab,
                            // table top mesh, etc.), not a separate occluder.
                            bool nearSurface = Vector3.Distance(occHit.point, projectedPos) < 0.15f;
                            if (!sameFurniture && !nearSurface) continue; // truly occluded — skip
                        }
                    }
                }

                // This surface is valid and visible — use it
                _currentSurface = surface;
                _lastValidSurface = surface;
                _isOnWall = surface.IsVertical;

                Vector3 pos = surface.SnapToGrid(hitResult.worldPosition, GetGridSize(surface), camPos);

                float halfExtent = GetHeldHalfExtentAlongNormal(hitResult.surfaceNormal);
                pos += hitResult.surfaceNormal * halfExtent;

                // Heavy slot snap — if the surface has a slotted DropZone the
                // held item belongs to, override pos with the next free slot
                // and teleport the rigidbody so the shoe physically clicks in.
                var slotZone = surface.GetComponent<DropZone>();
                if (slotZone == null) slotZone = surface.GetComponentInParent<DropZone>();
                if (slotZone != null && slotZone.UseSlotting && _held != null
                    && (slotZone.ZoneName == _held.HomeZoneName || slotZone.ZoneName == _held.AltHomeZoneName)
                    && slotZone.TryPeekNearestSlot(pos, out var slotPos, out _))
                {
                    pos = slotPos;
                    if (_heldRb != null)
                    {
                        _heldRb.position = pos;
                        _heldRb.linearVelocity = Vector3.zero;
                        _heldRb.angularVelocity = Vector3.zero;
                    }
                }

                _grabTarget = pos;

                // Track depth so fallback stays smooth if cursor leaves surface
                _fallbackDepth = Vector3.Dot(
                    _grabTarget - camPos,
                    cam.transform.forward);

                if (_isOnWall)
                {
                    Vector3 wn = hitResult.surfaceNormal;
                    if (_held.CanWallMount && Vector3.Dot(_heldOriginalForward, wn) < 0f)
                        wn = -wn;
                    _held.AlignToWall(wn, _wallRotation);
                }

                foundSurface = true;
                break; // first valid, non-occluded surface wins
            }
        }

        if (!foundSurface)
        {
            // No valid surface or dollying — float at fallback depth following cursor
            _currentSurface = null;
            _isOnWall = false;

            Vector3 planePoint = cam.transform.position + cam.transform.forward * _fallbackDepth;
            var plane = new Plane(-cam.transform.forward, planePoint);
            if (plane.Raycast(ray, out float enter))
                _grabTarget = ray.GetPoint(enter);
        }

        // Wall clamping removed — felt too harsh. Surface bounds still constrain placement.

        // Pair snap: if holding a pairable item near its partner, override
        // _grabTarget to magnetically lock onto the pair position.
        UpdatePairSnap();

        // Trash can snap: soft pull toward lid when carrying trash nearby.
        UpdateTrashCanSnap();

        // Turntable snap: soft pull toward platter when carrying vinyl nearby.
        UpdateTurntableSnap();

        // Watering can snap: pull toward nearest plant when carrying the can.
        UpdateWateringCanSnap();

        // Bottle snap: pull toward nearest drink glass when carrying a bottle.
        UpdateBottleSnap();

        // Gunpla snap: pull held part toward body when nearby.
        UpdateGunplaSnap();
    }

    // ── Gunpla part magnetic snap ────────────────────────────────────

    private GunplaFigure _nearestGunplaBody;

    private void UpdateGunplaSnap()
    {
        _nearestGunplaBody = null;
        if (_held == null) return;

        var part = _held.GetComponent<GunplaPart>();
        if (part == null || part.IsAttached) return;

        // Find nearest GunplaBody that can accept this part
        GunplaFigure bestBody = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < GunplaFigure.All.Count; i++)
        {
            var body = GunplaFigure.All[i];
            if (body == null || !body.CanAccept(part)) continue;

            float dist = Vector3.Distance(body.transform.position, _grabTarget);
            if (dist < body.SnapRadius && dist < bestDist)
            {
                bestDist = dist;
                bestBody = body;
            }
        }

        if (bestBody == null) return;

        _nearestGunplaBody = bestBody;

        // Magnetic pull toward the attach point
        Vector3 attachPos = bestBody.GetAttachPosition(part);
        float proximity = 1f - (bestDist / bestBody.SnapRadius);
        float strength = proximity * proximity * 0.9f;
        _grabTarget = Vector3.Lerp(_grabTarget, attachPos, strength);
    }

    /// <summary>
    /// Clamps _grabTarget so the held item cannot pass through walls and always
    /// stays on the camera's side of any geometry (no wall between camera and item).
    /// </summary>
    private void ClampGrabTargetToWalls()
    {
        if (_heldRb == null) return;
        // Only clamp against explicit barrier cubes — not general wall geometry,
        // which catches the entire room and makes carrying feel sticky.
        if (_barrierLayer == 0) return;

        const float margin = 0.05f;
        int blockMask = _barrierLayer;

        // 1) Camera → target: keep item on camera's side of any wall/barrier
        //    (skip when on a surface to allow cross-room placement)
        if (_currentSurface == null)
        {
            Vector3 camPos = cam.transform.position;
            Vector3 camToTarget = _grabTarget - camPos;
            float camDist = camToTarget.magnitude;

            if (camDist > 0.001f &&
                Physics.Raycast(camPos, camToTarget.normalized, out RaycastHit camHit, camDist, blockMask))
            {
                float safeDist = Mathf.Max(0f, camHit.distance - margin);
                _grabTarget = camPos + camToTarget.normalized * safeDist;
            }
        }

        // 2) Current position → target: prevent tunneling through walls/barriers
        //    (always active — even when snapped to a surface)
        Vector3 from = _heldRb.position;
        Vector3 delta = _grabTarget - from;
        float dist = delta.magnitude;

        if (dist > 0.001f &&
            Physics.Raycast(from, delta.normalized, out RaycastHit wallHit, dist, blockMask))
        {
            float safeDist = Mathf.Max(0f, wallHit.distance - margin);
            _grabTarget = from + delta.normalized * safeDist;
        }
    }

    // ── Rotate input ────────────────────────────────────────────────
    // RMB = rotate around the item's configured RotateAxis (per-item X, Y, or Z)

    private void UpdateScrollInput()
    {
        if (_held == null) return;

        var mouse = UnityEngine.InputSystem.Mouse.current;
        bool rotatePressed = mouse != null && mouse.rightButton.wasPressedThisFrame;
        if (!rotatePressed) return;

        Vector3 axis = _held.RotateAxis;
        if (axis.sqrMagnitude < 0.001f) return; // rotation locked for this item

        float angle = scrollRotateStep;
        if (_isOnWall && Mathf.Abs(axis.y) > 0.5f)
            _wallRotation += angle;
        else
            _held.transform.RotateAround(_held.RotatePivotWorld, axis, angle);

        // Rotate tick feel
        if (_squishCoroutine != null) StopCoroutine(_squishCoroutine);
        _squishCoroutine = StartCoroutine(SquishPop(_held.transform, 1.06f, 1.0f, 0.08f));
    }

    // ── Helper: half-extent along a normal ────────────────────────────

    private const float PlacementSafetyMargin = 0.02f;

    private float GetHeldHalfExtentAlongNormal(Vector3 normal)
    {
        // Distance from pivot to the bottom of the collider along the surface normal.
        // extentDot = half-height of collider along normal
        // centerDot = how far the collider center is from the pivot along normal
        // Subtracting gives the distance from pivot to the collider's bottom edge.
        Vector3 n = normal.normalized;
        float extentDot = Mathf.Abs(Vector3.Dot(_heldBoundsExtents, n));
        float centerDot = Vector3.Dot(_heldBoundsCenterOffset, n);
        return Mathf.Max(extentDot - centerDot, 0.01f) + PlacementSafetyMargin;
    }

    // ── Smart book placement ─────────────────────────────────────────

    /// <summary>
    /// For books with smart placement, determines upright vs flat orientation
    /// and adjusts position for flat-book stacking. Returns false if not applicable.
    /// </summary>
    private bool TryComputeBookOrientation(
        PlacementSurface surface, Vector3 snappedPos, Quaternion baseRot,
        out Quaternion bookRot, out Vector3 adjustedPos, out bool isFlat)
    {
        bookRot = baseRot;
        adjustedPos = snappedPos;
        isFlat = false;

        if (_held == null || surface == null || surface.IsVertical) return false;
        var book = _held.GetComponent<BookItem>();
        if (book == null || !book.UseSmartPlacement) return false;

        float yaw = baseRot.eulerAngles.y;
        bookRot = Quaternion.Euler(book.FlatOffset) * Quaternion.Euler(0f, yaw, 0f);
        isFlat = true;

        // Stack on top of existing flat books at this cell
        float stackY = GetFlatBookStackTop(surface, snappedPos);
        if (stackY > adjustedPos.y)
            adjustedPos.y = stackY;

        return true;
    }

    private bool HasAdjacentUprightBook(PlacementSurface surface, Vector3 worldPos)
    {
        float searchRadius = gridSize * 1.5f;
        for (int i = 0; i < PlaceableObject.All.Count; i++)
        {
            var po = PlaceableObject.All[i];
            if (po == null || po == _held) continue;
            if (po.CurrentState == PlaceableObject.State.Held) continue;
            if (po.LastPlacedSurface != surface) continue;

            var otherBook = po.GetComponent<BookItem>();
            if (otherBook == null || !otherBook.UseSmartPlacement) continue;
            if (!otherBook.IsUpright) continue;

            if (Vector3.Distance(worldPos, po.transform.position) < searchRadius)
                return true;
        }
        return false;
    }

    private float GetFlatBookStackTop(PlacementSurface surface, Vector3 worldPos)
    {
        float topY = worldPos.y;
        float cellRadius = gridSize * 0.5f;

        for (int i = 0; i < PlaceableObject.All.Count; i++)
        {
            var po = PlaceableObject.All[i];
            if (po == null || po == _held) continue;
            if (po.CurrentState == PlaceableObject.State.Held) continue;
            if (po.LastPlacedSurface != surface) continue;

            var otherBook = po.GetComponent<BookItem>();
            if (otherBook == null || !otherBook.UseSmartPlacement) continue;
            if (!otherBook.IsPlacedFlat) continue;

            float dx = Mathf.Abs(worldPos.x - po.transform.position.x);
            float dz = Mathf.Abs(worldPos.z - po.transform.position.z);
            if (dx > cellRadius || dz > cellRadius) continue;

            var col = po.GetComponent<Collider>()
                   ?? po.GetComponentInChildren<Collider>();
            if (col != null)
            {
                float bookTop = col.bounds.max.y;
                if (bookTop > topY)
                    topY = bookTop;
            }
        }

        if (topY > worldPos.y)
            topY += _heldBoundsExtents.y;

        return topY;
    }

    /// <summary>
    /// Lock a slot-deposited item (and any paired children) so it can't fall
    /// out of the slot or get bumped around. Reverted on next pickup via
    /// PlaceableObject.OnPickedUp, which sets isKinematic = false.
    /// </summary>
    private static void LockRigidbodyAtSlot(PlaceableObject po)
    {
        if (po == null) return;

        var rb = po.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // Paired partner is parented under this transform — lock it too.
        var childRbs = po.GetComponentsInChildren<Rigidbody>();
        for (int i = 0; i < childRbs.Length; i++)
        {
            if (childRbs[i] == rb) continue;
            childRbs[i].linearVelocity = Vector3.zero;
            childRbs[i].angularVelocity = Vector3.zero;
            childRbs[i].isKinematic = true;
            childRbs[i].useGravity = false;
        }
    }

    // Buffer reused by FindOccupant — keeps the helper allocation-free.
    private static readonly Collider[] s_occupancyBuffer = new Collider[16];

    /// <summary>
    /// Returns a PlaceableObject occupying the cell at <paramref name="worldPos"/>
    /// for the held item, or null if the cell is free. Excludes the held item itself
    /// and any of its children. Used for placement gating and shadow color.
    /// </summary>
    private PlaceableObject FindOccupant(Vector3 worldPos)
    {
        if (_held == null) return null;

        // For wall items: use the tangent-plane extents (X and Y in wall space)
        // which are larger than the thin Z depth. This prevents wall items from
        // overlapping each other on the same wall.
        float radius;
        if (_isOnWall)
        {
            radius = Mathf.Max(_heldBoundsExtents.x, _heldBoundsExtents.y) * 0.8f;
            if (radius < 0.04f) radius = 0.04f;
        }
        else
        {
            radius = Mathf.Max(_heldBoundsExtents.x, _heldBoundsExtents.z) * 0.85f;
            if (radius < 0.02f) radius = 0.02f;
        }

        int count = Physics.OverlapSphereNonAlloc(worldPos, radius, s_occupancyBuffer, placeableLayer, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var po = s_occupancyBuffer[i].GetComponentInParent<PlaceableObject>();
            if (po == null) continue;
            if (po == _held) continue;
            if (po.transform.IsChildOf(_held.transform)) continue;
            return po;
        }
        return null;
    }

    // ── Shadow preview ──────────────────────────────────────────────

    private void BuildShadow()
    {
        _shadowGO = new GameObject("PlacementShadow");
        _shadowGO.transform.SetParent(transform);

        var mf = _shadowGO.AddComponent<MeshFilter>();
        _shadowRenderer = _shadowGO.AddComponent<MeshRenderer>();

        var mesh = new Mesh { name = "ShadowQuad" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f),
            new Vector3( 0.5f, 0f, -0.5f),
            new Vector3( 0.5f, 0f,  0.5f),
            new Vector3(-0.5f, 0f,  0.5f)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(1f, 1f), new Vector2(0f, 1f)
        };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateNormals();
        mf.sharedMesh = mesh;

        int texSize = 32;
        var tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        float center = texSize * 0.5f;
        float radius = center - 1f;
        for (int y = 0; y < texSize; y++)
        {
            for (int x = 0; x < texSize; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float a = Mathf.Clamp01((radius - dist) / 2f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();

        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Transparent");
        _shadowMat = new Material(shader);
        _shadowMat.mainTexture = tex;
        var cfgInit = PlacementPreviewSettings.Instance;
        _shadowMat.color = cfgInit != null ? cfgInit.shadowValidColor : new Color(0.25f, 0.9f, 0.3f, 0.75f);
        _shadowMat.SetFloat("_Surface", 1f);
        _shadowMat.SetFloat("_Blend", 0f);
        _shadowMat.SetInt("_ZTest", (int)CompareFunction.Always);
        _shadowMat.renderQueue = 3900;
        _shadowRenderer.sharedMaterial = _shadowMat;
        _shadowRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _shadowRenderer.receiveShadows = false;

        _shadowGO.SetActive(false);
    }

    private void ShowShadow(bool show)
    {
        var cfg = PlacementPreviewSettings.Instance;
        if (cfg != null && !cfg.showShadow) show = false;
        if (_shadowGO != null)
            _shadowGO.SetActive(show);
    }

    private Color GetShadowColor(bool valid)
    {
        var cfg = PlacementPreviewSettings.Instance;
        if (cfg != null)
            return valid ? cfg.shadowValidColor : cfg.shadowInvalidColor;
        return valid ? new Color(0.25f, 0.9f, 0.3f, 0.75f) : new Color(0.95f, 0.18f, 0.2f, 0.75f);
    }

    // ── Squish-pop feel ──────────────────────────────────────────
    private System.Collections.IEnumerator SquishPop(Transform target, float squish, float pop, float duration = 0.15f)
    {
        if (target == null) yield break;
        Vector3 baseScale = target.localScale;
        float seg = duration / 3f;

        // Squish
        for (float t = 0f; t < seg; t += Time.deltaTime)
        {
            if (target == null) yield break;
            target.localScale = Vector3.Lerp(baseScale, baseScale * squish, t / seg);
            yield return null;
        }
        // Pop
        for (float t = 0f; t < seg; t += Time.deltaTime)
        {
            if (target == null) yield break;
            target.localScale = Vector3.Lerp(baseScale * squish, baseScale * pop, t / seg);
            yield return null;
        }
        // Settle
        for (float t = 0f; t < seg; t += Time.deltaTime)
        {
            if (target == null) yield break;
            target.localScale = Vector3.Lerp(baseScale * pop, baseScale, t / seg);
            yield return null;
        }
        if (target != null) target.localScale = baseScale;
        _squishCoroutine = null;
    }

    // ── Ghost preview ──────────────────────────────────────────────

    /// <summary>
    /// Build a translucent clone of the held item, reparented under a ghost
    /// root so we can move it to the grid-snapped position each frame. The
    /// player sees exactly where the real item will land without breaking the
    /// smooth carry motion of the held item itself.
    /// </summary>
    private void BuildGhostPreview(PlaceableObject source)
    {
        var cfg = PlacementPreviewSettings.Instance;
        if (cfg == null || !cfg.showGhost) return;
        if (source == null) return;
        DestroyGhostPreview();

        _ghostPreviewGO = Instantiate(source.gameObject);
        _ghostPreviewGO.name = $"GhostPreview_{source.name}";
        _ghostPreviewGO.transform.localScale = source.transform.lossyScale;

        // Strip everything that isn't a pure visual (transform + mesh render).
        foreach (var po in _ghostPreviewGO.GetComponentsInChildren<PlaceableObject>(true))
            DestroyImmediate(po);
        foreach (var pi in _ghostPreviewGO.GetComponentsInChildren<PairableItem>(true))
            DestroyImmediate(pi);
        foreach (var ld in _ghostPreviewGO.GetComponentsInChildren<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>(true))
            DestroyImmediate(ld);
        foreach (var light in _ghostPreviewGO.GetComponentsInChildren<Light>(true))
            DestroyImmediate(light);

        var comps = _ghostPreviewGO.GetComponentsInChildren<Component>(includeInactive: true);
        for (int i = 0; i < comps.Length; i++)
        {
            var c = comps[i];
            if (c == null) continue;
            if (c is Transform) continue;
            if (c is MeshFilter) continue;
            if (c is MeshRenderer) continue;
            Destroy(c);
        }

        int ignoreLayer = 2;
        foreach (Transform t in _ghostPreviewGO.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = ignoreLayer;

        Color validColor = cfg != null ? cfg.ghostValidColor : new Color(0.5f, 0.95f, 0.55f, 0.35f);
        bool outline = cfg != null && cfg.ghostStyle == PlacementPreviewSettings.GhostStyle.Outline;
        float thickness = cfg != null ? cfg.outlineThickness : 0.005f;

        _ghostPreviewMats.Clear();
        var renderers = _ghostPreviewGO.GetComponentsInChildren<MeshRenderer>(includeInactive: true);

        if (outline)
        {
            var outlineShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (outlineShader == null) outlineShader = Shader.Find("Unlit/Color");
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    var mesh = Instantiate(mf.sharedMesh);
                    var verts = mesh.vertices;
                    var norms = mesh.normals;
                    for (int v = 0; v < verts.Length; v++)
                        verts[v] += norms[v] * thickness;
                    mesh.vertices = verts;
                    mesh.RecalculateBounds();
                    mf.sharedMesh = mesh;
                }
                var mat = new Material(outlineShader);
                mat.color = validColor;
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_Cull", 1f);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = 3500;
                // Replace ALL material slots with the ghost material
                var mats = new Material[r.sharedMaterials.Length];
                for (int m = 0; m < mats.Length; m++) mats[m] = mat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                _ghostPreviewMats.Add(mat);
            }
        }
        else
        {
            var ghostShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (ghostShader == null) ghostShader = Shader.Find("Sprites/Default");
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                var mat = new Material(ghostShader);
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = 3500;
                mat.color = validColor;
                // Replace ALL material slots with the ghost material
                var mats = new Material[r.sharedMaterials.Length];
                for (int m = 0; m < mats.Length; m++) mats[m] = mat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                _ghostPreviewMats.Add(mat);
            }
        }

        _ghostPreviewGO.SetActive(false);
    }

    /// <summary>Position the ghost at the currently-planned placement pose.
    /// When visible, tints green (valid) or red (can't fit / blocked).</summary>
    private void UpdateGhostPreview(Vector3 worldPos, Quaternion worldRot, bool show, bool valid = true)
    {
        if (_ghostPreviewGO == null) return;
        if (show)
        {
            _ghostPreviewGO.transform.SetPositionAndRotation(worldPos, worldRot);
            if (!_ghostPreviewGO.activeSelf)
                _ghostPreviewGO.SetActive(true);

            var cfg = PlacementPreviewSettings.Instance;
            Color validColor = cfg != null ? cfg.ghostValidColor : new Color(0.5f, 0.95f, 0.55f, 0.35f);
            Color invalidColor = cfg != null ? cfg.ghostInvalidColor : new Color(0.95f, 0.2f, 0.2f, 0.35f);
            Color tint = valid ? validColor : invalidColor;
            for (int i = 0; i < _ghostPreviewMats.Count; i++)
                if (_ghostPreviewMats[i] != null) _ghostPreviewMats[i].color = tint;
        }
        else if (_ghostPreviewGO.activeSelf)
        {
            _ghostPreviewGO.SetActive(false);
        }
    }

    private void DestroyGhostPreview()
    {
        for (int i = 0; i < _ghostPreviewMats.Count; i++)
            if (_ghostPreviewMats[i] != null) Destroy(_ghostPreviewMats[i]);
        _ghostPreviewMats.Clear();

        if (_ghostPreviewGO != null)
        {
            Destroy(_ghostPreviewGO);
            _ghostPreviewGO = null;
        }
    }

    private void UpdateShadow()
    {
        if (_shadowGO == null || _heldRb == null) return;

        // Hide shadow and ghost while magnetically snapped to an interaction
        // target (bottle→glass, watering can→plant, shoe→partner) — the snap
        // itself is the visual feedback, not the placement preview
        if (_nearestDrinkGlass != null || _nearestWaterablePlant != null || _pairSnapTarget != null)
        {
            _shadowGO.SetActive(false);
            UpdateGhostPreview(Vector3.zero, Quaternion.identity, show: false);
            return;
        }

        // Only show shadow when hovering a valid placement surface
        if (_currentSurface == null)
        {
            _shadowGO.SetActive(false);
            UpdateGhostPreview(Vector3.zero, Quaternion.identity, show: false);
            return;
        }

        _shadowGO.SetActive(true);

        var hitResult = _currentSurface.ProjectOntoSurface(_grabTarget, cam.transform.position);

        // Slot-zone fast path: if the surface has a slotted DropZone the held
        // item belongs to, the shadow follows the slot — not the cursor — and
        // is green whenever the rack has any free slot. The actual click goes
        // through the auto-slot deposit, which doesn't care about grid cells.
        var slotZone = _currentSurface.GetComponent<DropZone>();
        if (slotZone == null) slotZone = _currentSurface.GetComponentInParent<DropZone>();
        bool isSlotZoneMatch = slotZone != null && slotZone.UseSlotting && _held != null
            && (slotZone.ZoneName == _held.HomeZoneName || slotZone.ZoneName == _held.AltHomeZoneName);

        if (isSlotZoneMatch)
        {
            bool slotFree = slotZone.TryPeekNearestSlot(hitResult.worldPosition, out Vector3 slotPos, out Quaternion slotRot);
            Vector3 slotShadow = slotFree
                ? slotPos + hitResult.surfaceNormal * 0.02f
                : hitResult.worldPosition + hitResult.surfaceNormal * 0.02f;

            _shadowMat.color = GetShadowColor(slotFree);
            _shadowGO.transform.position = slotShadow;
            _shadowGO.transform.rotation = Quaternion.FromToRotation(Vector3.up, hitResult.surfaceNormal);
            _shadowGO.transform.localScale = new Vector3(_heldShadowDiameter, 1f, _heldShadowDiameter);

            // Ghost preview follows the slot pose — red if rack is full.
            // Preserve held rotation when the zone allows it.
            Quaternion ghostRot = slotFree
                ? (slotZone.PreserveItemRotation ? _held.transform.rotation : slotRot)
                : Quaternion.identity;
            UpdateGhostPreview(slotFree ? slotPos : hitResult.worldPosition,
                               ghostRot,
                               show: true, valid: slotFree);
            return;
        }

        // Snap the preview to the same grid cell Place() will use, so what you
        // see is exactly what you'll get.
        Vector3 snapped = _currentSurface.SnapToGrid(hitResult.worldPosition, GetGridSize(_currentSurface), cam.transform.position);
        float halfExtent = GetHeldHalfExtentAlongNormal(hitResult.surfaceNormal);
        Vector3 placePos = snapped + hitResult.surfaceNormal * halfExtent;

        Quaternion placeRot = _currentSurface.IsVertical
            ? Quaternion.LookRotation(hitResult.surfaceNormal, Vector3.up)
              * Quaternion.AngleAxis(_wallRotation, Vector3.forward)
            : _held.transform.rotation;

        // Mirror the home-snap override from Place() so ghost matches exactly
        if (_held.HasHome && Vector3.Distance(placePos, _held.HomePosition) < _held.HomeTolerance)
        {
            placePos = _held.HomePosition;
            var pairable = _held.GetComponent<PairableItem>();
            if (pairable == null || !pairable.IsPaired)
                placeRot = _held.HomeRotation;
        }

        // Shadow always sits directly under the ghost on the surface face.
        // Derived from placePos so any override (home snap, etc.) keeps them aligned.
        // Shadow sits just above the surface face to avoid z-fighting with the floor
        Vector3 shadowPos = hitResult.worldPosition + hitResult.surfaceNormal * 0.02f;
        Quaternion shadowRot = Quaternion.FromToRotation(Vector3.up, hitResult.surfaceNormal);

        bool canPlace = (!_currentSurface.IsVertical || _held.CanWallMount)
            && (_currentSurface.IsVertical || !_held.WallOnly);

        // Trash items only show valid on floor surfaces and trash cans
        if (canPlace && _held.Category == ItemCategory.Trash)
        {
            var zone = _currentSurface.GetComponent<DropZone>();
            bool isTrashCan = zone != null && zone.DestroyOnDeposit;
            canPlace = _currentSurface.IsFloor || isTrashCan;
        }

        // Barrier: blocked if placement overlaps barrier geometry.
        // Skip for wall surfaces — the wall itself is on the barrier layer.
        if (canPlace && _barrierLayer != 0 && !_currentSurface.IsVertical)
        {
            if (Physics.CheckSphere(placePos, 0.009f, _barrierLayer))
                canPlace = false;
        }

        // Exclusion zones (no-place areas without physical colliders)
        if (canPlace && PlacementExclusionZone.IsExcluded(placePos))
            canPlace = false;

        // Fridge shelves blocked when doors are closed
        if (canPlace && FridgeController.Instance != null
            && FridgeController.Instance.IsShelfAndClosed(_currentSurface))
            canPlace = false;

        // Cubby interiors blocked when door is closed
        if (canPlace && IsCubbyAndClosed(_currentSurface))
            canPlace = false;

        // Bounds check: reject if the item's footprint hangs off the surface edge.
        // Skip for floors (infinite-feeling) and walls (handled by wall snap).
        if (canPlace && !_currentSurface.IsFloor && !_currentSurface.IsVertical)
        {
            float halfX = _heldBoundsExtents.x;
            float halfZ = _heldBoundsExtents.z;
            // Check all four footprint corners against the surface bounds
            Vector3 fwd = Vector3.ProjectOnPlane(placeRot * Vector3.forward, hitResult.surfaceNormal).normalized;
            Vector3 rgt = Vector3.Cross(hitResult.surfaceNormal, fwd).normalized;
            if (!_currentSurface.ContainsWorldPoint(placePos + rgt * halfX + fwd * halfZ)
             || !_currentSurface.ContainsWorldPoint(placePos - rgt * halfX + fwd * halfZ)
             || !_currentSurface.ContainsWorldPoint(placePos + rgt * halfX - fwd * halfZ)
             || !_currentSurface.ContainsWorldPoint(placePos - rgt * halfX - fwd * halfZ))
                canPlace = false;
        }

        // Occupancy: blocked unless the occupant is a valid pair partner of the held item.
        if (canPlace)
        {
            var occupant = FindOccupant(placePos);
            if (occupant != null)
            {
                var heldPair = _held.GetComponent<PairableItem>();
                var occPair = occupant.GetComponent<PairableItem>();
                bool canStackHere = heldPair != null && occPair != null
                    && occPair.CanPairWith(heldPair);
                if (!canStackHere) canPlace = false;
            }
        }

        _shadowMat.color = GetShadowColor(canPlace);
        _shadowGO.transform.position = shadowPos;
        _shadowGO.transform.rotation = shadowRot;
        _shadowGO.transform.localScale = new Vector3(_heldShadowDiameter, 1f, _heldShadowDiameter);

        // Ghost preview sits at the planned landing pose — green when valid,
        // red when the item can't fit or placement is blocked.
        UpdateGhostPreview(placePos, placeRot, show: true, valid: canPlace);
    }

    // ── Public API ──────────────────────────────────────────────────

    /// <summary>
    /// Enable or disable grabbing. Called by ApartmentManager during state transitions.
    /// When disabled while holding, forces a drop onto the nearest surface.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        _isEnabled = enabled;

        if (!enabled && _held != null)
        {
            // Vinyl records always return to their sleeve on force-drop
            var forceVinyl = _held.GetComponent<VinylDisc>();
            if (forceVinyl != null && forceVinyl.HomeSleeve != null)
            {
                forceVinyl.HomeSleeve.ReturnVinyl(forceVinyl);
                ClearHeld();
                return;
            }

            // Bottles return home during drink phase on force-drop
            var forceBottle = _held.GetComponent<BottleItem>();
            if (forceBottle != null && forceBottle.HasHome)
            {
                forceBottle.ReturnHome();
                ClearHeld();
                return;
            }

            // Restore rotation constraints before configuring rigidbody
            _heldRb.constraints = _originalConstraints;

            var nearest = FindNearestSurfaceForHeld(_heldRb.position);
            if (nearest != null)
            {
                var hitResult = nearest.ProjectOntoSurface(_heldRb.position, cam != null ? cam.transform.position : (Vector3?)null);
                float halfExtent = GetHeldHalfExtentAlongNormal(hitResult.surfaceNormal);
                Vector3 pos = hitResult.worldPosition + hitResult.surfaceNormal * halfExtent;
                Quaternion rot = nearest.IsVertical
                    ? Quaternion.LookRotation(hitResult.surfaceNormal, Vector3.up)
                    : _held.transform.rotation;

                _heldRb.linearVelocity = Vector3.zero;
                _held.OnPlaced(nearest, false, pos, rot);

                // Book public/private toggle on force-drop
                var bookItem = _held.GetComponent<BookItem>();
                if (bookItem != null) bookItem.OnBookPlaced(nearest);

                // Stack-aware plate hooks on force-drop. Auto-stack-on-place
                // is gone — pairing is the only stack path now.
                var stackable = _held.GetComponent<StackablePlate>();
                if (stackable != null)
                {
                    var dropZone = nearest.GetComponent<DishDropZone>();
                    if (dropZone != null)
                    {
                        dropZone.RegisterDeposit(stackable);
                        var childPlates = stackable.GetComponentsInChildren<StackablePlate>();
                        foreach (var child in childPlates)
                        {
                            if (child != stackable)
                                dropZone.RegisterDeposit(child);
                        }
                    }
                }
            }
            else
            {
                _heldRb.useGravity = true;
                _heldRb.linearVelocity = Vector3.zero;
                _held.OnDropped();
            }

            ClearHeld();
        }
    }

    private static bool IsCubbyAndClosed(PlacementSurface surface)
    {
        var cubby = DrawerController.FindByInteriorSurface(surface);
        return cubby != null && cubby.IsInteriorAndClosed(surface);
    }

    private PlacementSurface FindNearestSurfaceForHeld(Vector3 worldPos)
    {
        if (_held == null) return PlacementSurface.FindNearest(worldPos);
        return PlacementSurface.FindNearest(worldPos,
            skipVertical: !_held.CanWallMount,
            skipHorizontal: _held.WallOnly);
    }
}
