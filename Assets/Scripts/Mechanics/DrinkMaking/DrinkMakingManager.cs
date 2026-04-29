using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Scene-scoped singleton FSM for the drink-making prototype.
/// States: ChoosingRecipe → Pouring → Stirring → Scoring.
/// Handles all player input and delegates to <see cref="GlassController"/>,
/// <see cref="BottleController"/>, and <see cref="StirController"/>.
/// </summary>
[DisallowMultipleComponent]
public class DrinkMakingManager : MonoBehaviour, IStationManager
{
    public bool IsAtIdleState => currentState == State.ChoosingRecipe;

    public static DrinkMakingManager Instance { get; private set; }

    public enum State { ChoosingRecipe, Pouring, Stirring, Scoring }

    [Header("References")]
    [Tooltip("The glass simulation.")]
    public GlassController glass;

    [Tooltip("Stir quality detector.")]
    public StirController stirrer;

    [Tooltip("Main camera for raycasts.")]
    public Camera mainCamera;

    [Header("Bottles")]
    [Tooltip("All bottles in the scene (order matches counter layout).")]
    public BottleController[] bottles;

    [Header("Recipes")]
    [Tooltip("Available recipes the player can choose from.")]
    public DrinkRecipeDefinition[] availableRecipes;

    [Header("UI")]
    [Tooltip("HUD reference for state-driven display.")]
    public DrinkMakingHUD hud;

    [Header("Scoring")]
    [Tooltip("Points deducted for overflow.")]
    public float overflowPenalty = 30f;

    [Header("Audio")]
    public AudioClip pourCompleteSFX;
    public AudioClip stirCompleteSFX;
    public AudioClip scoreSFX;

    [Header("Events")]
    [Tooltip("Fired with the final score when a drink is scored.")]
    public UnityEvent<int> OnDrinkScored;

    [Header("Runtime (Read-Only)")]
    public State currentState = State.ChoosingRecipe;
    public DrinkRecipeDefinition activeRecipe;
    public int lastScore;

    // Input
    private InputAction _clickAction;
    private InputAction _mousePosition;

    // Pour state
    private BottleController _selectedBottle;
    private bool _isPouring;

    // Scoring breakdown (public for HUD)
    [HideInInspector] public float lastFillScore;
    [HideInInspector] public float lastOverflowScore;
    [HideInInspector] public float lastIngredientScore;
    [HideInInspector] public float lastStirScore;

    // ── Singleton lifecycle ────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        _clickAction = new InputAction("DrinkClick", InputActionType.Button, "<Mouse>/leftButton");
        _mousePosition = new InputAction("DrinkPointer", InputActionType.Value, "<Mouse>/position");

