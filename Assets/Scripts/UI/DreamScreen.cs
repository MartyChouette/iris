using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Psychedelic dream interstitial shown between days. Semi-transparent (50%)
/// over the apartment so the room is visible beneath swirling colors.
/// 1960s Disney Fantasia / Spectroscope Cinecolor Panavision aesthetic:
/// slowly rotating color bands, hue-shifting gradients, soft radial patterns.
///
/// Procedurally generated — no external textures required.
/// </summary>
public class DreamScreen : MonoBehaviour
{
    public static DreamScreen Instance { get; private set; }

    [Header("Timing")]
    [SerializeField] private float _fadeInDuration = 1.5f;
    [SerializeField] private float _holdDuration = 4f;
    [SerializeField] private float _fadeOutDuration = 1.5f;

    [Header("Visuals")]
    [Tooltip("Overlay opacity at full visibility (1.0 = fully opaque).")]
    [SerializeField, Range(0f, 1f)] private float _overlayAlpha = 1f;

    [Tooltip("How fast the color bands rotate (degrees/sec).")]
    [SerializeField] private float _rotateSpeed = 15f;

    [Tooltip("How fast the hue shifts across the spectrum.")]
    [SerializeField] private float _hueShiftSpeed = 0.08f;

    [Header("Text")]
    [SerializeField] private string[] _dreamPhrases = new[]
    {
        "Nema drifts to sleep...",
        "The apartment hums quietly...",
        "Tomorrow is another day...",
        "Dreams swirl in the dark...",
    };

    private GameObject _canvasRoot;
    private CanvasGroup _canvasGroup;
    private RawImage _psychedelicImage;
    private RectTransform _psychedelicRT;
    private TMP_Text _dreamText;
    private Texture2D _gradientTex;
    private bool _isShowing;
    private float _time;

    private const int TexW = 256;
    private const int TexH = 256;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoSpawn()
    {
        if (Instance != null) return;
        var go = new GameObject("DreamScreen");
        go.AddComponent<DreamScreen>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildUI();
        _canvasRoot.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (_gradientTex != null) Destroy(_gradientTex);
        if (Instance == this) Instance = null;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    {
        StopAllCoroutines();
        if (_canvasRoot != null) _canvasRoot.SetActive(false);
        if (_canvasGroup != null) _canvasGroup.alpha = 0f;
    }

    private int _updateFrame;

    private void Update()
    {
        if (!_isShowing) return;

        _time += Time.unscaledDeltaTime;

        // Psychedelic visuals disabled — just show text over dark background
    }

    // ── Public API ──────────────────────────────────────────────────

    /// <summary>Show the dream screen, hold, then hide. Returns when fully hidden.</summary>
    public Coroutine Play(string overrideText = null)
    {
        return StartCoroutine(DreamSequence(overrideText));
    }

    /// <summary>Show the dream screen and leave it up until HideAndFadeOut is called.</summary>
    public void ShowAndHold(string overrideText = null)
    {
        StartCoroutine(FadeIn(overrideText));
    }

    public Coroutine HideAndFadeOut()
    {
        return StartCoroutine(FadeOut());
    }

    // ── Sequences ───────────────────────────────────────────────────

    private IEnumerator DreamSequence(string overrideText)
    {
        yield return FadeIn(overrideText);
        yield return new WaitForSecondsRealtime(_holdDuration);
        yield return FadeOut();
    }

    private IEnumerator FadeIn(string overrideText)
    {
        _isShowing = true;
        _time = 0f;
        _canvasRoot.SetActive(true);

        // Pick dream text
        string text = overrideText;
        if (string.IsNullOrEmpty(text) && _dreamPhrases != null && _dreamPhrases.Length > 0)
            text = _dreamPhrases[Random.Range(0, _dreamPhrases.Length)];
        if (_dreamText != null)
            _dreamText.text = text ?? "";

        // Fade in
        float elapsed = 0f;
        while (elapsed < _fadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _fadeInDuration);
            t = t * t * (3f - 2f * t); // smoothstep
            _canvasGroup.alpha = t;
            yield return null;
        }
        _canvasGroup.alpha = 1f;
    }

