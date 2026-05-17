using UnityEngine;

/// <summary>
/// A living plant decoration spawned from a successful flower trimming.
/// Health decreases each day by 1/totalDaysAlive. Visual color and scale
/// degrade as health drops. When health reaches 0, the plant dies.
/// </summary>
public class LivingFlowerPlant : MonoBehaviour
{
    [Header("Lifespan")]
    [Tooltip("Day this plant was spawned.")]
    [SerializeField] private int _spawnDay;

    [Tooltip("Total days this plant will survive (from flower trimming score).")]
    [SerializeField] private int _totalDaysAlive;

    [Tooltip("Current health (1.0 = fresh, 0.0 = dead).")]
    [SerializeField] private float _health = 1f;

    [Header("Character")]
    [Tooltip("Name of the date character who gave this flower.")]
    [SerializeField] private string _characterName;

    // ─── Visual Settings ──────────────────────────────────────────
    private static readonly Color HealthyColor = new Color(0.3f, 0.7f, 0.2f);
    private static readonly Color WiltingColor = new Color(0.8f, 0.7f, 0.2f);
    private static readonly Color DeadColor    = new Color(0.4f, 0.25f, 0.1f);

    private const float MinScale = 0.8f;
    private const float MaxScale = 1.0f;

    private Renderer[] _renderers;
    private Color[] _originalColors;
    private Vector3 _baseScale;
    private bool _isDead;

    // ── Water Persistence ────────────────────────────────────────
    public enum WaterState { Underwatered, Perfect, Overwatered }

    [Header("Water")]
    [Tooltip("Current water level (0-1). Persists between days.")]
    [SerializeField] private float _currentWaterLevel;

    [Tooltip("Target water level from PlantDefinition.thirstLevel.")]
    [SerializeField] private float _waterTarget = 0.7f;

    [Tooltip("Water tolerance band around target.")]
    [SerializeField] private float _waterTolerance = 0.08f;

    [Tooltip("Drying rate per day (before weather multiplier).")]
    [SerializeField] private float _dryingRate = 0.15f;

    [Tooltip("Chance of shedding a leaf when stressed.")]
    [SerializeField] private float _leafSheddingChance = 0.5f;

    private bool _overflowedToday;

    public float CurrentWaterLevel => _currentWaterLevel;
    public float WaterTarget => _waterTarget;
    public bool OverflowedToday => _overflowedToday;

    // ─── Public API ───────────────────────────────────────────────

    public int SpawnDay => _spawnDay;
    public int TotalDaysAlive => _totalDaysAlive;
    public float Health => _health;
    public string CharacterName => _characterName;
    public bool IsDead => _isDead;

    public void Initialize(string characterName, int spawnDay, int totalDaysAlive)
    {
        _characterName = characterName;
        _spawnDay = spawnDay;
        _totalDaysAlive = Mathf.Max(1, totalDaysAlive);
        _health = 1f;
        _isDead = false;
        UpdateVisuals();
    }

    public void SetHealth(float health)
    {
        _health = Mathf.Clamp01(health);
        if (_health <= 0f) Die();
        else UpdateVisuals();
    }

    /// <summary>Called each morning by LivingFlowerPlantManager.</summary>
    public void AdvanceDay()
    {
        if (_isDead) return;

        _health -= 1f / _totalDaysAlive;
        _health = Mathf.Max(0f, _health);

        if (_health <= 0f)
            Die();
        else
            UpdateVisuals();
    }

    // ─── Lifecycle ────────────────────────────────────────────────

    private void Awake()
    {
        // Collect only flower renderers — skip pot and soil so wilt tint
        // doesn't turn them green.
        var allRenderers = GetComponentsInChildren<Renderer>();
        var tintable = new System.Collections.Generic.List<Renderer>();
        var colors = new System.Collections.Generic.List<Color>();

        foreach (var r in allRenderers)
        {
            if (r.gameObject.name == "Pot" || r.gameObject.name == "Soil")
                continue;
            tintable.Add(r);
            // Read from sharedMaterial to avoid creating instances
            colors.Add(r.sharedMaterial != null ? r.sharedMaterial.color : Color.white);
        }

        _renderers = tintable.ToArray();
        _originalColors = colors.ToArray();
        _baseScale = transform.localScale;
    }

    // ─── Internal ─────────────────────────────────────────────────

