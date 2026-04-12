using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A bouquet of trimmed flowers stored in the fridge. Trimmed flowers
/// accumulate here instead of being scattered around the apartment.
///
/// The bouquet has water level, drying rate, and health — the player
/// waters it with the watering can. Under/overwatering affects the
/// bouquet's health, date scoring, and air quality contribution.
///
/// Scene-scoped singleton. Place inside the fridge on a shelf slot.
/// </summary>
public class FridgeBouquet : MonoBehaviour
{
    public static FridgeBouquet Instance { get; private set; }

    [Header("Bouquet")]
    [Tooltip("Maximum flowers the bouquet can hold.")]
    [SerializeField] private int _maxFlowers = 6;

    [Tooltip("PlantDefinition for watering parameters (pot shape, thirst, drying rate).")]
    [SerializeField] private PlantDefinition _plantDefinition;

    [Header("Visuals")]
    [Tooltip("Parent transform for flower visual children.")]
    [SerializeField] private Transform _flowerContainer;

    [Tooltip("How much each flower offsets from center (fan spread).")]
    [SerializeField] private float _fanSpread = 0.02f;

    [Header("Health")]
    [Tooltip("Days the bouquet survives without watering before dying.")]
    [SerializeField] private int _baseDaysAlive = 7;

    private readonly List<GameObject> _flowers = new();
    private float _currentWaterLevel;
    private float _health = 1f;
    private bool _overflowedToday;
    private float _waterTarget = 0.7f;
    private float _waterTolerance = 0.08f;
    private float _dryingRate = 0.15f;

    // ── Public API ──────────────────────────────────────────────────

    public int FlowerCount => _flowers.Count;
    public float Health => _health;
    public float CurrentWaterLevel => _currentWaterLevel;
    public bool IsEmpty => _flowers.Count == 0;

    public enum WaterState { Underwatered, Perfect, Overwatered }

    public WaterState GetWaterState()
    {
        if (_flowers.Count == 0) return WaterState.Perfect;
        if (_currentWaterLevel < _waterTarget - _waterTolerance) return WaterState.Underwatered;
        if (_currentWaterLevel > _waterTarget + _waterTolerance || _overflowedToday) return WaterState.Overwatered;
        return WaterState.Perfect;
    }

    public float GetAirQualityContribution()
    {
        if (_flowers.Count == 0) return 0f;
        return GetWaterState() == WaterState.Perfect ? _health : _health * 0.3f;
    }

    // ── Lifecycle ───────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        if (_plantDefinition != null)
        {
            _waterTarget = _plantDefinition.thirstLevel;
            _waterTolerance = _plantDefinition.waterTolerance;
            _dryingRate = _plantDefinition.dryingRatePerDay;
        }

        // Add WaterablePlant so the watering can snaps to it
        var waterable = GetComponent<WaterablePlant>();
        if (waterable == null) waterable = gameObject.AddComponent<WaterablePlant>();
        if (_plantDefinition != null) waterable.definition = _plantDefinition;

        // Add ReactableTag for date reactions
        var reactable = GetComponent<ReactableTag>();
        if (reactable == null)
        {
            reactable = gameObject.AddComponent<ReactableTag>();
            var tagsField = typeof(ReactableTag).GetField("tags",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (tagsField != null)
                tagsField.SetValue(reactable, new[] { "flower", "bouquet", "gift" });
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Flower Management ───────────────────────────────────────────

    /// <summary>
    /// Add a trimmed flower to the bouquet. Called by FlowerTrimmingBridge
    /// instead of LivingFlowerPlantManager.SpawnPlant.
    /// </summary>
    public void AddFlower(GameObject flowerVisual, string characterName)
    {
        if (_flowers.Count >= _maxFlowers)
        {
            Debug.Log("[FridgeBouquet] Bouquet is full — oldest flower removed.");
            RemoveOldest();
        }

        if (flowerVisual != null)
        {
            flowerVisual.transform.SetParent(_flowerContainer != null ? _flowerContainer : transform, false);

            // Fan flowers out from center
            float offset = (_flowers.Count - _maxFlowers * 0.5f) * _fanSpread;
            flowerVisual.transform.localPosition = new Vector3(offset, 0f, 0f);
            flowerVisual.transform.localRotation = Quaternion.Euler(0f, 0f, offset * 200f); // slight tilt

            // Strip physics
            var rb = flowerVisual.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
            foreach (var col in flowerVisual.GetComponentsInChildren<Collider>())
                col.enabled = false;

            _flowers.Add(flowerVisual);
        }

        // Reset health when new flower added (fresh bouquet)
        _health = 1f;
        UpdateReactableState();

        Debug.Log($"[FridgeBouquet] Added flower from {characterName}. Total: {_flowers.Count}");
        CaptionDisplay.Show($"Added {characterName}'s flower to the bouquet.", 3f);
    }

    private void RemoveOldest()
    {
        if (_flowers.Count == 0) return;
        var oldest = _flowers[0];
        _flowers.RemoveAt(0);
        if (oldest != null) Destroy(oldest);
    }

    // ── Water Management ────────────────────────────────────────────

    /// <summary>Set the water level (called by WateringManager after pouring).</summary>
    public void SetWaterLevel(float level)
    {
        _currentWaterLevel = Mathf.Clamp01(level);
        _overflowedToday = level > 1f;
    }

    /// <summary>Apply overnight water loss. Called each morning.</summary>
    public void ApplyOvernightDrying(float weatherMultiplier)
    {
        if (_flowers.Count == 0) return;

        _currentWaterLevel -= _dryingRate * weatherMultiplier;
        _currentWaterLevel = Mathf.Max(0f, _currentWaterLevel);
        _overflowedToday = false;

        // Health decays based on water stress
        var state = GetWaterState();
        if (state != WaterState.Perfect)
            _health -= 1f / _baseDaysAlive;
        _health = Mathf.Max(0f, _health);

        if (_health <= 0f)
        {
            Debug.Log("[FridgeBouquet] Bouquet has wilted.");
            // Don't destroy — let it go brown for visual feedback
        }

        UpdateReactableState();
        UpdateAirQuality();
    }

    private void UpdateReactableState()
    {
        var reactable = GetComponent<ReactableTag>();
        if (reactable != null)
            reactable.IsActive = _flowers.Count > 0 && _health > 0f;
    }

    private void UpdateAirQuality()
    {
        float quality = GetAirQualityContribution();
        if (quality > 0f)
            MoodMachine.Instance?.SetSource("AirQuality", quality);
        else
            MoodMachine.Instance?.RemoveSource("AirQuality");
    }

    // ── Save / Load ─────────────────────────────────────────────────

    public BouquetSaveData ToSaveData()
    {
        return new BouquetSaveData
        {
            flowerCount = _flowers.Count,
            currentWaterLevel = _currentWaterLevel,
            health = _health
        };
    }

    public void LoadFromSave(BouquetSaveData data)
    {
        if (data == null) return;
        _currentWaterLevel = data.currentWaterLevel;
        _health = data.health;
        // Flower visuals would need to be rebuilt from save — for now just track count
        UpdateReactableState();
    }

    [System.Serializable]
    public class BouquetSaveData
    {
        public int flowerCount;
        public float currentWaterLevel;
        public float health;
    }
}
