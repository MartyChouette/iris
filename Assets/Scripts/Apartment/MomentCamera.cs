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
    [Tooltip("How far the camera sits from the target (world units).")]
    [SerializeField] private float _frameDistance = 1.2f;

    [Tooltip("Height offset above the target for the camera.")]
    [SerializeField] private float _frameHeight = 0.4f;

    [Tooltip("FOV when framing a moment (lower = tighter zoom).")]
    [SerializeField] private float _frameFOV = 40f;

    [Header("Timing")]
    [Tooltip("Seconds to glide the camera to the target.")]
    [SerializeField] private float _pushDuration = 0.6f;

    [Tooltip("Seconds the camera holds on the target before returning.")]
    [SerializeField] private float _holdDuration = 1.5f;

    [Tooltip("Seconds to glide the camera back to normal.")]
    [SerializeField] private float _returnDuration = 0.4f;

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
        // Default approach: from the main camera's current forward direction
        var cam = Camera.main;
        Vector3 approachDir = cam != null ? -cam.transform.forward : Vector3.back;
        StartFrameFrom(target, approachDir, holdOverride);
    }

    private void StartFrameFrom(Vector3 target, Vector3 approachDir, float holdOverride)
    {
        if (_activeRoutine != null)
            StopCoroutine(_activeRoutine);

        float hold = holdOverride >= 0f ? holdOverride : _holdDuration;
        _activeRoutine = StartCoroutine(MomentRoutine(target, approachDir.normalized, hold));
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
            ApartmentManager.Instance?.ClearPresetBase();
        }
    }

    private IEnumerator MomentRoutine(Vector3 target, Vector3 approachDir, float holdTime)
    {
        var am = ApartmentManager.Instance;
        if (am == null) yield break;

        var cam = Camera.main;
        if (cam == null) yield break;

        _isMomentActive = true;

        // Calculate frame position: offset from target along approach direction
        Vector3 framePos = target + approachDir * _frameDistance + Vector3.up * _frameHeight;
        Quaternion frameRot = Quaternion.LookRotation(target - framePos, Vector3.up);

        // Capture start values
        Vector3 startPos = cam.transform.position;
        Quaternion startRot = cam.transform.rotation;
        float startFOV = cam.fieldOfView;

        // ── Push: glide to frame position ──
        float elapsed = 0f;
        while (elapsed < _pushDuration)
        {
            elapsed += Time.deltaTime;
            float t = _pushCurve.Evaluate(Mathf.Clamp01(elapsed / _pushDuration));

            Vector3 pos = Vector3.Lerp(startPos, framePos, t);
            Quaternion rot = Quaternion.Slerp(startRot, frameRot, t);
            float fov = Mathf.Lerp(startFOV, _frameFOV, t);

            am.SetPresetBase(pos, rot, fov);
            yield return null;
        }

        am.SetPresetBase(framePos, frameRot, _frameFOV);

        // ── Hold ──
        yield return new WaitForSeconds(holdTime);

        // ── Return: get the CURRENT correct camera target from ApartmentManager ──
        // (not the stale startPos — the player may have changed areas during the hold)
        am.ClearPresetBase(); // briefly clear so we can read where the camera SHOULD be
        yield return null;    // one frame for ApartmentManager to update position

        Vector3 returnPos = cam.transform.position;
        Quaternion returnRot = cam.transform.rotation;
        float returnFOV = cam.fieldOfView;

        // Re-override so we can lerp smoothly from the moment frame to the correct position
        am.SetPresetBase(framePos, frameRot, _frameFOV);

        elapsed = 0f;
        while (elapsed < _returnDuration)
        {
            elapsed += Time.deltaTime;
            float t = _returnCurve.Evaluate(Mathf.Clamp01(elapsed / _returnDuration));

            Vector3 pos = Vector3.Lerp(framePos, returnPos, t);
            Quaternion rot = Quaternion.Slerp(frameRot, returnRot, t);
            float fov = Mathf.Lerp(_frameFOV, returnFOV, t);

            am.SetPresetBase(pos, rot, fov);
            yield return null;
        }

        // Release to normal browsing
        am.ClearPresetBase();
        _isMomentActive = false;
        _activeRoutine = null;
    }
}
