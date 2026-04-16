using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Toggleable left-side checklist showing what the selected date likes.
/// Numpad 1 toggles. Subscribes to NewspaperManager.OnDateSelected.
/// Polls ReactableTag.All each frame when visible for live completion.
/// Auto-hides when date phase starts.
/// </summary>
public class PrepChecklistPanel : MonoBehaviour
{
    public static PrepChecklistPanel Instance { get; private set; }

    // ─── Tag → human-readable preparation action ─────────────────
    private static readonly Dictionary<string, string> s_tagDescriptions = new Dictionary<string, string>
    {
        { "vinyl",          "Play a vinyl record" },
        { "book",           "Display a book" },
        { "plant",          "Water the plants" },
        { "perfume",        "Spray a perfume" },
        { "perfume_floral", "Spray a floral perfume" },
        { "perfume_woody",  "Spray a woody perfume" },
        { "perfume_citrus", "Spray a citrus perfume" },
        { "coffee_book",    "Display a coffee table book" },
        { "clean",          "Clean the apartment" },
        { "drink",          "Make a drink" },
        { "outfit",         "Choose the right outfit" },
        { "record",         "Play a record" },
        { "flower",         "Display a flower" },
        { "gift",           "Display a gift" },
    };

    private InputAction _toggleAction;
    private Canvas _canvas;
    private GameObject _panelRoot;
    private TMP_Text _titleText;
    private readonly List<ChecklistRow> _rows = new List<ChecklistRow>();

    private DatePersonalDefinition _currentDate;
    private bool _visible;

    private struct ChecklistRow
    {
        public string tag;
        public TMP_Text label;
        public TMP_Text icon;
        public bool completed;
    }

