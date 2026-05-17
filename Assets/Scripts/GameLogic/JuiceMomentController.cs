/**
 * @file JuiceMomentController.cs
 * @brief JuiceMomentController script.
 * @details
 * - Auto-generated Doxygen header. Expand @details with intent, invariants, and perf notes as needed.
*
 * @ingroup thirdparty
 */

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using DynamicMeshCutter;   // for XYTetherJoint cut suppression


#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Central controller for cinematic "juice moments":
/// - Barrier pre-hold tension
/// - Freeze frame
/// - Slow-motion with curves
/// - Camera push-in
/// - Micro camera shake
/// - FOV burst
/// - URP Bloom / Chromatic / MotionBlur spikes
/// - Screen ripple / sprite flash
/// - Sound stingers + particle bursts
/// - Temporary input disable
/// 
/// Call TriggerJuiceMoment() with a JuiceTimelineAsset to play.
/// </summary>
/**
 * @details
 * Intent:
 * - Centralized controller for "juice moments": brief, authored bursts of feedback (visual/audio/haptics).
 * - Provides serializable settings pods (chromatic flash, motion blur spike, freeze frame, ripple, etc.)
 *   that can be triggered by game events (cuts, snaps, evaluation, failures).
 *
 * Design rule:
 * - Keep this layer strictly reactive (it reacts to gameplay truth) and never authoritative
 *   (it should not decide scoring / failure).
 *
 * Authoring knobs:
 * - Each effect has its own enable flag and curve for shaping intensity over time.
 * - FreezeFrameSettings uses a frozenTimeScale multiplier (often 0) for impact punctuation.
 *
 * Gotchas:
 * - Be careful with timeScale changes during UI transitions (grading/gameover). Prefer unscaled timers
 *   when you need UI to animate during pause/slowmo.
 

*Responsibilities:
 * - (Documented) See fields and methods below.
 *
 * Unity lifecycle:
 * - Awake(): cache references / validate setup.
 * - OnEnable()/OnDisable(): hook/unhook events.
 * - Update(): per-frame behavior (if any).
 *
 * Gotchas:
 * - Keep hot paths allocation-free (Update/cuts/spawns).
 * - Prefer event-driven UI updates over per-frame string building.
 *
 * @ingroup thirdparty
 */
public class JuiceMomentController : MonoBehaviour
{
    public static JuiceMomentController Instance { get; private set; }

    #region Core References

    [Header("Core References")]
    [Tooltip("Camera whose transform and FOV we will manipulate.")]
    public Camera targetCamera;

    [Tooltip("Optional separate rig/parent for camera position shake & push.")]
    public Transform cameraRig; // if null, we�ll move the camera itself

    [Tooltip("Global URP Volume with Bloom / Chromatic / MotionBlur overrides.")]
    public Volume postProcessVolume;

    [Tooltip("AudioSource used for playing one-shot stingers.")]
    public AudioSource stingerAudioSource;

    [Tooltip("Default timeline if none is passed.")]
    public JuiceTimelineAsset defaultTimeline;

    [Tooltip("Behaviours to temporarily disable during the juice moment (e.g. input scripts).")]
    public UnityEngine.Behaviour[] behavioursToDisable;

    #endregion

    #region Settings Classes

    [System.Serializable]
    /**
     * @class SlowMoSettings
     * @brief SlowMoSettings component.
     * @details
     * Responsibilities:
     * - (Documented) See fields and methods below.
     *
     * Unity lifecycle:
     * - Awake(): cache references / validate setup.
     * - OnEnable()/OnDisable(): hook/unhook events.
     * - Update(): per-frame behavior (if any).
     *
     * Gotchas:
     * - Keep hot paths allocation-free (Update/cuts/spawns).
     * - Prefer event-driven UI updates over per-frame string building.
     *
     * @ingroup thirdparty
     */
    public class SlowMoSettings
    {
        public bool enabled = true;

        [Header("Scales")]
        [Range(0f, 1f)] public float preHoldTimeScale = 0.25f;
        [Range(0f, 1f)] public float impactSlowMoScale = 0.08f;

