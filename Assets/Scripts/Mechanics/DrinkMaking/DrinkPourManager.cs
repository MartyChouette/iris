using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Physical bottle-pour drink making system. Player grabs a bottle, brings it
/// near a glass (magnetic snap), holds click to pour. Colored liquid layers
/// fill the 2D cutaway. Garnishes are grabbed and dropped on the glass.
///
/// States: Idle → Pouring → Garnishing → Scoring
///
/// Replaces SimpleDrinkManager for the reworked Phase 2 drink interaction.
/// </summary>
public class DrinkPourManager : MonoBehaviour
{
    public static DrinkPourManager Instance { get; private set; }

    public enum State { Idle, ChoosingGlass, Pouring, Garnishing, Scoring, WaitingForDelivery, ChoosingServeGlass }

    [Header("Recipes")]
    [Tooltip("Available recipes the player can make.")]
    [SerializeField] private DrinkRecipeDefinition[] _recipes;

    [Header("Scoring")]
    [SerializeField] private float _scoreDisplayTime = 3f;

    [Header("Audio")]
    public AudioClip pourSFX;
    public AudioClip overflowSFX;
    public AudioClip scoreSFX;
    public AudioClip perfectSFX;

    // ── Public API ──────────────────────────────────────────────────

    public State CurrentState { get; private set; } = State.Idle;
    public DrinkGlass ActiveGlass => _activeGlass;
    public DrinkRecipeDefinition ActiveRecipe => _activeRecipe;

    // ── Runtime ─────────────────────────────────────────────────────

    private DrinkGlass _activeGlass;
    private DrinkRecipeDefinition _activeRecipe;
    private DrinkIngredientDefinition _pouringIngredient;
    private bool _overflowSFXPlayed;
    private float _scoreTimer;
    private int _lastScore;

    private InputAction _clickAction;

