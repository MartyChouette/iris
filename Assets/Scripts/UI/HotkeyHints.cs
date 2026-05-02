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

        // Background panel
        var bgGO = new GameObject("HintBG");
        bgGO.transform.SetParent(canvasGO.transform, false);
        var bgRT = bgGO.AddComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0.5f, 0f);
        bgRT.anchorMax = new Vector2(0.5f, 0f);
        bgRT.pivot = new Vector2(0.5f, 0f);
        bgRT.anchoredPosition = new Vector2(0f, 6f);
        bgRT.sizeDelta = new Vector2(620f, 34f);
        var bgImg = bgGO.AddComponent<UnityEngine.UI.Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.35f);
        bgImg.raycastTarget = false;

        var textGO = new GameObject("HintText");
        textGO.transform.SetParent(bgGO.transform, false);
        var rt = textGO.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(10f, 2f);
        rt.offsetMax = new Vector2(-10f, -2f);

        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text = $"F8 Feedback  |  F9 Bug Report  |  {GameVersion.Display}";
        tmp.fontSize = 16f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(1f, 1f, 1f, 0.4f);
        tmp.raycastTarget = false;
    }
}