        [Header("Curves")]
        public bool useSameCurve = false;
        public AnimationCurve slowInCurve = AnimationCurve.EaseInOut(0, 1, 1, 0.08f);
        public AnimationCurve slowOutCurve = AnimationCurve.EaseInOut(0, 0.08f, 1, 1);

        [Header("FixedDeltaTime")]
        [Tooltip("Base fixedDeltaTime at scale 1.0 (usually 0.02).")]
        public float baseFixedDeltaTime = 0.02f;
    }

    [System.Serializable]
    /**
     * @class CameraPushSettings
     * @brief CameraPushSettings component.
     * @details
     * Responsibilities:
     * - (Documented) See fields and methods below.
     *
     * Unity lifecycle:
     * - Awake(): cache references / validate setup.
     * - OnEnable()/OnDisable(): hook/unhook events.
     * - Update(): per-frame behavior (if any).
     *
     * Gotchas:
     * - Keep hot paths allocation-free (Update/cuts/spawns).
     * - Prefer event-driven UI updates over per-frame string building.
     *
     * @ingroup thirdparty
     */
    public class CameraPushSettings
    {
        public bool enabled = true;

        [Tooltip("Local Z distance to push camera forward (negative if forward is -Z).")]
        public float pushDistance = -0.6f;

        [Tooltip("Ease curve for push during the main moment.")]
        public AnimationCurve pushCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    }

    [System.Serializable]
    /**
     * @class FreezeFrameSettings
     * @brief FreezeFrameSettings component.
     * @details
     * Responsibilities:
     * - (Documented) See fields and methods below.
     *
     * Unity lifecycle:
     * - Awake(): cache references / validate setup.
     * - OnEnable()/OnDisable(): hook/unhook events.
     * - Update(): per-frame behavior (if any).
     *
     * Gotchas:
     * - Keep hot paths allocation-free (Update/cuts/spawns).
     * - Prefer event-driven UI updates over per-frame string building.
     *
     * @ingroup thirdparty
     */
    public class FreezeFrameSettings
    {
        public bool enabled = true;

        [Tooltip("Extra time scale multiplier while frozen (usually 0).")]
        [Range(0f, 0.1f)] public float frozenTimeScale = 0f;
    }

    [System.Serializable]
    /**
     * @class ShakeSettings
     * @brief ShakeSettings component.
     * @details
     * Responsibilities:
     * - (Documented) See fields and methods below.
     *
     * Unity lifecycle:
     * - Awake(): cache references / validate setup.
     * - OnEnable()/OnDisable(): hook/unhook events.
     * - Update(): per-frame behavior (if any).
     *
     * Gotchas:
     * - Keep hot paths allocation-free (Update/cuts/spawns).
     * - Prefer event-driven UI updates over per-frame string building.
     *
     * @ingroup thirdparty
     */
    public class ShakeSettings
    {
        public bool enabled = true;
        [Tooltip("How strong the camera shake is at impact.")]
        public float impactAmplitude = 0.2f;
        [Tooltip("Frequency of camera shake oscillation.")]
        public float frequency = 25f;
    }

    [System.Serializable]
    /**
     * @class FOVBurstSettings
     * @brief FOVBurstSettings component.
     * @details
     * Responsibilities:
     * - (Documented) See fields and methods below.
     *
     * Unity lifecycle:
     * - Awake(): cache references / validate setup.
     * - OnEnable()/OnDisable(): hook/unhook events.
     * - Update(): per-frame behavior (if any).
     *
     * Gotchas:
     * - Keep hot paths allocation-free (Update/cuts/spawns).
     * - Prefer event-driven UI updates over per-frame string building.
     *
     * @ingroup thirdparty
     */
    public class FOVBurstSettings
    {
        public bool enabled = true;
        [Tooltip("How much to add to the camera FOV at peak.")]
        public float burstAmount = 6f;
        public AnimationCurve burstCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    }

