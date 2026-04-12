using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Scene-scoped singleton managing daily weather.
/// Weighted random weather per day, feeds MoodMachine + NatureBox shader.
/// F3 cycles through weather states for debug.
/// </summary>
public class WeatherSystem : MonoBehaviour
{
    public static WeatherSystem Instance { get; private set; }

    public enum WeatherState { Clear, Overcast, Rainy, Stormy, Snowy, FallingLeaves }

    [Header("Weather Weights (relative, not %)")]
    [Tooltip("Chance weight for clear weather.")]
    [SerializeField] private float _clearWeight = 35f;
    [Tooltip("Chance weight for overcast weather.")]
    [SerializeField] private float _overcastWeight = 25f;
    [Tooltip("Chance weight for rainy weather.")]
    [SerializeField] private float _rainyWeight = 15f;
    [Tooltip("Chance weight for stormy weather.")]
    [SerializeField] private float _stormyWeight = 8f;
    [Tooltip("Chance weight for snowy weather.")]
    [SerializeField] private float _snowyWeight = 10f;
    [Tooltip("Chance weight for falling leaves weather.")]
    [SerializeField] private float _fallingLeavesWeight = 7f;

    [Header("Mood Values (fed to MoodMachine)")]
    [SerializeField] private float _clearMood = 0.1f;
    [SerializeField] private float _overcastMood = 0.3f;
    [SerializeField] private float _rainyMood = 0.6f;
    [SerializeField] private float _stormyMood = 0.9f;
    [SerializeField] private float _snowyMood = 0.4f;
    [SerializeField] private float _fallingLeavesMood = 0.25f;

    [Header("Rain Intensity")]
    [SerializeField] private float _clearRain = 0f;
    [SerializeField] private float _overcastRain = 0f;
    [SerializeField] private float _rainyRain = 0.6f;
    [SerializeField] private float _stormyRain = 1f;

    [Header("Schedule")]
    [Tooltip("Optional pre-planned weather per day. Days without entries use weighted random.")]
    [SerializeField] private WeatherSchedule _schedule;

    [Header("Audio")]
    [Tooltip("Ambient loop for rain.")]
    [SerializeField] private AudioClip _rainAmbience;
    [Tooltip("Ambient loop for storms.")]
    [SerializeField] private AudioClip _stormAmbience;

    public WeatherState CurrentWeather { get; private set; } = WeatherState.Clear;
    public float CurrentRainIntensity { get; private set; }
    public float CurrentSnowIntensity { get; private set; }
    public float CurrentLeafIntensity { get; private set; }
    public float CurrentOvercast { get; private set; }

    /// <summary>
    /// How much weather accelerates plant water evaporation.
    /// Hot/clear = faster drying, rainy/snowy = slower.
    /// </summary>
    public float GetDryingMultiplier()
    {
        return CurrentWeather switch
        {
            WeatherState.Clear         => 1.5f,
            WeatherState.Overcast      => 1.0f,
            WeatherState.Rainy         => 0.6f,
            WeatherState.Stormy        => 0.4f,
            WeatherState.Snowy         => 0.3f,
            WeatherState.FallingLeaves => 1.2f,
            _ => 1.0f,
        };
    }

    // Timeline playback
    private bool _timelineActive;
    private WeatherSchedule.DayTimeline _activeTimeline;

