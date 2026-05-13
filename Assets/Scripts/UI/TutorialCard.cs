using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Show/dismiss controller for the tutorial card overlay.
/// Activated by MainMenuManager before loading the apartment scene.
/// Fades in with a subtle scale animation, fades out on dismiss.
/// </summary>
public class TutorialCard : MonoBehaviour
{
    [SerializeField] private GameObject _root;
    [SerializeField] private Button _startButton;
    [SerializeField] private AudioClip _dismissSFX;

    [Header("Animation")]
    [Tooltip("Duration of the fade-in animation.")]
    [SerializeField] private float _fadeInDuration = 0.35f;

    [Tooltip("Duration of the fade-out animation.")]
    [SerializeField] private float _fadeOutDuration = 0.2f;

    [Tooltip("Starting scale for the pop-in (1.0 = no scale).")]
    [SerializeField] private float _scaleFrom = 0.92f;

    private Action _onDismiss;
    private CanvasGroup _canvasGroup;
    private RectTransform _panelRT;
    private Coroutine _animCoroutine;
    private GameObject _premiseText;
    private GameObject _controlsText;
    private bool _onControlsPage;
    private bool _spawnedByCode;

    private void Awake()
    {
        // Scene-placed instances stay disabled (legacy MainMenuManager reference).
        // Code-spawned instances skip this — ShowObjective sets _spawnedByCode first.
        if (_spawnedByCode) return;
        if (_root != null) _root.SetActive(false);
        gameObject.SetActive(false);
    }

    [Header("Placeholder")]
    [Tooltip("Optional PNG image to display. If null, shows 'PLACEHOLDER TUT' text.")]
    [SerializeField] private Sprite _tutorialImage;

    /// <summary>Show the tutorial card with fade-in. When the player clicks BEGIN, onDismiss is invoked.</summary>
    public void Show(Action onDismiss)
    {
        _onDismiss = onDismiss;

        // Ensure the GameObject is active so coroutines can run
        gameObject.SetActive(true);

        // Destroy any scene-wired card and build the runtime two-page version
        if (_root != null)
        {
            Destroy(_root);
            _root = null;
            _startButton = null;
        }
        BuildPlaceholderCard();

        if (_root != null) _root.SetActive(true);
        if (_startButton != null) _startButton.onClick.AddListener(OnStartClicked);

        if (_animCoroutine != null) StopCoroutine(_animCoroutine);

        if (AccessibilitySettings.ReduceMotion)
        {
            if (_canvasGroup != null) _canvasGroup.alpha = 1f;
            if (_panelRT != null) _panelRT.localScale = Vector3.one;
        }
        else
        {
            _animCoroutine = StartCoroutine(FadeIn());
        }
    }

