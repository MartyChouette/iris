using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Large 2D overlay showing a pot cross-section during the watering pour.
/// Displays the pot shape silhouette, rising water, bubbly soil layer,
/// target line, and overflow effects. Built procedurally — no prefab needed.
///
/// The UI appears when the watering can snaps to a plant (magnetic engagement)
/// and fades out when the can disengages. A bottom-to-top reveal shader gives
/// a "plant growing" transition.
/// </summary>
public class PotCrossSectionUI : MonoBehaviour
{
    public static PotCrossSectionUI Instance { get; private set; }

    [Header("Layout")]
    [Tooltip("Height of the pot visual in pixels.")]
    [SerializeField] private float _potHeight = 400f;

    [Tooltip("Width of the pot visual in pixels.")]
    [SerializeField] private float _potWidth = 200f;

    [Header("Colors — Cozy Muted")]
    [SerializeField] private Color _potColor = new Color(0.545f, 0.659f, 0.533f, 0.95f);    // #8BA888 dusty sage
    [SerializeField] private Color _soilColor = new Color(0.36f, 0.29f, 0.196f, 0.9f);      // #5C4A32 dark earth
    [SerializeField] private Color _waterColor = new Color(0.42f, 0.659f, 0.627f, 0.75f);   // #6BA8A0 teal
    [SerializeField] private Color _foamColor = new Color(0.769f, 0.69f, 0.541f, 0.7f);     // #C4B08A sandy
    [SerializeField] private Color _targetLineColor = new Color(1f, 0.894f, 0.71f, 0.85f);  // #FFE4B5 warm white
    [SerializeField] private Color _targetBandColor = new Color(1f, 0.894f, 0.71f, 0.15f);  // warm white band
    [SerializeField] private Color _overflowColor = new Color(0.85f, 0.35f, 0.3f, 0.6f);    // muted red
    [SerializeField] private Color _bgDimColor = new Color(0f, 0f, 0f, 0.45f);

    [Header("Animation")]
    [SerializeField] private float _lerpSpeed = 6f;

    [Tooltip("How fast the reveal/hide animation plays (seconds).")]
    [SerializeField] private float _revealDuration = 0.4f;

    [Tooltip("How fast the hide animation plays (seconds).")]
    [SerializeField] private float _hideDuration = 0.25f;

    [Tooltip("Scale overshoot on reveal (1.0 = no overshoot, 1.15 = 15% bounce).")]
    [SerializeField] private float _scaleOvershoot = 1.08f;

    private GameObject _canvasRoot;
    private CanvasGroup _canvasGroup;
    private Image _bgDim;
    private RectTransform _potRT;
    private Image _potImage;
    private Image _waterImage;
    private RectTransform _waterRT;
    private Image _foamImage;
    private RectTransform _foamRT;
    private Image _targetBandImage;
    private RectTransform _targetBandRT;
    private Image _targetLineImage;
    private RectTransform _targetLineRT;
    private Image _overflowImage;
    private TMP_Text _plantNameText;
    private TMP_Text _statusText;

    private float _smoothWater;
    private float _smoothFoam;
    private bool _isShowing;
    private PlantDefinition.PotShape _currentShape;

    // ── Reveal animation state ──
    private enum RevealState { Hidden, Revealing, Shown, Hiding }
    private RevealState _revealState = RevealState.Hidden;
    private float _revealT; // 0 = hidden, 1 = fully revealed
    private Material _revealMat; // runtime instance of PlantRevealUI shader
    private static readonly int RevealProgressID = Shader.PropertyToID("_RevealProgress");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoSpawn()
    {
        if (Instance != null) return;
        var go = new GameObject("PotCrossSectionUI");
        go.AddComponent<PotCrossSectionUI>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildUI();
        _canvasRoot.SetActive(false);

        // Hide on any scene change so the UI doesn't persist to the main menu
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_revealMat != null) Destroy(_revealMat);
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    {
        Hide();
    }

    private PlantDefinition _activeDef;

