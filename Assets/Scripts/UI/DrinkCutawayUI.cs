using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Large 2D overlay showing a glass cross-section during drink pouring.
/// Displays colored liquid layers stacking up, foam, target fill line,
/// and glass shape silhouette. Heavier layers visually sit at the bottom.
/// </summary>
public class DrinkCutawayUI : MonoBehaviour
{
    public static DrinkCutawayUI Instance { get; private set; }

    [Header("Layout")]
    [SerializeField] private float _glassHeight = 350f;
    [SerializeField] private float _glassWidthHighball = 120f;
    [SerializeField] private float _glassWidthWine = 160f;

    [Header("Colors")]
    [SerializeField] private Color _glassColor = new Color(0.85f, 0.9f, 0.92f, 0.25f);
    [SerializeField] private Color _foamColor = new Color(0.95f, 0.92f, 0.85f, 0.5f);
    [SerializeField] private Color _targetLineColor = new Color(1f, 0.894f, 0.71f, 0.85f);
    [SerializeField] private Color _targetBandColor = new Color(1f, 0.894f, 0.71f, 0.15f);
    [SerializeField] private Color _overflowColor = new Color(0.85f, 0.35f, 0.3f, 0.6f);
    [SerializeField] private Color _bgDimColor = new Color(0f, 0f, 0f, 0.4f);

    [Header("Animation")]
    [SerializeField] private float _lerpSpeed = 6f;

    private GameObject _canvasRoot;
    private RectTransform _glassRT;
    private RectTransform _targetLineRT;
    private RectTransform _targetBandRT;
    private Image _overflowImage;
    private Image _foamImage;
    private RectTransform _foamRT;
    private TMP_Text _drinkNameText;
    private TMP_Text _statusText;
    private readonly List<(Image image, RectTransform rt)> _layerImages = new();

    private bool _isShowing;
    private DrinkGlass _activeGlass;
    private float _smoothFoam;

    // Glass shape masking
    private Image _glassImage;
    private Mask _glassMask;
    private Texture2D _silhouetteTexture;
    private Sprite _silhouetteSprite;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoSpawn()
    {
        if (Instance != null) return;
        var go = new GameObject("DrinkCutawayUI");
        go.AddComponent<DrinkCutawayUI>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildUI();
        _canvasRoot.SetActive(false);
    }