    private void OnStartClicked()
    {
        // First click: swap premise → controls page
        if (!_onControlsPage && _premiseText != null && _controlsText != null)
        {
            _onControlsPage = true;
            _premiseText.SetActive(false);
            _controlsText.SetActive(true);
            if (_dismissSFX != null && AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX(_dismissSFX);
            return;
        }

        // Second click (or single-page): dismiss
        if (_startButton != null) _startButton.onClick.RemoveListener(OnStartClicked);

        if (_dismissSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(_dismissSFX);

        if (_animCoroutine != null) StopCoroutine(_animCoroutine);

        if (AccessibilitySettings.ReduceMotion)
            FinishDismiss();
        else
            _animCoroutine = StartCoroutine(FadeOut());
    }

    private void FinishDismiss()
    {
        if (_root != null) _root.SetActive(false);
        _onDismiss?.Invoke();
        _onDismiss = null;
    }

    // ── Placeholder builder ────────────────────────────────────────

    private void BuildPlaceholderCard()
    {
        _root = new GameObject("TutorialCardRoot");
        _root.transform.SetParent(transform, false);

        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        _root.AddComponent<GraphicRaycaster>();

        _canvasGroup = _root.AddComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;

        // Dim backdrop
        var bg = new GameObject("Backdrop");
        bg.transform.SetParent(_root.transform, false);
        var bgRT = bg.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one; bgRT.sizeDelta = Vector2.zero;
        var bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.75f);

        // Card panel
        var panel = new GameObject("CardPanel");
        panel.transform.SetParent(_root.transform, false);
        _panelRT = panel.AddComponent<RectTransform>();
        _panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        _panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        _panelRT.sizeDelta = new Vector2(700f, 500f);
        var panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0.95f, 0.93f, 0.88f, 1f);

        var theme = IrisTextTheme.Active;

        // Page 1: Premise
        _premiseText = new GameObject("PremisePage");
        _premiseText.transform.SetParent(panel.transform, false);
        var premRT = _premiseText.AddComponent<RectTransform>();
        premRT.anchorMin = new Vector2(0.08f, 0.18f);
        premRT.anchorMax = new Vector2(0.92f, 0.92f);
        premRT.sizeDelta = Vector2.zero;
        var premTMP = _premiseText.AddComponent<TextMeshProUGUI>();
        premTMP.text =
            "<size=110%><i>You made a date tonight,\ncalled a personal ad</i></size>\n\n" +
            "<size=95%>\"F, 25, $2,000/mo.\nSeeking a lover to give me " +
            "reasons to live,\nor move to Paris with and co-parent a cat.\"</size>";
        premTMP.fontSize = 26f;
        premTMP.alignment = TextAlignmentOptions.Center;
        premTMP.color = new Color(0.15f, 0.15f, 0.15f);
        if (theme != null && theme.primaryFont != null) premTMP.font = theme.primaryFont;

        // Page 2: Controls (hidden until first START click)
        _controlsText = new GameObject("ControlsPage");
        _controlsText.transform.SetParent(panel.transform, false);
        var ctrlRT = _controlsText.AddComponent<RectTransform>();
        ctrlRT.anchorMin = new Vector2(0.08f, 0.18f);
        ctrlRT.anchorMax = new Vector2(0.92f, 0.92f);
        ctrlRT.sizeDelta = Vector2.zero;
        var ctrlTMP = _controlsText.AddComponent<TextMeshProUGUI>();
        ctrlTMP.text =
            "<size=130%><b>Controls</b></size>\n\n" +
            "<b>[Click]</b>  Interact\n" +
            "<b>[Click + Hold]</b>  Pour\n" +
            "<b>[RMB]</b>  Back out of pouring\n" +
            "<b>[Scroll]</b>  Zoom in / out\n" +
            "<b>[Esc]</b>  Pause\n\n" +
            "<size=90%><b>While holding an item:</b></size>\n" +
            "<b>[RMB]</b>  Roll\n" +
            "<b>[Mouse Fwd]</b>  Rotate\n" +
            "<b>[Mouse Back]</b>  Flip";
        ctrlTMP.fontSize = 24f;
        ctrlTMP.alignment = TextAlignmentOptions.Left;
        ctrlTMP.color = new Color(0.15f, 0.15f, 0.15f);
        if (theme != null && theme.primaryFont != null) ctrlTMP.font = theme.primaryFont;
        _controlsText.SetActive(false);
        _onControlsPage = false;

        // START button
        var btnGO = new GameObject("StartBtn");
        btnGO.transform.SetParent(panel.transform, false);
        var btnRT = btnGO.AddComponent<RectTransform>();
        btnRT.anchorMin = new Vector2(0.5f, 0f);
        btnRT.anchorMax = new Vector2(0.5f, 0f);
        btnRT.pivot = new Vector2(0.5f, 0f);
        btnRT.anchoredPosition = new Vector2(0f, 15f);
        btnRT.sizeDelta = new Vector2(200f, 50f);
        var btnImg = btnGO.AddComponent<Image>();
        btnImg.color = new Color(0.2f, 0.2f, 0.22f);
        _startButton = btnGO.AddComponent<Button>();
        _startButton.targetGraphic = btnImg;

        var btnTxtGO = new GameObject("Label");
        btnTxtGO.transform.SetParent(btnGO.transform, false);
        var btnTxtRT = btnTxtGO.AddComponent<RectTransform>();
        btnTxtRT.anchorMin = Vector2.zero; btnTxtRT.anchorMax = Vector2.one; btnTxtRT.sizeDelta = Vector2.zero;
        var btnTMP = btnTxtGO.AddComponent<TextMeshProUGUI>();
        btnTMP.text = "START";
        btnTMP.fontSize = 24f;
        btnTMP.alignment = TextAlignmentOptions.Center;
        btnTMP.color = new Color(0.95f, 0.92f, 0.85f);
        if (theme != null && theme.primaryFont != null) btnTMP.font = theme.primaryFont;
    }

    // ── Static one-shot objective card ─────────────────────────────────

    /// <summary>
    /// Spawn a simple single-page tutorial card with the given message.
    /// Dismisses on click and destroys itself.
    /// </summary>
    public static void ShowObjective(string message, Action onDismiss = null)
    {
        // Create disabled so Awake doesn't kill it, then flag and re-enable
        var go = new GameObject("TutorialCard_Objective");
        go.SetActive(false);
        DontDestroyOnLoad(go);
        var card = go.AddComponent<TutorialCard>();
        card._spawnedByCode = true;
        go.SetActive(true);
        card._onDismiss = () =>
        {
            onDismiss?.Invoke();
            Destroy(go);
        };
        card.BuildObjectiveCard(message);

        if (AccessibilitySettings.ReduceMotion)
        {
            if (card._canvasGroup != null) card._canvasGroup.alpha = 1f;
            if (card._panelRT != null) card._panelRT.localScale = Vector3.one;
        }
        else
        {
            card._animCoroutine = card.StartCoroutine(card.FadeIn());
        }
    }

