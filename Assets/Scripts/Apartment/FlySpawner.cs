using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Spawns flies near smelly items. Handles click-to-swat detection.
/// Scene-scoped singleton — lives in the apartment scene.
/// Flies spawn when items have SmellAmount >= threshold (1-2 per item, 5 max).
/// </summary>
public class FlySpawner : MonoBehaviour
{
    public static FlySpawner Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
    }

    private static readonly RaycastHit[] s_swatHitBuffer = new RaycastHit[8];

    [Header("Spawning")]
    [Tooltip("Minimum SmellAmount on a ReactableTag before flies appear.")]
    [SerializeField] private float _smellThreshold = 0.3f;

    [Tooltip("Max flies per smelly item.")]
    [SerializeField] private int _fliesPerItem = 2;

    [Tooltip("Max total flies in the scene.")]
    [SerializeField] private int _maxFlies = 5;

    [Tooltip("Seconds between spawn checks.")]
    [SerializeField] private float _spawnInterval = 3f;

    [Header("Detection")]
    [Tooltip("Layer mask for fly click detection.")]
    [SerializeField] private LayerMask _clickMask = ~0;

    private float _spawnTimer;
    private readonly Dictionary<ReactableTag, int> _fliesPerSource = new();
    private Camera _cam;
    private float _camTimer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoSpawn()
    {
        if (Instance != null) return;
        var go = new GameObject("[FlySpawner]");
        go.AddComponent<FlySpawner>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    {
        // Clear stale per-scene state on any scene change so flies from a previous
        // scene's items don't linger in the registry.
        _fliesPerSource.Clear();
        _cam = null;
    }

    private void Update()
    {
        // Don't spawn during trimming or menu
        if (!IsActivePhase()) return;

        // Spawn check
        _spawnTimer -= Time.deltaTime;
        if (_spawnTimer <= 0f)
        {
            _spawnTimer = _spawnInterval;
            TrySpawnFlies();
        }

        // Click-to-swat detection
        if (IrisInput.Instance != null && IrisInput.Instance.Click.WasPressedThisFrame())
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            // Don't swat while holding an object
            if (ObjectGrabber.IsHoldingObject) return;

            TrySwatFly();
        }
    }

    private void TrySwatFly()
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        Vector2 screenPos = IrisInput.CursorPosition;
        Ray ray = ApartmentManager.Instance != null
            ? ApartmentManager.Instance.ScreenPointToRay(screenPos)
            : _cam.ScreenPointToRay(screenPos);

        // Check all hits — fly might be behind another collider
        int hitCount = Physics.RaycastNonAlloc(ray, s_swatHitBuffer, 100f, _clickMask);
        float closestDist = float.MaxValue;
        FlyController closestFly = null;

        for (int i = 0; i < hitCount; i++)
        {
            var fly = s_swatHitBuffer[i].collider.GetComponent<FlyController>();
            if (fly == null) continue;
            if (s_swatHitBuffer[i].distance < closestDist)
            {
                closestDist = s_swatHitBuffer[i].distance;
                closestFly = fly;
            }
        }

        if (closestFly != null)
        {
            closestFly.Swat();
            ObjectGrabber.ConsumeClickExternal();
        }
    }

    private static readonly List<ReactableTag> s_deadKeys = new();

    private void TrySpawnFlies()
    {
        // Clean up dead entries (reuse static list to avoid allocation)
        s_deadKeys.Clear();
        foreach (var kvp in _fliesPerSource)
            if (kvp.Key == null) s_deadKeys.Add(kvp.Key);
        for (int i = 0; i < s_deadKeys.Count; i++)
            _fliesPerSource.Remove(s_deadKeys[i]);

        int totalFlies = FlyController.All.Count;
        if (totalFlies >= _maxFlies) return;

        var allTags = ReactableTag.All;
        for (int i = 0; i < allTags.Count; i++)
        {
            if (totalFlies >= _maxFlies) break;

            var tag = allTags[i];
            if (tag == null || !tag.gameObject.activeInHierarchy) continue;
            if (tag.SmellAmount < _smellThreshold) continue;

            // Skip flies themselves
            if (tag.GetComponent<FlyController>() != null) continue;

            // Count existing flies for this source
            _fliesPerSource.TryGetValue(tag, out int existing);
            if (existing >= _fliesPerItem) continue;

            // Spawn a fly
            var flyGO = new GameObject($"Fly_{tag.name}");
            var fly = flyGO.AddComponent<FlyController>();
            fly.Init(tag.transform);

            _fliesPerSource[tag] = existing + 1;
            totalFlies++;
        }
    }

    private bool IsActivePhase()
    {
        if (DayPhaseManager.Instance == null) return false;
        var phase = DayPhaseManager.Instance.CurrentPhase;
        return phase == DayPhaseManager.DayPhase.Exploration
            || phase == DayPhaseManager.DayPhase.DateInProgress;
    }
}
