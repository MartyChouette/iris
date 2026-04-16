using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Full-screen overlay showing Nema's personality spider chart (editable)
/// and her wellbeing flower chart (read-only). Opens when clicking Nema
/// in the apartment during non-date phases.
///
/// Left side: Spider chart — drag vertices to allocate personality points.
/// Right side: Flower chart — petals bloom/wilt showing her life satisfaction.
/// </summary>
public class NemaProfilePanel : MonoBehaviour
{
    public static NemaProfilePanel Instance { get; private set; }

    [Header("Data")]
    [Tooltip("Nema's personality ScriptableObject. Create via Iris/Nema Personality.")]
    [SerializeField] private NemaPersonality _personality;

    [Header("Layout")]
    [SerializeField] private float _chartRadius = 120f;
    [SerializeField] private float _petalMaxRadius = 100f;

    private Canvas _canvas;
    private GameObject _panelRoot;
    private CanvasGroup _group;
    private bool _isOpen;

    // Spider chart
    private RectTransform[] _spiderVertices;
    private TMP_Text[] _spiderLabels;
    private TMP_Text _pointsRemainingText;
    private RawImage _spiderChartImage;
    private Texture2D _spiderTex;

    // Flower chart
    private RawImage _flowerChartImage;
    private Texture2D _flowerTex;
    private TMP_Text _overallText;

