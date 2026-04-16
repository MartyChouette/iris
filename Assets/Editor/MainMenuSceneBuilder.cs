using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

/// <summary>
/// Editor utility that builds the main menu scene matching L_Main_Menu visual structure:
/// Nema parallax head (6 sprite layers + separate background with BacklightPulse),
/// MusicPlayer, Bloom Volume, title with TMP_FocusBlur, TextDissolveButton hover effects.
///
/// Adds our game-specific panels on top:
/// ModeSelect (Demo/Showcase/Full) → GamePanel (New/Continue/Load/Back/Quit) → SaveSlots (3 slots).
/// Menu: Window > Iris > Build Main Menu Scene
/// </summary>
public static class MainMenuSceneBuilder
{
    private const string SoDir = "Assets/ScriptableObjects/GameModes";

    // ── Layout constants (matching L_Main_Menu) ──────────────────
    private static readonly Vector2 ButtonSize = new Vector2(400f, 100f);
    private static readonly float ButtonX = 680f;

    // Mode select buttons (right side, stacked — matching L_Main_Menu Y spacing)
    private static readonly float ModeBtn0Y = -140f;
    private static readonly float ModeBtn1Y = -240f;
    private static readonly float ModeBtn2Y = -340f;

    // Game panel buttons
    private static readonly float GameBtn0Y = -80f;   // New Game
    private static readonly float GameBtn1Y = -180f;   // Continue
    private static readonly float GameBtn2Y = -280f;   // Load Save
    private static readonly float GameBtn3Y = -380f;   // Back
    private static readonly float GameBtn4Y = -460f;   // Quit

    // Save slot buttons (centered)
    private static readonly float SlotBtnX = 0f;
    private static readonly float SlotBtn0Y = 100f;
    private static readonly float SlotBtn1Y = 0f;
    private static readonly float SlotBtn2Y = -100f;
    private static readonly float SlotBackY = -220f;

    // Title layout (left side — matching L_Main_Menu)
    private static readonly Vector2 TitlePos = new Vector2(-600f, 340f);
    private static readonly Vector2 TitleSize = new Vector2(500f, 300f);

    // Game panel labels (right side, above buttons)
    private static readonly Vector2 ModeNamePos = new Vector2(680f, 120f);
    private static readonly Vector2 ModeDescPos = new Vector2(680f, 50f);

    // Nema head (matching L_Main_Menu)
    private static readonly Vector3 NemaHeadPos = new Vector3(-1.02f, 1.28f, 0f);
    private static readonly Vector3 NemaHeadScale = new Vector3(0.75f, 0.75f, 0.75f);

    // Background (separate root GO, matching L_Main_Menu)
    private static readonly Vector3 BackgroundPos = new Vector3(0f, 1.28f, 0f);
    private static readonly Vector3 BackgroundScale = new Vector3(2f, 2f, 2f);

    [MenuItem("Window/Iris/Build Main Menu Scene")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ── 1. Camera (matching L_Main_Menu position) ────────────────
        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<UnityEngine.Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.192f, 0.302f, 0.475f, 0f);
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 1000f;
        camGO.transform.position = new Vector3(0f, 1f, -10f);
        camGO.AddComponent<AudioListener>();

