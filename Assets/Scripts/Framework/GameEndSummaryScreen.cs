using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Scene-scoped singleton that displays a "Thanks for Playing" summary screen
/// when the game session ends (demo timer or calendar complete).
/// Runtime-built UI, hidden by default. Show() populates stats and activates.
/// Continue button chains to PlaytestFeedbackForm then returns to main menu.
/// </summary>
public class GameEndSummaryScreen : MonoBehaviour
{
    public static GameEndSummaryScreen Instance { get; private set; }

    // ── UI references (built at runtime) ──
    private GameObject _canvasRoot;
    private TextMeshProUGUI _titleText;
    private TextMeshProUGUI _subtitleText;
    private TextMeshProUGUI _datesSectionText;
    private TextMeshProUGUI _flowerSectionText;
    private TextMeshProUGUI _apartmentSectionText;
    private Button _continueButton;

    private static readonly Color PanelBg = new Color(0.08f, 0.07f, 0.09f, 0.97f);
    private static readonly Color Eggshell = new Color(0.95f, 0.92f, 0.85f);
    private static readonly Color Muted = new Color(0.65f, 0.63f, 0.58f);
    private static readonly Color Accent = new Color(0.85f, 0.65f, 0.45f);
    private static readonly Color BtnColor = new Color(0.25f, 0.55f, 0.35f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneCallback()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Additive) return;
        if (Instance != null) return;
        var go = new GameObject("GameEndSummaryScreen");
        go.AddComponent<GameEndSummaryScreen>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        BuildUI();
        _canvasRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ═══════════════════════════════════════
    //  Public API
    // ═══════════════════════════════════════

    public void Show()
    {
        PopulateStats();
        _canvasRoot.SetActive(true);
        TimeScaleManager.Set(TimeScaleManager.PRIORITY_PAUSE, 0f);
        Debug.Log("[GameEndSummaryScreen] Showing summary.");
    }

    // ═══════════════════════════════════════
    //  Stats Population
    // ═══════════════════════════════════════

    private void PopulateStats()
    {
        // Title
        _titleText.text = "Thanks for Playing!";

        // Subtitle — mode name + days played
        string modeName = MainMenuManager.ActiveConfig != null
            ? MainMenuManager.ActiveConfig.modeName
            : "Game";
        int daysPlayed = GameClock.Instance != null ? GameClock.Instance.CurrentDay : 1;
        bool isDemoMode = GameClock.Instance != null && GameClock.Instance.IsDemoMode;

        if (isDemoMode)
        {
            float totalSeconds = MainMenuManager.ActiveConfig != null
                ? MainMenuManager.ActiveConfig.demoTimeLimitSeconds
                : 0f;
            int totalMins = Mathf.CeilToInt(totalSeconds / 60f);
            _subtitleText.text = $"~ {modeName} ({totalMins} min) ~";
        }
        else
        {
            _subtitleText.text = $"~ {modeName} — Day {daysPlayed} ~";
        }

        // ── Dates section ──
        var entries = DateHistory.Entries;
        if (entries != null && entries.Count > 0)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<b>Your Dates</b>");
            foreach (var e in entries)
            {
                string line = $"  Day {e.day}: {e.name} — {e.grade} ({e.affection:F0}%)";

                // Learned likes
                if (e.learnedLikes != null && e.learnedLikes.Count > 0)
                    line += $"  <color=#8BC48B>liked {string.Join(", ", e.learnedLikes)}</color>";

                // Learned dislikes
                if (e.learnedDislikes != null && e.learnedDislikes.Count > 0)
                    line += $"  <color=#C48B8B>disliked {string.Join(", ", e.learnedDislikes)}</color>";

                sb.AppendLine(line);
            }
            _datesSectionText.text = sb.ToString();
        }
        else
        {
            _datesSectionText.text = "<b>Your Dates</b>\n  No dates yet.";
        }

        // ── Flower section ──
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<b>Flower Trimming</b>");

            // Best flower from date history
            DateHistory.DateHistoryEntry bestFlower = null;
            if (entries != null)
            {
                foreach (var e in entries)
                {
                    if (e.flowerScore > 0 && (bestFlower == null || e.flowerScore > bestFlower.flowerScore))
                        bestFlower = e;
                }
            }

            if (bestFlower != null)
                sb.AppendLine($"  Best: {bestFlower.flowerGrade} ({bestFlower.flowerScore}pts, {bestFlower.flowerDaysAlive} days alive)");
            else
                sb.AppendLine("  No flowers trimmed.");

            // Living plants
            int plantCount = 0;
            int plantSlots = WaterablePlant.All.Count;
            if (LivingFlowerPlantManager.Instance != null)
            {
                var plants = LivingFlowerPlantManager.Instance.ActivePlants;
                plantCount = plants != null ? plants.Count : 0;
            }
            sb.AppendLine($"  Flowers still growing: {plantCount}/{plantSlots}");

