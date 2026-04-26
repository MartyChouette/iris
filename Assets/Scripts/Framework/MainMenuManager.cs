using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Main menu controller with three panels:
/// ModeSelect → GamePanel → SaveSlots.
///
/// Static ActiveConfig persists across scene loads so GameClock / DayPhaseManager
/// can read the selected mode's pacing values on Start().
/// </summary>
public class MainMenuManager : MonoBehaviour
{
    // ── Static state (survives scene load) ───────────────────────
    public static GameModeConfig ActiveConfig { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ActiveConfig = null;
    }

    private enum MenuState { ModeSelect, GamePanel, SaveSlots }

    // ── Scene ────────────────────────────────────────────────────
    [Header("Scene")]
    [Tooltip("Build index of the apartment scene. -1 = next scene after this one.")]
    [SerializeField] private int _apartmentSceneIndex = -1;

    [Header("Fade")]
    [Tooltip("Fade duration before loading. 0 = instant.")]
    [SerializeField] private float _fadeDuration = 0.5f;

    // ── Game Mode Configs ────────────────────────────────────────
    [Header("Game Mode Configs")]
    [SerializeField] private GameModeConfig _demoConfig;
    [SerializeField] private GameModeConfig _showcaseConfig;
    [SerializeField] private GameModeConfig _fullConfig;

    // ── Panels ───────────────────────────────────────────────────
    [Header("Panels")]
    [SerializeField] private GameObject _modeSelectPanel;
    [SerializeField] private GameObject _gamePanel;
    [SerializeField] private GameObject _saveSlotPanel;

    // ── Mode Select Buttons ──────────────────────────────────────
    [Header("Mode Select")]
    [SerializeField] private Button _demoButton;
    [SerializeField] private Button _showcaseButton;
    [SerializeField] private Button _fullButton;

    // ── Game Panel ───────────────────────────────────────────────
    [Header("Game Panel")]
    [SerializeField] private TMP_Text _modeNameLabel;
    [SerializeField] private TMP_Text _modeDescLabel;
    [SerializeField] private Button _newGameButton;
    [SerializeField] private Button _continueButton;
    [SerializeField] private Button _loadSaveButton;
    [SerializeField] private Button _gamePanelBackButton;
    [SerializeField] private Button _quitButton;

    // ── Save Slots ───────────────────────────────────────────────
    [Header("Save Slots")]
    [SerializeField] private Button _slot0Button;
    [SerializeField] private Button _slot1Button;
    [SerializeField] private Button _slot2Button;
    [SerializeField] private TMP_Text _slot0Label;
    [SerializeField] private TMP_Text _slot1Label;
    [SerializeField] private TMP_Text _slot2Label;
    [SerializeField] private Button _saveSlotBackButton;

    // ── Tutorial ─────────────────────────────────────────────────
    [Header("Tutorial")]
    [SerializeField] private TutorialCard _tutorialCard;

    // ── Runtime ──────────────────────────────────────────────────
    private MenuState _state;
    private GameModeConfig _selectedConfig;
    private bool _loading;
    private bool _quitConfirmShowing;
    private GameObject _quitConfirmPanel;
    private GameObject _musicVotePanel;
    private Transform _musicVoteContainer;
    private string _selectedMood;

    // ── Scene preloading ──────────────────────────────────────────
    private AsyncOperation _preloadOp;
    private int _targetSceneIndex;

    // ═══════════════════════════════════════════════════════════════
    // Lifecycle
    // ═══════════════════════════════════════════════════════════════

    // Input managed by IrisInput singleton — no local enable/disable needed.