    private void Update()
    {
        if (_revealState == RevealState.Hidden) return;

        // Animate reveal/hide
        UpdateRevealAnimation();
    }

    /// <summary>Show the pot cross-section for a specific plant.</summary>
    public void Show(PlantDefinition def, float startWaterLevel)
    {
        // Already showing for the same plant — don't restart animation
        if (_isShowing && _activeDef == def && _revealState != RevealState.Hiding)
            return;

        _isShowing = true;
        _activeDef = def;
        _currentShape = def != null ? def.potShape : PlantDefinition.PotShape.TallCylinder;
        _smoothWater = startWaterLevel;
        _smoothFoam = startWaterLevel;

        // Apply pot shape width
        ApplyPotShape();

        // Set target line position
        float target = def != null ? def.idealWaterLevel : 0.7f;
        float tolerance = def != null ? def.waterTolerance : 0.08f;
        SetTargetLine(target, tolerance);

        // Set water to starting level
        UpdateWaterVisual(_smoothWater);
        UpdateFoamVisual(_smoothFoam);

        // Plant name
        if (_plantNameText != null)
            _plantNameText.text = def != null ? def.plantName : "Plant";

        if (_statusText != null)
            _statusText.text = "Hold to pour...";

        SetOverflowing(false);

        // Start reveal animation — if interrupting a hide, keep current
        // progress for smooth reversal instead of snapping to zero
        _canvasRoot.SetActive(true);
        if (_revealState == RevealState.Hidden) _revealT = 0f;
        _revealState = RevealState.Revealing;
        ApplyRevealVisuals(_revealT);
    }

    /// <summary>Update water and soil bubble levels each frame during pour.</summary>
    public void UpdatePour(float waterLevel, float soilBubbleLevel)
    {
        _smoothWater = Mathf.Lerp(_smoothWater, waterLevel, Time.deltaTime * _lerpSpeed);
        _smoothFoam = Mathf.Lerp(_smoothFoam, soilBubbleLevel, Time.deltaTime * _lerpSpeed);

        UpdateWaterVisual(_smoothWater);
        UpdateFoamVisual(_smoothFoam);
        UpdateShapeWidths();
    }

    /// <summary>Show/hide overflow visual.</summary>
    public void SetOverflowing(bool overflow)
    {
        if (_overflowImage != null)
            _overflowImage.gameObject.SetActive(overflow);
    }

    /// <summary>Update status text.</summary>
    public void SetStatus(string text)
    {
        if (_statusText != null) _statusText.text = text;
    }

    /// <summary>Hide the overlay with a fade-out animation.</summary>
    public void Hide()
    {
        if (!_isShowing && _revealState == RevealState.Hidden) return;

        _isShowing = false;
        _activeDef = null;

        if (_revealState == RevealState.Hidden)
        {
            // Already hidden, just make sure
            if (_canvasRoot != null) _canvasRoot.SetActive(false);
            return;
        }

        // Start hide animation (reverse reveal)
        _revealState = RevealState.Hiding;
    }

    /// <summary>True if the UI is currently visible or animating in.</summary>
    public bool IsVisible => _isShowing || _revealState == RevealState.Revealing || _revealState == RevealState.Shown;

    // ── Reveal animation ────────────────────────────────────────────

    private void UpdateRevealAnimation()
    {
        switch (_revealState)
        {
            case RevealState.Revealing:
                _revealT += Time.deltaTime / Mathf.Max(_revealDuration, 0.01f);
                if (_revealT >= 1f)
                {
                    _revealT = 1f;
                    _revealState = RevealState.Shown;
                }
                ApplyRevealVisuals(_revealT);
                break;

            case RevealState.Hiding:
                _revealT -= Time.deltaTime / Mathf.Max(_hideDuration, 0.01f);
                if (_revealT <= 0f)
                {
                    _revealT = 0f;
                    _revealState = RevealState.Hidden;
                    if (_canvasRoot != null) _canvasRoot.SetActive(false);
                }
                ApplyRevealVisuals(_revealT);
                break;

            case RevealState.Shown:
                // Fully visible, nothing to animate
                break;
        }
    }

