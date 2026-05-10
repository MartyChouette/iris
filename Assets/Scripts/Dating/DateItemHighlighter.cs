using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Toggleable item highlighter that shows green rims on items the date likes
/// and red rims on items they dislike. Numpad 3 toggles.
/// Re-scans ReactableTag.All every 0.5s when active.
/// Clears on phase change or toggle off.
/// </summary>
public class DateItemHighlighter : MonoBehaviour
{
    public static DateItemHighlighter Instance { get; private set; }

    private InputAction _toggleAction;
    private bool _active;
    private float _rescanTimer;
    private DatePersonalDefinition _currentDate;

    // Track which highlights we applied so we can clear them
    private readonly List<ItemHighlight> _likedHighlights = new List<ItemHighlight>();
    private readonly List<ItemHighlight> _dislikedHighlights = new List<ItemHighlight>();

    private const float RescanInterval = 0.5f;

    // ─── Lifecycle ───────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _toggleAction = new InputAction("ToggleHighlighter", InputActionType.Button,
            "<Keyboard>/numpad3");
    }

    private void OnEnable()
    {
        _toggleAction.Enable();

        if (NewspaperManager.Instance != null)
            NewspaperManager.Instance.OnDateSelected.AddListener(OnDateSelected);
    }

    private void OnDisable()
    {
        _toggleAction.Disable();
        ClearAllHighlights();

        if (NewspaperManager.Instance != null)
            NewspaperManager.Instance.OnDateSelected.RemoveListener(OnDateSelected);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        if (DayPhaseManager.Instance != null)
            DayPhaseManager.Instance.OnPhaseChanged.AddListener(OnPhaseChanged);
    }

    private void Update()
    {
        if (_toggleAction.WasPressedThisFrame() && _currentDate != null)
        {
            _active = !_active;
            if (_active)
                ApplyHighlights();
            else
                ClearAllHighlights();
        }

        if (!_active) return;

        _rescanTimer -= Time.deltaTime;
        if (_rescanTimer <= 0f)
        {
            _rescanTimer = RescanInterval;
            ApplyHighlights();
        }
    }

    // ─── Events ──────────────────────────────────────────────────

    private void OnDateSelected(DatePersonalDefinition def)
    {
        _currentDate = def;
        if (_active)
            ApplyHighlights();
    }

    private void OnPhaseChanged(int phase)
    {
        if (phase == (int)DayPhaseManager.DayPhase.DateInProgress ||
            phase == (int)DayPhaseManager.DayPhase.Morning)
        {
            _active = false;
            ClearAllHighlights();
            _currentDate = null;
        }
    }

    // ─── Core ────────────────────────────────────────────────────

    private void ApplyHighlights()
    {
        ClearAllHighlights();

        if (_currentDate == null || _currentDate.preferences == null) return;

        var prefs = _currentDate.preferences;
        var allReactables = ReactableTag.All;

        for (int i = 0; i < allReactables.Count; i++)
        {
            var reactable = allReactables[i];
            var highlight = reactable.GetComponent<ItemHighlight>();
            if (highlight == null) continue;

            var tags = reactable.Tags;
            if (tags == null) continue;

            bool isLiked = false;
            bool isDisliked = false;

            for (int t = 0; t < tags.Length; t++)
            {
                if (ArrayContains(prefs.likedTags, tags[t]))
                    isLiked = true;
                if (ArrayContains(prefs.dislikedTags, tags[t]))
                    isDisliked = true;
            }

            // Dislike takes priority if both matched
            if (isDisliked)
            {
                highlight.SetPrepDislikedHighlighted(true);
                _dislikedHighlights.Add(highlight);
            }
            else if (isLiked)
            {
                highlight.SetPrepLikedHighlighted(true);
                _likedHighlights.Add(highlight);
            }
        }
    }

    private void ClearAllHighlights()
    {
        for (int i = 0; i < _likedHighlights.Count; i++)
        {
            if (_likedHighlights[i] != null)
                _likedHighlights[i].SetPrepLikedHighlighted(false);
        }
        _likedHighlights.Clear();

        for (int i = 0; i < _dislikedHighlights.Count; i++)
        {
            if (_dislikedHighlights[i] != null)
                _dislikedHighlights[i].SetPrepDislikedHighlighted(false);
        }
        _dislikedHighlights.Clear();
    }

    private static bool ArrayContains(string[] arr, string value)
    {
        if (arr == null) return false;
        for (int i = 0; i < arr.Length; i++)
        {
            if (arr[i] == value) return true;
        }
        return false;
    }
}
