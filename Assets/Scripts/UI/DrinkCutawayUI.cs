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

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Update()
    {
        if (!_isShowing || _activeGlass == null) return;

        // Auto-dismiss if pour manager is idle
        if (DrinkPourManager.Instance != null
            && DrinkPourManager.Instance.CurrentState == DrinkPourManager.State.Idle)
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

        // Glass width based on type
        bool isWine = recipe != null && recipe.requiredGlass != null
            && recipe.requiredGlass.glassName.ToLower().Contains("wine");
        float width = isWine ? _glassWidthWine : _glassWidthHighball;
        if (_glassRT != null) _glassRT.sizeDelta = new Vector2(width, _glassHeight);

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
        _canvasRoot.AddComponent<GraphicRaycaster>();

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
