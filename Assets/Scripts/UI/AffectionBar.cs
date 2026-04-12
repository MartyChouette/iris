using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Affection meter displayed as a growing flower on the left side of the screen.
/// Shows during date phases, hidden otherwise. The stem grows from bottom to top
/// as affection increases, and the flower head blooms at the top.
/// Placeholder visuals — will be replaced with 2D/3D art later.
/// </summary>
public class AffectionBar : MonoBehaviour
{
    public static AffectionBar Instance { get; private set; }

    [Header("Layout")]
    [Tooltip("Total height of the flower meter in pixels.")]
    [SerializeField] private float _meterHeight = 200f;

    [Tooltip("Distance from left edge of screen.")]
    [SerializeField] private float _edgeMargin = 30f;

    [Tooltip("Vertical offset from center of screen.")]
    [SerializeField] private float _verticalOffset = -50f;

    [Header("Stem")]
    [Tooltip("Stem width in pixels.")]
    [SerializeField] private float _stemWidth = 4f;

    [Tooltip("Stem color.")]
    [SerializeField] private Color _stemColor = new Color(0.35f, 0.65f, 0.3f, 0.9f);

    [Header("Flower Head")]
    [Tooltip("Max flower head size in pixels (at 100% affection).")]
    [SerializeField] private float _flowerMaxSize = 32f;

    [Tooltip("Min flower head size in pixels (at 0% affection — tiny bud).")]
    [SerializeField] private float _flowerMinSize = 8f;

    [Tooltip("Flower color at low affection (bud).")]
    [SerializeField] private Color _budColor = new Color(0.5f, 0.7f, 0.4f, 0.9f);

    [Tooltip("Flower color at high affection (full bloom).")]
    [SerializeField] private Color _bloomColor = new Color(0.95f, 0.45f, 0.6f, 0.95f);

    [Header("Leaves")]
    [Tooltip("Leaf size in pixels.")]
    [SerializeField] private float _leafSize = 10f;

    [Tooltip("Leaf color.")]
    [SerializeField] private Color _leafColor = new Color(0.3f, 0.6f, 0.25f, 0.8f);

    [Header("Pot")]
    [Tooltip("Pot width in pixels.")]
    [SerializeField] private float _potWidth = 24f;

    [Tooltip("Pot height in pixels.")]
    [SerializeField] private float _potHeight = 18f;

    [Tooltip("Pot color.")]
    [SerializeField] private Color _potColor = new Color(0.65f, 0.35f, 0.2f, 0.9f);

    private GameObject _canvasRoot;
    private RectTransform _stemRT;
    private RectTransform _flowerRT;
    private Image _flowerImage;
    private RectTransform _leafLeftRT;
    private RectTransform _leafRightRT;
    private float _currentFill;
    private float _targetFill;
    private float _pulseTimer;
    private float _lastFill;
    private bool _visible;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoSpawn()
    {
        if (Instance != null) return;
        var go = new GameObject("AffectionBar");
        go.AddComponent<AffectionBar>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildUI();
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        if (DateSessionManager.Instance != null)
            DateSessionManager.Instance.OnAffectionChanged.AddListener(OnAffectionChanged);

        if (DayPhaseManager.Instance != null)
            DayPhaseManager.Instance.OnPhaseChanged.AddListener(OnPhaseChanged);
    }

    private void Update()
    {
        if (!_visible) return;

        _currentFill = Mathf.MoveTowards(_currentFill, _targetFill, Time.deltaTime * 1.5f);

        // Stem grows from bottom
        if (_stemRT != null)
            _stemRT.anchorMax = new Vector2(1f, _currentFill);

        // Flower head sits at top of stem, blooms as fill increases
        if (_flowerRT != null && _flowerImage != null)
        {
            float size = Mathf.Lerp(_flowerMinSize, _flowerMaxSize, _currentFill);

            // Juice: pulse the flower bigger when affection just increased
            if (_pulseTimer > 0f)
            {
                _pulseTimer -= Time.deltaTime;
                float pulse = Mathf.Sin(_pulseTimer / 0.4f * Mathf.PI * 3f); // 3 bounces
                size *= 1f + Mathf.Abs(pulse) * 0.3f; // up to 30% bigger during pulse
            }

            _flowerRT.sizeDelta = new Vector2(size, size);
            _flowerRT.anchoredPosition = new Vector2(0f, 0f);
            _flowerImage.color = Color.Lerp(_budColor, _bloomColor, _currentFill);
        }

        // Leaves appear at ~30% and ~60% stem height
        UpdateLeaf(_leafLeftRT, 0.3f, -1f);
        UpdateLeaf(_leafRightRT, 0.6f, 1f);
    }

    private void UpdateLeaf(RectTransform leaf, float threshold, float side)
    {
        if (leaf == null) return;
        bool show = _currentFill >= threshold;
        leaf.gameObject.SetActive(show);
        if (show)
        {
            float leafY = threshold * _meterHeight;
            leaf.anchoredPosition = new Vector2(side * (_stemWidth * 0.5f + _leafSize * 0.3f), leafY);
        }
    }

    private void OnAffectionChanged(float affection)
    {
        float newFill = Mathf.Clamp01(affection / 100f);
        // Trigger pulse when affection increases
        if (newFill > _targetFill + 0.01f)
            _pulseTimer = 0.4f;
        _targetFill = newFill;
    }

    private void OnPhaseChanged(int phaseInt)
    {
        var phase = (DayPhaseManager.DayPhase)phaseInt;
        SetVisible(phase == DayPhaseManager.DayPhase.DateInProgress);
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        if (_canvasRoot != null)
            _canvasRoot.SetActive(visible);
    }

