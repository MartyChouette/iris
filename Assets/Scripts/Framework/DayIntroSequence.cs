using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Cinemachine;

/// <summary>
/// Cinematic intro that plays at the start of every day:
///   1. Track shot through the apartment (perspective camera on waypoints)
///   2. "DAY N" title fills the screen
///   3. Camera turns around, pulls back
///   4. Fade to white
///   5. Paris description text over white
///   6. Fade out, resume normal morning flow
///
/// Hook: called by DayPhaseManager.MorningTransition() before anything else.
/// Requires serialized waypoint transforms placed in the scene.
/// </summary>
public class DayIntroSequence : MonoBehaviour
{
    public static DayIntroSequence Instance { get; private set; }

    [Header("Track Shot")]
    [Tooltip("Waypoints the camera travels through (position + rotation). First = start, last = end before turnaround.")]
    [SerializeField] private Transform[] _waypoints;

    [Tooltip("Camera speed in world units per second (constant along the entire path).")]
    [SerializeField] private float _trackSpeed = 2.0f;

    [Tooltip("Duration of the turnaround + pullback in seconds.")]
    [SerializeField] private float _pullbackDuration = 2.0f;

    [Tooltip("How far back the camera pulls after turning around (world units).")]
    [SerializeField] private float _pullbackDistance = 2.0f;

    [Header("Day Title")]
    [Tooltip("How long the DAY N title is fully visible.")]
    [SerializeField] private float _titleHoldDuration = 1.5f;

    [Tooltip("Title fade in/out duration.")]
    [SerializeField] private float _titleFadeDuration = 0.4f;

    [Header("Paris Description")]
    [Tooltip("Duration the Paris description is shown over white.")]
    [SerializeField] private float _descriptionHoldDuration = 4.0f;

    [Header("Fade Timing")]
    [Tooltip("Duration of fade to white after pullback.")]
    [SerializeField] private float _fadeToWhiteDuration = 0.8f;

    [Tooltip("Duration of fade out from white (back to black/clear) before morning.")]
    [SerializeField] private float _fadeFromWhiteDuration = 0.6f;

    [Header("Camera")]
    [Tooltip("FOV for the intro perspective camera.")]
    [SerializeField] private float _introFOV = 50f;

    // ── Runtime ─────────────────────────────────────────────────────

    private CinemachineCamera _introCamera;
    private Canvas _overlayCanvas;
    private CanvasGroup _overlayCG;
    private TextMeshProUGUI _titleText;
    private TextMeshProUGUI _descText;

    private const string ParisDescription =
        "A cozy apartment sim where you date strangers, tend your space, " +
        "and slowly realize some of them never leave\n\n" +
        "F, 25, $2,000/mo. Seeking a lover to give me reasons to live, " +
        "or move to Paris with and co-parent a cat.";