    // ── Lifecycle ───────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        _clickAction = new InputAction("DrinkClick", InputActionType.Button, "<Mouse>/leftButton");
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        _clickAction?.Dispose();
        _clickAction = null;
    }
    private void OnEnable() { _clickAction?.Enable(); }
    private void OnDisable() { _clickAction?.Disable(); }

    private void Update()
    {
        switch (CurrentState)
        {
            case State.Pouring:
                UpdatePouring();
                // Backspace/Delete dumps the glass
                if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete))
                    ResetActiveGlass();
                break;
            case State.ChoosingServeGlass:
                // Waiting for player to click a glass to serve
                break;
        }
    }

    // ── Called by ObjectGrabber ──────────────────────────────────────

    /// <summary>Step 1: highlight all glasses so the player picks one to begin.</summary>
    public void BeginGlassChoice()
    {
        // Unsuppress highlights once for the entire drink-making session
        InteractableHighlight.SuppressVisuals = false;
        CurrentState = State.ChoosingGlass;
        HighlightAllGlasses(true);
        Debug.Log("[DrinkPourManager] Step 1: Choose a glass.");
    }

    /// <summary>Called by ObjectGrabber when player clicks a glass during ChoosingGlass state.</summary>
    public void SelectGlass(DrinkGlass glass)
    {
        if (glass == null) return;
        _activeGlass = glass;
        _activeRecipe = FindRecipeForGlass(glass);

        HighlightAllGlasses(false);
        CurrentState = State.Pouring;
        _overflowSFXPlayed = false;

        if (DrinkCutawayUI.Instance != null)
            DrinkCutawayUI.Instance.Show(glass, _activeRecipe);

        // Highlight the first ingredient's bottle
        HighlightNextIngredientBottle();

        Debug.Log($"[DrinkPourManager] Glass selected — {glass.name}. Pour away!");
    }

    /// <summary>Step 3: player clicked Serve → highlight glasses for final choice.</summary>
    public void BeginServeChoice()
    {
        HighlightSingleGlass(_activeGlass, false);
        DrinkCutawayUI.Instance?.Hide();

        CurrentState = State.ChoosingServeGlass;
        HighlightAllGlasses(true);
        Debug.Log("[DrinkPourManager] Choose which glass to serve.");
    }

    /// <summary>Start pouring an ingredient into a glass.</summary>
    public void StartPouring(DrinkGlass glass, DrinkIngredientDefinition ingredient)
    {
        if (glass == null || ingredient == null) return;

        // Close the old recipe panel if it's open — the two systems are mutually exclusive
        SimpleDrinkManager.Instance?.HideRecipePanel();

        // Ensure glass has a highlight component for later prompts
        if (glass.GetComponent<InteractableHighlight>() == null)
            glass.gameObject.AddComponent<InteractableHighlight>();

        // Switch active glass (contents persist — never clear on switch)
        if (_activeGlass != glass)
        {
            // Un-highlight previous glass
            HighlightSingleGlass(_activeGlass, false);
            _activeGlass = glass;
            _activeRecipe = FindRecipeForGlass(glass);
        }

        _pouringIngredient = ingredient;

        // Clear bottle highlights once the player has grabbed one
        HighlightAllBottles(false);

        HighlightSingleGlass(glass, true);

        // Update cutaway UI if we switched glasses
        if (DrinkCutawayUI.Instance != null)
            DrinkCutawayUI.Instance.Show(glass, _activeRecipe);
    }

    /// <summary>Stop the current pour (bottle moved away or released).</summary>
    public void StopPouring()
    {
        _pouringIngredient = null;
        HighlightSingleGlass(_activeGlass, false);
        // Stay in Pouring state — player can grab another bottle

        // Update bottle highlight to next expected ingredient
        HighlightNextIngredientBottle();
    }

    /// <summary>Empty the active glass so the player can start over.</summary>
    public void ResetActiveGlass()
    {
        if (_activeGlass == null) return;
        _activeGlass.Clear();
        _overflowSFXPlayed = false;
        DrinkCutawayUI.Instance?.SetStatus("Glass emptied");
        HighlightNextIngredientBottle();
        Debug.Log("[DrinkPourManager] Glass reset.");
    }

    // FinishDrink removed — flow is now: pour freely → click Serve → choose glass

    /// <summary>Add a garnish to the active glass.</summary>
    public void AddGarnish(DrinkGarnishDefinition garnish)
    {
        if (_activeGlass == null) return;
        _activeGlass.AddGarnish(garnish);
        Debug.Log($"[DrinkPourManager] Garnish added: {garnish.garnishName}");
    }

    /// <summary>
    /// Click a glass to serve it to the date. Called from ObjectGrabber
    /// when the player clicks a glass during Pouring or WaitingForDelivery.
    /// </summary>
    /// <summary>
    /// Serve a specific glass to the date. Called from ObjectGrabber when
    /// the player clicks a glass during ChoosingServeGlass state.
    /// </summary>
    public void ServeGlass(DrinkGlass glass)
    {
        if (glass == null) return;
        if (glass.TotalFill <= 0f) return; // can't serve an empty glass

        _activeGlass = glass;
        _activeRecipe = FindRecipeForGlass(glass);

        HighlightAllGlasses(false);
        HighlightSingleGlass(_activeGlass, false);
        PickupDescriptionHUD.Instance?.Hide();

        CalculateScore();

        // Hand off to DateSessionManager — triggers reaction + phase 3
        // Pass the glass so DateSessionManager can hide it and spawn a dirty copy
        DateSessionManager.Instance?.ReceiveDrink(_activeRecipe, _lastScore, _activeGlass);

        DrinkCutawayUI.Instance?.Hide();
        _activeGlass = null;
        _activeRecipe = null;
        _pouringIngredient = null;
        CurrentState = State.Idle;

        Debug.Log($"[DrinkPourManager] Served glass — score {_lastScore}.");
    }

    /// <summary>Force back to idle (phase transitions).</summary>
    public void ForceIdle()
    {
        HighlightAllGlasses(false);
        HighlightAllBottles(false);
        HighlightSingleGlass(_activeGlass, false);
        // Re-suppress highlights (balanced with the unsuppress in BeginGlassChoice)
        InteractableHighlight.SuppressVisuals = true;
        DrinkCutawayUI.Instance?.Hide();
        _activeGlass = null;
        _activeRecipe = null;
        _pouringIngredient = null;
        CurrentState = State.Idle;
    }

    // ── Pouring ─────────────────────────────────────────────────────

    private void UpdatePouring()
    {
        if (_activeGlass == null) { EndSession(); return; }

        if (_pouringIngredient != null && _clickAction.IsPressed())
        {
            // Pour liquid into glass
            float rate = _pouringIngredient.pourRate;

            // Glass shape affects fill rate
            if (_activeGlass.Definition != null)
                rate *= _activeGlass.Definition.fillRateMultiplier;

            float delta = rate * Time.deltaTime;
            _activeGlass.AddLiquid(_pouringIngredient, delta);

            // Near-overflow warning
            if (_activeGlass.NearOverflow)
                DrinkCutawayUI.Instance?.SetNearOverflow(true);

            // Overflow
            if (_activeGlass.IsOverflowing && !_overflowSFXPlayed)
            {
                _overflowSFXPlayed = true;
                if (overflowSFX != null && AudioManager.Instance != null)
                    AudioManager.Instance.PlaySFX(overflowSFX);
                DrinkCutawayUI.Instance?.SetStatus("Overflowing!");
                DrinkCutawayUI.Instance?.SetNearOverflow(false);
            }
        }
        else
        {
            // Not pouring — settle foam
            _activeGlass.SettleFoam(0.2f);
        }

        // Update cutaway UI
        // (DrinkCutawayUI reads from ActiveGlass.Layers directly in its Update)
    }

    // ── Scoring ──────────────────────────────────────────────────────

    private void CalculateScore()
    {
        if (_activeGlass == null || _activeRecipe == null)
        {
            _lastScore = 0;
            return;
        }

        float orderScore = CalculateOrderScore();
        float layerScore = CalculateLayerScore();
        float fillScore = CalculateFillScore();
        float garnishBonus = CalculateGarnishBonus();
        float overflowPenalty = _activeGlass.IsOverflowing ? -15f : 0f;

        float raw = orderScore + layerScore + fillScore + garnishBonus + overflowPenalty;
        _lastScore = Mathf.Clamp((int)raw, 0, _activeRecipe.baseScore);

        if (scoreSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(scoreSFX);

        if (_lastScore >= 80 && perfectSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(perfectSFX);

        DrinkCutawayUI.Instance?.SetStatus($"Score: {_lastScore}");

        Debug.Log($"[DrinkPourManager] Score: {_lastScore} (order={orderScore:F0} layer={layerScore:F0} fill={fillScore:F0} garnish={garnishBonus:F0} overflow={overflowPenalty:F0})");
    }

    /// <summary>0-10 points bonus: were ingredients poured in the correct order?</summary>
    private float CalculateOrderScore()
    {
        if (_activeRecipe.ingredients == null || _activeRecipe.ingredients.Length == 0) return 10f;

        var order = _activeGlass.PourOrder;
        var expected = _activeRecipe.ingredients;
        int correct = 0;

        for (int i = 0; i < Mathf.Min(order.Count, expected.Length); i++)
        {
            if (order[i] == expected[i]) correct++;
        }

        if (correct == expected.Length) return 10f;
        if (correct >= expected.Length - 1) return 5f;
        return 0f;
    }

    /// <summary>0-50 points: how close is each layer to its target portion?</summary>
    private float CalculateLayerScore()
    {
        if (_activeRecipe.ingredients == null || _activeRecipe.portionNormalized == null)
            return 50f;

        float totalFill = _activeGlass.TotalFill;
        if (totalFill <= 0f) return 0f;

        float totalAccuracy = 0f;
        int count = Mathf.Min(_activeRecipe.ingredients.Length, _activeRecipe.portionNormalized.Length);

        for (int i = 0; i < count; i++)
        {
            float target = _activeRecipe.portionNormalized[i] * _activeRecipe.idealFillLevel;
            float actual = GetLayerAmount(_activeRecipe.ingredients[i]);
            float dist = Mathf.Abs(actual - target);
            float accuracy = Mathf.Clamp01(1f - dist / Mathf.Max(_activeRecipe.portionTolerance, 0.01f));
            totalAccuracy += accuracy;
        }

        return (totalAccuracy / count) * 50f;
    }

    /// <summary>0-30 points: how close is total fill to the target line?</summary>
    private float CalculateFillScore()
    {
        float totalFill = _activeGlass.TotalFill;
        float dist = Mathf.Abs(totalFill - _activeRecipe.idealFillLevel);
        float accuracy = Mathf.Clamp01(1f - dist / Mathf.Max(_activeRecipe.fillTolerance, 0.01f));
        return accuracy * 30f;
    }

    /// <summary>0-10 points: correct garnishes?</summary>
    private float CalculateGarnishBonus()
    {
        if (_activeRecipe.garnishes == null || _activeRecipe.garnishes.Length == 0) return 0f;

        float bonus = 0f;
        var addedGarnishes = _activeGlass.Garnishes;

        for (int i = 0; i < _activeRecipe.garnishes.Length; i++)
        {
            if (_activeRecipe.garnishes[i] != null && addedGarnishes.Contains(_activeRecipe.garnishes[i]))
                bonus += _activeRecipe.garnishes[i].bonusPoints;
        }

        return Mathf.Min(bonus, 10f);
    }

    private float GetLayerAmount(DrinkIngredientDefinition ingredient)
    {
        var layers = _activeGlass.Layers;
        for (int i = 0; i < layers.Count; i++)
        {
            if (layers[i].ingredient == ingredient)
                return layers[i].amount;
        }
        return 0f;
    }

    // ── Session management ──────────────────────────────────────────

    private void EndSession()
    {
        ForceIdle();
    }

    private DrinkRecipeDefinition FindRecipeForGlass(DrinkGlass glass)
    {
        if (_recipes == null || glass == null || glass.Definition == null) return null;
        for (int i = 0; i < _recipes.Length; i++)
        {
            if (_recipes[i] != null && _recipes[i].requiredGlass == glass.Definition)
                return _recipes[i];
        }
        return _recipes.Length > 0 ? _recipes[0] : null;
    }

    // ── Glass highlight helpers ─────────────────────────────────────
    // Each drink-making step uses a distinct highlight layer so the
    // player always knows exactly what to click:
    //   ChoosingGlass:      Display (soft blue)  — "pick a glass"
    //   Pouring glass:      Gaze    (teal)       — "this glass is active"
    //   Pouring bottle:     Gaze    (teal)       — "grab this next"
    //   ChoosingServeGlass: PrepLiked (cyan-green) — "serve this one"

    private void HighlightAllGlasses(bool on)
    {
        bool isServe = CurrentState == State.ChoosingServeGlass;
        var glasses = DrinkGlass.All;
        for (int i = 0; i < glasses.Count; i++)
        {
            if (glasses[i] == null) continue;
            var hl = glasses[i].GetComponent<InteractableHighlight>();
            if (hl == null && on)
                hl = glasses[i].gameObject.AddComponent<InteractableHighlight>();
            if (hl == null) continue;

            if (!on)
            {
                // Clear all drink-related layers cleanly
                hl.SetDisplayHighlighted(false);
                hl.SetPrepLikedHighlighted(false);
                hl.SetGazeHighlighted(false);
            }
            else if (isServe)
                hl.SetPrepLikedHighlighted(true);  // cyan-green = ready to serve
            else
                hl.SetDisplayHighlighted(true);     // soft blue = choose a glass
        }
    }

    private void HighlightSingleGlass(DrinkGlass glass, bool on)
    {
        if (glass == null) return;
        var hl = glass.GetComponent<InteractableHighlight>();
        if (hl == null && on)
            hl = glass.gameObject.AddComponent<InteractableHighlight>();
        if (hl != null) hl.SetGazeHighlighted(on); // teal = active glass
    }

    /// <summary>Highlight only the bottle for the next expected ingredient in the recipe.</summary>
    private void HighlightNextIngredientBottle()
    {
        if (_activeRecipe == null || _activeRecipe.ingredients == null || _activeGlass == null)
        {
            HighlightAllBottles(false);
            return;
        }

        int step = _activeGlass.PourOrder.Count;
        if (step >= _activeRecipe.ingredients.Length)
        {
            // All ingredients poured
            HighlightAllBottles(false);
            DrinkCutawayUI.Instance?.SetStatus("Ready to serve!");
            return;
        }

        var nextIngredient = _activeRecipe.ingredients[step];

        foreach (var po in PlaceableObject.All)
        {
            if (po == null) continue;
            var bottle = po.GetComponent<BottleItem>();
            if (bottle == null) continue;

            bool isNext = bottle.Ingredient == nextIngredient;
            var hl = po.GetComponent<InteractableHighlight>();
            if (isNext && hl == null)
                hl = po.gameObject.AddComponent<InteractableHighlight>();
            if (hl != null) hl.SetGazeHighlighted(isNext); // teal = grab this bottle
        }
    }

    private void HighlightAllBottles(bool on)
    {
        foreach (var po in PlaceableObject.All)
        {
            if (po == null) continue;
            var bottle = po.GetComponent<BottleItem>();
            if (bottle == null) continue;
            var hl = po.GetComponent<InteractableHighlight>();
            if (hl == null && on)
                hl = po.gameObject.AddComponent<InteractableHighlight>();
            if (hl != null) hl.SetGazeHighlighted(on);
        }
    }
}