    // ─── Lifecycle ───────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _toggleAction = new InputAction("ToggleChecklist", InputActionType.Button,
            "<Keyboard>/numpad1");

        BuildUI();
        _panelRoot.SetActive(false);
    }

    private void OnEnable()
    {
        _toggleAction.Enable();

        if (NewspaperManager.Instance != null)
            NewspaperManager.Instance.OnDateSelected.AddListener(OnDateSelected);
    }

    private void OnDisable()
    {
        _toggleAction.Disable();

        if (NewspaperManager.Instance != null)
            NewspaperManager.Instance.OnDateSelected.RemoveListener(OnDateSelected);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (NewspaperManager.Instance != null)
            NewspaperManager.Instance.OnDateSelected.RemoveListener(OnDateSelected);
        if (DayPhaseManager.Instance != null)
            DayPhaseManager.Instance.OnPhaseChanged.RemoveListener(OnPhaseChanged);
    }

    private void Start()
    {
        // Also subscribe to phase changes to auto-hide
        if (DayPhaseManager.Instance != null)
            DayPhaseManager.Instance.OnPhaseChanged.AddListener(OnPhaseChanged);
    }

    private void Update()
    {
        if (_toggleAction.WasPressedThisFrame() && _currentDate != null)
            SetVisible(!_visible);

        if (_visible)
            RefreshCompletion();
    }

    // ─── Events ──────────────────────────────────────────────────

    private void OnDateSelected(DatePersonalDefinition def)
    {
        _currentDate = def;
        PopulateChecklist(def);
        SetVisible(true);
    }

    private void OnPhaseChanged(int phase)
    {
        // Auto-hide when date starts or morning begins
        if (phase == (int)DayPhaseManager.DayPhase.DateInProgress ||
            phase == (int)DayPhaseManager.DayPhase.Morning)
        {
            SetVisible(false);
            _currentDate = null;
        }
    }

    // ─── Core ────────────────────────────────────────────────────

    private void PopulateChecklist(DatePersonalDefinition def)
    {
        // Clear existing rows
        foreach (var row in _rows)
        {
            if (row.label != null) Destroy(row.label.transform.parent.gameObject);
        }
        _rows.Clear();

        if (_titleText != null)
            _titleText.text = $"{def.characterName} likes...";

        if (def.preferences == null) return;

        foreach (string tag in def.preferences.likedTags)
        {
            if (string.IsNullOrEmpty(tag)) continue;

            string desc = s_tagDescriptions.TryGetValue(tag, out string d) ? d : tag;
            var row = CreateRow(tag, desc);
            _rows.Add(row);
        }
    }

    private void RefreshCompletion()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            bool done = IsTagActive(row.tag);

            if (done != row.completed)
            {
                row.completed = done;
                row.icon.text = done ? "<color=#4CAF50>\u2713</color>" : "<color=#888>\u25CB</color>";
                row.label.color = done
                    ? new Color(0.6f, 0.8f, 0.6f)
                    : new Color(0.85f, 0.85f, 0.85f);
                _rows[i] = row;
            }
        }
    }

    private bool IsTagActive(string tag)
    {
        var all = ReactableTag.All;
        for (int i = 0; i < all.Count; i++)
        {
            if (!all[i].IsActive) continue;
            var tags = all[i].Tags;
            if (tags == null) continue;
            for (int t = 0; t < tags.Length; t++)
            {
                if (tags[t] == tag) return true;
            }
        }
        return false;
    }

    private void SetVisible(bool vis)
    {
        _visible = vis;
        if (_panelRoot != null) _panelRoot.SetActive(vis);
    }

    // ─── UI Construction ─────────────────────────────────────────

    private void BuildUI()
    {
        // ScreenSpace Overlay canvas
        var canvasGO = new GameObject("PrepChecklistCanvas");
        canvasGO.transform.SetParent(transform, false);
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 50;
        canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGO.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
        canvasGO.AddComponent<GraphicRaycaster>();

        // Panel root (left side)
        _panelRoot = new GameObject("Panel");
        _panelRoot.transform.SetParent(canvasGO.transform, false);
        var panelRT = _panelRoot.AddComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0f, 0.3f);
        panelRT.anchorMax = new Vector2(0f, 0.85f);
        panelRT.pivot = new Vector2(0f, 1f);
        panelRT.offsetMin = new Vector2(10f, 0f);
        panelRT.offsetMax = new Vector2(290f, 0f);
        panelRT.localScale = Vector3.one;

        var panelImg = _panelRoot.AddComponent<Image>();
        panelImg.color = new Color(0.06f, 0.06f, 0.08f, 0.82f);
        panelImg.raycastTarget = false;

        // Title
        var titleGO = new GameObject("Title");
        titleGO.transform.SetParent(_panelRoot.transform, false);
        var titleRT = titleGO.AddComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0f, 1f);
        titleRT.anchorMax = new Vector2(1f, 1f);
        titleRT.pivot = new Vector2(0.5f, 1f);
        titleRT.anchoredPosition = new Vector2(0f, -8f);
        titleRT.sizeDelta = new Vector2(0f, 36f);
        titleRT.localScale = Vector3.one;
        _titleText = titleGO.AddComponent<TextMeshProUGUI>();
        _titleText.text = "Preparing...";
        _titleText.fontSize = 20f;
        _titleText.fontStyle = FontStyles.Bold;
        _titleText.color = new Color(0.95f, 0.9f, 0.8f);
        _titleText.alignment = TextAlignmentOptions.Center;
        _titleText.raycastTarget = false;

        // Hint text
        var hintGO = new GameObject("Hint");
        hintGO.transform.SetParent(_panelRoot.transform, false);
        var hintRT = hintGO.AddComponent<RectTransform>();
        hintRT.anchorMin = new Vector2(0f, 0f);
        hintRT.anchorMax = new Vector2(1f, 0f);
        hintRT.pivot = new Vector2(0.5f, 0f);
        hintRT.anchoredPosition = new Vector2(0f, 4f);
        hintRT.sizeDelta = new Vector2(0f, 20f);
        hintRT.localScale = Vector3.one;
        var hintTMP = hintGO.AddComponent<TextMeshProUGUI>();
        hintTMP.text = "[Numpad 1] toggle";
        hintTMP.fontSize = 11f;
        hintTMP.fontStyle = FontStyles.Italic;
        hintTMP.color = new Color(0.5f, 0.5f, 0.5f);
        hintTMP.alignment = TextAlignmentOptions.Center;
        hintTMP.raycastTarget = false;
    }

    private ChecklistRow CreateRow(string tag, string description)
    {
        var rowGO = new GameObject($"Row_{tag}");
        rowGO.transform.SetParent(_panelRoot.transform, false);
        var rowRT = rowGO.AddComponent<RectTransform>();
        rowRT.anchorMin = new Vector2(0f, 1f);
        rowRT.anchorMax = new Vector2(1f, 1f);
        rowRT.pivot = new Vector2(0.5f, 1f);
        float yOffset = -48f - _rows.Count * 32f;
        rowRT.anchoredPosition = new Vector2(0f, yOffset);
        rowRT.sizeDelta = new Vector2(0f, 28f);
        rowRT.localScale = Vector3.one;

        // Icon (check or circle)
        var iconGO = new GameObject("Icon");
        iconGO.transform.SetParent(rowGO.transform, false);
        var iconRT = iconGO.AddComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0f, 0f);
        iconRT.anchorMax = new Vector2(0f, 1f);
        iconRT.pivot = new Vector2(0f, 0.5f);
        iconRT.anchoredPosition = new Vector2(12f, 0f);
        iconRT.sizeDelta = new Vector2(24f, 0f);
        iconRT.localScale = Vector3.one;
        var iconTMP = iconGO.AddComponent<TextMeshProUGUI>();
        iconTMP.text = "<color=#888>\u25CB</color>";
        iconTMP.fontSize = 16f;
        iconTMP.alignment = TextAlignmentOptions.Center;
        iconTMP.raycastTarget = false;

        // Label
        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(rowGO.transform, false);
        var labelRT = labelGO.AddComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0f, 0f);
        labelRT.anchorMax = new Vector2(1f, 1f);
        labelRT.offsetMin = new Vector2(38f, 0f);
        labelRT.offsetMax = new Vector2(-8f, 0f);
        labelRT.localScale = Vector3.one;
        var labelTMP = labelGO.AddComponent<TextMeshProUGUI>();
        labelTMP.text = description;
        labelTMP.fontSize = 15f;
        labelTMP.color = new Color(0.85f, 0.85f, 0.85f);
        labelTMP.alignment = TextAlignmentOptions.Left;
        labelTMP.raycastTarget = false;

        return new ChecklistRow
        {
            tag = tag,
            label = labelTMP,
            icon = iconTMP,
            completed = false
        };
    }
}
