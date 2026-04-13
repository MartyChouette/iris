using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Watering system driven by the physical watering can.
/// Player holds the can near a plant (ObjectGrabber magnetic snap), then
/// click-and-holds to pour. Water rises in the PotCrossSectionUI overlay.
/// Chicken pour: release pauses, re-press resumes. Water never drains during
/// a session — only overnight via drying.
///
/// States: Idle → Approaching → Pouring → Done
///   Idle        — can is on shelf, no watering
///   Approaching — can is held, not near a plant yet
///   Pouring     — can snapped to plant, mouse held = water flowing
///   Done        — session ended, water level saved to plant
/// </summary>
[DisallowMultipleComponent]
public class WateringManager : MonoBehaviour
{
    public static WateringManager Instance { get; private set; }

    public enum State { Idle, Approaching, Pouring, Done }

    [Header("References")]
    [Tooltip("Main camera (auto-found if null).")]
    [SerializeField] private Camera _mainCamera;

    [Header("Pour Settings")]
    [Tooltip("Base pour rate (water units/sec). Modified by pot shape.")]
    [SerializeField] private float _basePourRate = 0.1f;

    [Tooltip("How much faster foam/soil bubbles rise compared to water. Higher = more obscured water line.")]
    [SerializeField] private float _foamRateMultiplier = 2.5f;

    [Tooltip("Foam settle speed when not pouring (units/sec).")]
    [SerializeField] private float _foamSettleRate = 0.2f;

    [Tooltip("Amplitude of pour rate oscillation (0 = steady, 0.15 = ±15%).")]
    [SerializeField] private float _pourRateOscAmplitude = 0.15f;

    [Tooltip("Speed of pour rate oscillation (Hz).")]
    [SerializeField] private float _pourRateOscSpeed = 2f;

    [Header("Audio")]
    public AudioClip pourSFX;
    public AudioClip overflowSFX;
    public AudioClip perfectSFX;

    // ── Public API ──────────────────────────────────────────────────

    public State CurrentState { get; private set; } = State.Idle;

    /// <summary>The plant currently being watered (null if none).</summary>
    public WaterablePlant ActivePlant => _activePlant;

    /// <summary>Current water level during pour session.</summary>
    public float WaterLevel => _waterLevel;

    /// <summary>Current foam/bubble level (obscures water line).</summary>
    public float FoamLevel => _foamLevel;

    /// <summary>True if water overflowed the pot.</summary>
    public bool Overflowed => _overflowed;

    // ── Runtime ─────────────────────────────────────────────────────

    private WaterablePlant _activePlant;
    private PlantDefinition _activeDef;
    private LivingFlowerPlant _activeLiving;
    private float _waterLevel;
    private float _foamLevel;
    private bool _overflowed;
    private bool _overflowSFXPlayed;
    private float _pourTime;
    private bool _isPouring; // true while mouse is held during pour

    private InputAction _clickAction;

