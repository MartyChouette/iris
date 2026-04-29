using UnityEngine;

/// <summary>
/// Low sine hum that grows in volume as the cursor approaches messy items
/// (trash, or non-General items left out of their home zone).
/// Pulses with a taper pattern (mmmmm mm mm mm mmmmmm) for an uneasy feeling.
/// Active during Exploration and DateInProgress phases only.
/// </summary>
public class MessProximityHum : MonoBehaviour
{
    public static MessProximityHum Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
    }

    [Header("Detection")]
    [Tooltip("Max distance (world units) at which the hum begins.")]
    [SerializeField] private float _detectionRadius = 1.85f;

    [Header("Audio")]
    [Tooltip("Base frequency of the sine tone (Hz). 110 = low A.")]
    [SerializeField] private float _frequency = 110f;

    [Tooltip("Maximum volume at closest proximity.")]
    [SerializeField, Range(0f, 1f)] private float _maxVolume = 0.25f;

    [Tooltip("Volume smoothing time (seconds).")]
    [SerializeField] private float _smoothTime = 0.15f;

    [Header("Fade")]
    [Tooltip("Fade-out smoothing time (seconds). Faster than fade-in for snappy cutoff.")]
    [SerializeField] private float _fadeOutTime = 0.08f;

    private AudioSource _source;
    private float _currentVolume;
    private float _volumeVelocity;
    private Camera _cam;
    private float _camRefreshTimer;
    private int _proxScanFrame;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoSpawn()
    {
        if (Instance != null) return;
        var go = new GameObject("[MessProximityHum]");
        go.AddComponent<MessProximityHum>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.loop = true;
        _source.spatialBlend = 0f; // 2D
        _source.volume = 0f;
        CreateSineClip();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        // Destroy the procedural AudioClip we created in CreateSineClip
        if (_source != null && _source.clip != null)
        {
            var clip = _source.clip;
            _source.clip = null;
            Destroy(clip);
        }
    }

    private void Update()
    {
        // Only active during apartment phases (not trimming, not evening/morning transitions)
        if (!ShouldBeActive())
        {
            if (_source.isPlaying && _currentVolume < 0.001f)
                _source.Pause();
            _currentVolume = Mathf.SmoothDamp(_currentVolume, 0f, ref _volumeVelocity, _smoothTime);
            _source.volume = 0f;
            return;
        }

        // Don't hum while holding an object (player is already acting on it)
        if (ObjectGrabber.IsHoldingObject)
        {
            _currentVolume = Mathf.SmoothDamp(_currentVolume, 0f, ref _volumeVelocity, _smoothTime);
            _source.volume = 0f;
            return;
        }

        // Throttle Camera.main lookup
        _camRefreshTimer -= Time.deltaTime;
        if (_camRefreshTimer <= 0f || _cam == null)
        {
            _cam = Camera.main;
            _camRefreshTimer = 0.5f;
        }
        if (_cam == null) return;

        // Raycast from cursor to get world position
        Vector2 screenPos = IrisInput.CursorPosition;
        Ray ray = ApartmentManager.Instance != null ? ApartmentManager.Instance.ScreenPointToRay(screenPos) : _cam.ScreenPointToRay(screenPos);

        Vector3 cursorWorld;
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            cursorWorld = hit.point;
        else
            return; // cursor not over anything

        // Throttle proximity scan — every 4th frame is enough for audio fade
        _proxScanFrame++;
        if (_proxScanFrame % 4 != 0) { _source.volume = _currentVolume; return; }

        // Find closest messy item — trash, or anything misplaced from its home zone
        float closestDist = float.MaxValue;
        var all = PlaceableObject.All;
        for (int i = 0; i < all.Count; i++)
        {
            var p = all[i];
            if (p == null) continue;
            if (!p.gameObject.activeInHierarchy) continue;
            if (p.CurrentState == PlaceableObject.State.Held) continue;
            if (!IsMessy(p)) continue;

            float dist = Vector3.Distance(cursorWorld, p.transform.position);
            if (dist < closestDist)
                closestDist = dist;
        }

        // Map distance to target volume
        float targetVol = 0f;
        if (closestDist < _detectionRadius)
        {
            // Quadratic falloff — gentle at edge, strong up close
            float t = 1f - (closestDist / _detectionRadius);
            targetVol = t * t * _maxVolume;
        }

        // Smooth volume — fast fade-out, gentle fade-in
        float smoothing = targetVol < _currentVolume ? _fadeOutTime : _smoothTime;
        _currentVolume = Mathf.SmoothDamp(_currentVolume, targetVol, ref _volumeVelocity, smoothing);

        // Apply global volume settings
        float globalVol = AccessibilitySettings.MasterVolume * AccessibilitySettings.SFXVolume;
        _source.volume = _currentVolume * globalVol;

        if (_currentVolume > 0.001f && !_source.isPlaying)
            _source.Play();
        else if (_currentVolume < 0.001f && _source.isPlaying)
            _source.Pause();
    }

    private bool ShouldBeActive()
    {
        if (DayPhaseManager.Instance == null) return false;
        var phase = DayPhaseManager.Instance.CurrentPhase;
        return phase == DayPhaseManager.DayPhase.Exploration
            || phase == DayPhaseManager.DayPhase.DateInProgress;
    }

    /// <summary>Trash items, or non-General items left out of their home zone.</summary>
    private static bool IsMessy(PlaceableObject p)
    {
        if (p.Category == ItemCategory.Trash) return true;
        if (p.Category == ItemCategory.General) return false;
        return p.HasHome && !p.IsAtHome;
    }

    private void CreateSineClip()
    {
        int sampleRate = 44100;
        int sampleCount = sampleRate; // 1 second loop
        var clip = AudioClip.Create("MessHum", sampleCount, 1, sampleRate, false);
        float[] data = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            // Fundamental + subtle harmonics for warmth
            float t = (float)i / sampleRate;
            float sample = 0.6f * Mathf.Sin(2f * Mathf.PI * _frequency * t)        // fundamental
                         + 0.25f * Mathf.Sin(2f * Mathf.PI * _frequency * 2f * t)   // 2nd harmonic
                         + 0.15f * Mathf.Sin(2f * Mathf.PI * _frequency * 3f * t);  // 3rd harmonic
            data[i] = sample * 0.4f; // headroom
        }

        clip.SetData(data, 0);
        _source.clip = clip;
    }
}
