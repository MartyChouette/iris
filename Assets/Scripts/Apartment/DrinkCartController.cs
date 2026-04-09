using UnityEngine;

/// <summary>
/// Clickable drink cart in the kitchen. Opens the SimpleDrinkManager recipe
/// panel during Phase 2 (BackgroundJudging). Replaces the fridge as the
/// drink-making trigger — the fridge is now pure storage.
/// </summary>
public class DrinkCartController : MonoBehaviour
{
    public static DrinkCartController Instance { get; private set; }

    [Header("Interaction")]
    [Tooltip("Layer mask for the drink cart collider.")]
    [SerializeField] private LayerMask _cartLayer;

    [Tooltip("Main camera used for raycasting.")]
    [SerializeField] private Camera _mainCamera;

    [Header("Wall Occlusion")]
    [Tooltip("Layer mask for walls that can block clicks.")]
    [SerializeField] private LayerMask _wallOcclusionLayer;

    [Header("Audio")]
    [Tooltip("SFX played when the cart is activated.")]
    [SerializeField] private AudioClip _activateSFX;

    private InteractableHighlight _highlight;
    private bool _guideActive;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (DayPhaseManager.Instance != null && !DayPhaseManager.Instance.IsInteractionPhase) return;

        bool isDrinkPhase = DateSessionManager.Instance != null
            && DateSessionManager.Instance.CurrentDatePhase == DateSessionManager.DatePhase.BackgroundJudging;

        // Rim guide during drink phase
        if (isDrinkPhase)
            UpdateGuide();
        else if (_guideActive)
            StopGuide();

        // Only clickable during drink phase
        if (!isDrinkPhase) return;

        if (IrisInput.Instance == null || !IrisInput.Instance.Click.WasPressedThisFrame()) return;
        if (ObjectGrabber.IsHoldingObject) return;
        if (ObjectGrabber.ClickConsumedThisFrame) return;
        if (_mainCamera == null) return;

        // Don't open if already pouring or scoring
        if (SimpleDrinkManager.Instance != null
            && SimpleDrinkManager.Instance.CurrentState != SimpleDrinkManager.State.ChoosingRecipe)
            return;

        Vector2 mousePos = IrisInput.CursorPosition;
        var ray = _mainCamera.ScreenPointToRay(mousePos);

        if (!Physics.Raycast(ray, out var cartHit, 20f, _cartLayer)) return;

        // Wall occlusion
        if (_wallOcclusionLayer.value != 0
            && Physics.Raycast(ray, out var wallHit, 20f, _wallOcclusionLayer)
            && wallHit.distance < cartHit.distance)
            return;

        // Open the recipe panel
        if (_activateSFX != null)
            AudioManager.Instance?.PlaySFX(_activateSFX);

        StopGuide();
        SimpleDrinkManager.Instance?.ShowRecipePanel();
        ObjectGrabber.ConsumeClickExternal();

        Debug.Log("[DrinkCartController] Drink cart clicked — showing recipe panel.");
    }

    // ── Rim Guide ──────────────────────────────────────────────
    // Steady highlight on the cart until the player selects a recipe.

    private void UpdateGuide()
    {
        bool shouldGuide = SimpleDrinkManager.Instance == null
            || (SimpleDrinkManager.Instance.CurrentState == SimpleDrinkManager.State.ChoosingRecipe
                && SimpleDrinkManager.Instance.ActiveRecipe == null);

        if (shouldGuide && !_guideActive)
        {
            if (_highlight == null)
                _highlight = GetComponent<InteractableHighlight>();
            if (_highlight != null)
                _highlight.SetHighlighted(true);
            _guideActive = true;
        }
        else if (!shouldGuide && _guideActive)
        {
            StopGuide();
        }
    }

    private void StopGuide()
    {
        if (_highlight != null)
            _highlight.SetHighlighted(false);
        _guideActive = false;
    }
}