    private void ApplyRevealVisuals(float t)
    {
        // Smooth ease-out for reveal, ease-in for hide
        float eased = 1f - (1f - t) * (1f - t); // quadratic ease-out

        // Canvas group alpha (overall fade)
        if (_canvasGroup != null)
            _canvasGroup.alpha = Mathf.Clamp01(eased);

        // Pot scale — grows from bottom (pivot is at 0.5, 0)
        if (_potRT != null)
        {
            // Overshoot on scale Y: slightly beyond 1.0 then settle
            float scaleY;
            if (t < 1f)
            {
                // Elastic overshoot: ramp past 1.0 then settle
                float overshoot = 1f + (_scaleOvershoot - 1f) * Mathf.Sin(eased * Mathf.PI);
                scaleY = eased * overshoot;
            }
            else
            {
                scaleY = 1f;
            }
            _potRT.localScale = new Vector3(1f, Mathf.Clamp(scaleY, 0f, _scaleOvershoot + 0.1f), 1f);
        }

        // Reveal shader progress on pot background
        if (_revealMat != null)
            _revealMat.SetFloat(RevealProgressID, eased);

        // Bg dim follows alpha
        if (_bgDim != null)
        {
            var c = _bgDimColor;
            c.a = _bgDimColor.a * Mathf.Clamp01(eased);
            _bgDim.color = c;
        }
    }

    // ── Internal ────────────────────────────────────────────────────

    private void UpdateWaterVisual(float level)
    {
        if (_waterRT == null) return;
        float clamped = Mathf.Clamp01(level);
        _waterRT.anchorMax = new Vector2(1f, clamped);
    }

    private void UpdateFoamVisual(float level)
    {
        if (_foamRT == null || _waterRT == null) return;
        float foamTop = Mathf.Clamp01(level);

        // Foam starts from near the bottom of the water and covers up to
        // its own level — this HIDES the water line beneath the bubbly dirt.
        // The player can't see where water actually is, only the foam surface.
        float foamBottom = Mathf.Max(0f, _smoothWater - 0.08f); // foam dips slightly below water line
        if (foamTop <= foamBottom)
        {
            _foamImage.gameObject.SetActive(false);
            return;
        }
        _foamImage.gameObject.SetActive(true);
        _foamRT.anchorMin = new Vector2(0f, foamBottom);
        _foamRT.anchorMax = new Vector2(1f, foamTop);
    }

    /// <summary>
    /// Update the water and foam fill image widths to match the vase shape
    /// at their current height. This creates the visual effect of the vase
    /// being wider/narrower at different levels.
    /// </summary>
    private void UpdateShapeWidths()
    {
        if (_activeDef == null) return;

        // Water width matches vase shape at water level
        if (_waterRT != null)
        {
            float waterWidth = _activeDef.GetShapeWidth(_smoothWater);
            float inset = (1f - waterWidth) * _potWidth * 0.5f;
            _waterRT.offsetMin = new Vector2(Mathf.Max(4f, inset), 0f);
            _waterRT.offsetMax = new Vector2(-Mathf.Max(4f, inset), 0f);
        }

        // Foam width matches vase shape at foam level
        if (_foamRT != null)
        {
            float foamWidth = _activeDef.GetShapeWidth(_smoothFoam);
            float inset = (1f - foamWidth) * _potWidth * 0.5f;
            _foamRT.offsetMin = new Vector2(Mathf.Max(4f, inset), _foamRT.offsetMin.y);
            _foamRT.offsetMax = new Vector2(-Mathf.Max(4f, inset), _foamRT.offsetMax.y);
        }
    }

