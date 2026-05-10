using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Runtime debug panel for the apartment scene. Toggle with F3.
/// Scrollable panel with sections: Grid, Highlight, Grab Feel, Atmosphere, Info.
/// </summary>
public class ApartmentDebugPanel : MonoBehaviour
{
    public static ApartmentDebugPanel Instance { get; private set; }

    private InputAction _toggleAction;
    private GameObject _panelGO;
    private bool _visible;

    private TMP_Text _gridLabel;
    private TMP_Text _infoText;

    // Highlight controls
    private TMP_Text _hlStyleLabel;
    private Slider _hlWidthSlider;
    private Slider _hlAlphaSlider;
    private Slider _hlPulseSlider;
    private Slider _hlRimSlider;
    private TMP_Text _hlWidthLabel;
    private TMP_Text _hlAlphaLabel;
    private TMP_Text _hlPulseLabel;
    private TMP_Text _hlRimLabel;

    // Grab feel controls
    private TMP_Text _grabFeelLabel;
    private TMP_Text _grabSpringLabel;
    private TMP_Text _grabDamperLabel;
    private TMP_Text _grabAccelLabel;
    private TMP_Text _grabSpeedLabel;
    private Slider _grabSpringSlider;
    private Slider _grabDamperSlider;
    private Slider _grabAccelSlider;
    private Slider _grabSpeedSlider;

    // Atmosphere controls
    private TMP_Text _atmSatLabel;
    private TMP_Text _atmContrastLabel;
    private TMP_Text _atmExposureLabel;
    private TMP_Text _atmBloomIntLabel;
    private TMP_Text _atmBloomThreshLabel;
    private TMP_Text _atmBloomScatterLabel;
    private TMP_Text _atmVignetteLabel;
    private TMP_Text _atmGrainLabel;
    private Slider _atmSatSlider;
    private Slider _atmContrastSlider;
    private Slider _atmExposureSlider;
    private Slider _atmBloomIntSlider;
    private Slider _atmBloomThreshSlider;
    private Slider _atmBloomScatterSlider;
    private Slider _atmVignetteSlider;
    private Slider _atmGrainSlider;

    // Time-of-day controls
    private Slider _todSlider;
    private TMP_Text _todLabel;

    private TMP_Text _saveStatusLabel;

