using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Scene-scoped singleton that fades the screen to/from black via a full-screen CanvasGroup overlay.
/// Supports easing curves and configurable durations (0 = hard cut).
/// Built by ApartmentSceneBuilder.BuildScreenFade().
/// </summary>
public class ScreenFade : MonoBehaviour
{
    public static ScreenFade Instance { get; private set; }

    [SerializeField] private CanvasGroup _canvasGroup;

    [Header("Phase Title")]
    [Tooltip("Text shown during phase transitions (separate from DreamText used by GameClock).")]
    [SerializeField] private TMP_Text _phaseText;

    [Header("Default Durations")]
    [Tooltip("Default fade-out duration in seconds. Set to 0 for a hard cut.")]
    public float defaultFadeOutDuration = 0.5f;

    [Tooltip("Default fade-in duration in seconds. Set to 0 for a hard cut.")]
    public float defaultFadeInDuration = 0.5f;

    [Header("Easing Curves")]
    [Tooltip("Easing curve for fade-out (0→1 maps time-normalized to alpha). Defaults to ease-in.")]
    public AnimationCurve fadeOutCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Easing curve for fade-in (0→1 maps time-normalized to alpha). Defaults to ease-out.")]
    public AnimationCurve fadeInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    /// <summary>True while a fade coroutine is running.</summary>
    public bool IsFading { get; private set; }

    // Track the active fade coroutine so we can stop it before starting a new one
    private Coroutine _activeFadeCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[ScreenFade] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // NOTE: Do NOT override canvas sortingOrder here. The scene builder sets
        // ScreenFade to sortingOrder 100 and NameEntryScreen to 110 (above it).
        // Forcing a higher order blocks NameEntryScreen clicks and deadlocks startup.
    }

    private void Start()
    {
        // Editor direct play (no menu) — clear fade immediately so screen isn't white
        if (_canvasGroup != null && MainMenuManager.ActiveConfig == null)
        {
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Fade to black (alpha 0 → 1) using the default duration and fadeOutCurve.
    /// </summary>
    public Coroutine FadeOut() => FadeOut(defaultFadeOutDuration);

    /// <summary>
    /// Fade to black with explicit duration. Uses fadeOutCurve for easing.
    /// Duration of 0 = instant hard cut.
    /// </summary>
    public Coroutine FadeOut(float duration)
    {
        if (_activeFadeCoroutine != null) StopCoroutine(_activeFadeCoroutine);
        _activeFadeCoroutine = StartCoroutine(FadeCoroutine(0f, 1f, duration, true, fadeOutCurve));
        return _activeFadeCoroutine;
    }

    /// <summary>
    /// Fade in from black (alpha 1 → 0) using the default duration and fadeInCurve.
    /// </summary>
    public Coroutine FadeIn() => FadeIn(defaultFadeInDuration);

    /// <summary>
    /// Fade in from black with explicit duration. Uses fadeInCurve for easing.
    /// Duration of 0 = instant hard cut.
    /// </summary>
    public Coroutine FadeIn(float duration)
    {
        if (_activeFadeCoroutine != null) StopCoroutine(_activeFadeCoroutine);
        _activeFadeCoroutine = StartCoroutine(FadeCoroutine(1f, 0f, duration, false, fadeInCurve));
        return _activeFadeCoroutine;
    }

    /// <summary>Show phase title text on the black screen.</summary>
    public void ShowPhaseTitle(string text)
    {
        EnsurePhaseText();
        if (_phaseText != null)
        {
            _phaseText.text = text;
            _phaseText.gameObject.SetActive(true);
        }
    }

    /// <summary>Hide phase title text.</summary>
    public void HidePhaseTitle()
    {
        if (_phaseText != null)
        {
            _phaseText.text = "";
            _phaseText.gameObject.SetActive(false);
        }
    }

    /// <summary>Create _phaseText at runtime if not wired in Inspector.</summary>
    private void EnsurePhaseText()
    {
        if (_phaseText != null) return;

        // Create on a separate canvas ABOVE the fade overlay so text is always visible
        var canvasGO = new GameObject("PhaseTitleCanvas");
        canvasGO.transform.SetParent(transform, false);

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 105; // above ScreenFade (100)

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var textGO = new GameObject("PhaseTitle");
        textGO.transform.SetParent(canvasGO.transform, false);

        var rt = textGO.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        _phaseText = textGO.AddComponent<TMPro.TextMeshProUGUI>();
        _phaseText.fontSize = 48f;
        _phaseText.alignment = TMPro.TextAlignmentOptions.Center;
        _phaseText.color = new Color(0.95f, 0.92f, 0.85f);
        _phaseText.textWrappingMode = TextWrappingModes.Normal;
        textGO.SetActive(false);

        Debug.Log("[ScreenFade] Created phase title on overlay canvas (sortingOrder 105).");
    }

    /// <summary>
    /// Fade to black, invoke an action, then fade back in.
    /// Useful for scene-to-scene or phase-to-phase transitions.
    /// </summary>
    public Coroutine FadeOutIn(float outDuration, float inDuration, System.Action onBlack = null)
    {
        if (_activeFadeCoroutine != null) StopCoroutine(_activeFadeCoroutine);
        _activeFadeCoroutine = StartCoroutine(FadeOutInCoroutine(outDuration, inDuration, onBlack));
        return _activeFadeCoroutine;
    }

    /// <summary>
    /// FadeOutIn using default durations.
    /// </summary>
    public Coroutine FadeOutIn(System.Action onBlack = null)
    {
        return FadeOutIn(defaultFadeOutDuration, defaultFadeInDuration, onBlack);
    }

    private IEnumerator FadeOutInCoroutine(float outDuration, float inDuration, System.Action onBlack)
    {
        yield return FadeCoroutine(0f, 1f, outDuration, true, fadeOutCurve);
        onBlack?.Invoke();
        yield return FadeCoroutine(1f, 0f, inDuration, false, fadeInCurve);
    }

    private IEnumerator FadeCoroutine(float from, float to, float duration, bool blockWhenDone,
        AnimationCurve curve)
    {
        if (_canvasGroup == null) yield break;

        IsFading = true;
        // Only block raycasts when fading OUT (going dark). Fade-ins should
        // let clicks through so buttons are responsive immediately.
        _canvasGroup.blocksRaycasts = blockWhenDone;

        // Hard cut — instant transition
        if (duration <= 0f)
        {
            _canvasGroup.alpha = to;
            _canvasGroup.blocksRaycasts = blockWhenDone;
            IsFading = false;
            yield break;
        }

        float elapsed = 0f;
        float maxDuration = duration + 5f; // safety cap — never hang longer than 5s past expected
        while (elapsed < duration && elapsed < maxDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float curved = curve != null ? curve.Evaluate(t) : t;
            _canvasGroup.alpha = Mathf.Lerp(from, to, curved);
            yield return null;
        }

        _canvasGroup.alpha = to;
        _canvasGroup.blocksRaycasts = blockWhenDone;
        IsFading = false;
    }
}
