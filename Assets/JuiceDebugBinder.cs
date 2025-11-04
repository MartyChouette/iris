using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class JuiceDebugBinder : MonoBehaviour
{
    [Header("Settings Asset (toggles write here)")]
    public JuiceSettings settings;

    [Header("Affect these Rope Cutters (sliders write here)")]
    public Obi.Samples.RopeSweepCutJuicy[] ropeCutTargets;

    [Header("Existing UI (assign your already-made controls)")]
    public Toggle uiJuiceToggle;      // → settings.enableUIJuice
    public Toggle timeJuiceToggle;    // → settings.enableTimeJuice
    public Toggle sapFxToggle;        // → settings.enableSapFX (optional)

    public Slider freezeDurationSlider;          // seconds (≥ 0)
    public TMP_Text freezeDurationText;            // optional readout

    public Slider freezeTimeScaleSlider;         // 0..1
    public TMP_Text freezeTimeScaleText;

    public Slider postFreezeDurationSlider;      // seconds (≥ 0)
    public TMP_Text postFreezeDurationText;

    public Slider postFreezeTimeScaleSlider;     // 0..1
    public TMP_Text postFreezeTimeScaleText;

    void OnEnable()
    {
        // seed toggles from settings
        if (settings)
        {
            if (uiJuiceToggle) uiJuiceToggle.isOn = settings.enableUIJuice;
            if (timeJuiceToggle) timeJuiceToggle.isOn = settings.enableTimeJuice;
            if (sapFxToggle) sapFxToggle.isOn = settings.enableSapFX;
        }

        // seed sliders from first target
        var t = FirstTarget();
        if (t)
        {
            if (freezeDurationSlider) freezeDurationSlider.value = Mathf.Max(0f, t.freezeDuration);
            if (freezeTimeScaleSlider) freezeTimeScaleSlider.value = Mathf.Clamp01(t.freezeTimeScale);
            if (postFreezeDurationSlider) postFreezeDurationSlider.value = Mathf.Max(0f, t.postFreezeSloMoDuration);
            if (postFreezeTimeScaleSlider) postFreezeTimeScaleSlider.value = Mathf.Clamp01(t.postFreezeTimeScale);
        }

        // toggle listeners
        if (uiJuiceToggle) uiJuiceToggle.onValueChanged.AddListener(v => { if (settings) settings.enableUIJuice = v; });
        if (timeJuiceToggle) timeJuiceToggle.onValueChanged.AddListener(v => { if (settings) settings.enableTimeJuice = v; });
        if (sapFxToggle) sapFxToggle.onValueChanged.AddListener(v => { if (settings) settings.enableSapFX = v; });

        // slider listeners
        if (freezeDurationSlider)
            freezeDurationSlider.onValueChanged.AddListener(v =>
            { v = Mathf.Max(0f, v); ForEachTarget(x => x.freezeDuration = v); SetText(freezeDurationText, $"Freeze: {v:0.###}s"); });

        if (freezeTimeScaleSlider)
            freezeTimeScaleSlider.onValueChanged.AddListener(v =>
            { v = Mathf.Clamp01(v); ForEachTarget(x => x.freezeTimeScale = v); SetText(freezeTimeScaleText, $"Freeze TS: {v:0.###}"); });

        if (postFreezeDurationSlider)
            postFreezeDurationSlider.onValueChanged.AddListener(v =>
            { v = Mathf.Max(0f, v); ForEachTarget(x => x.postFreezeSloMoDuration = v); SetText(postFreezeDurationText, $"SloMo: {v:0.###}s"); });

        if (postFreezeTimeScaleSlider)
            postFreezeTimeScaleSlider.onValueChanged.AddListener(v =>
            { v = Mathf.Clamp01(v); ForEachTarget(x => x.postFreezeTimeScale = v); SetText(postFreezeTimeScaleText, $"SloMo TS: {v:0.###}"); });

        // initial labels
        if (freezeDurationSlider) SetText(freezeDurationText, $"Freeze: {freezeDurationSlider.value:0.###}s");
        if (freezeTimeScaleSlider) SetText(freezeTimeScaleText, $"Freeze TS: {freezeTimeScaleSlider.value:0.###}");
        if (postFreezeDurationSlider) SetText(postFreezeDurationText, $"SloMo: {postFreezeDurationSlider.value:0.###}s");
        if (postFreezeTimeScaleSlider) SetText(postFreezeTimeScaleText, $"SloMo TS: {postFreezeTimeScaleSlider.value:0.###}");
    }

    void OnDisable()
    {
        if (uiJuiceToggle) uiJuiceToggle.onValueChanged.RemoveAllListeners();
        if (timeJuiceToggle) timeJuiceToggle.onValueChanged.RemoveAllListeners();
        if (sapFxToggle) sapFxToggle.onValueChanged.RemoveAllListeners();

        if (freezeDurationSlider) freezeDurationSlider.onValueChanged.RemoveAllListeners();
        if (freezeTimeScaleSlider) freezeTimeScaleSlider.onValueChanged.RemoveAllListeners();
        if (postFreezeDurationSlider) postFreezeDurationSlider.onValueChanged.RemoveAllListeners();
        if (postFreezeTimeScaleSlider) postFreezeTimeScaleSlider.onValueChanged.RemoveAllListeners();
    }

    // helpers
    Obi.Samples.RopeSweepCutJuicy FirstTarget()
    {
        if (ropeCutTargets == null) return null;
        foreach (var r in ropeCutTargets) if (r) return r;
        return null;
    }

    void ForEachTarget(System.Action<Obi.Samples.RopeSweepCutJuicy> act)
    {
        if (ropeCutTargets == null) return;
        foreach (var r in ropeCutTargets) if (r) act(r);
    }

    static void SetText(TMP_Text t, string s) { if (t) t.text = s; }
}
