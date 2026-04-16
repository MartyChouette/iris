using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// DontDestroyOnLoad loading screen with async scene loading,
/// falling pixel petals, progress bar, and animated text.
/// Petals fall continuously while loading is in progress.
/// Self-destructs after the new scene finishes loading.
/// </summary>
public class LoadingScreen : MonoBehaviour
{
    private static LoadingScreen _instance;

    private CanvasGroup _canvasGroup;
    private Image _barFill;
    private TMP_Text _loadingText;
    private RectTransform _petalContainer;

    private float _ellipsisTimer;
    private int _ellipsisCount;

    // Petal system
    private RectTransform[] _petals;
    private float[] _petalX;
    private float[] _petalY;
    private float[] _petalSpeed;
    private float[] _petalSway;
    private float[] _petalSwayOffset;
    private float[] _petalRot;
    private float[] _petalRotSpeed;
    private const int PetalCount = 40;
    private const float SwayAmount = 30f;
    private const float SwaySpeed = 1.5f;

    private static readonly Color[] PetalColors =
    {
        new Color(1f, 0.75f, 0.80f),    // soft pink
        new Color(1f, 0.85f, 0.88f),    // light pink
        new Color(0.95f, 0.65f, 0.72f), // dusty rose
        new Color(1f, 0.92f, 0.85f),    // cream
        new Color(0.98f, 0.80f, 0.75f), // peach
        new Color(0.85f, 0.60f, 0.70f), // mauve
    };

    // ═══════════════════════════════════════════════════════════════
    // Static entry point
    // ═══════════════════════════════════════════════════════════════

    public static void LoadScene(int buildIndex)
    {
        if (_instance != null) return;
        var go = new GameObject("LoadingScreen");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<LoadingScreen>();
        _instance.Build();
        _instance.StartCoroutine(_instance.LoadCoroutine(buildIndex));
    }

    public static void LoadPreloaded(AsyncOperation preloadedOp)
    {
        if (_instance != null) return;
        var go = new GameObject("LoadingScreen");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<LoadingScreen>();
        _instance.Build();
        _instance.StartCoroutine(_instance.ActivatePreloadedCoroutine(preloadedOp));
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    // ═══════════════════════════════════════════════════════════════
    // Build UI procedurally
    // ═══════════════════════════════════════════════════════════════

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        gameObject.AddComponent<GraphicRaycaster>();

        _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        _canvasGroup.alpha = 1f;
        _canvasGroup.blocksRaycasts = true;

        // Black background
        var bg = CreateChild<Image>("Background");
        Stretch(bg.rectTransform);
        bg.color = Color.black;

        // Petal container (fullscreen)
        var containerGO = new GameObject("PetalContainer");
        containerGO.transform.SetParent(transform, false);
        _petalContainer = containerGO.AddComponent<RectTransform>();
        Stretch(_petalContainer);

        BuildPetals();

        // Progress bar background
        var barBgGO = new GameObject("BarBG");
        barBgGO.transform.SetParent(transform, false);
        var barBgRT = barBgGO.AddComponent<RectTransform>();
        barBgRT.anchorMin = new Vector2(0.5f, 0f);
        barBgRT.anchorMax = new Vector2(0.5f, 0f);
        barBgRT.anchoredPosition = new Vector2(0f, 60f);
        barBgRT.sizeDelta = new Vector2(300f, 3f);
        var barBgImg = barBgGO.AddComponent<Image>();
        barBgImg.color = new Color(0.2f, 0.2f, 0.2f);

        // Progress bar fill
        var fillGO = new GameObject("BarFill");
        fillGO.transform.SetParent(barBgGO.transform, false);
        var fillRT = fillGO.AddComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = new Vector2(0f, 1f);
        fillRT.sizeDelta = Vector2.zero;
        fillRT.anchoredPosition = Vector2.zero;
        _barFill = fillGO.AddComponent<Image>();
        _barFill.color = new Color(0.95f, 0.9f, 0.8f);

        // Loading text
        var textGO = new GameObject("LoadingText");
        textGO.transform.SetParent(transform, false);
        var textRT = textGO.AddComponent<RectTransform>();
        textRT.anchorMin = new Vector2(0.5f, 0f);
        textRT.anchorMax = new Vector2(0.5f, 0f);
        textRT.anchoredPosition = new Vector2(0f, 30f);
        textRT.sizeDelta = new Vector2(300f, 40f);
        _loadingText = textGO.AddComponent<TextMeshProUGUI>();
        _loadingText.text = "Loading.";
        _loadingText.fontSize = 20f;
        _loadingText.alignment = TextAlignmentOptions.Center;
        _loadingText.color = new Color(0.95f, 0.9f, 0.8f);
    }

    // ═══════════════════════════════════════════════════════════════
    // Falling pixel petals
    // ═══════════════════════════════════════════════════════════════

