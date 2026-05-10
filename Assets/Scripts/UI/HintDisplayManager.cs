using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Drives the hint UI panel. Wire up the TMP_Text references from the
/// UI_Mockup hierarchy and populate the hints list with HintDefinition SOs.
/// Call SetPhase() to swap text, or it auto-detects from DayPhaseManager.
/// </summary>
[ExecuteAlways]
public class HintDisplayManager : MonoBehaviour
{
    public static HintDisplayManager Instance { get; private set; }

    [Header("UI References")]
    [Tooltip("TMP_Text that shows the control hints (LMB, RMB, etc.)")]
    [SerializeField] private TMP_Text _hintContentText;

    [Tooltip("TMP_Text that shows the contextual prompt.")]
    [SerializeField] private TMP_Text _hintPromptText;

    [Tooltip("Root GameObject of the hint panel (toggled on/off).")]
    [SerializeField] private GameObject _hintPanel;

    [Header("Hints")]
    [Tooltip("List of hint definitions. One per game phase.")]
    [SerializeField] private HintDefinition[] _hints;

    private HintDefinition.GamePhase _currentPhase;
    private bool _visible = true;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        // Scene setup: _hintPanel should be a UI Panel with an Image (background)
        // and the two TMP_Text children. Set it up in the editor — no runtime creation.
    }

    private void OnEnable()
    {
        // Apply hint text immediately in editor (no play mode needed)
        ApplyFirstAvailableHint();
    }

    private void Start()
    {
        // Listen to day phase changes
        if (DayPhaseManager.Instance != null)
        {
            DayPhaseManager.Instance.OnPhaseChanged.AddListener(OnDayPhaseChanged);
            _subscribedToPhaseManager = true;
        }

        // Set initial hint
        UpdateHintFromGameState();
    }

    private void ApplyFirstAvailableHint()
    {
        // Show the first hint immediately so text is visible in editor
        if (_hints != null && _hints.Length > 0)
        {
            var hint = FindHint(HintDefinition.GamePhase.RoomArrangement) ?? _hints[0];
            if (hint != null)
            {
                if (_hintContentText != null) _hintContentText.text = hint.hintContent;
                if (_hintPromptText != null) _hintPromptText.text = hint.hintPrompt;
            }
        }
    }

    private void OnDestroy()
    {
        if (DayPhaseManager.Instance != null)
            DayPhaseManager.Instance.OnPhaseChanged.RemoveListener(OnDayPhaseChanged);
        if (Instance == this) Instance = null;
    }

    private HintDefinition.GamePhase _lastApplied = (HintDefinition.GamePhase)(-1);
    private bool _subscribedToPhaseManager;

    private void Update()
    {
        if (!Application.isPlaying) return;

        // Late-bind to DayPhaseManager if it wasn't ready at Start
        if (!_subscribedToPhaseManager && DayPhaseManager.Instance != null)
        {
            DayPhaseManager.Instance.OnPhaseChanged.AddListener(OnDayPhaseChanged);
            _subscribedToPhaseManager = true;
            UpdateHintFromGameState();
        }

        // Poll game state every frame to catch date sub-phase transitions
        HintDefinition.GamePhase target = GetTargetPhase();
        if (target != _lastApplied)
        {
            SetPhase(target);
            _lastApplied = target;
        }
    }

    private HintDefinition.GamePhase GetTargetPhase()
    {
        var dayPhase = DayPhaseManager.Instance != null
            ? DayPhaseManager.Instance.CurrentPhase
            : DayPhaseManager.DayPhase.Exploration;

        if (dayPhase == DayPhaseManager.DayPhase.FlowerTrimming)
            return HintDefinition.GamePhase.FlowerArrangement;

        var dateMgr = DateSessionManager.Instance;
        if (dateMgr != null && dateMgr.CurrentDatePhase != DateSessionManager.DatePhase.None)
        {
            return dateMgr.CurrentDatePhase switch
            {
                DateSessionManager.DatePhase.BackgroundJudging => HintDefinition.GamePhase.DrinkMaking,
                DateSessionManager.DatePhase.Reveal => HintDefinition.GamePhase.WarmUp,
                _ => HintDefinition.GamePhase.RoomArrangement
            };
        }

        return HintDefinition.GamePhase.RoomArrangement;
    }

    private void OnDayPhaseChanged(int phaseInt)
    {
        _lastApplied = (HintDefinition.GamePhase)(-1);
        UpdateHintFromGameState();
    }

    private void UpdateHintFromGameState()
    {
        var dayPhase = DayPhaseManager.Instance != null
            ? DayPhaseManager.Instance.CurrentPhase
            : DayPhaseManager.DayPhase.Exploration;

        if (dayPhase == DayPhaseManager.DayPhase.FlowerTrimming)
        {
            SetPhase(HintDefinition.GamePhase.FlowerArrangement);
            return;
        }

        // During a date, check which date sub-phase we're in
        var dateMgr = DateSessionManager.Instance;
        if (dateMgr != null && dateMgr.CurrentDatePhase != DateSessionManager.DatePhase.None)
        {
            switch (dateMgr.CurrentDatePhase)
            {
                case DateSessionManager.DatePhase.BackgroundJudging:
                    SetPhase(HintDefinition.GamePhase.DrinkMaking);
                    return;
                case DateSessionManager.DatePhase.Reveal:
                    SetPhase(HintDefinition.GamePhase.WarmUp);
                    return;
            }
        }

        // Default: room arrangement
        SetPhase(HintDefinition.GamePhase.RoomArrangement);
    }

    /// <summary>Switch to a specific phase's hints.</summary>
    public void SetPhase(HintDefinition.GamePhase phase)
    {
        _currentPhase = phase;
        ApplyHint(FindHint(phase));
    }

    /// <summary>Show or hide the entire hint panel.</summary>
    public void SetVisible(bool visible)
    {
        _visible = visible;
        if (_hintPanel != null)
            _hintPanel.SetActive(visible);
    }

    /// <summary>Toggle hint panel visibility.</summary>
    public void Toggle()
    {
        SetVisible(!_visible);
    }

    /// <summary>Override both texts directly (for one-off hints).</summary>
    public void SetCustom(string content, string prompt)
    {
        if (_hintContentText != null) _hintContentText.text = content;
        if (_hintPromptText != null) _hintPromptText.text = prompt;
        SetVisible(true);
    }

    private HintDefinition FindHint(HintDefinition.GamePhase phase)
    {
        if (_hints == null) return null;
        for (int i = 0; i < _hints.Length; i++)
        {
            if (_hints[i] != null && _hints[i].phase == phase)
                return _hints[i];
        }
        return null;
    }

    private void ApplyHint(HintDefinition hint)
    {
        if (hint == null)
        {
            Debug.LogWarning($"[HintDisplayManager] No hint found for phase {_currentPhase}");
            if (_hintContentText != null) _hintContentText.text = "";
            if (_hintPromptText != null) _hintPromptText.text = "";
            return;
        }

        Debug.Log($"[HintDisplayManager] Applying hint: {hint.name} ({hint.phase})");
        if (_hintContentText != null) _hintContentText.text = hint.hintContent;
        if (_hintPromptText != null) _hintPromptText.text = hint.hintPrompt;
        SetVisible(true);
    }
}
