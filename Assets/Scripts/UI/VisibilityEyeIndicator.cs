using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shows a fading eye icon (open/closed) next to items when they are placed
/// or when their visibility to the date changes (e.g. cubby door closes).
/// Icons are world-space sprites that always face the camera and fade out
/// over a few seconds. Rendered on top of everything so they show through doors.
/// </summary>
public class VisibilityEyeIndicator : MonoBehaviour
{
    public static VisibilityEyeIndicator Instance { get; private set; }

    [Header("Timing")]
    [Tooltip("Seconds before the icon starts fading.")]
    [SerializeField] private float _holdDuration = 1.0f;

    [Tooltip("Seconds for the fade-out.")]
    [SerializeField] private float _fadeDuration = 1.5f;

    [Header("Easing")]
    [Tooltip("Fade-in curve (0→1 over hold duration). X = time normalized, Y = alpha.")]
    [SerializeField] private AnimationCurve _fadeInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Fade-out curve (0→1 over fade duration). X = time normalized, Y = alpha (1=opaque, 0=gone).")]
    [SerializeField] private AnimationCurve _fadeOutCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("Layout")]
    [Tooltip("Offset from the item's center (world units).")]
    [SerializeField] private Vector3 _offset = new Vector3(0.15f, 0.15f, 0f);

    [Tooltip("World-space size of the icon.")]
    [SerializeField] private float _iconSize = 0.12f;

    [Header("Custom Icons (optional)")]
    [Tooltip("Custom open-eye sprite. If null, falls back to the procedural pixel art below.")]
    [SerializeField] private Sprite _openEyeSpriteOverride;

    [Tooltip("Custom closed-eye sprite. If null, falls back to the procedural pixel art below.")]
    [SerializeField] private Sprite _closedEyeSpriteOverride;

    // ── Runtime ──
    private Texture2D _openEyeTex;
    private Texture2D _closedEyeTex;
    private Sprite _openEyeSprite;
    private Sprite _closedEyeSprite;
    private bool _ownsOpenSprite;
    private bool _ownsClosedSprite;
    private Camera _cam;

    private readonly List<IconInstance> _active = new();
    private readonly Queue<IconInstance> _pool = new();

    private struct IconInstance
    {
        public GameObject go;
        public SpriteRenderer sr;
        public Transform target;
        public float timer;
        public float totalLife;
    }

    /// <summary>Auto-spawn if not present in scene.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoSpawn()
    {
        if (Instance != null) return;
        var go = new GameObject("VisibilityEyeIndicator");
        go.AddComponent<VisibilityEyeIndicator>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Use inspector-assigned sprites when set, otherwise fall back to the
        // built-in procedural pixel art. `_ownsXxxSprite` tracks whether we
        // allocated the sprite so OnDestroy only releases what it owns and
        // doesn't free a user asset.
        if (_openEyeSpriteOverride != null)
        {
            _openEyeSprite = _openEyeSpriteOverride;
            _ownsOpenSprite = false;
        }
        else
        {
            _openEyeTex = GenOpenEye();
            _openEyeSprite = Sprite.Create(_openEyeTex,
                new Rect(0, 0, _openEyeTex.width, _openEyeTex.height),
                new Vector2(0.5f, 0.5f), 128f);
            _ownsOpenSprite = true;
        }

        if (_closedEyeSpriteOverride != null)
        {
            _closedEyeSprite = _closedEyeSpriteOverride;
            _ownsClosedSprite = false;
        }
        else
        {
            _closedEyeTex = GenClosedEye();
            _closedEyeSprite = Sprite.Create(_closedEyeTex,
                new Rect(0, 0, _closedEyeTex.width, _closedEyeTex.height),
                new Vector2(0.5f, 0.5f), 128f);
            _ownsClosedSprite = true;
        }
    }

    private void OnEnable()
    {
        ObjectGrabber.OnObjectPlaced += OnItemPlaced;
        DrawerController.OnDrawerPrivacyChanged += OnDrawerChanged;
    }

    private void OnDisable()
    {
        ObjectGrabber.OnObjectPlaced -= OnItemPlaced;
        DrawerController.OnDrawerPrivacyChanged -= OnDrawerChanged;
    }

    private void Start()
    {
        // Remove auto-spawned duplicate if user placed one manually
        if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        // Only destroy what we allocated — never free a sprite dragged in
        // from the inspector, since that's a shared project asset.
        if (_ownsOpenSprite && _openEyeSprite != null) Destroy(_openEyeSprite);
        if (_ownsClosedSprite && _closedEyeSprite != null) Destroy(_closedEyeSprite);
        if (_openEyeTex != null) Destroy(_openEyeTex);
        if (_closedEyeTex != null) Destroy(_closedEyeTex);
    }

    /// <summary>
    /// Flash the open/closed eye on every active ReactableTag in the scene.
    /// Call once at exploration start to show the player what the date can see.
    /// </summary>
    public void FlashAllItems()
    {
        var all = ReactableTag.All;
        for (int i = 0; i < all.Count; i++)
        {
            var tag = all[i];
            if (tag == null || !tag.gameObject.activeInHierarchy) continue;
            if (!tag.IsActive) continue;
            ShowIcon(tag.transform, tag.IsPrivate);
        }
    }

    private void OnItemPlaced(PlaceableObject placed)
    {
        if (placed == null) return;

        // Only show for items that the date can react to
        var tag = placed.GetComponent<ReactableTag>();
        if (tag == null) return;

        ShowIcon(placed.transform, tag.IsPrivate);
    }

    private void OnDrawerChanged(DrawerController drawer, bool isClosed, PlaceableObject[] items)
    {
        if (isClosed && items.Length > 0)
        {
            // Items are hidden — show closed eye on the drawer itself
            ShowIcon(drawer.transform, true);
        }
        else
        {
            // Door opened — show open eye on each revealed item
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] != null && items[i].gameObject.activeInHierarchy)
                    ShowIcon(items[i].transform, false);
            }
        }
    }

    private void ShowIcon(Transform target, bool isPrivate)
    {
        // Recycle any existing icon for the same target
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            if (_active[i].target == target)
            {
                _active[i].go.SetActive(false);
                _pool.Enqueue(_active[i]);
                _active.RemoveAt(i);
            }
        }

        var icon = GetOrCreateIcon();
        icon.target = target;
        icon.timer = 0f;
        icon.totalLife = _holdDuration + _fadeDuration;
        icon.sr.sprite = isPrivate ? _closedEyeSprite : _openEyeSprite;
        icon.sr.color = Color.white;
        icon.go.SetActive(true);

        _active.Add(icon);
    }

    private void LateUpdate()
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        float dt = Time.unscaledDeltaTime;

        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var icon = _active[i];
            icon.timer += dt;

            if (icon.timer >= icon.totalLife || icon.target == null)
            {
                icon.go.SetActive(false);
                _pool.Enqueue(icon);
                _active.RemoveAt(i);
                continue;
            }

            // Position: offset from target, billboard toward camera
            Vector3 camRight = _cam.transform.right;
            Vector3 camUp = _cam.transform.up;
            icon.go.transform.position = icon.target.position
                + camRight * _offset.x + camUp * _offset.y + _cam.transform.forward * _offset.z;
            icon.go.transform.rotation = _cam.transform.rotation;
            icon.go.transform.localScale = Vector3.one * _iconSize;

            // Fade: curve-driven fade-in during hold, curve-driven fade-out after
            float alpha;
            if (icon.timer <= _holdDuration)
            {
                // Fade-in phase
                float t = _holdDuration > 0f ? Mathf.Clamp01(icon.timer / _holdDuration) : 1f;
                alpha = _fadeInCurve.Evaluate(t);
            }
            else
            {
                // Fade-out phase
                float t = _fadeDuration > 0f ? Mathf.Clamp01((icon.timer - _holdDuration) / _fadeDuration) : 1f;
                alpha = _fadeOutCurve.Evaluate(t);
            }
            var c = icon.sr.color;
            c.a = alpha;
            icon.sr.color = c;

            _active[i] = icon;
        }
    }

    private IconInstance GetOrCreateIcon()
    {
        if (_pool.Count > 0)
            return _pool.Dequeue();

        var go = new GameObject("VisEyeIcon");
        go.transform.SetParent(transform);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 999;
        // Use the Iris/OverlaySprite shader which hard-codes ZTest Always and
        // Overlay queue so the eye icons draw ON TOP of every other scene
        // geometry (including closed cubby doors). The old Sprites/Default
        // shader used ZTest LEqual, which meant the eye got clipped by the
        // closed cubby it was trying to indicate.
        var overlayShader = Shader.Find("Iris/OverlaySprite");
        if (overlayShader == null) overlayShader = Shader.Find("Sprites/Default");
        sr.material = new Material(overlayShader);
        sr.material.renderQueue = 4000;
        go.SetActive(false);

        return new IconInstance { go = go, sr = sr };
    }

    // ── Procedural eye textures (16x16 pixel art) ──

    private static Texture2D GenOpenEye()
    {
        const int S = 16;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var px = new Color32[S * S];

        var white = new Color32(240, 240, 235, 255);
        var outline = new Color32(60, 55, 50, 255);
        var iris = new Color32(90, 130, 90, 255);
        var pupil = new Color32(30, 25, 20, 255);

        // Eye outline (almond shape)
        // Row 6-9: the eye body
        for (int x = 3; x <= 12; x++) Set(px, S, x, 6, outline);
        for (int x = 2; x <= 13; x++) Set(px, S, x, 7, white);
        for (int x = 2; x <= 13; x++) Set(px, S, x, 8, white);
        for (int x = 3; x <= 12; x++) Set(px, S, x, 9, outline);

        // Corners
        Set(px, S, 2, 7, outline); Set(px, S, 13, 7, outline);
        Set(px, S, 2, 8, outline); Set(px, S, 13, 8, outline);
        Set(px, S, 1, 7, outline); Set(px, S, 14, 7, outline);
        Set(px, S, 1, 8, outline); Set(px, S, 14, 8, outline);

        // Iris (center)
        for (int y = 6; y <= 9; y++)
            for (int x = 6; x <= 9; x++)
                Set(px, S, x, y, iris);

        // Pupil (center of iris)
        Set(px, S, 7, 7, pupil); Set(px, S, 8, 7, pupil);
        Set(px, S, 7, 8, pupil); Set(px, S, 8, 8, pupil);

        // Highlight
        Set(px, S, 9, 6, new Color32(255, 255, 255, 200));

        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    private static Texture2D GenClosedEye()
    {
        const int S = 16;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var px = new Color32[S * S];

        var outline = new Color32(200, 190, 180, 255);  // bright cream so it's visible
        var slash = new Color32(220, 80, 70, 255);       // red slash

        // Closed eye — thick horizontal line
        for (int x = 1; x <= 14; x++) { Set(px, S, x, 7, outline); Set(px, S, x, 8, outline); }

        // Downward curve at edges
        Set(px, S, 1, 6, outline); Set(px, S, 2, 6, outline);
        Set(px, S, 13, 6, outline); Set(px, S, 14, 6, outline);

        // Eyelashes (thicker, below the line)
        Set(px, S, 3, 6, outline); Set(px, S, 4, 5, outline); Set(px, S, 5, 5, outline);
        Set(px, S, 7, 5, outline); Set(px, S, 8, 5, outline);
        Set(px, S, 10, 5, outline); Set(px, S, 11, 5, outline); Set(px, S, 12, 6, outline);

        // Diagonal slash (top-left to bottom-right)
        for (int i = 0; i < 12; i++)
        {
            int x = 2 + i;
            int y = 12 - i;
            Set(px, S, x, y, slash);
            Set(px, S, x, y - 1, slash);
        }

        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    private static void Set(Color32[] px, int w, int x, int y, Color32 c)
    {
        if (x >= 0 && x < w && y >= 0 && y < w)
            px[y * w + x] = c;
    }
}
