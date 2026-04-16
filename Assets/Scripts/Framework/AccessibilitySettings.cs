using UnityEngine;

/// <summary>
/// Central accessibility and settings hub for Iris.
/// Static utility (no MonoBehaviour). All values backed by PlayerPrefs.
/// Systems subscribe to OnSettingsChanged for live updates.
/// </summary>
public static class AccessibilitySettings
{
    // ── Colorblind ──────────────────────────────────────────────
    public enum ColorblindMode
    {
        Normal,
        Deuteranopia,
        Protanopia,
        Tritanopia
    }

    // ── PlayerPrefs keys ────────────────────────────────────────
    private const string K_ColorblindMode  = "Iris_ColorblindMode";
    private const string K_HighContrast    = "Iris_HighContrast";
    private const string K_TextScale       = "Iris_TextScale";
    private const string K_ReduceMotion    = "Iris_ReduceMotion";
    private const string K_ScreenShake     = "Iris_ScreenShakeScale";
    private const string K_MasterVolume    = "Iris_MasterVolume";
    private const string K_MusicVolume     = "Iris_MusicVolume";
    private const string K_SFXVolume       = "Iris_SFXVolume";
    private const string K_AmbienceVolume  = "Iris_AmbienceVolume";
    private const string K_UIVolume        = "Iris_UIVolume";
    private const string K_CaptionsEnabled = "Iris_CaptionsEnabled";
    private const string K_TimerMultiplier = "Iris_TimerMultiplier";
    private const string K_ResolutionScale = "Iris_ResolutionScale";
    private const string K_QualityPreset   = "Iris_QualityPreset";
    private const string K_PSXEnabled      = "Iris_PSXEnabled";
    private const string K_InvertScroll    = "Iris_InvertScroll";

    // ── Event ───────────────────────────────────────────────────
    public static event System.Action OnSettingsChanged;

    // ── Batch support ───────────────────────────────────────────
    private static int s_batchDepth;

    /// <summary>Suppress OnSettingsChanged until EndChanges is called. Nestable.</summary>
    public static void BeginChanges() => s_batchDepth++;

    /// <summary>End a batch. Fires OnSettingsChanged when the outermost batch closes.</summary>
    public static void EndChanges()
    {
        s_batchDepth = Mathf.Max(0, s_batchDepth - 1);
        if (s_batchDepth == 0)
            NotifyChanged();
    }

    private static void NotifyChanged()
    {
        if (s_batchDepth > 0) return;
        OnSettingsChanged?.Invoke();
    }

    // ── Backing fields (cached from PlayerPrefs) ────────────────
    private static ColorblindMode s_colorblindMode;
    private static bool  s_highContrast;
    private static float s_textScale;
    private static bool  s_reduceMotion;
    private static float s_screenShakeScale;
    private static float s_masterVolume;
    private static float s_musicVolume;
    private static float s_sfxVolume;
    private static float s_ambienceVolume;
    private static float s_uiVolume;
    private static bool  s_captionsEnabled;
    private static float s_timerMultiplier;
    private static float s_resolutionScale;
    private static int   s_qualityPreset;
    private static bool  s_psxEnabled;
    private static bool  s_invertScroll;

    // ── Color palettes per colorblind mode ──────────────────────
    private static readonly Color[] s_happyColors =
    {
        new Color(0.3f, 1f, 0.4f, 1f),     // Normal: green
        new Color(0.2f, 0.5f, 1f, 1f),     // Deuteranopia: blue
        new Color(0.2f, 0.5f, 1f, 1f),     // Protanopia: blue
        new Color(1f, 0.3f, 0.85f, 1f)     // Tritanopia: magenta
    };

    private static readonly Color[] s_sadColors =
    {
        new Color(1f, 0.3f, 0.25f, 1f),    // Normal: red
        new Color(1f, 0.6f, 0.1f, 1f),     // Deuteranopia: orange
        new Color(1f, 0.7f, 0.2f, 1f),     // Protanopia: yellow-orange
        new Color(0f, 0.85f, 0.9f, 1f)     // Tritanopia: cyan
    };

    // ── Init / Domain Reload ────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void DomainReset()
    {
        s_batchDepth = 0;
        OnSettingsChanged = null;
        LoadAllDefaults();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        LoadAll();
    }

    private static void LoadAllDefaults()
    {
        s_colorblindMode  = ColorblindMode.Normal;
        s_highContrast    = false;
        s_textScale       = 1f;
        s_reduceMotion    = false;
        s_screenShakeScale = 1f;
        s_masterVolume    = 1f;
        s_musicVolume     = 1f;
        s_sfxVolume       = 1f;
        s_ambienceVolume  = 1f;
        s_uiVolume        = 1f;
        s_captionsEnabled = false;
        s_timerMultiplier = 1f;
        s_resolutionScale = 1f;
        s_qualityPreset   = -1;
        s_psxEnabled      = true;
        s_invertScroll    = false;
    }