    private void Start()
    {
        // Wire button listeners at runtime
        if (_demoButton != null) _demoButton.onClick.AddListener(OnDemoClicked);

        // 7 Days mode disabled — not yet implemented
        if (_showcaseButton != null)
        {
            _showcaseButton.interactable = false;
            var showcaseLabel = _showcaseButton.GetComponentInChildren<TMP_Text>();
            if (showcaseLabel != null) showcaseLabel.color = new Color(0.4f, 0.4f, 0.4f, 0.5f);
        }

        // Infinite mode disabled — not yet implemented
        if (_fullButton != null)
        {
            _fullButton.interactable = false;
            var fullLabel = _fullButton.GetComponentInChildren<TMP_Text>();
            if (fullLabel != null) fullLabel.color = new Color(0.4f, 0.4f, 0.4f, 0.5f);
        }

        if (_newGameButton != null) _newGameButton.onClick.AddListener(OnNewGame);
        if (_continueButton != null) _continueButton.onClick.AddListener(OnContinue);
        if (_loadSaveButton != null) _loadSaveButton.onClick.AddListener(OnLoadSave);
        if (_gamePanelBackButton != null) _gamePanelBackButton.onClick.AddListener(OnGamePanelBack);
        if (_quitButton != null) _quitButton.onClick.AddListener(QuitGame);

        if (_slot0Button != null) _slot0Button.onClick.AddListener(() => OnSlotClicked(0));
        if (_slot1Button != null) _slot1Button.onClick.AddListener(() => OnSlotClicked(1));
        if (_slot2Button != null) _slot2Button.onClick.AddListener(() => OnSlotClicked(2));
        if (_saveSlotBackButton != null) _saveSlotBackButton.onClick.AddListener(OnSaveSlotBack);

        BuildQuitConfirmPanel();
        BuildMusicVotePanel();

        // Skip mode select — go straight to demo game panel
        if (_demoConfig != null)
            SelectMode(_demoConfig);
        else
            ShowPanel(MenuState.ModeSelect);

        // Start silent — music plays when the player previews a mood

        // Fade in from black
        if (ScreenFade.Instance != null)
            ScreenFade.Instance.FadeIn(_fadeDuration);

        // Compute target scene index eagerly so ActivateLoad() always has the right value,
        // even if the preload coroutine hasn't finished yet.
        _targetSceneIndex = _apartmentSceneIndex >= 0
            ? _apartmentSceneIndex
            : SceneManager.GetActiveScene().buildIndex + 1;

        // Preload apartment scene in the background while player browses menu
        PreloadApartmentScene();
    }

    private void PreloadApartmentScene()
    {
        StartCoroutine(PreloadCoroutine());
    }

    private IEnumerator PreloadCoroutine()
    {
        // Wait a frame for the scene to fully settle
        yield return null;

        if (_targetSceneIndex < 0 || _targetSceneIndex >= SceneManager.sceneCountInBuildSettings)
        {
            Debug.LogWarning($"[MainMenuManager] Cannot preload scene index {_targetSceneIndex}.");
            yield break;
        }

        _preloadOp = SceneManager.LoadSceneAsync(_targetSceneIndex);
        if (_preloadOp != null)
        {
            _preloadOp.allowSceneActivation = false;
            Debug.Log($"[MainMenuManager] Preloading scene {_targetSceneIndex} in background.");
        }

    }