    [System.Serializable]
    /**
     * @class BloomPulseSettings
     * @brief BloomPulseSettings component.
     * @details
     * Responsibilities:
     * - (Documented) See fields and methods below.
     *
     * Unity lifecycle:
     * - Awake(): cache references / validate setup.
     * - OnEnable()/OnDisable(): hook/unhook events.
     * - Update(): per-frame behavior (if any).
     *
     * Gotchas:
     * - Keep hot paths allocation-free (Update/cuts/spawns).
     * - Prefer event-driven UI updates over per-frame string building.
     *
     * @ingroup thirdparty
     */
    public class BloomPulseSettings
    {
        public bool enabled = true;
        [Tooltip("How much to add at peak to bloom intensity.")]
        public float extraIntensity = 1.5f;
        public AnimationCurve pulseCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    }

    [System.Serializable]
    /**
     * @class ChromaticFlashSettings
     * @brief ChromaticFlashSettings component.
     * @details
     * Responsibilities:
     * - (Documented) See fields and methods below.
     *
     * Unity lifecycle:
     * - Awake(): cache references / validate setup.
     * - OnEnable()/OnDisable(): hook/unhook events.
     * - Update(): per-frame behavior (if any).
     *
     * Gotchas:
     * - Keep hot paths allocation-free (Update/cuts/spawns).
     * - Prefer event-driven UI updates over per-frame string building.
     *
     * @ingroup thirdparty
     */
    public class ChromaticFlashSettings
    {
        public bool enabled = true;
        [Tooltip("Chromatic aberration extra intensity at peak.")]
        public float extraChromatic = 0.4f;

        [Tooltip("Vignette intensity at peak (added).")]
        public float extraVignette = 0.25f;

        public AnimationCurve flashCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    }

    [System.Serializable]
    /**
     * @class MotionBlurSpikeSettings
     * @brief MotionBlurSpikeSettings component.
     * @details
     * Responsibilities:
     * - (Documented) See fields and methods below.
     *
     * Unity lifecycle:
     * - Awake(): cache references / validate setup.
     * - OnEnable()/OnDisable(): hook/unhook events.
     * - Update(): per-frame behavior (if any).
     *
     * Gotchas:
     * - Keep hot paths allocation-free (Update/cuts/spawns).
     * - Prefer event-driven UI updates over per-frame string building.
     *
     * @ingroup thirdparty
     */
    public class MotionBlurSpikeSettings
    {
        public bool enabled = true;

        [Tooltip("Extra motion blur intensity at peak.")]
        public float extraIntensity = 0.6f;

        public AnimationCurve spikeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    }

    [System.Serializable]
    /**
     * @class RumbleSettings
     * @brief RumbleSettings component.
     * @details
     * Responsibilities:
     * - (Documented) See fields and methods below.
     *
     * Unity lifecycle:
     * - Awake(): cache references / validate setup.
     * - OnEnable()/OnDisable(): hook/unhook events.
     * - Update(): per-frame behavior (if any).
     *
     * Gotchas:
     * - Keep hot paths allocation-free (Update/cuts/spawns).
     * - Prefer event-driven UI updates over per-frame string building.
     *
     * @ingroup thirdparty
     */
    public class RumbleSettings
    {
        public bool enabled = true;
        [Range(0f, 1f)] public float lowFrequency = 0.7f;
        [Range(0f, 1f)] public float highFrequency = 0.9f;
    }

    [System.Serializable]
    /**
     * @class ScreenRippleSettings
     * @brief ScreenRippleSettings component.
     * @details
     * Responsibilities:
     * - (Documented) See fields and methods below.
     *
     * Unity lifecycle:
     * - Awake(): cache references / validate setup.
     * - OnEnable()/OnDisable(): hook/unhook events.
     * - Update(): per-frame behavior (if any).
     *
     * Gotchas:
     * - Keep hot paths allocation-free (Update/cuts/spawns).
     * - Prefer event-driven UI updates over per-frame string building.
     *
     * @ingroup thirdparty
     */
    public class ScreenRippleSettings
    {
        public bool enabled = true;

        [Tooltip("Material used by your full-screen ripple effect (e.g. on a quad or UI Image).")]
        public Material rippleMaterial;