    private const int TexSize = 280;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoSpawn()
    {
        if (Instance != null) return;
        var go = new GameObject("[NemaProfilePanel]");
        go.AddComponent<NemaProfilePanel>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildUI();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_spiderTex != null) Destroy(_spiderTex);
        if (_flowerTex != null) Destroy(_flowerTex);
    }

    public void Open()
    {
        if (_isOpen) return;
        _isOpen = true;
        _panelRoot.SetActive(true);
        _group.alpha = 1f;

        // Recalculate wellbeing when opening
        NemaWellbeing.Instance?.Recalculate();

        RedrawSpiderChart();
        RedrawFlowerChart();
        UpdateLabels();
    }

    public void Close()
    {
        _isOpen = false;
        _panelRoot.SetActive(false);
    }

    public bool IsOpen => _isOpen;

    private void Update()
    {
        if (!_isOpen) return;

        // ESC to close
        if (Input.GetKeyDown(KeyCode.Escape) ||
            (IrisInput.Instance != null && IrisInput.Instance.Pause.WasPressedThisFrame()))
        {
            Close();
            return;
        }

        // Click on spider chart to adjust traits
        if (Input.GetMouseButton(0) && _spiderChartImage != null)
            HandleSpiderClick();

        // Animate flower chart smoothly
        RedrawFlowerChart();
    }

    private void HandleSpiderClick()
    {
        if (_personality == null) return;

        Vector2 mousePos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _spiderChartImage.rectTransform, Input.mousePosition, null, out mousePos);

        Vector2 center = Vector2.zero;
        float dist = Vector2.Distance(mousePos, center);
        if (dist < 10f || dist > _chartRadius + 20f) return;

        // Find which trait axis the click is nearest to
        float angle = Mathf.Atan2(mousePos.y, mousePos.x) * Mathf.Rad2Deg;
        if (angle < 0f) angle += 360f;

        int closestTrait = 0;
        float closestAngleDist = float.MaxValue;
        for (int i = 0; i < NemaPersonality.TraitCount; i++)
        {
            float traitAngle = 90f + i * (360f / NemaPersonality.TraitCount);
            if (traitAngle >= 360f) traitAngle -= 360f;
            float d = Mathf.Abs(Mathf.DeltaAngle(angle, traitAngle));
            if (d < closestAngleDist)
            {
                closestAngleDist = d;
                closestTrait = i;
            }
        }

        if (closestAngleDist > 36f) return; // too far from any axis

        // Map distance to trait value (0-5)
        int newValue = Mathf.RoundToInt(Mathf.Clamp01(dist / _chartRadius) * NemaPersonality.MaxPerTrait);
        int oldValue = _personality.GetTrait(closestTrait);
        int delta = newValue - oldValue;

        // Check budget
        if (delta > 0 && _personality.PointsRemaining < delta)
            newValue = oldValue + _personality.PointsRemaining;

        _personality.SetTrait(closestTrait, newValue);
        RedrawSpiderChart();
        UpdateLabels();
    }

    // ── Drawing ────────────────────────────────────────────────

    // Reusable pixel buffers — allocated once, reused on every redraw
    private Color32[] _spiderPixels;
    private Color32[] _flowerPixels;

    private void RedrawSpiderChart()
    {
        if (_personality == null || _spiderTex == null) return;

        if (_spiderPixels == null || _spiderPixels.Length != TexSize * TexSize)
            _spiderPixels = new Color32[TexSize * TexSize];
        var px = _spiderPixels;
        var bg = new Color32(0, 0, 0, 0);
        System.Array.Fill(px, bg);

        Vector2 center = new Vector2(TexSize / 2f, TexSize / 2f);
        float radius = TexSize * 0.4f;

        // Grid rings (3 rings)
        var gridColor = new Color32(80, 80, 85, 60);
        for (int ring = 1; ring <= 3; ring++)
        {
            float r = radius * ring / 3f;
            DrawCircle(px, center, r, gridColor);
        }

        // Axis lines
        var axisColor = new Color32(120, 115, 110, 80);
        for (int i = 0; i < NemaPersonality.TraitCount; i++)
        {
            float angle = (90f + i * 72f) * Mathf.Deg2Rad;
            Vector2 end = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            DrawLine(px, center, end, axisColor);
        }

        // Filled shape
        Vector2[] points = new Vector2[NemaPersonality.TraitCount];
        for (int i = 0; i < NemaPersonality.TraitCount; i++)
        {
            float norm = _personality.GetNormalized(i);
            float angle = (90f + i * 72f) * Mathf.Deg2Rad;
            points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * Mathf.Max(norm, 0.05f);
        }

        // Fill polygon
        var fillColor = new Color32(180, 140, 200, 50);
        FillPolygon(px, points, fillColor);

        // Outline
        var outlineColor = new Color32(200, 170, 220, 200);
        for (int i = 0; i < points.Length; i++)
        {
            int next = (i + 1) % points.Length;
            DrawLine(px, points[i], points[next], outlineColor);
        }

        // Vertex dots
        var dotColor = new Color32(240, 220, 240, 255);
        for (int i = 0; i < points.Length; i++)
            DrawDot(px, points[i], 3, dotColor);

        _spiderTex.SetPixels32(px);
        _spiderTex.Apply();
    }

    private void RedrawFlowerChart()
    {
        if (_flowerTex == null || NemaWellbeing.Instance == null) return;

        if (_flowerPixels == null || _flowerPixels.Length != TexSize * TexSize)
            _flowerPixels = new Color32[TexSize * TexSize];
        var px = _flowerPixels;
        System.Array.Fill(px, new Color32(0, 0, 0, 0));

        Vector2 center = new Vector2(TexSize / 2f, TexSize / 2f);
        float maxR = TexSize * 0.38f;

        // Draw petals
        Color32[] petalColors =
        {
            new Color32(255, 200, 180, 140),  // Comfort — warm peach
            new Color32(255, 150, 170, 140),  // Romance — pink
            new Color32(180, 200, 255, 140),  // Expression — soft blue
            new Color32(160, 220, 160, 140),  // Nature — green
            new Color32(240, 220, 160, 140),  // Social — warm yellow
        };

        Color32[] petalOutlines =
        {
            new Color32(230, 160, 140, 200),
            new Color32(230, 110, 130, 200),
            new Color32(140, 160, 230, 200),
            new Color32(120, 190, 120, 200),
            new Color32(210, 190, 120, 200),
        };

        for (int i = 0; i < NemaWellbeing.PetalCount; i++)
        {
            float value = NemaWellbeing.Instance.GetDisplayPetal(i);
            float angle = (90f + i * 72f) * Mathf.Deg2Rad;
            float petalR = maxR * Mathf.Max(value, 0.08f);

            // Petal is an elongated ellipse along the axis
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 tip = center + dir * petalR;

            // Draw petal as a filled triangle-ish shape
            Vector2 perp = new Vector2(-dir.y, dir.x);
            float width = petalR * 0.35f;
            Vector2 left = center + perp * width;
            Vector2 right = center - perp * width;

            FillTriangle(px, left, right, tip, petalColors[i]);
            DrawLine(px, left, tip, petalOutlines[i]);
            DrawLine(px, right, tip, petalOutlines[i]);
            DrawLine(px, left, right, petalOutlines[i]);
        }

        // Center dot (flower center)
        var centerColor = new Color32(240, 220, 180, 255);
        DrawDot(px, center, 6, centerColor);

        // Wilted overlay: if overall < 0.3, gray tint
        float overall = NemaWellbeing.Instance.Overall;
        if (overall < 0.3f)
        {
            var gray = new Color32(100, 100, 100, (byte)((0.3f - overall) * 200));
            for (int j = 0; j < px.Length; j++)
            {
                if (px[j].a > 0)
                    px[j] = BlendColor(px[j], gray);
            }
        }

        _flowerTex.SetPixels32(px);
        _flowerTex.Apply();

        if (_overallText != null)
        {
            string mood = overall switch
            {
                >= 0.8f => "Thriving",
                >= 0.6f => "Content",
                >= 0.4f => "Okay",
                >= 0.2f => "Struggling",
                _ => "Wilting"
            };
            _overallText.text = mood;
            _overallText.color = Color.Lerp(
                new Color(0.7f, 0.5f, 0.5f),
                new Color(0.6f, 0.85f, 0.6f),
                overall);
        }
    }

    private void UpdateLabels()
    {
        if (_personality == null) return;
        for (int i = 0; i < NemaPersonality.TraitCount; i++)
        {
            if (_spiderLabels != null && i < _spiderLabels.Length && _spiderLabels[i] != null)
            {
                int val = _personality.GetTrait(i);
                _spiderLabels[i].text = $"{NemaPersonality.TraitNames[i]} {val}";
            }
        }
        if (_pointsRemainingText != null)
            _pointsRemainingText.text = $"Points: {_personality.PointsRemaining}";
    }

    // ── UI Building ────────────────────────────────────────────

    private void BuildUI()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 95;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        gameObject.AddComponent<GraphicRaycaster>();

        _group = gameObject.AddComponent<CanvasGroup>();

        // Root panel (full screen dim)
        _panelRoot = new GameObject("PanelRoot");
        _panelRoot.transform.SetParent(transform, false);
        var rootRT = _panelRoot.AddComponent<RectTransform>();
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.sizeDelta = Vector2.zero;
        var dimBg = _panelRoot.AddComponent<Image>();
        dimBg.color = new Color(0f, 0f, 0f, 0.7f);

        // Close button (click background to close)
        var closeBtn = _panelRoot.AddComponent<Button>();
        closeBtn.onClick.AddListener(Close);

        // Content panel (centered card)
        var contentGO = new GameObject("Content");
        contentGO.transform.SetParent(_panelRoot.transform, false);
        var contentRT = contentGO.AddComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0.5f, 0.5f);
        contentRT.anchorMax = new Vector2(0.5f, 0.5f);
        contentRT.sizeDelta = new Vector2(800f, 500f);
        var contentBg = contentGO.AddComponent<Image>();
        contentBg.color = new Color(0.1f, 0.1f, 0.12f, 0.95f);
        contentBg.raycastTarget = true; // block clicks through to dimBg

        // Title
        var titleGO = MakeText(contentGO.transform, "Nema", 28f, FontStyles.Bold,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -10f), new Vector2(0f, -50f));
        titleGO.color = new Color(0.95f, 0.92f, 0.85f);

        // ── Left: Spider Chart ──
        var spiderGO = new GameObject("SpiderChart");
        spiderGO.transform.SetParent(contentGO.transform, false);
        var spiderRT = spiderGO.AddComponent<RectTransform>();
        spiderRT.anchorMin = new Vector2(0f, 0f);
        spiderRT.anchorMax = new Vector2(0.5f, 0.9f);
        spiderRT.offsetMin = new Vector2(20f, 20f);
        spiderRT.offsetMax = new Vector2(-10f, 0f);

        var spiderLabel = MakeText(spiderGO.transform, "Personality", 18f, FontStyles.Normal,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -5f), new Vector2(0f, -28f));
        spiderLabel.color = new Color(0.8f, 0.75f, 0.85f);

        _spiderTex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false);
        _spiderTex.filterMode = FilterMode.Bilinear;
        _spiderChartImage = MakeRawImage(spiderGO.transform, _spiderTex, TexSize);

        // Points remaining
        var pointsGO = new GameObject("Points");
        pointsGO.transform.SetParent(spiderGO.transform, false);
        var pointsRT = pointsGO.AddComponent<RectTransform>();
        pointsRT.anchorMin = new Vector2(0f, 0f);
        pointsRT.anchorMax = new Vector2(1f, 0f);
        pointsRT.sizeDelta = new Vector2(0f, 25f);
        _pointsRemainingText = pointsGO.AddComponent<TextMeshProUGUI>();
        _pointsRemainingText.fontSize = 16f;
        _pointsRemainingText.alignment = TextAlignmentOptions.Center;
        _pointsRemainingText.color = new Color(0.7f, 0.7f, 0.7f);

        // Spider labels (positioned around chart)
        _spiderLabels = new TMP_Text[NemaPersonality.TraitCount];
        for (int i = 0; i < NemaPersonality.TraitCount; i++)
        {
            float angle = (90f + i * 72f) * Mathf.Deg2Rad;
            var labelGO = new GameObject($"Label_{i}");
            labelGO.transform.SetParent(spiderGO.transform, false);
            var labelRT = labelGO.AddComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0.5f, 0.5f);
            labelRT.anchorMax = new Vector2(0.5f, 0.5f);
            labelRT.sizeDelta = new Vector2(100f, 20f);
            float labelDist = TexSize * 0.28f;
            labelRT.anchoredPosition = new Vector2(
                Mathf.Cos(angle) * labelDist,
                Mathf.Sin(angle) * labelDist + 10f);
            _spiderLabels[i] = labelGO.AddComponent<TextMeshProUGUI>();
            _spiderLabels[i].fontSize = 13f;
            _spiderLabels[i].alignment = TextAlignmentOptions.Center;
            _spiderLabels[i].color = new Color(0.85f, 0.82f, 0.9f);
        }

        // ── Right: Flower Chart ──
        var flowerGO = new GameObject("FlowerChart");
        flowerGO.transform.SetParent(contentGO.transform, false);
        var flowerRT = flowerGO.AddComponent<RectTransform>();
        flowerRT.anchorMin = new Vector2(0.5f, 0f);
        flowerRT.anchorMax = new Vector2(1f, 0.9f);
        flowerRT.offsetMin = new Vector2(10f, 20f);
        flowerRT.offsetMax = new Vector2(-20f, 0f);

        var flowerLabel = MakeText(flowerGO.transform, "Wellbeing", 18f, FontStyles.Normal,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -5f), new Vector2(0f, -28f));
        flowerLabel.color = new Color(0.75f, 0.85f, 0.75f);

        _flowerTex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false);
        _flowerTex.filterMode = FilterMode.Bilinear;
        _flowerChartImage = MakeRawImage(flowerGO.transform, _flowerTex, TexSize);

        // Overall mood text
        var overallGO = new GameObject("Overall");
        overallGO.transform.SetParent(flowerGO.transform, false);
        var overallRT = overallGO.AddComponent<RectTransform>();
        overallRT.anchorMin = new Vector2(0f, 0f);
        overallRT.anchorMax = new Vector2(1f, 0f);
        overallRT.sizeDelta = new Vector2(0f, 25f);
        _overallText = overallGO.AddComponent<TextMeshProUGUI>();
        _overallText.fontSize = 18f;
        _overallText.alignment = TextAlignmentOptions.Center;
        _overallText.fontStyle = FontStyles.Italic;

        // Petal labels
        for (int i = 0; i < NemaWellbeing.PetalCount; i++)
        {
            float angle = (90f + i * 72f) * Mathf.Deg2Rad;
            var pLabelGO = new GameObject($"PetalLabel_{i}");
            pLabelGO.transform.SetParent(flowerGO.transform, false);
            var pLabelRT = pLabelGO.AddComponent<RectTransform>();
            pLabelRT.anchorMin = new Vector2(0.5f, 0.5f);
            pLabelRT.anchorMax = new Vector2(0.5f, 0.5f);
            pLabelRT.sizeDelta = new Vector2(90f, 18f);
            float labelDist = TexSize * 0.28f;
            pLabelRT.anchoredPosition = new Vector2(
                Mathf.Cos(angle) * labelDist,
                Mathf.Sin(angle) * labelDist + 10f);
            var pLabel = pLabelGO.AddComponent<TextMeshProUGUI>();
            pLabel.fontSize = 11f;
            pLabel.alignment = TextAlignmentOptions.Center;
            pLabel.color = new Color(0.75f, 0.8f, 0.75f);
            pLabel.text = NemaWellbeing.PetalNames[i];
        }

        _panelRoot.SetActive(false);
    }

    // ── Helpers ────────────────────────────────────────────────

    private TMP_Text MakeText(Transform parent, string text, float size, FontStyles style,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return tmp;
    }

    private RawImage MakeRawImage(Transform parent, Texture2D tex, int size)
    {
        var go = new GameObject("ChartImage");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = new Vector2(0f, -5f);
        var img = go.AddComponent<RawImage>();
        img.texture = tex;
        img.raycastTarget = true;
        return img;
    }

    // ── Pixel drawing primitives ──────────────────────────────

    private static void DrawDot(Color32[] px, Vector2 pos, int r, Color32 c)
    {
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                if (dx * dx + dy * dy <= r * r)
                    SetPx(px, (int)pos.x + dx, (int)pos.y + dy, c);
    }

    private static void DrawCircle(Color32[] px, Vector2 center, float radius, Color32 c)
    {
        int steps = Mathf.Max(32, (int)(radius * 2));
        for (int i = 0; i < steps; i++)
        {
            float a = i * Mathf.PI * 2f / steps;
            SetPx(px, (int)(center.x + Mathf.Cos(a) * radius), (int)(center.y + Mathf.Sin(a) * radius), c);
        }
    }

    private static void DrawLine(Color32[] px, Vector2 a, Vector2 b, Color32 c)
    {
        int steps = Mathf.Max(1, (int)Vector2.Distance(a, b));
        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            SetPx(px, (int)Mathf.Lerp(a.x, b.x, t), (int)Mathf.Lerp(a.y, b.y, t), c);
        }
    }

    private static void FillPolygon(Color32[] px, Vector2[] verts, Color32 c)
    {
        // Simple scanline fill
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var v in verts) { if (v.y < minY) minY = v.y; if (v.y > maxY) maxY = v.y; }
        for (int y = (int)minY; y <= (int)maxY; y++)
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            for (int i = 0; i < verts.Length; i++)
            {
                int j = (i + 1) % verts.Length;
                if ((verts[i].y <= y && verts[j].y > y) || (verts[j].y <= y && verts[i].y > y))
                {
                    float x = verts[i].x + (y - verts[i].y) / (verts[j].y - verts[i].y) * (verts[j].x - verts[i].x);
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                }
            }
            for (int x = (int)minX; x <= (int)maxX; x++)
                SetPx(px, x, y, c);
        }
    }

    private static void FillTriangle(Color32[] px, Vector2 a, Vector2 b, Vector2 c, Color32 col)
    {
        FillPolygon(px, new[] { a, b, c }, col);
    }

    private static void SetPx(Color32[] px, int x, int y, Color32 c)
    {
        if (x >= 0 && x < TexSize && y >= 0 && y < TexSize)
        {
            int idx = y * TexSize + x;
            if (c.a >= 255 || px[idx].a == 0)
                px[idx] = c;
            else
                px[idx] = BlendColor(px[idx], c);
        }
    }

    private static Color32 BlendColor(Color32 dst, Color32 src)
    {
        float sa = src.a / 255f;
        float da = 1f - sa;
        return new Color32(
            (byte)(src.r * sa + dst.r * da),
            (byte)(src.g * sa + dst.g * da),
            (byte)(src.b * sa + dst.b * da),
            (byte)Mathf.Min(255, src.a + dst.a));
    }
}
