using UnityEngine;
using UnityEngine.UI;
using TMPro;

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
    [SerializeField] private float _meterHeight = 400f;

    [Tooltip("Distance from left edge of screen.")]
    [SerializeField] private float _edgeMargin = 40f;

    [Tooltip("Vertical offset from center of screen.")]
    [SerializeField] private float _verticalOffset = -60f;

    [Header("Stem")]
    [Tooltip("Stem width in pixels.")]
    [SerializeField] private float _stemWidth = 6f;

    [Tooltip("Stem color.")]
    [SerializeField] private Color _stemColor = new Color(0.35f, 0.65f, 0.3f, 0.9f);

    [Header("Flower Head")]
    [Tooltip("Max flower head size in pixels (at 100% affection).")]
    [SerializeField] private float _flowerMaxSize = 60f;

    [Tooltip("Min flower head size in pixels (at 0% affection — tiny bud).")]
    [SerializeField] private float _flowerMinSize = 14f;

    [Tooltip("Flower color at low affection (bud).")]
    [SerializeField] private Color _budColor = new Color(0.5f, 0.7f, 0.4f, 0.9f);

    [Tooltip("Flower color at high affection (full bloom).")]
    [SerializeField] private Color _bloomColor = new Color(0.95f, 0.45f, 0.6f, 0.95f);

    [Header("Leaves")]
    [Tooltip("Leaf size in pixels.")]
    [SerializeField] private float _leafSize = 16f;

    [Tooltip("Leaf color.")]
    [SerializeField] private Color _leafColor = new Color(0.3f, 0.6f, 0.25f, 0.8f);

    [Header("Pot")]
    [Tooltip("Pot width in pixels.")]
    [SerializeField] private float _potWidth = 36f;

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
    private float _wiltTimer;
    private float _lastFill;
    private bool _visible;
    private RectTransform _rootRT; // cached for popup spawn position

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

    private void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        TrySubscribe();
    }

    private void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        Unsubscribe();
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        // Hide on scene change — no date active until proven otherwise
        SetVisible(false);
        // Re-subscribe after scene changes — singletons may have been recreated
        StartCoroutine(SubscribeDeferred());
    }

    private System.Collections.IEnumerator SubscribeDeferred()
    {
        yield return null; // wait one frame for singletons to Awake
        TrySubscribe();
    }

    private bool _subscribed;

    private void TrySubscribe()
    {
        // Dedupe: both OnEnable and the deferred coroutine can call this.
        // RemoveListener is a no-op if not registered, so we remove first
        // then add to guarantee exactly one subscription.
        if (DateSessionManager.Instance != null)
        {
            DateSessionManager.Instance.OnAffectionChanged.RemoveListener(OnAffectionChanged);
            DateSessionManager.Instance.OnAffectionChanged.AddListener(OnAffectionChanged);
        }

        if (DayPhaseManager.Instance != null)
        {
            DayPhaseManager.Instance.OnPhaseChanged.RemoveListener(OnPhaseChanged);
            DayPhaseManager.Instance.OnPhaseChanged.AddListener(OnPhaseChanged);
        }
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (DateSessionManager.Instance != null)
            DateSessionManager.Instance.OnAffectionChanged.RemoveListener(OnAffectionChanged);

        if (DayPhaseManager.Instance != null)
            DayPhaseManager.Instance.OnPhaseChanged.RemoveListener(OnPhaseChanged);
        _subscribed = false;
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
            float rotation = 0f;

            // Celebration juice: big bouncy pulse when affection increases
            if (_pulseTimer > 0f)
            {
                _pulseTimer -= Time.deltaTime;
                float t = _pulseTimer / 0.8f;
                float pulse = Mathf.Sin(t * Mathf.PI * 5f) * Mathf.Pow(t, 0.5f); // 5 bounces, decaying
                size *= 1f + Mathf.Abs(pulse) * 0.6f; // up to 60% bigger
                rotation = pulse * 15f; // wobble rotation
            }

            // Wilt juice: droop and grey out when affection drops
            if (_wiltTimer > 0f)
            {
                _wiltTimer -= Time.deltaTime;
                float t = _wiltTimer / 0.6f;
                float droop = Mathf.Sin(t * Mathf.PI * 2f) * t;
                rotation = droop * -25f; // droop to the side
                size *= 1f - Mathf.Abs(droop) * 0.2f; // shrink slightly
            }

            _flowerRT.sizeDelta = new Vector2(size, size);
            _flowerRT.anchoredPosition = new Vector2(0f, 0f);
            _flowerRT.localRotation = Quaternion.Euler(0f, 0f, rotation);
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
        // Celebration pulse when affection increases
        if (newFill > _targetFill + 0.01f)
            _pulseTimer = 0.8f;
        // Wilt droop when affection decreases
        else if (newFill < _targetFill - 0.01f)
            _wiltTimer = 0.6f;
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

    /// <summary>
    /// Show a floating text popup that rises from the flower head and fades out.
    /// Call from anywhere: AffectionBar.Instance?.ShowPopup("Shoes +5", true);
    /// </summary>
    public void ShowPopup(string text, bool positive)
    {
        if (_canvasRoot == null || !_visible) return;
        StartCoroutine(RunPopup(text, positive));
    }

    private System.Collections.IEnumerator RunPopup(string text, bool positive)
    {
        var go = new GameObject("AffectionPopup");
        go.transform.SetParent(_rootRT != null ? _rootRT : _canvasRoot.transform, false);
        var rt = go.AddComponent<RectTransform>();
        // Spawn above the flower head
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(10f, 10f);
        rt.sizeDelta = new Vector2(200f, 30f);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 18f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.color = positive
            ? new Color(1f, 0.4f, 0.55f, 1f)   // warm pink for likes
            : new Color(0.55f, 0.5f, 0.6f, 1f); // muted lavender for dislikes
        tmp.raycastTarget = false;

        // Use the project's primary font if available
        var theme = IrisTextTheme.Active;
        if (theme != null && theme.primaryFont != null)
            tmp.font = theme.primaryFont;

        // Float up and fade out over 1.5 seconds
        float duration = 1.5f;
        Vector2 startPos = rt.anchoredPosition;
        float riseDistance = 60f;

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float norm = t / duration;
            rt.anchoredPosition = startPos + Vector2.up * (riseDistance * norm);
            // Quick fade in, slow fade out
            float alpha = norm < 0.1f ? norm / 0.1f : 1f - Mathf.Pow((norm - 0.1f) / 0.9f, 2f);
            tmp.color = new Color(tmp.color.r, tmp.color.g, tmp.color.b, alpha);

            // Scale pop on entry
            float scale = norm < 0.15f ? Mathf.Lerp(0.5f, 1.1f, norm / 0.15f)
                : norm < 0.25f ? Mathf.Lerp(1.1f, 1f, (norm - 0.15f) / 0.1f)
                : 1f;
            rt.localScale = Vector3.one * scale;

            yield return null;
        }

        Destroy(go);
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
        _rootRT = rootGO.AddComponent<RectTransform>();
        var rootRT = _rootRT;
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