    private InputAction _debugCycleAction;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[WeatherSystem] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // F3 debug key
        _debugCycleAction = new InputAction("DebugCycleWeather",
            UnityEngine.InputSystem.InputActionType.Button,
            "<Keyboard>/f2");
    }

    private void OnEnable()
    {
        _debugCycleAction?.Enable();
    }

    private void OnDisable()
    {
        _debugCycleAction?.Disable();
    }

    private void Start()
    {
        // Generate initial weather from current day
        int day = GameClock.Instance != null ? GameClock.Instance.CurrentDay : 1;
        GenerateWeather(day);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            if (MoodMachine.Instance != null)
                MoodMachine.Instance.RemoveSource("Weather");
            Instance = null;
        }
        _debugCycleAction?.Dispose();
    }

    private void Update()
    {
        // F3 debug cycling
        if (_debugCycleAction != null && _debugCycleAction.WasPressedThisFrame())
        {
            _timelineActive = false; // debug override stops timeline
            int next = ((int)CurrentWeather + 1) % 6;
            ForceWeather((WeatherState)next);
        }

        // Timeline playback — lerp between keyframed weather states
        if (_timelineActive)
            TickTimeline();
    }

    private void TickTimeline()
    {
        float timeOfDay = GameClock.Instance != null ? GameClock.Instance.NormalizedTimeOfDay : 0.5f;

        WeatherSchedule.Evaluate(_activeTimeline, timeOfDay,
            out var stateA, out var stateB, out float t);

        // Get the NatureBox values for each state and lerp between them
        GetWeatherValues(stateA, out float rainA, out float snowA, out float leafA,
            out float overcastA, out float cloudA, out float fogA, out float snowCapA, out float moodA);
        GetWeatherValues(stateB, out float rainB, out float snowB, out float leafB,
            out float overcastB, out float cloudB, out float fogB, out float snowCapB, out float moodB);

        CurrentRainIntensity = Mathf.Lerp(rainA, rainB, t);
        CurrentSnowIntensity = Mathf.Lerp(snowA, snowB, t);
        CurrentLeafIntensity = Mathf.Lerp(leafA, leafB, t);
        CurrentOvercast      = Mathf.Lerp(overcastA, overcastB, t);

        // Update displayed state (nearest keyframe)
        var prevWeather = CurrentWeather;
        CurrentWeather = t < 0.5f ? stateA : stateB;
        if (CurrentWeather != prevWeather)
            NemaWeatherDialogue.ReactToWeather(CurrentWeather);

        // Feed mood (lerped)
        float mood = Mathf.Lerp(moodA, moodB, t);
        if (MoodMachine.Instance != null)
            MoodMachine.Instance.SetSource("Weather", mood);

        // Push lerped values to NatureBox
        if (NatureBoxController.Instance != null)
        {
            NatureBoxController.Instance.SetWeatherTargets(
                CurrentRainIntensity, CurrentSnowIntensity, CurrentLeafIntensity,
                CurrentOvercast, Mathf.Lerp(snowCapA, snowCapB, t),
                Mathf.Lerp(cloudA, cloudB, t), Mathf.Lerp(fogA, fogB, t));
        }
    }

    /// <summary>Get all weather values for a given state without applying them.</summary>
    private void GetWeatherValues(WeatherState state,
        out float rain, out float snow, out float leaf,
        out float overcast, out float cloud, out float fog, out float snowCap, out float mood)
    {
        rain = 0f; snow = 0f; leaf = 0f; overcast = 0f;
        cloud = 0.45f; fog = 0.35f; snowCap = 0f;

        mood = state switch
        {
            WeatherState.Clear         => _clearMood,
            WeatherState.Overcast      => _overcastMood,
            WeatherState.Rainy         => _rainyMood,
            WeatherState.Stormy        => _stormyMood,
            WeatherState.Snowy         => _snowyMood,
            WeatherState.FallingLeaves => _fallingLeavesMood,
            _ => _clearMood
        };

        switch (state)
        {
            case WeatherState.Clear:
                rain = _clearRain; cloud = 0.3f; fog = 0.25f; break;
            case WeatherState.Overcast:
                rain = _overcastRain; overcast = 0.5f; cloud = 0.75f; fog = 0.45f; break;
            case WeatherState.Rainy:
                rain = _rainyRain; overcast = 0.4f; cloud = 0.8f; fog = 0.5f; break;
            case WeatherState.Stormy:
                rain = _stormyRain; overcast = 0.8f; cloud = 0.95f; fog = 0.6f; break;
            case WeatherState.Snowy:
                snow = 0.8f; overcast = 0.3f; cloud = 0.65f; fog = 0.4f; snowCap = 0.9f; break;
            case WeatherState.FallingLeaves:
                leaf = 0.7f; cloud = 0.4f; fog = 0.3f; break;
        }
    }

    /// <summary>Generate weather for a given day. Uses schedule timeline if available, otherwise weighted random.</summary>
    public void GenerateWeather(int day)
    {
        // Check schedule for a timeline
        if (_schedule != null && _schedule.TryGetTimeline(day, out var timeline) && timeline.keyframes != null && timeline.keyframes.Length > 0)
        {
            _activeTimeline = timeline;
            _timelineActive = true;
            // Set initial weather from first keyframe
            SetWeather(timeline.keyframes[0].weather);
            Debug.Log($"[WeatherSystem] Day {day} weather (timeline): {timeline.keyframes.Length} keyframes");
            return;
        }

        _timelineActive = false;

        float total = _clearWeight + _overcastWeight + _rainyWeight
                    + _stormyWeight + _snowyWeight + _fallingLeavesWeight;
        float roll = Random.Range(0f, total);

        float cumulative = _clearWeight;
        if (roll < cumulative)
        { SetWeather(WeatherState.Clear); goto done; }

        cumulative += _overcastWeight;
        if (roll < cumulative)
        { SetWeather(WeatherState.Overcast); goto done; }

        cumulative += _rainyWeight;
        if (roll < cumulative)
        { SetWeather(WeatherState.Rainy); goto done; }

        cumulative += _stormyWeight;
        if (roll < cumulative)
        { SetWeather(WeatherState.Stormy); goto done; }

        cumulative += _snowyWeight;
        if (roll < cumulative)
        { SetWeather(WeatherState.Snowy); goto done; }

        SetWeather(WeatherState.FallingLeaves);

        done:
        Debug.Log($"[WeatherSystem] Day {day} weather: {CurrentWeather}");
    }

    /// <summary>Force a specific weather state (debug/testing).</summary>
    public void ForceWeather(WeatherState state)
    {
        SetWeather(state);
        Debug.Log($"[WeatherSystem] Forced weather: {state}");
    }

    private void SetWeather(WeatherState state)
    {
        CurrentWeather = state;

        // ── Mood ──
        float mood = state switch
        {
            WeatherState.Clear         => _clearMood,
            WeatherState.Overcast      => _overcastMood,
            WeatherState.Rainy         => _rainyMood,
            WeatherState.Stormy        => _stormyMood,
            WeatherState.Snowy         => _snowyMood,
            WeatherState.FallingLeaves => _fallingLeavesMood,
            _ => _clearMood
        };

        // ── Per-state intensities ──
        CurrentRainIntensity = 0f;
        CurrentSnowIntensity = 0f;
        CurrentLeafIntensity = 0f;
        CurrentOvercast = 0f;

        float cloudDensity = 0.45f; // base
        float horizonFog = 0.35f;   // base
        float snowCap = 0f;

        switch (state)
        {
            case WeatherState.Clear:
                CurrentRainIntensity = _clearRain;
                cloudDensity = 0.3f;
                horizonFog = 0.25f;
                break;

            case WeatherState.Overcast:
                CurrentRainIntensity = _overcastRain;
                CurrentOvercast = 0.5f;
                cloudDensity = 0.75f;
                horizonFog = 0.45f;
                break;

            case WeatherState.Rainy:
                CurrentRainIntensity = _rainyRain;
                CurrentOvercast = 0.4f;
                cloudDensity = 0.8f;
                horizonFog = 0.5f;
                break;

            case WeatherState.Stormy:
                CurrentRainIntensity = _stormyRain;
                CurrentOvercast = 0.8f;
                cloudDensity = 0.95f;
                horizonFog = 0.6f;
                break;

            case WeatherState.Snowy:
                CurrentSnowIntensity = 0.8f;
                CurrentOvercast = 0.3f;
                cloudDensity = 0.65f;
                horizonFog = 0.4f;
                snowCap = 0.9f;
                break;

            case WeatherState.FallingLeaves:
                CurrentLeafIntensity = 0.7f;
                cloudDensity = 0.4f;
                horizonFog = 0.3f;
                break;
        }

        // Feed MoodMachine
        if (MoodMachine.Instance != null)
            MoodMachine.Instance.SetSource("Weather", mood);

        // Push to NatureBox shader
        PushToNatureBox(cloudDensity, horizonFog, snowCap);

        // Nema comments on weather
        NemaWeatherDialogue.ReactToWeather(state);
    }

    private void PushToNatureBox(float cloudDensity, float horizonFog, float snowCap)
    {
        if (NatureBoxController.Instance == null) return;

        NatureBoxController.Instance.SetWeatherTargets(
            CurrentRainIntensity,
            CurrentSnowIntensity,
            CurrentLeafIntensity,
            CurrentOvercast,
            snowCap,
            cloudDensity,
            horizonFog
        );
    }

    // ── Persistence ───────────────────────────────────────────────

    public int GetStateForSave() => (int)CurrentWeather;

    public void LoadFromSave(int state)
    {
        if (state >= 0 && state <= 5)
            SetWeather((WeatherState)state);
    }
}
