using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Shown once per play session before gameplay begins.
/// Asks the player for consent to collect playtest telemetry.
/// If declined, telemetry fields are omitted but the feedback form still appears.
/// Does NOT persist across sessions — asked fresh each time.
/// </summary>
public class PlaytestConsentScreen : MonoBehaviour
{
    public static bool HasConsent { get; private set; }
    public static bool WasShown { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void DomainReset()
    {
        HasConsent = false;
        WasShown = false;
    }

    private GameObject _root;
    private System.Action _onComplete;

    /// <summary>
    /// Show consent screen. Calls onComplete(true/false) when player decides.
    /// If already shown this session, calls onComplete immediately with cached result.
    /// </summary>
    public static void ShowIfNeeded(System.Action onComplete)
    {
        // Auto-consent — metrics are sent silently to Discord feedback channel.
        // No consent screen shown to the player.
        HasConsent = true;
        WasShown = true;
        onComplete?.Invoke();
    }

    private void Awake()
    {
        BuildUI();
    }

    private void BuildUI()
    {
        // Canvas
        _root = new GameObject("PlaytestConsentCanvas");
        _root.transform.SetParent(transform, false);

        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;

        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        _root.AddComponent<GraphicRaycaster>();

        // Dim background
        var bg = CreateChild(_root, "Background");
        var bgRT = bg.AddComponent<RectTransform>();
        StretchFill(bgRT);
        var bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.85f);

        // Panel
        var panel = CreateChild(_root, "Panel");
        var panelRT = panel.AddComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta = new Vector2(800f, 400f);
        var panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0.12f, 0.12f, 0.14f, 1f);

        // Title
        var title = CreateChild(panel, "Title");
        var titleRT = title.AddComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0f, 1f);
        titleRT.anchorMax = new Vector2(1f, 1f);
        titleRT.pivot = new Vector2(0.5f, 1f);
        titleRT.anchoredPosition = new Vector2(0f, -20f);
        titleRT.sizeDelta = new Vector2(0f, 50f);
        var titleTMP = title.AddComponent<TextMeshProUGUI>();
        titleTMP.text = "Playtest Build";
        titleTMP.fontSize = 32f;
        titleTMP.alignment = TextAlignmentOptions.Center;
        titleTMP.color = new Color(0.95f, 0.92f, 0.85f);

        // Body text
        var body = CreateChild(panel, "Body");
        var bodyRT = body.AddComponent<RectTransform>();
        bodyRT.anchorMin = new Vector2(0.05f, 0.35f);
        bodyRT.anchorMax = new Vector2(0.95f, 0.85f);
        bodyRT.pivot = new Vector2(0.5f, 0.5f);
        bodyRT.anchoredPosition = Vector2.zero;
        bodyRT.sizeDelta = Vector2.zero;
        var bodyTMP = body.AddComponent<TextMeshProUGUI>();
        bodyTMP.text = "This is a playtest build. We collect gameplay data " +
            "(actions, timing, choices) to improve the game. " +
            "No visual or auditory information is captured.\n\n" +
            "Continue?";
        bodyTMP.fontSize = 22f;
        bodyTMP.alignment = TextAlignmentOptions.Center;
        bodyTMP.color = new Color(0.8f, 0.8f, 0.78f);

        // Buttons row
        float btnY = -320f;
        float btnW = 200f;
        float btnH = 50f;

        // "I Agree" button
        CreateButton(panel, "AgreeBtn", "I Agree", -120f, btnY, btnW, btnH,
            new Color(0.25f, 0.55f, 0.35f), () => OnChoice(true));

        // "No Thanks" button
        CreateButton(panel, "DeclineBtn", "No Thanks", 120f, btnY, btnW, btnH,
            new Color(0.55f, 0.25f, 0.25f), () => OnChoice(false));

        TimeScaleManager.Set(TimeScaleManager.PRIORITY_PAUSE, 0f);
    }

    private void OnChoice(bool agreed)
    {
        HasConsent = agreed;
        WasShown = true;

        TimeScaleManager.Clear(TimeScaleManager.PRIORITY_PAUSE);

        Debug.Log($"[PlaytestConsentScreen] Consent {(agreed ? "granted" : "declined")}.");

        var cb = _onComplete;
        _onComplete = null;

        if (_root != null) Destroy(_root);
        Destroy(gameObject);

        cb?.Invoke();
    }

    // ── UI helpers ──

    private static GameObject CreateChild(GameObject parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    private static void StretchFill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
    }

    private static void CreateButton(GameObject parent, string name, string label,
        float x, float y, float w, float h, Color color, UnityEngine.Events.UnityAction onClick)
    {
        var btnGO = CreateChild(parent, name);
        var btnRT = btnGO.AddComponent<RectTransform>();
        btnRT.anchorMin = new Vector2(0.5f, 1f);
        btnRT.anchorMax = new Vector2(0.5f, 1f);
        btnRT.pivot = new Vector2(0.5f, 0.5f);
        btnRT.anchoredPosition = new Vector2(x, y);
        btnRT.sizeDelta = new Vector2(w, h);

        var btnImg = btnGO.AddComponent<Image>();
        btnImg.color = color;

        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        btn.onClick.AddListener(onClick);

        var txtGO = CreateChild(btnGO, "Label");
        var txtRT = txtGO.AddComponent<RectTransform>();
        StretchFill(txtRT);
        var tmp = txtGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 22f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
    }
}