    private void BuildPetals()
    {
        _petals = new RectTransform[PetalCount];
        _petalX = new float[PetalCount];
        _petalY = new float[PetalCount];
        _petalSpeed = new float[PetalCount];
        _petalSway = new float[PetalCount];
        _petalSwayOffset = new float[PetalCount];
        _petalRot = new float[PetalCount];
        _petalRotSpeed = new float[PetalCount];

        for (int i = 0; i < PetalCount; i++)
        {
            var go = new GameObject($"Petal{i}");
            go.transform.SetParent(_petalContainer, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);

            // Pixel-art size: small rectangles (5-12px)
            float w = Random.Range(4f, 10f);
            float h = Random.Range(6f, 14f);
            rt.sizeDelta = new Vector2(w, h);

            var img = go.AddComponent<Image>();
            img.color = PetalColors[Random.Range(0, PetalColors.Length)];
            img.raycastTarget = false;

            _petals[i] = rt;

            // Stagger initial positions across the full screen height
            _petalX[i] = Random.Range(0f, 1920f);
            _petalY[i] = Random.Range(0f, 1080f + 200f);
            _petalSpeed[i] = Random.Range(60f, 180f);
            _petalSway[i] = Random.Range(0.8f, 1.5f);
            _petalSwayOffset[i] = Random.Range(0f, Mathf.PI * 2f);
            _petalRot[i] = Random.Range(0f, 360f);
            _petalRotSpeed[i] = Random.Range(-90f, 90f);
        }
    }

    private void UpdatePetals()
    {
        float dt = Time.unscaledDeltaTime;

        for (int i = 0; i < PetalCount; i++)
        {
            _petalY[i] += _petalSpeed[i] * dt;
            _petalRot[i] += _petalRotSpeed[i] * dt;

            // Gentle horizontal sway
            float sway = Mathf.Sin(Time.unscaledTime * SwaySpeed * _petalSway[i] + _petalSwayOffset[i]) * SwayAmount;

            // Respawn at top when off bottom
            if (_petalY[i] > 1080f + 50f)
            {
                _petalY[i] = -Random.Range(20f, 100f);
                _petalX[i] = Random.Range(0f, 1920f);
                _petalSpeed[i] = Random.Range(60f, 180f);
            }

            if (_petals[i] != null)
            {
                _petals[i].anchoredPosition = new Vector2(_petalX[i] + sway, -_petalY[i]);
                _petals[i].localRotation = Quaternion.Euler(0f, 0f, _petalRot[i]);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Async load coroutines
    // ═══════════════════════════════════════════════════════════════

    private IEnumerator LoadCoroutine(int buildIndex)
    {
        float holdTimer = 0.3f;
        while (holdTimer > 0f)
        {
            holdTimer -= Time.unscaledDeltaTime;
            UpdatePetals();
            yield return null;
        }

        var op = SceneManager.LoadSceneAsync(buildIndex);
        if (op == null)
        {
            Debug.LogError("[LoadingScreen] LoadSceneAsync returned null. Attempting to fade back to visibility.");
            // Fade the ScreenFade back to clear so the player isn't soft-locked on black
            ScreenFade.Instance?.FadeIn(0.5f);
            Destroy(gameObject);
            yield break;
        }

        while (!op.isDone)
        {
            float t = Mathf.Clamp01(op.progress / 0.9f);
            SetProgress(t);
            UpdatePetals();
            UpdateEllipsis();
            yield return null;
        }

        SetProgress(1f);
        yield return null;

        // Warm up shaders to avoid stutter on first render
        Shader.WarmupAllShaders();
        yield return null;

        // Fade out
        float fadeTime = 0.5f;
        float elapsed = 0f;
        while (elapsed < fadeTime)
        {
            elapsed += Time.unscaledDeltaTime;
            _canvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / fadeTime);
            UpdatePetals();
            yield return null;
        }

        Destroy(gameObject);
    }

    private IEnumerator ActivatePreloadedCoroutine(AsyncOperation op)
    {
        float holdTimer = 0.3f;
        while (holdTimer > 0f)
        {
            holdTimer -= Time.unscaledDeltaTime;
            UpdatePetals();
            yield return null;
        }

        if (op == null)
        {
            Debug.LogError("[LoadingScreen] Preloaded AsyncOperation is null.");
            Destroy(gameObject);
            yield break;
        }

        float preProgress = Mathf.Clamp01(op.progress / 0.9f);
        SetProgress(preProgress);

        op.allowSceneActivation = true;

        while (!op.isDone)
        {
            float t = Mathf.Clamp01(op.progress / 0.9f);
            SetProgress(t);
            UpdatePetals();
            UpdateEllipsis();
            yield return null;
        }

        SetProgress(1f);
        yield return null;

        Shader.WarmupAllShaders();
        yield return null;

        float fadeTime = 0.5f;
        float elapsed = 0f;
        while (elapsed < fadeTime)
        {
            elapsed += Time.unscaledDeltaTime;
            _canvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / fadeTime);
            UpdatePetals();
            yield return null;
        }

        Destroy(gameObject);
    }

    // ═══════════════════════════════════════════════════════════════
    // Visual updates
    // ═══════════════════════════════════════════════════════════════

    private void SetProgress(float t)
    {
        if (_barFill != null)
            _barFill.rectTransform.anchorMax = new Vector2(t, 1f);
    }

    private void UpdateEllipsis()
    {
        _ellipsisTimer += Time.unscaledDeltaTime;
        if (_ellipsisTimer >= 0.5f)
        {
            _ellipsisTimer = 0f;
            _ellipsisCount = (_ellipsisCount + 1) % 3;
        }

        if (_loadingText != null)
        {
            _loadingText.text = _ellipsisCount switch
            {
                0 => "Loading.",
                1 => "Loading..",
                _ => "Loading..."
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════

    private T CreateChild<T>(string name, RectTransform parent = null) where T : Component
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent != null ? parent : transform, false);
        go.AddComponent<RectTransform>();
        return go.AddComponent<T>();
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
    }
}
