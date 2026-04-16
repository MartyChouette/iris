using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using TMPro;

[DisallowMultipleComponent]
public class PauseMenuController : MonoBehaviour
{
    [System.Serializable]
    public class PausePage
    {
        [Tooltip("Internal ID for this page (e.g. 'Records', 'Items', 'System').")]
        public string pageId;

        [Tooltip("Display name shown in the pause menu header.")]
        public string displayName;

        [Tooltip("Root GameObject for this page's UI.")]
        public GameObject root;
    }

    [Header("Root UI")]
    [Tooltip("Root object of the entire pause menu (usually a Canvas or panel).")]
    public GameObject pauseRoot;

    [Tooltip("Label showing the current page's display name.")]
    public TMP_Text pageTitleLabel;

    [Tooltip("Optional: label showing 'Page 1 / 3' style info.")]
    public TMP_Text pageIndexLabel;

    [Header("Pages (set up in Inspector)")]
    public PausePage[] pages;

    [Header("Options Subpanel")]
    [Tooltip("Optional options panel that can be shown from the System page.")]
    public GameObject optionsPanel;

    [Header("Debug")]
    public bool debugLogs = false;

    // Inline InputActions — no InputActionAsset required
    private InputAction _pauseAction;
    private InputAction _pageLeftAction;
    private InputAction _pageRightAction;

    private int _currentPageIndex;
    private bool _isPaused;

    public bool IsPaused => _isPaused;

    private void Awake()
    {
        _pauseAction = new InputAction("Pause", InputActionType.Button, "<Keyboard>/escape");
        _pageLeftAction = new InputAction("PageLeft", InputActionType.Button, "<Keyboard>/q");
        _pageRightAction = new InputAction("PageRight", InputActionType.Button, "<Keyboard>/e");
    }

    private void Start()
    {
        if (pauseRoot != null)
            pauseRoot.SetActive(false);

        if (optionsPanel != null)
            optionsPanel.SetActive(false);
    }

    private void OnEnable()
    {
        _pauseAction.Enable();
        _pageLeftAction.Enable();
        _pageRightAction.Enable();
    }

    private void OnDisable()
    {
        _pauseAction?.Disable();
        _pageLeftAction?.Disable();
        _pageRightAction?.Disable();
    }

    private void OnDestroy()
    {
        _pauseAction?.Dispose();
        _pageLeftAction?.Dispose();
        _pageRightAction?.Dispose();
        _pauseAction = null;
        _pageLeftAction = null;
        _pageRightAction = null;
    }

    private void Update()
    {
        if (_pauseAction.WasPressedThisFrame())
        {
            if (!_isPaused) OpenPauseMenu();
            else ClosePauseMenu();
        }

        if (!_isPaused) return;

        if (_pageLeftAction.WasPressedThisFrame())
            PreviousPage();
        if (_pageRightAction.WasPressedThisFrame())
            NextPage();
    }

    // ─────────────────── Pause open/close ───────────────────

    public void OpenPauseMenu()
    {
        if (_isPaused) return;

        _isPaused = true;
        TimeScaleManager.Set(TimeScaleManager.PRIORITY_PAUSE, 0f);

        if (pauseRoot != null)
            pauseRoot.SetActive(true);

        if (optionsPanel != null)
            optionsPanel.SetActive(false);

        SetPage(0);

        if (debugLogs)
            Debug.Log("[PauseMenuController] Pause menu opened.", this);
    }

    public void ClosePauseMenu()
    {
        if (!_isPaused) return;

        _isPaused = false;
        TimeScaleManager.Clear(TimeScaleManager.PRIORITY_PAUSE);

        if (pauseRoot != null)
            pauseRoot.SetActive(false);

        if (optionsPanel != null)
            optionsPanel.SetActive(false);

        if (debugLogs)
            Debug.Log("[PauseMenuController] Pause menu closed.", this);
    }

    public void UI_ClosePauseMenu() => ClosePauseMenu();

    // ─────────────────── Page switching ───────────────────

    public void NextPage()
    {
        if (pages == null || pages.Length == 0) return;
        SetPage((_currentPageIndex + 1) % pages.Length);
    }

    public void PreviousPage()
    {
        if (pages == null || pages.Length == 0) return;
        SetPage((_currentPageIndex - 1 + pages.Length) % pages.Length);
    }

    public void SetPage(int index)
    {
        if (pages == null || pages.Length == 0) return;

        index = Mathf.Clamp(index, 0, pages.Length - 1);
        _currentPageIndex = index;

        for (int i = 0; i < pages.Length; i++)
        {
            var page = pages[i];
            if (page == null || page.root == null) continue;
            page.root.SetActive(i == _currentPageIndex);
        }

        var current = pages[_currentPageIndex];

        if (pageTitleLabel != null)
            pageTitleLabel.text = current != null ? current.displayName : "";

        if (pageIndexLabel != null && pages.Length > 0)
            pageIndexLabel.text = $"{_currentPageIndex + 1} / {pages.Length}";

        if (debugLogs && current != null)
            Debug.Log($"[PauseMenuController] Switched to page '{current.pageId}' (index {_currentPageIndex}).", this);
    }

    public void UI_NextPage() => NextPage();
    public void UI_PreviousPage() => PreviousPage();

    // ─────────────────── System page actions ───────────────────

    public void RestartLevel()
    {
        if (debugLogs)
            Debug.Log("[PauseMenuController] RestartLevel called.", this);

        TimeScaleManager.ClearAll();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void GoToMainMenu(string mainMenuSceneName)
    {
        if (debugLogs)
            Debug.Log("[PauseMenuController] GoToMainMenu called → " + mainMenuSceneName, this);

        if (string.IsNullOrEmpty(mainMenuSceneName))
        {
            Debug.LogWarning("[PauseMenuController] GoToMainMenu called with empty scene name.", this);
            return;
        }

        StartCoroutine(QuitToMenuSequence(mainMenuSceneName));
    }

    private IEnumerator QuitToMenuSequence(string sceneName)
    {
        TimeScaleManager.ClearAll();

        AudioManager.Instance?.StopAmbience();
        AudioManager.Instance?.StopWeather();
        AudioManager.Instance?.StopMusic(0.8f);

        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeOut(0.8f);
        else
            yield return new WaitForSecondsRealtime(0.8f);

        MusicDirector.Instance?.PlayMenuSong();
        SceneManager.LoadScene(sceneName);
    }

    public void ShowOptionsPanel()
    {
        if (optionsPanel != null)
            optionsPanel.SetActive(true);
    }

    public void HideOptionsPanel()
    {
        if (optionsPanel != null)
            optionsPanel.SetActive(false);
    }

    public void QuitGame()
    {
        if (debugLogs)
            Debug.Log("[PauseMenuController] QuitGame called.", this);

        TimeScaleManager.ClearAll();
        GracefulQuit.Execute();
    }
}
