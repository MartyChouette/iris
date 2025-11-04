using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(40000)]
public class JuiceHUD : MonoBehaviour
{
    [Header("Settings Asset (required)")]
    public JuiceSettings settings;

    [Header("Behavior")]
    [Tooltip("If ON, this script will create its own panel & toggles. Leave OFF to use your existing debug UI.")]
    public bool buildUI = false;   // ← default OFF so nothing gets drawn

    [Header("Use Existing UI (only if buildUI = false)")]
    public Toggle uiJuiceToggle;      // binds to settings.enableUIJuice
    public Toggle timeJuiceToggle;    // binds to settings.enableTimeJuice
    public Toggle sapFxToggle;        // binds to settings.enableSapFX (optional)

    // ===== The rest of these are only used when buildUI = true =====
    [Header("Canvas hookup (optional, only when buildUI = true)")]
    public Canvas targetCanvas;
    public RectTransform panelParent;

    [Header("Panel Style (only when buildUI = true)")]
    public Vector2 size = new Vector2(220, 140);
    [Tooltip("Anchor to screen edge (1,0.5 = right center, 0,0.5 = left center).")]
    public Vector2 anchor = new Vector2(1f, 0.5f);
    public Vector2 margin = new Vector2(12, 0);
    [Range(0, 1)] public float panelAlpha = 0.8f;
    public int sortingOrderAdd = 10;

    Canvas _canvas;
    RectTransform _panel;
    Toggle _uiJuiceT, _timeJuiceT, _sapFxT;

    void Awake()
    {
        if (!settings)
        {
            Debug.LogWarning($"{name}: JuiceHUD needs a JuiceSettings asset.");
            enabled = false;
            return;
        }

        if (!buildUI)
        {
            // Binder-only mode: DO NOT CREATE ANY UI.
            // Just seed existing toggles (if provided).
            if (uiJuiceToggle) uiJuiceToggle.isOn = settings.enableUIJuice;
            if (timeJuiceToggle) timeJuiceToggle.isOn = settings.enableTimeJuice;
            if (sapFxToggle) sapFxToggle.isOn = settings.enableSapFX;

            // Hook listeners
            if (uiJuiceToggle) uiJuiceToggle.onValueChanged.AddListener(v => settings.enableUIJuice = v);
            if (timeJuiceToggle) timeJuiceToggle.onValueChanged.AddListener(v => settings.enableTimeJuice = v);
            if (sapFxToggle) sapFxToggle.onValueChanged.AddListener(v => settings.enableSapFX = v);

            return; // ← nothing drawn
        }

        // ------- Only runs when buildUI == true (legacy self-built panel) -------
        _canvas = ResolveCanvas(targetCanvas);
        if (!_canvas) _canvas = CreateFallbackCanvas();

        Transform parent = panelParent ? panelParent : _canvas.transform;

        _panel = CreatePanel(parent, "Juice Panel", size, anchor, margin, panelAlpha);

        _uiJuiceT = CreateToggle(_panel, new Vector2(12, -16), "UI Juice",
                                   settings.enableUIJuice, v => settings.enableUIJuice = v);
        _timeJuiceT = CreateToggle(_panel, new Vector2(12, -56), "Time Juice",
                                   settings.enableTimeJuice, v => settings.enableTimeJuice = v);
        _sapFxT = CreateToggle(_panel, new Vector2(12, -96), "Sap FX",
                                   settings.enableSapFX, v => settings.enableSapFX = v);
    }

    // ===== Helpers used only by buildUI path =====
    Canvas ResolveCanvas(Canvas preferred)
    {
        if (preferred && preferred.isActiveAndEnabled) return preferred;

        var canvases = FindObjectsOfType<Canvas>(includeInactive: false);
        foreach (var c in canvases)
        {
            if (!c || !c.isActiveAndEnabled) continue;
            if (c.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                c.sortingOrder += sortingOrderAdd;
                return c;
            }
        }
        foreach (var c in canvases)
        {
            if (c && c.isActiveAndEnabled) { c.sortingOrder += sortingOrderAdd; return c; }
        }
        return null;
    }

    Canvas CreateFallbackCanvas()
    {
        var go = new GameObject("JuiceHUD_FallbackCanvas",
                                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var c = go.GetComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 5000;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        return c;
    }

    RectTransform CreatePanel(Transform parent, string name, Vector2 size, Vector2 anchor, Vector2 margin, float alpha)
    {
        var go = new GameObject(name, typeof(Image));
        go.transform.SetParent(parent, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor;
        rt.sizeDelta = size;
        rt.anchoredPosition = new Vector2(anchor.x > 0.5f ? -margin.x : margin.x,
                                          anchor.y > 0.5f ? -margin.y : margin.y);

        var img = go.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, alpha);

        // Header
        var header = new GameObject("Header", typeof(Text));
        header.transform.SetParent(rt, false);
        var ht = header.GetComponent<Text>();
        ht.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        ht.fontSize = 16; ht.alignment = TextAnchor.UpperLeft;
        ht.color = Color.white; ht.text = "JUICE";
        var hrt = header.GetComponent<RectTransform>();
        hrt.anchorMin = hrt.anchorMax = new Vector2(0, 1); hrt.pivot = new Vector2(0, 1);
        hrt.anchoredPosition = new Vector2(12, -8);
        hrt.sizeDelta = new Vector2(size.x - 24, 22);

        return rt;
    }

    Toggle CreateToggle(RectTransform parent, Vector2 pos, string label, bool initial, System.Action<bool> onChanged)
    {
        var row = new GameObject(label, typeof(RectTransform));
        row.transform.SetParent(parent, false);
        var rt = row.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(parent.sizeDelta.x - 24, 28);

        var tgo = new GameObject("Toggle", typeof(Toggle), typeof(Image));
        tgo.transform.SetParent(row.transform, false);
        var tgr = tgo.GetComponent<RectTransform>();
        tgr.anchorMin = tgr.anchorMax = new Vector2(0, 0.5f);
        tgr.pivot = new Vector2(0, 0.5f);
        tgr.anchoredPosition = new Vector2(0, 0);
        tgr.sizeDelta = new Vector2(24, 24);

        var bg = tgo.GetComponent<Image>();
        bg.color = new Color(1, 1, 1, 0.10f);

        var checkGO = new GameObject("Check", typeof(Image));
        checkGO.transform.SetParent(tgr, false);
        var check = checkGO.GetComponent<Image>();
        var cr = checkGO.GetComponent<RectTransform>();
        cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0.5f);
        cr.pivot = new Vector2(0.5f, 0.5f);
        cr.sizeDelta = new Vector2(14, 14);
        check.color = Color.white;

        var textGO = new GameObject("Label", typeof(Text));
        textGO.transform.SetParent(row.transform, false);
        var txt = textGO.GetComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        txt.fontSize = 14; txt.color = Color.white; txt.text = label;
        var lrt = textGO.GetComponent<RectTransform>();
        lrt.anchorMin = lrt.anchorMax = new Vector2(0, 0.5f);
        lrt.pivot = new Vector2(0, 0.5f);
        lrt.anchoredPosition = new Vector2(36, 0);
        lrt.sizeDelta = new Vector2(parent.sizeDelta.x - 48, 24);

        var t = tgo.GetComponent<Toggle>();
        t.isOn = initial;
        t.targetGraphic = bg;
        t.graphic = check;
        t.onValueChanged.AddListener(v => onChanged?.Invoke(v));

        check.enabled = initial;
        t.onValueChanged.AddListener(v => check.enabled = v);

        return t;
    }
}
