using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Screen-space dialogue box with character portrait. Shows Nema's face alongside
/// her comments in a VN-style panel at the bottom of the screen.
/// Portrait sits inside the text frame border, left-aligned.
/// Auto-spawns as a DDoL singleton.
/// </summary>
public class DialoguePortraitBox : MonoBehaviour
{
    public static DialoguePortraitBox Instance { get; private set; }

    [Header("Portrait")]
    [Tooltip("Default portrait texture. Loaded from ArtAssets if not assigned.")]
    [SerializeField] private Texture2D _defaultPortrait;

    [Header("Timing")]
    [Tooltip("Slide-in duration (seconds).")]
    [SerializeField] private float _slideInDuration = 0.25f;

    [Tooltip("Slide-out duration (seconds).")]
    [SerializeField] private float _slideOutDuration = 0.2f;

    [Header("Layout")]
    [Tooltip("Panel height in reference pixels.")]
    [SerializeField] private float _panelHeight = 100f;

    [Tooltip("Portrait size inside the frame.")]
    [SerializeField] private float _portraitSize = 80f;

    // UI references
    private Canvas _canvas;
    private CanvasGroup _group;
    private RectTransform _panelRT;
    private RawImage _portraitImage;
    private TMP_Text _dialogueText;
    private TMP_Text _nameText;
    private Coroutine _activeRoutine;
    private float _panelOffscreenY;

    // Disabled — VN talking head box removed for now.
    // [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    // private static void AutoSpawn()
    // {
    //     if (Instance != null) return;
    //     var go = new GameObject("[DialoguePortraitBox]");
    //     go.AddComponent<DialoguePortraitBox>();
    // }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildUI();
        LoadDefaultPortrait();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Show dialogue with the default portrait (Nema).</summary>
    public void Say(string text, float duration = 3f)
    {
        Say(null, "Nema", text, duration);
    }

    /// <summary>Show dialogue with a specific portrait and name.</summary>
    public void Say(Texture2D portrait, string characterName, string text, float duration = 3f)
    {
        if (_activeRoutine != null)
            StopCoroutine(_activeRoutine);
        _activeRoutine = StartCoroutine(DialogueSequence(portrait, characterName, text, duration));
    }

    /// <summary>Dismiss immediately if showing.</summary>
    public void Dismiss()
    {
        if (_activeRoutine != null)
        {
            StopCoroutine(_activeRoutine);
            _activeRoutine = null;
        }
        _panelRT.anchoredPosition = new Vector2(0f, _panelOffscreenY);
        _group.alpha = 0f;
    }

    private IEnumerator DialogueSequence(Texture2D portrait, string characterName, string text, float duration)
    {
        // Set content
        _portraitImage.texture = portrait != null ? portrait : _defaultPortrait;
        _nameText.text = characterName ?? "Nema";
        _dialogueText.text = text;

        // Slide in from bottom
        float startY = _panelOffscreenY;
        float endY = 0f;
        float elapsed = 0f;

        _group.alpha = 1f;
        while (elapsed < _slideInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / _slideInDuration);
            _panelRT.anchoredPosition = new Vector2(0f, Mathf.Lerp(startY, endY, t));
            yield return null;
        }
        _panelRT.anchoredPosition = new Vector2(0f, endY);

        // Hold
        yield return new WaitForSecondsRealtime(duration);

