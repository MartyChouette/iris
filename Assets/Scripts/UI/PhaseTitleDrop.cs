using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Epic phase title drop — big raw-font text that appears over the live scene,
/// holds, then fades out. Auto-spawns its own overlay canvas.
/// </summary>
public class PhaseTitleDrop : MonoBehaviour
{
    public static PhaseTitleDrop Instance { get; private set; }

    [Header("Timing")]
    [Tooltip("Seconds for the title to fade in.")]
    [SerializeField] private float _fadeInDuration = 0.3f;

    [Tooltip("Seconds the title holds at full opacity.")]
    [SerializeField] private float _holdDuration = 2.0f;

    [Tooltip("Seconds for the title to fade out.")]
    [SerializeField] private float _fadeOutDuration = 1.5f;

    [Header("Easing")]
    [SerializeField] private AnimationCurve _fadeInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private AnimationCurve _fadeOutCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("Style")]
    [Tooltip("Font size for the title.")]
    [SerializeField] private float _fontSize = 96f;

    [Tooltip("Text color.")]
    [SerializeField] private Color _textColor = new Color(0.95f, 0.92f, 0.85f, 1f);

    [Tooltip("Drop shadow color.")]
    [SerializeField] private Color _shadowColor = new Color(0f, 0f, 0f, 0.6f);

    [Header("Tilt Shift")]
    [Tooltip("Maximum tilt-shift blur strength during the title drop.")]
    [SerializeField] private float _tiltShiftStrength = 1f;

    [Tooltip("Focus band center (0.5 = screen center).")]
    [SerializeField] private float _tiltShiftCenter = 0.5f;

    [Tooltip("Half-width of the sharp focus band in UV space.")]
    [SerializeField] private float _tiltShiftWidth = 0.15f;

    [Tooltip("Max blur radius in texels.")]
    [SerializeField] private float _tiltShiftRadius = 8f;

    private Canvas _canvas;
    private TMP_Text _titleText;
    private TMP_Text _shadowText;
    private CanvasGroup _group;
    private Coroutine _activeRoutine;

    private static readonly int TiltAmountID = Shader.PropertyToID("_TiltShiftAmount");
    private static readonly int TiltCenterID = Shader.PropertyToID("_TiltShiftCenter");
    private static readonly int TiltWidthID  = Shader.PropertyToID("_TiltShiftWidth");
    private static readonly int TiltRadiusID = Shader.PropertyToID("_TiltShiftRadius");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoSpawn()
    {
        if (Instance != null) return;
        var go = new GameObject("PhaseTitleDrop");
        go.AddComponent<PhaseTitleDrop>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildUI();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    {
        if (_activeRoutine != null) { StopCoroutine(_activeRoutine); _activeRoutine = null; }
        if (_group != null) _group.alpha = 0f;
    }

    private void BuildUI()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 90; // below ScreenFade (100) so fade covers it

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;

        // Shadow text (offset down-right)
        var shadowGO = new GameObject("Shadow");
        shadowGO.transform.SetParent(transform, false);
        var shadowRT = shadowGO.AddComponent<RectTransform>();
        shadowRT.anchorMin = Vector2.zero;
        shadowRT.anchorMax = Vector2.one;
        shadowRT.offsetMin = new Vector2(3f, -3f);
        shadowRT.offsetMax = new Vector2(3f, -3f);
        _shadowText = shadowGO.AddComponent<TextMeshProUGUI>();
        _shadowText.fontSize = _fontSize;
        _shadowText.alignment = TextAlignmentOptions.Center;
        _shadowText.color = new Color(0f, 0f, 0f, 0.35f);
        _shadowText.fontStyle = FontStyles.Normal;
        _shadowText.textWrappingMode = TMPro.TextWrappingModes.NoWrap;

        // Main title text
        var titleGO = new GameObject("Title");
        titleGO.transform.SetParent(transform, false);
        var titleRT = titleGO.AddComponent<RectTransform>();
        titleRT.anchorMin = Vector2.zero;
        titleRT.anchorMax = Vector2.one;
        titleRT.offsetMin = Vector2.zero;
        titleRT.offsetMax = Vector2.zero;
        _titleText = titleGO.AddComponent<TextMeshProUGUI>();
        _titleText.fontSize = _fontSize;
        _titleText.alignment = TextAlignmentOptions.Center;
        _titleText.color = _textColor;
        _titleText.fontStyle = FontStyles.Normal;
        _titleText.textWrappingMode = TMPro.TextWrappingModes.NoWrap;

        // Apply game font
        var theme = IrisTextTheme.Active;
        if (theme != null && theme.primaryFont != null)
        {
            _titleText.font = theme.primaryFont;
            _shadowText.font = theme.primaryFont;
        }

        // Disable TMP material underlay — the font material has a built-in
        // underlay that stacks with our manual shadow GO, causing double text.
        DisableUnderlay(_titleText);
        DisableUnderlay(_shadowText);
    }

    private static void DisableUnderlay(TMP_Text text)
    {
        // Create instance material so we don't modify the shared font asset
        text.fontMaterial = new Material(text.fontMaterial);
        text.fontMaterial.SetFloat("_UnderlayDilate", -1f);
        text.fontMaterial.SetColor("_UnderlayColor", Color.clear);
    }

    /// <summary>Show an epic title drop over the live scene.</summary>
    public Coroutine Show(string text)
    {
        if (_activeRoutine != null)
            StopCoroutine(_activeRoutine);
        _activeRoutine = StartCoroutine(TitleSequence(text));
        return _activeRoutine;
    }

    private IEnumerator TitleSequence(string text)
    {
        _titleText.text = text;
        _shadowText.text = text;

        // Set tilt-shift parameters
        Shader.SetGlobalFloat(TiltCenterID, _tiltShiftCenter);
        Shader.SetGlobalFloat(TiltWidthID, _tiltShiftWidth);
        Shader.SetGlobalFloat(TiltRadiusID, _tiltShiftRadius);

        // Fade in (title + tilt-shift together)
        float elapsed = 0f;
        while (elapsed < _fadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _fadeInDuration);
            _group.alpha = _fadeInCurve.Evaluate(t);
            Shader.SetGlobalFloat(TiltAmountID, _fadeInCurve.Evaluate(t) * _tiltShiftStrength);
            yield return null;
        }
        _group.alpha = 1f;
        Shader.SetGlobalFloat(TiltAmountID, _tiltShiftStrength);

        // Hold
        yield return new WaitForSecondsRealtime(_holdDuration);

        // Fade out (title + tilt-shift together)
        elapsed = 0f;
        while (elapsed < _fadeOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _fadeOutDuration);
            _group.alpha = _fadeOutCurve.Evaluate(t);
            Shader.SetGlobalFloat(TiltAmountID, _fadeOutCurve.Evaluate(t) * _tiltShiftStrength);
            yield return null;
        }
        _group.alpha = 0f;
        Shader.SetGlobalFloat(TiltAmountID, 0f);
        _activeRoutine = null;
    }
}