    private void Update()
    {
        bool pressed = (IrisInput.Instance != null && IrisInput.Instance.Pause.WasPressedThisFrame())
                    || Input.GetKeyDown(KeyCode.Escape);
        if (!pressed || _loading) return;

        if (_quitConfirmShowing)
        {
            HideQuitConfirm();
            return;
        }

        switch (_state)
        {
            case MenuState.ModeSelect:
                ShowQuitConfirm();
                break;
            case MenuState.GamePanel:
                OnGamePanelBack();
                break;
            case MenuState.SaveSlots:
                OnSaveSlotBack();
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Panel management
    // ═══════════════════════════════════════════════════════════════

    private void ShowPanel(MenuState state)
    {
        _state = state;
        if (_modeSelectPanel != null) _modeSelectPanel.SetActive(state == MenuState.ModeSelect);
        if (_gamePanel != null) _gamePanel.SetActive(state == MenuState.GamePanel);
        if (_saveSlotPanel != null) _saveSlotPanel.SetActive(state == MenuState.SaveSlots);
    }

    // ═══════════════════════════════════════════════════════════════
    // Mode Select
    // ═══════════════════════════════════════════════════════════════

    public void OnDemoClicked() => SelectMode(_demoConfig);
    public void OnShowcaseClicked() => SelectMode(_showcaseConfig);
    public void OnFullClicked() => SelectMode(_fullConfig);

    private void SelectMode(GameModeConfig config)
    {
        if (config == null) return;
        _selectedConfig = config;

        // Mode name/description hidden — these labels are no longer shown to the player
        if (_modeNameLabel != null) _modeNameLabel.gameObject.SetActive(false);
        if (_modeDescLabel != null) _modeDescLabel.gameObject.SetActive(false);

        // Continue button — visible only if a matching save exists
        int continueSlot = FindMostRecentSlot(config.modeName);
        if (_continueButton != null)
            _continueButton.gameObject.SetActive(continueSlot >= 0);

        ShowPanel(MenuState.GamePanel);
    }

    // ═══════════════════════════════════════════════════════════════
    // Game Panel
    // ═══════════════════════════════════════════════════════════════

    public void OnNewGame()
    {
        if (_loading) return;

        ActiveConfig = _selectedConfig;

        // Find first empty slot
        int slot = FindFirstEmptySlot();
        if (slot < 0) slot = 0; // overwrite slot 0 if all full
        SaveManager.ActiveSlot = slot;

        // Delete any existing save in this slot so NameEntryScreen starts fresh
        SaveManager.DeleteSlot(slot);

        // Clear all static registries to prevent stale state from previous games
        DateHistory.LoadFrom(null);
        ItemStateRegistry.Clear();
        PlayerData.PlayerName = "Nema";

        // Telemetry always on — no consent screen
        if (_tutorialCard != null)
            _tutorialCard.Show(() => LoadApartment());
        else
            LoadApartment();
    }

    public void OnContinue()
    {
        if (_loading || _selectedConfig == null) return;

        int slot = FindMostRecentSlot(_selectedConfig.modeName);
        if (slot < 0) return;

        ActiveConfig = _selectedConfig;
        SaveManager.ActiveSlot = slot;
        LoadApartment();
    }

    public void OnLoadSave()
    {
        RefreshSlotLabels();
        ShowPanel(MenuState.SaveSlots);
    }

    public void OnGamePanelBack()
    {
        ShowPanel(MenuState.ModeSelect);
    }

    public void QuitGame()
    {
        ShowQuitConfirm();
    }

    private void ShowQuitConfirm()
    {
        _quitConfirmShowing = true;
        if (_quitConfirmPanel != null)
            _quitConfirmPanel.SetActive(true);
    }

    private void HideQuitConfirm()
    {
        _quitConfirmShowing = false;
        if (_quitConfirmPanel != null)
            _quitConfirmPanel.SetActive(false);
    }

    private void DoQuitToDesktop()
    {
        Debug.Log("[MainMenuManager] Quitting application.");

        // Cancel any in-flight scene preload so it doesn't hold scene memory open
        if (_preloadOp != null)
        {
            _preloadOp.allowSceneActivation = true; // release so Unity can clean up
            _preloadOp = null;
        }

        GracefulQuit.Execute();
    }

    // ═══════════════════════════════════════════════════════════════
    // Save Slots
    // ═══════════════════════════════════════════════════════════════

    private void RefreshSlotLabels()
    {
        TMP_Text[] labels = { _slot0Label, _slot1Label, _slot2Label };
        for (int i = 0; i < SaveManager.SlotCount; i++)
        {
            if (labels[i] == null) continue;

            var data = SaveManager.PeekSlot(i);
            if (data != null)
            {
                string mode = string.IsNullOrEmpty(data.gameModeName) ? "Unknown" : data.gameModeName;
                labels[i].text = $"Slot {i + 1}: {mode} \u2014 Day {data.currentDay}";
            }
            else
            {
                labels[i].text = $"Slot {i + 1}: Empty";
            }
        }
    }

    public void OnSlotClicked(int slot)
    {
        if (_loading) return;
        if (!SaveManager.HasSave(slot)) return;

        ActiveConfig = _selectedConfig;
        SaveManager.ActiveSlot = slot;
        LoadApartment();
    }

    public void OnSaveSlotBack()
    {
        ShowPanel(MenuState.GamePanel);
    }

    // ═══════════════════════════════════════════════════════════════
    // Music Vote Panel (bottom-left overlay on game panel)
    // ═══════════════════════════════════════════════════════════════

    private readonly Color _cozyTint   = new Color(1f, 0.75f, 0.45f);
    private readonly Color _sadTint    = new Color(0.55f, 0.65f, 0.9f);
    private readonly Color _creepyTint = new Color(0.6f, 0.4f, 0.65f);
    private Image _cozyBtnImg, _sadBtnImg, _creepyBtnImg;

    private void BuildMusicVotePanel()
    {
        // Own overlay canvas so it works regardless of scene canvas hierarchy
        _musicVotePanel = new GameObject("MusicVoteCanvas");
        _musicVotePanel.transform.SetParent(transform, false);
        var canvas = _musicVotePanel.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        var scaler = _musicVotePanel.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        _musicVotePanel.AddComponent<GraphicRaycaster>();

        // Content container anchored to bottom-left
        var container = new GameObject("VoteContent");
        container.transform.SetParent(_musicVotePanel.transform, false);
        var panelRT = container.AddComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0f, 0f);
        panelRT.anchorMax = new Vector2(0f, 0f);
        panelRT.pivot = new Vector2(0f, 0f);
        panelRT.anchoredPosition = new Vector2(30f, 30f);
        panelRT.sizeDelta = new Vector2(220f, 180f);

        // Redirect all child creation to the container
        _musicVoteContainer = container.transform;

        var theme = IrisTextTheme.Active;

        // Question label
        var labelGO = new GameObject("VoteLabel");
        labelGO.transform.SetParent(_musicVoteContainer, false);
        var labelRT = labelGO.AddComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0f, 1f);
        labelRT.anchorMax = new Vector2(1f, 1f);
        labelRT.pivot = new Vector2(0.5f, 1f);
        labelRT.anchoredPosition = Vector2.zero;
        labelRT.sizeDelta = new Vector2(0f, 35f);
        var labelTMP = labelGO.AddComponent<TextMeshProUGUI>();
        labelTMP.text = "Menu music?";
        labelTMP.fontSize = 18f;
        labelTMP.fontStyle = FontStyles.Bold;
        labelTMP.alignment = TextAlignmentOptions.Left;
        labelTMP.color = new Color(0.85f, 0.82f, 0.78f);
        if (theme != null && theme.primaryFont != null) labelTMP.font = theme.primaryFont;

        // Three mood preview buttons stacked vertically
        _cozyBtnImg   = MakeVoteButton("Cozy",   new Vector2(0f, -40f),  _cozyTint);
        _sadBtnImg    = MakeVoteButton("Sad",    new Vector2(0f, -75f),  _sadTint);
        _creepyBtnImg = MakeVoteButton("Creepy", new Vector2(0f, -110f), _creepyTint);

        // Cast Vote button
        var voteGO = new GameObject("CastVoteBtn");
        voteGO.transform.SetParent(_musicVoteContainer, false);
        var voteRT = voteGO.AddComponent<RectTransform>();
        voteRT.anchorMin = new Vector2(0f, 1f);
        voteRT.anchorMax = new Vector2(1f, 1f);
        voteRT.pivot = new Vector2(0.5f, 1f);
        voteRT.anchoredPosition = new Vector2(0f, -148f);
        voteRT.sizeDelta = new Vector2(0f, 28f);

        var voteImg = voteGO.AddComponent<Image>();
        voteImg.color = new Color(0.15f, 0.15f, 0.18f, 0.8f);
        var voteBtn = voteGO.AddComponent<Button>();
        voteBtn.targetGraphic = voteImg;
        voteBtn.onClick.AddListener(CastMusicVote);

        var voteTxtGO = new GameObject("Label");
        voteTxtGO.transform.SetParent(voteGO.transform, false);
        var voteTxtRT = voteTxtGO.AddComponent<RectTransform>();
        voteTxtRT.anchorMin = Vector2.zero;
        voteTxtRT.anchorMax = Vector2.one;
        voteTxtRT.sizeDelta = Vector2.zero;
        var voteTMP = voteTxtGO.AddComponent<TextMeshProUGUI>();
        voteTMP.text = "Cast Vote";
        voteTMP.fontSize = 15f;
        voteTMP.alignment = TextAlignmentOptions.Center;
        voteTMP.color = new Color(0.7f, 0.85f, 0.7f);
        voteTMP.raycastTarget = false;
        if (theme != null && theme.primaryFont != null) voteTMP.font = theme.primaryFont;
    }

