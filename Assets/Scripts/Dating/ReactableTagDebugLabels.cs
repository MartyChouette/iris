using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// Debug overlay that shows floating labels above all active ReactableTags.
/// Toggle with L key. During a date, labels are color-coded:
/// green = liked/loved, red = disliked, white = neutral.
/// </summary>
public class ReactableTagDebugLabels : MonoBehaviour
{
    public static ReactableTagDebugLabels Instance { get; private set; }

    private InputAction _toggleAction;
    private bool _visible;
    private readonly List<GameObject> _labelGOs = new List<GameObject>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        _toggleAction = new InputAction("ToggleDebugLabels", InputActionType.Button,
            "<Keyboard>/l");
    }

    private void OnEnable() => _toggleAction?.Enable();
    private void OnDisable() => _toggleAction?.Disable();

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        _toggleAction?.Dispose();
    }

    private void Update()
    {
        if (_toggleAction != null && _toggleAction.WasPressedThisFrame())
        {
            _visible = !_visible;
            if (_visible)
                CreateLabels();
            else
                DestroyLabels();
        }

        if (_visible)
            UpdateLabels();
    }

    private void CreateLabels()
    {
        DestroyLabels();

        var prefs = GetCurrentDatePreferences();

        foreach (var tag in ReactableTag.All)
        {
            if (tag == null) continue;

            var labelGO = CreateWorldLabel(tag, prefs);
            _labelGOs.Add(labelGO);
        }

        Debug.Log($"[ReactableTagDebugLabels] Showing {_labelGOs.Count} labels.");
    }

    private void DestroyLabels()
    {
        foreach (var go in _labelGOs)
        {
            if (go != null) Destroy(go);
        }
        _labelGOs.Clear();
    }

    private Camera _cachedCam;

    private void UpdateLabels()
    {
        if (_cachedCam == null) _cachedCam = Camera.main;
        var cam = _cachedCam;
        if (cam == null) return;

        for (int i = _labelGOs.Count - 1; i >= 0; i--)
        {
            var go = _labelGOs[i];
            if (go == null) { _labelGOs.RemoveAt(i); continue; }

            // Billboard toward camera
            go.transform.rotation = Quaternion.LookRotation(
                go.transform.position - cam.transform.position, Vector3.up);
        }
    }

    private GameObject CreateWorldLabel(ReactableTag tag, DatePreferences prefs)
    {
        string privacy = tag.IsPrivate ? "[Private]" : "[Public]";
        string activeStr = tag.IsActive ? "" : " (Inactive)";
        string text = $"{privacy}{activeStr} {string.Join(", ", tag.Tags)}";
        Color color = tag.IsPrivate ? new Color(0.6f, 0.6f, 0.6f) : GetTagColor(tag, prefs);

        var pivot = new GameObject($"DebugLabel_{tag.gameObject.name}");
        pivot.transform.position = tag.transform.position + Vector3.up * 0.3f;

        var canvasGO = new GameObject("Canvas");
        canvasGO.transform.SetParent(pivot.transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = canvasGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(400f, 50f);
        canvasGO.transform.localScale = Vector3.one * 0.005f;

        var textGO = new GameObject("Text");
        textGO.transform.SetParent(canvasGO.transform, false);
        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 22f;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        return pivot;
    }

    private Color GetTagColor(ReactableTag tag, DatePreferences prefs)
    {
        if (prefs == null) return Color.white;

        foreach (string t in tag.Tags)
        {
            foreach (string liked in prefs.likedTags)
            {
                if (t == liked) return Color.green;
            }
        }
        foreach (string t in tag.Tags)
        {
            foreach (string disliked in prefs.dislikedTags)
            {
                if (t == disliked) return Color.red;
            }
        }

        return Color.white;
    }

    private DatePreferences GetCurrentDatePreferences()
    {
        if (DateSessionManager.Instance == null) return null;
        var date = DateSessionManager.Instance.CurrentDate;
        return date != null ? date.preferences : null;
    }
}