    // ── Lifecycle ───────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        _clickAction = new InputAction("WaterClick", InputActionType.Button, "<Mouse>/leftButton");
        if (_mainCamera == null) _mainCamera = Camera.main;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }
    private void OnEnable() { _clickAction.Enable(); }
    private void OnDisable() { _clickAction.Disable(); }

    private void Update()
    {
        if (CurrentState == State.Pouring)
            UpdatePouring();
    }

    // ── Called by ObjectGrabber ──────────────────────────────────────

    /// <summary>
    /// Start a watering session on a specific plant.
    /// Called by ObjectGrabber when the player holds the watering can
    /// near a plant and clicks.
    /// </summary>
    public void StartWatering(WaterablePlant plant)
    {
        if (plant == null) return;

        // Already watering this same plant — don't restart, just resume
        if (CurrentState == State.Pouring && _activePlant == plant)
            return;

        // Switching plants — save the previous one first
        if (CurrentState == State.Pouring && _activePlant != null && _activePlant != plant)
            SaveWaterLevel();

        _activePlant = plant;
        _activeDef = plant.definition;
        _activeLiving = plant.GetComponent<LivingFlowerPlant>();

        // Start from the plant's current water level (persistence — never resets)
        _waterLevel = plant.WaterLevel;
        _foamLevel = _waterLevel;
        _overflowed = _waterLevel > 1f;
        _overflowSFXPlayed = _overflowed;
        _pourTime = 0f;
        _isPouring = false;

        CurrentState = State.Pouring;

        if (PotCrossSectionUI.Instance != null && _activeDef != null)
            PotCrossSectionUI.Instance.Show(_activeDef, _waterLevel);

        Debug.Log($"[WateringManager] Started watering: {_activeDef?.plantName ?? "unknown"} (water={_waterLevel:F2})");
    }

    /// <summary>Save current water level to the plant without ending the session.</summary>
    private void SaveWaterLevel()
    {
        // Save to WaterablePlant (works for ALL plants)
        if (_activePlant != null)
            _activePlant.WaterLevel = Mathf.Min(_waterLevel, 1f);

        // Also sync to LivingFlowerPlant if present (for overnight drying/scoring)
        if (_activeLiving != null)
            _activeLiving.SetWaterLevel(Mathf.Min(_waterLevel, 1f));
    }

    /// <summary>
    /// End the watering session. Saves water level to the plant.
    /// Called when the player moves the can away or places it down.
    /// </summary>
    public void EndWatering()
    {
        if (CurrentState != State.Pouring) return;

        // Save water level to the living plant
        if (_activeLiving != null)
            _activeLiving.SetWaterLevel(_waterLevel);

        // Hide UI
        PotCrossSectionUI.Instance?.Hide();

        _activePlant = null;
        _activeDef = null;
        _activeLiving = null;
        CurrentState = State.Idle;

        Debug.Log("[WateringManager] Watering session ended.");
    }

    /// <summary>Force back to idle. Called on phase transitions.</summary>
    public void ForceIdle()
    {
        PotCrossSectionUI.Instance?.Hide();
        _activePlant = null;
        _activeDef = null;
        _activeLiving = null;
        CurrentState = State.Idle;
    }

    // ── Pouring ─────────────────────────────────────────────────────

    private void UpdatePouring()
    {
        if (_activeDef == null) { EndWatering(); return; }

        bool mouseHeld = _clickAction.IsPressed();

        if (mouseHeld)
        {
            // Chicken pour: accumulate water while held
            _pourTime += Time.deltaTime;

            // Fill rate with shape multiplier and oscillation
            float shapeMult = _activeDef.GetShapeFillMultiplier(_waterLevel);
            float osc = 1f + _pourRateOscAmplitude * Mathf.Sin(_pourRateOscSpeed * Mathf.PI * 2f * _pourTime);
            float rate = _basePourRate * shapeMult * osc;

            float delta = rate * Time.deltaTime;
            _waterLevel += delta;

            // Foam grows much faster than water to obscure the water line.
            // The gap between foam and water is the "blind zone" the player
            // can't see through. Foam leads water by a growing margin.
            float foamDelta = delta * _foamRateMultiplier;
            _foamLevel += foamDelta;
            // Foam always stays above water — never below
            _foamLevel = Mathf.Max(_foamLevel, _waterLevel + 0.03f);

            _isPouring = true;

            // Save water level to plant continuously
            SaveWaterLevel();

            // Overflow check — EITHER water or foam going over the rim = spill
            bool rimOverflow = _waterLevel > 1f || _foamLevel > 1f;
            if (rimOverflow && !_overflowed)
            {
                _overflowed = true;
                PotCrossSectionUI.Instance?.SetOverflowing(true);
                PotCrossSectionUI.Instance?.SetStatus("Overflowing!");

                if (!_overflowSFXPlayed && overflowSFX != null && AudioManager.Instance != null)
                {
                    AudioManager.Instance.PlaySFX(overflowSFX);
                    _overflowSFXPlayed = true;
                }

                // Spawn water spill stain immediately at the plant's base
                if (_activePlant != null)
                    SpawnOverflowSpill(_activePlant.transform.position);
            }

            _waterLevel = Mathf.Min(_waterLevel, 1.1f);
            _foamLevel = Mathf.Min(_foamLevel, 1.2f);
        }
        else
        {
            // Released — foam settles toward water level
            if (_foamLevel > _waterLevel)
            {
                _foamLevel -= _foamSettleRate * Time.deltaTime;
                _foamLevel = Mathf.Max(_foamLevel, _waterLevel);
            }

            if (_isPouring)
            {
                _isPouring = false;
                // Update status based on accuracy
                UpdatePourStatus();
            }
        }

        // Update UI
        PotCrossSectionUI.Instance?.UpdatePour(_waterLevel, _foamLevel);
    }

    private void UpdatePourStatus()
    {
        if (_activeDef == null) return;

        float target = _activeDef.idealWaterLevel;
        float tolerance = _activeDef.waterTolerance;
        float diff = _waterLevel - target;

        if (_overflowed)
            PotCrossSectionUI.Instance?.SetStatus("Too much water!");
        else if (Mathf.Abs(diff) <= tolerance)
            PotCrossSectionUI.Instance?.SetStatus("Looks perfect!");
        else if (diff < -tolerance)
            PotCrossSectionUI.Instance?.SetStatus("Could use more...");
        else
            PotCrossSectionUI.Instance?.SetStatus("A bit much...");
    }

    /// <summary>Spawn a water spill stain at the plant's base when overflowing.</summary>
    private static void SpawnOverflowSpill(Vector3 plantPos)
    {
        // Find a nearby CleanableSurface slot or create a SpillSurface
        Vector3 spillPos = plantPos + new Vector3(
            Random.Range(-0.1f, 0.1f), 0.001f, Random.Range(-0.1f, 0.1f));

        var spillGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
        spillGO.name = "WaterSpill";
        spillGO.transform.position = spillPos;
        spillGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // flat on floor
        spillGO.transform.localScale = new Vector3(0.25f, 0.25f, 1f);

        // Add SpillSurface for wipe-to-clean
        var spill = spillGO.AddComponent<SpillSurface>();

        // Replace MeshCollider (can't be trigger) with a flat BoxCollider
        var meshCol = spillGO.GetComponent<MeshCollider>();
        if (meshCol != null) Object.Destroy(meshCol);
        var box = spillGO.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(1f, 1f, 0.01f);

        Debug.Log($"[WateringManager] Water spill spawned at {spillPos}.");
    }
}