    private Image MakeVoteButton(string mood, Vector2 yOffset, Color tint)
    {
        var btnGO = new GameObject($"Vote_{mood}");
        btnGO.transform.SetParent(_musicVoteContainer, false);
        var btnRT = btnGO.AddComponent<RectTransform>();
        btnRT.anchorMin = new Vector2(0f, 1f);
        btnRT.anchorMax = new Vector2(1f, 1f);
        btnRT.pivot = new Vector2(0.5f, 1f);
        btnRT.anchoredPosition = yOffset;
        btnRT.sizeDelta = new Vector2(0f, 30f);

        var btnImg = btnGO.AddComponent<Image>();
        btnImg.color = new Color(tint.r * 0.2f, tint.g * 0.2f, tint.b * 0.2f, 0.7f);

        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        var colors = btn.colors;
        colors.highlightedColor = new Color(tint.r * 0.4f, tint.g * 0.4f, tint.b * 0.4f);
        colors.pressedColor = new Color(tint.r * 0.6f, tint.g * 0.6f, tint.b * 0.6f);
        btn.colors = colors;
        btn.onClick.AddListener(() => PreviewMood(mood));

        var txtGO = new GameObject("Label");
        txtGO.transform.SetParent(btnGO.transform, false);
        var txtRT = txtGO.AddComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.sizeDelta = Vector2.zero;
        var tmp = txtGO.AddComponent<TextMeshProUGUI>();
        tmp.text = mood;
        tmp.fontSize = 17f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = tint;
        tmp.raycastTarget = false;

        var theme = IrisTextTheme.Active;
        if (theme != null && theme.primaryFont != null) tmp.font = theme.primaryFont;

        return btnImg;
    }

