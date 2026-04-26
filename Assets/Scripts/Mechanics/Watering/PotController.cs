using UnityEngine;

/// <summary>
/// Soil/water/foam simulation for the watering prototype.
/// Adapted from <see cref="GlassController"/> — uses Transform scaling for visual levels.
/// Dirt foams up when watered (homage to drink-making foam mechanic).
/// </summary>
[DisallowMultipleComponent]
public class PotController : MonoBehaviour
{
    [Header("Definition")]
    [Tooltip("Active plant definition (set by WateringManager when entering watering state).")]
    public PlantDefinition definition;

    [Header("Visuals")]
    [Tooltip("Renderer for the soil box (colour lerps dry to wet).")]
    public Renderer soilRenderer;

    [Tooltip("Transform for the soil box (scales Y with water level).")]
    public Transform soilTransform;

    [Tooltip("Renderer for the foam box (bubbly dirt on top).")]
    public Renderer foamRenderer;

    [Tooltip("Transform for the foam box (scales Y with foam minus water).")]
    public Transform foamTransform;

    [Tooltip("Thin marker at the ideal water level.")]
    public Transform fillLineMarker;

    [Tooltip("Thin marker tracking the current water level.")]
    public Transform waterLineMarker;

    [Header("Water Visuals")]
    [Tooltip("Renderer for the water box (transparent blue).")]
    public Renderer waterRenderer;

    [Tooltip("Transform for the water box (scales Y with water level).")]
    public Transform waterTransform;

    [Header("Overflow Visuals")]
    [Tooltip("Small drip boxes on the pot exterior (animate when overflowed).")]
    public Transform[] overflowDrips;

    [Header("Drain Visuals")]
    [Tooltip("Small drip box below the pot (visible when water > drainThreshold).")]
    public Transform drainDrip;

    [Tooltip("Water level above which drain drip appears.")]
    public float drainThreshold = 0.5f;

    [Header("Pot Dimensions")]
    [Tooltip("Internal height of the pot in world units.")]
    public float potWorldHeight = 0.10f;

    [Tooltip("Visual radius of the pot interior.")]
    public float potWorldRadius = 0.04f;

    // ── State (read-only from outside) ───────────────────────────────

    [Header("State (Read-Only)")]
    [SerializeField] private float _waterLevel;
    [SerializeField] private float _foamLevel;
    [SerializeField] private bool _overflowed;

    private bool _isPouring;
    private MaterialPropertyBlock _soilMPB;
    private MaterialPropertyBlock _foamMPB;
    private MaterialPropertyBlock _waterMPB;

    // ── Public API ───────────────────────────────────────────────────

    public float WaterLevel => _waterLevel;
    public float FoamLevel => _foamLevel;
    public bool Overflowed => _overflowed;
    public bool IsPouring => _isPouring;

    /// <summary>Externally-set target level for the fill line marker (written by WateringManager each frame).</summary>
    public float TargetLevel { get; set; }

    /// <summary>
    /// How close the water level is to the ideal fill line (1 = perfect, 0 = way off).
    /// </summary>
    public float FillAccuracy
    {
        get
        {
            if (definition == null) return 0f;
            float dist = Mathf.Abs(_waterLevel - definition.idealWaterLevel);
            return Mathf.Clamp01(1f - dist / Mathf.Max(definition.waterTolerance, 0.001f));
        }
    }

    /// <summary>
    /// Add water while pouring (call each frame while mouse held).
    /// </summary>
    public void Pour(float dt)
    {
        if (definition == null) return;

        float liquidDelta = definition.pourRate * dt;
        _waterLevel += liquidDelta;

        // Foam rises FASTER than water (dirt bubbles up)
        float foamDelta = liquidDelta * definition.foamRateMultiplier;
        _foamLevel += foamDelta;

        // Clamp water to 0-1
        _waterLevel = Mathf.Clamp01(_waterLevel);

        // Foam can exceed 1.0 → overflow
        if (_foamLevel > 1f)
        {
            _foamLevel = 1f;
            _overflowed = true;
        }

        // Foam never below water
        _foamLevel = Mathf.Max(_foamLevel, _waterLevel);

        _isPouring = true;
    }

    /// <summary>
    /// Signal the end of a pour.
    /// </summary>
    public void StopPouring()
    {
        _isPouring = false;
    }

    /// <summary>
    /// Reset pot to empty for a new plant.
    /// </summary>
    public void Clear()
    {
        _waterLevel = 0f;
        _foamLevel = 0f;
        _overflowed = false;
        _isPouring = false;

        // Hide water/overflow/drain visuals
        if (waterTransform != null)
            waterTransform.gameObject.SetActive(false);

        if (overflowDrips != null)
        {
            for (int i = 0; i < overflowDrips.Length; i++)
                if (overflowDrips[i] != null) overflowDrips[i].gameObject.SetActive(false);
        }

        if (drainDrip != null)
            drainDrip.gameObject.SetActive(false);
    }

    // ── MonoBehaviour ────────────────────────────────────────────────

    void Awake()
    {
        _soilMPB = new MaterialPropertyBlock();
        _foamMPB = new MaterialPropertyBlock();
        _waterMPB = new MaterialPropertyBlock();
    }