            _flowerSectionText.text = sb.ToString();
        }

        // ── Apartment section ──
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<b>Your Apartment</b>");

            float tidiness = TidyScorer.Instance != null ? TidyScorer.Instance.OverallTidiness : -1f;
            if (tidiness >= 0f)
                sb.AppendLine($"  Tidiness: {Mathf.RoundToInt(tidiness * 100f)}%");
            else
                sb.AppendLine("  Tidiness: N/A");

            int plantCount = 0;
            int totalPlants = WaterablePlant.All.Count;
            if (LivingFlowerPlantManager.Instance != null)
            {
                var plants = LivingFlowerPlantManager.Instance.ActivePlants;
                plantCount = plants != null ? plants.Count : 0;
            }
            sb.AppendLine($"  Living plants: {plantCount}/{totalPlants}");

            _apartmentSectionText.text = sb.ToString();
        }
    }

    // ═══════════════════════════════════════
    //  Continue → Feedback → Menu
    // ═══════════════════════════════════════

    private void OnContinueClicked()
    {
        _canvasRoot.SetActive(false);

        if (PlaytestFeedbackForm.Instance != null)
        {
            PlaytestFeedbackForm.Instance.OpenWithCallback(ReturnToMenu);
        }
        else
        {
            ReturnToMenu();
        }
    }

    private void ReturnToMenu()
    {
        TimeScaleManager.ClearAll();
        MusicDirector.Instance?.PlayMenuSong();

        // Try known main menu scene names, then fall back to build index 0
        if (SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/mainmenu_nemahead.unity") >= 0)
        {
            SceneManager.LoadScene("mainmenu_nemahead");
        }
        else if (SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/mainmenu.unity") >= 0)
        {
            SceneManager.LoadScene("mainmenu");
        }
        else
        {
            Debug.Log("[GameEndSummaryScreen] Loading menu via build index 0.");
            SceneManager.LoadScene(0);
        }
    }

    // ═══════════════════════════════════════
    //  Runtime UI Construction
    // ═══════════════════════════════════════

    private void BuildUI()
    {
        // ── Canvas ──
        _canvasRoot = new GameObject("SummaryCanvas");
        _canvasRoot.transform.SetParent(transform, false);

        var canvas = _canvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        var scaler = _canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        _canvasRoot.AddComponent<GraphicRaycaster>();

        // ── Full-screen dim background ──
        var dimGO = MakeChild(_canvasRoot, "DimBg");
        var dimRT = dimGO.AddComponent<RectTransform>();
        StretchFill(dimRT);
        var dimImg = dimGO.AddComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.85f);

        // ── Panel ──
        var panel = MakeChild(_canvasRoot, "Panel");
        var panelRT = panel.AddComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta = new Vector2(750f, 700f);
        var panelImg = panel.AddComponent<Image>();
        panelImg.color = PanelBg;

        float y = -30f;

        // ── Title ──
        _titleText = AddText(panel, "Title", "", 34f, y, 50f, Eggshell, TextAlignmentOptions.Center);
        y -= 55f;

        // ── Subtitle ──
        _subtitleText = AddText(panel, "Subtitle", "", 22f, y, 32f, Muted, TextAlignmentOptions.Center);
        y -= 50f;

        // ── Divider ──
        y = AddDivider(panel, y);

        // ── Dates section ──
        _datesSectionText = AddText(panel, "Dates", "", 19f, y, 150f, Eggshell, TextAlignmentOptions.Left);
        _datesSectionText.richText = true;
        y -= 160f;

        // ── Divider ──
        y = AddDivider(panel, y);

        // ── Flower section ──
        _flowerSectionText = AddText(panel, "Flowers", "", 19f, y, 90f, Eggshell, TextAlignmentOptions.Left);
        _flowerSectionText.richText = true;
        y -= 100f;

        // ── Divider ──
        y = AddDivider(panel, y);

        // ── Apartment section ──
        _apartmentSectionText = AddText(panel, "Apartment", "", 19f, y, 80f, Eggshell, TextAlignmentOptions.Left);
        _apartmentSectionText.richText = true;
        y -= 90f;

        // ── Continue button ──
        y -= 15f;
        _continueButton = AddButton(panel, "ContinueBtn", "Continue", 0f, y, 220f, 50f, BtnColor);
        _continueButton.onClick.AddListener(OnContinueClicked);
    }

    // ── UI Factory Helpers ──

    private TextMeshProUGUI AddText(GameObject parent, string name, string text,
        float fontSize, float yPos, float height, Color color, TextAlignmentOptions align)
    {
        var go = MakeChild(parent, name);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.08f, 1f);
        rt.anchorMax = new Vector2(0.92f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, yPos);
        rt.sizeDelta = new Vector2(0f, height);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = align;
        tmp.color = color;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Ellipsis;

        return tmp;
    }

    private float AddDivider(GameObject parent, float yPos)
    {
        var go = MakeChild(parent, "Divider");
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.08f, 1f);
        rt.anchorMax = new Vector2(0.92f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, yPos);
        rt.sizeDelta = new Vector2(0f, 2f);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.3f, 0.28f, 0.25f, 0.6f);

        return yPos - 12f;
    }

    private Button AddButton(GameObject parent, string name, string label,
        float x, float yPos, float w, float h, Color color)
    {
        var btnGO = MakeChild(parent, name);
        var btnRT = btnGO.AddComponent<RectTransform>();
        btnRT.anchorMin = new Vector2(0.5f, 1f);
        btnRT.anchorMax = new Vector2(0.5f, 1f);
        btnRT.pivot = new Vector2(0.5f, 0.5f);
        btnRT.anchoredPosition = new Vector2(x, yPos - h * 0.5f);
        btnRT.sizeDelta = new Vector2(w, h);

        var img = btnGO.AddComponent<Image>();
        img.color = color;

        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = img;

        var txtGO = MakeChild(btnGO, "Label");
        var txtRT = txtGO.AddComponent<RectTransform>();
        StretchFill(txtRT);
        var tmp = txtGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 24f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        return btn;
    }

    private static GameObject MakeChild(GameObject parent, string name)
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
}