        [Tooltip("Float property on the material controlling ripple strength.")]
        public string rippleStrengthProperty = "_RippleStrength";

        [Tooltip("Peak extra strength for the ripple.")]
        public float peakStrength = 1.0f;

        public AnimationCurve rippleCurve = AnimationCurve.EaseInOut(0, 0, 1, 0);
    }

    [System.Serializable]
    /**
     * @class StingerSettings
     * @brief StingerSettings component.
     * @details
     * Responsibilities:
     * - (Documented) See fields and methods below.
     *
     * Unity lifecycle:
     * - Awake(): cache references / validate setup.
     * - OnEnable()/OnDisable(): hook/unhook events.
     * - Update(): per-frame behavior (if any).
     *
     * Gotchas:
     * - Keep hot paths allocation-free (Update/cuts/spawns).
     * - Prefer event-driven UI updates over per-frame string building.
     *
     * @ingroup thirdparty
     */
    public class StingerSettings
    {
        [Header("Audio Clips (optional)")]
        public AudioClip startClip;
        public AudioClip impactClip;
        public AudioClip endClip;

        [Header("Particle Systems (optional)")]
        public List<ParticleSystem> startParticles = new List<ParticleSystem>();
        public List<ParticleSystem> impactParticles = new List<ParticleSystem>();
        public List<ParticleSystem> endParticles = new List<ParticleSystem>();
    }

    [System.Serializable]
    /**
     * @class JuiceEvents
     * @brief JuiceEvents component.
     * @details
     * Responsibilities:
     * - (Documented) See fields and methods below.
     *
     * Unity lifecycle:
     * - Awake(): cache references / validate setup.
     * - OnEnable()/OnDisable(): hook/unhook events.
     * - Update(): per-frame behavior (if any).
     *
     * Gotchas:
     * - Keep hot paths allocation-free (Update/cuts/spawns).
     * - Prefer event-driven UI updates over per-frame string building.
     *
     * @ingroup thirdparty
     */
    public class JuiceEvents
    {
        public UnityEvent onJuiceStart;
        public UnityEvent onPreHold;
        public UnityEvent onFreeze;
        public UnityEvent onRelease;
        public UnityEvent onJuiceEnd;
    }

    #endregion

    #region Serialized Settings Instances

    [Header("Slow Motion")]
    public SlowMoSettings slowMo = new SlowMoSettings();

    [Header("Camera Push-In")]
    public CameraPushSettings cameraPush = new CameraPushSettings();

    [Header("Freeze Frame")]
    public FreezeFrameSettings freezeFrame = new FreezeFrameSettings();

    [Header("Camera Shake")]
    public ShakeSettings shake = new ShakeSettings();

    [Header("FOV Burst")]
    public FOVBurstSettings fovBurst = new FOVBurstSettings();

    [Header("Bloom Pulse")]
    public BloomPulseSettings bloomPulse = new BloomPulseSettings();

    [Header("Chromatic / Vignette Flash")]
    public ChromaticFlashSettings chromaticFlash = new ChromaticFlashSettings();

    [Header("Motion Blur Spike (URP)")]
    public MotionBlurSpikeSettings motionBlurSpike = new MotionBlurSpikeSettings();

    [Header("Gamepad Rumble")]
    public RumbleSettings rumble = new RumbleSettings();

    [Header("Screen Ripple / Hit Flash")]
    public ScreenRippleSettings screenRipple = new ScreenRippleSettings();

    [Header("Stingers & Particles")]
    public StingerSettings stingers = new StingerSettings();

    [Header("Timeline Events")]
    public JuiceEvents events = new JuiceEvents();

    #endregion

    #region Private State

    Coroutine _currentRoutine;

    // Time-scale changes are routed through TimeScaleManager (PRIORITY_JUICE).
    // No local save/restore needed.

    Transform _camTransform;
    Vector3 _camOriginalLocalPos;
    float _camOriginalFOV;

    Bloom _bloom;
    ChromaticAberration _chromatic;
    Vignette _vignette;
    MotionBlur _motionBlur;