    void Update()
    {
        if (definition == null) return;

        SettleFoam(Time.deltaTime);
        UpdateVisuals();
    }

    // ── Internals ────────────────────────────────────────────────────

    private void SettleFoam(float dt)
    {
        if (!_isPouring)
        {
            // Foam settles toward water level
            _foamLevel = Mathf.MoveTowards(_foamLevel, _waterLevel, definition.foamSettleRate * dt);

            // Water slowly absorbs into soil
            if (_waterLevel > 0f)
            {
                _waterLevel = Mathf.MoveTowards(_waterLevel, 0f, definition.absorptionRate * dt);
                _foamLevel = Mathf.Max(_foamLevel, _waterLevel);
            }
        }
    }

    private void UpdateVisuals()
    {
        float h = potWorldHeight;

        // Soil colour lerp (dry → wet based on water level)
        if (soilRenderer != null)
        {
            Color soilColor = Color.Lerp(definition.dryColor, definition.wetColor, _waterLevel);
            soilRenderer.GetPropertyBlock(_soilMPB);
            _soilMPB.SetColor("_BaseColor", soilColor);
            soilRenderer.SetPropertyBlock(_soilMPB);
        }

        // Soil transform — scales Y with water level
        if (soilTransform != null)
        {
            float soilHeight = Mathf.Max(_waterLevel * h, 0.001f);
            soilTransform.localScale = new Vector3(
                soilTransform.localScale.x,
                soilHeight,
                soilTransform.localScale.z);
            soilTransform.localPosition = new Vector3(0f, soilHeight * 0.5f, 0f);
        }

        // Foam transform — sits on top of soil, fades as it settles
        if (foamTransform != null)
        {
            float soilHeight = _waterLevel * h;
            float foamHeight = Mathf.Max((_foamLevel - _waterLevel) * h, 0f);

            float foamRatio = _waterLevel > 0.001f ? (_foamLevel - _waterLevel) / Mathf.Max(_waterLevel, 0.01f) : 0f;
            float foamAlpha = Mathf.Clamp01(foamRatio * 5f);

            if (foamAlpha < 0.01f)
            {
                foamTransform.gameObject.SetActive(false);
            }
            else
            {
                foamTransform.gameObject.SetActive(true);
                foamTransform.localScale = new Vector3(
                    foamTransform.localScale.x,
                    Mathf.Max(foamHeight, 0.001f),
                    foamTransform.localScale.z);
                foamTransform.localPosition = new Vector3(0f, soilHeight + foamHeight * 0.5f, 0f);

                if (foamRenderer != null)
                {
                    foamRenderer.GetPropertyBlock(_foamMPB);
                    Color fc = definition.foamColor;
                    fc.a = foamAlpha;
                    _foamMPB.SetColor("_BaseColor", fc);
                    foamRenderer.SetPropertyBlock(_foamMPB);
                }
            }
        }

        // Fill line tracks the oscillating target level
        if (fillLineMarker != null)
        {
            float fillY = TargetLevel * h;
            fillLineMarker.localPosition = new Vector3(0f, fillY, 0f);
        }

        // Current water line
        if (waterLineMarker != null)
        {
            float waterY = _waterLevel * h;
            waterLineMarker.localPosition = new Vector3(0f, waterY, 0f);
        }

        // Water box — transparent blue layer
        if (waterTransform != null)
        {
            bool showWater = _waterLevel > 0.05f;
            waterTransform.gameObject.SetActive(showWater);

            if (showWater)
            {
                float waterHeight = Mathf.Max(_waterLevel * h, 0.001f);
                waterTransform.localScale = new Vector3(
                    waterTransform.localScale.x,
                    waterHeight,
                    waterTransform.localScale.z);
                waterTransform.localPosition = new Vector3(0f, waterHeight * 0.5f, 0f);

                if (waterRenderer != null)
                {
                    waterRenderer.GetPropertyBlock(_waterMPB);
                    _waterMPB.SetColor("_BaseColor", new Color(0.3f, 0.5f, 0.8f, 0.4f));
                    waterRenderer.SetPropertyBlock(_waterMPB);
                }
            }
        }

        // Overflow drips — visible when overflowed, animate with PingPong
        if (overflowDrips != null)
        {
            for (int i = 0; i < overflowDrips.Length; i++)
            {
                if (overflowDrips[i] == null) continue;
                overflowDrips[i].gameObject.SetActive(_overflowed);

                if (_overflowed)
                {
                    float ping = Mathf.PingPong(Time.time * 1.5f + i * 0.7f, 1f);
                    float s = Mathf.Lerp(0.5f, 1.2f, ping);
                    overflowDrips[i].localScale = new Vector3(
                        overflowDrips[i].localScale.x,
                        0.01f * s,
                        overflowDrips[i].localScale.z);
                }
            }
        }

        // Drain drip — visible when water > drainThreshold
        if (drainDrip != null)
        {
            bool showDrain = _waterLevel > drainThreshold;
            drainDrip.gameObject.SetActive(showDrain);

            if (showDrain)
            {
                float excess = (_waterLevel - drainThreshold) / (1f - drainThreshold);
                float dripScale = Mathf.Lerp(0.005f, 0.015f, excess);
                drainDrip.localScale = new Vector3(dripScale, dripScale * 2f, dripScale);
            }
        }
    }
}
