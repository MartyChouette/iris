using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-space UI cursor that replaces the hardware cursor during pouring.
/// Rotates smoothly based on PourDragHelper.TiltAngle to show the pour tilt.
/// Auto-spawns its own overlay canvas.
/// </summary>
public class PourCursorOverlay : MonoBehaviour
{
    public static PourCursorOverlay Instance { get; private set; }

    [Header("Scale")]
    [Tooltip("Cursor size when not pouring (just clicked, PourRate=0).")]
    [SerializeField] private float _minSize = 24f;

    [Tooltip("Cursor size at max pour (PourRate=1).")]
    [SerializeField] private float _maxSize = 48f;

    private Canvas _canvas;
    private RawImage _cursorImage;
    private RectTransform _cursorRT;
    private Texture2D _lockedTexture;
    private bool _active;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoSpawn()
    {
        if (Instance != null) return;
        var go = new GameObject("PourCursorOverlay");
        go.AddComponent<PourCursorOverlay>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void BuildUI()
    {
        // Overlay canvas on top of everything
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 200;

        // No raycaster — this is purely visual, shouldn't block clicks

        var imgGO = new GameObject("PourCursor");
        imgGO.transform.SetParent(transform, false);
        _cursorImage = imgGO.AddComponent<RawImage>();
        _cursorRT = imgGO.GetComponent<RectTransform>();
        _cursorRT.sizeDelta = new Vector2(32f, 32f);
        _cursorRT.pivot = new Vector2(0.5f, 0.5f);

        _cursorImage.raycastTarget = false;
        imgGO.SetActive(false);
    }

    /// <summary>
    /// Set the cursor texture for the pour overlay before dragging starts.
    /// Call this when the player clicks a pourable item so the pour cursor
    /// matches the hover cursor seamlessly.
    /// </summary>
    public void LockTexture(Texture2D tex)
    {
        _lockedTexture = tex;
    }

    private void LateUpdate()
    {
        bool shouldShow = PourDragHelper.IsDragging;

        if (shouldShow && !_active)
        {
            // Use the locked texture (set before drag began) so it matches the hover cursor.
            // Fall back to GlobalCursorManager's current texture if nothing was locked.
            Texture2D tex = _lockedTexture;
            if (tex == null)
            {
                var gcm = GlobalCursorManager.Instance;
                tex = gcm != null ? gcm.GetCurrentCursorTexture() : null;
            }

            if (tex != null)
                _cursorImage.texture = tex;

            _cursorRT.sizeDelta = new Vector2(_minSize, _minSize);
            Cursor.visible = false;
            _cursorImage.gameObject.SetActive(true);
            _active = true;
        }
        else if (!shouldShow && _active)
        {
            Cursor.visible = true;
            _cursorImage.gameObject.SetActive(false);
            _lockedTexture = null;
            _active = false;
        }

        if (!_active) return;

        // Lock the visual cursor to the click origin so the icon stays put
        // while the player drags downward to control pour rate. The OS cursor
        // is hidden during drag, so the player only sees this anchored icon.
        _cursorRT.position = PourDragHelper.ClickOrigin;

        // Rotate: tilt clockwise as player drags down
        float angle = -PourDragHelper.TiltAngle;
        _cursorRT.localRotation = Quaternion.Euler(0f, 0f, angle);

        // Scale with pour intensity — bigger = pouring harder
        float size = Mathf.Lerp(_minSize, _maxSize, PourDragHelper.PourRate);
        _cursorRT.sizeDelta = new Vector2(size, size);

        // Fade alpha to match the cursor manager
        float alpha = 1f;
        if (GlobalCursorManager.Instance != null)
            alpha = GlobalCursorManager.Instance.CurrentAlpha;
        _cursorImage.color = new Color(1f, 1f, 1f, alpha);
    }
}