        // Slide out
        elapsed = 0f;
        while (elapsed < _slideOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / _slideOutDuration);
            _panelRT.anchoredPosition = new Vector2(0f, Mathf.Lerp(endY, startY, t));
            _group.alpha = 1f - t;
            yield return null;
        }

        _panelRT.anchoredPosition = new Vector2(0f, startY);
        _group.alpha = 0f;
        _activeRoutine = null;
    }

    private void BuildUI()
    {
        // Overlay canvas — below ScreenFade (100) but above gameplay UI
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 85;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;

        // Panel — anchored to bottom, full width
        var panelGO = new GameObject("Panel");
        panelGO.transform.SetParent(transform, false);
        _panelRT = panelGO.AddComponent<RectTransform>();
        _panelRT.anchorMin = new Vector2(0f, 0f);
        _panelRT.anchorMax = new Vector2(1f, 0f);
        _panelRT.pivot = new Vector2(0.5f, 0f);
        _panelRT.sizeDelta = new Vector2(0f, _panelHeight);
        _panelRT.anchoredPosition = Vector2.zero;

        _panelOffscreenY = -_panelHeight - 20f;
        _panelRT.anchoredPosition = new Vector2(0f, _panelOffscreenY);

        // Background
        var bgImage = panelGO.AddComponent<Image>();
        bgImage.color = new Color(0.08f, 0.08f, 0.10f, 0.88f);
        bgImage.raycastTarget = false;

        // Top border line
        var borderGO = new GameObject("Border");
        borderGO.transform.SetParent(panelGO.transform, false);
        var borderRT = borderGO.AddComponent<RectTransform>();
        borderRT.anchorMin = new Vector2(0f, 1f);
        borderRT.anchorMax = new Vector2(1f, 1f);
        borderRT.pivot = new Vector2(0.5f, 1f);
        borderRT.sizeDelta = new Vector2(0f, 2f);
        var borderImg = borderGO.AddComponent<Image>();
        borderImg.color = new Color(0.95f, 0.92f, 0.85f, 0.6f);
        borderImg.raycastTarget = false;

        // Portrait — inside the frame, left side
        var portraitGO = new GameObject("Portrait");
        portraitGO.transform.SetParent(panelGO.transform, false);
        var portraitRT = portraitGO.AddComponent<RectTransform>();
        portraitRT.anchorMin = new Vector2(0f, 0.5f);
        portraitRT.anchorMax = new Vector2(0f, 0.5f);
        portraitRT.pivot = new Vector2(0f, 0.5f);
        portraitRT.anchoredPosition = new Vector2(12f, 0f);
        portraitRT.sizeDelta = new Vector2(_portraitSize, _portraitSize);

        _portraitImage = portraitGO.AddComponent<RawImage>();
        _portraitImage.raycastTarget = false;

        // Portrait border
        var pBorderGO = new GameObject("PortraitBorder");
        pBorderGO.transform.SetParent(portraitGO.transform, false);
        var pBorderRT = pBorderGO.AddComponent<RectTransform>();
        pBorderRT.anchorMin = Vector2.zero;
        pBorderRT.anchorMax = Vector2.one;
        pBorderRT.sizeDelta = new Vector2(4f, 4f);
        pBorderRT.anchoredPosition = Vector2.zero;
        var pBorderImg = pBorderGO.AddComponent<Image>();
        pBorderImg.color = new Color(0.95f, 0.92f, 0.85f, 0.5f);
        pBorderImg.raycastTarget = false;
        // Make it a frame (hollow) by putting it behind — the RawImage renders on top
        pBorderGO.transform.SetAsFirstSibling();

        // Name label — above dialogue text, right of portrait
        var nameGO = new GameObject("Name");
        nameGO.transform.SetParent(panelGO.transform, false);
        var nameRT = nameGO.AddComponent<RectTransform>();
        nameRT.anchorMin = new Vector2(0f, 1f);
        nameRT.anchorMax = new Vector2(1f, 1f);
        nameRT.pivot = new Vector2(0f, 1f);
        float textLeft = 12f + _portraitSize + 16f;
        nameRT.offsetMin = new Vector2(textLeft, -30f);
        nameRT.offsetMax = new Vector2(-20f, -6f);

        _nameText = nameGO.AddComponent<TextMeshProUGUI>();
        _nameText.fontSize = 18f;
        _nameText.fontStyle = FontStyles.Bold;
        _nameText.color = new Color(0.95f, 0.82f, 0.65f);
        _nameText.alignment = TextAlignmentOptions.TopLeft;
        _nameText.raycastTarget = false;

        // Dialogue text — right of portrait, fills remaining space
        var textGO = new GameObject("Text");
        textGO.transform.SetParent(panelGO.transform, false);
        var textRT = textGO.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(textLeft, 10f);
        textRT.offsetMax = new Vector2(-20f, -30f);

        _dialogueText = textGO.AddComponent<TextMeshProUGUI>();
        _dialogueText.fontSize = 20f;
        _dialogueText.color = new Color(0.92f, 0.90f, 0.85f);
        _dialogueText.alignment = TextAlignmentOptions.TopLeft;
        _dialogueText.textWrappingMode = TextWrappingModes.Normal;
        _dialogueText.raycastTarget = false;
    }

    private void LoadDefaultPortrait()
    {
        if (_defaultPortrait != null) return;

        // Try loading from known path
        _defaultPortrait = Resources.Load<Texture2D>("Portraits/nema_portrait");
        if (_defaultPortrait != null) return;

        // Fallback: generate a simple placeholder
        const int S = 64;
        _defaultPortrait = new Texture2D(S, S, TextureFormat.RGBA32, false);
        _defaultPortrait.filterMode = FilterMode.Point;
        var px = new Color32[S * S];
        var skin = new Color32(220, 195, 170, 255);
        var hair = new Color32(50, 40, 35, 255);
        var eye = new Color32(90, 130, 90, 255);

        // Simple face
        for (int y = 16; y < 52; y++)
            for (int x = 18; x < 46; x++)
                px[y * S + x] = skin;
        // Hair
        for (int y = 40; y < 56; y++)
            for (int x = 14; x < 50; x++)
                px[y * S + x] = hair;
        // Eyes
        for (int y = 30; y < 34; y++)
        {
            px[y * S + 26] = eye; px[y * S + 27] = eye;
            px[y * S + 36] = eye; px[y * S + 37] = eye;
        }

        _defaultPortrait.SetPixels32(px);
        _defaultPortrait.Apply();
    }
}