    private void SetTargetLine(float target, float tolerance)
    {
        if (_targetBandRT != null)
        {
            _targetBandRT.anchorMin = new Vector2(0f, Mathf.Clamp01(target - tolerance));
            _targetBandRT.anchorMax = new Vector2(1f, Mathf.Clamp01(target + tolerance));
        }
        if (_targetLineRT != null)
        {
            float y = Mathf.Clamp01(target);
            _targetLineRT.anchorMin = new Vector2(0f, y);
            _targetLineRT.anchorMax = new Vector2(1f, y);
        }
    }

    private void ApplyPotShape()
    {
        if (_potRT == null) return;

        // Base width at the widest point of each shape
        float maxWidth = _currentShape switch
        {
            PlantDefinition.PotShape.RoundBulb => _potWidth * 1.2f,
            PlantDefinition.PotShape.TaperedCone => _potWidth * 1.3f,
            PlantDefinition.PotShape.Hourglass => _potWidth * 1.1f,
            _ => _potWidth, // TallCylinder
        };
        _potRT.sizeDelta = new Vector2(maxWidth, _potHeight);
    }

    // ── Build UI ────────────────────────────────────────────────────

    private void BuildUI()
    {
        _canvasRoot = new GameObject("PotCrossSectionCanvas");
        _canvasRoot.transform.SetParent(transform, false);
        var canvas = _canvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        var scaler = _canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        _canvasRoot.AddComponent<GraphicRaycaster>();

        // CanvasGroup for overall alpha fade
        _canvasGroup = _canvasRoot.AddComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;
        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.interactable = false;

        // Background dim
        var dimGO = CreateUIElement("BgDim", _canvasRoot.transform);
        var dimRT = dimGO.GetComponent<RectTransform>();
        dimRT.anchorMin = Vector2.zero;
        dimRT.anchorMax = Vector2.one;
        dimRT.offsetMin = Vector2.zero;
        dimRT.offsetMax = Vector2.zero;
        _bgDim = dimGO.AddComponent<Image>();
        _bgDim.color = _bgDimColor;
        _bgDim.raycastTarget = false;

        // Pot container (centered, pivot at bottom for grow-up animation)
        var potGO = CreateUIElement("Pot", _canvasRoot.transform);
        _potRT = potGO.GetComponent<RectTransform>();
        _potRT.anchorMin = new Vector2(0.5f, 0.5f);
        _potRT.anchorMax = new Vector2(0.5f, 0.5f);
        _potRT.pivot = new Vector2(0.5f, 0f); // bottom pivot for grow-up
        _potRT.sizeDelta = new Vector2(_potWidth, _potHeight);
        _potRT.anchoredPosition = new Vector2(0f, -_potHeight * 0.5f); // offset so visual center stays mid-screen

        // Create reveal shader material for pot background
        _revealMat = CreateRevealMaterial();

        _potImage = potGO.AddComponent<Image>();
        _potImage.color = _potColor;
        _potImage.raycastTarget = false;
        if (_revealMat != null)
            _potImage.material = _revealMat;

        // Water fill (bottom-up inside pot)
        var waterGO = CreateUIElement("Water", potGO.transform);
        _waterRT = waterGO.GetComponent<RectTransform>();
        _waterRT.anchorMin = Vector2.zero;
        _waterRT.anchorMax = new Vector2(1f, 0f);
        _waterRT.offsetMin = new Vector2(4f, 0f);
        _waterRT.offsetMax = new Vector2(-4f, 0f);
        _waterImage = waterGO.AddComponent<Image>();
        _waterImage.color = _waterColor;
        _waterImage.raycastTarget = false;

        // Foam/soil bubble layer (sits on top of water)
        var foamGO = CreateUIElement("Foam", potGO.transform);
        _foamRT = foamGO.GetComponent<RectTransform>();
        _foamRT.anchorMin = Vector2.zero;
        _foamRT.anchorMax = Vector2.zero;
        _foamRT.offsetMin = new Vector2(4f, 0f);
        _foamRT.offsetMax = new Vector2(-4f, 0f);
        _foamImage = foamGO.AddComponent<Image>();
        _foamImage.color = _foamColor;
        _foamImage.raycastTarget = false;

        // Target band (tolerance zone)
        var bandGO = CreateUIElement("TargetBand", potGO.transform);
        _targetBandRT = bandGO.GetComponent<RectTransform>();
        _targetBandRT.anchorMin = new Vector2(0f, 0.62f);
        _targetBandRT.anchorMax = new Vector2(1f, 0.78f);
        _targetBandRT.offsetMin = Vector2.zero;
        _targetBandRT.offsetMax = Vector2.zero;
        _targetBandImage = bandGO.AddComponent<Image>();
        _targetBandImage.color = _targetBandColor;
        _targetBandImage.raycastTarget = false;

        // Target line (exact)
        var lineGO = CreateUIElement("TargetLine", potGO.transform);
        _targetLineRT = lineGO.GetComponent<RectTransform>();
        _targetLineRT.anchorMin = new Vector2(0f, 0.7f);
        _targetLineRT.anchorMax = new Vector2(1f, 0.7f);
        _targetLineRT.sizeDelta = new Vector2(0f, 3f);
        _targetLineImage = lineGO.AddComponent<Image>();
        _targetLineImage.color = _targetLineColor;
        _targetLineImage.raycastTarget = false;

        // Overflow indicator (top of pot, hidden by default)
        var overflowGO = CreateUIElement("Overflow", potGO.transform);
        var overflowRT = overflowGO.GetComponent<RectTransform>();
        overflowRT.anchorMin = new Vector2(0f, 0.95f);
        overflowRT.anchorMax = new Vector2(1f, 1.05f);
        overflowRT.offsetMin = Vector2.zero;
        overflowRT.offsetMax = Vector2.zero;
        _overflowImage = overflowGO.AddComponent<Image>();
        _overflowImage.color = _overflowColor;
        _overflowImage.raycastTarget = false;
        overflowGO.SetActive(false);

        // Plant name text (above pot)
        var nameGO = CreateUIElement("PlantName", _canvasRoot.transform);
        var nameRT = nameGO.GetComponent<RectTransform>();
        nameRT.anchorMin = new Vector2(0.5f, 0.5f);
        nameRT.anchorMax = new Vector2(0.5f, 0.5f);
        nameRT.pivot = new Vector2(0.5f, 0f);
        nameRT.anchoredPosition = new Vector2(0f, _potHeight * 0.5f + 20f);
        nameRT.sizeDelta = new Vector2(400f, 40f);
        _plantNameText = nameGO.AddComponent<TextMeshProUGUI>();
        _plantNameText.text = "Plant";
        _plantNameText.fontSize = 28f;
        _plantNameText.alignment = TextAlignmentOptions.Center;
        _plantNameText.color = Color.white;
        _plantNameText.raycastTarget = false;

        // Status text (below pot)
        var statusGO = CreateUIElement("Status", _canvasRoot.transform);
        var statusRT = statusGO.GetComponent<RectTransform>();
        statusRT.anchorMin = new Vector2(0.5f, 0.5f);
        statusRT.anchorMax = new Vector2(0.5f, 0.5f);
        statusRT.pivot = new Vector2(0.5f, 1f);
        statusRT.anchoredPosition = new Vector2(0f, -_potHeight * 0.5f - 10f);
        statusRT.sizeDelta = new Vector2(400f, 30f);
        _statusText = statusGO.AddComponent<TextMeshProUGUI>();
        _statusText.text = "";
        _statusText.fontSize = 20f;
        _statusText.alignment = TextAlignmentOptions.Center;
        _statusText.color = new Color(1f, 1f, 1f, 0.7f);
        _statusText.raycastTarget = false;
    }

    private static Material CreateRevealMaterial()
    {
        var shader = Shader.Find("UI/PlantRevealUI");
        if (shader == null)
        {
            Debug.LogWarning("[PotCrossSectionUI] PlantRevealUI shader not found — using default UI.");
            return null;
        }
        var mat = new Material(shader);
        mat.SetFloat(RevealProgressID, 0f);
        return mat;
    }

    private static GameObject CreateUIElement(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }
}
