using UnityEngine;
using TMPro;

/// <summary>
/// Shows persistent hotkey hints at the bottom of the screen.
/// Auto-spawns via RuntimeInitializeOnLoadMethod.
/// </summary>
public class HotkeyHints : MonoBehaviour
{
    private static HotkeyHints s_instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoSpawn()
    {
        if (s_instance != null) return;
        var go = new GameObject("HotkeyHints");
        DontDestroyOnLoad(go);
        go.AddComponent<HotkeyHints>();
    }

    private void Awake()
    {
        if (s_instance != null && s_instance != this)
        {
            Destroy(gameObject);
            return;
        }
        s_instance = this;
        BuildUI();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (s_instance == this) s_instance = null;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    {
        // Only show hints in the apartment scene, not the main menu
        var root = transform.GetChild(0);
        if (root != null) root.gameObject.SetActive(s.buildIndex != 0);
    }

    private void BuildUI()
    {
        var canvasGO = new GameObject("HotkeyHintsCanvas");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;

        var scaler = canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var textGO = new GameObject("HintText");
        textGO.transform.SetParent(canvasGO.transform, false);
        var rt = textGO.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 10f);
        rt.sizeDelta = new Vector2(600f, 30f);

        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text = $"F8 Feedback  |  F9 Bug Report  |  {GameVersion.Display}";
        tmp.fontSize = 16f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(1f, 1f, 1f, 0.3f);
        tmp.raycastTarget = false;
    }
}