    private static readonly Color UnderwaterTint = new Color(0.7f, 0.55f, 0.3f); // dry brown
    private static readonly Color OverwaterTint = new Color(0.4f, 0.5f, 0.35f);  // soggy dark green

    private void UpdateVisuals()
    {
        // Wilt tint: white (fresh) → yellowish (wilting) → brown (dead)
        Color wiltTint;
        if (_health > 0.5f)
            wiltTint = Color.Lerp(WiltingColor, Color.white, (_health - 0.5f) * 2f);
        else
            wiltTint = Color.Lerp(DeadColor, WiltingColor, _health * 2f);

        // Water stress tint overlay — browning for dry, dark green for soggy
        var waterState = GetWaterState();
        if (waterState == WaterState.Underwatered)
            wiltTint *= UnderwaterTint;
        else if (waterState == WaterState.Overwatered)
            wiltTint *= OverwaterTint;

        // Apply tint via MPB — no material instances created
        if (_renderers != null)
        {
            var mpb = new MaterialPropertyBlock();
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;
                mpb.SetColor("_BaseColor", _originalColors[i] * wiltTint);
                _renderers[i].SetPropertyBlock(mpb);
            }
        }

        // Scale shrinks as health drops
        float scale = Mathf.Lerp(MinScale, MaxScale, _health);
        transform.localScale = _baseScale * scale;
    }

    /// <summary>Set water config from PlantDefinition.</summary>
    public void ConfigureWater(float target, float tolerance, float dryingRate, float sheddingChance)
    {
        _waterTarget = target;
        _waterTolerance = tolerance;
        _dryingRate = dryingRate;
        _leafSheddingChance = sheddingChance;
    }

    /// <summary>Set the water level (called by WateringManager after pouring).</summary>
    public void SetWaterLevel(float level)
    {
        _currentWaterLevel = Mathf.Clamp01(level);
        _overflowedToday = level > 1f;
    }

    /// <summary>Apply overnight water loss. Called each morning by LivingFlowerPlantManager.</summary>
    public void ApplyOvernightDrying(float weatherMultiplier)
    {
        _currentWaterLevel -= _dryingRate * weatherMultiplier;
        _currentWaterLevel = Mathf.Max(0f, _currentWaterLevel);
        _overflowedToday = false;
    }

    /// <summary>Current watering quality relative to target.</summary>
    public WaterState GetWaterState()
    {
        if (_currentWaterLevel < _waterTarget - _waterTolerance) return WaterState.Underwatered;
        if (_currentWaterLevel > _waterTarget + _waterTolerance || _overflowedToday) return WaterState.Overwatered;
        return WaterState.Perfect;
    }

    /// <summary>Air quality contribution (0-1). Perfect = 1, stressed = reduced.</summary>
    public float GetAirQualityContribution()
    {
        if (_isDead) return 0f;
        return GetWaterState() == WaterState.Perfect ? _health : _health * 0.3f;
    }

    /// <summary>Whether this plant should shed a leaf this morning (random check).</summary>
    public bool ShouldShedLeaf()
    {
        if (GetWaterState() == WaterState.Perfect) return false;
        return Random.value < _leafSheddingChance;
    }

    private void Die()
    {
        _isDead = true;
        _health = 0f;

        Debug.Log($"[LivingFlowerPlant] Plant from {_characterName} has died.");

        // Deactivate ReactableTag so date NPCs don't react to dead plants
        var reactable = GetComponent<ReactableTag>();
        if (reactable != null) reactable.IsActive = false;

        // Remove MoodMachine source
        MoodMachine.Instance?.RemoveSource($"Plant_{_characterName}");

        gameObject.SetActive(false);
    }

    /// <summary>Create a serializable record for save data.</summary>
    public LivingPlantRecord ToRecord()
    {
        return new LivingPlantRecord
        {
            characterName = _characterName,
            spawnDay = _spawnDay,
            totalDaysAlive = _totalDaysAlive,
            currentHealth = _health,
            currentWaterLevel = _currentWaterLevel,
            px = transform.position.x,
            py = transform.position.y,
            pz = transform.position.z
        };
    }

    /// <summary>Restore water level from save data.</summary>
    public void RestoreWaterLevel(float waterLevel)
    {
        _currentWaterLevel = Mathf.Clamp01(waterLevel);
    }
}