        // ── 2. Directional Light (warm white, matching L_Main_Menu) ──
        var lightGO = new GameObject("Directional Light");
        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.957f, 0.839f);
        light.intensity = 1f;
        lightGO.transform.position = new Vector3(0f, 3f, 0f);
        lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // ── 3. EventSystem ───────────────────────────────────────────
        var eventSysGO = new GameObject("EventSystem");
        eventSysGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
        eventSysGO.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

        // ── 4. Game Mode SOs ─────────────────────────────────────────
        var demoConfig = EnsureGameModeConfig("Mode_7Minutes", "7 Minutes",
            "Real-time 7-minute countdown. A quick taste of Iris.",
            totalDays: 1, demoTimeLimitSeconds: 420f, realSecondsPerGameHour: 60f, prepDuration: 60f);

        var showcaseConfig = EnsureGameModeConfig("Mode_7Days", "7 Days",
            "7 in-game days. The full dating loop at normal pace.",
            totalDays: 7, demoTimeLimitSeconds: 0f, realSecondsPerGameHour: 60f, prepDuration: 120f);

        var fullConfig = EnsureGameModeConfig("Mode_Infinite", "Infinite",
            "No day limit. The complete Iris experience.",
            totalDays: 0, demoTimeLimitSeconds: 0f, realSecondsPerGameHour: 60f, prepDuration: 120f);

        // ── 5. Nema Head (6 sprite layers, matching L_Main_Menu) ─────
        var nemaHead = new GameObject("Nema_Head");
        nemaHead.transform.position = NemaHeadPos;
        nemaHead.transform.localScale = NemaHeadScale;
        var jitter = nemaHead.AddComponent<MotionJitter>();
        jitter.inputMode = MotionJitter.InputMode.MousePosition;
        jitter.noiseFrequency = 2f;
        jitter.positionStrength = 0.03f;
        jitter.rotationStrength = 0.05f;
        jitter.scaleStrength = 0.01f;

        // Sprite children — auto-load from Assets/ArtAssets/Nema_Main/
        string[] layerNames = { "1", "2_bangs", "2_face", "3_face", "4_neck", "5_hair" };
        int[] sortOrders = { 3, 2, 1, 0, -1, -2 };
        string[] spriteFiles =
        {
            "Assets/ArtAssets/Nema_Main/2025-12-10 203418.png",  // 1 (front hair)
            "Assets/ArtAssets/Nema_Main/2025-12-10 203334.png",  // 2_bangs
            "Assets/ArtAssets/Nema_Main/2025-12-10 203400.png",  // 2_face (detail)
            "Assets/ArtAssets/Nema_Main/2025-12-10 203346.png",  // 3_face (base)
            "Assets/ArtAssets/Nema_Main/2025-12-10 203409.png",  // 4_neck
            "Assets/ArtAssets/Nema_Main/2025-12-10 203321.png",  // 5_hair (back)
        };
        var layerTransforms = new Transform[layerNames.Length];
        for (int i = 0; i < layerNames.Length; i++)
        {
            var layerGO = new GameObject(layerNames[i]);
            layerGO.transform.SetParent(nemaHead.transform, false);
            var sr = layerGO.AddComponent<SpriteRenderer>();
            sr.sortingOrder = sortOrders[i];
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spriteFiles[i]);
            if (sprite != null)
                sr.sprite = sprite;
            else
                Debug.LogWarning($"[MainMenuSceneBuilder] Sprite not found: {spriteFiles[i]}");
            layerTransforms[i] = layerGO.transform;
        }

        // ── 6. Background (separate root GO with BacklightPulse) ─────
        var bgGO = new GameObject("6_background");
        bgGO.transform.position = BackgroundPos;
        bgGO.transform.localScale = BackgroundScale;
        var bgSR = bgGO.AddComponent<SpriteRenderer>();
        bgSR.sortingOrder = -3;
        var bgSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/ArtAssets/Nema_Main/2025-12-10 203427.png");
        if (bgSprite != null)
            bgSR.sprite = bgSprite;
        else
            Debug.LogWarning("[MainMenuSceneBuilder] Background sprite not found.");
        var pulse = bgGO.AddComponent<BacklightPulse>();
        pulse.dimColor = new Color(1.149f, 2.742f, 3.291f, 1f);
        pulse.brightColor = new Color(2.920f, 6.716f, 8.041f, 1f);
        pulse.pulseSpeed = 0.4f;

        // ── 7. ParallaxManager (8 layers matching L_Main_Menu) ───────
        var parallaxGO = new GameObject("ParallaxManager");
        var parallax = parallaxGO.AddComponent<MouseParallax>();
        parallax.smoothing = 5f;
        parallax.maxOffset = 20f;

        // Nema sprite layers
        float[] moveSpeeds = { -0.1f, -0.15f, -0.2f, -0.3f, -0.35f, -0.6f };
        for (int i = 0; i < layerTransforms.Length; i++)
        {
            parallax.layers.Add(new MouseParallax.ParallaxLayer
            {
                layerObject = layerTransforms[i],
                moveSpeed = moveSpeeds[i]
            });
        }

        // Background layer (stationary)
        parallax.layers.Add(new MouseParallax.ParallaxLayer
        {
            layerObject = bgGO.transform,
            moveSpeed = 0f
        });

        // ── 8. MusicPlayer (AudioManager + AudioSource) ─────────────
        var musicGO = new GameObject("MusicPlayer");
        musicGO.AddComponent<AudioManager>();

        var musicSourceGO = new GameObject("Music_Sound_Source");
        musicSourceGO.transform.SetParent(musicGO.transform, false);
        var audioSource = musicSourceGO.AddComponent<AudioSource>();
        audioSource.loop = true;
        audioSource.playOnAwake = false;
        audioSource.volume = 0.5f;

        // ── 8b. MusicDirector (persistent cross-scene music manager) ──
        var directorGO = new GameObject("MusicDirector");
        directorGO.AddComponent<MusicDirector>();
        // _menuSong clip must be assigned in inspector after build
        // MusicDirector.PlayMenuSong() is called by MainMenuManager.Start()

        // ── 9. Bloom Volume (URP) ────────────────────────────────────
        var bloomGO = new GameObject("Bloom");
        bloomGO.transform.position = new Vector3(1.08f, -0.99f, -0.05f);
        var volume = bloomGO.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 1f;
        // VolumeProfile must be assigned by hand or loaded from existing asset

        // ── 10. Canvas ───────────────────────────────────────────────
        var canvasGO = new GameObject("Canvas");
        canvasGO.layer = LayerMask.NameToLayer("UI");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 0;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasGO.AddComponent<GraphicRaycaster>();

        // Title — "Iris" on left with TMP_FocusBlur (matching L_Main_Menu)
        var titleGO = CreateLabel("Title_Text (TMP)", canvasGO.transform,
            TitlePos, TitleSize,
            "Iris", 300f, FontStyles.Normal, TextAlignmentOptions.Center,
            Color.white);
        var focusBlur = titleGO.AddComponent<TMP_FocusBlur>();
        focusBlur.morphSpeed = 3f;
        focusBlur.jitterIntensity = 4f;
        focusBlur.noiseScale = 10f;
        focusBlur.preserveCharacterShape = false;

        // Add title to parallax (extreme movement, matching L_Main_Menu layer 0)
        parallax.layers.Insert(0, new MouseParallax.ParallaxLayer
        {
            layerObject = titleGO.transform,
            moveSpeed = -200f
        });

        // ── 11. ModeSelectPanel ──────────────────────────────────────
        var modePanel = CreatePanel("ModeSelectPanel", canvasGO.transform);

        var demoBtn = CreateMenuButton("Demo_Button", modePanel.transform,
            new Vector2(ButtonX, ModeBtn0Y), ButtonSize, "7 Minutes", 2f, addFocusBlur: true);

        var showcaseBtn = CreateMenuButton("Showcase_Button", modePanel.transform,
            new Vector2(ButtonX, ModeBtn1Y), ButtonSize, "7 Days", 2f);

        var fullBtn = CreateMenuButton("Full_Button", modePanel.transform,
            new Vector2(ButtonX, ModeBtn2Y), ButtonSize, "Infinite", 2f);

        // ── 12. GamePanel (starts hidden) ────────────────────────────
        var gamePanel = CreatePanel("GamePanel", canvasGO.transform);
        gamePanel.SetActive(false);

        // Mode name + description labels
        var modeNameLabel = CreateLabel("ModeNameLabel", gamePanel.transform,
            ModeNamePos, new Vector2(400f, 60f),
            "", 52f, FontStyles.Bold, TextAlignmentOptions.Center, Color.white);

        var modeDescLabel = CreateLabel("ModeDescLabel", gamePanel.transform,
            ModeDescPos, new Vector2(400f, 40f),
            "", 20f, FontStyles.Italic, TextAlignmentOptions.Center,
            new Color(0.8f, 0.8f, 0.8f));

        var newGameBtn = CreateMenuButton("NewGame_Button", gamePanel.transform,
            new Vector2(ButtonX, GameBtn0Y), ButtonSize, "New Game", 2f, addFocusBlur: true);

        var continueBtn = CreateMenuButton("Continue_Button", gamePanel.transform,
            new Vector2(ButtonX, GameBtn1Y), ButtonSize, "Continue", 2f);

        var loadSaveBtn = CreateMenuButton("LoadSave_Button", gamePanel.transform,
            new Vector2(ButtonX, GameBtn2Y), ButtonSize, "Load Save", 2f);

        var backBtn = CreateMenuButton("Back_Button", gamePanel.transform,
            new Vector2(ButtonX, GameBtn3Y), ButtonSize, "Back", 5f);

        var quitBtn = CreateMenuButton("Quit_Button", gamePanel.transform,
            new Vector2(ButtonX, GameBtn4Y), ButtonSize, "Quit", 5f);

        // ── 13. SaveSlotPanel (starts hidden) ────────────────────────
        var saveSlotPanel = CreatePanel("SaveSlotPanel", canvasGO.transform);
        saveSlotPanel.SetActive(false);

        var slot0Btn = CreateSlotButton("Slot_0_Button", saveSlotPanel.transform,
            new Vector2(SlotBtnX, SlotBtn0Y), out var slot0Label);

        var slot1Btn = CreateSlotButton("Slot_1_Button", saveSlotPanel.transform,
            new Vector2(SlotBtnX, SlotBtn1Y), out var slot1Label);

        var slot2Btn = CreateSlotButton("Slot_2_Button", saveSlotPanel.transform,
            new Vector2(SlotBtnX, SlotBtn2Y), out var slot2Label);

        var slotBackBtn = CreateMenuButton("SlotBack_Button", saveSlotPanel.transform,
            new Vector2(SlotBtnX, SlotBackY), new Vector2(300f, 80f), "Back", 5f);

        // ── 14. Tutorial Card ────────────────────────────────────────
        var tutorialCard = BuildTutorialCard();

        // ── 15. ScreenFade overlay ───────────────────────────────────
        BuildScreenFade();

        // ── 16. MainMenuManager ──────────────────────────────────────
        var menuMgrGO = new GameObject("GameMainMenuManager");
        var menuMgr = menuMgrGO.AddComponent<MainMenuManager>();
        var mgrSO = new SerializedObject(menuMgr);

        // Configs
        mgrSO.FindProperty("_demoConfig").objectReferenceValue = demoConfig;
        mgrSO.FindProperty("_showcaseConfig").objectReferenceValue = showcaseConfig;
        mgrSO.FindProperty("_fullConfig").objectReferenceValue = fullConfig;

        // Panels
        mgrSO.FindProperty("_modeSelectPanel").objectReferenceValue = modePanel;
        mgrSO.FindProperty("_gamePanel").objectReferenceValue = gamePanel;
        mgrSO.FindProperty("_saveSlotPanel").objectReferenceValue = saveSlotPanel;

        // Mode select buttons
        mgrSO.FindProperty("_demoButton").objectReferenceValue = demoBtn;
        mgrSO.FindProperty("_showcaseButton").objectReferenceValue = showcaseBtn;
        mgrSO.FindProperty("_fullButton").objectReferenceValue = fullBtn;

        // Game panel
        mgrSO.FindProperty("_modeNameLabel").objectReferenceValue = modeNameLabel.GetComponent<TMP_Text>();
        mgrSO.FindProperty("_modeDescLabel").objectReferenceValue = modeDescLabel.GetComponent<TMP_Text>();
        mgrSO.FindProperty("_newGameButton").objectReferenceValue = newGameBtn;
        mgrSO.FindProperty("_continueButton").objectReferenceValue = continueBtn;
        mgrSO.FindProperty("_loadSaveButton").objectReferenceValue = loadSaveBtn;
        mgrSO.FindProperty("_gamePanelBackButton").objectReferenceValue = backBtn;
        mgrSO.FindProperty("_quitButton").objectReferenceValue = quitBtn;

        // Save slots
        mgrSO.FindProperty("_slot0Button").objectReferenceValue = slot0Btn;
        mgrSO.FindProperty("_slot1Button").objectReferenceValue = slot1Btn;
        mgrSO.FindProperty("_slot2Button").objectReferenceValue = slot2Btn;
        mgrSO.FindProperty("_slot0Label").objectReferenceValue = slot0Label;
        mgrSO.FindProperty("_slot1Label").objectReferenceValue = slot1Label;
        mgrSO.FindProperty("_slot2Label").objectReferenceValue = slot2Label;
        mgrSO.FindProperty("_saveSlotBackButton").objectReferenceValue = slotBackBtn;

        // Tutorial
        mgrSO.FindProperty("_tutorialCard").objectReferenceValue = tutorialCard;

        mgrSO.ApplyModifiedPropertiesWithoutUndo();

        // ── 17. Text theme applier ──────────────────────────────────
        var themeGO = new GameObject("IrisTextThemeApplier");
        themeGO.AddComponent<IrisTextThemeApplier>();

        // ── 18. Save scene ───────────────────────────────────────────
        string dir = "Assets/Scenes";
        if (!AssetDatabase.IsValidFolder(dir))
            AssetDatabase.CreateFolder("Assets", "Scenes");

        string path = $"{dir}/mainmenu.unity";
        EditorSceneManager.SaveScene(scene, path);
        Debug.Log($"[MainMenuSceneBuilder] Scene saved to {path}");
    }

    // ═══════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════

    private static GameModeConfig EnsureGameModeConfig(string assetName, string modeName,
        string description, int totalDays, float demoTimeLimitSeconds,
        float realSecondsPerGameHour, float prepDuration)
    {
        if (!AssetDatabase.IsValidFolder("Assets/ScriptableObjects"))
            AssetDatabase.CreateFolder("Assets", "ScriptableObjects");
        if (!AssetDatabase.IsValidFolder(SoDir))
            AssetDatabase.CreateFolder("Assets/ScriptableObjects", "GameModes");

        string path = $"{SoDir}/{assetName}.asset";
        var config = AssetDatabase.LoadAssetAtPath<GameModeConfig>(path);

        if (config == null)
        {
            config = ScriptableObject.CreateInstance<GameModeConfig>();
            config.modeName = modeName;
            config.modeDescription = description;
            config.totalDays = totalDays;
            config.demoTimeLimitSeconds = demoTimeLimitSeconds;
            config.realSecondsPerGameHour = realSecondsPerGameHour;
            config.prepDuration = prepDuration;
            AssetDatabase.CreateAsset(config, path);
            Debug.Log($"[MainMenuSceneBuilder] Created GameModeConfig: {path}");
        }

        return config;
    }

    private static GameObject CreatePanel(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.layer = LayerMask.NameToLayer("UI");

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        return go;
    }

    /// <summary>
    /// Creates a text-only menu button matching L_Main_Menu style:
    /// disabled Image (transparent), white TMP text at 70pt Bold,
    /// TextDissolveButton hover effect, optional TMP_FocusBlur on text.
    /// </summary>
    private static Button CreateMenuButton(string name, Transform parent,
        Vector2 anchoredPos, Vector2 size, string label, float dissolveSpeed,
        bool addFocusBlur = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.layer = LayerMask.NameToLayer("UI");

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        rt.localScale = Vector3.one;

        // Fully transparent Image — keeps raycast target for Button click detection
        var img = go.AddComponent<Image>();
        img.color = Color.clear;

        var btn = go.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.96f, 0.96f, 0.96f, 1f);
        colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
        colors.selectedColor = new Color(0.96f, 0.96f, 0.96f, 1f);
        colors.fadeDuration = 0.1f;
        btn.colors = colors;

        // TextDissolveButton effect (matching L_Main_Menu values)
        var dissolve = go.AddComponent<TextDissolveButton>();
        dissolve.animationSpeed = dissolveSpeed;
        dissolve.normalSoftness = 0f;
        dissolve.blurredSoftness = 1f;
        dissolve.normalDilate = 0f;
        dissolve.dissolvedDilate = -1f;
        dissolve.normalScale = 1f;
        dissolve.blurredScale = 1.2f;
        dissolve.normalAlpha = 1f;
        dissolve.blurredAlpha = 0.4f;

        // Child TMP text (70pt Bold matching L_Main_Menu)
        var textGO = new GameObject($"{name}_Text (TMP)");
        textGO.transform.SetParent(go.transform, false);
        textGO.layer = LayerMask.NameToLayer("UI");
        var textRT = textGO.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;
        textRT.localScale = Vector3.one;

        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 70f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;

        // Optional TMP_FocusBlur on text (for primary action buttons)
        if (addFocusBlur)
        {
            var fb = textGO.AddComponent<TMP_FocusBlur>();
            fb.morphSpeed = 3f;
            fb.jitterIntensity = 4f;
            fb.noiseScale = 10f;
            fb.preserveCharacterShape = false;
        }

        return btn;
    }

    private static Button CreateSlotButton(string name, Transform parent,
        Vector2 anchoredPos, out TMP_Text labelText)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.layer = LayerMask.NameToLayer("UI");

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(500f, 80f);
        rt.localScale = Vector3.one;

        // Subtle background for slot buttons
        var img = go.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.05f);

        var btn = go.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = new Color(1f, 1f, 1f, 0.05f);
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.15f);
        colors.pressedColor = new Color(1f, 1f, 1f, 0.25f);
        colors.selectedColor = new Color(1f, 1f, 1f, 0.1f);
        colors.fadeDuration = 0.1f;
        btn.colors = colors;

        // Label child
        var labelGO = new GameObject($"{name}_Label");
        labelGO.transform.SetParent(go.transform, false);
        labelGO.layer = LayerMask.NameToLayer("UI");
        var labelRT = labelGO.AddComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = new Vector2(20f, 0f);
        labelRT.offsetMax = new Vector2(-20f, 0f);
        labelRT.localScale = Vector3.one;

        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text = "Empty";
        tmp.fontSize = 28f;
        tmp.fontStyle = FontStyles.Normal;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.color = new Color(0.85f, 0.85f, 0.82f);
        tmp.raycastTarget = false;

        labelText = tmp;
        return btn;
    }

    private static GameObject CreateLabel(string name, Transform parent,
        Vector2 anchoredPos, Vector2 size, string text,
        float fontSize, FontStyles style, TextAlignmentOptions alignment, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.layer = LayerMask.NameToLayer("UI");

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        rt.localScale = Vector3.one;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = alignment;
        tmp.color = color;
        tmp.raycastTarget = false;

        return go;
    }

    private static TutorialCard BuildTutorialCard()
    {
        var cardCanvasGO = new GameObject("TutorialCard");
        var cardCanvas = cardCanvasGO.AddComponent<Canvas>();
        cardCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        cardCanvas.sortingOrder = 50;

        var cardScaler = cardCanvasGO.AddComponent<CanvasScaler>();
        cardScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cardScaler.referenceResolution = new Vector2(1920f, 1080f);
        cardCanvasGO.AddComponent<GraphicRaycaster>();

        // Backdrop
        var backdrop = new GameObject("Backdrop");
        backdrop.transform.SetParent(cardCanvasGO.transform, false);
        var bdRT = backdrop.AddComponent<RectTransform>();
        bdRT.anchorMin = Vector2.zero;
        bdRT.anchorMax = Vector2.one;
        bdRT.offsetMin = Vector2.zero;
        bdRT.offsetMax = Vector2.zero;
        var bdImg = backdrop.AddComponent<Image>();
        bdImg.color = new Color(0f, 0f, 0f, 0.7f);
        bdImg.raycastTarget = true;

        // Card shadow — slightly larger dark panel behind the card for depth
        var shadow = new GameObject("CardShadow");
        shadow.transform.SetParent(cardCanvasGO.transform, false);
        var shadowRT = shadow.AddComponent<RectTransform>();
        shadowRT.anchorMin = new Vector2(0.5f, 0.5f);
        shadowRT.anchorMax = new Vector2(0.5f, 0.5f);
        shadowRT.sizeDelta = new Vector2(920f, 770f);
        shadowRT.anchoredPosition = new Vector2(4f, -4f);
        var shadowImg = shadow.AddComponent<Image>();
        shadowImg.color = new Color(0f, 0f, 0f, 0.4f);
        shadowImg.raycastTarget = false;

        // Card panel — warm dark parchment
        var panel = new GameObject("CardPanel");
        panel.transform.SetParent(cardCanvasGO.transform, false);
        var panelRT = panel.AddComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta = new Vector2(900f, 750f);
        panelRT.anchoredPosition = Vector2.zero;
        var panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0.12f, 0.10f, 0.09f, 0.97f); // dark warm brown

        // Inner border accent
        var border = new GameObject("Border");
        border.transform.SetParent(panel.transform, false);
        var borderRT = border.AddComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero;
        borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = new Vector2(10f, 10f);
        borderRT.offsetMax = new Vector2(-10f, -10f);
        var borderImg = border.AddComponent<Image>();
        borderImg.color = new Color(0.75f, 0.55f, 0.35f, 0.12f); // faint gold border
        borderImg.raycastTarget = false;

        // Title
        var titleGO = new GameObject("Title");
        titleGO.transform.SetParent(panel.transform, false);
        var titleRT = titleGO.AddComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0.5f, 1f);
        titleRT.anchorMax = new Vector2(0.5f, 1f);
        titleRT.anchoredPosition = new Vector2(0f, -55f);
        titleRT.sizeDelta = new Vector2(800f, 60f);
        var titleTMP = titleGO.AddComponent<TextMeshProUGUI>();
        titleTMP.text = "HOW TO PLAY";
        titleTMP.fontSize = 36f;
        titleTMP.fontStyle = FontStyles.Bold;
        titleTMP.alignment = TextAlignmentOptions.Center;
        titleTMP.color = new Color(0.92f, 0.82f, 0.65f); // warm gold
        titleTMP.characterSpacing = 8f;
        titleTMP.raycastTarget = false;

        // Divider line under title
        var divider = new GameObject("Divider");
        divider.transform.SetParent(panel.transform, false);
        var divRT = divider.AddComponent<RectTransform>();
        divRT.anchorMin = new Vector2(0.5f, 1f);
        divRT.anchorMax = new Vector2(0.5f, 1f);
        divRT.anchoredPosition = new Vector2(0f, -90f);
        divRT.sizeDelta = new Vector2(500f, 1f);
        var divImg = divider.AddComponent<Image>();
        divImg.color = new Color(0.75f, 0.55f, 0.35f, 0.3f);
        divImg.raycastTarget = false;

        // Controls text — two columns feel, left-aligned
        var controlsGO = new GameObject("Controls");
        controlsGO.transform.SetParent(panel.transform, false);
        var ctrlRT = controlsGO.AddComponent<RectTransform>();
        ctrlRT.anchorMin = new Vector2(0.5f, 0.5f);
        ctrlRT.anchorMax = new Vector2(0.5f, 0.5f);
        ctrlRT.anchoredPosition = new Vector2(0f, 30f);
        ctrlRT.sizeDelta = new Vector2(780f, 420f);
        var ctrlTMP = controlsGO.AddComponent<TextMeshProUGUI>();
        ctrlTMP.text =
            "<color=#D4A574>[Click / Arrows]</color>     Browse rooms\n" +
            "<color=#D4A574>[Click]</color>                       Interact\n" +
            "<color=#D4A574>[Click + Hold]</color>           Pour\n" +
            "<color=#D4A574>[RMB]</color>                        Cancel\n" +
            "<color=#D4A574>[Esc]</color>                          Pause\n" +
            "<color=#D4A574>[Scroll]</color>                      Rotate held items\n" +
            "<color=#D4A574>[MMB]</color>                        Show labels\n" +
            "\n" +
            "<color=#E8D5B7>Your Day</color>\n" +
            "Read the paper. Pick a date.\n" +
            "Prep your apartment. Impress your guest.\n" +
            "If things go well, you may trim their flower.";
        ctrlTMP.fontSize = 28f;
        ctrlTMP.fontStyle = FontStyles.Normal;
        ctrlTMP.alignment = TextAlignmentOptions.Left;
        ctrlTMP.color = new Color(0.82f, 0.78f, 0.72f); // warm cream text
        ctrlTMP.raycastTarget = false;
        ctrlTMP.richText = true;
        ctrlTMP.lineSpacing = 8f;

        // Bottom divider line (mirrors top divider to frame content)
        var divider2 = new GameObject("DividerBottom");
        divider2.transform.SetParent(panel.transform, false);
        var div2RT = divider2.AddComponent<RectTransform>();
        div2RT.anchorMin = new Vector2(0.5f, 0f);
        div2RT.anchorMax = new Vector2(0.5f, 0f);
        div2RT.anchoredPosition = new Vector2(0f, 100f);
        div2RT.sizeDelta = new Vector2(500f, 1f);
        var div2Img = divider2.AddComponent<Image>();
        div2Img.color = new Color(0.75f, 0.55f, 0.35f, 0.2f);
        div2Img.raycastTarget = false;

        // Start button — warm accent
        var startBtnGO = new GameObject("StartButton");
        startBtnGO.transform.SetParent(panel.transform, false);
        var sBtnRT = startBtnGO.AddComponent<RectTransform>();
        sBtnRT.anchorMin = new Vector2(0.5f, 0f);
        sBtnRT.anchorMax = new Vector2(0.5f, 0f);
        sBtnRT.anchoredPosition = new Vector2(0f, 55f);
        sBtnRT.sizeDelta = new Vector2(220f, 52f);
        var sBtnImg = startBtnGO.AddComponent<Image>();
        sBtnImg.color = new Color(0.65f, 0.42f, 0.25f); // warm brown button

        var sBtn = startBtnGO.AddComponent<Button>();
        var sBtnColors = sBtn.colors;
        sBtnColors.normalColor = new Color(0.65f, 0.42f, 0.25f);
        sBtnColors.highlightedColor = new Color(0.80f, 0.55f, 0.35f);
        sBtnColors.pressedColor = new Color(0.50f, 0.32f, 0.18f);
        sBtnColors.selectedColor = new Color(0.70f, 0.48f, 0.30f);
        sBtn.colors = sBtnColors;

        var sBtnLabelGO = new GameObject("Label");
        sBtnLabelGO.transform.SetParent(startBtnGO.transform, false);
        var sBtnLabelRT = sBtnLabelGO.AddComponent<RectTransform>();
        sBtnLabelRT.anchorMin = Vector2.zero;
        sBtnLabelRT.anchorMax = Vector2.one;
        sBtnLabelRT.offsetMin = Vector2.zero;
        sBtnLabelRT.offsetMax = Vector2.zero;
        var sBtnTMP = sBtnLabelGO.AddComponent<TextMeshProUGUI>();
        sBtnTMP.text = "BEGIN";
        sBtnTMP.fontSize = 26f;
        sBtnTMP.fontStyle = FontStyles.Bold;
        sBtnTMP.alignment = TextAlignmentOptions.Center;
        sBtnTMP.color = new Color(0.95f, 0.92f, 0.85f); // cream label
        sBtnTMP.characterSpacing = 6f;
        sBtnTMP.raycastTarget = false;

        // Card starts hidden
        cardCanvasGO.SetActive(false);

        // Wire TutorialCard component
        var tc = cardCanvasGO.AddComponent<TutorialCard>();
        var tcSO = new SerializedObject(tc);
        tcSO.FindProperty("_root").objectReferenceValue = cardCanvasGO;
        tcSO.FindProperty("_startButton").objectReferenceValue = sBtn;
        tcSO.ApplyModifiedPropertiesWithoutUndo();

        return tc;
    }

    private static void BuildScreenFade()
    {
        var go = new GameObject("ScreenFade");

        var fadeCanvas = go.AddComponent<Canvas>();
        fadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        fadeCanvas.sortingOrder = 100;

        var blackPanel = new GameObject("BlackPanel");
        blackPanel.transform.SetParent(go.transform, false);
        var rt = blackPanel.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var img = blackPanel.AddComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = true;

        var cg = go.AddComponent<CanvasGroup>();
        cg.alpha = 1f;
        cg.blocksRaycasts = true;

        var fade = go.AddComponent<ScreenFade>();
        var fadeSO = new SerializedObject(fade);
        fadeSO.FindProperty("_canvasGroup").objectReferenceValue = cg;
        fadeSO.FindProperty("defaultFadeOutDuration").floatValue = 0.5f;
        fadeSO.FindProperty("defaultFadeInDuration").floatValue = 0.5f;
        fadeSO.ApplyModifiedPropertiesWithoutUndo();
    }
}
