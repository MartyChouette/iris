using System.Collections;
using UnityEngine;

/// <summary>
/// Persistent (DDoL) music director that manages cross-scene music transitions.
/// Plays the menu song on the main menu, continues through the loading screen,
/// and cross-fades out as the game's environmental audio fades in.
///
/// Uses AudioManager's musicSource for playback. Higher-level than AudioManager —
/// AudioManager is the channel hub, MusicDirector is the music state machine.
/// </summary>
public class MusicDirector : MonoBehaviour
{
    public static MusicDirector Instance { get; private set; }

    [Header("Menu Music")]
    [Tooltip("Default menu song (used as fallback). Set at runtime by mood selection.")]
    [SerializeField] private AudioClip _menuSong;

    [Header("Mood Songs")]
    [Tooltip("Cozy mood menu song.")]
    [SerializeField] private AudioClip _cozySong;
    [Tooltip("Sad mood menu song (the original default).")]
    [SerializeField] private AudioClip _sadSong;
    [Tooltip("Creepy mood menu song.")]
    [SerializeField] private AudioClip _creepySong;

    [Tooltip("Volume for menu song (before master/music multipliers).")]
    [SerializeField] private float _menuSongVolume = 0.5f;

    [Header("Cross-fade")]
    [Tooltip("Seconds to fade menu music out when entering gameplay.")]
    [SerializeField] private float _crossFadeDuration = 3f;

    private bool _isCrossFading;
    private Coroutine _crossFadeRoutine;

    /// <summary>Auto-spawn only if no MusicDirector exists in the scene.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        // Scene-placed MusicDirector (with clips assigned) gets priority.
        // Only auto-spawn if the scene doesn't have one.
        if (Instance != null) return;
        var go = new GameObject("[MusicDirector]");
        go.AddComponent<MusicDirector>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // Detach from parent (scene builder might parent us) so DDoL works
        if (transform.parent != null)
            transform.SetParent(null, worldPositionStays: true);

        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Select a mood and switch the menu song. Starts playing immediately.</summary>
    public void SetMood(string mood)
    {
        AudioClip clip = mood switch
        {
            "Cozy"   => _cozySong,
            "Sad"    => _sadSong,
            "Creepy" => _creepySong,
            _        => _sadSong
        };
        if (clip != null) _menuSong = clip;
        // Force restart with the new clip
        if (AudioManager.Instance != null)
        {
            var src = AudioManager.Instance.musicSource;
            if (src != null) src.Stop();
        }
        PlayMenuSong();
    }

    /// <summary>
    /// Start playing the menu song. Safe to call multiple times — won't restart
    /// if the same clip is already playing.
    /// </summary>
    public void PlayMenuSong()
    {
        if (_menuSong == null)
        {
            Debug.LogWarning("[MusicDirector] No menu song — assign _menuSong in Inspector.");
            return;
        }
        if (AudioManager.Instance == null) return;

        var src = AudioManager.Instance.musicSource;
        if (src != null && src.isPlaying && src.clip == _menuSong)
            return; // already playing

        AudioManager.Instance.PlayMusic(_menuSong, _menuSongVolume, loop: true);
        Debug.Log("[MusicDirector] Menu song started.");
    }

    /// <summary>
    /// Begin cross-fading the menu music out. Call this when the game scene
    /// starts its environmental audio (e.g. from DayPhaseManager).
    /// </summary>
    public void FadeOutMenuMusic()
    {
        if (_isCrossFading) return;
        if (AudioManager.Instance == null) return;

        var src = AudioManager.Instance.musicSource;
        if (src == null || !src.isPlaying) return;

        _crossFadeRoutine = StartCoroutine(CrossFadeOut());
    }

    /// <summary>
    /// Immediately stop any music the director is managing.
    /// </summary>
    public void StopImmediate()
    {
        if (_crossFadeRoutine != null)
        {
            StopCoroutine(_crossFadeRoutine);
            _crossFadeRoutine = null;
        }
        _isCrossFading = false;

        AudioManager.Instance?.StopMusic();
        Debug.Log("[MusicDirector] Music stopped immediately.");
    }

    /// <summary>
    /// True if the director is currently fading out menu music.
    /// </summary>
    public bool IsCrossFading => _isCrossFading;

    /// <summary>
    /// True if the menu song is still the active music track (no record selected yet).
    /// </summary>
    public bool IsMenuMusicPlaying
    {
        get
        {
            if (_menuSong == null || AudioManager.Instance == null) return false;
            var src = AudioManager.Instance.musicSource;
            return src != null && src.isPlaying && src.clip == _menuSong;
        }
    }

    private IEnumerator CrossFadeOut()
    {
        _isCrossFading = true;

        var src = AudioManager.Instance?.musicSource;
        if (src == null || !src.isPlaying)
        {
            _isCrossFading = false;
            yield break;
        }

        float startVol = src.volume;
        float elapsed = 0f;

        Debug.Log($"[MusicDirector] Cross-fading menu music out over {_crossFadeDuration}s.");

        while (elapsed < _crossFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _crossFadeDuration);

            if (src != null)
                src.volume = Mathf.Lerp(startVol, 0f, t);

            yield return null;
        }

        // Fully stop
        if (src != null)
        {
            src.Stop();
            src.volume = startVol; // restore base volume for future PlayMusic calls
        }

        _isCrossFading = false;
        _crossFadeRoutine = null;
        Debug.Log("[MusicDirector] Menu music fade complete.");
    }
}