    private void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        // Hide drink UI when switching scenes (e.g. quit to main menu)
        Hide();
    }

    private void OnDestroy()
    {
        CleanupSilhouette();
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!_isShowing || _activeGlass == null) return;

        // Auto-dismiss if pour manager is idle or destroyed (scene change)
        if (DrinkPourManager.Instance == null
            || DrinkPourManager.Instance.CurrentState == DrinkPourManager.State.Idle)
        {
            Hide();
            return;
        }

        UpdateLayerVisuals();
    }

    // ── Public API ──────────────────────────────────────────────────

    public void Show(DrinkGlass glass, DrinkRecipeDefinition recipe)
    {
        _isShowing = true;
        _activeGlass = glass;
        _smoothFoam = glass.TotalFill;

        // Detect glass shape
        string glassName = recipe?.requiredGlass?.glassName ?? "";
        bool isWine = glassName.ToLower().Contains("wine");
        float width = isWine ? _glassWidthWine : _glassWidthHighball;
        if (_glassRT != null) _glassRT.sizeDelta = new Vector2(width, _glassHeight);

        // Apply silhouette mask for shaped glasses
        ApplyGlassShape(isWine);

        // Target line
        float target = recipe != null ? recipe.idealFillLevel : 0.75f;
        float tolerance = recipe != null ? recipe.fillTolerance : 0.1f;
        if (_targetLineRT != null)
        {
            _targetLineRT.anchorMin = new Vector2(0f, target);
            _targetLineRT.anchorMax = new Vector2(1f, target);
        }
        if (_targetBandRT != null)
        {
            _targetBandRT.anchorMin = new Vector2(0f, Mathf.Clamp01(target - tolerance));
            _targetBandRT.anchorMax = new Vector2(1f, Mathf.Clamp01(target + tolerance));
        }

        if (_drinkNameText != null)
            _drinkNameText.text = recipe != null ? recipe.drinkName : "Drink";

        SetOverflowing(false);
        _canvasRoot.SetActive(true);
    }

    public void Hide()
    {
        _isShowing = false;
        _activeGlass = null;
        if (_canvasRoot != null) _canvasRoot.SetActive(false);
    }

    public void SetStatus(string text)
    {
        if (_statusText != null) _statusText.text = text;
    }

    public void SetOverflowing(bool overflow)
    {
        if (_overflowImage != null) _overflowImage.gameObject.SetActive(overflow);
    }

    // ── Layer rendering ─────────────────────────────────────────────

    private void UpdateLayerVisuals()
    {
        if (_activeGlass == null) return;

        var layers = _activeGlass.Layers;

        // Ensure we have enough layer images
        while (_layerImages.Count < layers.Count)
            AddLayerImage();

        // Hide excess layers
        for (int i = layers.Count; i < _layerImages.Count; i++)
            _layerImages[i].image.gameObject.SetActive(false);

        // Position each layer: stack from bottom, each layer's height = its amount
        float bottomAnchor = 0f;
        for (int i = 0; i < layers.Count; i++)
        {
            var (img, rt) = _layerImages[i];
            float layerHeight = Mathf.Clamp01(layers[i].amount);

            rt.anchorMin = new Vector2(0f, bottomAnchor);
            rt.anchorMax = new Vector2(1f, Mathf.Clamp01(bottomAnchor + layerHeight));
            rt.offsetMin = new Vector2(4f, 0f);
            rt.offsetMax = new Vector2(-4f, 0f);

            Color targetColor = layers[i].ingredient.liquidColor;
            img.color = Color.Lerp(img.color, targetColor, Time.deltaTime * _lerpSpeed);
            img.gameObject.SetActive(true);

            bottomAnchor += layerHeight;
        }

        // Foam
        float fill = _activeGlass.TotalFill;
        _smoothFoam = Mathf.Lerp(_smoothFoam, _activeGlass.FoamLevel, Time.deltaTime * _lerpSpeed);
        if (_foamRT != null && _foamImage != null)
        {
            float foamBottom = Mathf.Clamp01(fill - 0.02f);
            float foamTop = Mathf.Clamp01(_smoothFoam);
            if (foamTop > foamBottom)
            {
                _foamRT.anchorMin = new Vector2(0f, foamBottom);
                _foamRT.anchorMax = new Vector2(1f, foamTop);
                _foamRT.offsetMin = new Vector2(4f, 0f);
                _foamRT.offsetMax = new Vector2(-4f, 0f);
                _foamImage.gameObject.SetActive(true);
            }
            else
            {
                _foamImage.gameObject.SetActive(false);
            }
        }

        // Overflow
        SetOverflowing(_activeGlass.IsOverflowing);
    }

    private void AddLayerImage()
    {
        var go = new GameObject($"Layer_{_layerImages.Count}");
        go.transform.SetParent(_glassRT, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.color = Color.clear;
        img.raycastTarget = false;
        // Insert below foam in sibling order
        go.transform.SetSiblingIndex(_glassRT.childCount - 3);
        _layerImages.Add((img, rt));
    }

    // ── Glass shape silhouettes ────────────────────────────────────

    /// <summary>
    /// Apply a silhouette mask so the glass cutaway matches the actual glass shape.
    /// Highball = plain rectangle (no mask). Wine = stem + bowl.
    /// </summary>
    private void ApplyGlassShape(bool isWine)
    {
        if (_glassImage == null)
            _glassImage = _glassRT.GetComponent<Image>();

        if (!isWine)
        {
            // Highball: plain rectangle — remove mask if it was set
            _glassImage.sprite = null;
            _glassImage.color = _glassColor;
            if (_glassMask != null) _glassMask.enabled = false;
            return;
        }

        // Wine glass: stem + bowl silhouette
        CleanupSilhouette();

        const int texW = 64;
        const int texH = 128;
        _silhouetteTexture = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
        _silhouetteTexture.filterMode = FilterMode.Bilinear;
        _silhouetteTexture.wrapMode = TextureWrapMode.Clamp;

        var pixels = new Color32[texW * texH];
        var transparent = new Color32(0, 0, 0, 0);
        var white = new Color32(255, 255, 255, 255);

        float halfW = texW * 0.5f;

        for (int y = 0; y < texH; y++)
        {
            float t = (float)y / (texH - 1); // 0 = bottom, 1 = top
            float halfWidth = WineGlassProfile(t) * halfW;

            for (int x = 0; x < texW; x++)
            {
                float distFromCenter = Mathf.Abs(x - halfW + 0.5f);
                // Soft edge (1px anti-alias)
                pixels[y * texW + x] = distFromCenter <= halfWidth ? white
                    : distFromCenter <= halfWidth + 1f
                        ? new Color32(255, 255, 255, (byte)(255 * (1f - (distFromCenter - halfWidth))))
                        : transparent;
            }
        }

        _silhouetteTexture.SetPixels32(pixels);
        _silhouetteTexture.Apply();

        _silhouetteSprite = Sprite.Create(
            _silhouetteTexture,
            new Rect(0, 0, texW, texH),
            new Vector2(0.5f, 0.5f),
            100f);

        _glassImage.sprite = _silhouetteSprite;
        _glassImage.type = Image.Type.Simple;
        _glassImage.preserveAspect = false;
        _glassImage.color = _glassColor;

        // Mask clips liquid layers and foam to the glass shape
        if (_glassMask == null)
            _glassMask = _glassRT.gameObject.AddComponent<Mask>();
        _glassMask.showMaskGraphic = true;
        _glassMask.enabled = true;
    }

    /// <summary>
    /// Wine glass profile: returns half-width multiplier (0-1) at normalized height t.
    /// 0 = bottom (base), 1 = top (rim).
    ///
    ///   rim  ──╮     ╭──   0.92 wide, slight flare
    ///          │     │
    ///   bowl   ╰─────╯     widest at ~0.55
    ///           │   │
    ///   stem    │   │      narrow
    ///          ─┴───┴─     base
    /// </summary>
    private static float WineGlassProfile(float t)
    {
        // Base (0.00 – 0.06): flat foot
        if (t < 0.06f) return 0.28f;
        // Base-to-stem taper (0.06 – 0.12)
        if (t < 0.12f) return Mathf.Lerp(0.28f, 0.07f, (t - 0.06f) / 0.06f);
        // Stem (0.12 – 0.32): thin
        if (t < 0.32f) return 0.07f;
        // Stem-to-bowl flare (0.32 – 0.50)
        if (t < 0.50f)
        {
            float s = (t - 0.32f) / 0.18f;
            return Mathf.Lerp(0.07f, 0.95f, s * s); // ease-in curve
        }
        // Bowl (0.50 – 0.75): widest
        if (t < 0.75f) return Mathf.Lerp(0.95f, 1.0f, (t - 0.50f) / 0.25f);
        // Bowl-to-rim taper (0.75 – 1.0): slight inward curve
        float r = (t - 0.75f) / 0.25f;
        return Mathf.Lerp(1.0f, 0.85f, r * r);
    }

    private void CleanupSilhouette()
    {
        if (_silhouetteSprite != null) { Destroy(_silhouetteSprite); _silhouetteSprite = null; }
        if (_silhouetteTexture != null) { Destroy(_silhouetteTexture); _silhouetteTexture = null; }
    }

    // ── Build UI ────────────────────────────────────────────────────

    private void BuildUI()
    {
        _canvasRoot = new GameObject("DrinkCutawayCanvas");
        _canvasRoot.transform.SetParent(transform, false);
        var canvas = _canvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        var scaler = _canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        // No GraphicRaycaster — this is a display-only overlay.
        // Adding one would block all game clicks via IsPointerOverGameObject.

        // Dim background
        var dimGO = CreateUI("BgDim", _canvasRoot.transform);
        var dimRT = dimGO.GetComponent<RectTransform>();
        dimRT.anchorMin = Vector2.zero; dimRT.anchorMax = Vector2.one;
        dimRT.offsetMin = Vector2.zero; dimRT.offsetMax = Vector2.zero;
        dimGO.AddComponent<Image>().color = _bgDimColor;

        // Glass container
        var glassGO = CreateUI("Glass", _canvasRoot.transform);
        _glassRT = glassGO.GetComponent<RectTransform>();
        _glassRT.anchorMin = new Vector2(0.5f, 0.5f);
        _glassRT.anchorMax = new Vector2(0.5f, 0.5f);
        _glassRT.pivot = new Vector2(0.5f, 0.5f);
        _glassRT.sizeDelta = new Vector2(_glassWidthHighball, _glassHeight);
        glassGO.AddComponent<Image>().color = _glassColor;

        // Target band
        var bandGO = CreateUI("TargetBand", glassGO.transform);
        _targetBandRT = bandGO.GetComponent<RectTransform>();
        _targetBandRT.anchorMin = new Vector2(0f, 0.65f);
        _targetBandRT.anchorMax = new Vector2(1f, 0.85f);
        _targetBandRT.offsetMin = Vector2.zero; _targetBandRT.offsetMax = Vector2.zero;
        bandGO.AddComponent<Image>().color = _targetBandColor;

        // Target line
        var lineGO = CreateUI("TargetLine", glassGO.transform);
        _targetLineRT = lineGO.GetComponent<RectTransform>();
        _targetLineRT.anchorMin = new Vector2(0f, 0.75f);
        _targetLineRT.anchorMax = new Vector2(1f, 0.75f);
        _targetLineRT.sizeDelta = new Vector2(0f, 3f);
        lineGO.AddComponent<Image>().color = _targetLineColor;

        // Foam (on top of layers)
        var foamGO = CreateUI("Foam", glassGO.transform);
        _foamRT = foamGO.GetComponent<RectTransform>();
        _foamImage = foamGO.AddComponent<Image>();
        _foamImage.color = _foamColor;
        _foamImage.gameObject.SetActive(false);

        // Overflow
        var overflowGO = CreateUI("Overflow", glassGO.transform);
        var overflowRT = overflowGO.GetComponent<RectTransform>();
        overflowRT.anchorMin = new Vector2(0f, 0.95f);
        overflowRT.anchorMax = new Vector2(1f, 1.05f);
        overflowRT.offsetMin = Vector2.zero; overflowRT.offsetMax = Vector2.zero;
        _overflowImage = overflowGO.AddComponent<Image>();
        _overflowImage.color = _overflowColor;
        overflowGO.SetActive(false);

        // Drink name
        var nameGO = CreateUI("DrinkName", _canvasRoot.transform);
        var nameRT = nameGO.GetComponent<RectTransform>();
        nameRT.anchorMin = new Vector2(0.5f, 0.5f); nameRT.anchorMax = new Vector2(0.5f, 0.5f);
        nameRT.pivot = new Vector2(0.5f, 0f);
        nameRT.anchoredPosition = new Vector2(0f, _glassHeight * 0.5f + 20f);
        nameRT.sizeDelta = new Vector2(400f, 40f);
        _drinkNameText = nameGO.AddComponent<TextMeshProUGUI>();
        _drinkNameText.text = ""; _drinkNameText.fontSize = 28f;
        _drinkNameText.alignment = TextAlignmentOptions.Center;
        _drinkNameText.color = Color.white;

        // Status
        var statusGO = CreateUI("Status", _canvasRoot.transform);
        var statusRT = statusGO.GetComponent<RectTransform>();
        statusRT.anchorMin = new Vector2(0.5f, 0.5f); statusRT.anchorMax = new Vector2(0.5f, 0.5f);
        statusRT.pivot = new Vector2(0.5f, 1f);
        statusRT.anchoredPosition = new Vector2(0f, -_glassHeight * 0.5f - 10f);
        statusRT.sizeDelta = new Vector2(400f, 30f);
        _statusText = statusGO.AddComponent<TextMeshProUGUI>();
        _statusText.text = ""; _statusText.fontSize = 20f;
        _statusText.alignment = TextAlignmentOptions.Center;
        _statusText.color = new Color(1f, 1f, 1f, 0.7f);
    }

    private static GameObject CreateUI(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }
}