    private void BuildUI()
    {
        _canvasRoot = new GameObject("AffectionFlowerCanvas");
        _canvasRoot.transform.SetParent(transform, false);
        var canvas = _canvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        var scaler = _canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        // Root container positioned on left side
        var rootGO = new GameObject("FlowerRoot");
        rootGO.transform.SetParent(_canvasRoot.transform, false);
        var rootRT = rootGO.AddComponent<RectTransform>();
        rootRT.anchorMin = new Vector2(0f, 0.5f);
        rootRT.anchorMax = new Vector2(0f, 0.5f);
        rootRT.pivot = new Vector2(0f, 0f);
        rootRT.anchoredPosition = new Vector2(_edgeMargin, _verticalOffset - _meterHeight * 0.5f);
        rootRT.sizeDelta = new Vector2(_stemWidth, _meterHeight);

        // Pot at bottom
        var potGO = new GameObject("Pot");
        potGO.transform.SetParent(rootGO.transform, false);
        var potRT = potGO.AddComponent<RectTransform>();
        potRT.anchorMin = new Vector2(0.5f, 0f);
        potRT.anchorMax = new Vector2(0.5f, 0f);
        potRT.pivot = new Vector2(0.5f, 1f);
        potRT.anchoredPosition = new Vector2(0f, 2f);
        potRT.sizeDelta = new Vector2(_potWidth, _potHeight);
        var potImg = potGO.AddComponent<Image>();
        potImg.color = _potColor;
        potImg.raycastTarget = false;

        // Pot rim (slightly wider strip at top of pot)
        var rimGO = new GameObject("PotRim");
        rimGO.transform.SetParent(potGO.transform, false);
        var rimRT = rimGO.AddComponent<RectTransform>();
        rimRT.anchorMin = new Vector2(0f, 1f);
        rimRT.anchorMax = new Vector2(1f, 1f);
        rimRT.pivot = new Vector2(0.5f, 0f);
        rimRT.anchoredPosition = Vector2.zero;
        rimRT.sizeDelta = new Vector2(6f, 4f);
        var rimImg = rimGO.AddComponent<Image>();
        rimImg.color = _potColor * 0.85f;
        rimImg.raycastTarget = false;

        // Stem (grows from bottom of root)
        var stemBGGO = new GameObject("StemBG");
        stemBGGO.transform.SetParent(rootGO.transform, false);
        var stemBGRT = stemBGGO.AddComponent<RectTransform>();
        stemBGRT.anchorMin = Vector2.zero;
        stemBGRT.anchorMax = Vector2.one;
        stemBGRT.offsetMin = Vector2.zero;
        stemBGRT.offsetMax = Vector2.zero;
        // Invisible container

        var stemGO = new GameObject("Stem");
        stemGO.transform.SetParent(stemBGGO.transform, false);
        _stemRT = stemGO.AddComponent<RectTransform>();
        _stemRT.anchorMin = Vector2.zero;
        _stemRT.anchorMax = new Vector2(1f, 0f); // grows upward
        _stemRT.offsetMin = Vector2.zero;
        _stemRT.offsetMax = Vector2.zero;
        var stemImg = stemGO.AddComponent<Image>();
        stemImg.color = _stemColor;
        stemImg.raycastTarget = false;

        // Flower head (circle at top of stem)
        var flowerGO = new GameObject("FlowerHead");
        flowerGO.transform.SetParent(stemGO.transform, false);
        _flowerRT = flowerGO.AddComponent<RectTransform>();
        _flowerRT.anchorMin = new Vector2(0.5f, 1f);
        _flowerRT.anchorMax = new Vector2(0.5f, 1f);
        _flowerRT.pivot = new Vector2(0.5f, 0.5f);
        _flowerRT.sizeDelta = new Vector2(_flowerMinSize, _flowerMinSize);
        _flowerImage = flowerGO.AddComponent<Image>();
        _flowerImage.color = _budColor;
        _flowerImage.raycastTarget = false;

        // Left leaf
        var leafLGO = new GameObject("LeafLeft");
        leafLGO.transform.SetParent(rootGO.transform, false);
        _leafLeftRT = leafLGO.AddComponent<RectTransform>();
        _leafLeftRT.anchorMin = new Vector2(0.5f, 0f);
        _leafLeftRT.anchorMax = new Vector2(0.5f, 0f);
        _leafLeftRT.pivot = new Vector2(1f, 0.5f);
        _leafLeftRT.sizeDelta = new Vector2(_leafSize, _leafSize * 0.6f);
        _leafLeftRT.localRotation = Quaternion.Euler(0f, 0f, 30f);
        var leafLImg = leafLGO.AddComponent<Image>();
        leafLImg.color = _leafColor;
        leafLImg.raycastTarget = false;
        leafLGO.SetActive(false);

        // Right leaf
        var leafRGO = new GameObject("LeafRight");
        leafRGO.transform.SetParent(rootGO.transform, false);
        _leafRightRT = leafRGO.AddComponent<RectTransform>();
        _leafRightRT.anchorMin = new Vector2(0.5f, 0f);
        _leafRightRT.anchorMax = new Vector2(0.5f, 0f);
        _leafRightRT.pivot = new Vector2(0f, 0.5f);
        _leafRightRT.sizeDelta = new Vector2(_leafSize, _leafSize * 0.6f);
        _leafRightRT.localRotation = Quaternion.Euler(0f, 0f, -30f);
        var leafRImg = leafRGO.AddComponent<Image>();
        leafRImg.color = _leafColor;
        leafRImg.raycastTarget = false;
        leafRGO.SetActive(false);
    }
}
