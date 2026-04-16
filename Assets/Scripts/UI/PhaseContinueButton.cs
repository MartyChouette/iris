using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Playtest-quality "Continue →" button shown between date phases.
/// Auto-spawns as a global singleton. Call Show(callback) to display,
/// Hide() to dismiss. The button disables itself on click to prevent
/// double-taps, then invokes the callback.
/// </summary>
public class PhaseContinueButton : MonoBehaviour
{
    public static PhaseContinueButton Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoSpawn()
    {
        if (Instance != null) return;
        var go = new GameObject("PhaseContinueButton");
        DontDestroyOnLoad(go);
        go.AddComponent<PhaseContinueButton>();
    }

    [Tooltip("Delay before the button fades in after Show() is called.")]
    [SerializeField] private float _showDelay = 0.5f;

    [Tooltip("Fade-in duration.")]
    [SerializeField] private float _fadeDuration = 0.35f;

    private Canvas _canvas;
    private CanvasGroup _canvasGroup;
    private Button _button;
    private System.Action _onClick;
    private Coroutine _fadeCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        BuildUI();
        _canvas.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Public API ──────────────────────────────────────────────────

    /// <summary>Show the button. Calls <paramref name="onClicked"/> once when pressed.</summary>
    public void Show(System.Action onClicked)
    {
        _onClick = onClicked;
        _canvas.gameObject.SetActive(true);
        _canvasGroup.interactable = true;
        _canvasGroup.blocksRaycasts = true;

        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);

        if (AccessibilitySettings.ReduceMotion)
        {
            _canvasGroup.alpha = 1f;
        }
        else
        {
            _canvasGroup.alpha = 0f;
            _fadeCoroutine = StartCoroutine(FadeIn());
        }
    }

    /// <summary>Hide the button immediately.</summary>
    public void Hide()
    {
        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = null;
        }
        _onClick = null;
        if (_canvas != null)
            _canvas.gameObject.SetActive(false);
    }

    // ── Build UI in code ────────────────────────────────────────────

    private void BuildUI()
    {
        // Canvas
        var canvasGO = new GameObject("PhaseContinueCanvas");
        canvasGO.transform.SetParent(transform, false);

        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 201;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        _canvasGroup = canvasGO.AddComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;

        // Button panel — bottom-right
        var btnGO = new GameObject("ContinueBtn");
        btnGO.transform.SetParent(canvasGO.transform, false);

        var btnRT = btnGO.AddComponent<RectTransform>();
        btnRT.anchorMin = new Vector2(1f, 0f);
        btnRT.anchorMax = new Vector2(1f, 0f);
        btnRT.pivot = new Vector2(1f, 0f);
        btnRT.anchoredPosition = new Vector2(-60f, 40f);
        btnRT.sizeDelta = new Vector2(220f, 50f);

        var btnImg = btnGO.AddComponent<Image>();
        btnImg.color = new Color(0.25f, 0.55f, 0.35f, 1f);

        _button = btnGO.AddComponent<Button>();
        _button.targetGraphic = btnImg;
        _button.onClick.AddListener(OnButtonClicked);

        // Label
        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(btnGO.transform, false);

        var labelRT = labelGO.AddComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;

        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text = "Continue \u2192";
        tmp.fontSize = 24f;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;

        var theme = IrisTextTheme.Active;
        if (theme != null && theme.primaryFont != null)
            tmp.font = theme.primaryFont;
    }

    // ── Internals ───────────────────────────────────────────────────

    private void OnButtonClicked()
    {
        _canvasGroup.interactable = false;
        _canvasGroup.blocksRaycasts = false;

        var cb = _onClick;
        _onClick = null;
        Hide();
        cb?.Invoke();
    }

    private System.Collections.IEnumerator FadeIn()
    {
        if (_showDelay > 0f)
            yield return new WaitForSeconds(_showDelay);

        float elapsed = 0f;
        while (elapsed < _fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _fadeDuration);
            _canvasGroup.alpha = t;
            yield return null;
        }
        _canvasGroup.alpha = 1f;
        _fadeCoroutine = null;
    }
}