    private void PreviewMood(string mood)
    {
        _selectedMood = mood;
        MusicDirector.Instance?.SetMood(mood);

        // Highlight the selected button, dim the others
        float sel = 0.5f, dim = 0.2f;
        _cozyBtnImg.color   = mood == "Cozy"   ? _cozyTint * sel   : new Color(_cozyTint.r * dim,   _cozyTint.g * dim,   _cozyTint.b * dim,   0.7f);
        _sadBtnImg.color    = mood == "Sad"    ? _sadTint * sel    : new Color(_sadTint.r * dim,    _sadTint.g * dim,    _sadTint.b * dim,    0.7f);
        _creepyBtnImg.color = mood == "Creepy" ? _creepyTint * sel : new Color(_creepyTint.r * dim, _creepyTint.g * dim, _creepyTint.b * dim, 0.7f);
    }

    private void CastMusicVote()
    {
        if (string.IsNullOrEmpty(_selectedMood))
        {
            Debug.Log("[MainMenuManager] No mood selected — pick one first.");
            return;
        }

        var config = DiscordWebhookConfig.Instance;
        string url = config != null ? config.FeedbackWebhookURL : null;

        StartCoroutine(DiscordWebhookService.PostEmbed(
            url,
            "Menu Music Vote",
            $"A player voted for **{_selectedMood}** menu music.",
            _selectedMood switch
            {
                "Cozy"   => 0xFFBF73, // warm orange
                "Sad"    => 0x8CA6E6, // soft blue
                "Creepy" => 0x9966A6, // purple
                _        => 0x888888
            },
            new (string, string, bool)[]
            {
                ("Mood", _selectedMood, true),
                ("Player", PlayerData.PlayerName ?? "Unknown", true)
            },
            "Menu Music Poll",
            null));

        // Visual feedback — swap button text to "Voted!"
        var voteLabel = _musicVoteContainer.Find("CastVoteBtn/Label");
        if (voteLabel != null)
        {
            var tmp = voteLabel.GetComponent<TextMeshProUGUI>();
            if (tmp != null) { tmp.text = "Voted!"; tmp.color = new Color(0.5f, 1f, 0.6f); }
        }

        Debug.Log($"[MainMenuManager] Music vote cast: {_selectedMood}");
    }

    // ═══════════════════════════════════════════════════════════════
    // Quit Confirm Panel (built at runtime)
    // ═══════════════════════════════════════════════════════════════