        if (mainCamera == null)
            mainCamera = Camera.main;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        _clickAction.Enable();
        _mousePosition.Enable();
    }

    void OnDisable()
    {
        _clickAction.Disable();
        _mousePosition.Disable();
    }

    // ── Update dispatch ────────────────────────────────────────────────

    void Update()
    {
        switch (currentState)
        {
            case State.ChoosingRecipe:
                UpdateChoosingRecipe();
                break;
            case State.Pouring:
                UpdatePouring();
                break;
            case State.Stirring:
                UpdateStirring();
                break;
            case State.Scoring:
                UpdateScoring();
                break;
        }
    }

    // ── ChoosingRecipe ─────────────────────────────────────────────────

    private void UpdateChoosingRecipe()
    {
        // Recipe selection is handled by UI buttons calling SelectRecipe().
    }

    /// <summary>Called by UI button to choose a recipe.</summary>
    public void SelectRecipe(int index)
    {
        if (index < 0 || index >= availableRecipes.Length) return;

        activeRecipe = availableRecipes[index];

        // Reset glass
        if (glass != null)
        {
            glass.definition = activeRecipe.requiredGlass;
            glass.Clear();
        }

        // Reset stirrer
        if (stirrer != null)
            stirrer.Reset();

        // Deselect all bottles
        DeselectAllBottles();

        // Glow the glass so the player knows where to pour
        if (glass != null) glass.EnableGlow();

        currentState = State.Pouring;
        Debug.Log($"[DrinkMakingManager] Selected recipe: {activeRecipe.drinkName}");
    }

    // ── Pouring ────────────────────────────────────────────────────────

    private void UpdatePouring()
    {
        if (activeRecipe == null || glass == null) return;

        Vector2 pointer = _mousePosition.ReadValue<Vector2>();

        // Click → select bottle or start pour on glass
        if (_clickAction.WasPressedThisFrame())
        {
            Ray ray = ApartmentManager.Instance != null ? ApartmentManager.Instance.ScreenPointToRay(pointer) : mainCamera.ScreenPointToRay(pointer);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                // Check if hit a bottle
                var bottle = hit.collider.GetComponent<BottleController>();
                if (bottle == null)
                    bottle = hit.collider.GetComponentInParent<BottleController>();

                if (bottle != null)
                {
                    SelectBottle(bottle);
                    return;
                }

                // Check if hit the glass and we have a bottle selected
                var glassHit = hit.collider.GetComponent<GlassController>();
                if (glassHit == null)
                    glassHit = hit.collider.GetComponentInParent<GlassController>();

                if (glassHit != null && _selectedBottle != null)
                {
                    BeginPour();
                }
            }
        }

        // Hold → continue pouring
        if (_isPouring && _selectedBottle != null)
        {
            if (_clickAction.IsPressed())
            {
                glass.Pour(_selectedBottle.ingredient, Time.deltaTime);
            }
            else
            {
                EndPour();
            }
        }
    }

    private void SelectBottle(BottleController bottle)
    {
        if (_isPouring) EndPour();

        DeselectAllBottles();
        _selectedBottle = bottle;
        _selectedBottle.Select();
        Debug.Log($"[DrinkMakingManager] Selected bottle: {bottle.ingredient?.ingredientName ?? "null"}");
    }

    private void DeselectAllBottles()
    {
        if (bottles == null) return;
        for (int i = 0; i < bottles.Length; i++)
        {
            if (bottles[i] != null)
                bottles[i].Deselect();
        }
        _selectedBottle = null;
    }

    private void BeginPour()
    {
        if (_selectedBottle == null) return;
        _isPouring = true;
        _selectedBottle.StartPour();
        if (glass != null) glass.SetPouring(true);
    }

    private void EndPour()
    {
        if (_selectedBottle != null)
            _selectedBottle.StopPour();

        if (glass != null)
        {
            glass.StopPouring();
            glass.SetPouring(false);
        }

        _isPouring = false;
    }

    /// <summary>Called by the "Done Pouring" UI button.</summary>
    public void FinishPouring()
    {
        if (currentState != State.Pouring) return;
        EndPour();
        DeselectAllBottles();

        if (AudioManager.Instance != null && pourCompleteSFX != null)
            AudioManager.Instance.PlaySFX(pourCompleteSFX);

        if (activeRecipe != null && activeRecipe.requiresStir)
        {
            if (stirrer != null) stirrer.Reset();
            currentState = State.Stirring;
            Debug.Log("[DrinkMakingManager] → Stirring");
        }
        else
        {
            CalculateScore();
        }
    }

    // ── Stirring ───────────────────────────────────────────────────────

    private void UpdateStirring()
    {
        if (activeRecipe == null || stirrer == null) return;

        if (_clickAction.IsPressed())
        {
            Vector2 pointer = _mousePosition.ReadValue<Vector2>();

            // Convert screen position to world position on the glass plane
            Ray ray = ApartmentManager.Instance != null ? ApartmentManager.Instance.ScreenPointToRay(pointer) : mainCamera.ScreenPointToRay(pointer);
            Plane glassPlane = new Plane(Vector3.forward,
                glass != null ? glass.transform.position : Vector3.zero);

            if (glassPlane.Raycast(ray, out float enter))
            {
                Vector3 worldPos = ray.GetPoint(enter);
                stirrer.UpdateStir(new Vector2(worldPos.x, worldPos.y), Time.deltaTime);
            }
        }

        // Auto-complete when sustained time is enough
        if (stirrer.SustainedTime >= activeRecipe.stirDuration)
        {
            if (AudioManager.Instance != null && stirCompleteSFX != null)
                AudioManager.Instance.PlaySFX(stirCompleteSFX);

            CalculateScore();
        }
    }

    /// <summary>Called by the "Done Stirring" UI button to skip/end early.</summary>
    public void FinishStirring()
    {
        if (currentState != State.Stirring) return;
        CalculateScore();
    }

    // ── Scoring ────────────────────────────────────────────────────────

    private void CalculateScore()
    {
        if (activeRecipe == null || glass == null)
        {
            currentState = State.Scoring;
            return;
        }

        var glassDef = activeRecipe.requiredGlass;

        // Fill score (0-50): how close liquid is to fill line
        float fillDist = Mathf.Abs(glass.LiquidLevel - glassDef.fillLineNormalized);
        float fillNorm = Mathf.Clamp01(1f - fillDist / Mathf.Max(glassDef.fillLineTolerance, 0.001f));
        lastFillScore = fillNorm * 50f;

        // Overflow penalty
        lastOverflowScore = glass.Overflowed ? -overflowPenalty : 0f;

        // Ingredient score (0-30): correct ingredients in glass
        lastIngredientScore = 30f; // simplified — any pour counts

        // Stir score (0-20)
        if (activeRecipe.requiresStir && stirrer != null)
            lastStirScore = stirrer.StirQuality * 20f;
        else
            lastStirScore = 20f;

        float raw = lastFillScore + lastIngredientScore + lastStirScore + lastOverflowScore;
        lastScore = Mathf.Clamp((int)raw, 0, activeRecipe.baseScore);

        if (AudioManager.Instance != null && scoreSFX != null)
            AudioManager.Instance.PlaySFX(scoreSFX);

        // Remove glow once scored
        if (glass != null) glass.DisableGlow();

        OnDrinkScored?.Invoke(lastScore);

        // Deliver drink to coffee table for date reaction
        CoffeeTableDelivery.Instance?.DeliverDrink(activeRecipe, glass.CurrentColor, lastScore);

        // Clear the glass so it doesn't linger in the kitchen after serving
        if (glass != null) glass.Clear();

        currentState = State.Scoring;
        Debug.Log($"[DrinkMakingManager] Score: {lastScore} (fill={lastFillScore:F0} ingr={lastIngredientScore:F0} stir={lastStirScore:F0} overflow={lastOverflowScore:F0})");
    }

    private void UpdateScoring()
    {
        // Scoring screen — "Next" / "Retry" handled by UI buttons.
    }

    /// <summary>Called by "Retry" button — replay the same recipe.</summary>
    public void Retry()
    {
        if (activeRecipe == null) return;
        int idx = System.Array.IndexOf(availableRecipes, activeRecipe);
        if (idx >= 0) SelectRecipe(idx);
    }

    /// <summary>Called by "Next" button — return to recipe selection.</summary>
    public void NextRecipe()
    {
        activeRecipe = null;
        DeselectAllBottles();
        if (glass != null) { glass.DisableGlow(); glass.Clear(); }
        if (stirrer != null) stirrer.Reset();
        currentState = State.ChoosingRecipe;
        Debug.Log("[DrinkMakingManager] → ChoosingRecipe");
    }
}
