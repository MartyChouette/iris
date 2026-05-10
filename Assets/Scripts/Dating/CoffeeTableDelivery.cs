using UnityEngine;

/// <summary>
/// Auto-delivers drinks to the coffee table when a drink is scored.
/// Spawns a visual cup and notifies DateSessionManager. At the end of a
/// date the visual cup is promoted to a real "dirty glass" PlaceableObject
/// that the player must carry to the sink during Evening clean-up.
/// </summary>
public class CoffeeTableDelivery : MonoBehaviour
{
    public static CoffeeTableDelivery Instance { get; private set; }

    [Header("References")]
    [Tooltip("Where the drink cup appears.")]
    [SerializeField] private Transform drinkSpawnPoint;

    [Tooltip("Prefab for the drink cup visual. If null, a primitive is created.")]
    [SerializeField] private GameObject drinkCupPrefab;

    [Header("Dirty Glass Promotion")]
    [Tooltip("Layer index assigned to the dirty glass so ObjectGrabber can click and pick it up. Should match the project's placeable layer (15 in the default scene).")]
    [SerializeField] private int _dirtyGlassLayer = 15;

    [Tooltip("Tint applied to the liquid when the drink becomes a dirty glass — murky / leftover-looking.")]
    [SerializeField] private Color _dirtyLiquidTint = new Color(0.3f, 0.22f, 0.18f, 1f);

    [Header("Audio")]
    [SerializeField] private AudioClip deliverSFX;

    private GameObject _currentDrink;
    private Material _drinkMat;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[CoffeeTableDelivery] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnEnable()
    {
        // Subscribe after a frame so DateSessionManager has finished its Awake.
        StartCoroutine(SubscribeDateEnded());
    }

    private System.Collections.IEnumerator SubscribeDateEnded()
    {
        yield return null;
        if (DateSessionManager.Instance != null)
            DateSessionManager.Instance.OnDateSessionEnded.AddListener(OnDateEnded);
    }

    private void OnDisable()
    {
        if (DateSessionManager.Instance != null)
            DateSessionManager.Instance.OnDateSessionEnded.RemoveListener(OnDateEnded);
    }

