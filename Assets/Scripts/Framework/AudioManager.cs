using UnityEngine;
using System.Collections;

/// <summary>
/// Central audio hub for Iris: owns persistent AudioSources for common mix lanes.
/// Survives scene loads via DontDestroyOnLoad.
/// Volume levels driven by AccessibilitySettings (master + per-channel).
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    /// <summary>Auto-spawn AudioManager if none exists (lets you play any scene directly in editor).</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureExists()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneCheck;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneCheck;
    }

    private static void OnSceneCheck(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (mode == UnityEngine.SceneManagement.LoadSceneMode.Additive) return;
        if (Instance != null) return;

        var go = new GameObject("AudioManager (Auto)");
        go.AddComponent<AudioManager>();
        Debug.Log("[AudioManager] Auto-spawned — no existing instance found.");
    }

    [Header("Audio Channels (2D)")]
    public AudioSource sfxSource;
    public AudioSource ambienceSource;
    public AudioSource weatherSource;
    public AudioSource musicSource;
    public AudioSource environmentSource;
    public AudioSource uiSource;

    [Header("Debug")]
    public bool debugLogs = false;


    // Cached per-channel volumes (applied on top of base volume args)
    private float _masterVol = 1f;
    private float _sfxVol    = 1f;
    private float _musicVol  = 1f;
    private float _ambVol    = 1f;
    private float _uiVol     = 1f;

    // Non-music mix: scales all channels except music (1 = full, 0.85 = -15%, etc.)
    private float _nonMusicMix = 1f;
    private float _nonMusicMixTarget = 1f;
    private Coroutine _nonMusicFadeRoutine;

    // SFX auto-cutoff: fades SFX source to silence after this many seconds (0 = disabled)
    private float _sfxCutoffTime;
    private Coroutine _sfxCutoffRoutine;
    private float _sfxBaseVolume = 1f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (transform.parent != null)
        {
            transform.SetParent(null, worldPositionStays: true);
        }

        DontDestroyOnLoad(gameObject);

        EnsureSource(ref sfxSource, "Audio_SFX");
        EnsureSource(ref ambienceSource, "Audio_Ambience");
        EnsureSource(ref weatherSource, "Audio_Weather");
        EnsureSource(ref musicSource, "Audio_Music");
        EnsureSource(ref environmentSource, "Audio_Environment");
        EnsureSource(ref uiSource, "Audio_UI");

        _sfxCutoffTime = 2f; // always-on: SFX fade+cut after 2 seconds

        AccessibilitySettings.OnSettingsChanged += ApplyVolumeSettings;
        ApplyVolumeSettings();
    }

    private void OnDestroy()
    {
        AccessibilitySettings.OnSettingsChanged -= ApplyVolumeSettings;
        if (_nonMusicFadeRoutine != null) StopCoroutine(_nonMusicFadeRoutine);
        if (_sfxCutoffRoutine != null) StopCoroutine(_sfxCutoffRoutine);
        if (_musicFadeCoroutine != null) StopCoroutine(_musicFadeCoroutine);
        if (_duckRoutine != null) StopCoroutine(_duckRoutine);
        if (Instance == this)
            Instance = null;
    }

    // ─── Volume Integration ──────────────────────────────────────

    /// <summary>
    /// Reads master + per-channel volumes from AccessibilitySettings
    /// and applies them to looping sources.
    /// </summary>
    private void ApplyVolumeSettings()
    {
        _masterVol = AccessibilitySettings.MasterVolume;
        _sfxVol    = AccessibilitySettings.SFXVolume;
        _musicVol  = AccessibilitySettings.MusicVolume;
        _ambVol    = AccessibilitySettings.AmbienceVolume;
        _uiVol     = AccessibilitySettings.UIVolume;

        // Update looping source volumes live
        if (IsValid(musicSource) && musicSource.isPlaying)
            musicSource.volume = _masterVol * _musicVol;
        if (IsValid(ambienceSource) && ambienceSource.isPlaying)
            ambienceSource.volume = _masterVol * _ambVol;
        if (IsValid(weatherSource) && weatherSource.isPlaying)
            weatherSource.volume = _masterVol * _ambVol;
    }

    // ─── Non-Music Mix (scene-level volume for everything except music) ──

    /// <summary>
    /// Scale all non-music channels (SFX, ambience, weather, environment, UI).
    /// 1.0 = full volume, 0.85 = -15%, 0.75 = -25%.
    /// Music (record player / menu) is unaffected.
    /// </summary>
    public void SetNonMusicMix(float mix, float fadeDuration = 0.5f)
    {
        _nonMusicMixTarget = Mathf.Clamp01(mix);
        if (_nonMusicFadeRoutine != null) StopCoroutine(_nonMusicFadeRoutine);

        if (fadeDuration <= 0f)
        {
            _nonMusicMix = _nonMusicMixTarget;
            ApplyNonMusicMix();
        }
        else
        {
            _nonMusicFadeRoutine = StartCoroutine(FadeNonMusicMix(fadeDuration));
        }
    }

    private IEnumerator FadeNonMusicMix(float duration)
    {
        float start = _nonMusicMix;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            _nonMusicMix = Mathf.Lerp(start, _nonMusicMixTarget, elapsed / duration);
            ApplyNonMusicMix();
            yield return null;
        }
        _nonMusicMix = _nonMusicMixTarget;
        ApplyNonMusicMix();
        _nonMusicFadeRoutine = null;
    }

    private void ApplyNonMusicMix()
    {
        // Update looping non-music sources
        if (IsValid(ambienceSource) && ambienceSource.isPlaying)
            ambienceSource.volume = ambienceSource.volume; // re-set on next PlayAmbience
        if (IsValid(weatherSource) && weatherSource.isPlaying)
            weatherSource.volume = weatherSource.volume;
    }

    /// <summary>Effective SFX volume including non-music mix.</summary>
    private float EffectiveSFXVol(float baseVol) => baseVol * _masterVol * _sfxVol * _nonMusicMix;
    private float EffectiveAmbVol(float baseVol) => baseVol * _masterVol * _ambVol * _nonMusicMix;
    private float EffectiveUIVol(float baseVol) => baseVol * _masterVol * _uiVol * _nonMusicMix;

    // ─── SFX Auto-Cutoff (fade SFX source to silence after N seconds) ──

    /// <summary>
    /// Enable SFX auto-cutoff: any SFX will fade out and stop after this many seconds.
    /// Pass 0 to disable. Used in apartment to prevent long SFX from droning.
    /// </summary>
    public void SetSFXCutoff(float seconds)
    {
        _sfxCutoffTime = Mathf.Max(seconds, 0f);
    }

    private void ScheduleSFXCutoff()
    {
        if (_sfxCutoffTime <= 0f) return;

        // Restore volume to known baseline before rescheduling — prevents
        // interrupted fades from ratcheting the volume down over time.
        if (_sfxCutoffRoutine != null)
        {
            StopCoroutine(_sfxCutoffRoutine);
            if (IsValid(sfxSource))
                sfxSource.volume = _sfxBaseVolume;
        }
        else
        {
            // Capture baseline on first schedule (clean state)
            if (IsValid(sfxSource))
                _sfxBaseVolume = sfxSource.volume;
        }

        _sfxCutoffRoutine = StartCoroutine(SFXCutoffRoutine(_sfxCutoffTime));
    }

    private IEnumerator SFXCutoffRoutine(float totalTime)
    {
        if (!IsValid(sfxSource)) yield break;

        // Let SFX play for most of the time, then fade the last 0.5s
        float fadeStart = Mathf.Max(totalTime - 0.5f, 0f);
        float fadeDuration = totalTime - fadeStart;

        yield return new WaitForSeconds(fadeStart);

        if (!IsValid(sfxSource)) yield break;
        float elapsed = 0f;

        while (elapsed < fadeDuration && IsValid(sfxSource))
        {
            elapsed += Time.unscaledDeltaTime;
            sfxSource.volume = Mathf.Lerp(_sfxBaseVolume, 0f, elapsed / fadeDuration);
            yield return null;
        }

        // Cut and restore volume for next SFX
        if (IsValid(sfxSource))
        {
            sfxSource.Stop();
            sfxSource.volume = _sfxBaseVolume;
        }
        _sfxCutoffRoutine = null;
    }

    // ─── Helpers ─────────────────────────────────────────────────

    private void EnsureSource(ref AudioSource src, string childName)
    {
        if (src != null)
            return;

        Transform child = transform.Find(childName);
        if (child != null)
            src = child.GetComponent<AudioSource>();

        if (src == null)
        {
            GameObject go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 0f;
        }

        if (src.transform.parent != transform)
            src.transform.SetParent(transform, worldPositionStays: true);
    }

    private static bool IsValid(AudioSource src) => src != null;

    private static void ShowCaption(string caption, float duration = 2f)
    {
        if (string.IsNullOrEmpty(caption)) return;
        if (!AccessibilitySettings.CaptionsEnabled) return;
        CaptionDisplay.Show(caption, duration);
    }

    // ─── One-shot channels ───────────────────────────────────────

    public void PlaySFX(AudioClip clip, float volume = 1f, string caption = null)
    {
        if (clip == null || !IsValid(sfxSource)) return;
        sfxSource.PlayOneShot(clip, EffectiveSFXVol(volume));
        if (debugLogs) Debug.Log("[AudioManager] SFX: " + clip.name, this);
        ShowCaption(caption);
        ScheduleSFXCutoff();
    }

    public void PlayEnvironment(AudioClip clip, float volume = 1f, string caption = null)
    {
        if (clip == null || !IsValid(environmentSource)) return;
        environmentSource.PlayOneShot(clip, EffectiveSFXVol(volume));
        ShowCaption(caption);
    }

    public void PlayUI(AudioClip clip, float volume = 1f, string caption = null)
    {
        if (clip == null || !IsValid(uiSource)) return;
        uiSource.PlayOneShot(clip, EffectiveUIVol(volume));
        ShowCaption(caption);
    }

    // ─── Ambience ────────────────────────────────────────────────

    public void PlayAmbience(AudioClip clip, float volume = 1f, bool loop = true)
    {
        if (clip == null || !IsValid(ambienceSource)) return;
        ambienceSource.clip = clip;
        ambienceSource.volume = EffectiveAmbVol(volume);
        ambienceSource.loop = loop;
        ambienceSource.Play();
    }

    public void StopAmbience()
    {
        if (!IsValid(ambienceSource)) return;
        ambienceSource.Stop();
    }

    // ─── Weather ─────────────────────────────────────────────────

    public void PlayWeather(AudioClip clip, float volume = 1f, bool loop = true)
    {
        if (clip == null || !IsValid(weatherSource)) return;
        weatherSource.clip = clip;
        weatherSource.volume = EffectiveAmbVol(volume);
        weatherSource.loop = loop;
        weatherSource.Play();
    }

    public void StopWeather()
    {
        if (!IsValid(weatherSource)) return;
        weatherSource.Stop();
    }

    // ─── Music ───────────────────────────────────────────────────

    public void PlayMusic(AudioClip clip, float volume = 1f, bool loop = true)
    {
        if (clip == null || !IsValid(musicSource)) return;
        _preDuckVolume = -1f; // clear duck state on new track
        musicSource.clip = clip;
        musicSource.volume = volume * _masterVol * _musicVol;
        musicSource.loop = loop;
        musicSource.Play();
    }

    private Coroutine _musicFadeCoroutine;
    private AudioClip _fadingOutClip;

    public void PauseMusic()
    {
        if (IsValid(musicSource) && musicSource.isPlaying)
            musicSource.Pause();
    }

    public void ResumeMusic()
    {
        if (IsValid(musicSource) && !musicSource.isPlaying && musicSource.clip != null)
            musicSource.UnPause();
    }

    public void StopMusic(float fadeTime = 0f)
    {
        if (!IsValid(musicSource)) return;
        _preDuckVolume = -1f;

        if (_musicFadeCoroutine != null)
        {
            StopCoroutine(_musicFadeCoroutine);
            _musicFadeCoroutine = null;
        }

        if (fadeTime <= 0f)
        {
            musicSource.Stop();
        }
        else
        {
            _fadingOutClip = musicSource.clip;
            _musicFadeCoroutine = StartCoroutine(FadeOutMusic(fadeTime));
        }
    }

    private IEnumerator FadeOutMusic(float time)
    {
        if (!IsValid(musicSource))
            yield break;

        float start = musicSource.volume;
        float t = 0f;

        while (t < time && IsValid(musicSource))
        {
            // Abort if a new clip started playing (PlayMusic was called during fade)
            if (musicSource.clip != _fadingOutClip)
            {
                _musicFadeCoroutine = null;
                yield break;
            }

            t += Time.unscaledDeltaTime;
            musicSource.volume = Mathf.Lerp(start, 0f, t / time);
            yield return null;
        }

        if (IsValid(musicSource) && musicSource.clip == _fadingOutClip)
        {
            musicSource.Stop();
            musicSource.volume = start;
        }

        _musicFadeCoroutine = null;
    }

    // ─── Music Duck (lower volume temporarily, then restore) ──

    private float _preDuckVolume = -1f;
    private Coroutine _duckRoutine;

    /// <summary>
    /// Smoothly lower music volume. Call UnduckMusic() to restore.
    /// </summary>
    public void DuckMusic(float targetVolume = 0.15f, float duration = 0.5f)
    {
        if (!IsValid(musicSource) || !musicSource.isPlaying) return;
        if (_preDuckVolume >= 0f) return; // already ducked

        _preDuckVolume = musicSource.volume;
        if (_duckRoutine != null) StopCoroutine(_duckRoutine);
        _duckRoutine = StartCoroutine(FadeMusicTo(targetVolume, duration));
    }

    /// <summary>
    /// Restore music volume after a duck.
    /// </summary>
    public void UnduckMusic(float duration = 1f)
    {
        if (_preDuckVolume < 0f) return;
        if (!IsValid(musicSource)) { _preDuckVolume = -1f; return; }

        float target = _preDuckVolume;
        _preDuckVolume = -1f;
        if (_duckRoutine != null) StopCoroutine(_duckRoutine);
        _duckRoutine = StartCoroutine(FadeMusicTo(target, duration));
    }

    private IEnumerator FadeMusicTo(float targetVol, float duration)
    {
        if (!IsValid(musicSource)) yield break;
        float start = musicSource.volume;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            if (IsValid(musicSource))
                musicSource.volume = Mathf.Lerp(start, targetVol, elapsed / duration);
            yield return null;
        }
        if (IsValid(musicSource))
            musicSource.volume = targetVol;
        _duckRoutine = null;
    }

    // ─── Dual SFX ────────────────────────────────────────────────

    public void PlayDualSFX(AudioClip first, AudioClip second, float delay, float volume = 1f)
    {
        if (!IsValid(sfxSource)) return;
        if (first == null && second == null) return;
        StartCoroutine(PlayDualRoutine(first, second, delay, volume));
    }

    private IEnumerator PlayDualRoutine(AudioClip first, AudioClip second, float delay, float volume)
    {
        if (!IsValid(sfxSource))
            yield break;

        float vol = EffectiveSFXVol(volume);
        if (first != null)
            sfxSource.PlayOneShot(first, vol);

        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        if (!IsValid(sfxSource))
            yield break;

        if (second != null)
            sfxSource.PlayOneShot(second, vol);
    }
}
