using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Scene-scoped singleton tracking all living plants in the apartment.
/// Spawns plants from flower trimming results, advances their health each day,
/// and feeds MoodMachine with average plant health.
/// Plants are wrapped in a pot+soil and made placeable via ObjectGrabber.
/// </summary>
public class LivingFlowerPlantManager : MonoBehaviour
{
    public static LivingFlowerPlantManager Instance { get; private set; }

    [Header("Plant Slots")]
    [Tooltip("Pre-placed transforms where living plants can be spawned.")]
    [SerializeField] private Transform[] _plantSlots;

    [Header("Layer & Material")]
    [Tooltip("Layer index for spawned plants (must match ObjectGrabber's placeableLayer mask).")]
    [SerializeField] private int _plantLayer;

    [Tooltip("Material for pot/soil/procedural parts. Falls back to Shader.Find if unassigned.")]
    [SerializeField] private Material _fallbackMaterial;

    private readonly List<LivingFlowerPlant> _activePlants = new();
    private int _nextSlotIndex;

    public IReadOnlyList<LivingFlowerPlant> ActivePlants => _activePlants;

    // ─── Lifecycle ────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[LivingFlowerPlantManager] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ─── Public API ───────────────────────────────────────────────

    /// <summary>
    /// Spawn a new living plant at the next available slot.
    /// Called after flower trimming completes successfully.
    /// </summary>
    /// <param name="trimmedVisual">Optional cloned flower mesh from TrimmedFlowerSnapshot.
    /// When provided, uses the actual trimmed flower visuals instead of procedural geometry.
    /// Pass null to use the procedural fallback (e.g. when loading from save).</param>
    public void SpawnPlant(string characterName, int daysAlive, GameObject trimmedVisual = null)
    {
        if (_plantSlots == null || _plantSlots.Length == 0)
        {
            Debug.LogWarning("[LivingFlowerPlantManager] No plant slots configured.");
            if (trimmedVisual != null) Object.Destroy(trimmedVisual);
            return;
        }

        if (daysAlive <= 0)
        {
            Debug.Log("[LivingFlowerPlantManager] Plant has 0 days alive — not spawning.");
            if (trimmedVisual != null) Object.Destroy(trimmedVisual);
            return;
        }

        // Find next available slot (cycle through slots)
        Transform slot = null;
        for (int i = 0; i < _plantSlots.Length; i++)
        {
            int idx = (_nextSlotIndex + i) % _plantSlots.Length;
            if (_plantSlots[idx] != null)
            {
                slot = _plantSlots[idx];
                _nextSlotIndex = (idx + 1) % _plantSlots.Length;
                break;
            }
        }

        if (slot == null)
        {
            Debug.LogWarning("[LivingFlowerPlantManager] All plant slots are null.");
            if (trimmedVisual != null) Object.Destroy(trimmedVisual);
            return;
        }

        // Use cloned trimmed flower visuals if provided, otherwise procedural fallback
        GameObject plantGO;
        if (trimmedVisual != null)
        {
            plantGO = trimmedVisual;
            plantGO.name = $"LivingPlant_{characterName}";
            plantGO.SetActive(true);
            CreatePotWithSoil(plantGO);
        }
        else
        {
            plantGO = CreateProceduralPlant(characterName);
        }

        plantGO.transform.position = slot.position;
        plantGO.transform.rotation = slot.rotation;

        // Move out of DontDestroyOnLoad (if it was there for scene-unload survival)
        // so it gets cleaned up normally when leaving the apartment scene.
        SceneManager.MoveGameObjectToScene(plantGO, gameObject.scene);

        var plant = plantGO.AddComponent<LivingFlowerPlant>();
        int currentDay = GameClock.Instance != null ? GameClock.Instance.CurrentDay : 1;
        plant.Initialize(characterName, currentDay, daysAlive);

        // Configure water parameters from the first available PlantDefinition
        // (TODO: per-character plant definitions when more date characters are added)
        var plantDef = FindDefaultPlantDefinition();
        if (plantDef != null)
            plant.ConfigureWater(plantDef.thirstLevel, plantDef.waterTolerance,
                                 plantDef.dryingRatePerDay, plantDef.leafSheddingChance);

        // Add ReactableTag for date NPC reactions
        var reactable = plantGO.AddComponent<ReactableTag>();
        reactable.Setup(new[] { "plant", "flower", "gift" }, reactable.DisplayName);

        MakePlaceable(plantGO);

        // Make it waterable like other apartment plants
        var waterable = plantGO.GetComponent<WaterablePlant>();
        if (waterable == null) waterable = plantGO.AddComponent<WaterablePlant>();
        if (plantDef != null) waterable.definition = plantDef;

        // Lightweight breathing wobble instead of full physics joints
        if (plantGO.GetComponent<PlantBreathingWobble>() == null)
            plantGO.AddComponent<PlantBreathingWobble>();

        _activePlants.Add(plant);

        // Feed MoodMachine
        UpdateMoodSource();

        Debug.Log($"[LivingFlowerPlantManager] Spawned plant from {characterName} " +
                  $"({daysAlive} days) at {slot.position}" +
                  (trimmedVisual != null ? " [trimmed flower visual]" : " [procedural]"));

        // Temporary build-visible confirmation (remove later)
        CaptionDisplay.Show($"Flower from {characterName} placed ({daysAlive} days)", 3f);
    }

