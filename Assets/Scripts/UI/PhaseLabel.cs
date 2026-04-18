using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Bottom-left label showing the current game mode: Cleaning, Dating, Trimming.
/// Auto-spawns and listens to DayPhaseManager.OnPhaseChanged.
/// </summary>
public class PhaseLabel : MonoBehaviour
{
    private TMP_Text _label;
    private GameObject _root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoSpawn()
    {
        var go = new GameObject("[PhaseLabel]");
        go.AddComponent<PhaseLabel>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        BuildUI();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    {
        SetText("");
    }

    private void OnEnable()
    {
        if (DayPhaseManager.Instance != null)
            DayPhaseManager.Instance.OnPhaseChanged.AddListener(OnPhaseChanged);
    }

    private void OnDisable()
    {
        if (DayPhaseManager.Instance != null)
            DayPhaseManager.Instance.OnPhaseChanged.RemoveListener(OnPhaseChanged);
    }

    private void Start()
    {
        // Catch the current phase on startup
        if (DayPhaseManager.Instance != null)
            OnPhaseChanged((int)DayPhaseManager.Instance.CurrentPhase);
        else
            SetText("");
    }

    private void OnPhaseChanged(int phaseInt)
    {
        var phase = (DayPhaseManager.DayPhase)phaseInt;
        switch (phase)
        {
            case DayPhaseManager.DayPhase.Exploration:
            case DayPhaseManager.DayPhase.Evening:
                SetText("Cleaning");
                break;
            case DayPhaseManager.DayPhase.DateInProgress:
                SetText("Dating");
                break;
            case DayPhaseManager.DayPhase.FlowerTrimming:
                SetText("Trimming");
                break;
            default:
                SetText("");
                break;
        }
    }

    private void SetText(string text)
    {
        if (_root != null)
            _root.SetActive(!string.IsNullOrEmpty(text));
        if (_label != null)
            _label.text = text;
    }

    private void BuildUI()
    {
        _root = new GameObject("PhaseLabelCanvas");
        _root.transform.SetParent(transform, false);

        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(_root.transform, false);

        var rt = labelGO.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(20f, 12f);
        rt.sizeDelta = new Vector2(200f, 30f);

        _label = labelGO.AddComponent<TextMeshProUGUI>();
        _label.fontSize = 16f;
        _label.fontStyle = FontStyles.Bold;
        _label.alignment = TextAlignmentOptions.Left;
        _label.color = new Color(0.75f, 0.72f, 0.68f, 0.6f);
        _label.raycastTarget = false;

        var theme = IrisTextTheme.Active;
        if (theme != null && theme.primaryFont != null)
            _label.font = theme.primaryFont;

        _root.SetActive(false);
    }
}