    private static void LoadAll()
    {
        s_colorblindMode  = (ColorblindMode)PlayerPrefs.GetInt(K_ColorblindMode, 0);
        s_highContrast    = PlayerPrefs.GetInt(K_HighContrast, 0) == 1;
        s_textScale       = PlayerPrefs.GetFloat(K_TextScale, 1f);
        s_reduceMotion    = PlayerPrefs.GetInt(K_ReduceMotion, 0) == 1;
        s_screenShakeScale = PlayerPrefs.GetFloat(K_ScreenShake, 1f);
        s_masterVolume    = PlayerPrefs.GetFloat(K_MasterVolume, 1f);
        s_musicVolume     = PlayerPrefs.GetFloat(K_MusicVolume, 1f);
        s_sfxVolume       = PlayerPrefs.GetFloat(K_SFXVolume, 1f);
        s_ambienceVolume  = PlayerPrefs.GetFloat(K_AmbienceVolume, 1f);
        s_uiVolume        = PlayerPrefs.GetFloat(K_UIVolume, 1f);
        s_captionsEnabled = PlayerPrefs.GetInt(K_CaptionsEnabled, 0) == 1;
        s_timerMultiplier = PlayerPrefs.GetFloat(K_TimerMultiplier, 1f);
        s_resolutionScale = PlayerPrefs.GetFloat(K_ResolutionScale, 1f);
        s_qualityPreset   = PlayerPrefs.GetInt(K_QualityPreset, -1);
        s_psxEnabled      = PlayerPrefs.GetInt(K_PSXEnabled, 1) == 1;
        s_invertScroll    = PlayerPrefs.GetInt(K_InvertScroll, 0) == 1;

        // Clamp loaded values
        s_textScale       = Mathf.Clamp(s_textScale, 0.8f, 1.5f);
        s_screenShakeScale = Mathf.Clamp01(s_screenShakeScale);
        s_masterVolume    = Mathf.Clamp01(s_masterVolume);
        s_musicVolume     = Mathf.Clamp01(s_musicVolume);
        s_sfxVolume       = Mathf.Clamp01(s_sfxVolume);
        s_ambienceVolume  = Mathf.Clamp01(s_ambienceVolume);
        s_uiVolume        = Mathf.Clamp01(s_uiVolume);
        s_resolutionScale = Mathf.Clamp(s_resolutionScale, 0.5f, 1f);
    }

    // ── Visual Properties ───────────────────────────────────────

    public static ColorblindMode CurrentColorblindMode
    {
        get => s_colorblindMode;
        set { s_colorblindMode = value; SaveInt(K_ColorblindMode, (int)value); NotifyChanged(); }
    }

    // Legacy alias
    public static ColorblindMode Mode => s_colorblindMode;

    public static void SetMode(ColorblindMode mode) => CurrentColorblindMode = mode;

    public static bool HighContrast
    {
        get => s_highContrast;
        set { s_highContrast = value; SaveBool(K_HighContrast, value); NotifyChanged(); }
    }

    public static float TextScale
    {
        get => s_textScale;
        set { s_textScale = Mathf.Clamp(value, 0.8f, 1.5f); SaveFloat(K_TextScale, s_textScale); NotifyChanged(); }
    }

    // ── Motion Properties ───────────────────────────────────────

    public static bool ReduceMotion
    {
        get => s_reduceMotion;
        set { s_reduceMotion = value; SaveBool(K_ReduceMotion, value); NotifyChanged(); }
    }

    public static float ScreenShakeScale
    {
        get => s_screenShakeScale;
        set { s_screenShakeScale = Mathf.Clamp01(value); SaveFloat(K_ScreenShake, s_screenShakeScale); NotifyChanged(); }
    }

    // ── Audio Properties ────────────────────────────────────────

    public static float MasterVolume
    {
        get => s_masterVolume;
        set { s_masterVolume = Mathf.Clamp01(value); SaveFloat(K_MasterVolume, s_masterVolume); NotifyChanged(); }
    }

    public static float MusicVolume
    {
        get => s_musicVolume;
        set { s_musicVolume = Mathf.Clamp01(value); SaveFloat(K_MusicVolume, s_musicVolume); NotifyChanged(); }
    }