    /// <summary>Called by GameClock.OnDayStarted event each morning.</summary>
    public void AdvanceAllPlants()
    {
        // Get weather drying multiplier
        float dryMult = WeatherSystem.Instance != null
            ? WeatherSystem.Instance.GetDryingMultiplier()
            : 1f;

        for (int i = _activePlants.Count - 1; i >= 0; i--)
        {
            if (_activePlants[i] == null || _activePlants[i].IsDead)
            {
                _activePlants.RemoveAt(i);
                continue;
            }

            var plant = _activePlants[i];

            // Apply overnight water evaporation
            plant.ApplyOvernightDrying(dryMult);

            // Check watering consequences
            var waterState = plant.GetWaterState();
            if (waterState != LivingFlowerPlant.WaterState.Perfect)
            {
                // Leaf shedding (spawns trash near the plant)
                if (plant.ShouldShedLeaf() && AuthoredMessSpawner.Instance != null)
                {
                    Vector3 leafPos = plant.transform.position
                        + new Vector3(Random.Range(-0.15f, 0.15f), 0f, Random.Range(-0.15f, 0.15f));
                    // Spawn a small leaf trash item near the plant
                    // (uses the existing mess system — AuthoredMessSpawner handles cleanup)
                    Debug.Log($"[LivingFlowerPlantManager] {plant.CharacterName}'s plant shed a leaf ({waterState}).");
                }

                // Overwatered → spawn water spill stain near plant
                if (waterState == LivingFlowerPlant.WaterState.Overwatered || plant.OverflowedToday)
                {
                    Debug.Log($"[LivingFlowerPlantManager] {plant.CharacterName}'s plant overwatered — water spill spawned.");
                    // TODO: spawn CleanableSurface stain at plant base position
                }
            }

            // Advance general plant health (lifespan decay)
            plant.AdvanceDay();

            if (plant.IsDead)
                _activePlants.RemoveAt(i);
        }

        UpdateMoodSource();
        UpdateAirQuality();

        Debug.Log($"[LivingFlowerPlantManager] Advanced plants. {_activePlants.Count} alive. Weather drying: {dryMult:F1}x");
    }

    /// <summary>Feed air quality to MoodMachine based on plant watering health.</summary>
    private void UpdateAirQuality()
    {
        if (_activePlants.Count == 0)
        {
            MoodMachine.Instance?.RemoveSource("AirQuality");
            return;
        }

        float totalQuality = 0f;
        for (int i = 0; i < _activePlants.Count; i++)
        {
            if (_activePlants[i] != null)
                totalQuality += _activePlants[i].GetAirQualityContribution();
        }

        float avgQuality = totalQuality / _activePlants.Count;
        MoodMachine.Instance?.SetSource("AirQuality", avgQuality);
    }

    /// <summary>Get all living plant records for save data.</summary>
    public List<LivingPlantRecord> GetRecordsForSave()
    {
        var records = new List<LivingPlantRecord>();
        foreach (var plant in _activePlants)
        {
            if (plant != null && !plant.IsDead)
                records.Add(plant.ToRecord());
        }
        return records;
    }

    /// <summary>Restore plants from save data.</summary>
    public void LoadFromRecords(List<LivingPlantRecord> records)
    {
        if (records == null) return;

        foreach (var record in records)
        {
            var plantGO = CreateProceduralPlant(record.characterName);
            plantGO.transform.position = new Vector3(record.px, record.py, record.pz);

            var plant = plantGO.AddComponent<LivingFlowerPlant>();
            plant.Initialize(record.characterName, record.spawnDay, record.totalDaysAlive);
            plant.SetHealth(record.currentHealth);
            plant.RestoreWaterLevel(record.currentWaterLevel);

            // Restore water config from PlantDefinition
            var plantDef = FindDefaultPlantDefinition();
            if (plantDef != null)
                plant.ConfigureWater(plantDef.thirstLevel, plantDef.waterTolerance,
                                     plantDef.dryingRatePerDay, plantDef.leafSheddingChance);

            var reactable = plantGO.AddComponent<ReactableTag>();
            reactable.Setup(new[] { "plant", "flower", "gift" }, reactable.DisplayName);

            MakePlaceable(plantGO);

            // Set WaterablePlant definition for watering can interaction
            var waterable = plantGO.GetComponent<WaterablePlant>();
            if (waterable == null) waterable = plantGO.AddComponent<WaterablePlant>();
            if (plantDef != null) waterable.definition = plantDef;

            _activePlants.Add(plant);
        }

        UpdateMoodSource();
    }

    // ─── Internal ─────────────────────────────────────────────────