    private void BuildObjectiveCard(string message)
    {
        _root = new GameObject("ObjectiveCardRoot");
        _root.transform.SetParent(transform, false);

        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        _root.AddComponent<GraphicRaycaster>();

        _canvasGroup = _root.AddComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;

        // Dim backdrop
        var bg = new GameObject("Backdrop");
        bg.transform.SetParent(_root.transform, false);
        var bgRT = bg.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one; bgRT.sizeDelta = Vector2.zero;
        var bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.6f);

        // Card panel
        var panel = new GameObject("CardPanel");
        panel.transform.SetParent(_root.transform, false);
        _panelRT = panel.AddComponent<RectTransform>();
        _panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        _panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        _panelRT.sizeDelta = new Vector2(680f, 260f);
        var panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0.95f, 0.93f, 0.88f, 1f);

        var theme = IrisTextTheme.Active;

        // Message text
        var msgGO = new GameObject("Message");
        msgGO.transform.SetParent(panel.transform, false);
        var msgRT = msgGO.AddComponent<RectTransform>();
        msgRT.anchorMin = new Vector2(0.08f, 0.30f);
        msgRT.anchorMax = new Vector2(0.92f, 0.90f);
        msgRT.sizeDelta = Vector2.zero;
        var msgTMP = msgGO.AddComponent<TextMeshProUGUI>();
        msgTMP.text = message;
        msgTMP.fontSize = 28f;
        msgTMP.alignment = TextAlignmentOptions.Center;
        msgTMP.color = new Color(0.15f, 0.15f, 0.15f);
        if (theme != null && theme.primaryFont != null) msgTMP.font = theme.primaryFont;

        // OK button
        var btnGO = new GameObject("OKBtn");
        btnGO.transform.SetParent(panel.transform, false);
        var btnRT = btnGO.AddComponent<RectTransform>();
        btnRT.anchorMin = new Vector2(0.5f, 0f);
        btnRT.anchorMax = new Vector2(0.5f, 0f);
        btnRT.pivot = new Vector2(0.5f, 0f);
        btnRT.anchoredPosition = new Vector2(0f, 15f);
        btnRT.sizeDelta = new Vector2(160f, 44f);
        var btnImg = btnGO.AddComponent<Image>();
        btnImg.color = new Color(0.2f, 0.2f, 0.22f);
        _startButton = btnGO.AddComponent<Button>();
        _startButton.targetGraphic = btnImg;
        _startButton.onClick.AddListener(OnStartClicked);

        var btnTxtGO = new GameObject("Label");
        btnTxtGO.transform.SetParent(btnGO.transform, false);
        var btnTxtRT = btnTxtGO.AddComponent<RectTransform>();
        btnTxtRT.anchorMin = Vector2.zero; btnTxtRT.anchorMax = Vector2.one; btnTxtRT.sizeDelta = Vector2.zero;
        var btnTMP = btnTxtGO.AddComponent<TextMeshProUGUI>();
        btnTMP.text = "OK";
        btnTMP.fontSize = 22f;
        btnTMP.alignment = TextAlignmentOptions.Center;
        btnTMP.color = new Color(0.95f, 0.92f, 0.85f);
        if (theme != null && theme.primaryFont != null) btnTMP.font = theme.primaryFont;

        // No second page — single click dismisses
        _premiseText = null;
        _controlsText = null;
        _onControlsPage = true; // skip page-flip logic
    }

    // ── Animation ────────────────────────────────────────────────────

    private IEnumerator FadeIn()
    {
        if (_canvasGroup != null) _canvasGroup.alpha = 0f;
        if (_panelRT != null) _panelRT.localScale = Vector3.one * _scaleFrom;

        float elapsed = 0f;
        while (elapsed < _fadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _fadeInDuration);
            // Ease out cubic: fast start, gentle settle
            float eased = 1f - (1f - t) * (1f - t) * (1f - t);

            if (_canvasGroup != null) _canvasGroup.alpha = eased;
            if (_panelRT != null) _panelRT.localScale = Vector3.one * Mathf.Lerp(_scaleFrom, 1f, eased);
            yield return null;
        }

        if (_canvasGroup != null) _canvasGroup.alpha = 1f;
        if (_panelRT != null) _panelRT.localScale = Vector3.one;
        _animCoroutine = null;
    }

    private IEnumerator FadeOut()
    {
        float startAlpha = _canvasGroup != null ? _canvasGroup.alpha : 1f;
        float elapsed = 0f;

        while (elapsed < _fadeOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _fadeOutDuration);
            // Ease in quad: gentle start, fast finish
            float eased = t * t;

            if (_canvasGroup != null) _canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, eased);
            if (_panelRT != null) _panelRT.localScale = Vector3.one * Mathf.Lerp(1f, _scaleFrom, eased);
            yield return null;
        }

        _animCoroutine = null;
        FinishDismiss();
    }
}