    private IEnumerator FadeOut()
    {
        float elapsed = 0f;
        while (elapsed < _fadeOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = 1f - Mathf.Clamp01(elapsed / _fadeOutDuration);
            t = t * t * (3f - 2f * t);
            _canvasGroup.alpha = t;
            yield return null;
        }
        _canvasGroup.alpha = 0f;
        _canvasRoot.SetActive(false);
        _isShowing = false;
    }

    // ── Procedural gradient ─────────────────────────────────────────

    // Reusable pixel buffer — allocated once, reused every update
    private Color32[] _pixels;

    private void UpdateGradientTexture(float hueOffset)
    {
        if (_gradientTex == null) return;

        if (_pixels == null || _pixels.Length != TexW * TexH)
            _pixels = new Color32[TexW * TexH];
        var pixels = _pixels;
        byte alphaB = (byte)(255 * _overlayAlpha);

        for (int y = 0; y < TexH; y++)
        {
            float ny = (float)y / TexH;
            for (int x = 0; x < TexW; x++)
            {
                float nx = (float)x / TexW;

                // Radial distance from center
                float dx = nx - 0.5f;
                float dy = ny - 0.5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                float angle = Mathf.Atan2(dy, dx) / (Mathf.PI * 2f) + 0.5f;

                // 1960s color bands: concentric rings + angular sectors
                float band1 = Mathf.Sin(r * 8f + hueOffset * 3f) * 0.5f + 0.5f;
                float band2 = Mathf.Sin(angle * 6f + hueOffset * 2f) * 0.5f + 0.5f;
                float band3 = Mathf.Sin((r + angle) * 4f - hueOffset) * 0.5f + 0.5f;

                // Combine into psychedelic hue/saturation
                float hue = Mathf.Repeat(band1 * 0.4f + band2 * 0.3f + hueOffset, 1f);
                float sat = 0.6f + band3 * 0.4f;
                float val = 0.5f + band1 * 0.3f + band2 * 0.2f;

                // Soft vignette — darker at edges
                float vignette = 1f - Mathf.Clamp01((r - 0.3f) / 0.7f) * 0.4f;
                val *= vignette;

                Color c = Color.HSVToRGB(hue, sat, val);
                pixels[y * TexW + x] = new Color32(
                    (byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), alphaB);
            }
        }

        _gradientTex.SetPixels32(pixels);
        _gradientTex.Apply();
    }

    // ── Build UI ────────────────────────────────────────────────────

    private void BuildUI()
    {
        _canvasRoot = new GameObject("DreamCanvas");
        _canvasRoot.transform.SetParent(transform, false);
        var canvas = _canvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 105; // above ScreenFade (100) so dream shows over black

        var scaler = _canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        _canvasGroup = _canvasRoot.AddComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;
        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.interactable = false;

        // Solid dark background instead of psychedelic gradient
        var bgGO = new GameObject("DarkBG");
        bgGO.transform.SetParent(_canvasRoot.transform, false);
        var bgRT = bgGO.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0.05f, 0.05f, 0.08f, 1f);
        bgImg.raycastTarget = false;

        // Floating dream text
        var textGO = new GameObject("DreamText");
        textGO.transform.SetParent(_canvasRoot.transform, false);
        var textRT = textGO.AddComponent<RectTransform>();
        textRT.anchorMin = new Vector2(0.5f, 0.5f);
        textRT.anchorMax = new Vector2(0.5f, 0.5f);
        textRT.pivot = new Vector2(0.5f, 0.5f);
        textRT.sizeDelta = new Vector2(800f, 100f);
        textRT.anchoredPosition = Vector2.zero;

        _dreamText = textGO.AddComponent<TextMeshProUGUI>();
        _dreamText.text = "";
        _dreamText.fontSize = 36f;
        _dreamText.fontStyle = FontStyles.Italic;
        _dreamText.alignment = TextAlignmentOptions.Center;
        _dreamText.color = new Color(1f, 1f, 1f, 0.9f);
        _dreamText.enableWordWrapping = true;
    }
}