    float _originalBloomIntensity;
    float _originalChromaticIntensity;
    float _originalVignetteIntensity;
    float _originalMotionBlurIntensity;

    bool _volumeReady;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (!targetCamera)
        {
            targetCamera = Camera.main;
            if (!targetCamera)
                Debug.LogError("[JuiceMomentController] No targetCamera assigned and Camera.main is null!", this);
        }
        _camTransform = cameraRig != null ? cameraRig : (targetCamera != null ? targetCamera.transform : transform);
        _camOriginalLocalPos = _camTransform.localPosition;
        _camOriginalFOV = targetCamera != null ? targetCamera.fieldOfView : 60f;

        CacheVolumeOverrides();
    }

    private void OnDisable()
    {
        if (_currentRoutine != null)
        {
            StopCoroutine(_currentRoutine);
            _currentRoutine = null;
            XYTetherJoint.SetCutBreakSuppressed(false);
            ResetAll();
            SetBehavioursEnabled(true);
        }
    }

    private void CacheVolumeOverrides()
    {
        if (!postProcessVolume || !postProcessVolume.profile)
            return;

        var profile = postProcessVolume.profile;

        profile.TryGet(out _bloom);
        profile.TryGet(out _chromatic);
        profile.TryGet(out _vignette);
        profile.TryGet(out _motionBlur);

        if (_bloom != null) _originalBloomIntensity = _bloom.intensity.value;
        if (_chromatic != null) _originalChromaticIntensity = _chromatic.intensity.value;
        if (_vignette != null) _originalVignetteIntensity = _vignette.intensity.value;
        if (_motionBlur != null) _originalMotionBlurIntensity = _motionBlur.intensity.value;

        _volumeReady = true;
    }

    #endregion

    #region Public API

    /// <summary>
    /// Trigger the juice moment using the given timeline.
    /// If null, uses defaultTimeline.
    /// </summary>
    public void TriggerJuiceMoment(JuiceTimelineAsset timeline)
    {
        if (timeline == null) timeline = defaultTimeline;
        if (timeline == null)
        {
            Debug.LogWarning("[JuiceMomentController] No timeline provided and no defaultTimeline assigned.");
            return;
        }

        if (_currentRoutine != null)
        {
            StopCoroutine(_currentRoutine);
            // Always clear suppression — restoring previous state can race with PlaneBehaviour.
            XYTetherJoint.SetCutBreakSuppressed(false);
            ResetAll();
            SetBehavioursEnabled(true);
        }

        _currentRoutine = StartCoroutine(JuiceRoutine(timeline));
    }

    #endregion

    #region Main Routine

    private IEnumerator JuiceRoutine(JuiceTimelineAsset timeline)
    {
        events.onJuiceStart?.Invoke();

        // Suppress joint breaks during the juice moment
        XYTetherJoint.SetCutBreakSuppressed(true);

        // Block inputs during the entire moment
        SetBehavioursEnabled(false);


        // Phase: Pre-hold (barrier tension)
        if (timeline.preHoldDuration > 0f)
        {
            events.onPreHold?.Invoke();
            if (timeline.triggerStartEvents)
            {
                PlayStinger(stingers.startClip);
                PlayParticles(stingers.startParticles);
            }

            if (timeline.useSlowMo && slowMo.enabled)
            {
                yield return PreHoldPhase(timeline.preHoldDuration);
            }
            else
            {
                // Just wait in unscaled time
                yield return WaitUnscaled(timeline.preHoldDuration);
            }
        }

        // Phase: Freeze at impact
        if (timeline.useFreezeFrame && freezeFrame.enabled && timeline.freezeDuration > 0f)
        {
            events.onFreeze?.Invoke();
            if (timeline.triggerImpactEvents)
            {
                PlayStinger(stingers.impactClip);
                PlayParticles(stingers.impactParticles);
            }

            yield return FreezePhase(timeline.freezeDuration);
        }

        // Phase: Slow-mo in + sustain + out
        if (timeline.useSlowMo && slowMo.enabled)
        {
            // Motion blur spike can overlap with slow-mo
            if (timeline.useMotionBlurSpike && motionBlurSpike.enabled && _motionBlur != null)
            {
                StartCoroutine(MotionBlurSpikePhase(
                    timeline.slowMoInDuration +
                    timeline.slowMoSustainDuration +
                    timeline.slowMoOutDuration));
            }

            yield return SlowMoPhase(
                timeline.slowMoInDuration,
                timeline.slowMoSustainDuration,
                timeline.slowMoOutDuration);
        }

        // Phase: Settle / return camera + FX
        yield return SettlePhase(timeline);

        if (timeline.triggerEndEvents)
        {
            PlayStinger(stingers.endClip);
            PlayParticles(stingers.endParticles);
        }

        events.onRelease?.Invoke();
        events.onJuiceEnd?.Invoke();

        // Always clear suppression — the juice moment is over.
        XYTetherJoint.SetCutBreakSuppressed(false);

        // Restore camera/time/effects and re-enable input
        ResetAll();
        SetBehavioursEnabled(true);
        _currentRoutine = null;

    }

    #endregion

    #region Phases

    private IEnumerator PreHoldPhase(float duration)
    {
        float startScale = Time.timeScale;
        float targetScale = slowMo.preHoldTimeScale;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(duration, 0.0001f);
            float eval = slowMo.slowInCurve.Evaluate(t);
            float scale = Mathf.Lerp(startScale, targetScale, eval);
            ApplyTimeScale(scale);
            yield return null;
        }
    }

    private IEnumerator FreezePhase(float duration)
    {
        float prevScale = Time.timeScale;
        ApplyTimeScale(freezeFrame.frozenTimeScale);

        // Micro camera shake + FOV burst + FX + ripple at impact
        float shakeT = 0f;
        float half = Mathf.Max(duration, 0.0001f);

        while (shakeT < duration)
        {
            float normalized = Mathf.Clamp01(shakeT / duration);

            if (shake.enabled)
            {
                ApplyShake(shake.impactAmplitude, normalized);
            }

            ApplyFOVBurst(normalized);
            ApplyBloomPulse(normalized);
            ApplyChromaticFlash(normalized);
            ApplyScreenRipple(normalized);

            // Rumble is short; we can just fire and forget once at start
            if (rumble.enabled && Mathf.Approximately(shakeT, 0f))
            {
                TriggerRumble(duration);
            }

            shakeT += Time.unscaledDeltaTime;
            yield return null;
        }

        // Restore time scale BEFORE slow-mo phase (we�ll re-apply)
        ApplyTimeScale(prevScale);
        // Reset immediate camera shake and ripple for next phases (but keep FOV, PP to be blended in settle)
        ResetCamLocalPosition();
        ResetRippleImmediate();
    }

    private IEnumerator SlowMoPhase(float inDur, float sustainDur, float outDur)
    {
        float startScale = Time.timeScale;
        float targetScale = slowMo.impactSlowMoScale;

        // In
        if (inDur > 0f)
        {
            float t = 0f;
            AnimationCurve inCurve = slowMo.slowInCurve;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / Mathf.Max(inDur, 0.0001f);
                float eval = inCurve.Evaluate(t);
                float scale = Mathf.Lerp(startScale, targetScale, eval);
                ApplyTimeScale(scale);

                float normalized = t; // reuse for camera/FX
                ApplyCameraPush(normalized);
                ApplyFOVBurst(normalized);
                ApplyBloomPulse(normalized);
                ApplyChromaticFlash(normalized);
                ApplyScreenRipple(normalized);

                yield return null;
            }
        }
        else
        {
            ApplyTimeScale(targetScale);
        }

        // Sustain
        if (sustainDur > 0f)
        {
            float t = 0f;
            while (t < sustainDur)
            {
                t += Time.unscaledDeltaTime;
                // Keep effects near peak
                ApplyCameraPush(1f);
                ApplyFOVBurst(1f);
                ApplyBloomPulse(1f);
                ApplyChromaticFlash(1f);
                ApplyScreenRipple(1f);
                yield return null;
            }
        }

        // Out
        if (outDur > 0f)
        {
            float t = 0f;
            AnimationCurve outCurve = slowMo.useSameCurve ? slowMo.slowInCurve : slowMo.slowOutCurve;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / Mathf.Max(outDur, 0.0001f);
                float eval = outCurve.Evaluate(t);
                float scale = Mathf.Lerp(targetScale, 1f, eval);
                ApplyTimeScale(scale);

                // Reverse camera push + FX towards baseline
                float normalized = 1f - eval;
                ApplyCameraPush(normalized);
                ApplyFOVBurst(normalized);
                ApplyBloomPulse(normalized);
                ApplyChromaticFlash(normalized);
                ApplyScreenRipple(normalized);

                yield return null;
            }
        }
        else
        {
            ApplyTimeScale(1f);
        }

        // Ensure time scale is fully restored by end of phase
        ApplyTimeScale(1f);
    }

    private IEnumerator SettlePhase(JuiceTimelineAsset timeline)
    {
        float duration = Mathf.Max(timeline.settleDuration, 0.0001f);
        float t = 0f;

        Vector3 startPos = _camTransform.localPosition;
        float startFOV = targetCamera.fieldOfView;

        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / duration;

            float eval = Mathf.Clamp01(t);

            // Camera position & FOV back
            _camTransform.localPosition = Vector3.Lerp(startPos, _camOriginalLocalPos, eval);
            targetCamera.fieldOfView = Mathf.Lerp(startFOV, _camOriginalFOV, eval);

            // PP blend back
            if (_volumeReady)
            {
                if (_bloom != null)
                {
                    _bloom.intensity.value = Mathf.Lerp(_bloom.intensity.value, _originalBloomIntensity, eval);
                }
                if (_chromatic != null)
                {
                    _chromatic.intensity.value = Mathf.Lerp(_chromatic.intensity.value, _originalChromaticIntensity, eval);
                }
                if (_vignette != null)
                {
                    _vignette.intensity.value = Mathf.Lerp(_vignette.intensity.value, _originalVignetteIntensity, eval);
                }
                if (_motionBlur != null)
                {
                    _motionBlur.intensity.value = Mathf.Lerp(_motionBlur.intensity.value, _originalMotionBlurIntensity, eval);
                }
            }

            // Ripple fades out too
            ApplyScreenRipple(0f);

            yield return null;
        }
    }

    private IEnumerator MotionBlurSpikePhase(float totalDuration)
    {
        if (!_motionBlur || !_volumeReady) yield break;
        float dur = Mathf.Max(totalDuration, 0.001f);
        float t = 0f;

        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / dur;
            float eval = motionBlurSpike.spikeCurve.Evaluate(t);
            float extra = motionBlurSpike.extraIntensity * eval;
            _motionBlur.intensity.value = _originalMotionBlurIntensity + extra;
            yield return null;
        }

        _motionBlur.intensity.value = _originalMotionBlurIntensity;
    }

    #endregion

    #region Helpers - Time & Input

    private void ApplyTimeScale(float scale)
    {
        TimeScaleManager.Set(TimeScaleManager.PRIORITY_JUICE, scale);
    }

    private void SetBehavioursEnabled(bool enabled)
    {
        if (behavioursToDisable == null) return;
        foreach (var b in behavioursToDisable)
        {
            if (b != null) b.enabled = enabled;
        }
    }

    private IEnumerator WaitUnscaled(float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    #endregion

    #region Helpers - Camera Transform & Effects

    private void ResetCamLocalPosition()
    {
        _camTransform.localPosition = _camOriginalLocalPos;
    }

    private void ApplyCameraPush(float normalized)
    {
        if (!cameraPush.enabled) return;
        if (targetCamera == null || _camTransform == null) return;

        float eval = cameraPush.pushCurve.Evaluate(normalized);
        Vector3 basePos = _camOriginalLocalPos;
        Vector3 pushedPos = basePos + Vector3.forward * cameraPush.pushDistance;
        _camTransform.localPosition = Vector3.Lerp(basePos, pushedPos, eval);
    }

    private void ApplyShake(float amplitude, float normalized)
    {
        if (!shake.enabled) return;
        if (_camTransform == null) return;

        float t = Time.unscaledTime * shake.frequency;
        float damp = 1f - normalized; // start strong, then fade
        float offsetX = (Mathf.PerlinNoise(t, 0f) - 0.5f) * 2f * amplitude * damp;
        float offsetY = (Mathf.PerlinNoise(0f, t) - 0.5f) * 2f * amplitude * damp;

        _camTransform.localPosition = _camOriginalLocalPos + new Vector3(offsetX, offsetY, 0f);
    }

    private void ApplyFOVBurst(float normalized)
    {
        if (!fovBurst.enabled || targetCamera == null) return;
        float eval = fovBurst.burstCurve.Evaluate(normalized);
        targetCamera.fieldOfView = _camOriginalFOV + fovBurst.burstAmount * eval;
    }

    private void ApplyBloomPulse(float normalized)
    {
        if (!_volumeReady || _bloom == null || !bloomPulse.enabled) return;
        float eval = bloomPulse.pulseCurve.Evaluate(normalized);
        _bloom.intensity.value = _originalBloomIntensity + bloomPulse.extraIntensity * eval;
    }

    private void ApplyChromaticFlash(float normalized)
    {
        if (!_volumeReady || !chromaticFlash.enabled) return;

        float eval = chromaticFlash.flashCurve.Evaluate(normalized);

        if (_chromatic != null)
        {
            _chromatic.intensity.value = _originalChromaticIntensity + chromaticFlash.extraChromatic * eval;
        }
        if (_vignette != null)
        {
            _vignette.intensity.value = _originalVignetteIntensity + chromaticFlash.extraVignette * eval;
        }
    }

    private void ApplyScreenRipple(float normalized)
    {
        if (!screenRipple.enabled) return;
        if (!screenRipple.rippleMaterial) return;

        float eval = screenRipple.rippleCurve.Evaluate(normalized);
        float strength = screenRipple.peakStrength * eval;
        screenRipple.rippleMaterial.SetFloat(screenRipple.rippleStrengthProperty, strength);
    }

    private void ResetRippleImmediate()
    {
        if (!screenRipple.rippleMaterial) return;
        screenRipple.rippleMaterial.SetFloat(screenRipple.rippleStrengthProperty, 0f);
    }

    #endregion

    #region Helpers - Audio, Particles, Rumble

    private void PlayStinger(AudioClip clip)
    {
        if (!clip || !stingerAudioSource) return;
        stingerAudioSource.PlayOneShot(clip);
    }

    private void PlayParticles(List<ParticleSystem> systems)
    {
        if (systems == null) return;
        foreach (var ps in systems)
        {
            if (ps != null) ps.Play(true);
        }
    }

    private void TriggerRumble(float duration)
    {
#if ENABLE_INPUT_SYSTEM
        if (!rumble.enabled) return;
        var gamepad = Gamepad.current;
        if (gamepad == null) return;

        gamepad.SetMotorSpeeds(rumble.lowFrequency, rumble.highFrequency);
        StartCoroutine(StopRumbleAfter(duration));
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private IEnumerator StopRumbleAfter(float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        var gamepad = Gamepad.current;
        if (gamepad != null)
        {
            gamepad.SetMotorSpeeds(0f, 0f);
        }
    }
#endif

    #endregion

    #region Helpers - Reset

    private void ResetAll()
    {
        TimeScaleManager.Clear(TimeScaleManager.PRIORITY_JUICE);
        ResetCamLocalPosition();
        if (targetCamera != null) targetCamera.fieldOfView = _camOriginalFOV;

        if (_volumeReady)
        {
            if (_bloom != null) _bloom.intensity.value = _originalBloomIntensity;
            if (_chromatic != null) _chromatic.intensity.value = _originalChromaticIntensity;
            if (_vignette != null) _vignette.intensity.value = _originalVignetteIntensity;
            if (_motionBlur != null) _motionBlur.intensity.value = _originalMotionBlurIntensity;
        }

        ResetRippleImmediate();
    }

    #endregion
}