    private void OnDateEnded(DatePersonalDefinition date, float finalAffection)
    {
        PromoteToDirtyGlass();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Place a drink on the coffee table and notify the date.</summary>
    public void DeliverDrink(DrinkRecipeDefinition recipe, Color liquidColor, int score)
    {
        ClearDrink();

        Vector3 spawnPos = drinkSpawnPoint != null ? drinkSpawnPoint.position : transform.position;

        if (drinkCupPrefab != null)
        {
            _currentDrink = Instantiate(drinkCupPrefab, spawnPos, Quaternion.identity);
        }
        else
        {
            // Fallback: small cylinder as cup
            _currentDrink = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _currentDrink.name = "DrinkCup";
            _currentDrink.transform.position = spawnPos;
            _currentDrink.transform.localScale = new Vector3(0.08f, 0.06f, 0.08f);
        }

        // Tint the cup to liquid color (track material to avoid leak)
        var rend = _currentDrink.GetComponent<Renderer>();
        if (rend == null) rend = _currentDrink.GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            // Always create from a known-good URP Lit shader to avoid broken materials
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            _drinkMat = new Material(shader);
            _drinkMat.SetColor("_BaseColor", liquidColor);
            _drinkMat.color = liquidColor;
            // Ensure surface type is opaque
            _drinkMat.SetFloat("_Surface", 0f);
            rend.material = _drinkMat;
        }

        if (deliverSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(deliverSFX);

        // Notify DateSessionManager
        DateSessionManager.Instance?.ReceiveDrink(recipe, score);

#if UNITY_EDITOR
        Debug.Log($"[CoffeeTableDelivery] Delivered {recipe?.drinkName ?? "drink"} (score={score})");
#endif
    }

    /// <summary>Remove the current drink from the table.</summary>
    public void ClearDrink()
    {
        if (_drinkMat != null)
        {
            Destroy(_drinkMat);
            _drinkMat = null;
        }
        if (_currentDrink != null)
        {
            Destroy(_currentDrink);
            _currentDrink = null;
        }
    }

    /// <summary>
    /// Called when the date session ends. Takes the coffee-table drink cup
    /// (if one is still there) and turns it into a real pickable "dirty
    /// glass" PlaceableObject in the Dish category with a Sink home zone,
    /// so the player has to walk it to the sink during Evening clean-up.
    /// If there's no cup on the table (player never made a drink, or the
    /// date left early), this is a no-op.
    /// </summary>
    public void PromoteToDirtyGlass()
    {
        if (_currentDrink == null) return;

        var glass = _currentDrink;
        glass.name = "DirtyGlass";
        glass.layer = _dirtyGlassLayer;
        // Make sure every child is on the same layer so raycasts and overlap
        // checks hit the glass regardless of which mesh collider the cursor
        // lands on.
        foreach (Transform t in glass.GetComponentsInChildren<Transform>(includeInactive: true))
            t.gameObject.layer = _dirtyGlassLayer;

        // Dim the liquid to a murky, dirty color so visually the glass reads
        // as leftover / used.
        if (_drinkMat != null)
        {
            _drinkMat.SetColor("_BaseColor", _dirtyLiquidTint);
            _drinkMat.color = _dirtyLiquidTint;
        }
        else
        {
            var rend = glass.GetComponent<Renderer>();
            if (rend == null) rend = glass.GetComponentInChildren<Renderer>();
            if (rend != null && rend.sharedMaterial != null)
            {
                rend.material.SetColor("_BaseColor", _dirtyLiquidTint);
                rend.material.color = _dirtyLiquidTint;
            }
        }

        // Ensure there's a collider so it can be clicked and physics-queried.
        if (glass.GetComponent<Collider>() == null
            && glass.GetComponentInChildren<Collider>() == null)
        {
            var col = glass.AddComponent<BoxCollider>();
            var r = glass.GetComponentInChildren<Renderer>();
            if (r != null)
            {
                col.center = r.bounds.center - glass.transform.position;
                col.size = r.bounds.size;
            }
        }

        // Rigidbody before PlaceableObject — PlaceableObject has a
        // RequireComponent(Rigidbody, Collider) attribute and will add them
        // if missing, but adding explicitly lets us tune mass up front.
        var rb = glass.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = glass.AddComponent<Rigidbody>();
            rb.mass = 0.15f;
            rb.angularDamping = 0.5f;
        }

        // Tag it for date reactions + dish category routing.
        var reactable = glass.GetComponent<ReactableTag>();
        if (reactable == null)
        {
            reactable = glass.AddComponent<ReactableTag>();
            reactable.Setup(new[] { "dirty_glass", "dish", "leftover" }, "Dirty Glass");
            reactable.SmellAmount = 0.1f;
        }

        // Core placeable wiring: Dish category routes through the Sink
        // destroy-on-deposit zone, and the home zone name matches the Sink
        // DropZone so the highlight pulses when the player holds the glass.
        var placeable = glass.GetComponent<PlaceableObject>();
        if (placeable == null)
            placeable = glass.AddComponent<PlaceableObject>();

        // Use reflection to set private serialized fields at runtime since
        // they don't have public setters.
        SetPrivateField(placeable, "_itemCategory", ItemCategory.Dish);
        SetPrivateField(placeable, "_homeZoneName", "Sink");
        SetPrivateField(placeable, "_itemDescription", "Leftover glass from date night.");
        SetPrivateField(placeable, "_smellPerDay", 0.15f);

        // Add interaction highlight so the cursor system lights it up on hover.
        if (glass.GetComponent<ItemHighlight>() == null)
            glass.AddComponent<ItemHighlight>();

        Debug.Log("[CoffeeTableDelivery] Drink cup promoted to DirtyGlass (→ Sink).");

        // Release our reference — the glass is now a free-standing item
        // managed by the regular PlaceableObject / ObjectGrabber systems.
        _currentDrink = null;
        _drinkMat = null;
    }

    /// <summary>Reflection helper to set a private serialized field on a component at runtime.</summary>
    private static void SetPrivateField(Component target, string fieldName, object value)
    {
        if (target == null) return;
        var f = target.GetType().GetField(fieldName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (f != null) f.SetValue(target, value);
    }
}