    private void UpdateMoodSource()
    {
        if (MoodMachine.Instance == null) return;

        if (_activePlants.Count == 0)
        {
            MoodMachine.Instance.RemoveSource("LivingPlants");
            return;
        }

        float totalHealth = 0f;
        foreach (var plant in _activePlants)
        {
            if (plant != null && !plant.IsDead)
                totalHealth += plant.Health;
        }

        float avgHealth = totalHealth / _activePlants.Count;
        MoodMachine.Instance.SetSource("LivingPlants", avgHealth);
    }

    /// <summary>
    /// Add pot (brown cube) and soil (dark disc) as children of the given parent.
    /// Strips auto-generated colliders from primitives so the root BoxCollider is authoritative.
    /// </summary>
    private void CreatePotWithSoil(GameObject parent)
    {
        // Pot (brown cube)
        var pot = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pot.name = "Pot";
        pot.transform.SetParent(parent.transform);
        pot.transform.localPosition = Vector3.zero;
        pot.transform.localScale = new Vector3(0.12f, 0.10f, 0.12f);
        Object.Destroy(pot.GetComponent<Collider>());
        var potRend = pot.GetComponent<Renderer>();
        if (potRend != null)
            potRend.sharedMaterial = GetSafeMaterial(new Color(0.55f, 0.35f, 0.20f));

        // Soil (dark brown disc sitting on top of pot)
        var soil = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        soil.name = "Soil";
        soil.transform.SetParent(parent.transform);
        soil.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        soil.transform.localScale = new Vector3(0.10f, 0.01f, 0.10f);
        Object.Destroy(soil.GetComponent<Collider>());
        var soilRend = soil.GetComponent<Renderer>();
        if (soilRend != null)
            soilRend.sharedMaterial = GetSafeMaterial(new Color(0.25f, 0.15f, 0.08f));
    }

    /// <summary>
    /// Make a plant GO grabbable: kinematic Rigidbody + auto-sized BoxCollider + PlaceableObject.
    /// Must be called AFTER all child visuals are parented.
    /// </summary>
    private void MakePlaceable(GameObject go)
    {
        // Set layer on GO + all children so ObjectGrabber raycast can hit it
        if (_plantLayer > 0)
            SetLayerRecursive(go, _plantLayer);

        // Rigidbody first (PlaceableObject requires it)
        var rb = go.GetComponent<Rigidbody>();
        if (rb == null) rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;

        // BoxCollider sized to encapsulate all child renderers
        var col = go.GetComponent<BoxCollider>();
        if (col == null) col = go.AddComponent<BoxCollider>();

        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            col.center = go.transform.InverseTransformPoint(bounds.center);
            col.size = bounds.size;
        }
        else
        {
            col.center = Vector3.zero;
            col.size = new Vector3(0.12f, 0.12f, 0.12f);
        }

        // PlaceableObject — defaults: category=General, no home zone
        go.AddComponent<PlaceableObject>();

        // InteractableHighlight for hover/gaze/display rim light on all child renderers
        go.AddComponent<InteractableHighlight>();
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    private static PlantDefinition FindDefaultPlantDefinition()
    {
        // Try Resources first, then fall back to any loaded PlantDefinition
        var def = Resources.Load<PlantDefinition>("DefaultPlantDefinition");
        if (def != null) return def;

        // Search all loaded SOs
        var all = Resources.FindObjectsOfTypeAll<PlantDefinition>();
        return all.Length > 0 ? all[0] : null;
    }

    private Material GetSafeMaterial(Color color)
    {
        if (_fallbackMaterial != null)
        {
            var mat = new Material(_fallbackMaterial);
            mat.color = color;
            return mat;
        }

        var shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard")
                     ?? Shader.Find("Sprites/Default");
        var fallback = new Material(shader);
        fallback.color = color;
        return fallback;
    }

    private GameObject CreateProceduralPlant(string characterName)
    {
        var plantGO = new GameObject($"LivingPlant_{characterName}");

        CreatePotWithSoil(plantGO);

        // Stem (green cylinder)
        var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        stem.name = "Stem";
        stem.transform.SetParent(plantGO.transform);
        stem.transform.localPosition = new Vector3(0f, 0.12f, 0f);
        stem.transform.localScale = new Vector3(0.02f, 0.10f, 0.02f);
        Object.Destroy(stem.GetComponent<Collider>());
        var stemRend = stem.GetComponent<Renderer>();
        if (stemRend != null)
            stemRend.sharedMaterial = GetSafeMaterial(new Color(0.3f, 0.7f, 0.2f));

        // Flower head (colored sphere)
        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "FlowerHead";
        head.transform.SetParent(plantGO.transform);
        head.transform.localPosition = new Vector3(0f, 0.24f, 0f);
        head.transform.localScale = new Vector3(0.08f, 0.06f, 0.08f);
        Object.Destroy(head.GetComponent<Collider>());
        var headRend = head.GetComponent<Renderer>();
        if (headRend != null)
        {
            int hash = characterName != null ? characterName.GetHashCode() : 0;
            float hue = Mathf.Abs(hash % 360) / 360f;
            headRend.sharedMaterial = GetSafeMaterial(Color.HSVToRGB(hue, 0.6f, 0.9f));
        }

        return plantGO;
    }
}
