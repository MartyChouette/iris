using System.Collections;
using UnityEngine;

/// <summary>
/// Cinematic moment camera — smoothly frames important events:
/// book collection completing, item discoveries, date arrivals, phase transitions.
///
/// Call MomentCamera.FrameTarget() from anywhere to trigger a camera push
/// toward a world-space target. The camera holds for a beat, then returns
/// to normal apartment browsing.
///
/// Uses ApartmentManager.SetPresetBase/ClearPresetBase under the hood,
/// so it integrates cleanly with the existing camera system.
/// </summary>
public class MomentCamera : MonoBehaviour
{
    public static MomentCamera Instance { get; private set; }

    [Header("Framing")]
    [Tooltip("FOV when framing a moment (lower = tighter zoom). Set to 0 to keep current FOV.")]
    [SerializeField] private float _frameFOV = 0f;

    [Tooltip("Pause the game during the hold phase (time scale 0). Camera lerps use unscaled time.")]
    [SerializeField] private bool _pauseDuringHold = true;

    [Header("Timing")]
    [Tooltip("Seconds to glide the camera to the target.")]
    [SerializeField] private float _pushDuration = 1.0f;

    [Tooltip("Seconds the camera holds on the target before returning.")]
    [SerializeField] private float _holdDuration = 1.5f;

    [Tooltip("Seconds to glide the camera back to normal.")]
    [SerializeField] private float _returnDuration = 0.8f;

    [Header("Easing")]
    [SerializeField] private AnimationCurve _pushCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private AnimationCurve _returnCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private Coroutine _activeRoutine;
    private bool _isMomentActive;

    public bool IsMomentActive => _isMomentActive;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Smoothly push the camera to frame a world-space target, hold, then return.
    /// Safe to call while another moment is active — it interrupts gracefully.
    /// </summary>
    /// <param name="target">World position to look at.</param>
    /// <param name="holdOverride">Custom hold duration. -1 = use default.</param>
    public static void FrameTarget(Vector3 target, float holdOverride = -1f)
    {
        if (Instance == null) return;
        Instance.StartFrame(target, holdOverride);
    }

    /// <summary>
    /// Frame a target with a specific approach direction (e.g., from the left).
    /// </summary>
    public static void FrameTargetFrom(Vector3 target, Vector3 approachDir, float holdOverride = -1f)
    {
        if (Instance == null) return;
        Instance.StartFrameFrom(target, approachDir, holdOverride);
    }

    /// <summary>Cancel any active moment and return to normal camera immediately.</summary>
    public static void Cancel()
    {
        if (Instance == null) return;
        Instance.CancelMoment();
    }

    private void StartFrame(Vector3 target, float holdOverride)
    {
        if (_activeRoutine != null)
            StopCoroutine(_activeRoutine);

        float hold = holdOverride >= 0f ? holdOverride : _holdDuration;
        _activeRoutine = StartCoroutine(MomentRoutine(target, hold));
    }

    private void StartFrameFrom(Vector3 target, Vector3 approachDir, float holdOverride)
    {
        // approachDir ignored — camera keeps its current angle and slides over
        StartFrame(target, holdOverride);
    }

    private void CancelMoment()
    {
        if (_activeRoutine != null)
        {
            StopCoroutine(_activeRoutine);
            _activeRoutine = null;
        }

        if (_isMomentActive)
        {
            _isMomentActive = false;
            TimeScaleManager.Clear(s_momentPriority);
            ApartmentManager.Instance?.ClearPresetBase();
        }
    }

    private static readonly int s_momentPriority = 80; // below pause (100)

    private IEnumerator MomentRoutine(Vector3 target, float holdTime)
    {
        var am = ApartmentManager.Instance;
        if (am == null) yield break;

        var cam = Camera.main;
        if (cam == null) yield break;

        _isMomentActive = true;

        // Capture start values
        Vector3 startPos = cam.transform.position;
        Quaternion startRot = cam.transform.rotation;
        float startFOV = cam.fieldOfView;

        // Keep the same rotation — just slide the camera so the target is centered.
        Vector3 camForward = cam.transform.forward;
        float depth = Vector3.Dot(target - startPos, camForward);
        Vector3 framePos = target - camForward * depth;
        Quaternion frameRot = startRot;
        float frameFOV = _frameFOV > 0f ? _frameFOV : startFOV;

        // ── Push: smooth glide to frame position (unscaled so it works while paused) ──
        float elapsed = 0f;
        while (elapsed < _pushDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = _pushCurve.Evaluate(Mathf.Clamp01(elapsed / _pushDuration));

            Vector3 pos = Vector3.Lerp(startPos, framePos, t);
            float fov = Mathf.Lerp(startFOV, frameFOV, t);

            am.SetPresetBase(pos, frameRot, fov);
            yield return null;
        }

        am.SetPresetBase(framePos, frameRot, frameFOV);

        // ── Hold: freeze the game so the player can admire the item ──
        if (_pauseDuringHold)
            TimeScaleManager.Set(s_momentPriority, 0f);

        yield return new WaitForSecondsRealtime(holdTime);

        if (_pauseDuringHold)
            TimeScaleManager.Clear(s_momentPriority);

        // ── Return: get the CURRENT correct camera target from ApartmentManager ──
        am.ClearPresetBase();
        yield return null;

        Vector3 returnPos = cam.transform.position;
        float returnFOV = cam.fieldOfView;

        am.SetPresetBase(framePos, frameRot, frameFOV);

        elapsed = 0f;
        while (elapsed < _returnDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = _returnCurve.Evaluate(Mathf.Clamp01(elapsed / _returnDuration));

            Vector3 pos = Vector3.Lerp(framePos, returnPos, t);
            float fov = Mathf.Lerp(frameFOV, returnFOV, t);

            am.SetPresetBase(pos, frameRot, fov);
            yield return null;
        }

        // Release to normal browsing
        am.ClearPresetBase();
        _isMomentActive = false;
        _activeRoutine = null;
    }
}