    private void BuildQuitConfirmPanel()
    {
        // Overlay canvas so it sits above everything
        _quitConfirmPanel = new GameObject("QuitConfirmCanvas");
        _quitConfirmPanel.transform.SetParent(transform, false);

        var canvas = _quitConfirmPanel.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = _quitConfirmPanel.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        _quitConfirmPanel.AddComponent<GraphicRaycaster>();

        // Dim bg
        var dimGO = new GameObject("Dim");
        dimGO.transform.SetParent(_quitConfirmPanel.transform, false);
        var dimRT = dimGO.AddComponent<RectTransform>();
        dimRT.anchorMin = Vector2.zero;
        dimRT.anchorMax = Vector2.one;
        dimRT.sizeDelta = Vector2.zero;
        var dimImg = dimGO.AddComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.6f);

        // Panel
        var panelGO = new GameObject("Panel");
        panelGO.transform.SetParent(_quitConfirmPanel.transform, false);
        var panelRT = panelGO.AddComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta = new Vector2(500f, 200f);
        var panelImg = panelGO.AddComponent<Image>();
        panelImg.color = new Color(0.12f, 0.12f, 0.14f, 1f);

        // Label
        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(panelGO.transform, false);
        var labelRT = labelGO.AddComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0f, 0.5f);
        labelRT.anchorMax = new Vector2(1f, 1f);
        labelRT.sizeDelta = Vector2.zero;
        labelRT.anchoredPosition = Vector2.zero;
        var labelTMP = labelGO.AddComponent<TextMeshProUGUI>();
        labelTMP.text = "Quit to desktop?";
        labelTMP.fontSize = 28f;
        labelTMP.alignment = TextAlignmentOptions.Center;
        labelTMP.color = new Color(0.95f, 0.92f, 0.85f);

        // Yes button
        MakeQuitConfirmButton(panelGO.transform, "Yes", new Vector2(-80f, -50f), DoQuitToDesktop);

        // No button
        MakeQuitConfirmButton(panelGO.transform, "No", new Vector2(80f, -50f), HideQuitConfirm);

        _quitConfirmPanel.SetActive(false);
    }

    private void MakeQuitConfirmButton(Transform parent, string label, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        var btnGO = new GameObject($"Btn_{label}");
        btnGO.transform.SetParent(parent, false);
        var btnRT = btnGO.AddComponent<RectTransform>();
        btnRT.anchorMin = new Vector2(0.5f, 0.5f);
        btnRT.anchorMax = new Vector2(0.5f, 0.5f);
        btnRT.anchoredPosition = pos;
        btnRT.sizeDelta = new Vector2(140f, 45f);

        var btnImg = btnGO.AddComponent<Image>();
        btnImg.color = new Color(0.22f, 0.22f, 0.26f);

        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        btn.onClick.AddListener(onClick);

        var txtGO = new GameObject("Label");
        txtGO.transform.SetParent(btnGO.transform, false);
        var txtRT = txtGO.AddComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.sizeDelta = Vector2.zero;
        var tmp = txtGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 22f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
    }

    // ═══════════════════════════════════════════════════════════════
    // Scene loading
    // ═══════════════════════════════════════════════════════════════

    private void LoadApartment()
    {
        if (_loading) return;
        _loading = true;

        if (ScreenFade.Instance != null && _fadeDuration > 0f)
            StartCoroutine(FadeAndLoad());
        else
            ActivateLoad();
    }

    private IEnumerator FadeAndLoad()
    {
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeOut(_fadeDuration);
        ActivateLoad();
    }

    private void ActivateLoad()
    {
        if (_preloadOp != null)
        {
            // Hand off the preloaded AsyncOperation to the loading screen
            LoadingScreen.LoadPreloaded(_preloadOp);
        }
        else
        {
            // Fallback: no preload available, load from scratch
            LoadingScreen.LoadScene(_targetSceneIndex);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Slot helpers
    // ═══════════════════════════════════════════════════════════════

    private static int FindFirstEmptySlot()
    {
        for (int i = 0; i < SaveManager.SlotCount; i++)
        {
            if (!SaveManager.HasSave(i)) return i;
        }
        return -1;
    }

    private static int FindMostRecentSlot(string modeName)
    {
        int bestSlot = -1;
        int bestDay = -1;

        for (int i = 0; i < SaveManager.SlotCount; i++)
        {
            var data = SaveManager.PeekSlot(i);
            if (data == null) continue;
            if (data.gameModeName != modeName) continue;

            if (data.currentDay > bestDay)
            {
                bestDay = data.currentDay;
                bestSlot = i;
            }
        }

        return bestSlot;
    }
}