    private const float FontSize = 16f;
    private const float PanelWidth = 360f;
    private const float RowHeight = 24f;
    private const string PrefPrefix = "DebugTweak_";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _toggleAction = new InputAction("ToggleDebugPanel", InputActionType.Button,
            "<Keyboard>/f3");
    }

    private void Start()
    {
        BuildPanel();
        _panelGO.SetActive(false);
        LoadSavedTweaks();
    }

    private void OnEnable() => _toggleAction?.Enable();
    private void OnDisable() => _toggleAction?.Disable();

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        _toggleAction?.Dispose();
    }

    private void Update()
    {
        if (_toggleAction != null && _toggleAction.WasPressedThisFrame())
        {
            _visible = !_visible;
            _panelGO.SetActive(_visible);

            // Suppress MoodMachine overrides while debug panel is open
            // so atmosphere sliders take effect directly
            if (AtmosphereController.Instance != null)
                AtmosphereController.Instance.SuppressMoodOverrides = _visible;

            if (_visible) SyncSlidersToSystems();
        }

        if (Input.GetKeyDown(KeyCode.F5))
        {
            // CycleStyle removed — ItemHighlight no longer has style cycling
        }

        if (Input.GetKeyDown(KeyCode.F6))
        {
            ObjectGrabber.CycleGrabFeel();
            RefreshGrabFeelLabel();
            SyncGrabSliders();
        }

        // [ / ] scrub time of day (shift = fine 15min steps, normal = 1hr)
        if (GameClock.Instance != null)
        {
            float step = Keyboard.current.leftShiftKey.isPressed ? 0.25f : 1f;
            if (Keyboard.current.leftBracketKey.wasPressedThisFrame)
                ScrubTime(-step);
            else if (Keyboard.current.rightBracketKey.wasPressedThisFrame)
                ScrubTime(step);
        }

        if (_visible)
            UpdateInfo();
    }

    private void ScrubTime(float delta)
    {
        var clock = GameClock.Instance;
        if (clock == null) return;

        // Auto-pause so the clock doesn't keep ticking after scrub
        DateDebugOverlay.IsTimePaused = true;

        float hour = Mathf.Clamp(clock.CurrentHour + delta, 0f, 30f);
        clock.SetHour(hour);

        // Sync slider if panel is open
        if (_todSlider != null)
            _todSlider.SetValueWithoutNotify(hour);
        UpdateTodLabel(hour);
    }

    /// <summary>Read current values from all systems and push to sliders.</summary>
    private void SyncSlidersToSystems()
    {
        SyncGrabSliders();
        SyncAtmosphereSliders();

        if (_todSlider != null && GameClock.Instance != null)
        {
            _todSlider.SetValueWithoutNotify(GameClock.Instance.CurrentHour);
            UpdateTodLabel(GameClock.Instance.CurrentHour);
        }
    }

    private void UpdateTodLabel(float hour)
    {
        if (_todLabel == null) return;
        float displayHour = Mathf.Repeat(hour, 24f);
        int h = Mathf.FloorToInt(displayHour);
        int m = Mathf.FloorToInt((displayHour - h) * 60f);
        string paused = DateDebugOverlay.IsTimePaused ? " (PAUSED)" : "";
        _todLabel.text = $"Time: {h:D2}:{m:D2}{paused}";
    }

    private void SyncGrabSliders()
    {
        RefreshGrabFeelLabel();
        ObjectGrabber.GetCurrentGrabParams(out float spring, out float damper, out float accel, out float speed);
        SyncSlider(_grabSpringSlider, _grabSpringLabel, spring, "Spring", "F0");
        SyncSlider(_grabDamperSlider, _grabDamperLabel, damper, "Damper", "F0");
        SyncSlider(_grabAccelSlider, _grabAccelLabel, accel, "Accel", "F0");
        SyncSlider(_grabSpeedSlider, _grabSpeedLabel, speed, "Speed", "F0");
    }

    private void SyncAtmosphereSliders()
    {
        var atm = AtmosphereController.Instance;
        if (atm == null) return;

        SyncSlider(_atmSatSlider, _atmSatLabel, atm.Saturation, "Saturation", "F0");
        SyncSlider(_atmContrastSlider, _atmContrastLabel, atm.Contrast, "Contrast", "F0");
        SyncSlider(_atmExposureSlider, _atmExposureLabel, atm.PostExposure, "Exposure", "F1");
        SyncSlider(_atmBloomIntSlider, _atmBloomIntLabel, atm.BloomIntensity, "Bloom", "F2");
        SyncSlider(_atmBloomThreshSlider, _atmBloomThreshLabel, atm.BloomThreshold, "Thresh", "F2");
        SyncSlider(_atmBloomScatterSlider, _atmBloomScatterLabel, atm.BloomScatter, "Scatter", "F2");
        SyncSlider(_atmVignetteSlider, _atmVignetteLabel, atm.VignetteIntensity, "Vignette", "F2");
        SyncSlider(_atmGrainSlider, _atmGrainLabel, atm.GrainIntensity, "Grain", "F2");
    }

    private static void SyncSlider(Slider slider, TMP_Text label, float value, string name, string fmt)
    {
        if (slider != null) slider.SetValueWithoutNotify(value);
        if (label != null) label.text = $"{name}: {value.ToString(fmt)}";
    }

    // ══════════════════════════════════════════════════════════════
    // Save / Load / Reset tweaks (PlayerPrefs)
    // ══════════════════════════════════════════════════════════════

    private static bool HasSavedTweaks() => PlayerPrefs.HasKey(PrefPrefix + "saved");

    private void SaveTweaks()
    {
        var atm = AtmosphereController.Instance;
        if (atm != null)
        {
            PlayerPrefs.SetFloat(PrefPrefix + "sat", atm.Saturation);
            PlayerPrefs.SetFloat(PrefPrefix + "con", atm.Contrast);
            PlayerPrefs.SetFloat(PrefPrefix + "exp", atm.PostExposure);
            PlayerPrefs.SetFloat(PrefPrefix + "bloomInt", atm.BloomIntensity);
            PlayerPrefs.SetFloat(PrefPrefix + "bloomThr", atm.BloomThreshold);
            PlayerPrefs.SetFloat(PrefPrefix + "bloomSca", atm.BloomScatter);
            PlayerPrefs.SetFloat(PrefPrefix + "vig", atm.VignetteIntensity);
            PlayerPrefs.SetFloat(PrefPrefix + "grain", atm.GrainIntensity);
        }

        ObjectGrabber.GetCurrentGrabParams(out float sp, out float da, out float ac, out float spd);
        PlayerPrefs.SetFloat(PrefPrefix + "gSpring", sp);
        PlayerPrefs.SetFloat(PrefPrefix + "gDamper", da);
        PlayerPrefs.SetFloat(PrefPrefix + "gAccel", ac);
        PlayerPrefs.SetFloat(PrefPrefix + "gSpeed", spd);

        var grabber = Object.FindAnyObjectByType<ObjectGrabber>();
        if (grabber != null)
            PlayerPrefs.SetFloat(PrefPrefix + "grid", grabber.GridSize);

        PlayerPrefs.SetInt(PrefPrefix + "saved", 1);
        PlayerPrefs.Save();

        if (_saveStatusLabel != null) _saveStatusLabel.text = "Tweaks saved!";
        Debug.Log("[ApartmentDebugPanel] Tweaks saved to PlayerPrefs.");
    }

    private void ClearSavedTweaks()
    {
        string[] keys = { "sat", "con", "exp", "bloomInt", "bloomThr", "bloomSca",
                          "vig", "grain", "gSpring", "gDamper", "gAccel", "gSpeed", "grid", "saved" };
        foreach (var k in keys) PlayerPrefs.DeleteKey(PrefPrefix + k);
        PlayerPrefs.Save();

        if (_saveStatusLabel != null) _saveStatusLabel.text = "Saved tweaks cleared";
        Debug.Log("[ApartmentDebugPanel] Saved tweaks cleared.");
    }

    /// <summary>Called from Start after panel build — applies saved tweaks if any.</summary>
    private void LoadSavedTweaks()
    {
        if (!HasSavedTweaks()) return;

        var atm = AtmosphereController.Instance;
        if (atm != null)
        {
            atm.Saturation = PlayerPrefs.GetFloat(PrefPrefix + "sat", atm.Saturation);
            atm.Contrast = PlayerPrefs.GetFloat(PrefPrefix + "con", atm.Contrast);
            atm.PostExposure = PlayerPrefs.GetFloat(PrefPrefix + "exp", atm.PostExposure);
            atm.BloomIntensity = PlayerPrefs.GetFloat(PrefPrefix + "bloomInt", atm.BloomIntensity);
            atm.BloomThreshold = PlayerPrefs.GetFloat(PrefPrefix + "bloomThr", atm.BloomThreshold);
            atm.BloomScatter = PlayerPrefs.GetFloat(PrefPrefix + "bloomSca", atm.BloomScatter);
            atm.VignetteIntensity = PlayerPrefs.GetFloat(PrefPrefix + "vig", atm.VignetteIntensity);
            atm.GrainIntensity = PlayerPrefs.GetFloat(PrefPrefix + "grain", atm.GrainIntensity);
        }

        if (PlayerPrefs.HasKey(PrefPrefix + "gSpring"))
        {
            ObjectGrabber.SetGrabOverrides(
                PlayerPrefs.GetFloat(PrefPrefix + "gSpring"),
                PlayerPrefs.GetFloat(PrefPrefix + "gDamper"),
                PlayerPrefs.GetFloat(PrefPrefix + "gAccel"),
                PlayerPrefs.GetFloat(PrefPrefix + "gSpeed")
            );
        }

        if (PlayerPrefs.HasKey(PrefPrefix + "grid"))
        {
            var grabber = Object.FindAnyObjectByType<ObjectGrabber>();
            if (grabber != null) grabber.GridSize = PlayerPrefs.GetFloat(PrefPrefix + "grid");
        }

        Debug.Log("[ApartmentDebugPanel] Loaded saved tweaks from PlayerPrefs.");
    }

    private void LogCurrentValues()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("═══ Debug Tweak Values ═══");

        var atm = AtmosphereController.Instance;
        if (atm != null)
        {
            sb.AppendLine($"Saturation: {atm.Saturation:F1}");
            sb.AppendLine($"Contrast: {atm.Contrast:F1}");
            sb.AppendLine($"PostExposure: {atm.PostExposure:F2}");
            sb.AppendLine($"BloomIntensity: {atm.BloomIntensity:F2}");
            sb.AppendLine($"BloomThreshold: {atm.BloomThreshold:F2}");
            sb.AppendLine($"BloomScatter: {atm.BloomScatter:F2}");
            sb.AppendLine($"VignetteIntensity: {atm.VignetteIntensity:F2}");
            sb.AppendLine($"GrainIntensity: {atm.GrainIntensity:F2}");
        }

        ObjectGrabber.GetCurrentGrabParams(out float sp, out float da, out float ac, out float spd);
        sb.AppendLine($"Grab: spring={sp:F0} damper={da:F0} accel={ac:F0} speed={spd:F0}");

        var grabber = Object.FindAnyObjectByType<ObjectGrabber>();
        if (grabber != null) sb.AppendLine($"GridSize: {grabber.GridSize:F2}");

        string text = sb.ToString();
        Debug.Log(text);
        GUIUtility.systemCopyBuffer = text;

        if (_saveStatusLabel != null) _saveStatusLabel.text = "Copied to clipboard + console";
    }

    private void UpdateInfo()
    {
        if (_infoText == null) return;

        var sb = new System.Text.StringBuilder();

        if (ObjectGrabber.IsHoldingObject)
            sb.AppendLine("Holding: " + ObjectGrabber.HeldObject.ItemDescription);

        if (TidyScorer.Instance != null)
            sb.AppendLine($"Tidiness: {TidyScorer.Instance.OverallTidiness:P0}");

        if (GameClock.Instance != null)
        {
            float h = GameClock.Instance.CurrentHour;
            int hours = Mathf.FloorToInt(Mathf.Repeat(h, 24f));
            int mins = Mathf.FloorToInt((Mathf.Repeat(h, 24f) - hours) * 60f);
            sb.AppendLine($"Day {GameClock.Instance.CurrentDay}  {hours:D2}:{mins:D2}");
        }

        if (DayPhaseManager.Instance != null)
            sb.AppendLine($"Phase: {DayPhaseManager.Instance.CurrentPhase}");

        if (MoodMachine.Instance != null)
            sb.AppendLine($"Mood: {MoodMachine.Instance.Mood:F2}");

        var atm = AtmosphereController.Instance;
        if (atm != null)
            sb.AppendLine($"Atmo: ON (sat={atm.Saturation:F0} exp={atm.PostExposure:F1})");

        if (PSXRenderController.Instance != null && PSXRenderController.Instance.enabled)
            sb.AppendLine($"PSX: ON (snap={PSXRenderController.Instance.VertexSnapResolution.x:F0})");

        _infoText.text = sb.ToString();
    }

    // ── Highlight tuning ──

    private void RefreshHighlightStyleLabel()
    {
        // Style cycling removed — ItemHighlight no longer has CurrentStyle
    }

    private void OnHighlightParamChanged()
    {
        float width = _hlWidthSlider != null ? _hlWidthSlider.value : 0.008f;
        float alpha = _hlAlphaSlider != null ? _hlAlphaSlider.value : 0.25f;
        float pulse = _hlPulseSlider != null ? _hlPulseSlider.value : 0.1f;
        float rim = _hlRimSlider != null ? _hlRimSlider.value : 2.5f;

        if (_hlWidthLabel != null) _hlWidthLabel.text = $"Width: {width:F3}";
        if (_hlAlphaLabel != null) _hlAlphaLabel.text = $"Alpha: {alpha:F2}";
        if (_hlPulseLabel != null) _hlPulseLabel.text = $"Pulse: {pulse:F2}";
        if (_hlRimLabel != null) _hlRimLabel.text = $"Rim: {rim:F1}";
    }

    // ── Grab feel tuning ──

    private void RefreshGrabFeelLabel()
    {
        if (_grabFeelLabel != null)
            _grabFeelLabel.text = $"Preset: {ObjectGrabber.CurrentFeel} (F6)";
    }

    private void OnGrabParamChanged()
    {
        float spring = _grabSpringSlider != null ? _grabSpringSlider.value : 35f;
        float damper = _grabDamperSlider != null ? _grabDamperSlider.value : 6f;
        float accel = _grabAccelSlider != null ? _grabAccelSlider.value : 20f;
        float speed = _grabSpeedSlider != null ? _grabSpeedSlider.value : 5f;

        if (_grabSpringLabel != null) _grabSpringLabel.text = $"Spring: {spring:F0}";
        if (_grabDamperLabel != null) _grabDamperLabel.text = $"Damper: {damper:F0}";
        if (_grabAccelLabel != null) _grabAccelLabel.text = $"Accel: {accel:F0}";
        if (_grabSpeedLabel != null) _grabSpeedLabel.text = $"Speed: {speed:F0}";

        ObjectGrabber.SetGrabOverrides(spring, damper, accel, speed);
    }

    // ══════════════════════════════════════════════════════════════
    // Panel construction — scrollable, sectioned
    // ══════════════════════════════════════════════════════════════

    private void BuildPanel()
    {
        // Canvas
        var canvasGO = new GameObject("ApartmentDebugCanvas");
        canvasGO.transform.SetParent(transform);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasGO.AddComponent<GraphicRaycaster>();

        // Panel (right side, full height)
        _panelGO = new GameObject("DebugPanel");
        _panelGO.transform.SetParent(canvasGO.transform, false);
        var panelRT = _panelGO.AddComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(1f, 0f);
        panelRT.anchorMax = new Vector2(1f, 1f);
        panelRT.pivot = new Vector2(1f, 0.5f);
        panelRT.anchoredPosition = new Vector2(-8f, 0f);
        panelRT.sizeDelta = new Vector2(PanelWidth, -16f);

        var panelImg = _panelGO.AddComponent<Image>();
        panelImg.color = new Color(0.05f, 0.05f, 0.05f, 0.9f);
        panelImg.raycastTarget = true;

        // ScrollRect
        var scrollGO = new GameObject("Scroll");
        scrollGO.transform.SetParent(_panelGO.transform, false);
        var scrollRT = scrollGO.AddComponent<RectTransform>();
        scrollRT.anchorMin = Vector2.zero;
        scrollRT.anchorMax = Vector2.one;
        scrollRT.offsetMin = new Vector2(4f, 4f);
        scrollRT.offsetMax = new Vector2(-4f, -4f);

        var scroll = scrollGO.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;

        // Clip mask (RectMask2D avoids stencil issues with TMP)
        scrollGO.AddComponent<RectMask2D>();

        // Content (vertical layout)
        var contentGO = new GameObject("Content");
        contentGO.transform.SetParent(scrollGO.transform, false);
        var contentRT = contentGO.AddComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0f, 1f);
        contentRT.anchorMax = new Vector2(1f, 1f);
        contentRT.pivot = new Vector2(0.5f, 1f);
        contentRT.anchoredPosition = Vector2.zero;
        contentRT.sizeDelta = new Vector2(0f, 0f);

        var contentLayout = contentGO.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(8, 8, 4, 4);
        contentLayout.spacing = 1f;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;

        var contentFitter = contentGO.AddComponent<ContentSizeFitter>();
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.content = contentRT;
        scroll.viewport = scrollRT;

        var cp = contentGO.transform;

        // ═══════════════════════════════════════
        //  SECTIONS
        // ═══════════════════════════════════════

        AddSectionHeader(cp, "DEBUG PANEL (F3)", new Color(1f, 1f, 1f));

        // ── Grid ──
        var grabber = Object.FindAnyObjectByType<ObjectGrabber>();
        float initGrid = grabber != null ? grabber.GridSize : 0.11f;
        AddSliderRow(cp, "Grid Size", 0.05f, 1.0f, initGrid,
            val =>
            {
                var g = Object.FindAnyObjectByType<ObjectGrabber>();
                if (g != null) g.GridSize = val;
                if (_gridLabel != null) _gridLabel.text = $"Grid: {val:F2}m";
            }, out _gridLabel);

        // ── Highlight ──
        AddSectionHeader(cp, "HIGHLIGHT", new Color(1f, 0.9f, 0.7f));
        _hlStyleLabel = AddLabel(cp, "Style: (F5)");

        AddSliderRow(cp, "Width", 0.001f, 0.05f, 0.008f, _ => OnHighlightParamChanged(), out _hlWidthLabel);
        _hlWidthSlider = GetLastSlider(cp);
        AddSliderRow(cp, "Alpha", 0.05f, 1.0f, 0.25f, _ => OnHighlightParamChanged(), out _hlAlphaLabel);
        _hlAlphaSlider = GetLastSlider(cp);
        AddSliderRow(cp, "Pulse", 0f, 0.5f, 0.1f, _ => OnHighlightParamChanged(), out _hlPulseLabel);
        _hlPulseSlider = GetLastSlider(cp);
        AddSliderRow(cp, "Rim Power", 0.5f, 8.0f, 2.5f, _ => OnHighlightParamChanged(), out _hlRimLabel);
        _hlRimSlider = GetLastSlider(cp);

        // ── Grab Feel ──
        AddSectionHeader(cp, "GRAB FEEL", new Color(0.9f, 0.75f, 1f));
        _grabFeelLabel = AddLabel(cp, $"Preset: {ObjectGrabber.CurrentFeel} (F6)");

        AddSliderRow(cp, "Spring", 5f, 500f, 35f, _ => OnGrabParamChanged(), out _grabSpringLabel);
        _grabSpringSlider = GetLastSlider(cp);
        AddSliderRow(cp, "Damper", 1f, 50f, 6f, _ => OnGrabParamChanged(), out _grabDamperLabel);
        _grabDamperSlider = GetLastSlider(cp);
        AddSliderRow(cp, "Accel", 5f, 200f, 20f, _ => OnGrabParamChanged(), out _grabAccelLabel);
        _grabAccelSlider = GetLastSlider(cp);
        AddSliderRow(cp, "Speed", 1f, 30f, 5f, _ => OnGrabParamChanged(), out _grabSpeedLabel);
        _grabSpeedSlider = GetLastSlider(cp);

        // ── Atmosphere ──
        AddSectionHeader(cp, "ATMOSPHERE", new Color(0.7f, 0.85f, 1f));

        var atm = AtmosphereController.Instance;
        float sat = atm != null ? atm.Saturation : -25f;
        float con = atm != null ? atm.Contrast : 12f;
        float exp = atm != null ? atm.PostExposure : 0.3f;
        float bInt = atm != null ? atm.BloomIntensity : 0.6f;
        float bTh = atm != null ? atm.BloomThreshold : 0.7f;
        float bSc = atm != null ? atm.BloomScatter : 0.75f;
        float vig = atm != null ? atm.VignetteIntensity : 0.3f;
        float grn = atm != null ? atm.GrainIntensity : 0.15f;

        AddSliderRow(cp, "Saturation", -80f, 20f, sat,
            val => { if (AtmosphereController.Instance != null) AtmosphereController.Instance.Saturation = val;
                     if (_atmSatLabel != null) _atmSatLabel.text = $"Saturation: {val:F0}"; },
            out _atmSatLabel);
        _atmSatSlider = GetLastSlider(cp);
        AddSliderRow(cp, "Contrast", -50f, 50f, con,
            val => { if (AtmosphereController.Instance != null) AtmosphereController.Instance.Contrast = val;
                     if (_atmContrastLabel != null) _atmContrastLabel.text = $"Contrast: {val:F0}"; },
            out _atmContrastLabel);
        _atmContrastSlider = GetLastSlider(cp);
        AddSliderRow(cp, "Exposure", -2f, 3f, exp,
            val => { if (AtmosphereController.Instance != null) AtmosphereController.Instance.PostExposure = val;
                     if (_atmExposureLabel != null) _atmExposureLabel.text = $"Exposure: {val:F1}"; },
            out _atmExposureLabel);
        _atmExposureSlider = GetLastSlider(cp);
        AddSliderRow(cp, "Bloom Int", 0f, 3f, bInt,
            val => { if (AtmosphereController.Instance != null) AtmosphereController.Instance.BloomIntensity = val;
                     if (_atmBloomIntLabel != null) _atmBloomIntLabel.text = $"Bloom: {val:F2}"; },
            out _atmBloomIntLabel);
        _atmBloomIntSlider = GetLastSlider(cp);
        AddSliderRow(cp, "Bloom Thresh", 0f, 2f, bTh,
            val => { if (AtmosphereController.Instance != null) AtmosphereController.Instance.BloomThreshold = val;
                     if (_atmBloomThreshLabel != null) _atmBloomThreshLabel.text = $"Thresh: {val:F2}"; },
            out _atmBloomThreshLabel);
        _atmBloomThreshSlider = GetLastSlider(cp);
        AddSliderRow(cp, "Bloom Scatter", 0f, 1f, bSc,
            val => { if (AtmosphereController.Instance != null) AtmosphereController.Instance.BloomScatter = val;
                     if (_atmBloomScatterLabel != null) _atmBloomScatterLabel.text = $"Scatter: {val:F2}"; },
            out _atmBloomScatterLabel);
        _atmBloomScatterSlider = GetLastSlider(cp);
        AddSliderRow(cp, "Vignette", 0f, 1f, vig,
            val => { if (AtmosphereController.Instance != null) AtmosphereController.Instance.VignetteIntensity = val;
                     if (_atmVignetteLabel != null) _atmVignetteLabel.text = $"Vignette: {val:F2}"; },
            out _atmVignetteLabel);
        _atmVignetteSlider = GetLastSlider(cp);
        AddSliderRow(cp, "Film Grain", 0f, 1f, grn,
            val => { if (AtmosphereController.Instance != null) AtmosphereController.Instance.GrainIntensity = val;
                     if (_atmGrainLabel != null) _atmGrainLabel.text = $"Grain: {val:F2}"; },
            out _atmGrainLabel);
        _atmGrainSlider = GetLastSlider(cp);

        // ── PSX ──
        AddSectionHeader(cp, "PSX (F4 toggle)", new Color(0.95f, 0.7f, 0.85f));
        var psx = PSXRenderController.Instance;

        AddSliderRow(cp, "Res Divisor", 1f, 6f, psx != null ? psx.ResolutionDivisor : 3f,
            val => { if (PSXRenderController.Instance != null) PSXRenderController.Instance.ResolutionDivisor = val; },
            out _);
        AddSliderRow(cp, "Color Depth", 4f, 256f, psx != null ? psx.ColorDepth : 32f,
            val => { if (PSXRenderController.Instance != null) PSXRenderController.Instance.ColorDepth = val; },
            out _);
        AddSliderRow(cp, "Vertex Snap X", 16f, 640f, psx != null ? psx.VertexSnapResolution.x : 160f,
            val => { if (PSXRenderController.Instance != null)
                PSXRenderController.Instance.VertexSnapResolution = new Vector2(val, val * 0.75f); },
            out _);
        AddSliderRow(cp, "Affine Warp", 0f, 1f, psx != null ? psx.AffineIntensity : 1f,
            val => { if (PSXRenderController.Instance != null) PSXRenderController.Instance.AffineIntensity = val; },
            out _);
        AddSliderRow(cp, "Dither", 0f, 1f, psx != null ? psx.DitherIntensity : 0.5f,
            val => { if (PSXRenderController.Instance != null) PSXRenderController.Instance.DitherIntensity = val; },
            out _);
        AddSliderRow(cp, "Dither Shadow Bias", 0f, 1f, psx != null ? psx.DitherShadowBias : 0.7f,
            val => { if (PSXRenderController.Instance != null) PSXRenderController.Instance.DitherShadowBias = val; },
            out _);
        AddSliderRow(cp, "Shadow Dither", 0f, 1f, psx != null ? psx.ShadowDitherIntensity : 1f,
            val => { if (PSXRenderController.Instance != null) PSXRenderController.Instance.ShadowDitherIntensity = val; },
            out _);
        AddSliderRow(cp, "Tilt-Shift", 0f, 1f, psx != null ? psx.TiltShiftAmount : 0f,
            val => { if (PSXRenderController.Instance != null) PSXRenderController.Instance.TiltShiftAmount = val; },
            out _);
        AddSliderRow(cp, "Tilt Center", 0f, 1f, psx != null ? psx.TiltShiftCenter : 0.5f,
            val => { if (PSXRenderController.Instance != null) PSXRenderController.Instance.TiltShiftCenter = val; },
            out _);
        AddSliderRow(cp, "Tilt Width", 0.01f, 0.5f, psx != null ? psx.TiltShiftWidth : 0.15f,
            val => { if (PSXRenderController.Instance != null) PSXRenderController.Instance.TiltShiftWidth = val; },
            out _);
        AddSliderRow(cp, "Tilt Radius", 1f, 20f, psx != null ? psx.TiltShiftRadius : 8f,
            val => { if (PSXRenderController.Instance != null) PSXRenderController.Instance.TiltShiftRadius = val; },
            out _);

        // ── Time of Day ──
        AddSectionHeader(cp, "TIME OF DAY  [ / ]", new Color(1f, 0.95f, 0.6f));
        float initHour = GameClock.Instance != null ? GameClock.Instance.CurrentHour : 12f;
        AddSliderRow(cp, "Hour", 0f, 30f, initHour,
            val =>
            {
                if (GameClock.Instance == null) return;
                DateDebugOverlay.IsTimePaused = true;
                GameClock.Instance.SetHour(val);
                UpdateTodLabel(val);
            }, out _todLabel);
        _todSlider = GetLastSlider(cp);
        UpdateTodLabel(initHour);

        // ── Info readout ──
        AddSectionHeader(cp, "STATUS", new Color(0.8f, 0.9f, 0.8f));
        _infoText = AddLabel(cp, "");
        _infoText.color = new Color(0.8f, 0.9f, 0.8f);

        // ── Save / Load / Reset ──
        AddSectionHeader(cp, "PRESETS", new Color(1f, 0.85f, 0.5f));
        _saveStatusLabel = AddLabel(cp, HasSavedTweaks() ? "Saved tweaks loaded" : "No saved tweaks");
        _saveStatusLabel.color = new Color(0.7f, 0.7f, 0.7f);
        _saveStatusLabel.fontSize = FontSize - 2f;

        AddButtonRow(cp, "Save Tweaks", new Color(0.3f, 0.55f, 0.35f), SaveTweaks);
        AddButtonRow(cp, "Clear Saved", new Color(0.55f, 0.3f, 0.3f), ClearSavedTweaks);
        AddButtonRow(cp, "Copy to Log", new Color(0.35f, 0.4f, 0.55f), LogCurrentValues);
    }

    // ══════════════════════════════════════════════════════════════
    // UI Helpers
    // ══════════════════════════════════════════════════════════════

    private Slider GetLastSlider(Transform parent)
    {
        var sliders = parent.GetComponentsInChildren<Slider>();
        return sliders.Length > 0 ? sliders[sliders.Length - 1] : null;
    }

    private TMP_Text AddLabel(Transform parent, string text)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = RowHeight;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = FontSize;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.raycastTarget = false;
        return tmp;
    }

    private void AddSectionHeader(Transform parent, string text, Color color)
    {
        // Small spacer
        var spacer = new GameObject("Spacer");
        spacer.transform.SetParent(parent, false);
        spacer.AddComponent<RectTransform>();
        spacer.AddComponent<LayoutElement>().preferredHeight = 6f;

        // Header bar
        var go = new GameObject("SectionHeader");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = RowHeight + 2f;

        var img = go.AddComponent<Image>();
        img.color = new Color(color.r * 0.15f, color.g * 0.15f, color.b * 0.15f, 0.8f);
        img.raycastTarget = false;

        // Text inside header
        var textGO = new GameObject("Text");
        textGO.transform.SetParent(go.transform, false);
        var textRT = textGO.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(8f, 0f);
        textRT.offsetMax = Vector2.zero;

        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = FontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.raycastTarget = false;
    }

    private void AddSliderRow(Transform parent, string label, float min, float max,
        float initial, System.Action<float> onChange, out TMP_Text valueLabel)
    {
        var rowGO = new GameObject("SliderRow");
        rowGO.transform.SetParent(parent, false);
        rowGO.AddComponent<RectTransform>();
        rowGO.AddComponent<LayoutElement>().preferredHeight = RowHeight + 6f;

        var rowLayout = rowGO.AddComponent<VerticalLayoutGroup>();
        rowLayout.spacing = 0f;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;

        // Value label
        var labelGO = new GameObject("ValueLabel");
        labelGO.transform.SetParent(rowGO.transform, false);
        labelGO.AddComponent<RectTransform>();
        labelGO.AddComponent<LayoutElement>().preferredHeight = RowHeight - 4f;
        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text = $"{label}: {initial:F2}";
        tmp.fontSize = FontSize - 2f;
        tmp.color = new Color(0.85f, 0.85f, 0.85f);
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.raycastTarget = false;
        valueLabel = tmp;

        // Slider
        var sliderGO = new GameObject("Slider");
        sliderGO.transform.SetParent(rowGO.transform, false);
        sliderGO.AddComponent<RectTransform>();
        sliderGO.AddComponent<LayoutElement>().preferredHeight = 12f;

        // BG
        var bgGO = new GameObject("BG");
        bgGO.transform.SetParent(sliderGO.transform, false);
        var bgRT = bgGO.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
        bgGO.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f);

        // Fill area
        var fillAreaGO = new GameObject("FillArea");
        fillAreaGO.transform.SetParent(sliderGO.transform, false);
        var fillAreaRT = fillAreaGO.AddComponent<RectTransform>();
        fillAreaRT.anchorMin = Vector2.zero; fillAreaRT.anchorMax = Vector2.one;
        fillAreaRT.offsetMin = Vector2.zero; fillAreaRT.offsetMax = Vector2.zero;

        var fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(fillAreaGO.transform, false);
        var fillRT = fillGO.AddComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = new Vector2(0f, 1f);
        fillRT.offsetMin = Vector2.zero; fillRT.offsetMax = Vector2.zero;
        fillGO.AddComponent<Image>().color = new Color(0.3f, 0.5f, 0.65f);

        // Handle
        var handleAreaGO = new GameObject("HandleArea");
        handleAreaGO.transform.SetParent(sliderGO.transform, false);
        var handleAreaRT = handleAreaGO.AddComponent<RectTransform>();
        handleAreaRT.anchorMin = Vector2.zero; handleAreaRT.anchorMax = Vector2.one;
        handleAreaRT.offsetMin = Vector2.zero; handleAreaRT.offsetMax = Vector2.zero;

        var handleGO = new GameObject("Handle");
        handleGO.transform.SetParent(handleAreaGO.transform, false);
        var handleRT = handleGO.AddComponent<RectTransform>();
        handleRT.sizeDelta = new Vector2(10f, 0f);
        var handleImg = handleGO.AddComponent<Image>();
        handleImg.color = Color.white;

        var slider = sliderGO.AddComponent<Slider>();
        slider.fillRect = fillRT;
        slider.handleRect = handleRT;
        slider.targetGraphic = handleImg;
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = initial;
        slider.onValueChanged.AddListener(val => onChange?.Invoke(val));
    }

    private void AddButtonRow(Transform parent, string label, Color bgColor, System.Action onClick)
    {
        var go = new GameObject("Button");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        go.AddComponent<LayoutElement>().preferredHeight = RowHeight + 4f;

        var img = go.AddComponent<Image>();
        img.color = bgColor;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick?.Invoke());

        var textGO = new GameObject("Text");
        textGO.transform.SetParent(go.transform, false);
        var textRT = textGO.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = FontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
    }
}
