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

    public enum State { Idle, Pouring, Garnishing, Scoring, WaitingForDelivery, ChoosingGlass }

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

    private bool _glassHighlightActive;

    private void Update()
    {
        switch (CurrentState)
        {
            case State.Pouring:
                UpdatePouring();
                // Prompt to click the glass when hands are empty and liquid has been poured
                if (!ObjectGrabber.IsHoldingObject && _activeGlass != null && _activeGlass.TotalFill > 0f)
                {
                    if (!_glassHighlightActive)
                    {
                        _glassHighlightActive = true;
                        var hl = _activeGlass.GetComponent<InteractableHighlight>();
                        if (hl != null) hl.SetHighlighted(true);
                        PickupDescriptionHUD.Instance?.Show("Click the glass to finish the drink");
                    }
                }
                else if (_glassHighlightActive)
                {
                    _glassHighlightActive = false;
                    var hl = _activeGlass != null ? _activeGlass.GetComponent<InteractableHighlight>() : null;
                    if (hl != null) hl.SetHighlighted(false);
                    PickupDescriptionHUD.Instance?.Hide();
                }
                break;
            case State.Scoring:
                _scoreTimer -= Time.deltaTime;
                if (_scoreTimer <= 0f) EnterDeliveryMode();
                break;
            case State.WaitingForDelivery:
                // Update prompt based on whether the player is holding the glass
                if (ObjectGrabber.IsHoldingObject
                    && ObjectGrabber.HeldObject != null
                    && ObjectGrabber.HeldObject.GetComponent<DrinkGlass>() != null)
                {
                    string dateName = DateSessionManager.Instance?.CurrentDate?.characterName ?? "your date";
                    PickupDescriptionHUD.Instance?.Show($"Click on {dateName}!");
                }
                break;
        }
    }

    // ── Called by ObjectGrabber ──────────────────────────────────────

    /// <summary>Enter glass-choosing mode — highlight all glasses so the player picks one.</summary>
    public void BeginGlassChoice()
    {
        CurrentState = State.ChoosingGlass;
        HighlightAllGlasses(true);
        Debug.Log("[DrinkPourManager] Choose a glass to pour into.");
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
            _activeGlass = glass;
            _activeRecipe = FindRecipeForGlass(glass);
        }

        _pouringIngredient = ingredient;

        // Turn off glass highlights, turn on ingredient highlights
        if (CurrentState == State.ChoosingGlass)
            HighlightAllGlasses(false);

        if (CurrentState != State.Pouring)
        {
            CurrentState = State.Pouring;
            _overflowSFXPlayed = false;

            if (DrinkCutawayUI.Instance != null)
                DrinkCutawayUI.Instance.Show(glass, _activeRecipe);
        }
    }

    /// <summary>Stop the current pour (bottle moved away or released).</summary>
    public void StopPouring()
    {
        _pouringIngredient = null;
        // Stay in Pouring state — player can grab another bottle
    }

    /// <summary>Player signals they're done (clicks glass when not holding a bottle).</summary>
    public void FinishDrink()
    {
        if (CurrentState == State.Pouring || CurrentState == State.Garnishing)
        {
            // Clear the "click glass" prompt
            _glassHighlightActive = false;
            if (_activeGlass != null)
            {
                var hl = _activeGlass.GetComponent<InteractableHighlight>();
                if (hl != null) hl.SetHighlighted(false);
            }
            PickupDescriptionHUD.Instance?.Hide();

            CalculateScore();
            CurrentState = State.Scoring;
            _scoreTimer = _scoreDisplayTime;
        }
    }

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
    public void ServeGlass(DrinkGlass glass)
    {
        if (glass == null) return;
        if (glass.TotalFill <= 0f) return; // can't serve an empty glass

        _activeGlass = glass;
        _activeRecipe = FindRecipeForGlass(glass);

        _glassHighlightActive = false;
        HighlightAllGlasses(false);
        PickupDescriptionHUD.Instance?.Hide();

        CalculateScore();

        // Hand off to DateSessionManager — triggers reaction + phase 3
        DateSessionManager.Instance?.ReceiveDrink(_activeRecipe, _lastScore);

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

            // Overflow
            if (_activeGlass.IsOverflowing && !_overflowSFXPlayed)
            {
                _overflowSFXPlayed = true;
                if (overflowSFX != null && AudioManager.Instance != null)
                    AudioManager.Instance.PlaySFX(overflowSFX);
                DrinkCutawayUI.Instance?.SetStatus("Overflowing!");
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
        float overflowPenalty = _activeGlass.IsOverflowing ? -20f : 0f;

        float raw = orderScore + layerScore + fillScore + garnishBonus + overflowPenalty;
        _lastScore = Mathf.Clamp((int)raw, 0, _activeRecipe.baseScore);

        if (scoreSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(scoreSFX);

        if (_lastScore >= 80 && perfectSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(perfectSFX);

        DrinkCutawayUI.Instance?.SetStatus($"Score: {_lastScore}");

        Debug.Log($"[DrinkPourManager] Score: {_lastScore} (order={orderScore:F0} layer={layerScore:F0} fill={fillScore:F0} garnish={garnishBonus:F0} overflow={overflowPenalty:F0})");
    }

    /// <summary>0-30 points: were ingredients poured in the correct order?</summary>
    private float CalculateOrderScore()
    {
        if (_activeRecipe.ingredients == null || _activeRecipe.ingredients.Length == 0) return 30f;

        var order = _activeGlass.PourOrder;
        var expected = _activeRecipe.ingredients;
        int correct = 0;

        for (int i = 0; i < Mathf.Min(order.Count, expected.Length); i++)
        {
            if (order[i] == expected[i]) correct++;
        }

        if (correct == expected.Length) return 30f;
        if (correct >= expected.Length - 1) return 15f;
        return 0f;
    }

    /// <summary>0-40 points: how close is each layer to its target portion?</summary>
    private float CalculateLayerScore()
    {
        if (_activeRecipe.ingredients == null || _activeRecipe.portionNormalized == null)
            return 40f;

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

        return (totalAccuracy / count) * 40f;
    }

    /// <summary>0-20 points: how close is total fill to the target line?</summary>
    private float CalculateFillScore()
    {
        float totalFill = _activeGlass.TotalFill;
        float dist = Mathf.Abs(totalFill - _activeRecipe.idealFillLevel);
        float accuracy = Mathf.Clamp01(1f - dist / Mathf.Max(_activeRecipe.fillTolerance, 0.01f));
        return accuracy * 20f;
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

    // ── Delivery ────────────────────────────────────────────────────

    /// <summary>
    /// After scoring, re-highlight glasses so the player can click one to serve.
    /// </summary>
    private void EnterDeliveryMode()
    {
        DrinkCutawayUI.Instance?.Hide();
        _glassHighlightActive = false;
        CurrentState = State.WaitingForDelivery;

        HighlightAllGlasses(true);
        PickupDescriptionHUD.Instance?.Show("Click a glass to serve!");
        Debug.Log("[DrinkPourManager] Click a glass to serve.");
    }

    // ── Session management ──────────────────────────────────────────

    private void EndSession()
    {
        DrinkCutawayUI.Instance?.Hide();
        _activeGlass = null;
        _activeRecipe = null;
        _pouringIngredient = null;
        CurrentState = State.Idle;
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

    private void HighlightAllGlasses(bool on)
    {
        if (on) InteractableHighlight.SuppressVisuals = false;

        var glasses = DrinkGlass.All;
        for (int i = 0; i < glasses.Count; i++)
        {
            if (glasses[i] == null) continue;
            var hl = glasses[i].GetComponent<InteractableHighlight>();
            if (hl == null && on)
                hl = glasses[i].gameObject.AddComponent<InteractableHighlight>();
            if (hl != null) hl.SetHighlighted(on);
        }

        if (!on) InteractableHighlight.SuppressVisuals = true;
    }
}