    private const int IntroCamPriority = 40; // above read cam (30) and browse cam (20)

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        CleanupUI();
    }

    // ── Public API ──────────────────────────────────────────────────

    /// <summary>
    /// Play the full day intro sequence. Yields until complete.
    /// Call while the screen is already black/white (ScreenFade alpha = 1).
    /// </summary>
    public IEnumerator Play(int dayNumber)
    {
        if (_waypoints == null || _waypoints.Length < 2)
        {
            Debug.LogWarning("[DayIntroSequence] Not enough waypoints — skipping intro.");
            yield break;
        }

        BuildUI();

        // Switch brain to perspective while screen is still opaque white.
        // The browse camera is ortho — suppress it so the perspective intro cam wins.
        CameraTestController.Instance?.SuspendPreset();
        if (ApartmentManager.Instance != null)
            ApartmentManager.Instance.SetBrowseCameraActive(false);

        CreateIntroCamera();

        // Let one frame pass so Cinemachine picks up the new camera + perspective mode
        yield return null;

        // ── 1. Fade in from white — track shot begins ──
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeIn(0.5f);

        // ── 2. Forward track shot + DAY N title ──
        yield return StartCoroutine(TrackAndTitle(dayNumber));

        // ── 3. Turnaround + pullback ──
        yield return StartCoroutine(Pullback());

        // ── 4. Fade to white ──
        if (ScreenFade.Instance != null)
            yield return ScreenFade.Instance.FadeOut(_fadeToWhiteDuration);

        // ── 5. Show Paris description over white ──
        yield return StartCoroutine(ShowDescription());

        // ── 6. Cleanup — restore screen to opaque white for MorningTransition to fade in ──
        DestroyIntroCamera();
        CleanupUI();

        // Screen is now white/opaque — MorningTransition will FadeIn from here
    }

    // ── Track Shot ──────────────────────────────────────────────────

    // Arc-length table for constant-speed traversal
    private float[] _arcLengths;   // cumulative distance at each waypoint
    private float _totalArcLength;

    /// <summary>Build a cumulative distance table from the waypoint positions.</summary>
    private void BuildArcLengthTable()
    {
        int n = _waypoints.Length;
        _arcLengths = new float[n];
        _arcLengths[0] = 0f;
        for (int i = 1; i < n; i++)
            _arcLengths[i] = _arcLengths[i - 1] +
                Vector3.Distance(_waypoints[i - 1].position, _waypoints[i].position);
        _totalArcLength = _arcLengths[n - 1];
    }

    /// <summary>
    /// Given a distance along the path, return the segment index and local t.
    /// Provides constant-speed traversal regardless of waypoint spacing.
    /// </summary>
    private void DistanceToSegment(float dist, out int seg, out float segT)
    {
        dist = Mathf.Clamp(dist, 0f, _totalArcLength);
        for (int i = 1; i < _arcLengths.Length; i++)
        {
            if (dist <= _arcLengths[i])
            {
                seg = i - 1;
                float segLen = _arcLengths[i] - _arcLengths[i - 1];
                segT = segLen > 0.001f ? (dist - _arcLengths[i - 1]) / segLen : 0f;
                return;
            }
        }
        seg = _waypoints.Length - 2;
        segT = 1f;
    }

    /// <summary>
    /// Catmull-Rom spline position. Uses virtual duplicate endpoints for
    /// the first and last segments so the curve passes through all waypoints.
    /// </summary>
    private Vector3 CatmullRomPos(int seg, float t)
    {
        int n = _waypoints.Length;
        Vector3 p0 = _waypoints[Mathf.Max(seg - 1, 0)].position;
        Vector3 p1 = _waypoints[seg].position;
        Vector3 p2 = _waypoints[Mathf.Min(seg + 1, n - 1)].position;
        Vector3 p3 = _waypoints[Mathf.Min(seg + 2, n - 1)].position;

        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    /// <summary>
    /// Smooth rotation along the path using Catmull-Rom-style Slerp
    /// (3-point Squad approximation for inner waypoints).
    /// </summary>
    private Quaternion SmoothRotation(int seg, float t)
    {
        Quaternion a = _waypoints[seg].rotation;
        Quaternion b = _waypoints[Mathf.Min(seg + 1, _waypoints.Length - 1)].rotation;
        return Quaternion.Slerp(a, b, t);
    }

    private void SetCameraFromDistance(float dist)
    {
        if (_introCamera == null) return;
        DistanceToSegment(dist, out int seg, out float t);
        _introCamera.transform.position = CatmullRomPos(seg, t);
        _introCamera.transform.rotation = SmoothRotation(seg, t);
    }

    private IEnumerator TrackAndTitle(int dayNumber)
    {
        BuildArcLengthTable();
        float trackDuration = _totalArcLength / Mathf.Max(_trackSpeed, 0.01f);

        float titleAppearTime = trackDuration * 0.2f;
        float titleDisappearTime = trackDuration * 0.8f;
        bool titleShowing = false;

        _titleText.text = $"DAY {dayNumber}";
        _titleText.gameObject.SetActive(true);

        float elapsed = 0f;
        while (elapsed < trackDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float dist = Mathf.Clamp(elapsed * _trackSpeed, 0f, _totalArcLength);
            SetCameraFromDistance(dist);

            // Title fade in/out
            if (elapsed >= titleAppearTime && !titleShowing)
            {
                titleShowing = true;
                StartCoroutine(FadeCanvasGroup(_overlayCG, 0f, 1f, _titleFadeDuration));
            }
            if (elapsed >= titleDisappearTime && titleShowing)
            {
                titleShowing = false;
                StartCoroutine(FadeCanvasGroup(_overlayCG, 1f, 0f, _titleFadeDuration));
            }

            yield return null;
        }

        // Snap to final waypoint
        SetCameraFromDistance(_totalArcLength);
        _overlayCG.alpha = 0f;
        _titleText.gameObject.SetActive(false);
    }

    private IEnumerator Pullback()
    {
        if (_introCamera == null) yield break;

        Transform lastWP = _waypoints[_waypoints.Length - 1];
        Vector3 startPos = lastWP.position;
        Quaternion startRot = lastWP.rotation;

        // Turn 180 degrees and pull back
        Quaternion endRot = startRot * Quaternion.Euler(0f, 180f, 0f);
        Vector3 pullDir = endRot * Vector3.forward;
        Vector3 endPos = startPos - pullDir * _pullbackDistance;

        float elapsed = 0f;
        while (elapsed < _pullbackDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _pullbackDuration);
            float smooth = t * t * (3f - 2f * t);

            _introCamera.transform.position = Vector3.Lerp(startPos, endPos, smooth);
            _introCamera.transform.rotation = Quaternion.Slerp(startRot, endRot, smooth);

            yield return null;
        }
    }

    // ── Description ─────────────────────────────────────────────────

    private IEnumerator ShowDescription()
    {
        _descText.text = ParisDescription;
        _descText.gameObject.SetActive(true);
        _overlayCG.alpha = 0f;

        // Fade in
        yield return FadeCanvasGroup(_overlayCG, 0f, 1f, 0.5f);

        // Hold
        yield return new WaitForSecondsRealtime(_descriptionHoldDuration);

        // Fade out
        yield return FadeCanvasGroup(_overlayCG, 1f, 0f, 0.4f);

        _descText.gameObject.SetActive(false);
    }

    // ── Camera ──────────────────────────────────────────────────────

    private void CreateIntroCamera()
    {
        if (_introCamera != null) return;

        var go = new GameObject("Cam_DayIntro");
        go.transform.SetParent(transform, false);

        _introCamera = go.AddComponent<CinemachineCamera>();
        _introCamera.Priority = IntroCamPriority;

        var lens = _introCamera.Lens;
        lens.FieldOfView = _introFOV;
        lens.NearClipPlane = 0.1f;
        lens.FarClipPlane = 200f;
        lens.ModeOverride = Unity.Cinemachine.LensSettings.OverrideModes.Perspective;
        _introCamera.Lens = lens;

        // Start at first waypoint
        if (_waypoints != null && _waypoints.Length > 0)
        {
            go.transform.position = _waypoints[0].position;
            go.transform.rotation = _waypoints[0].rotation;
        }
    }

    private void DestroyIntroCamera()
    {
        if (_introCamera != null)
        {
            Destroy(_introCamera.gameObject);
            _introCamera = null;
        }
    }

    // ── UI ───────────────────────────────────────────────────────────

    private void BuildUI()
    {
        if (_overlayCanvas != null) return;

        var canvasGO = new GameObject("DayIntroOverlay");
        canvasGO.transform.SetParent(transform, false);

        _overlayCanvas = canvasGO.AddComponent<Canvas>();
        _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _overlayCanvas.sortingOrder = 110; // above ScreenFade (100) and phase text (105)

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _overlayCG = canvasGO.AddComponent<CanvasGroup>();
        _overlayCG.alpha = 0f;
        _overlayCG.blocksRaycasts = false;

        // DAY N title — big, centered, fills the screen
        var titleGO = new GameObject("DayTitle");
        titleGO.transform.SetParent(canvasGO.transform, false);

        var titleRT = titleGO.AddComponent<RectTransform>();
        titleRT.anchorMin = Vector2.zero;
        titleRT.anchorMax = Vector2.one;
        titleRT.offsetMin = Vector2.zero;
        titleRT.offsetMax = Vector2.zero;

        _titleText = titleGO.AddComponent<TextMeshProUGUI>();
        _titleText.fontSize = 180f;
        _titleText.alignment = TextAlignmentOptions.Center;
        _titleText.color = new Color(0.95f, 0.92f, 0.85f, 1f);
        _titleText.fontStyle = FontStyles.Bold;
        _titleText.enableWordWrapping = false;

        var theme = IrisTextTheme.Active;
        if (theme != null && theme.primaryFont != null)
            _titleText.font = theme.primaryFont;

        titleGO.SetActive(false);

        // Paris description — centered, smaller
        var descGO = new GameObject("ParisDesc");
        descGO.transform.SetParent(canvasGO.transform, false);

        var descRT = descGO.AddComponent<RectTransform>();
        descRT.anchorMin = new Vector2(0.1f, 0.15f);
        descRT.anchorMax = new Vector2(0.9f, 0.85f);
        descRT.offsetMin = Vector2.zero;
        descRT.offsetMax = Vector2.zero;

        _descText = descGO.AddComponent<TextMeshProUGUI>();
        _descText.fontSize = 36f;
        _descText.alignment = TextAlignmentOptions.Center;
        _descText.color = new Color(0.2f, 0.2f, 0.22f, 1f); // dark on white bg
        _descText.enableWordWrapping = true;
        _descText.lineSpacing = 10f;
        _descText.richText = true;

        if (theme != null && theme.primaryFont != null)
            _descText.font = theme.primaryFont;

        descGO.SetActive(false);
    }

    private void CleanupUI()
    {
        if (_overlayCanvas != null)
        {
            Destroy(_overlayCanvas.gameObject);
            _overlayCanvas = null;
            _overlayCG = null;
            _titleText = null;
            _descText = null;
        }
    }

    // ── Utility ─────────────────────────────────────────────────────

    private static IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to, float duration)
    {
        if (cg == null) yield break;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            cg.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        cg.alpha = to;
    }
}