    public static float SFXVolume
    {
        get => s_sfxVolume;
        set { s_sfxVolume = Mathf.Clamp01(value); SaveFloat(K_SFXVolume, s_sfxVolume); NotifyChanged(); }
    }

    public static float AmbienceVolume
    {
        get => s_ambienceVolume;
        set { s_ambienceVolume = Mathf.Clamp01(value); SaveFloat(K_AmbienceVolume, s_ambienceVolume); NotifyChanged(); }
    }

    public static float UIVolume
    {
        get => s_uiVolume;
        set { s_uiVolume = Mathf.Clamp01(value); SaveFloat(K_UIVolume, s_uiVolume); NotifyChanged(); }
    }

    public static bool CaptionsEnabled
    {
        get => s_captionsEnabled;
        set { s_captionsEnabled = value; SaveBool(K_CaptionsEnabled, value); NotifyChanged(); }
    }

    // ── Timing Properties ───────────────────────────────────────

    /// <summary>
    /// Timer multiplier: 1.0 = normal, 1.5 = relaxed, 2.0 = extended, 0 = unlimited (no timer).
    /// </summary>
    public static float TimerMultiplier
    {
        get => s_timerMultiplier;
        set { s_timerMultiplier = Mathf.Max(0f, value); SaveFloat(K_TimerMultiplier, s_timerMultiplier); NotifyChanged(); }
    }

    // ── Performance Properties ──────────────────────────────────

    public static float ResolutionScale
    {
        get => s_resolutionScale;
        set
        {
            s_resolutionScale = Mathf.Clamp(value, 0.5f, 1f);
            SaveFloat(K_ResolutionScale, s_resolutionScale);
            ScalableBufferManager.ResizeBuffers(s_resolutionScale, s_resolutionScale);
            NotifyChanged();
        }
    }

    public static int QualityPreset
    {
        get => s_qualityPreset;
        set
        {
            s_qualityPreset = value;
            SaveInt(K_QualityPreset, value);
            if (IrisQualityManager.Instance != null)
                IrisQualityManager.Instance.ApplyPreset(value);
            NotifyChanged();
        }
    }

    public static bool PSXEnabled
    {
        get => s_psxEnabled;
        set { s_psxEnabled = value; SaveBool(K_PSXEnabled, value); NotifyChanged(); }
    }

    public static bool InvertScroll
    {
        get => s_invertScroll;
        set { s_invertScroll = value; SaveBool(K_InvertScroll, value); NotifyChanged(); }
    }

    // ── Color Helpers (unchanged API) ───────────────────────────

    public static Color GetHappyColor() => s_happyColors[(int)s_colorblindMode];
    public static Color GetSadColor()   => s_sadColors[(int)s_colorblindMode];

    // ── Reset ───────────────────────────────────────────────────

    /// <summary>Reset all settings to defaults, save, and notify.</summary>
    public static void ResetAll()
    {
        LoadAllDefaults();

        PlayerPrefs.DeleteKey(K_ColorblindMode);
        PlayerPrefs.DeleteKey(K_HighContrast);
        PlayerPrefs.DeleteKey(K_TextScale);
        PlayerPrefs.DeleteKey(K_ReduceMotion);
        PlayerPrefs.DeleteKey(K_ScreenShake);
        PlayerPrefs.DeleteKey(K_MasterVolume);
        PlayerPrefs.DeleteKey(K_MusicVolume);
        PlayerPrefs.DeleteKey(K_SFXVolume);
        PlayerPrefs.DeleteKey(K_AmbienceVolume);
        PlayerPrefs.DeleteKey(K_UIVolume);
        PlayerPrefs.DeleteKey(K_CaptionsEnabled);
        PlayerPrefs.DeleteKey(K_TimerMultiplier);
        PlayerPrefs.DeleteKey(K_ResolutionScale);
        PlayerPrefs.DeleteKey(K_QualityPreset);
        PlayerPrefs.DeleteKey(K_PSXEnabled);
        PlayerPrefs.DeleteKey(K_InvertScroll);
        PlayerPrefs.Save();

        ScalableBufferManager.ResizeBuffers(1f, 1f);
        NotifyChanged();
    }

    // ── PlayerPrefs helpers ─────────────────────────────────────

    private static void SaveInt(string key, int val)
    {
        PlayerPrefs.SetInt(key, val);
        PlayerPrefs.Save();
    }

    private static void SaveFloat(string key, float val)
    {
        PlayerPrefs.SetFloat(key, val);
        PlayerPrefs.Save();
    }

    private static void SaveBool(string key, bool val)
    {
        PlayerPrefs.SetInt(key, val ? 1 : 0);
        PlayerPrefs.Save();
    }
}
